using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 主驱动（Stage 2.4）：把已移植的各层零件串成完整 .laya.vfx。
    /// C# 移植自 unity-vfx-to-laya.js 的模块顶层顺序执行流程。
    ///
    /// 本文件（Driver）含：全局状态 + 分类/ID 分配 + operator 主循环 + builtin/LogicalNot/getProperty 展开。
    /// context 主循环 / link pass / properties / post-fix / 输出组装 见 VfxConverter.Driver2.cs（后续）。
    /// </summary>
    public partial class VfxConverter
    {
        // ── 驱动全局状态 ──
        public LayaDefs Defs;
        public Dictionary<string, Jval> AssetMapping = new Dictionary<string, Jval>();
        public Dictionary<string, string> MainTextureRepeatVariant = new Dictionary<string, string>();
        public Func<string, string, string> FindLayaExportedLm;   // (fbxBase, subMeshHint) → lm uuid（可注入；默认用 FindLmDefault）
        public readonly Dictionary<string, string> LayaExportedLmByName = new Dictionary<string, string>();  // baseName → lm uuid（取最新 mtime）
        public readonly Dictionary<string, string> LmUuidToName = new Dictionary<string, string>();           // lm uuid → baseName（所有导入）
        public readonly HashSet<string> LmExportExcludeFbx = new HashSet<string> { "AlienStatue" };

        // findLayaExportedLm 内置实现（对应 JS L126-139）
        public string FindLmDefault(string fbxName, string subMeshHint)
        {
            if (string.IsNullOrEmpty(fbxName)) return null;
            string v;
            if (!string.IsNullOrEmpty(subMeshHint) && LayaExportedLmByName.TryGetValue(fbxName + "-" + subMeshHint, out v)) return v;
            string only = null; int cnt = 0;
            foreach (var k in LayaExportedLmByName.Keys) if (k.StartsWith(fbxName + "-")) { cnt++; only = k; }
            if (cnt == 1) return LayaExportedLmByName[only];
            return null;
        }
        public readonly HashSet<string> UnmappedGuids = new HashSet<string>();
        public readonly Dictionary<string, string> ResolvedMeshNames = new Dictionary<string, string>();
        public readonly Dictionary<string, string> ResourceInlineHolders = new Dictionary<string, string>();

        // ── subgraph 加载/内联（对应 JS loadSubgraph L253 + inlineSubgraphOperators L1113 + resolveSlotAliases L1250） ──
        public Dictionary<string, string> GuidToClass;                 // .cs.meta guid → 类名（子图 yaml 解析用）
        public Dictionary<string, string> SubgraphPathByGuid = new Dictionary<string, string>();   // .vfxblock/.vfxoperator guid → 文件绝对路径
        public class SubgraphData { public List<VfxEntry> Entries; public Dictionary<string, VfxEntry> ById; public string FilePath; }
        private readonly Dictionary<string, SubgraphData> _subgraphCache = new Dictionary<string, SubgraphData>();
        public readonly Dictionary<string, List<string>> SlotAliasOnLookup = new Dictionary<string, List<string>>();  // slotID → [resolvedSlotIDs]
        private int _subInstanceCounter = 0;

        public SubgraphData LoadSubgraph(string guid)
        {
            SubgraphData cached;
            if (_subgraphCache.TryGetValue(guid, out cached)) return cached;
            string filePath;
            if (!SubgraphPathByGuid.TryGetValue(guid, out filePath) || !System.IO.File.Exists(filePath))
            {
                Warn("[subgraph] file not found for guid " + guid);
                _subgraphCache[guid] = null;
                return null;
            }
            string yaml = System.IO.File.ReadAllText(filePath);
            var subEntries = UnityYamlParser.ParseEntries(yaml, GuidToClass);
            var subByID = new Dictionary<string, VfxEntry>();
            foreach (var e in subEntries) subByID[e.FileID] = e;
            var result = new SubgraphData { Entries = subEntries, ById = subByID, FilePath = filePath };
            _subgraphCache[guid] = result;
            return result;
        }

        /// <summary>
        /// 内联 VFXSubgraphOperator（对应 JS L1113-1246）。必须在 BuildClassificationAndIds 之前执行，
        /// 让克隆的 sub op 一起进入分类，保证 ID 分配顺序与 JS 一致。
        /// </summary>
        public void InlineSubgraphOperators()
        {
            var callers = new List<VfxEntry>();
            foreach (var e in Entries) if (e.ClassType == "VFXSubgraphOperator") callers.Add(e);
            foreach (var caller in callers)
            {
                var subRefM = Regex.Match(caller.Body, @"m_Subgraph:\s*\{fileID:\s*-?\d+,\s*guid:\s*([a-f0-9]{32})");
                if (!subRefM.Success) { Warn("[skip-subop] caller " + caller.FileID + " 缺 m_Subgraph guid"); continue; }
                var sub = LoadSubgraph(subRefM.Groups[1].Value);
                if (sub == null) continue;
                // 纯数字前缀避免破坏 LinkedSlots/m_Children 等 regex（只匹配 \d+）；
                // "9"+三位实例号 前缀保证与主图 ID（上界 9e18）不冲突
                int myInstance = ++_subInstanceCounter;
                string INSTANCE_TAG = "9" + myInstance.ToString().PadLeft(3, '0');
                Func<string, string> makePrefixed = (subIDStr) => INSTANCE_TAG + subIDStr;
                var subShortIDs = new HashSet<string>();
                foreach (var e in sub.Entries) subShortIDs.Add(e.FileID);
                Func<string, string> rewriteRefs = (body) => Regex.Replace(body, @"fileID:\s*(-?\d+)", (m) =>
                {
                    string id = m.Groups[1].Value;
                    return subShortIDs.Contains(id) ? "fileID: " + makePrefixed(id) : m.Value;
                });
                // caller input/output slot 的 (name → fileID)
                var subCallerInputSlots = UnityYamlParser.GetRefArrayField(caller.Body, "m_InputSlots");
                var subCallerOutputSlots = UnityYamlParser.GetRefArrayField(caller.Body, "m_OutputSlots");
                var callerInputByName = new Dictionary<string, string>();
                foreach (var sid in subCallerInputSlots)
                {
                    var e2 = Get(sid); if (e2 == null) continue;
                    var name = UnityYamlParser.GetSlotPropertyName(e2);
                    if (name != null) callerInputByName[name] = sid;
                }
                var callerOutputByName = new Dictionary<string, string>();
                foreach (var sid in subCallerOutputSlots)
                {
                    var e2 = Get(sid); if (e2 == null) continue;
                    var name = UnityYamlParser.GetSlotPropertyName(e2);
                    if (name != null) callerOutputByName[name] = sid;
                }
                // 克隆 + 注入（跳过 sub 的 VFXGraph / VFXUI）
                foreach (var e in sub.Entries)
                {
                    if (e.ClassType == "VFXGraph" || e.ClassType == "VFXUI") continue;
                    string newID = makePrefixed(e.FileID);
                    var cloned = new VfxEntry { FileID = newID, TypeNum = e.TypeNum, ClassType = e.ClassType, Body = rewriteRefs(e.Body), InlinedFromSub = true };
                    Entries.Add(cloned);
                    ById[newID] = cloned;
                }
                // VFXParameter 别名
                foreach (var e in sub.Entries)
                {
                    if (e.ClassType != "VFXParameter") continue;
                    var em = Regex.Match(e.Body, @"m_ExposedName:\s*(.+)");
                    string exposedName = em.Success ? em.Groups[1].Value.Trim() : null;
                    if (string.IsNullOrEmpty(exposedName)) continue;
                    var paramOuts = UnityYamlParser.GetRefArrayField(e.Body, "m_OutputSlots");
                    var paramIns = UnityYamlParser.GetRefArrayField(e.Body, "m_InputSlots");
                    // 输入参数：caller.input(全树) → sub 内部消费者(带前缀)
                    if (paramOuts.Count > 0)
                    {
                        string callerInSlot;
                        if (callerInputByName.TryGetValue(exposedName, out callerInSlot))
                        {
                            var allConsumers = new List<string>();
                            Action<string> collectAll = null;
                            collectAll = (slotID) =>
                            {
                                VfxEntry sE; if (!sub.ById.TryGetValue(slotID, out sE) || sE == null) return;
                                foreach (var lid in UnityYamlParser.GetLinkedSlots(sE)) allConsumers.Add(makePrefixed(lid));
                                foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) collectAll(cid);
                            };
                            foreach (var sid in paramOuts) collectAll(sid);
                            if (allConsumers.Count > 0)
                            {
                                Action<string> registerAliasOnCaller = null;
                                registerAliasOnCaller = (slotID) =>
                                {
                                    SlotAliasOnLookup[slotID] = allConsumers;
                                    var cE = Get(slotID);
                                    if (cE != null) foreach (var cid in UnityYamlParser.GetRefArrayField(cE.Body, "m_Children")) registerAliasOnCaller(cid);
                                };
                                registerAliasOnCaller(callerInSlot);
                            }
                        }
                    }
                    // 输出参数：sub parameter.inputSlot(带前缀, 全树) → caller.output 的外部消费者
                    if (paramIns.Count > 0)
                    {
                        string callerOutSlot;
                        if (callerOutputByName.TryGetValue(exposedName, out callerOutSlot))
                        {
                            var callerOutE = Get(callerOutSlot);
                            if (callerOutE != null)
                            {
                                var externalConsumers = new List<string>();
                                Action<string> collectFromCallerOut = null;
                                collectFromCallerOut = (slotID) =>
                                {
                                    var sE = Get(slotID); if (sE == null) return;
                                    foreach (var lid in UnityYamlParser.GetLinkedSlots(sE)) externalConsumers.Add(lid);
                                    foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) collectFromCallerOut(cid);
                                };
                                collectFromCallerOut(callerOutSlot);
                                if (externalConsumers.Count > 0)
                                {
                                    Action<string> registerAlias = null;
                                    registerAlias = (slotID) =>
                                    {
                                        SlotAliasOnLookup[makePrefixed(slotID)] = externalConsumers;
                                        VfxEntry sE; if (sub.ById.TryGetValue(slotID, out sE) && sE != null)
                                            foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) registerAlias(cid);
                                    };
                                    foreach (var sid in paramIns) registerAlias(sid);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>链接 pass 用：递归解析 slot 别名链（对应 JS L1250-1261）。</summary>
        public List<string> ResolveSlotAliases(string slotID, HashSet<string> visited = null)
        {
            if (visited == null) visited = new HashSet<string>();
            if (visited.Contains(slotID)) return new List<string>();
            visited.Add(slotID);
            List<string> aliased;
            if (SlotAliasOnLookup.TryGetValue(slotID, out aliased))
            {
                var outList = new List<string>();
                foreach (var a in aliased) outList.AddRange(ResolveSlotAliases(a, visited));
                return outList;
            }
            return new List<string> { slotID };
        }

        private int _nextLayaId = 1;
        public int NewId() { return _nextLayaId++; }

        public List<VfxEntry> Contexts = new List<VfxEntry>();
        public List<VfxEntry> OperatorEntries = new List<VfxEntry>();
        public readonly Dictionary<string, int> FileIDToLayaId = new Dictionary<string, int>();
        public readonly Dictionary<string, int> BlockFileIDToLayaId = new Dictionary<string, int>();

        public readonly List<Jval> LayaOperators = new List<Jval>();
        public readonly List<Jval> LayaContexts = new List<Jval>();

        public class SlotRef {
            public int LayaId; public int SlotIdx; public string Dir; public string OpTypeId; public string RealSlotId; public bool IsResource;
            // block 目标（recordBlockSlotTree 写入；BlockId!=0 表示这是某 block 的 input slot）
            public int BlockId; public int CtxId; public string SlotPropName;
        }
        public readonly Dictionary<string, SlotRef> SlotToOpId = new Dictionary<string, SlotRef>();

        public class BuiltinSlotOp { public string SlotID; public int LayaOpId; public string LayaType; }
        public readonly List<BuiltinSlotOp> BuiltinSlotToOp = new List<BuiltinSlotOp>();
        public class LogicalNotEntry { public string InSlotID; public string OutSlotID; public int SubtractOpId; }
        public readonly List<LogicalNotEntry> LogicalNotInfo = new List<LogicalNotEntry>();
        public class ParamToOpEntry { public int LayaOpId; public List<string> OutputSlots; }
        public readonly List<ParamToOpEntry> ParameterToOp = new List<ParamToOpEntry>();

        // ── 资源解析（对应 JS resolveResourceRef / swapMainRepeatVariant） ──
        public string SwapMainRepeatVariant(string uuidOrRes)
        {
            if (uuidOrRes == null) return uuidOrRes;
            string bare = uuidOrRes.StartsWith("res://") ? uuidOrRes.Substring(6) : uuidOrRes;
            string v;
            if (!MainTextureRepeatVariant.TryGetValue(bare, out v)) return uuidOrRes;
            return uuidOrRes.StartsWith("res://") ? "res://" + v : v;
        }

        /// <summary>
        /// 解析 slot inline 的资源引用 {obj:{guid,...}} 为 res://uuid。
        /// 返回 null 统一表示"无资源"（对应 JS 的 null/undefined 两种情形）。
        /// </summary>
        public string ResolveResourceRef(Jval value)
        {
            if (value == null || !value.IsObject) return null;                 // 不是资源引用（JS undefined）
            var obj = value.Get("obj");
            if (obj == null || !obj.IsObject) return null;
            string guid = obj.StrOf("guid");
            if (string.IsNullOrEmpty(guid) || IsAllZeros(guid)) return null;   // null reference
            Jval m;
            if (AssetMapping.TryGetValue(guid, out m) && !string.IsNullOrEmpty(m.StrOf("layaUuid")))
            {
                string layaUuid = m.StrOf("layaUuid");
                string type = m.StrOf("type");
                string unityName = m.StrOf("unityName");
                if (type == "Mesh" && unityName != null && unityName.ToLowerInvariant().EndsWith(".fbx"))
                {
                    string fbxBase = unityName.Substring(0, unityName.Length - 4);
                    if (!LmExportExcludeFbx.Contains(fbxBase))
                    {
                        string lmUuid = FindLayaExportedLm != null ? FindLayaExportedLm(fbxBase, m.StrOf("subMeshHint")) : FindLmDefault(fbxBase, m.StrOf("subMeshHint"));
                        if (lmUuid != null) { ResolvedMeshNames[lmUuid] = fbxBase; return "res://" + lmUuid; }
                    }
                }
                if (type == "Mesh" && unityName != null)
                    ResolvedMeshNames[layaUuid] = Regex.Replace(unityName, @"\.(fbx|lm)$", "", RegexOptions.IgnoreCase);
                return "res://" + layaUuid;
            }
            if (m != null && m.StrOf("layaUuid") == "") return null;
            UnmappedGuids.Add(guid);
            if (guid.Length == 32)
                return "res://" + guid.Substring(0, 8) + "-" + guid.Substring(8, 4) + "-" + guid.Substring(12, 4) + "-" + guid.Substring(16, 4) + "-" + guid.Substring(20, 12);
            return null;
        }
        private static bool IsAllZeros(string s) { foreach (char c in s) if (c != '0') return false; return true; }

        /// <summary>分类 context/operator/block 并按 JS 顺序分配 Laya id（对应 JS L1264-1305）。</summary>
        public void BuildClassificationAndIds()
        {
            Contexts.Clear();
            foreach (var e in Entries) if (VfxMaps.CONTEXT_CLASSES.Contains(e.ClassType)) Contexts.Add(e);

            // pass 1：context → laya id
            foreach (var ctx in Contexts) FileIDToLayaId[ctx.FileID] = NewId();

            // 收集 top-level operators
            var blockFileIDSet = new HashSet<string>();
            foreach (var ctx in Contexts)
                foreach (var c in UnityYamlParser.GetRefArrayField(ctx.Body, "m_Children")) blockFileIDSet.Add(c);

            OperatorEntries.Clear();
            foreach (var e in Entries)
            {
                if (!VfxMaps.IsOperatorNode(e.ClassType)) continue;
                if (blockFileIDSet.Contains(e.FileID)) continue;
                string parent = UnityYamlParser.GetRefField(e.Body, "m_Parent");
                var parentEntry = (parent != null && parent != "0") ? Get(parent) : null;
                bool isInContext = parentEntry != null && VfxMaps.CONTEXT_CLASSES.Contains(parentEntry.ClassType);
                if (!isInContext) OperatorEntries.Add(e);
            }

            // pass 2：operator → laya id
            foreach (var op in OperatorEntries) FileIDToLayaId[op.FileID] = NewId();

            // block → laya id
            foreach (var ctx in Contexts)
                foreach (var cid in UnityYamlParser.GetRefArrayField(ctx.Body, "m_Children")) BlockFileIDToLayaId[cid] = NewId();
        }

        public int? GetLayaIdForFileID(string fid)
        {
            int v;
            if (fid != null && FileIDToLayaId.TryGetValue(fid, out v)) return v;
            if (fid != null && BlockFileIDToLayaId.TryGetValue(fid, out v)) return v;
            return null;
        }

        // ── operator 主循环（对应 JS L2187-2543） ──
        private static readonly HashSet<string> INLINE_SCALAR_TYPES = new HashSet<string>
        {
            "inlineFloat","inlineInt","inlineUint","inlineBool","inlineVector2","inlineVector3","inlineVector4","inlineMatrix4x4"
        };
        private static readonly HashSet<string> POLYMORPHIC_OPS = new HashSet<string>
        {
            "add","subtract","multiply","divide","modulo","negate","absolute","minimum","maximum","saturate","smoothstep",
            "clamp","lerp","power","squareRoot","oneMinus","fractional","floor","ceiling","round","sine","cosine","tangent",
            "remap","remapRange","linearRemap","normalize","length","squaredLength","distance","squaredDistance","dotProduct",
            "appendVector","swizzle","compare","switchOp","step","sign","reciprocal","sineWave","inverseLerp","randomNumber",
        };
        private static readonly Dictionary<string, string> COMPOSITE_SUBSLOT_ALIAS = new Dictionary<string, string> { { "angles", "rotation" } };

        /// <summary>把每个 top-level operator 转换为 Laya operator 节点（对应 JS L2187-2543）。</summary>
        public void ConvertOperators()
        {
            foreach (var op in OperatorEntries)
            {
                int layaId = FileIDToLayaId[op.FileID];
                string layaType;
                VfxMaps.OP_MAP.TryGetValue(op.ClassType, out layaType);   // 可能不含 key → null
                bool hasKey = VfxMaps.OP_MAP.ContainsKey(op.ClassType);
                if (!hasKey || layaType == null)
                {
                    if (op.ClassType == "VFXInlineOperator")
                    {
                        var tm = Regex.Match(op.Body, @"m_Type:\s*\n\s*m_SerializableType:\s*([^,]+)");
                        string t = tm.Success ? tm.Groups[1].Value.Trim() : "";
                        if (Regex.IsMatch(t, @"UnityEngine\.(Mesh|Texture2D|Texture3D|TextureCube|Cubemap)", RegexOptions.IgnoreCase))
                        {
                            var inSlots = UnityYamlParser.GetRefArrayField(op.Body, "m_InputSlots");
                            var slot0 = inSlots.Count > 0 && inSlots[0] != null ? Get(inSlots[0]) : null;
                            var v = slot0 != null ? UnityYamlParser.GetSlotInlineValue(slot0) : null;
                            var resolved = ResolveResourceRef(v);
                            if (resolved != null) ResourceInlineHolders[op.FileID] = resolved;
                            continue;
                        }
                        if (Regex.IsMatch(t, @"UnityEngine\.(AnimationCurve|Gradient)", RegexOptions.IgnoreCase)) continue;
                        if (t.Contains("Single")) layaType = "inlineFloat";
                        else if (t.Contains("Int32")) layaType = "inlineInt";
                        else if (t.Contains("UInt32")) layaType = "inlineUint";
                        else if (t.Contains("Boolean")) layaType = "inlineBool";
                        else if (t.Contains("Vector2")) layaType = "inlineVector2";
                        else if (t.Contains("Vector3")) layaType = "inlineVector3";
                        else if (t.Contains("Vector4")) layaType = "inlineVector4";
                        else if (t.Contains("Color")) layaType = "inlineColor";
                        else if (t.Contains("Matrix4x4")) layaType = "inlineMatrix4x4";
                        else layaType = "inlineFloat";
                    }
                    else continue;   // skip-op（SILENT/TODO 都跳过，无功能影响）
                }

                var uiPos = UnityYamlParser.GetUIPos(op.Body);
                var layaOp = Jval.Obj()
                    .Set("id", layaId).Set("typeId", layaType).Set("uiData", uiPos)
                    .Set("output", Jval.Obj()).Set("props", Jval.Obj());

                var inputSlots = UnityYamlParser.GetRefArrayField(op.Body, "m_InputSlots");
                var outputSlots = UnityYamlParser.GetRefArrayField(op.Body, "m_OutputSlots");

                // 记录 slot tree
                var inIdeIds = Defs.GetInputs(layaType) ?? new string[0];
                Dictionary<string, string> opAlias;
                VfxMaps.OP_PROPNAME_ALIAS.TryGetValue(layaType, out opAlias);
                for (int i = 0; i < inputSlots.Count; i++)
                {
                    var slotEntry = Get(inputSlots[i]);
                    string propName = slotEntry != null ? UnityYamlParser.GetSlotPropertyName(slotEntry) : null;
                    string slotType = slotEntry != null ? (UnityYamlParser.GetSlotType(slotEntry) ?? "") : "";
                    bool isResourceSlot = Regex.IsMatch(slotType, "Mesh|Texture|Cubemap", RegexOptions.IgnoreCase);
                    if (opAlias != null && propName != null)
                    {
                        string ali;
                        if (opAlias.TryGetValue(propName, out ali)) propName = ali;
                        else { string lc = char.ToLowerInvariant(propName[0]) + propName.Substring(1); if (opAlias.TryGetValue(lc, out ali)) propName = ali; }
                    }
                    string realSlotId = null;
                    if (propName != null)
                    {
                        string lcName = propName.ToLowerInvariant();
                        foreach (var id in inIdeIds) if (id == propName || id.ToLowerInvariant() == lcName) { realSlotId = id; break; }
                    }
                    if (realSlotId == null) realSlotId = i < inIdeIds.Length ? inIdeIds[i] : null;
                    RecordSlotTree(inputSlots[i], i, "in", realSlotId, isResourceSlot, null, layaId, layaType);
                }
                var outIdeIds = Defs.GetOutputs(layaType) ?? new string[0];
                for (int i = 0; i < outputSlots.Count; i++)
                {
                    var slotEntry = Get(outputSlots[i]);
                    string propName = slotEntry != null ? UnityYamlParser.GetSlotPropertyName(slotEntry) : null;
                    string realSlotId = null;
                    if (propName != null)
                    {
                        string lcName = propName.ToLowerInvariant();
                        foreach (var id in outIdeIds) if (id == propName || id.ToLowerInvariant() == lcName) { realSlotId = id; break; }
                    }
                    if (realSlotId == null) realSlotId = i < outIdeIds.Length ? outIdeIds[i] : null;
                    RecordSlotTree(outputSlots[i], i, "out", realSlotId, false, null, layaId, layaType);
                }

                var props = layaOp.Get("props");
                if (INLINE_SCALAR_TYPES.Contains(layaType))
                {
                    if (op.ClassType == "Pi") props.Set("value", Math.PI);
                    else
                    {
                        string src = (inputSlots.Count > 0 && inputSlots[0] != null) ? inputSlots[0] : (outputSlots.Count > 0 ? outputSlots[0] : null);
                        var v = UnityYamlParser.GetSlotInlineValue(Get(src));
                        if (v != null && !v.IsNull)
                        {
                            if (v.IsNumber) props.Set("value", v.Num);
                            else if (v.IsObject)
                            {
                                if (v.Get("x") != null && v.Get("x").IsNumber) props.Set("x", v.NumOf("x"));
                                if (v.Get("y") != null && v.Get("y").IsNumber) props.Set("y", v.NumOf("y"));
                                if (v.Get("z") != null && v.Get("z").IsNumber) props.Set("z", v.NumOf("z"));
                                if (v.Get("w") != null && v.Get("w").IsNumber) props.Set("w", v.NumOf("w"));
                            }
                        }
                    }
                }
                else if (layaType == "inlineColor")
                {
                    var v = UnityYamlParser.GetSlotInlineValue(Get(inputSlots.Count > 0 ? inputSlots[0] : null));
                    if (v != null && v.IsObject)
                    {
                        props.Set("r", Nz1(v, "r", 1)).Set("g", Nz1(v, "g", 1)).Set("b", Nz1(v, "b", 1)).Set("a", Nz1(v, "a", 1));
                    }
                }
                else if (layaType == "getAttribute")
                {
                    string attr = UnityYamlParser.GetStringField(op.Body, "attribute") ?? "position";
                    int locInt = UnityYamlParser.GetIntField(op.Body, "location") ?? 0;
                    props.Set("attribute", VfxMaps.NormalizeAttrName(attr));
                    props.Set("location", locInt == 1 ? "Source" : "Current");
                }
                else
                {
                    var _inputs = Jval.Obj();
                    foreach (var slotID in inputSlots)
                    {
                        var slotEntry = Get(slotID);
                        if (slotEntry == null) continue;
                        string propName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                        if (propName == null) continue;
                        string slotType = UnityYamlParser.GetSlotType(slotEntry) ?? "";
                        bool isResourceSlot = Regex.IsMatch(slotType, "Mesh|Texture|Cubemap", RegexOptions.IgnoreCase);
                        bool isCurveOrGradientSlot = Regex.IsMatch(slotType, "AnimationCurve|Gradient", RegexOptions.IgnoreCase);
                        string slotKey = (char.ToLowerInvariant(propName[0]) + propName.Substring(1));
                        slotKey = Regex.Replace(slotKey, @"\s+", "");
                        Dictionary<string, string> opAlias2;
                        if (VfxMaps.OP_PROPNAME_ALIAS.TryGetValue(layaType, out opAlias2) && opAlias2 != null)
                        {
                            string ali;
                            if (opAlias2.TryGetValue(propName, out ali)) slotKey = ali;
                            else if (opAlias2.TryGetValue(slotKey, out ali)) slotKey = ali;
                        }
                        var linked = UnityYamlParser.GetLinkedSlots(slotEntry);
                        if (linked.Count > 0 && !isResourceSlot && !isCurveOrGradientSlot)
                        {
                            var folded = EvalConstOperatorFromInputSlot(slotID);
                            if (folded != null) _inputs.Set(slotKey, folded.Value);
                            continue;
                        }
                        if (isResourceSlot)
                        {
                            var rv = UnityYamlParser.GetSlotInlineValue(slotEntry);
                            var resolved = ResolveResourceRef(rv);
                            if (resolved != null) props.Set(slotKey, resolved);
                            continue;
                        }
                        if (Regex.IsMatch(slotType, "AnimationCurve", RegexOptions.IgnoreCase))
                        {
                            if (layaType == "sampleCurve" && slotKey == "curve")
                            {
                                Jval curveJson = null;
                                if (linked.Count > 0) curveJson = VfxCurveGradient.ResolveUpstreamInlineCurveGradient(slotEntry, Get);
                                if (curveJson == null) curveJson = UnityYamlParser.GetSlotInlineValue(slotEntry);
                                if (curveJson != null && curveJson.IsObject && curveJson.Get("frames") != null && curveJson.Get("frames").IsArray)
                                    props.Set("curve", VfxCurveGradient.ConvertUnityCurveToLaya(curveJson));
                            }
                            continue;
                        }
                        if (Regex.IsMatch(slotType, "Gradient", RegexOptions.IgnoreCase))
                        {
                            if (layaType == "sampleGradient" && slotKey == "gradient")
                            {
                                Jval gradientJson = null;
                                if (linked.Count > 0) gradientJson = VfxCurveGradient.ResolveUpstreamInlineCurveGradient(slotEntry, Get);
                                if (gradientJson == null) gradientJson = UnityYamlParser.GetSlotInlineValue(slotEntry);
                                if (gradientJson != null && gradientJson.IsObject && gradientJson.Get("colorKeys") != null && gradientJson.Get("colorKeys").IsArray)
                                    props.Set("gradient", Jval.Obj().Set("stops", VfxCurveGradient.UnityGradientToLayaStops(gradientJson)));
                            }
                            continue;
                        }
                        var val = UnityYamlParser.GetSlotInlineValue(slotEntry);
                        if (val == null || val.IsNull) continue;
                        if (val.IsObject && (val.Get("frames") != null || val.Get("colorKeys") != null)) continue;
                        if (val.IsObject)
                        {
                            var keys = new List<string>(val.Keys);
                            if (keys.Count == 1 && Regex.IsMatch(keys[0], "^(direction|position|vector)$", RegexOptions.IgnoreCase))
                            {
                                var inner = val.Get(keys[0]);
                                if (inner != null && inner.IsObject && (inner.Get("x") != null || inner.Get("y") != null || inner.Get("z") != null))
                                    val = inner;
                            }
                        }
                        _inputs.Set(slotKey, val);
                    }
                    if (_inputs.Count > 0) props.Set("_inputs", _inputs);
                    if (layaType == "randomNumber")
                    {
                        int seedMode = UnityYamlParser.GetIntField(op.Body, "seed") ?? 0;
                        string[] SEED_MAP = { "PerParticle", "PerComponent", "PerParticleStrip" };
                        props.Set("seed", (seedMode >= 0 && seedMode < 3) ? SEED_MAP[seedMode] : "PerParticle");
                        props.Set("constant", (UnityYamlParser.GetIntField(op.Body, "constant") ?? 0) == 1);
                    }
                }

                // op-specific yaml 字段
                if (layaType == "noise" || layaType == "curlNoise")
                {
                    int? ntInt = UnityYamlParser.GetIntField(op.Body, "type");
                    if (ntInt != null) { string[] NT = { "Value", "Perlin", "Cellular" }; props.Set("noiseType", (ntInt >= 0 && ntInt < 3) ? NT[ntInt.Value] : "Perlin"); }
                }
                if (layaType == "compare")
                {
                    int? condInt = UnityYamlParser.GetIntField(op.Body, "condition");
                    if (condInt != null) { string[] C = { "Equal", "Less", "LessOrEqual", "Greater", "GreaterOrEqual", "NotEqual" }; props.Set("operator", (condInt >= 0 && condInt < 6) ? C[condInt.Value] : "Less"); }
                }
                if (layaType == "swizzle")
                {
                    string mask = UnityYamlParser.GetStringField(op.Body, "mask");
                    if (mask != null) props.Set("pattern", mask);
                }
                if (layaType == "sequential3D" || layaType == "sequentialLine" || layaType == "sequentialCircle")
                {
                    int? modeIdx = UnityYamlParser.GetIntField(op.Body, "mode");
                    if (modeIdx != null) { string[] M = { "Wrap", "Clamp", "Mirror" }; props.Set("mode", (modeIdx >= 0 && modeIdx < 3) ? M[modeIdx.Value] : "Clamp"); }
                }

                // 多态 _type 推断
                if (POLYMORPHIC_OPS.Contains(layaType) && outputSlots.Count > 0)
                {
                    var outSlot = Get(outputSlots[0]);
                    string unityType = outSlot != null ? UnityYamlParser.GetSlotType(outSlot) : null;
                    string lt2 = VfxHelpers.UnityTypeToGlslType(unityType);
                    if (lt2 != null) props.Set("_type", lt2);
                }

                LayaOperators.Add(layaOp);
            }
        }

        private void RecordSlotTree(string slotID, int opIdx, string dir, string realSlotId, bool isResource, string parentRealSlotId, int layaId, string layaType)
        {
            var slotEntry = Get(slotID);
            string effectiveSlotId = realSlotId;
            if (slotEntry != null && parentRealSlotId != null)
            {
                string childPropName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                if (childPropName != null)
                {
                    string lower = childPropName.ToLowerInvariant();
                    string aliased; if (!COMPOSITE_SUBSLOT_ALIAS.TryGetValue(lower, out aliased)) aliased = lower;
                    effectiveSlotId = parentRealSlotId + "_" + aliased;
                }
            }
            SlotToOpId[slotID] = new SlotRef { LayaId = layaId, SlotIdx = opIdx, Dir = dir, OpTypeId = layaType, RealSlotId = effectiveSlotId, IsResource = isResource };
            if (slotEntry == null) return;
            foreach (var childID in UnityYamlParser.GetRefArrayField(slotEntry.Body, "m_Children"))
                RecordSlotTree(childID, opIdx, dir, realSlotId, isResource, effectiveSlotId, layaId, layaType);
        }

        /// <summary>展开 builtin 参数 / LogicalNot / VFXParameter(getProperty) 为 Laya operator（对应 JS L2592-2729）。</summary>
        public void ExpandBuiltinLogicalNotParameters()
        {
            // builtin
            foreach (var e in Entries)
            {
                if (e.ClassType != "VFXDynamicBuiltInParameter") continue;
                var outputSlots = UnityYamlParser.GetRefArrayField(e.Body, "m_OutputSlots");
                var uiPos = UnityYamlParser.GetUIPos(e.Body);
                foreach (var slotID in outputSlots)
                {
                    var slotEntry = Get(slotID);
                    if (slotEntry == null) continue;
                    string slotName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                    string layaType = null;
                    if (slotName != null) VfxMaps.BUILTIN_SLOT_MAP.TryGetValue(slotName, out layaType);
                    var linked = UnityYamlParser.GetLinkedSlots(slotEntry);
                    if (linked.Count == 0) continue;
                    if (layaType == null) continue;
                    int layaId = NewId();
                    LayaOperators.Add(Jval.Obj().Set("id", layaId).Set("typeId", layaType).Set("uiData", uiPos).Set("output", Jval.Obj()).Set("props", Jval.Obj()));
                    SlotToOpId[slotID] = new SlotRef { LayaId = layaId, SlotIdx = 0, Dir = "out", OpTypeId = layaType };
                    BuiltinSlotToOp.Add(new BuiltinSlotOp { SlotID = slotID, LayaOpId = layaId, LayaType = layaType });
                }
            }

            // LogicalNot
            foreach (var e in Entries)
            {
                if (e.ClassType != "LogicalNot") continue;
                var inputSlots = UnityYamlParser.GetRefArrayField(e.Body, "m_InputSlots");
                var outputSlots = UnityYamlParser.GetRefArrayField(e.Body, "m_OutputSlots");
                if (inputSlots.Count == 0 || outputSlots.Count == 0) continue;
                var uiPos = UnityYamlParser.GetUIPos(e.Body);
                int oneId = NewId();
                LayaOperators.Add(Jval.Obj().Set("id", oneId).Set("typeId", "inlineFloat")
                    .Set("uiData", Jval.Obj().Set("x", uiPos.NumOf("x") - 200).Set("y", uiPos.NumOf("y")))
                    .Set("output", Jval.Obj()).Set("props", Jval.Obj().Set("value", 1)));
                int subId = NewId();
                LayaOperators.Add(Jval.Obj().Set("id", subId).Set("typeId", "subtract").Set("uiData", uiPos).Set("output", Jval.Obj()).Set("props", Jval.Obj()));
                var subInputs = Defs.GetInputs("subtract") ?? new[] { "a", "b" };
                var oneOp = FindOp(oneId);
                oneOp.Get("output").Set("value", Jval.Obj().Set("infoArr", Jval.Arr(Jval.Obj().Set("nodeId", subId).Set("slotId", subInputs.Length > 0 ? subInputs[0] : "a"))));
                SlotToOpId[inputSlots[0]] = new SlotRef { LayaId = subId, SlotIdx = 1, Dir = "in", OpTypeId = "subtract" };
                SlotToOpId[outputSlots[0]] = new SlotRef { LayaId = subId, SlotIdx = 0, Dir = "out", OpTypeId = "subtract" };
                LogicalNotInfo.Add(new LogicalNotEntry { InSlotID = inputSlots[0], OutSlotID = outputSlots[0], SubtractOpId = subId });
            }

            // VFXParameter → getProperty
            int parameterExpanded = 0;
            foreach (var e in Entries)
            {
                if (e.ClassType != "VFXParameter") continue;
                if (e.InlinedFromSub) continue;   // sub 的 VFXParameter 通过 alias 内联，不走 getProperty 展开（JS L2680）
                string exposedName = UnityYamlParser.GetStringField(e.Body, "m_ExposedName");
                if (exposedName == null) continue;
                var outputSlots = UnityYamlParser.GetRefArrayField(e.Body, "m_OutputSlots");
                if (outputSlots.Count == 0) continue;
                if (VfxHelpers.UnityTypeToLayaPropType(UnityYamlParser.GetSlotType(Get(outputSlots[0]))) == "Transform") continue;
                bool hasLinks = false;
                foreach (var sid in outputSlots) if (HasLinkedSlotRec(sid)) { hasLinks = true; break; }
                if (!hasLinks) continue;
                int layaId = NewId();
                var uiPos = UnityYamlParser.GetUIPos(e.Body);
                var uiPosSpread = Jval.Obj().Set("x", uiPos.NumOf("x")).Set("y", uiPos.NumOf("y") + parameterExpanded * 64);
                LayaOperators.Add(Jval.Obj().Set("id", layaId).Set("typeId", "getProperty").Set("uiData", uiPosSpread)
                    .Set("output", Jval.Obj()).Set("props", Jval.Obj().Set("property", VfxHelpers.SanitizePropName(exposedName))));
                foreach (var sid in outputSlots) RegisterParamSlotTree(sid, layaId);
                ParameterToOp.Add(new ParamToOpEntry { LayaOpId = layaId, OutputSlots = outputSlots });
                parameterExpanded++;
            }
        }

        private bool HasLinkedSlotRec(string slotID)
        {
            var sE = Get(slotID);
            if (sE == null) return false;
            if (UnityYamlParser.GetLinkedSlots(sE).Count > 0) return true;
            foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) if (HasLinkedSlotRec(cid)) return true;
            return false;
        }
        private void RegisterParamSlotTree(string slotID, int layaId)
        {
            SlotToOpId[slotID] = new SlotRef { LayaId = layaId, SlotIdx = 0, Dir = "out", OpTypeId = "getProperty" };
            var sE = Get(slotID);
            if (sE == null) return;
            foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) RegisterParamSlotTree(cid, layaId);
        }

        public Jval FindOp(int id) { foreach (var o in LayaOperators) if ((int)o.NumOf("id") == id) return o; return null; }
    }
}
