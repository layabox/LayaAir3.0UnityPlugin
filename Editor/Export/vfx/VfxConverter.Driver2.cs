using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 驱动第二部分（Stage 2.4c）：context 主循环（含 shader 属性绑定 + 表达式图 builder +
    /// block 收集 + context props）。C# 移植自 unity-vfx-to-laya.js L2734-4697。
    ///
    /// 说明：常见路径忠实移植；特例（SkinnedMesh position 提取 / VFXSubgraphBlock 内联 /
    /// output ctx 字段）已补齐，其余按需补齐。
    /// </summary>
    public partial class VfxConverter
    {
        // shaderGraph guid→name / bps name→res 由外部注入（测试期用 json；插件接 ResoureMap）
        public Dictionary<string, string> ShaderGraphByGuid = new Dictionary<string, string>();
        public Dictionary<string, string> BlueprintShaderByName = new Dictionary<string, string>();
        public Dictionary<string, string> SubgraphNameByGuid = new Dictionary<string, string>();   // subgraph guid → 文件名（如 "vortex.vfxblock"）

        /// <summary>
        /// 把每个 context 转换为 Laya context 节点：解析 shader 属性绑定、收集 block、组装 props 与 flowLinks
        /// （对应 JS L2734-4697）。
        /// </summary>
        public void ConvertContexts()
        {
            foreach (var ctx in Contexts)
            {
                int layaId = FileIDToLayaId[ctx.FileID];
                string layaCtxType;
                if (!VfxMaps.CTX_MAP.TryGetValue(ctx.ClassType, out layaCtxType) || layaCtxType == null) continue;

                // 17.3 topology/shading rid 解耦 + 老式 inline shaderGraph
                string shaderGraphShaderName = null;
                {
                    double? topoRid = UnityYamlParser.GetNestedRid(ctx.Body, "m_Topology");
                    double? shadingRid = UnityYamlParser.GetNestedRid(ctx.Body, "m_Shading");
                    if (topoRid != null && shadingRid != null)
                    {
                        var refIds = UnityYamlParser.ParseRefIds(ctx.Body);
                        RefIdEntry topo, shading;
                        refIds.TryGetValue(topoRid.Value, out topo);
                        refIds.TryGetValue(shadingRid.Value, out shading);
                        string topoClass = topo != null ? topo.Class : null;
                        string shadingClass = shading != null ? shading.Class : null;
                        bool isShaderGraph = shadingClass == "ParticleShadingShaderGraph";
                        if (isShaderGraph && shading != null && shading.Data != null)
                        {
                            var sgm = Regex.Match(shading.Data, @"shaderGraph:\s*\{[^}]*guid:\s*([0-9a-f]{32})");
                            if (sgm.Success) ShaderGraphByGuid.TryGetValue(sgm.Groups[1].Value, out shaderGraphShaderName);
                        }
                        if (topoClass == "ParticleTopologyMesh" && isShaderGraph) layaCtxType = "outputShaderGraphMesh";
                        else if (topoClass == "ParticleTopologyQuad" && isShaderGraph) layaCtxType = "outputShaderGraphQuad";
                    }
                    if (shaderGraphShaderName == null)
                    {
                        var im = Regex.Match(ctx.Body, @"shaderGraph:\s*\{[^}]*guid:\s*([0-9a-f]{32})");
                        if (im.Success)
                        {
                            ShaderGraphByGuid.TryGetValue(im.Groups[1].Value, out shaderGraphShaderName);
                            if (shaderGraphShaderName != null)
                            {
                                if (layaCtxType == "outputMesh") layaCtxType = "outputShaderGraphMesh";
                                else if (layaCtxType == "outputBillboard" || layaCtxType == "outputComposedParticle") layaCtxType = "outputShaderGraphQuad";
                            }
                        }
                    }
                }

                var props = Jval.Obj();
                var layaCtx = Jval.Obj()
                    .Set("id", layaId).Set("typeId", layaCtxType)
                    .Set("uiData", UnityYamlParser.GetUIPos(ctx.Body))
                    .Set("blocks", Jval.Arr()).Set("props", props);

                if (shaderGraphShaderName != null)
                {
                    props.Set("shaderName", shaderGraphShaderName);
                    string bpsRes;
                    if (BlueprintShaderByName.TryGetValue(shaderGraphShaderName, out bpsRes) && bpsRes != null)
                        props.Set("shaderRes", bpsRes);
                    BuildShaderBinding(ctx, props, layaCtxType);
                }

                // block 收集
                var childIDs = UnityYamlParser.GetRefArrayField(ctx.Body, "m_Children");
                foreach (var childID in childIDs)
                {
                    var childEntry = Get(childID);
                    if (childEntry != null) ProcessChildBlock(childEntry, layaCtx, layaCtxType);
                }

                // context props
                BuildContextProps(ctx, layaCtx, props, layaCtxType);

                // Unity output 的 material _Color.a=0 隐藏整个 output → 注入 setAttribute(alpha,0)（JS L4635-4674）
                if (Regex.IsMatch(layaCtxType, "^output(Billboard|Mesh|StaticMesh|ComposedParticle|Cube|Trail|ShaderGraphMesh|ShaderGraphQuad)$"))
                {
                    var ctxInputsAll = new List<string>(UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots"));
                    foreach (var subID in UnityYamlParser.GetRefArrayField(ctx.Body, "m_SubOutputs"))
                    {
                        var subEntry = Get(subID); if (subEntry == null) continue;
                        foreach (var sid in UnityYamlParser.GetRefArrayField(subEntry.Body, "m_InputSlots")) ctxInputsAll.Add(sid);
                    }
                    foreach (var slotID in ctxInputsAll)
                    {
                        var slotEntry = Get(slotID); if (slotEntry == null) continue;
                        string propName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                        if (propName != "_Color" && propName != "Color") continue;
                        var v = UnityYamlParser.GetSlotInlineValue(slotEntry);
                        if (v == null || !v.IsObject) continue;
                        double aVal = (v.Get("a") != null && v.Get("a").IsNumber) ? v.NumOf("a")
                            : ((v.Get("w") != null && v.Get("w").IsNumber) ? v.NumOf("w") : 1);
                        if (aVal > 1e-4) continue;
                        int newBlockId = NewId();
                        layaCtx.Get("blocks").Push(Jval.Obj()
                            .Set("id", newBlockId).Set("typeId", "setAttribute").Set("enabled", true)
                            .Set("props", Jval.Obj()
                                .Set("attribute", "alpha").Set("source", "Slot").Set("composition", "Overwrite")
                                .Set("random", "Off").Set("channels", 7)
                                .Set("_values", Jval.Obj().Set("x", 0).Set("value", 0))));
                        break;
                    }
                }

                // flowLinks（对应 JS L4676-4692）
                var downstream = UnityYamlParser.GetOutputFlowSlot(ctx.Body);
                if (downstream.Count > 0)
                {
                    string slotName = ctx.ClassType == "VFXBasicSpawner" ? "spawnEvt" : "output";
                    var arr = new List<Jval>();
                    foreach (var d in downstream)
                    {
                        int tid; if (!FileIDToLayaId.TryGetValue(d, out tid)) continue;
                        var targetEntry = Get(d);
                        bool isSpawnTarget = targetEntry != null && targetEntry.ClassType == "VFXBasicSpawner";
                        arr.Add(Jval.Obj().Set("targetId", tid).Set("targetSlotId", isSpawnTarget ? "start" : "input"));
                    }
                    if (arr.Count > 0)
                        layaCtx.Set("flowLinks", Jval.Obj().Set(slotName, arr.Count == 1 ? arr[0] : ToArr(arr)));
                }

                LayaContexts.Add(layaCtx);
            }
        }

        // ── 表达式图 builder（对应 JS buildExpression L2833-3044） ──
        // graphNodes 是 Jval 对象（key=node id string），返回 root node id 或 null。
        private string BuildExpression(string slotID, Jval graphNodes, VfxEntry ctx, int depth = 0)
        {
            if (depth > 32) return null;
            var slot = Get(slotID);
            if (slot == null) return null;
            var linked = UnityYamlParser.GetLinkedSlots(slot);
            if (linked.Count == 0)
            {
                var inlineV = UnityYamlParser.GetSlotInlineValue(slot);
                string propName = UnityYamlParser.GetSlotPropertyName(slot);
                string slotType = UnityYamlParser.GetSlotTypeName(slot);
                string id = "c" + graphNodes.Count;
                graphNodes.Set(id, Jval.Obj().Set("kind", "Constant").Set("outputType", slotType ?? "float")
                    .Set("value", (inlineV != null && !inlineV.IsNull) ? inlineV : Jval.Of(0)).Set("slotName", propName));
                return id;
            }
            var upSlot = Get(linked[0]);
            if (upSlot == null) return null;
            string upMasterID = UnityYamlParser.GetSlotMaster(upSlot) ?? linked[0];
            var upMaster = Get(upMasterID);
            if (upMaster == null) return null;
            string ownerID = UnityYamlParser.GetRefField(upMaster.Body, "m_Owner");
            var owner = ownerID != null ? Get(ownerID) : null;
            if (owner == null) return null;
            string cls = owner.ClassType;

            if (cls == "VFXParameter")
            {
                string exposedName = UnityYamlParser.GetStringField(owner.Body, "m_ExposedName");
                var inlineV = UnityYamlParser.GetSlotInlineValue(upMaster);
                string slotType = UnityYamlParser.GetSlotTypeName(upMaster);
                string id = "p" + graphNodes.Count;
                graphNodes.Set(id, Jval.Obj().Set("kind", "VFXParameter").Set("outputType", slotType ?? "float")
                    .Set("exposedName", exposedName ?? "").Set("defaultValue", inlineV ?? Jval.Null()));
                return id;
            }
            if (cls == "VFXInlineOperator")
            {
                var opInputs = UnityYamlParser.GetRefArrayField(owner.Body, "m_InputSlots");
                Jval val = null; string slotType = null;
                if (opInputs.Count > 0)
                {
                    var inSlot = Get(opInputs[0]);
                    if (inSlot != null) { val = UnityYamlParser.GetSlotInlineValue(inSlot); slotType = UnityYamlParser.GetSlotTypeName(inSlot); }
                }
                string id = "c" + graphNodes.Count;
                graphNodes.Set(id, Jval.Obj().Set("kind", "Constant").Set("outputType", slotType ?? "float")
                    .Set("value", (val != null && !val.IsNull) ? val : Jval.Of(0)));
                return id;
            }

            // AgeOverLifetime → per-system 近似
            if (cls == "AgeOverLifetime" || cls == "Operator.AgeOverLifetime")
            {
                string cid = "c" + graphNodes.Count;
                var info = GetCtxSysInfo(ctx.FileID);
                if (info.Continuous)
                    graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", "float").Set("value", 0.5).Set("slotName", "ageMedian"));
                else
                    graphNodes.Set(cid, Jval.Obj().Set("kind", "GlobalTimeRatio").Set("outputType", "float").Set("duration", info.Lifetime).Set("continuous", false));
                return cid;
            }
            // VFXAttributeParameter → init 常量 / 中性默认
            if (cls == "VFXAttributeParameter")
            {
                string attrName = UnityYamlParser.GetStringField(owner.Body, "attribute");
                var constInfo = FindInitConstantAttributeValue(ctx, attrName);
                if (constInfo != null)
                {
                    string cid = "c" + graphNodes.Count;
                    graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", constInfo.Get("type").AsStr())
                        .Set("value", constInfo.Get("value")).Set("slotName", "attr_" + attrName));
                    return cid;
                }
                string norm = VfxMaps.NormalizeAttrName(attrName ?? "");
                if (norm == "color")
                {
                    string cid = "c" + graphNodes.Count;
                    graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", "vec4")
                        .Set("value", Jval.Obj().Set("r", 1).Set("g", 1).Set("b", 1).Set("a", 1)).Set("slotName", "attrDefault_" + attrName));
                    return cid;
                }
                if (norm == "alpha")
                {
                    string cid = "c" + graphNodes.Count;
                    graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", "float").Set("value", 1).Set("slotName", "attrDefault_" + attrName));
                    return cid;
                }
                return null;
            }

            // 已知 operator handler
            string kind = null, fixedOut = null;
            switch (cls)
            {
                case "VFXTime": case "Time": kind = "VFXTime"; fixedOut = "float"; break;
                case "VFXTotalTime": case "TotalTime": kind = "VFXTotalTime"; fixedOut = "float"; break;
                case "DeltaTime": case "VFXDeltaTime": kind = "VFXDeltaTime"; fixedOut = "float"; break;
                case "FrameIndex": case "VFXFrameIndex": kind = "VFXFrameIndex"; fixedOut = "float"; break;
                case "SampleGradient": case "Operator.SampleGradient": kind = "SampleGradient"; fixedOut = "vec4"; break;
                case "SampleCurve": case "Operator.SampleCurve": kind = "SampleCurve"; fixedOut = "float"; break;
                case "Add": case "Operator.Add": kind = "Add"; break;
                case "Subtract": case "Operator.Subtract": kind = "Sub"; break;
                case "Multiply": case "Operator.Multiply": kind = "Mul"; break;
                case "Divide": case "Operator.Divide": kind = "Div"; break;
                case "Modulo": case "Operator.Modulo": kind = "Mod"; break;
                case "Sine": kind = "Sin"; break;
                case "Cosine": kind = "Cos"; break;
                case "Frac": kind = "Frac"; break;
                case "Abs": kind = "Abs"; break;
                case "Negate": kind = "Neg"; break;
                case "Saturate": kind = "Saturate"; break;
                case "Lerp": case "Operator.Lerp": kind = "Lerp"; break;
                case "Random": case "RandomNumber": kind = "Random"; fixedOut = "float"; break;
            }
            if (kind == null) return null;

            var opInputs2 = UnityYamlParser.GetRefArrayField(owner.Body, "m_InputSlots");
            var inputNodeIds = new List<string>();
            foreach (var isid in opInputs2)
            {
                string childId = BuildExpression(isid, graphNodes, ctx, depth + 1);
                if (childId == null) return null;
                inputNodeIds.Add(childId);
            }
            // constant Random → 中点常量
            if ((cls == "Random" || cls == "RandomNumber") && (UnityYamlParser.GetIntField(owner.Body, "constant") ?? 0) == 1)
            {
                Func<string, string, double> readScalar = (nid, comp) =>
                {
                    if (nid == null) return 0;
                    var n = graphNodes.Get(nid);
                    if (n == null) return 0;
                    var nv = n.Get("value");
                    if (nv != null && nv.IsNumber) return nv.Num;
                    if (nv != null && nv.IsObject && nv.Get("x") != null && nv.Get("x").IsNumber) return comp == "y" ? nv.NumOf("y") : nv.NumOf("x");
                    var dv = n.Get("defaultValue");
                    if (dv != null && dv.IsNumber) return dv.Num;
                    if (dv != null && dv.IsObject && dv.Get("x") != null && dv.Get("x").IsNumber) return comp == "y" ? dv.NumOf("y") : dv.NumOf("x");
                    return 0;
                };
                double mn = readScalar(inputNodeIds.Count > 0 ? inputNodeIds[0] : null, "x");
                double mx = readScalar(inputNodeIds.Count > 1 ? inputNodeIds[1] : null, "y");
                string cid = "c" + graphNodes.Count;
                graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", "float").Set("value", (mn + mx) / 2).Set("slotName", "constRandom"));
                return cid;
            }
            string outType = fixedOut;
            if (outType == null && inputNodeIds.Count > 0) outType = graphNodes.Get(inputNodeIds[0]).StrOf("outputType") ?? "float";
            string id2 = "n" + graphNodes.Count;
            var inputsArr = Jval.Arr();
            foreach (var nid in inputNodeIds) inputsArr.Push(Jval.Of(nid));
            graphNodes.Set(id2, Jval.Obj().Set("kind", kind).Set("outputType", outType).Set("inputs", inputsArr));
            return id2;
        }

        // sys-info helpers（对应 JS getCtxSysInfo/fileHasContinuousSpawn/fileParticleLifetime/initContextLifetime）
        public class SysInfo { public bool Continuous; public double Lifetime; }
        private static bool IsContinuousSpawnClass(string cls)
        { return cls == "VFXSpawnerConstantRate" || cls == "VFXSpawnerPeriodicBurst" || cls == "VFXSpawnerVariableRate"; }

        private Dictionary<string, SysInfo> _ctxSysInfoMap;
        private SysInfo GetCtxSysInfo(string ctxFileID)
        {
            if (_ctxSysInfoMap == null)
            {
                _ctxSysInfoMap = new Dictionary<string, SysInfo>();
                foreach (var e in Entries)
                {
                    if (e.ClassType != "VFXBasicSpawner") continue;
                    bool cont = false;
                    foreach (var cid in UnityYamlParser.GetRefArrayField(e.Body, "m_Children"))
                    { var ce = Get(cid); if (ce != null && IsContinuousSpawnClass(ce.ClassType)) { cont = true; break; } }
                    var chain = new List<string>(); var seen = new HashSet<string>();
                    var stack = new List<string>(UnityYamlParser.GetOutputFlowSlot(e.Body));
                    double lifetime = 1;
                    while (stack.Count > 0)
                    {
                        string id = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                        if (seen.Contains(id)) continue; seen.Add(id);
                        chain.Add(id);
                        var b = Get(id); if (b == null) continue;
                        if (b.ClassType == "VFXBasicInitialize") { var lf = InitContextLifetime(b); if (lf != null && lf.Value > 0) lifetime = lf.Value; }
                        foreach (var dn in UnityYamlParser.GetOutputFlowSlot(b.Body)) stack.Add(dn);
                    }
                    foreach (var id in chain)
                    {
                        if (cont) _ctxSysInfoMap[id] = new SysInfo { Continuous = true, Lifetime = lifetime };
                        else if (!_ctxSysInfoMap.ContainsKey(id)) _ctxSysInfoMap[id] = new SysInfo { Continuous = false, Lifetime = lifetime };
                    }
                }
            }
            SysInfo v;
            if (_ctxSysInfoMap.TryGetValue(ctxFileID, out v)) return v;
            return new SysInfo { Continuous = FileHasContinuousSpawn(), Lifetime = FileParticleLifetime() };
        }
        private bool _continuousCache = false, _continuousComputed = false;
        private bool FileHasContinuousSpawn()
        {
            if (_continuousComputed) return _continuousCache;
            _continuousComputed = true;
            foreach (var e in Entries) if (IsContinuousSpawnClass(e.ClassType)) { _continuousCache = true; break; }
            return _continuousCache;
        }
        private double _lifetimeCache = 0;
        private double FileParticleLifetime()
        {
            if (_lifetimeCache != 0) return _lifetimeCache;
            _lifetimeCache = 1;
            foreach (var e in Entries) { if (e.ClassType != "VFXBasicInitialize") continue; var lf = InitContextLifetime(e); if (lf != null && lf.Value > 0) { _lifetimeCache = lf.Value; break; } }
            return _lifetimeCache;
        }
        private double? InitContextLifetime(VfxEntry initEntry)
        {
            foreach (var bid in UnityYamlParser.GetRefArrayField(initEntry.Body, "m_Children"))
            {
                var blk = Get(bid); if (blk == null) continue;
                if (!Regex.IsMatch(UnityYamlParser.GetStringField(blk.Body, "attribute") ?? "", @"(^|\W)lifetime(\W|$)", RegexOptions.IgnoreCase)) continue;
                var ins = UnityYamlParser.GetRefArrayField(blk.Body, "m_InputSlots");
                var r = ins.Count > 0 && ins[0] != null ? ResolveSetAttrScalar(ins[0]) : null;
                if (r != null && r.Value > 0) return r.Value;
            }
            return null;
        }
        // init 常量烘焙（对应 JS findInitConstantAttributeValue L1382-1438）：
        // 同 m_Data 的 init 里最后一个写该 attribute 的 enabled SetAttribute（Overwrite/Slot/Off 且无上游 link）
        // 的 inline 常量；update 有 enabled block 写同名 attribute → 非常量返回 null。
        private Jval FindInitConstantAttributeValue(VfxEntry outputCtx, string attrNameRaw)
        {
            if (string.IsNullOrEmpty(attrNameRaw)) return null;
            string want = VfxMaps.NormalizeAttrName(attrNameRaw);
            string dataRef = UnityYamlParser.GetRefField(outputCtx.Body, "m_Data");
            if (dataRef == null) return null;
            VfxEntry initCtx = null, updateCtx = null;
            foreach (var c in Contexts)
            {
                if (UnityYamlParser.GetRefField(c.Body, "m_Data") != dataRef) continue;
                if (c.ClassType == "VFXBasicInitialize") initCtx = c;
                else if (c.ClassType == "VFXBasicUpdate") updateCtx = c;
            }
            if (initCtx == null) return null;
            if (updateCtx != null)
            {
                foreach (var bid in UnityYamlParser.GetRefArrayField(updateCtx.Body, "m_Children"))
                {
                    var b = Get(bid); if (b == null) continue;
                    if (Regex.IsMatch(b.Body, @"^\s*m_Disabled:\s*1", RegexOptions.Multiline)) continue;
                    var a = UnityYamlParser.GetStringField(b.Body, "attribute");
                    if (a != null && VfxMaps.NormalizeAttrName(a) == want) return null;
                }
            }
            VfxEntry found = null;
            foreach (var bid in UnityYamlParser.GetRefArrayField(initCtx.Body, "m_Children"))
            {
                var b = Get(bid);
                if (b == null || b.ClassType != "SetAttribute") continue;
                if (Regex.IsMatch(b.Body, @"^\s*m_Disabled:\s*1", RegexOptions.Multiline)) continue;
                var a = UnityYamlParser.GetStringField(b.Body, "attribute");
                if (a == null || VfxMaps.NormalizeAttrName(a) != want) continue;
                found = b;   // 后者覆盖前者（Unity 语义）
            }
            if (found == null) return null;
            int comp = UnityYamlParser.GetIntField(found.Body, "Composition") ?? 0;
            int srcInt = UnityYamlParser.GetIntField(found.Body, "Source") ?? 0;
            int randInt = UnityYamlParser.GetIntField(found.Body, "Random") ?? 0;
            if (comp != 0 || srcInt != 0 || randInt != 0) return null;   // 非 Overwrite/Slot/Off
            var slots = UnityYamlParser.GetRefArrayField(found.Body, "m_InputSlots");
            var slot = slots.Count > 0 && slots[0] != null ? Get(slots[0]) : null;
            if (slot == null) return null;
            if (UnityYamlParser.GetLinkedSlots(slot).Count > 0) return null;   // 有上游 link → 非 inline 常量
            var v = UnityYamlParser.GetSlotInlineValue(slot);
            if (v == null) return null;
            if (v.IsNumber) return Jval.Obj().Set("type", "float").Set("value", v.Num);
            if (v.IsObject)
            {
                // Unity color attribute 是 RGB vec3；接 vec4 uniform 槽时 alpha 缺省补 1
                if (v.Get("r") != null)
                    return Jval.Obj().Set("type", "vec4").Set("value", Jval.Obj()
                        .Set("r", v.Get("r") != null && v.Get("r").IsNumber ? v.NumOf("r") : 1)
                        .Set("g", v.Get("g") != null && v.Get("g").IsNumber ? v.NumOf("g") : 1)
                        .Set("b", v.Get("b") != null && v.Get("b").IsNumber ? v.NumOf("b") : 1)
                        .Set("a", v.Get("a") != null && v.Get("a").IsNumber ? v.NumOf("a") : 1));
                if (v.Get("x") != null)
                {
                    if (v.Get("w") != null) return Jval.Obj().Set("type", "vec4").Set("value", v);
                    if (v.Get("z") != null) return Jval.Obj().Set("type", "vec3").Set("value", v);
                    return Jval.Obj().Set("type", "vec2").Set("value", v);
                }
            }
            return null;
        }
    }
}
