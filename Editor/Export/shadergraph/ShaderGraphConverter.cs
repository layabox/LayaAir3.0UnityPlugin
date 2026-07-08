using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LayaAir3.Converter
{
    /// <summary>
    /// Unity ShaderGraph(.shadergraph) → LayaAir 蓝图 shader(.bps) 转换器。
    /// C# 移植自 F:\git\LayaAir3.0\LayaVFXSample\shadertools\unity-shader-to-laya.js（已在项目验证）。
    /// 方法命名与结构与 JS 版一一对应，便于与 JS 产物 diff 对齐。
    /// </summary>
    public class ShaderGraphConverter
    {
        // ── 选项 ──
        public class Options
        {
            public bool SgInstanceMode;
            public string SgIncludeRelPath;
            public bool MaterialPropsMode;
            public bool ForceSupportVFX;
            public Dictionary<string, Jval> AssetMapping;  // Unity GUID → { layaUuid, unityName }
        }

        private struct Pos { public double X, Y; public Pos(double x, double y) { X = x; Y = y; } }

        private class InEdge { public string FromNodeId; public int FromSlotId; public string FromSlotObjId; }

        private class SgPropEntry
        {
            public string ReferenceName, DisplayName, PropertyType, Type, Attribute, Varying;
            public int Slot;
            public Jval Default;
        }

        public readonly SgIndex Idx;
        private bool alphaClip;
        private bool isVFXTarget;
        private readonly bool forceSupportVFX;
        private readonly List<Jval> layaArr = new List<Jval>();
        private List<Jval> layaArrRef; // 允许 _cleanupOrphanInjectedUV 替换
        private readonly List<Jval> uniformArr = new List<Jval>();
        private int bluePrintNum;
        private readonly Dictionary<string, Jval> unityToLaya = new Dictionary<string, Jval>();
        private readonly Dictionary<string, Jval> unityIdToObj = new Dictionary<string, Jval>();
        private readonly Dictionary<string, InEdge> inputEdgeByUnitySlot = new Dictionary<string, InEdge>();
        private readonly Dictionary<int, Jval> normalDecodeBySample = new Dictionary<int, Jval>();
        private readonly List<string> warnings = new List<string>();
        public readonly Dictionary<string, int> UnmappedNodes = new Dictionary<string, int>();
        private Pos layout = new Pos(0, 0);

        private readonly bool sgInstanceMode;
        private readonly string sgIncludeRelPath;
        private readonly List<SgPropEntry> sgPropertyTable = new List<SgPropEntry>();
        private readonly Dictionary<string, SgPropEntry> sgPropertyByRefName = new Dictionary<string, SgPropEntry>();

        private readonly bool materialPropsMode;
        private readonly Dictionary<string, Jval> uniformPropertyByRefName = new Dictionary<string, Jval>();

        private readonly Dictionary<string, Jval> assetMapping;
        public readonly HashSet<string> UnmappedGuids = new HashSet<string>();

        public readonly Dictionary<string, bool> KeywordDefines = new Dictionary<string, bool>();
        private string shaderType;      // "lit" | "unlit"
        private Jval pbrFragNode;

        public List<string> Warnings { get { return warnings; } }

        public ShaderGraphConverter(SgIndex idx, Options opts)
        {
            Idx = idx;
            opts = opts ?? new Options();
            forceSupportVFX = opts.ForceSupportVFX;
            sgInstanceMode = opts.SgInstanceMode;
            sgIncludeRelPath = opts.SgIncludeRelPath;
            materialPropsMode = opts.MaterialPropsMode;
            assetMapping = opts.AssetMapping ?? new Dictionary<string, Jval>();
            layaArrRef = layaArr;

            foreach (var n in idx.Nodes)
            {
                string oid = n.StrOf("m_ObjectId");
                if (oid != null) unityIdToObj[oid] = n;
            }
        }

        private int NextId() { return ++bluePrintNum; }
        private void Warn(string msg) { warnings.Add(msg); }

        /// <summary>执行转换，返回 .bps 内容的 Jval DOM。</summary>
        public Jval Convert()
        {
            BuildEdgeIndex();
            shaderType = DetectShaderType();
            FindMasterStackBlocks();
            PropagateTypes();
            return BuildBpsJson();
        }

        // ─────────────────────────────────────────────────────────────
        // 小工具
        // ─────────────────────────────────────────────────────────────
        private static string ShortType(Jval o) { return SgIndex.ShortType(o); }
        private static Jval Vec2(double x, double y) { return Jval.Obj().Set("x", x).Set("y", y); }
        private static Jval Vec3(double x, double y, double z) { return Jval.Obj().Set("x", x).Set("y", y).Set("z", z); }
        private static Jval Vec4(double x, double y, double z, double w) { return Jval.Obj().Set("x", x).Set("y", y).Set("z", z).Set("w", w); }
        private static Jval Col(double r, double g, double b, double a) { return Jval.Obj().Set("r", r).Set("g", g).Set("b", b).Set("a", a); }
        private static Jval Info(int id, int index) { return Jval.Obj().Set("id", id).Set("index", index); }

        private static bool ContainsCI(string s, string sub)
        {
            return s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        // slot 输入槽是否已有具体 defVal（非 undefined 且非 "_remove_"）
        private static bool HasConcreteDefVal(Jval slot)
        {
            var d = slot.Get("defVal");
            if (d == null) return false;
            if (d.IsString && d.Str == "_remove_") return false;
            return true;
        }

        // ─────────────────────────────────────────────────────────────
        // _detectShaderType
        // ─────────────────────────────────────────────────────────────
        private string DetectShaderType()
        {
            var gd = Idx.GraphData;
            var targets = gd != null ? gd.Get("m_ActiveTargets") : null;
            if (targets == null || !targets.IsArray) return "lit";
            string urpResult = null, hdResult = null;
            foreach (var tref in targets.Items)
            {
                var target = Idx.GetById(tref.StrOf("m_Id"));
                if (target == null) continue;
                if (target.Get("m_AlphaClip") != null && target.Get("m_AlphaClip").AsBool()) alphaClip = true;
                string mType = target.StrOf("m_Type") ?? "";
                bool hasLit = target.Get("m_Lit") != null && target.Get("m_Lit").IsBool;
                if (ContainsCI(mType, "VFXTarget") || hasLit)
                {
                    isVFXTarget = true;
                    return target.BoolOf("m_Lit") ? "lit" : "unlit";
                }
                var subRef = target.Get("m_ActiveSubTarget");
                if (subRef == null) continue;
                var subTarget = Idx.GetById(subRef.StrOf("m_Id"));
                if (subTarget == null) continue;
                string stType = subTarget.StrOf("m_Type") ?? "";
                bool isUnlit = ContainsCI(stType, "Unlit");
                bool isURP = ContainsCI(stType, "Universal.");
                bool isHD = ContainsCI(stType, "HighDefinition") || ContainsCI(stType, "HD");
                string result = isUnlit ? "unlit" : "lit";
                if (isURP && urpResult == null) urpResult = result;
                else if (isHD && hdResult == null) hdResult = result;
            }
            return urpResult ?? hdResult ?? "lit";
        }

        // ─────────────────────────────────────────────────────────────
        // _propagateTypes
        // ─────────────────────────────────────────────────────────────
        private void PropagateTypes()
        {
            var idToNode = new Dictionary<int, Jval>();
            foreach (var n in layaArrRef) idToNode[(int)n.NumOf("id")] = n;
            const int MAX_ITERS = 5;
            for (int iter = 0; iter < MAX_ITERS; iter++)
            {
                bool changed = false;
                foreach (var n in layaArrRef)
                {
                    string cid = n.StrOf("constDataID");
                    bool isTerminal = cid == "vertex" || cid == "PBR_fragment" || cid == "Unlit_fragment";
                    bool isCustom = cid == "function/custom";
                    if (!isTerminal && !isCustom)
                    {
                        var inputs = n.Get("inputList");
                        if (inputs != null)
                            foreach (var inp in inputs.Items)
                            {
                                var info = inp.Get("info");
                                if (info == null) continue;
                                Jval up;
                                if (!idToNode.TryGetValue((int)info.NumOf("id"), out up)) continue;
                                var upOutList = up.Get("outputList");
                                var upOut = upOutList != null ? upOutList.At((int)info.NumOf("index")) : null;
                                if (upOut == null || upOut.Get("type") == null) continue;
                                string t = upOut.StrOf("type");
                                string newType = t == "color" ? "vec4" : t;
                                if (inp.StrOf("type") != newType) { inp.Set("type", newType); changed = true; }
                            }
                    }
                    var before = OutTypes(n);
                    InferOutputTypeFromInputs(n);
                    var after = OutTypes(n);
                    for (int i = 0; i < before.Count && i < after.Count; i++)
                        if (before[i] != after[i]) { changed = true; break; }
                }
                if (!changed) break;
            }
        }

        private static List<string> OutTypes(Jval n)
        {
            var r = new List<string>();
            var ol = n.Get("outputList");
            if (ol != null) foreach (var o in ol.Items) r.Add(o.StrOf("type"));
            return r;
        }

        // ─────────────────────────────────────────────────────────────
        // buildEdgeIndex
        // ─────────────────────────────────────────────────────────────
        private void BuildEdgeIndex()
        {
            // nodeObjId → (localSlotId(int) → slotObjId(string))
            var nodeSlotByLocalId = new Dictionary<string, Dictionary<int, string>>();
            foreach (var n in Idx.Nodes)
            {
                var map = new Dictionary<int, string>();
                var slots = n.Get("m_Slots");
                if (slots != null)
                    foreach (var sref in slots.Items)
                    {
                        var s = Idx.GetById(sref.StrOf("m_Id"));
                        if (s == null) continue;
                        map[(int)s.NumOf("m_Id")] = s.StrOf("m_ObjectId");
                    }
                nodeSlotByLocalId[n.StrOf("m_ObjectId")] = map;
            }

            foreach (var e in Idx.Edges)
            {
                var from = e.Get("m_OutputSlot");
                var to = e.Get("m_InputSlot");
                if (from == null || to == null) continue;
                string fromNodeId = from.Get("m_Node") != null ? from.Get("m_Node").StrOf("m_Id") : null;
                string toNodeId = to.Get("m_Node") != null ? to.Get("m_Node").StrOf("m_Id") : null;
                int fromLocalSlot = (int)from.NumOf("m_SlotId");
                int toLocalSlot = (int)to.NumOf("m_SlotId");
                string fromSlotObjId = GetSlotObj(nodeSlotByLocalId, fromNodeId, fromLocalSlot);
                string toSlotObjId = GetSlotObj(nodeSlotByLocalId, toNodeId, toLocalSlot);
                if (toSlotObjId == null || fromSlotObjId == null) continue;
                inputEdgeByUnitySlot[toSlotObjId] = new InEdge
                {
                    FromNodeId = fromNodeId,
                    FromSlotId = fromLocalSlot,
                    FromSlotObjId = fromSlotObjId,
                };
            }
        }

        private static string GetSlotObj(Dictionary<string, Dictionary<int, string>> map, string nodeId, int localSlot)
        {
            if (nodeId == null) return null;
            Dictionary<int, string> inner;
            if (!map.TryGetValue(nodeId, out inner)) return null;
            string v;
            return inner.TryGetValue(localSlot, out v) ? v : null;
        }

        // ─────────────────────────────────────────────────────────────
        // findMasterStackBlocks
        // ─────────────────────────────────────────────────────────────
        private void FindMasterStackBlocks()
        {
            bool isUnlit = shaderType == "unlit";
            int vertexNodeId = NextId();
            int fragId = NextId();

            var layaVertex = Jval.Obj()
                .Set("x", 600).Set("y", 100)
                .Set("constDataID", "vertex")
                .Set("id", vertexNodeId)
                .Set("inputList", Jval.Arr(
                    Jval.Obj().Set("type", "vec3").Set("defVal", "_remove_"),
                    Jval.Obj().Set("type", "vec3").Set("defVal", "_remove_"),
                    Jval.Obj().Set("type", "vec4").Set("defVal", "_remove_")))
                .Set("outputList", Jval.Arr())
                .Set("select", false);

            Jval layaFrag;
            if (isUnlit)
            {
                layaFrag = Jval.Obj()
                    .Set("x", 600).Set("y", 400)
                    .Set("constDataID", "Unlit_fragment")
                    .Set("id", fragId)
                    .Set("inputList", Jval.Arr(
                        Jval.Obj().Set("type", "vec3").Set("defVal", Vec3(0, 0, 1)),
                        Jval.Obj().Set("type", "vec3").Set("defVal", Vec3(1, 1, 1)),
                        Jval.Obj().Set("type", "float").Set("defVal", 1)))
                    .Set("outputList", Jval.Arr())
                    .Set("select", false);
            }
            else
            {
                layaFrag = Jval.Obj()
                    .Set("x", 600).Set("y", 400)
                    .Set("ver", 1)
                    .Set("constDataID", "PBR_fragment")
                    .Set("id", fragId)
                    .Set("inputList", InitPbrFragInputList())
                    .Set("outputList", Jval.Arr())
                    .Set("select", false);
            }
            layaArrRef.Add(layaVertex);
            layaArrRef.Add(layaFrag);
            pbrFragNode = layaFrag;

            var slotMap = isUnlit ? SgNodeMapping.BLOCK_TO_UNLIT_SLOT : SgNodeMapping.BLOCK_TO_PBR_SLOT;
            var slotIndex = isUnlit ? SgNodeMapping.UNLIT_FRAGMENT_SLOTS : SgNodeMapping.PBR_FRAGMENT_SLOTS;

            foreach (var n in Idx.Nodes)
            {
                if (ShortType(n) != "BlockNode") continue;
                string desc = n.StrOf("m_SerializedDescriptor") ?? "";
                string[] parts = desc.Split('.');
                string stage = parts.Length > 0 ? parts[0] : "";
                string blockName = parts.Length > 0 ? parts[parts.Length - 1] : "";

                if (stage == "VertexDescription")
                {
                    if (!SgNodeMapping.VERTEX_SLOTS.ContainsKey(blockName))
                    {
                        Warn("Unknown VertexDescription BlockNode: " + desc);
                        continue;
                    }
                    int vIdx = SgNodeMapping.VERTEX_SLOTS[blockName];
                    string blockInputSlotObjId = FirstInputSlotObjId(n);
                    if (blockInputSlotObjId == null) continue;
                    InEdge edge;
                    if (!inputEdgeByUnitySlot.TryGetValue(blockInputSlotObjId, out edge)) continue;
                    ConnectInput(layaVertex, vIdx, edge.FromNodeId, edge.FromSlotId);
                    continue;
                }

                // Surface stage
                if (!slotMap.ContainsKey(blockName)) { Warn("Unknown BlockNode: " + desc); continue; }
                string targetSlotName = slotMap[blockName];
                if (targetSlotName == null) continue; // 已知但跳过
                if (!slotIndex.ContainsKey(targetSlotName)) continue;
                int targetIdx = slotIndex[targetSlotName];

                string inSlotObjId = FirstInputSlotObjId(n);
                if (inSlotObjId == null) continue;
                InEdge edge2;
                if (!inputEdgeByUnitySlot.TryGetValue(inSlotObjId, out edge2)) continue;
                ConnectInput(layaFrag, targetIdx, edge2.FromNodeId, edge2.FromSlotId);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // _connectInput
        // ─────────────────────────────────────────────────────────────
        private void ConnectInput(Jval toLayaNode, int toInputIdx, string fromUnityNodeId, int fromUnitySlotId)
        {
            var inputListArr = toLayaNode.Get("inputList");
            var layaInputSlot = inputListArr != null ? inputListArr.At(toInputIdx) : null;
            if (layaInputSlot == null) return;
            Jval srcU;
            if (!unityIdToObj.TryGetValue(fromUnityNodeId, out srcU)) return;
            string srcType = ShortType(srcU);

            if (srcType == "SplitNode")
            {
                string ch = null;
                switch (fromUnitySlotId) { case 1: ch = "R"; break; case 2: ch = "G"; break; case 3: ch = "B"; break; case 4: ch = "A"; break; }
                if (ch == null) return;
                string inSlotObjId = FindSlotObjIdByLocalId(srcU, 0);
                InEdge upEdge = null;
                if (inSlotObjId != null) inputEdgeByUnitySlot.TryGetValue(inSlotObjId, out upEdge);
                int maskId = NextId();
                var pos = UiPos(srcU);
                var maskNode = Jval.Obj()
                    .Set("x", pos.X).Set("y", pos.Y + 30)
                    .Set("constDataID", "math/expression/mask")
                    .Set("id", maskId)
                    .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "vec4").Set("defVal", "_remove_")))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "float")))
                    .Set("propertyVal", Jval.Obj().Set("R", ch == "R").Set("G", ch == "G").Set("B", ch == "B").Set("A", ch == "A"))
                    .Set("select", false);
                layaArrRef.Add(maskNode);
                if (upEdge != null)
                {
                    ConnectInput(maskNode, 0, upEdge.FromNodeId, upEdge.FromSlotId);
                    string inferred = InferOutputType(upEdge.FromNodeId, upEdge.FromSlotId);
                    if (inferred != null) maskNode.Get("inputList").At(0).Set("type", inferred);
                }
                else if (inSlotObjId != null)
                {
                    var inSlot = Idx.GetById(FindSlotByLocalId(srcU, 0));
                    if (inSlot != null)
                    {
                        var v = ExtractSlotDefault(inSlot, "vec4");
                        if (v != null) maskNode.Get("inputList").At(0).Set("defVal", v);
                    }
                }
                layaInputSlot.Set("info", Info(maskId, 0));
                AddReverseLink(maskNode, 0, (int)toLayaNode.NumOf("id"), toInputIdx);
                return;
            }

            var upstream = ConvertNodeRecursive(fromUnityNodeId);
            if (upstream == null) return;
            int outIdx = MapUnityOutputSlotToLaya(fromUnityNodeId, fromUnitySlotId);

            if (srcType == "SampleTexture2DNode" && (int)srcU.NumOf("m_TextureType", -1) == 1 && outIdx == 0)
            {
                var decode = GetOrCreateNormalDecode(upstream);
                layaInputSlot.Set("info", Info((int)decode.NumOf("id"), 0));
                AddReverseLink(decode, 0, (int)toLayaNode.NumOf("id"), toInputIdx);
                return;
            }

            layaInputSlot.Set("info", Info((int)upstream.NumOf("id"), outIdx));
            AddReverseLink(upstream, outIdx, (int)toLayaNode.NumOf("id"), toInputIdx);

            var upOut = upstream.Get("outputList").At(outIdx);
            string upstreamType = upOut != null ? upOut.StrOf("type") : null;
            string toCid = toLayaNode.StrOf("constDataID");
            bool isTerminal = toCid == "vertex" || toCid == "PBR_fragment" || toCid == "Unlit_fragment";
            bool isCustom = toCid == "function/custom";
            if (upstreamType != null && !isTerminal && !isCustom)
                layaInputSlot.Set("type", upstreamType == "color" ? "vec4" : upstreamType);
        }

        // ─────────────────────────────────────────────────────────────
        // _getOrCreateNormalDecode
        // ─────────────────────────────────────────────────────────────
        private Jval GetOrCreateNormalDecode(Jval sampleLayaNode)
        {
            int sampleId = (int)sampleLayaNode.NumOf("id");
            Jval cached;
            if (normalDecodeBySample.TryGetValue(sampleId, out cached)) return cached;
            int layaId = NextId();
            var decode = Jval.Obj()
                .Set("x", sampleLayaNode.NumOf("x") + 160).Set("y", sampleLayaNode.NumOf("y") + 40)
                .Set("constDataID", "function/custom")
                .Set("id", layaId)
                .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "vec4").Set("info", Info(sampleId, 0))))
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "vec4")))
                .Set("propertyVal", Jval.Obj()
                    .Set("code", "return vec4(c.xyz * 2.0 - 1.0, c.w);")
                    .Set("inputList", Jval.Arr(Jval.Obj().Set("name", "c").Set("type", "vec4")))
                    .Set("outputList", Jval.Arr())
                    .Set("type", "vec4")
                    .Set("defines", Jval.Arr())
                    .Set("includes", Jval.Arr()))
                .Set("select", false);
            layaArrRef.Add(decode);
            AddReverseLink(sampleLayaNode, 0, layaId, 0);
            normalDecodeBySample[sampleId] = decode;
            return decode;
        }

        private void AddReverseLink(Jval upstreamNode, int outIdx, int toNodeId, int toInputIdx)
        {
            var ol = upstreamNode.Get("outputList");
            var outSlot = ol != null ? ol.At(outIdx) : null;
            if (outSlot == null) return;
            var infoList = outSlot.Get("infoList");
            if (infoList == null) { infoList = Jval.Arr(); outSlot.Set("infoList", infoList); }
            infoList.Push(Info(toNodeId, toInputIdx));
        }

        private string FindSlotObjIdByLocalId(Jval unityNode, int localSlotId)
        {
            var slots = unityNode.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s != null && (int)s.NumOf("m_Id") == localSlotId) return s.StrOf("m_ObjectId");
                }
            return null;
        }
        private string FindSlotByLocalId(Jval unityNode, int localSlotId)
        {
            var slots = unityNode.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s != null && (int)s.NumOf("m_Id") == localSlotId) return sref.StrOf("m_Id");
                }
            return null;
        }

        // 推断 Unity output slot 在 Laya 端的标量类型
        private string InferOutputType(string unityNodeId, int slotId)
        {
            Jval u;
            if (!unityIdToObj.TryGetValue(unityNodeId, out u)) return null;
            string t = ShortType(u);
            switch (t)
            {
                case "UVNode": return "vec4";
                case "ColorNode": return "color";
                case "Vector4Node": return "vec4";
                case "Vector3Node": return "vec3";
                case "Vector2Node": return "vec2";
                case "Vector1Node": return "float";
                case "TimeNode": return "float";
                case "PositionNode":
                case "NormalVectorNode":
                case "TangentVectorNode": return "vec3";
            }
            var slots = u.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s != null && (int)s.NumOf("m_Id") == slotId && (int)s.NumOf("m_SlotType", -1) == 1)
                    {
                        string st = ShortType(s);
                        switch (st)
                        {
                            case "Vector1MaterialSlot": return "float";
                            case "Vector2MaterialSlot": return "vec2";
                            case "Vector3MaterialSlot": return "vec3";
                            case "Vector4MaterialSlot": return "vec4";
                            case "ColorRGBAMaterialSlot": return "color";
                            case "ColorRGBMaterialSlot": return "vec3";
                            case "DynamicVectorMaterialSlot": return "vec4";
                            case "DynamicValueMaterialSlot": return "vec4";
                        }
                    }
                }
            return null;
        }

        private string FirstInputSlotObjId(Jval unityNode)
        {
            var slots = unityNode.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s == null) continue;
                    if (!s.Has("m_SlotType") || (int)s.NumOf("m_SlotType", -1) == 0) return s.StrOf("m_ObjectId");
                }
            return null;
        }

        private Jval InitPbrFragInputList()
        {
            Func<string, Jval, Jval> E = (type, def) =>
            {
                var o = Jval.Obj().Set("type", type);
                if (def != null) o.Set("defVal", def);
                return o;
            };
            return Jval.Arr(
                E("float", Jval.Of("_remove_")),           // 0
                E("vec3", Vec3(0, 0, 1)),                  // 1 NormalTS
                E("vec3", Vec3(1, 1, 1)),                  // 2 BaseColor
                E("float", null),                          // 3 Metallic
                E("float", Jval.Of(0.5)),                  // 4 Specular
                E("vec3", Vec3(1, 1, 1)),                  // 5 SpecularColor
                E("float", null),                          // 6 Smoothness
                E("float", Jval.Of(1)),                    // 7 Occlusion
                E("vec3", Jval.Of("_remove_")),            // 8 EmissionColor
                E("float", Jval.Of(1)),                    // 9 Opacity
                E("float", Jval.Of(0)),                    // 10
                E("float", Jval.Of(0)),                    // 11
                E("float", Jval.Of(0)),                    // 12
                E("float", Jval.Of(0)),                    // 13
                E("vec3", Jval.Of("_remove_")),            // 14
                E("vec3", Vec3(0, 0, 0)),                  // 15
                E("float", Jval.Of(0)),                    // 16
                E("float", Jval.Of("_remove_")),           // 17
                E("float", Jval.Of(0)),                    // 18
                E("float", Jval.Of(0)),                    // 19
                E("float", Jval.Of(1.3)),                  // 20
                E("float", Jval.Of(400)));                 // 21
        }

        // ─────────────────────────────────────────────────────────────
        // convertNodeRecursive
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertNodeRecursive(string unityNodeId)
        {
            Jval existing;
            if (unityToLaya.TryGetValue(unityNodeId, out existing)) return existing;
            Jval u;
            if (!unityIdToObj.TryGetValue(unityNodeId, out u)) return null;
            string t = ShortType(u);

            if (t == "PropertyNode") return ConvertPropertyNode(u);
            if (t == "SplitNode") return null;
            if (t == "VertexColorNode") return ConvertVertexColorWithAdapter(u);

            NodeMap map;
            if (!SgNodeMapping.NODE_MAPPING.TryGetValue(t, out map))
            {
                UnmappedNodes[t] = (UnmappedNodes.ContainsKey(t) ? UnmappedNodes[t] : 0) + 1;
                Warn("Unmapped node type: " + t);
                return CreateConstFloat(0, UiPos(u));
            }

            if (map.CustomGlsl != null) return ConvertCustomGlslNode(u, t, map);
            if (map.CustomFunction) return ConvertCustomFunctionNode(u, t, map);

            int layaId = NextId();
            var pos = UiPos(u);
            var layaNode = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", map.Id)
                .Set("id", layaId)
                .Set("inputList", Jval.Arr())
                .Set("outputList", Jval.Arr())
                .Set("select", false);
            if (map.Ver != 0) layaNode.Set("ver", map.Ver);
            var inList = layaNode.Get("inputList");
            var outList = layaNode.Get("outputList");
            foreach (var inputName in (map.Inputs ?? new string[0]))
                inList.Push(Jval.Obj().Set("type", GuessInputType(t, inputName)).Set("defVal", "_remove_"));
            foreach (var outputName in (map.Outputs ?? new string[0]))
                outList.Push(Jval.Obj().Set("type", GuessOutputType(t, outputName)));

            layaArrRef.Add(layaNode);
            unityToLaya[unityNodeId] = layaNode;

            var slots = u.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s == null || (int)s.NumOf("m_SlotType", -1) != 0) continue;
                    string slotName = s.StrOf("m_DisplayName") ?? s.StrOf("m_ShaderOutputName") ?? "";
                    int layaIdx = MapUnitySlotToLayaInputIndex(t, slotName, (int)s.NumOf("m_SlotId"));
                    if (layaIdx < 0 || layaIdx >= inList.Count) continue;
                    InEdge edge;
                    if (inputEdgeByUnitySlot.TryGetValue(s.StrOf("m_ObjectId"), out edge))
                        ConnectInput(layaNode, layaIdx, edge.FromNodeId, edge.FromSlotId);
                    else
                    {
                        string targetType = inList.At(layaIdx).StrOf("type");
                        var v = ExtractSlotDefault(s, targetType);
                        if (v != null) inList.At(layaIdx).Set("defVal", v);
                    }
                }

            if (map.Saturate && inList.Count >= 3)
            {
                inList.At(1).Set("defVal", 0);
                inList.At(2).Set("defVal", 1);
            }
            if (map.ColorAsVec4 && inList.Count >= 4)
            {
                var c = u.Get("m_Color") != null ? u.Get("m_Color").Get("color") : null;
                if (c != null)
                {
                    inList.At(0).Set("defVal", c.NumOf("r", 1));
                    inList.At(1).Set("defVal", c.NumOf("g", 1));
                    inList.At(2).Set("defVal", c.NumOf("b", 1));
                    inList.At(3).Set("defVal", c.NumOf("a", 1));
                }
            }
            if (t == "ClampNode" && inList.Count >= 3)
            {
                if (!HasConcreteDefVal(inList.At(1))) inList.At(1).Set("defVal", 0);
                if (!HasConcreteDefVal(inList.At(2))) inList.At(2).Set("defVal", 1);
            }
            if (t == "TilingAndOffsetNode" && inList.Count >= 1 && inList.At(0).Get("info") == null)
            {
                Jval uvVal = null;
                if (slots != null)
                    foreach (var sref in slots.Items)
                    {
                        var s = Idx.GetById(sref.StrOf("m_Id"));
                        if (s != null && (int)s.NumOf("m_SlotType", -1) == 0 &&
                            (s.StrOf("m_DisplayName") ?? "").ToLowerInvariant() == "uv")
                        { uvVal = ExtractSlotDefault(s, "vec2"); break; }
                    }
                if (uvVal == null) uvVal = Vec2(0, 0);
                var constNode = CreateConstVec2(uvVal, new Pos(pos.X - 200, pos.Y));
                inList.At(0).Set("info", Info((int)constNode.NumOf("id"), 0));
                AddReverseLink(constNode, 0, layaId, 0);
                inList.At(0).Remove("defVal");
            }
            foreach (var inp in inList.Items)
            {
                if (inp.Get("info") != null) continue;
                if (HasConcreteDefVal(inp)) continue;
                inp.Set("defVal", SafeDefaultValue(inp.StrOf("type")));
            }
            InferOutputTypeFromInputs(layaNode);
            return layaNode;
        }

        // ─────────────────────────────────────────────────────────────
        // _inferOutputTypeFromInputs
        // ─────────────────────────────────────────────────────────────
        private static readonly HashSet<string> POLYMORPHIC_FIRST = new HashSet<string>
        {
            "math/basic/add","math/basic/minus","math/basic/multiply","math/basic/divide","math/basic/oneMinus",
            "math/common/abs","math/common/sign","math/common/floor","math/common/ceil","math/common/fract",
            "math/common/mod","math/common/min","math/common/max","math/common/clamp","math/common/lerp","math/common/mix",
            "math/common/step","math/common/smoothstep",
            "math/exponential/pow","math/exponential/sqrt","math/exponential/inversesqrt","math/exponential/exp",
            "math/exponential/log","math/exponential/exp2","math/exponential/log2",
            "math/trigonometry/sin","math/trigonometry/cos","math/trigonometry/tan","math/trigonometry/asin",
            "math/trigonometry/acos","math/trigonometry/atan","math/trigonometry/atan2","math/trigonometry/radians","math/trigonometry/degrees",
            "math/geometric/normalize","math/geometric/reflect","math/geometric/refract","math/geometric/faceforward",
            "function/normalScale",
        };
        private static readonly HashSet<string> ALWAYS_FLOAT = new HashSet<string>
        {
            "math/geometric/length","math/geometric/distance","math/geometric/dot",
        };

        private static int DimOf(string t)
        {
            if (t == null) return 1;
            if (t == "float" || t == "int" || t == "bool") return 1;
            if (t == "color" || t == "vec4") return 4;
            if (t == "vec3") return 3;
            if (t == "vec2") return 2;
            return 1;
        }
        private static string TypeOf(int n) { return n == 4 ? "vec4" : n == 3 ? "vec3" : n == 2 ? "vec2" : "float"; }

        private void InferOutputTypeFromInputs(Jval layaNode)
        {
            string cid = layaNode.StrOf("constDataID");
            var inList = layaNode.Get("inputList");
            var outList = layaNode.Get("outputList");
            int maxDim = 1;
            if (inList != null) foreach (var inp in inList.Items) { int d = DimOf(inp.StrOf("type")); if (d > maxDim) maxDim = d; }
            string maxType = TypeOf(maxDim);

            bool needUnifyInput = POLYMORPHIC_FIRST.Contains(cid) || ALWAYS_FLOAT.Contains(cid) || cid == "math/geometric/cross";
            if (needUnifyInput && inList != null)
                foreach (var inp in inList.Items)
                {
                    if (inp.StrOf("type") == "color") inp.Set("type", "vec4");
                    if (DimOf(inp.StrOf("type")) < maxDim) inp.Set("type", maxType);
                }

            if (cid == "math/geometric/cross" && outList != null && outList.At(0) != null) { outList.At(0).Set("type", "vec3"); return; }
            if (ALWAYS_FLOAT.Contains(cid) && outList != null && outList.At(0) != null) { outList.At(0).Set("type", "float"); return; }
            if (POLYMORPHIC_FIRST.Contains(cid) && outList != null && outList.At(0) != null && inList != null && inList.Count > 0)
                outList.At(0).Set("type", maxType);
        }

        // ─────────────────────────────────────────────────────────────
        // _convertPropertyNode
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertPropertyNode(Jval u)
        {
            var propRef = u.Get("m_Property");
            var prop = propRef != null ? Idx.GetById(propRef.StrOf("m_Id")) : null;
            var pos = UiPos(u);
            if (prop == null) return CreateConstFloat(0, pos);
            string ptype = ShortType(prop);

            if (sgInstanceMode)
            {
                var node = ConvertPropertyNodeAsInstance(u, prop, ptype, pos);
                if (node != null) return node;
            }
            if (materialPropsMode)
            {
                var node = ConvertPropertyNodeAsUniform(u, prop, ptype, pos);
                if (node != null) return node;
            }

            int layaId = NextId();
            var def = prop.Has("m_Value") ? prop.Get("m_Value") : prop.Get("m_Default");
            Jval layaNode;
            if (ptype == "Vector1ShaderProperty" || ptype == "FloatShaderProperty")
            {
                layaNode = BaseNode(pos, "basic/Float", layaId)
                    .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "float").Set("defVal", def != null && def.IsNumber ? def.Num : 0)))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "float")));
            }
            else if (ptype == "Vector2ShaderProperty")
            {
                layaNode = BaseNode(pos, "basic/Vector2", layaId).Set("ver", 1)
                    .Set("inputList", Jval.Arr(
                        Jval.Obj().Set("type", "vec2").Set("defVal", ToVec2(def)),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y"))))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XY").Set("type", "vec2")));
            }
            else if (ptype == "Vector3ShaderProperty")
            {
                layaNode = BaseNode(pos, "basic/Vector3", layaId).Set("ver", 1)
                    .Set("inputList", Jval.Arr(
                        Jval.Obj().Set("type", "vec3").Set("defVal", ToVec3(def)),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "z"))))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XYZ").Set("type", "vec3")));
            }
            else if (ptype == "Vector4ShaderProperty")
            {
                layaNode = BaseNode(pos, "basic/Vector4", layaId).Set("ver", 1)
                    .Set("inputList", Jval.Arr(
                        Jval.Obj().Set("type", "vec4").Set("defVal", ToVec4(def)),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "z")),
                        Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "w"))))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XYZW").Set("type", "vec4")));
            }
            else if (ptype == "ColorShaderProperty")
            {
                var c = ToColor(def);
                layaNode = BaseNode(pos, "basic/Vector4", layaId).Set("ver", 1)
                    .Set("inputList", Jval.Arr(
                        Jval.Obj().Set("type", "vec4").Set("defVal", Vec4(c[0], c[1], c[2], c[3])),
                        Jval.Obj().Set("type", "float").Set("defVal", c[0]),
                        Jval.Obj().Set("type", "float").Set("defVal", c[1]),
                        Jval.Obj().Set("type", "float").Set("defVal", c[2]),
                        Jval.Obj().Set("type", "float").Set("defVal", c[3])))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XYZW").Set("type", "vec4")));
            }
            else if (ptype == "BooleanShaderProperty")
            {
                layaNode = BaseNode(pos, "basic/Boolean", layaId)
                    .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "bool").Set("defVal", def != null && def.AsBool())))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "bool")));
            }
            else if (ptype == "Texture2DShaderProperty")
            {
                layaNode = BaseNode(pos, "texture/texture2D", layaId)
                    .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "sampler2D").Set("defVal", "_remove_")))
                    .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "sampler2D")));
            }
            else
            {
                Warn("Unsupported PropertyType: " + ptype);
                return CreateConstFloat(0, pos);
            }
            layaArrRef.Add(layaNode);
            unityToLaya[u.StrOf("m_ObjectId")] = layaNode;
            return layaNode;
        }

        private static Jval BaseNode(Pos pos, string constDataID, int id)
        {
            return Jval.Obj().Set("x", pos.X).Set("y", pos.Y).Set("constDataID", constDataID).Set("id", id).Set("select", false);
        }

        // 解析 Texture2DShaderProperty 的 GUID → res://<uuid>
        private string ResolveTextureProperty(Jval prop)
        {
            var val = prop != null ? prop.Get("m_Value") : null;
            string serialized = val != null ? val.StrOf("m_SerializedTexture") : null;
            if (serialized == null) return null;
            var pj = Jval.Parse(serialized);
            if (pj == null || !pj.IsObject) return null;
            var tex = pj.Get("texture");
            string guid = tex != null ? tex.StrOf("guid") : null;
            if (string.IsNullOrEmpty(guid) || IsAllZero(guid)) return null;
            Jval m;
            if (assetMapping.TryGetValue(guid, out m) && m.StrOf("layaUuid") != null)
                return "res://" + m.StrOf("layaUuid");
            string uname = (m != null && m.StrOf("unityName") != null) ? "  (" + m.StrOf("unityName") + ")" : "";
            UnmappedGuids.Add(guid + uname);
            return null;
        }
        private static bool IsAllZero(string s) { foreach (char c in s) if (c != '0') return false; return true; }

        // ─────────────────────────────────────────────────────────────
        // _convertPropertyNodeAsUniform
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertPropertyNodeAsUniform(Jval u, Jval prop, string ptype, Pos pos)
        {
            string refName = RefNameOf(prop);
            var def = prop.Has("m_Value") ? prop.Get("m_Value") : prop.Get("m_Default");
            string constId; Jval inputList, outputList; int ver = 0;

            if (ptype == "Vector1ShaderProperty" || ptype == "FloatShaderProperty")
            {
                constId = "basic/Float";
                inputList = Jval.Arr(Jval.Obj().Set("type", "float").Set("defVal", def != null && def.IsNumber ? def.Num : 0));
                outputList = Jval.Arr(Jval.Obj().Set("type", "float"));
            }
            else if (ptype == "Vector2ShaderProperty")
            {
                constId = "basic/Vector2"; ver = 1;
                inputList = Jval.Arr(
                    Jval.Obj().Set("type", "vec2").Set("defVal", ToVec2(def)),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y")));
                outputList = Jval.Arr(Jval.Obj().Set("name", "XY").Set("type", "vec2"));
            }
            else if (ptype == "Vector3ShaderProperty")
            {
                constId = "basic/Vector3"; ver = 1;
                inputList = Jval.Arr(
                    Jval.Obj().Set("type", "vec3").Set("defVal", ToVec3(def)),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "z")));
                outputList = Jval.Arr(Jval.Obj().Set("name", "XYZ").Set("type", "vec3"));
            }
            else if (ptype == "Vector4ShaderProperty")
            {
                constId = "basic/Vector4"; ver = 1;
                inputList = Jval.Arr(
                    Jval.Obj().Set("type", "vec4").Set("defVal", ToVec4(def)),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "x")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "y")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "z")),
                    Jval.Obj().Set("type", "float").Set("defVal", NumField(def, "w")));
                outputList = Jval.Arr(Jval.Obj().Set("name", "XYZW").Set("type", "vec4"));
            }
            else if (ptype == "ColorShaderProperty")
            {
                var c = ToColor(def);
                constId = "basic/Vector4"; ver = 1;
                inputList = Jval.Arr(
                    Jval.Obj().Set("type", "vec4").Set("defVal", Vec4(c[0], c[1], c[2], c[3])),
                    Jval.Obj().Set("type", "float").Set("defVal", c[0]),
                    Jval.Obj().Set("type", "float").Set("defVal", c[1]),
                    Jval.Obj().Set("type", "float").Set("defVal", c[2]),
                    Jval.Obj().Set("type", "float").Set("defVal", c[3]));
                outputList = Jval.Arr(Jval.Obj().Set("name", "XYZW").Set("type", "vec4"));
            }
            else if (ptype == "BooleanShaderProperty")
            {
                constId = "basic/Boolean";
                inputList = Jval.Arr(Jval.Obj().Set("type", "bool").Set("defVal", def != null && def.AsBool()));
                outputList = Jval.Arr(Jval.Obj().Set("type", "bool"));
            }
            else if (ptype == "Texture2DShaderProperty")
            {
                constId = "texture/texture2D";
                string resPath = ResolveTextureProperty(prop);
                inputList = Jval.Arr(Jval.Obj().Set("type", "sampler2D").Set("defVal", resPath ?? ""));
                outputList = Jval.Arr(Jval.Obj().Set("type", "sampler2D"));
            }
            else return null;

            Jval dataNode;
            if (!uniformPropertyByRefName.TryGetValue(refName, out dataNode))
            {
                int dataId = NextId();
                dataNode = Jval.Obj()
                    .Set("x", pos.X).Set("y", pos.Y)
                    .Set("constDataID", constId)
                    .Set("id", dataId)
                    .Set("inputList", inputList)
                    .Set("outputList", outputList)
                    .Set("uniformName", refName)
                    .Set("propertyVal", Jval.Obj())
                    .Set("select", false);
                if (ver != 0) dataNode.Set("ver", ver);
                uniformArr.Add(Jval.Obj().Set("id", constId).Set("data", dataNode).Set("index", uniformArr.Count));
                uniformPropertyByRefName[refName] = dataNode;
            }

            int refId = NextId();
            var refNode = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", constId)
                .Set("id", refId)
                .Set("inputList", DeepClone(inputList))
                .Set("outputList", DeepClone(outputList))
                .Set("uniformDataID", (int)dataNode.NumOf("id"))
                .Set("propertyVal", Jval.Obj())
                .Set("select", false);
            if (ver != 0) refNode.Set("ver", ver);
            layaArrRef.Add(refNode);
            unityToLaya[u.StrOf("m_ObjectId")] = refNode;
            return refNode;
        }

        // ─────────────────────────────────────────────────────────────
        // _convertCustomFunctionNode
        // ─────────────────────────────────────────────────────────────
        private class SlotEntry { public string SlotObjId; public int SlotId; public string Name; public string Type; public Jval Slot; }

        private Jval ConvertCustomFunctionNode(Jval u, string unityType, NodeMap map)
        {
            var inputs = new List<SlotEntry>();
            var outputs = new List<SlotEntry>();
            var slots = u.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s == null) continue;
                    string slotType = UnitySlotTypeToGlsl(s.StrOf("m_Type"));
                    if (slotType == null) continue;
                    var entry = new SlotEntry
                    {
                        SlotObjId = s.StrOf("m_ObjectId"),
                        SlotId = (int)s.NumOf("m_Id"),
                        Name = s.StrOf("m_DisplayName") ?? s.StrOf("m_ShaderOutputName") ?? ("slot" + (int)s.NumOf("m_Id")),
                        Type = slotType,
                        Slot = s,
                    };
                    if ((int)s.NumOf("m_SlotType", -1) == 0) inputs.Add(entry);
                    else if ((int)s.NumOf("m_SlotType", -1) == 1) outputs.Add(entry);
                }
            if (outputs.Count == 0) { Warn("CustomFunctionNode 无 output slot，跳过"); return null; }
            var outSlot = outputs[0];

            string code;
            int sourceType = (int)u.NumOf("m_SourceType", -1);
            if (sourceType == 1)
            {
                string body = (u.StrOf("m_FunctionBody") ?? "").Trim();
                if (body == "" || body == "Enter function body here...")
                {
                    Warn("CustomFunctionNode (" + (u.StrOf("m_FunctionName") ?? "?") + ") 函数体为空");
                    code = "return " + SafeDefaultExpr(outSlot.Type) + ";";
                }
                else
                {
                    string outName = outSlot.Name;
                    string replaced = ReplaceLastAssignment(body, outName);
                    if (replaced != null) code = replaced;
                    else code = body + "\nreturn " + SafeDefaultExpr(outSlot.Type) + ";";
                }
            }
            else
            {
                Warn("CustomFunctionNode \"" + (u.StrOf("m_FunctionName") ?? "?") + "\" 是 File mode（外部 .hlsl），转换器不展开 → 占位 fallback");
                code = "return " + SafeDefaultExpr(outSlot.Type) + ";";
            }

            int layaId = NextId();
            var pos = UiPos(u);
            var pvInputList = Jval.Arr();
            var nodeInputList = Jval.Arr();
            foreach (var si in inputs)
            {
                nodeInputList.Push(Jval.Obj().Set("type", si.Type).Set("defVal", "_remove_"));
                pvInputList.Push(Jval.Obj().Set("name", si.Name).Set("type", si.Type));
            }
            var layaNode = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", "function/custom")
                .Set("id", layaId)
                .Set("inputList", nodeInputList)
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", outSlot.Type)))
                .Set("propertyVal", Jval.Obj()
                    .Set("code", code)
                    .Set("inputList", pvInputList)
                    .Set("outputList", Jval.Arr())
                    .Set("type", outSlot.Type)
                    .Set("defines", Jval.Arr())
                    .Set("includes", Jval.Arr()))
                .Set("select", false);
            layaArrRef.Add(layaNode);
            unityToLaya[u.StrOf("m_ObjectId")] = layaNode;

            for (int i = 0; i < inputs.Count; i++)
            {
                var s = inputs[i].Slot;
                InEdge edge;
                if (inputEdgeByUnitySlot.TryGetValue(s.StrOf("m_ObjectId"), out edge))
                    ConnectInput(layaNode, i, edge.FromNodeId, edge.FromSlotId);
                else
                {
                    var v = ExtractSlotDefault(s, nodeInputList.At(i).StrOf("type"));
                    if (v != null) nodeInputList.At(i).Set("defVal", v);
                }
            }
            foreach (var inp in nodeInputList.Items)
            {
                if (inp.Get("info") != null) continue;
                if (HasConcreteDefVal(inp)) continue;
                inp.Set("defVal", SafeDefaultValue(inp.StrOf("type")));
            }
            return layaNode;
        }

        // 替换 body 中最后一个 `<outName> = expr;` 为 `return expr;`（模仿 JS 的 lookahead 正则）
        private static string ReplaceLastAssignment(string body, string outName)
        {
            // 找最后一处 outName = ...; 且其后不再出现 outName =
            var re = new System.Text.RegularExpressions.Regex(
                System.Text.RegularExpressions.Regex.Escape(outName) + @"\s*=\s*([^;]+);");
            var matches = re.Matches(body);
            if (matches.Count == 0) return null;
            var last = matches[matches.Count - 1];
            string expr = last.Groups[1].Value;
            return body.Substring(0, last.Index) + "return " + expr + ";" + body.Substring(last.Index + last.Length);
        }

        private string UnitySlotTypeToGlsl(string unityTypeStr)
        {
            string t = SgIndex.ShortType(unityTypeStr);
            switch (t)
            {
                case "Vector1MaterialSlot":
                case "ScreenPositionMaterialSlot": return "float";
                case "Vector2MaterialSlot": return "vec2";
                case "Vector3MaterialSlot":
                case "NormalMaterialSlot":
                case "PositionMaterialSlot":
                case "TangentMaterialSlot": return "vec3";
                case "Vector4MaterialSlot":
                case "DynamicVectorMaterialSlot": return "vec4";
                case "ColorRGBAMaterialSlot": return "vec4";
                case "ColorRGBMaterialSlot": return "vec3";
                case "DynamicValueMaterialSlot": return "vec4";
                case "BooleanMaterialSlot": return "bool";
                case "Matrix4MaterialSlot": return "mat4";
                default: return null;
            }
        }

        private static string SafeDefaultExpr(string type)
        {
            switch (type)
            {
                case "float": return "0.0";
                case "vec2": return "vec2(0.0)";
                case "vec3": return "vec3(0.0)";
                case "vec4": return "vec4(0.0)";
                case "bool": return "false";
                case "mat4": return "mat4(1.0)";
                default: return "0.0";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // _convertCustomGlslNode
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertCustomGlslNode(Jval u, string unityType, NodeMap map)
        {
            var cfg = map.CustomGlsl(u, this);
            int layaId = NextId();
            var pos = UiPos(u);
            var nodeInputList = Jval.Arr();
            foreach (var pt in cfg.ParamTypes) nodeInputList.Push(Jval.Obj().Set("type", pt).Set("defVal", "_remove_"));
            var pvInputList = Jval.Arr();
            for (int i = 0; i < cfg.ParamNames.Length; i++)
                pvInputList.Push(Jval.Obj().Set("name", cfg.ParamNames[i]).Set("type", cfg.ParamTypes[i]));
            var layaNode = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", "function/custom")
                .Set("id", layaId)
                .Set("inputList", nodeInputList)
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", cfg.OutputType)))
                .Set("propertyVal", Jval.Obj()
                    .Set("code", cfg.Code)
                    .Set("inputList", pvInputList)
                    .Set("outputList", Jval.Arr())
                    .Set("type", cfg.OutputType)
                    .Set("defines", Jval.Arr())
                    .Set("includes", Jval.Arr()))
                .Set("select", false);
            layaArrRef.Add(layaNode);
            unityToLaya[u.StrOf("m_ObjectId")] = layaNode;

            // 按 NODE_MAPPING.inputs（Unity slot 名）映射到 bp.inputList index
            var slots = u.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    if (s == null || (int)s.NumOf("m_SlotType", -1) != 0) continue;
                    string slotName = s.StrOf("m_DisplayName") ?? s.StrOf("m_ShaderOutputName") ?? "";
                    int layaIdx = MapUnitySlotToLayaInputIndex(unityType, slotName, (int)s.NumOf("m_SlotId"));
                    if (layaIdx < 0 || layaIdx >= nodeInputList.Count) continue;
                    InEdge edge;
                    if (inputEdgeByUnitySlot.TryGetValue(s.StrOf("m_ObjectId"), out edge))
                        ConnectInput(layaNode, layaIdx, edge.FromNodeId, edge.FromSlotId);
                    else
                    {
                        var v = ExtractSlotDefault(s, nodeInputList.At(layaIdx).StrOf("type"));
                        if (v != null) nodeInputList.At(layaIdx).Set("defVal", v);
                    }
                }
            foreach (var inp in nodeInputList.Items)
            {
                if (inp.Get("info") != null) continue;
                if (HasConcreteDefVal(inp)) continue;
                inp.Set("defVal", SafeDefaultValue(inp.StrOf("type")));
            }
            return layaNode;
        }

        // ─────────────────────────────────────────────────────────────
        // _convertPropertyNodeAsInstance
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertPropertyNodeAsInstance(Jval u, Jval prop, string ptype, Pos pos)
        {
            string attrType = null;
            if (ptype == "Vector1ShaderProperty" || ptype == "FloatShaderProperty") attrType = "float";
            else if (ptype == "Vector2ShaderProperty") attrType = "vec2";
            else if (ptype == "Vector3ShaderProperty") attrType = "vec3";
            else if (ptype == "Vector4ShaderProperty") attrType = "vec4";
            else if (ptype == "ColorShaderProperty") attrType = "vec4";
            else if (ptype == "BooleanShaderProperty") attrType = "float";
            if (attrType == null) return null;

            string refName = RefNameOf(prop);
            SgPropEntry entry;
            if (!sgPropertyByRefName.TryGetValue(refName, out entry))
            {
                int slot = sgPropertyTable.Count;
                var def = prop.Has("m_Value") ? prop.Get("m_Value") : prop.Get("m_Default");
                entry = new SgPropEntry
                {
                    ReferenceName = refName,
                    DisplayName = prop.StrOf("m_Name") ?? refName,
                    PropertyType = ptype,
                    Slot = slot,
                    Type = attrType,
                    Attribute = "a_AttrSGProp" + slot,
                    Varying = "v_AttrSGProp" + slot,
                    Default = SgPropertyDefaultValue(attrType, def),
                };
                sgPropertyTable.Add(entry);
                sgPropertyByRefName[refName] = entry;
            }

            int layaId = NextId();
            var layaNode = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", "function/custom")
                .Set("id", layaId)
                .Set("inputList", Jval.Arr())
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", attrType)))
                .Set("propertyVal", Jval.Obj()
                    .Set("code", "return " + entry.Varying + ";")
                    .Set("inputList", Jval.Arr())
                    .Set("outputList", Jval.Arr())
                    .Set("type", attrType)
                    .Set("defines", Jval.Arr())
                    .Set("includes", sgIncludeRelPath != null ? Jval.Arr(Jval.Of(sgIncludeRelPath)) : Jval.Arr()))
                .Set("select", false);
            layaArrRef.Add(layaNode);
            unityToLaya[u.StrOf("m_ObjectId")] = layaNode;
            return layaNode;
        }

        private Jval SgPropertyDefaultValue(string type, Jval raw)
        {
            if (type == "float") return Jval.Of(raw != null && raw.IsNumber ? raw.Num : 0);
            if (type == "vec2") return Vec2(NumField(raw, "x"), NumField(raw, "y"));
            if (type == "vec3") return Vec3(NumField(raw, "x"), NumField(raw, "y"), NumField(raw, "z"));
            if (type == "vec4")
            {
                if (raw != null && raw.Has("r"))
                    return Vec4(NumField(raw, "r"), NumField(raw, "g"), NumField(raw, "b"), NumField(raw, "a"));
                return Vec4(NumField(raw, "x"), NumField(raw, "y"), NumField(raw, "z"), NumField(raw, "w"));
            }
            return Jval.Of(0);
        }

        /// <summary>生成 SG 属性到实例 attribute 绑定的 GLSL include 文本（--sg-instanced 模式）。</summary>
        public string GenerateSgPropertyGlsl()
        {
            if (sgPropertyTable.Count == 0) return null;
            var lines = new List<string>();
            lines.Add("// Auto-generated by LayaAir3.0UnityPlugin ShaderGraphConverter (--sg-instanced)");
            lines.Add("// SG property → instance attribute bindings");
            lines.Add("// DO NOT EDIT — regenerate via the converter");
            lines.Add("");
            foreach (var e in sgPropertyTable)
                lines.Add("// slot " + e.Slot + ": " + e.ReferenceName + " (" + e.PropertyType + ", " + e.Type + ", default=" + e.Default.Serialize(0) + ")");
            lines.Add("");
            lines.Add("#if defined(VFX_INSTANCED)");
            lines.Add("    // VFX 模式：每粒子从 vertex attribute 读，通过 varying 传到 fragment");
            lines.Add("    #if !defined(SHADER_FS)");
            foreach (var e in sgPropertyTable) lines.Add("    attribute " + e.Type + " " + e.Attribute + ";");
            lines.Add("    #endif");
            foreach (var e in sgPropertyTable) lines.Add("    varying " + e.Type + " " + e.Varying + ";");
            lines.Add("");
            lines.Add("    // vs main 必须调用此宏才能让 attribute → varying 链路生效");
            lines.Add("    // （由 IDE / runtime 端在 vertex shader 主函数内注入 initSGProps()）");
            lines.Add("    #if !defined(SHADER_FS)");
            lines.Add("    void initSGProps() {");
            foreach (var e in sgPropertyTable) lines.Add("        " + e.Varying + " = " + e.Attribute + ";");
            lines.Add("    }");
            lines.Add("    #else");
            lines.Add("    void initSGProps() {}");
            lines.Add("    #endif");
            lines.Add("#else");
            lines.Add("    // 非 VFX 模式：使用 default 值（保持单实例渲染兼容）");
            foreach (var e in sgPropertyTable)
                lines.Add("    const " + e.Type + " " + e.Varying + " = " + GlslDefaultLiteral(e.Type, e.Default) + ";");
            lines.Add("    void initSGProps() {}");
            lines.Add("#endif");
            lines.Add("");
            return string.Join("\n", lines.ToArray());
        }

        private static string GlslDefaultLiteral(string type, Jval def)
        {
            Func<double, string> f = n =>
            {
                string s = Jval.ShortestRoundTrip(n);
                return s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0 ? s + ".0" : s;
            };
            if (type == "float") return f(def.AsNum());
            if (type == "vec2") return "vec2(" + f(def.NumOf("x")) + "," + f(def.NumOf("y")) + ")";
            if (type == "vec3") return "vec3(" + f(def.NumOf("x")) + "," + f(def.NumOf("y")) + "," + f(def.NumOf("z")) + ")";
            if (type == "vec4") return "vec4(" + f(def.NumOf("x")) + "," + f(def.NumOf("y")) + "," + f(def.NumOf("z")) + "," + f(def.NumOf("w")) + ")";
            return "0.0";
        }

        // ─────────────────────────────────────────────────────────────
        // _convertVertexColorWithAdapter
        // ─────────────────────────────────────────────────────────────
        private Jval ConvertVertexColorWithAdapter(Jval u)
        {
            var pos = UiPos(u);
            int vcId = NextId();
            var vcNode = Jval.Obj()
                .Set("x", pos.X - 200).Set("y", pos.Y)
                .Set("constDataID", "inputdata/vertex/VertexColor")
                .Set("id", vcId)
                .Set("inputList", Jval.Arr())
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "vec4")))
                .Set("select", false);
            layaArrRef.Add(vcNode);

            int adapterId = NextId();
            var adapter = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("ver", 1)
                .Set("constDataID", "basic/Vector4")
                .Set("id", adapterId)
                .Set("inputList", Jval.Arr(
                    Jval.Obj().Set("type", "vec4").Set("info", Info(vcId, 0)),
                    Jval.Obj().Set("type", "float").Set("defVal", 0),
                    Jval.Obj().Set("type", "float").Set("defVal", 0),
                    Jval.Obj().Set("type", "float").Set("defVal", 0),
                    Jval.Obj().Set("type", "float").Set("defVal", 0)))
                .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XYZW").Set("type", "vec4")))
                .Set("select", false);
            layaArrRef.Add(adapter);

            vcNode.Get("outputList").At(0).Set("infoList", Jval.Arr(Info(adapterId, 0)));
            unityToLaya[u.StrOf("m_ObjectId")] = adapter;
            return adapter;
        }

        private Jval CreateConstFloat(double v, Pos pos)
        {
            int layaId = NextId();
            var node = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("constDataID", "basic/Float").Set("id", layaId)
                .Set("inputList", Jval.Arr(Jval.Obj().Set("type", "float").Set("defVal", v)))
                .Set("outputList", Jval.Arr(Jval.Obj().Set("type", "float")))
                .Set("select", false);
            layaArrRef.Add(node);
            return node;
        }

        private Jval CreateConstVec2(Jval v, Pos pos)
        {
            int layaId = NextId();
            double vx = v != null ? v.NumOf("x") : 0;
            double vy = v != null ? v.NumOf("y") : 0;
            var node = Jval.Obj()
                .Set("x", pos.X).Set("y", pos.Y)
                .Set("ver", 1).Set("constDataID", "basic/Vector2").Set("id", layaId)
                .Set("inputList", Jval.Arr(
                    Jval.Obj().Set("type", "vec2").Set("defVal", Vec2(vx, vy)),
                    Jval.Obj().Set("type", "float").Set("defVal", vx),
                    Jval.Obj().Set("type", "float").Set("defVal", vy)))
                .Set("outputList", Jval.Arr(Jval.Obj().Set("name", "XY").Set("type", "vec2")))
                .Set("select", false);
            layaArrRef.Add(node);
            return node;
        }

        private void CleanupOrphanInjectedUV()
        {
            var referenced = new HashSet<int>();
            foreach (var n in layaArrRef)
            {
                var il = n.Get("inputList");
                if (il != null)
                    foreach (var inp in il.Items)
                    {
                        var info = inp.Get("info");
                        if (info != null && info.Get("id") != null) referenced.Add((int)info.NumOf("id"));
                    }
            }
            var kept = new List<Jval>();
            foreach (var n in layaArrRef)
            {
                bool injUV = n.Get("_injUV") != null && n.Get("_injUV").AsBool();
                if (injUV && !referenced.Contains((int)n.NumOf("id"))) continue;
                kept.Add(n);
            }
            foreach (var n in kept) n.Remove("_injUV");
            layaArrRef.Clear();
            layaArrRef.AddRange(kept);
        }

        private Pos UiPos(Jval u)
        {
            var ds = u != null ? u.Get("m_DrawState") : null;
            var p = ds != null ? ds.Get("m_Position") : null;
            if (p != null && p.Get("x") != null && p.Get("x").IsNumber)
                return new Pos(p.NumOf("x"), p.NumOf("y"));
            var pos = new Pos(layout.X, layout.Y);
            layout.Y += 200;
            if (layout.Y > 1200) { layout.Y = 0; layout.X += 300; }
            return pos;
        }

        private static Jval SafeDefaultValue(string type)
        {
            switch (type)
            {
                case "float":
                case "int": return Jval.Of(0);
                case "bool": return Jval.Of(false);
                case "vec2": return Vec2(0, 0);
                case "vec3": return Vec3(0, 0, 0);
                case "vec4": return Vec4(0, 0, 0, 0);
                case "color": return Col(1, 1, 1, 1);
                case "sampler2D":
                case "samplerCube": return Jval.Of("_remove_");
                default: return Jval.Of(0);
            }
        }

        private static double NumField(Jval v, string k) { return v != null ? v.NumOf(k, 0) : 0; }
        private static Jval ToVec2(Jval v) { return Vec2(NumField(v, "x"), NumField(v, "y")); }
        private static Jval ToVec3(Jval v) { return Vec3(NumField(v, "x"), NumField(v, "y"), NumField(v, "z")); }
        private static Jval ToVec4(Jval v) { return Vec4(NumField(v, "x"), NumField(v, "y"), NumField(v, "z"), NumField(v, "w")); }
        // 返回 [r,g,b,a]
        private static double[] ToColor(Jval v)
        {
            if (v == null) return new double[] { 1, 1, 1, 1 };
            double r = v.Has("r") ? v.NumOf("r") : v.NumOf("x", 1);
            double g = v.Has("g") ? v.NumOf("g") : v.NumOf("y", 1);
            double b = v.Has("b") ? v.NumOf("b") : v.NumOf("z", 1);
            double a = v.Has("a") ? v.NumOf("a") : v.NumOf("w", 1);
            return new double[] { r, g, b, a };
        }

        // _extractSlotDefault：按目标 Laya type 适配；返回 null 表示不设值
        private Jval ExtractSlotDefault(Jval s, string targetLayaType)
        {
            var v = s.Has("m_Value") ? s.Get("m_Value") : s.Get("m_DefaultValue");
            if (v == null || v.IsNull) return null;
            if (v.IsNumber || v.IsBool)
            {
                // 注意：与 JS 一致，bool 喂 vec 目标时输出布尔字面量分量（{x:true,y:true}），不做数字化
                if (v.IsBool)
                {
                    bool bv = v.Bool;
                    if (targetLayaType == "vec2") return Jval.Obj().Set("x", bv).Set("y", bv);
                    if (targetLayaType == "vec3") return Jval.Obj().Set("x", bv).Set("y", bv).Set("z", bv);
                    if (targetLayaType == "vec4") return Jval.Obj().Set("x", bv).Set("y", bv).Set("z", bv).Set("w", bv);
                    if (targetLayaType == "color") return Jval.Obj().Set("r", bv).Set("g", bv).Set("b", bv).Set("a", 1);
                    return Jval.Of(bv);
                }
                double n = v.Num;
                if (targetLayaType == "vec2") return Vec2(n, n);
                if (targetLayaType == "vec3") return Vec3(n, n, n);
                if (targetLayaType == "vec4") return Vec4(n, n, n, n);
                if (targetLayaType == "color") return Col(n, n, n, 1);
                return Jval.Of(n);
            }
            if (!v.IsObject) return null;
            if (v.Has("e00"))
            {
                if (targetLayaType == "float") return Jval.Of(v.NumOf("e00"));
                return null;
            }
            switch (targetLayaType)
            {
                case "float": return Jval.Of(v.Has("x") ? v.NumOf("x") : (v.Has("r") ? v.NumOf("r") : 0));
                case "vec2": return ToVec2(v);
                case "vec3": return ToVec3(v);
                case "vec4": return ToVec4(v);
                case "color": { var c = ToColor(v); return Col(c[0], c[1], c[2], c[3]); }
                case "bool": return Jval.Of((v.NumOf("x") != 0) || (v.NumOf("r") != 0));
                default:
                    if (v.Has("r")) { var c = ToColor(v); return Col(c[0], c[1], c[2], c[3]); }
                    if (v.Has("w")) return ToVec4(v);
                    if (v.Has("z")) return ToVec3(v);
                    if (v.Has("y")) return ToVec2(v);
                    return null;
            }
        }

        private int MapUnitySlotToLayaInputIndex(string unityType, string slotName, int slotId)
        {
            NodeMap map;
            if (!SgNodeMapping.NODE_MAPPING.TryGetValue(unityType, out map)) return -1;
            var list = map.Inputs ?? new string[0];
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == slotName) return i;
                if (Norm(list[i]) == Norm(slotName)) return i;
            }
            return -1;
        }

        private int MapUnityOutputSlotToLaya(string unityNodeId, int slotId)
        {
            Jval u;
            if (!unityIdToObj.TryGetValue(unityNodeId, out u)) return 0;
            string t = ShortType(u);
            NodeMap map;
            SgNodeMapping.NODE_MAPPING.TryGetValue(t, out map);
            string slotName = null;
            var slots = u.Get("m_Slots");
            if (slots != null)
                foreach (var sref in slots.Items)
                {
                    var s = Idx.GetById(sref.StrOf("m_Id"));
                    // 注意：Unity SG MaterialSlot 的本地 slot id 字段是 m_Id（不是 m_SlotId）。
                    if (s != null && (int)s.NumOf("m_SlotType", -1) == 1 && (int)s.NumOf("m_Id") == slotId)
                    {
                        slotName = s.StrOf("m_DisplayName") ?? s.StrOf("m_ShaderOutputName");
                        break;
                    }
                }
            if (map == null || slotName == null) return 0;
            var list = map.Outputs ?? new string[0];
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == slotName) return i;
                if (Norm(list[i]) == Norm(slotName)) return i;
            }
            return 0;
        }

        private static string GuessInputType(string unityType, string inputName)
        {
            string n = (inputName ?? "").ToLowerInvariant();
            if (n == "uv" || n == "tiling" || n == "offset") return "vec2";
            if (n == "color" || n == "rgba") return "color";
            if (n == "texture" || n == "sampler") return "sampler2D";
            if (n == "normal" || n == "view dir") return "vec3";
            return "float";
        }

        private static string GuessOutputType(string unityType, string outputName)
        {
            if (unityType == "ColorNode") return "vec4";
            if (unityType == "Vector4Node") return "vec4";
            if (unityType == "Vector3Node") return "vec3";
            if (unityType == "Vector2Node") return "vec2";
            if (unityType == "UVNode") return "vec2";
            if (unityType == "TilingAndOffsetNode") return "vec2";
            if (unityType == "ScreenPositionNode") return "vec2";
            if (unityType == "SampleTexture2DNode") return outputName == "RGBA" ? "vec4" : "float";
            if (unityType == "SplitNode") return "float";
            if (unityType == "VertexColorNode") return "vec4";
            if (unityType == "PositionNode") return "vec3";
            if (unityType == "NormalVectorNode") return "vec3";
            if (unityType == "TangentVectorNode") return "vec3";
            if (unityType == "ViewDirectionNode") return "vec3";
            if (unityType == "CameraNode") return "vec3";
            if (unityType == "NormalizeNode" || unityType == "ReflectionNode" || unityType == "CrossProductNode") return "vec3";
            if (unityType == "NormalStrengthNode") return "vec3";
            if (unityType == "NormalBlendNode" || unityType == "NormalReconstructZNode") return "vec3";
            return "float";
        }

        private static string RefNameOf(Jval prop)
        {
            // 注意：与 JS 一致，override 名返回 trim 后的串；空串(含全空白/空 default)按 falsy 继续兜底
            string overrideName = prop.StrOf("m_OverrideReferenceName");
            if (overrideName != null && overrideName.Trim() != "") return overrideName.Trim();
            string def = prop.StrOf("m_DefaultReferenceName");
            if (!string.IsNullOrEmpty(def)) return def;
            return "_" + (prop.StrOf("m_Name") ?? "Prop");
        }

        private static Jval DeepClone(Jval v)
        {
            if (v == null) return null;
            switch (v.Type)
            {
                case Jval.JType.Object:
                {
                    var o = Jval.Obj();
                    foreach (var k in v.Keys) o.Set(k, DeepClone(v.Get(k)));
                    return o;
                }
                case Jval.JType.Array:
                {
                    var a = Jval.Arr();
                    foreach (var e in v.Items) a.Push(DeepClone(e));
                    return a;
                }
                case Jval.JType.Number: return Jval.Of(v.Num);
                case Jval.JType.String: return Jval.Of(v.Str);
                case Jval.JType.Bool: return Jval.Of(v.Bool);
                default: return Jval.Null();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // buildBpsJson
        // ─────────────────────────────────────────────────────────────
        private Jval BuildBpsJson()
        {
            CleanupOrphanInjectedUV();
            var arr = Jval.Arr();
            foreach (var n in layaArrRef) arr.Push(n);

            var material = Jval.Obj()
                .Set("twoSided", false)
                .Set("blendModes", 0)
                .Set("materialType", shaderType == "unlit" ? 2 : 0)
                .Set("anisotropy", false).Set("clear coat", false).Set("sheen", false)
                .Set("transmission", false).Set("iridescence", false)
                .Set("scene fog", false).Set("alpha test", alphaClip)
                .Set("supportReflectionProbe", true).Set("enableInstancing", true);
            if (isVFXTarget || forceSupportVFX) material.Set("supportVFX", true);

            var uArr = Jval.Arr();
            foreach (var u in uniformArr) uArr.Push(u);

            var outJson = Jval.Obj()
                .Set("arr", arr)
                .Set("x", 0).Set("y", 0).Set("scale", 1)
                .Set("bluePrintNum", bluePrintNum)
                .Set("material", material)
                .Set("uniformData", Jval.Obj().Set("uniformArr", uArr))
                .Set("customInclude", Jval.Null())
                .Set("version", (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (KeywordDefines.Count > 0)
            {
                var onDefines = Jval.Arr();
                foreach (var kv in KeywordDefines) if (kv.Value) onDefines.Push(Jval.Of(kv.Key));
                if (onDefines.Count > 0) material.Set("defines", onDefines);
                var kd = Jval.Arr();
                foreach (var kv in KeywordDefines) kd.Push(Jval.Obj().Set("name", kv.Key).Set("defaultOn", kv.Value));
                outJson.Set("keywordDefines", kd);
            }

            if (sgPropertyTable.Count > 0)
            {
                var bindings = Jval.Arr();
                foreach (var e in sgPropertyTable)
                    bindings.Push(Jval.Obj()
                        .Set("referenceName", e.ReferenceName)
                        .Set("displayName", e.DisplayName)
                        .Set("slot", e.Slot)
                        .Set("type", e.Type)
                        .Set("attribute", e.Attribute)
                        .Set("varying", e.Varying)
                        .Set("default", e.Default));
                outJson.Set("sgPropertyBindings", bindings);
                if (sgIncludeRelPath != null)
                    outJson.Set("customInclude", Jval.Arr(Jval.Of(sgIncludeRelPath)));
            }
            return outJson;
        }

        public bool HasSgProperties { get { return sgPropertyTable.Count > 0; } }
        public int LayaNodeCount { get { return layaArrRef.Count; } }
    }
}
