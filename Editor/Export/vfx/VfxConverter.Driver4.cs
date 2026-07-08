using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 驱动第四部分：custom attrs + properties[] + link pass（op→op）+ 输出组装 + Convert() 入口。
    /// 移植自 unity-vfx-to-laya.js L4733-4743 / L5129-5191 / L4759-4925 / L5724-5763。
    ///
    /// operator→operator 与 operator→block link 均忠实移植，
    /// 含 block input slot 注册 / GPU-CPU event chain / subgraph 内联 / post-fix 群
    /// （对拍基线：UNI 132 + Learning Templates 33 + VFXSamples 40 全部与 JS 逐字节一致）。
    /// </summary>
    public partial class VfxConverter
    {
        public readonly List<Jval> LayaProperties = new List<Jval>();
        public readonly List<Jval> LayaCustomAttributes = new List<Jval>();
        public readonly List<Jval> LayaEvents = new List<Jval>();

        /// <summary>收集 VFXCustomAttributeDescriptor，生成 Laya 自定义属性表。</summary>
        public void BuildCustomAttributes()
        {
            foreach (var e in Entries)
            {
                if (e.ClassType != "VFXCustomAttributeDescriptor") continue;
                string rawName = UnityYamlParser.GetStringField(e.Body, "m_AttributeName");
                if (rawName == null) continue;
                int typeIdx = UnityYamlParser.GetIntField(e.Body, "m_Type") ?? 0;
                string layaType = (typeIdx >= 0 && typeIdx < VfxMaps.CUSTOM_ATTR_TYPE_MAP.Length) ? VfxMaps.CUSTOM_ATTR_TYPE_MAP[typeIdx] : "float";
                LayaCustomAttributes.Add(Jval.Obj().Set("name", VfxMaps.NormalizeAttrName(rawName)).Set("type", layaType));
            }
        }

        /// <summary>把暴露的 VFXParameter 转成 Laya properties[]（含范围/默认值/类型映射）。</summary>
        public void BuildProperties()
        {
            foreach (var e in Entries)
            {
                if (e.ClassType != "VFXParameter") continue;
                if (e.InlinedFromSub) continue;   // sub 的 VFXParameter 不算用户暴露 property（对应 JS L5132）
                string exposedName = UnityYamlParser.GetStringField(e.Body, "m_ExposedName");
                if (exposedName == null) continue;
                bool exposed = (UnityYamlParser.GetIntField(e.Body, "m_Exposed") ?? 0) == 1;
                var outputSlots = UnityYamlParser.GetRefArrayField(e.Body, "m_OutputSlots");
                var slot = outputSlots.Count > 0 ? Get(outputSlots[0]) : null;
                string slotType = UnityYamlParser.GetSlotType(slot);
                var defaultValue = UnityYamlParser.GetSlotInlineValue(slot);
                string layaType = VfxHelpers.UnityTypeToLayaPropType(slotType);
                if (layaType == "Transform") continue;
                var prop = Jval.Obj().Set("name", VfxHelpers.SanitizePropName(exposedName)).Set("type", layaType).Set("exposed", exposed);
                string category = UnityYamlParser.GetStringField(e.Body, "m_Category");
                if (category != null) prop.Set("group", category);
                if ((layaType == "float" || layaType == "int") && (UnityYamlParser.GetIntField(e.Body, "m_ValueFilter") ?? 0) == 1)
                {
                    var rmin = UnityYamlParser.GetNestedSerializableNumber(e.Body, "m_Min");
                    var rmax = UnityYamlParser.GetNestedSerializableNumber(e.Body, "m_Max");
                    if (rmin != null && rmax != null && rmax.Value > rmin.Value)
                        prop.Set("range", Jval.Arr(Jval.Of(rmin.Value), Jval.Of(rmax.Value)));
                }
                if (defaultValue != null && !defaultValue.IsNull)
                {
                    if (Regex.IsMatch(layaType, "^(Texture2D|Texture3D|TextureCube|Mesh)$")
                        && defaultValue.IsObject && defaultValue.Get("obj") != null && defaultValue.Get("obj").StrOf("guid") != null)
                    {
                        var resolved = ResolveResourceRef(defaultValue);
                        if (resolved != null) prop.Set("default", Jval.Arr(Jval.Of(resolved)));
                    }
                    else if (layaType == "Gradient" && defaultValue.IsObject && (defaultValue.Get("colorKeys") != null || defaultValue.Get("alphaKeys") != null))
                        prop.Set("default", Jval.Obj().Set("stops", VfxCurveGradient.UnityGradientToLayaStops(defaultValue)));
                    else prop.Set("default", defaultValue);
                }
                else
                {
                    if (layaType == "vec2") prop.Set("default", Jval.Obj().Set("x", 0).Set("y", 0));
                    else if (layaType == "vec3") prop.Set("default", Jval.Obj().Set("x", 0).Set("y", 0).Set("z", 0));
                    else if (layaType == "vec4") prop.Set("default", Jval.Obj().Set("x", 0).Set("y", 0).Set("z", 0).Set("w", 0));
                    else if (layaType == "color") prop.Set("default", Jval.Obj().Set("r", 0).Set("g", 0).Set("b", 0).Set("a", 1));
                }
                LayaProperties.Add(prop);
            }
        }

        /// <summary>link pass：op→op 与 op→block 连线（对应 JS L4759-4925，含 subgraph alias 透传）。</summary>
        public void LinkOperators()
        {
            foreach (var op in OperatorEntries)
            {
                int? sourceLayaId = null;
                { int v; if (FileIDToLayaId.TryGetValue(op.FileID, out v)) sourceLayaId = v; }
                if (sourceLayaId == null) continue;
                var layaOp = FindOp(sourceLayaId.Value);
                if (layaOp == null) continue;
                var outputSlots = UnityYamlParser.GetRefArrayField(op.Body, "m_OutputSlots");
                for (int oi = 0; oi < outputSlots.Count; oi++)
                {
                    var allLinked = new List<KeyValuePair<string, string>>(); // (linkedSlotID, swizzle)
                    Action<string, string> collect = null;
                    collect = (slotID, swizzle) =>
                    {
                        var entry = Get(slotID);
                        if (entry == null) return;
                        foreach (var lid in UnityYamlParser.GetLinkedSlots(entry)) allLinked.Add(new KeyValuePair<string, string>(lid, swizzle));
                        foreach (var childID in UnityYamlParser.GetRefArrayField(entry.Body, "m_Children"))
                        {
                            var cEntry = Get(childID);
                            string cName = cEntry != null ? UnityYamlParser.GetSlotPropertyName(cEntry) : null;
                            string childSwizzle = (cName != null && Regex.IsMatch(cName, "^[xyzw]$", RegexOptions.IgnoreCase)) ? cName.ToLowerInvariant() : swizzle;
                            collect(childID, childSwizzle);
                        }
                    };
                    collect(outputSlots[oi], "");
                    if (allLinked.Count == 0) continue;
                    var outputs = Defs.GetOutputs(layaOp.StrOf("typeId"));
                    string sourceSlotNameBase = (outputs != null && oi < outputs.Length) ? outputs[oi]
                        : layaOp.StrOf("typeId") == "randomNumber" ? "out"
                        : layaOp.StrOf("typeId").StartsWith("inline") ? "value" : "out";
                    var slotsByName = new Dictionary<string, List<Jval>>();
                    foreach (var link in allLinked)
                    {
                        string swizzle = link.Value ?? "";
                        string sourceSlotName = swizzle != "" ? sourceSlotNameBase + "_" + swizzle : sourceSlotNameBase;
                        List<Jval> infoArr;
                        if (!slotsByName.TryGetValue(sourceSlotName, out infoArr)) { infoArr = new List<Jval>(); slotsByName[sourceSlotName] = infoArr; }
                        // 透传 subgraph caller 的 input/output slot 别名（对应 JS L4801 resolveSlotAliases）
                        foreach (var aliasLid in ResolveSlotAliases(link.Key))
                        {
                        SlotRef target;
                        if (!SlotToOpId.TryGetValue(aliasLid, out target)) continue;
                        if (target.BlockId != 0)
                        {
                            // op→block
                            string blockSlotId = "value";
                            string spn = target.SlotPropName != null ? target.SlotPropName.ToLowerInvariant() : "";
                            string lname = Regex.Replace(spn, "_(vector|position|direction)_", "_");
                            var tblock = FindBlock(target.CtxId, target.BlockId);
                            string tbType = tblock != null ? tblock.StrOf("typeId") : null;
                            if (lname == "intensity") blockSlotId = "intensity";
                            else if (lname == "force") blockSlotId = "force";
                            else if (Regex.IsMatch(lname, "^force_[xyz]$")) blockSlotId = lname;
                            else if (lname == "heightsequencer") blockSlotId = "heightSequencer";
                            else if (lname == "arcsequencer") blockSlotId = "arcSequencer";
                            else if (lname == "linesequencer") blockSlotId = "lineSequencer";
                            else if (Regex.IsMatch(lname, "^[ab](_[xyzw])?$")) { if (tbType == "setAttribute") blockSlotId = lname.StartsWith("a") ? "value" : "b_value"; }
                            else if (Regex.IsMatch(lname, "(^|_)radius$")) { if (tbType == "setPositionShape") blockSlotId = "radius"; }
                            else if (target.SlotPropName != null && Regex.IsMatch(target.SlotPropName, "^[a-z][a-zA-Z0-9]*$")) blockSlotId = target.SlotPropName;
                            string targetSlotId = "block_" + target.BlockId + "_" + blockSlotId;
                            string effSw = swizzle;
                            if (effSw == "" && (blockSlotId == "value" || blockSlotId == "b_value") && target.SlotPropName != null)
                            { var m = Regex.Match(target.SlotPropName.ToLowerInvariant(), "_([xyzw])$"); if (m.Success) effSw = m.Groups[1].Value; }
                            if (effSw != "" && Regex.IsMatch(effSw, "^[xyzw]$") && (blockSlotId == "value" || blockSlotId == "b_value") && tblock != null
                                && (tbType == "setAttribute" || tbType == "setAttributeCurve" || tbType == "attributeFromMap"))
                            {
                                string attrName = tblock.Get("props") != null ? tblock.Get("props").StrOf("attribute") : null;
                                string attrType = CustomAttrType(attrName) ?? (attrName != null ? BuiltinAttrHint(attrName) : null);
                                if (attrType == "vec2" || attrType == "vec3" || attrType == "vec4" || attrType == "color") targetSlotId += "_" + effSw;
                            }
                            infoArr.Add(Jval.Obj().Set("nodeId", target.CtxId).Set("slotId", targetSlotId));
                        }
                        else if (target.Dir == "in" && target.OpTypeId != null)
                        {
                            if (target.IsResource) continue;
                            string realSlotId = target.RealSlotId;
                            if (realSlotId == null)
                            {
                                var inputs = Defs.GetInputs(target.OpTypeId);
                                if (inputs != null && target.SlotIdx < inputs.Length) realSlotId = inputs[target.SlotIdx];
                                else if (target.SlotIdx >= 2 && (target.OpTypeId == "multiply" || target.OpTypeId == "add"))
                                { string[] NARY = { "a", "b", "c", "d", "e", "f", "g", "h" }; realSlotId = target.SlotIdx < NARY.Length ? NARY[target.SlotIdx] : "a"; }
                                else realSlotId = "a";
                            }
                            infoArr.Add(Jval.Obj().Set("nodeId", target.LayaId).Set("slotId", realSlotId));
                        }
                        }   // end alias foreach
                    }
                    var output = layaOp.Get("output");
                    foreach (var kv in slotsByName)
                    {
                        if (kv.Value.Count == 0) continue;
                        var slotObj = output.Get(kv.Key);
                        if (slotObj == null) { slotObj = Jval.Obj().Set("infoArr", Jval.Arr()); output.Set(kv.Key, slotObj); }
                        var infoArrJ = slotObj.Get("infoArr");
                        foreach (var info in kv.Value) infoArrJ.Push(info);
                    }
                }
            }
        }

        public static Jval ToArr(List<Jval> list) { var a = Jval.Arr(); foreach (var x in list) a.Push(x); return a; }

        /// <summary>IDE 编译器 bug 规避：处理 setAttributeCurve 块（gradient 转 IDE 格式 / vec 曲线删除，对应 JS L5292-5418）。</summary>
        public void ApplyIdeWorkaround()
        {
            var VEC_ATTRS = new HashSet<string> { "position", "velocity", "scale", "axisX", "axisY", "axisZ" };
            Action<int, int> severIncomingLinks = (blockId, ctxId) =>
            {
                string target = "block_" + blockId + "_value";
                foreach (var op in LayaOperators)
                {
                    var outp = op.Get("output"); if (outp == null) continue;
                    foreach (var slotName in new List<string>(outp.Keys))
                    {
                        var slot = outp.Get(slotName);
                        var infoArr = slot != null ? slot.Get("infoArr") : null;
                        if (infoArr == null || !infoArr.IsArray) continue;
                        infoArr.Items.RemoveAll(c => (int)c.NumOf("nodeId") == ctxId && c.StrOf("slotId") == target);
                    }
                }
            };
            foreach (var ctx in LayaContexts)
            {
                var blocks = ctx.Get("blocks"); if (blocks == null) continue;
                int ctxId = (int)ctx.NumOf("id");
                string ctxType = ctx.StrOf("typeId");
                for (int i = blocks.Count - 1; i >= 0; i--)
                {
                    var b = blocks.At(i);
                    if (b.StrOf("typeId") != "setAttributeCurve") continue;
                    var bp = b.Get("props");
                    string attr = bp != null ? bp.StrOf("attribute") : null;
                    bool hasGradient = bp != null && bp.Get("gradient") != null;
                    if (attr == "color" && hasGradient)
                    {
                        bool isCustom = bp.StrOf("sampleMode") == "Custom";
                        if (isCustom)
                        {
                            int newOpId = NewId();
                            var stops = VfxCurveGradient.UnityGradientToLayaStops(bp.Get("gradient"));
                            var newGradOp = Jval.Obj().Set("id", newOpId).Set("typeId", "sampleGradient")
                                .Set("uiData", Jval.Obj().Set("x", 0).Set("y", 0))
                                .Set("props", Jval.Obj().Set("gradient", Jval.Obj().Set("stops", stops)));
                            LayaOperators.Add(newGradOp);
                            string oldComp = bp.StrOf("composition") ?? "Overwrite";
                            int bid = (int)b.NumOf("id");
                            blocks.Items[i] = Jval.Obj().Set("id", bid).Set("typeId", "setAttribute").Set("enabled", b.Get("enabled") == null || b.BoolOf("enabled"))
                                .Set("props", Jval.Obj().Set("attribute", "color").Set("source", "Slot").Set("composition", oldComp)
                                    .Set("random", "Off").Set("channels", 7).Set("_values", Jval.Obj().Set("r", 1).Set("g", 1).Set("b", 1).Set("a", 1)));
                            string targetSlot = "block_" + bid + "_value";
                            var subSlots = new HashSet<string> { targetSlot + "_x", targetSlot + "_y", targetSlot + "_z", targetSlot + "_w" };
                            foreach (var op in LayaOperators)
                            {
                                var outp = op.Get("output"); if (outp == null) continue;
                                foreach (var slotName in outp.Keys)
                                {
                                    var slot = outp.Get(slotName);
                                    var infoArr = slot != null ? slot.Get("infoArr") : null;
                                    if (infoArr == null || !infoArr.IsArray) continue;
                                    foreach (var c in infoArr.Items)
                                        if ((int)c.NumOf("nodeId") == ctxId && (c.StrOf("slotId") == targetSlot || subSlots.Contains(c.StrOf("slotId"))))
                                        { c.Set("nodeId", newOpId); c.Set("slotId", "t"); }
                                }
                            }
                            newGradOp.Set("output", Jval.Obj().Set("out", Jval.Obj().Set("infoArr", Jval.Arr(Jval.Obj().Set("nodeId", ctxId).Set("slotId", targetSlot)))));
                        }
                        else if (Regex.IsMatch(ctxType ?? "", "^output(Billboard|Mesh|Trail|ComposedParticle|LineStrip)$") || ctxType == "initialize" || ctxType == "update")
                        {
                            var stops = VfxCurveGradient.UnityGradientToLayaStops(bp.Get("gradient"));
                            bp.Set("gradient", VfxCurveGradient.StopsToIdeGradient(stops));
                        }
                        else { severIncomingLinks((int)b.NumOf("id"), ctxId); blocks.Items.RemoveAt(i); }
                    }
                    else if (attr == "scale") { /* 保留 per-channel */ }
                    else if (VEC_ATTRS.Contains(attr)) { severIncomingLinks((int)b.NumOf("id"), ctxId); blocks.Items.RemoveAt(i); }
                    else if (attr == "color" && !hasGradient) { severIncomingLinks((int)b.NumOf("id"), ctxId); blocks.Items.RemoveAt(i); }
                }
            }
        }

        /// <summary>把 setPositionShape/positionSDF/setPositionMesh 从 output ctx 移到对应 init ctx（对应 JS L5960-5999）。</summary>
        public void RerouteePositionBlocks()
        {
            var POS_INIT = new HashSet<string> { "setPositionShape", "positionSDF", "setPositionMesh" };
            foreach (var oCtx in LayaContexts)
            {
                if (!(oCtx.StrOf("typeId") ?? "").StartsWith("output")) continue;
                var oBlocks = oCtx.Get("blocks"); if (oBlocks == null) continue;
                var movable = new List<Jval>();
                foreach (var b in oBlocks.Items) if (POS_INIT.Contains(b.StrOf("typeId"))) movable.Add(b);
                if (movable.Count == 0) continue;
                int oId = (int)oCtx.NumOf("id");
                Jval initCtx = null;
                foreach (var uCtx in LayaContexts)
                {
                    if (uCtx.StrOf("typeId") != "update") continue;
                    var ufl = uCtx.Get("flowLinks"); if (ufl == null) continue;
                    var targets = ufl.Get("output"); if (targets == null) continue;
                    var tArr = targets.IsArray ? targets.Items : new List<Jval> { targets };
                    bool hit = false; foreach (var t in tArr) if ((int)t.NumOf("targetId") == oId) { hit = true; break; }
                    if (!hit) continue;
                    int uId = (int)uCtx.NumOf("id");
                    foreach (var iCtx in LayaContexts)
                    {
                        if (iCtx.StrOf("typeId") != "initialize") continue;
                        var ifl = iCtx.Get("flowLinks"); if (ifl == null) continue;
                        var initOut = ifl.Get("output");
                        if (initOut != null && !initOut.IsArray && (int)initOut.NumOf("targetId") == uId) { initCtx = iCtx; break; }
                    }
                    if (initCtx != null) break;
                }
                if (initCtx == null) continue;
                var iBlocks = initCtx.Get("blocks"); if (iBlocks == null) { iBlocks = Jval.Arr(); initCtx.Set("blocks", iBlocks); }
                foreach (var b in movable)
                {
                    bool exists = false;
                    foreach (var eb in iBlocks.Items)
                    {
                        string ebShape = eb.Get("props") != null ? eb.Get("props").StrOf("shape") : null;
                        string bShape = b.Get("props") != null ? b.Get("props").StrOf("shape") : null;
                        if (eb.StrOf("typeId") == b.StrOf("typeId") && ebShape == bShape) { exists = true; break; }
                    }
                    if (!exists) iBlocks.Push(b);
                }
                oBlocks.Items.RemoveAll(b => POS_INIT.Contains(b.StrOf("typeId")));
            }
        }

        private static readonly Regex UuidRe = new Regex(@"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
        private double? MeshScaleFactor(string meshUrl)
        {
            var m = UuidRe.Match(meshUrl); if (!m.Success) return null;
            string uuid = m.Value;
            double f;
            if (VfxMaps.SPECIAL_MESH_SCALE_UUIDS.TryGetValue(uuid, out f)) return f;
            string lmName; if (LmUuidToName.TryGetValue(uuid, out lmName) && VfxMaps.CM_UNIT_MESH_NAMES.Contains(lmName)) return 0.01;
            return null;
        }

        /// <summary>给 cm-unit mesh 输出 ctx 头部注入 setAttribute(size ×factor) block（对应 JS L6043-6080）。</summary>
        public void InjectMeshSizeFix()
        {
            var MESH_OUTPUT = new HashSet<string> { "outputShaderGraphMesh", "outputMesh", "outputComposedParticle" };
            foreach (var ctx in LayaContexts)
            {
                if (!MESH_OUTPUT.Contains(ctx.StrOf("typeId"))) continue;
                var props = ctx.Get("props");
                string meshUrl = props != null ? props.StrOf("mesh") : null;
                if (meshUrl == null) continue;
                var factor = MeshScaleFactor(meshUrl);
                if (factor == null) continue;
                var blocks = ctx.Get("blocks");
                if (blocks == null) { blocks = Jval.Arr(); ctx.Set("blocks", blocks); }
                bool already = false; foreach (var b in blocks.Items) if (b.Get("_meshSizeFix") != null) { already = true; break; }
                if (already) continue;
                // ⭐ 若输出已有 setAttribute size 块(如蜡烛 Overwrite 50.4),直接把它的值×factor:
                //    注入的 Multiply 块会被插到最前,但会被后面的 Overwrite 覆盖掉(×factor 失效)。
                //    对这种情况改成乘现有 size 值(幂等,标 _meshSizeFix)。CM_UNIT mesh 输出无 size 块→走下面注入。
                Jval existingSize = null;
                foreach (var b in blocks.Items)
                {
                    if (b.StrOf("typeId") != "setAttribute") continue;
                    var bp = b.Get("props");
                    if (bp != null && bp.StrOf("attribute") == "size") { existingSize = b; break; }
                }
                if (existingSize != null)
                {
                    var vals = existingSize.Get("props") != null ? existingSize.Get("props").Get("_values") : null;
                    if (vals != null)
                    {
                        double x = vals.NumOf("x"); double v = vals.NumOf("value");
                        vals.Set("x", x * factor.Value).Set("value", v * factor.Value);
                        existingSize.Set("_meshSizeFix", factor.Value);
                        continue;
                    }
                }
                // id = 当前所有 block 最大 id + 500（每次注入重算，对应 JS L6064）
                int maxId = 0;
                foreach (var c in LayaContexts) { var bs = c.Get("blocks"); if (bs != null) foreach (var b in bs.Items) { int id = (int)b.NumOf("id"); if (id > maxId) maxId = id; } }
                var newBlock = Jval.Obj()
                    .Set("id", maxId + 500)
                    .Set("typeId", "setAttribute").Set("enabled", true).Set("_meshSizeFix", factor.Value)
                    .Set("props", Jval.Obj().Set("attribute", "size").Set("source", "Slot").Set("composition", "Multiply")
                        .Set("random", "Off").Set("channels", 7)
                        .Set("_values", Jval.Obj().Set("x", factor.Value).Set("value", factor.Value)));
                blocks.Items.Insert(0, newBlock);
            }
        }

        /// <summary>给 setPositionMesh 块注入 meshScale（对应 JS L6087-6106）。</summary>
        public void InjectSetPositionMeshScale()
        {
            foreach (var ctx in LayaContexts)
            {
                var blocks = ctx.Get("blocks");
                if (blocks == null) continue;
                foreach (var b in blocks.Items)
                {
                    if (b.StrOf("typeId") != "setPositionMesh") continue;
                    var p = b.Get("props"); if (p == null) continue;
                    if (p.Get("meshScale") != null) continue;
                    string meshUrl = p.StrOf("mesh");
                    if (meshUrl == null || meshUrl.StartsWith("builtin:")) continue;
                    var factor = MeshScaleFactor(meshUrl);
                    if (factor == null) continue;
                    p.Set("meshScale", factor.Value);
                }
            }
        }

        // per-particle 逐粒子蜡池色补丁表(shaderRes uuid → SG属性名);ConverterWindow 转完后 patch 对应 .bps。
        public readonly List<KeyValuePair<string, string>> PerParticleColorPatches = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// mesh 输出的 SG float 属性连到 getAttribute(spawnIndex)(BuildShaderBinding 标了 props._perParticleColorIndexProp,
        /// 如蜡烛 _Color_Index 逐蜡烛不同蜡池色):给该系统 init 注入 setAttribute(color, channels=4[B], source=SpawnIndex)
        /// 让每粒子 color.b=spawnIndex(codegen SpawnIndex→float(id)),并记录 (shaderRes,propName) 待 patch .bps
        /// 把该属性 uniform 用法换成 vertexColor.b。只一个自由通道(color.b)→只承载一个此类属性。
        /// </summary>
        public void InjectPerParticleColorIndex()
        {
            // 反向 flow 映射:targetId → 源ctxId(找 mesh 输出所在系统的 init)
            var byId = new Dictionary<int, Jval>();
            var revFlow = new Dictionary<int, int>();
            foreach (var c in LayaContexts)
            {
                int cid = (int)c.NumOf("id");
                byId[cid] = c;
                var fl = c.Get("flowLinks");
                var outp = fl != null ? fl.Get("output") : null;
                if (outp == null) continue;
                if (outp.IsArray) { foreach (var lk in outp.Items) { int t = (int)lk.NumOf("targetId", -1); if (t >= 0) revFlow[t] = cid; } }
                else { int t = (int)outp.NumOf("targetId", -1); if (t >= 0) revFlow[t] = cid; }
            }
            foreach (var ctx in LayaContexts)
            {
                if (ctx.StrOf("typeId") != "outputShaderGraphMesh") continue;
                var props = ctx.Get("props"); if (props == null) continue;
                string prop = props.StrOf("_perParticleColorIndexProp"); if (prop == null) continue;
                props.Remove("_perParticleColorIndexProp");  // 临时标记,不写进最终 .laya.vfx

                // 记录 .bps patch
                string shaderRes = props.StrOf("shaderRes");
                if (shaderRes != null) { var mm = UuidRe.Match(shaderRes); if (mm.Success) PerParticleColorPatches.Add(new KeyValuePair<string, string>(mm.Value, prop)); }

                // 反向追到该系统的 init 上下文
                int cur = (int)ctx.NumOf("id");
                Jval initCtx = null; int guard = 0;
                while (revFlow.ContainsKey(cur) && guard++ < 20)
                {
                    cur = revFlow[cur];
                    Jval cc; if (byId.TryGetValue(cur, out cc) && cc.StrOf("typeId") == "initialize") { initCtx = cc; break; }
                }
                if (initCtx == null) continue;

                var blocks = initCtx.Get("blocks");
                if (blocks == null) { blocks = Jval.Arr(); initCtx.Set("blocks", blocks); }
                bool has = false; foreach (var b in blocks.Items) if (b.Get("_ppColorIndex") != null) { has = true; break; }
                if (has) continue;
                int maxId = 0;
                foreach (var c in LayaContexts) { var bs = c.Get("blocks"); if (bs != null) foreach (var b in bs.Items) { int id = (int)b.NumOf("id"); if (id > maxId) maxId = id; } }
                var block = Jval.Obj()
                    .Set("id", maxId + 600)
                    .Set("typeId", "setAttribute").Set("enabled", true).Set("_ppColorIndex", prop)
                    .Set("props", Jval.Obj().Set("attribute", "color").Set("source", "SpawnIndex")
                        .Set("composition", "Overwrite").Set("random", "Off").Set("channels", 4));
                blocks.Items.Add(block);
            }
        }

        /// <summary>angle 双重手性修正（对应 JS L5629-5721）。</summary>
        public void ApplyAngleHandedness()
        {
            var ctxById = new Dictionary<int, Jval>();
            foreach (var c in LayaContexts) ctxById[(int)c.NumOf("id")] = c;
            var MESH_OUT = new HashSet<string> { "outputShaderGraphMesh", "outputMesh", "outputStaticMesh" };
            var orientOutputs = new HashSet<int>();
            var meshOutputs = new HashSet<int>();
            foreach (var c in LayaContexts)
            {
                string t = c.StrOf("typeId") ?? "";
                bool isOrient = t.StartsWith("output") && HasOrientBlock(c);
                bool isMesh = MESH_OUT.Contains(t);
                if (isOrient || isMesh) orientOutputs.Add((int)c.NumOf("id"));
                if (isMesh) meshOutputs.Add((int)c.NumOf("id"));
            }
            if (orientOutputs.Count == 0) return;

            Func<int, HashSet<int>, HashSet<int>, bool> reaches = null;
            reaches = (startId, targetSet, seen) =>
            {
                if (seen.Contains(startId)) return false;
                seen.Add(startId);
                if (targetSet.Contains(startId)) return true;
                Jval c; if (!ctxById.TryGetValue(startId, out c)) return false;
                var fl = c.Get("flowLinks"); if (fl == null) return false;
                foreach (var k in fl.Keys)
                {
                    var v = fl.Get(k); var arr = v.IsArray ? v.Items : new List<Jval> { v };
                    foreach (var tt in arr) if (tt != null && tt.Get("targetId") != null && reaches((int)tt.NumOf("targetId"), targetSet, seen)) return true;
                }
                return false;
            };
            Func<int, string> meshNameOf = (ctxId) =>
            {
                Jval c; if (!ctxById.TryGetValue(ctxId, out c)) return "";
                var props = c.Get("props"); if (props == null) return "";
                string reff = props.StrOf("mesh");
                if (reff == null) { var spd = props.Get("shaderPropertyDefaults"); if (spd != null) reff = spd.StrOf("mesh"); }
                if (reff == null) return "";
                string key = reff.StartsWith("res://") ? reff.Substring(6) : reff;
                string nm; return ResolvedMeshNames.TryGetValue(key, out nm) ? nm : "";
            };
            Func<int, string> reachedMeshName = (startId) =>
            {
                var seen = new HashSet<int>(); var q = new Queue<int>(); q.Enqueue(startId);
                while (q.Count > 0)
                {
                    int id = q.Dequeue(); if (seen.Contains(id)) continue; seen.Add(id);
                    if (meshOutputs.Contains(id)) { var n = meshNameOf(id); if (n != "") return n; }
                    Jval c; if (!ctxById.TryGetValue(id, out c)) continue;
                    var fl = c.Get("flowLinks"); if (fl == null) continue;
                    foreach (var k in fl.Keys) { var v = fl.Get(k); var arr = v.IsArray ? v.Items : new List<Jval> { v }; foreach (var tt in arr) if (tt != null && tt.Get("targetId") != null) q.Enqueue((int)tt.NumOf("targetId")); }
                }
                return "";
            };
            foreach (var c in LayaContexts)
            {
                string ct = c.StrOf("typeId");
                if (ct != "initialize" && ct != "update") continue;
                if (!reaches((int)c.NumOf("id"), orientOutputs, new HashSet<int>())) continue;
                bool isWallMesh = Regex.IsMatch(reachedMeshName((int)c.NumOf("id")), "wall", RegexOptions.IgnoreCase);
                var blocks = c.Get("blocks"); if (blocks == null) continue;
                foreach (var b in blocks.Items)
                {
                    var bp = b.Get("props");
                    if (b.StrOf("typeId") != "setAttribute" || bp == null || bp.StrOf("attribute") != "angle") continue;
                    var vals = bp.Get("_values"); if (vals == null) continue;
                    foreach (var k in new[] { "x", "y", "b_x", "b_y" }) { var vv = vals.Get(k); if (vv != null && vv.IsNumber) vals.Set(k, -vv.Num); }
                    if (isWallMesh)
                    {
                        var zv = vals.Get("z"); var bzv = vals.Get("b_z");
                        bool zNum = zv != null && zv.IsNumber, bzNum = bzv != null && bzv.IsNumber;
                        if (zNum && bzNum && zv.Num != bzv.Num) { vals.Set("z", zv.Num + 180); vals.Set("b_z", bzv.Num + 180); }
                        else foreach (var k in new[] { "z", "b_z" }) { var vv = vals.Get(k); if (vv != null && vv.IsNumber) { double z = vv.Num + 180; if (z > 180) z -= 360; vals.Set(k, z); } }
                    }
                }
            }
        }
        private static bool HasOrientBlock(Jval ctx) { var bs = ctx.Get("blocks"); if (bs != null) foreach (var b in bs.Items) if (b.StrOf("typeId") == "orient") return true; return false; }

        /// <summary>把 setSpawnEventAttribute 上的 op chain link reroute 到 init block（对应 JS L5841-5952）。</summary>
        public void RerouteSpawnEventAttribute()
        {
            foreach (var ctx in LayaContexts)
            {
                if (ctx.StrOf("typeId") != "spawn") continue;
                var sblocks = ctx.Get("blocks"); if (sblocks == null) continue;
                int ctxId = (int)ctx.NumOf("id");
                foreach (var sb in new List<Jval>(sblocks.Items))
                {
                    if (sb.StrOf("typeId") != "setSpawnEventAttribute") continue;
                    var sbp = sb.Get("props"); string attr = sbp != null ? sbp.StrOf("attribute") : null;
                    if (attr == null) continue;
                    int _sbId = (int)sb.NumOf("id");
                    // ⚠ op→setSpawnEventAttribute 的 link slotId 实际是 block_<id>_<属性名>(如 block_29_position,
                    //   见 L146 用 SlotPropName),不是 block_<id>_value。两者都匹配,否则 reroute 漏做→spawn位置退化成
                    //   静态 valueX/Y/Z,时变的生成位置(如 StripSpawnRate 漩涡)丢失。
                    string targetSlotId = "block_" + _sbId + "_value";
                    string targetSlotIdAttr = "block_" + _sbId + "_" + attr;
                    var opsPointing = new List<Jval>();  // 收集指向此 spawn block 的 link Jval
                    foreach (var op in LayaOperators)
                    {
                        if (op.StrOf("typeId") == "getAttribute" && op.Get("props") != null && op.Get("props").StrOf("location") == "Source") continue;
                        var outp = op.Get("output"); if (outp == null) continue;
                        foreach (var slotName in outp.Keys)
                        {
                            var infoArr = outp.Get(slotName).Get("infoArr"); if (infoArr == null || !infoArr.IsArray) continue;
                            foreach (var link in infoArr.Items) { if ((int)link.NumOf("nodeId") != ctxId) continue; var _sid = link.StrOf("slotId"); if (_sid == targetSlotId || _sid == targetSlotIdAttr) opsPointing.Add(link); }
                        }
                    }
                    if (opsPointing.Count == 0) continue;
                    // init targets via flowLinks.spawnEvt
                    var fl = ctx.Get("flowLinks"); var spawnEvt = fl != null ? fl.Get("spawnEvt") : null;
                    var initTargets = new List<int>();
                    if (spawnEvt != null) { var arr = spawnEvt.IsArray ? spawnEvt.Items : new List<Jval> { spawnEvt }; foreach (var t in arr) if (t.Get("targetId") != null) initTargets.Add((int)t.NumOf("targetId")); }
                    foreach (var initId in initTargets)
                    {
                        Jval initCtx = null; foreach (var c in LayaContexts) if ((int)c.NumOf("id") == initId && c.StrOf("typeId") == "initialize") { initCtx = c; break; }
                        if (initCtx == null) continue;
                        var iblocks = initCtx.Get("blocks"); if (iblocks == null) { iblocks = Jval.Arr(); initCtx.Set("blocks", iblocks); }
                        Jval initBlock = null;
                        foreach (var b in iblocks.Items) { var bp = b.Get("props"); if (b.StrOf("typeId") == "setAttribute" && bp != null && bp.StrOf("attribute") == attr && bp.StrOf("source") == "Slot") { initBlock = b; break; } }
                        if (initBlock == null)
                        {
                            int newBlockId = 0; foreach (var b in iblocks.Items) { int id = (int)b.NumOf("id"); if (id > newBlockId) newBlockId = id; } newBlockId += 100;
                            var sv = sbp;
                            Func<string, double> comp = (k) => { var v = sv.Get(k); if (v != null && v.IsNumber) return v.Num; var vv = sv.Get("value"); return vv != null && vv.IsNumber ? vv.Num : 0; };
                            double val = sv.Get("value") != null && sv.Get("value").IsNumber ? sv.NumOf("value") : 0;
                            initBlock = Jval.Obj().Set("id", newBlockId).Set("typeId", "setAttribute").Set("enabled", true)
                                .Set("props", Jval.Obj().Set("attribute", attr).Set("source", "Slot").Set("composition", "Overwrite").Set("random", "Off")
                                    .Set("_values", Jval.Obj().Set("x", comp("valueX")).Set("y", comp("valueY")).Set("z", comp("valueZ")).Set("w", comp("valueW")).Set("value", val)));
                            iblocks.Push(initBlock);
                        }
                        // 删 init src=Source setAttribute(同 attr)
                        iblocks.Items.RemoveAll(b => { var bp = b.Get("props"); return b.StrOf("typeId") == "setAttribute" && bp != null && bp.StrOf("attribute") == attr && bp.StrOf("source") == "Source"; });
                        // reroute op links
                        string initBlockSlotId = "block_" + (int)initBlock.NumOf("id") + "_value";
                        foreach (var link in opsPointing) { link.Set("nodeId", (int)initCtx.NumOf("id")); link.Set("slotId", initBlockSlotId); }
                        // 移除 getAttribute(attr, Source) 指向 init block 的旧 link
                        foreach (var op in LayaOperators)
                        {
                            if (!(op.StrOf("typeId") == "getAttribute" && op.Get("props") != null && op.Get("props").StrOf("location") == "Source")) continue;
                            if (op.Get("props").StrOf("attribute") != attr) continue;
                            var outp = op.Get("output"); if (outp == null) continue;
                            foreach (var slotName in outp.Keys) { var infoArr = outp.Get(slotName).Get("infoArr"); if (infoArr != null && infoArr.IsArray) infoArr.Items.RemoveAll(l => (int)l.NumOf("nodeId") == (int)initCtx.NumOf("id") && l.StrOf("slotId") == initBlockSlotId); }
                        }
                        // 删 spawn block（保留 useLoopIndex/fromSpawnStateLoop）
                        if (!(sbp.Get("useLoopIndex") != null && sbp.BoolOf("useLoopIndex")) && !(sbp.Get("fromSpawnStateLoop") != null && sbp.BoolOf("fromSpawnStateLoop")))
                            sblocks.Items.Remove(sb);
                        break;
                    }
                }
            }
        }

        /// <summary>让 materialize 的 _MaterializeOverlayColor 由 ColorOverLife 渐变驱动（对应 JS L6114-6127）。</summary>
        public void FixMaterializeOverlay()
        {
            foreach (var ctx in LayaContexts)
            {
                var props = ctx.Get("props"); if (props == null) continue;
                var ex = props.Get("shaderPropertyExpressions"); if (ex == null) continue;
                var ov = ex.Get("_MaterializeOverlayColor"); var edge = ex.Get("_MaterializeEdgeColor");
                if (ov == null || edge == null) continue;
                var nodes = ov.Get("nodes");
                var ovRoot = nodes != null ? nodes.Get(ov.StrOf("rootNodeId")) : null;
                if (ovRoot == null || ovRoot.StrOf("kind") != "Constant") continue;
                var edgeCopy = LayaDefs.DeepClone(edge);
                edgeCopy.Set("outputType", "vec3");
                ex.Set("_MaterializeOverlayColor", edgeCopy);
            }
        }

        /// <summary>GPU/CPU event chain 转 LayaEvents + triggerEvent flowLinks（对应 JS L5502-5627）。</summary>
        public void BuildEvents()
        {
            var gpuEventCtxIds = new HashSet<string>();
            // GPU events
            foreach (var ctx in Contexts)
            {
                if (ctx.ClassType != "VFXBasicGPUEvent") continue;
                int layaId; if (!FileIDToLayaId.TryGetValue(ctx.FileID, out layaId)) continue;
                gpuEventCtxIds.Add(ctx.FileID);
                var ev = Jval.Obj().Set("id", layaId).Set("typeId", "gpuEvent").Set("uiData", UnityYamlParser.GetUIPos(ctx.Body));
                var downstream = UnityYamlParser.GetOutputFlowSlot(ctx.Body);
                if (downstream.Count > 0)
                {
                    var targets = new List<Jval>();
                    foreach (var d in downstream) { int tid; if (FileIDToLayaId.TryGetValue(d, out tid)) targets.Add(Jval.Obj().Set("targetId", tid).Set("targetSlotId", "input")); }
                    if (targets.Count > 0) ev.Set("flowLinks", Jval.Obj().Set("output", targets.Count == 1 ? targets[0] : ToArr(targets)));
                }
                LayaEvents.Add(ev);
            }
            // CPU events
            foreach (var ctx in Contexts)
            {
                if (ctx.ClassType != "VFXBasicEvent") continue;
                int layaId; if (!FileIDToLayaId.TryGetValue(ctx.FileID, out layaId)) continue;
                var m = Regex.Match(ctx.Body, @"^\s+eventName:\s*(.+)$", RegexOptions.Multiline);
                string eventName = m.Success ? m.Groups[1].Value.Trim() : "OnPlay";
                if ((eventName.StartsWith("\"") && eventName.EndsWith("\"")) || (eventName.StartsWith("'") && eventName.EndsWith("'")))
                    eventName = eventName.Length >= 2 ? eventName.Substring(1, eventName.Length - 2) : "";
                var ev = Jval.Obj().Set("id", layaId).Set("typeId", "event").Set("uiData", UnityYamlParser.GetUIPos(ctx.Body)).Set("props", Jval.Obj().Set("eventName", eventName));
                var downstream = UnityYamlParser.GetOutputFlowSlot(ctx.Body);
                if (downstream.Count > 0)
                {
                    var targets = new List<Jval>();
                    foreach (var d in downstream) { int tid; if (!FileIDToLayaId.TryGetValue(d, out tid)) continue; var te = Get(d); bool isSpawn = te != null && te.ClassType == "VFXBasicSpawner"; targets.Add(Jval.Obj().Set("targetId", tid).Set("targetSlotId", isSpawn ? "start" : "input")); }
                    if (targets.Count > 0) ev.Set("flowLinks", Jval.Obj().Set("evt", targets.Count == 1 ? targets[0] : ToArr(targets)));
                }
                LayaEvents.Add(ev);
            }
            // triggerEvent → GPUEvent flowLinks
            foreach (var ctx in Contexts)
            {
                if (ctx.ClassType != "VFXBasicUpdate" && ctx.ClassType != "VFXBasicInitialize") continue;
                int cid; if (!FileIDToLayaId.TryGetValue(ctx.FileID, out cid)) continue;
                var layaCtxFound = FindCtx(cid);
                if (layaCtxFound == null) continue;
                var childIDs = UnityYamlParser.GetRefArrayField(ctx.Body, "m_Children");
                var triggerChildIDs = new List<string>();
                foreach (var id in childIDs) { var e = Get(id); if (e != null && e.ClassType == "TriggerEvent") triggerChildIDs.Add(id); }
                var layaTriggerBlocks = new List<Jval>();
                var blocksJ = layaCtxFound.Get("blocks");
                if (blocksJ != null) foreach (var b in blocksJ.Items) if (b.StrOf("typeId") == "triggerEvent") layaTriggerBlocks.Add(b);
                foreach (var childID in childIDs)
                {
                    var child = Get(childID);
                    if (child == null || child.ClassType != "TriggerEvent") continue;
                    var blockOutputSlots = UnityYamlParser.GetRefArrayField(child.Body, "m_OutputSlots");
                    if (blockOutputSlots.Count == 0) continue;
                    var evtSlot = Get(blockOutputSlots[0]); if (evtSlot == null) continue;
                    var linked = UnityYamlParser.GetLinkedSlots(evtSlot); if (linked.Count == 0) continue;
                    var inputSlot = Get(linked[0]); if (inputSlot == null) continue;
                    string gpuEventFileID = UnityYamlParser.GetSlotOwner(inputSlot) ?? UnityYamlParser.GetSlotMaster(inputSlot);
                    if (gpuEventFileID != null && !gpuEventCtxIds.Contains(gpuEventFileID))
                    {
                        string masterID = UnityYamlParser.GetSlotMaster(inputSlot);
                        if (masterID != null) { var ms = Get(masterID); if (ms != null) gpuEventFileID = UnityYamlParser.GetSlotOwner(ms); }
                    }
                    if (gpuEventFileID == null || !gpuEventCtxIds.Contains(gpuEventFileID)) continue;
                    int gpuLayaId; if (!FileIDToLayaId.TryGetValue(gpuEventFileID, out gpuLayaId)) continue;
                    int idx = triggerChildIDs.IndexOf(childID);
                    if (idx < 0 || idx >= layaTriggerBlocks.Count) continue;
                    var targetBlock = layaTriggerBlocks[idx];
                    var fl = layaCtxFound.Get("flowLinks");
                    if (fl == null) { fl = Jval.Obj(); layaCtxFound.Set("flowLinks", fl); }
                    fl.Set("block_" + (int)targetBlock.NumOf("id") + "_evt", Jval.Obj().Set("targetId", gpuLayaId).Set("targetSlotId", "evt"));
                }
            }
        }

        /// <summary>把所有节点 uiData 平移到画布原点附近（对应 JS L5420-5441）。</summary>
        public void ShiftUiData()
        {
            var all = new List<Jval>(); all.AddRange(LayaContexts); all.AddRange(LayaOperators);
            if (all.Count == 0) return;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            foreach (var n in all) { var u = n.Get("uiData"); if (u == null) continue; if (u.NumOf("x") < minX) minX = u.NumOf("x"); if (u.NumOf("y") < minY) minY = u.NumOf("y"); }
            if (double.IsInfinity(minX)) return;
            double dx = 50 - minX, dy = 0 - minY;
            foreach (var n in all) { var u = n.Get("uiData"); if (u == null) continue; u.Set("x", u.NumOf("x") + dx).Set("y", u.NumOf("y") + dy); }
        }

        /// <summary>为缺少 update 的 initialize 自动插入空 update ctx（对应 JS L5443-5486）。</summary>
        public void InsertEmptyUpdateContexts()
        {
            var needing = new List<KeyValuePair<Jval, List<int>>>();
            foreach (var ctx in LayaContexts)
            {
                if (ctx.StrOf("typeId") != "initialize") continue;
                var fl = ctx.Get("flowLinks");
                var outp = fl != null ? fl.Get("output") : null;
                if (outp == null) continue;
                var targetIds = new List<int>();
                if (outp.IsArray) foreach (var x in outp.Items) targetIds.Add((int)x.NumOf("targetId"));
                else targetIds.Add((int)outp.NumOf("targetId"));
                bool hasUpdate = false;
                foreach (var tid in targetIds) { var t = FindCtx(tid); if (t != null && t.StrOf("typeId") == "update") { hasUpdate = true; break; } }
                if (hasUpdate) continue;
                needing.Add(new KeyValuePair<Jval, List<int>>(ctx, targetIds));
            }
            foreach (var kv in needing)
            {
                var ctx = kv.Key; var oldTargets = kv.Value;
                int newUpdateId = _nextLayaId++;
                var u = ctx.Get("uiData");
                var outLink = oldTargets.Count == 1
                    ? (Jval)Jval.Obj().Set("targetId", oldTargets[0]).Set("targetSlotId", "input")
                    : ToArr(oldTargets.ConvertAll(t => Jval.Obj().Set("targetId", t).Set("targetSlotId", "input")));
                var newCtx = Jval.Obj().Set("id", newUpdateId).Set("typeId", "update")
                    .Set("uiData", Jval.Obj().Set("x", (u != null ? u.NumOf("x") : 0) + 100).Set("y", (u != null ? u.NumOf("y") : 0) + 100))
                    .Set("blocks", Jval.Arr())
                    .Set("props", Jval.Obj().Set("updatePosition", true).Set("ageParticles", true).Set("reapParticles", true).Set("skipZeroDeltaTime", false))
                    .Set("flowLinks", Jval.Obj().Set("output", outLink));
                ctx.Get("flowLinks").Set("output", Jval.Obj().Set("targetId", newUpdateId).Set("targetSlotId", "input"));
                LayaContexts.Add(newCtx);
            }
        }

        /// <summary>用 IDE defs 默认值补全所有 ctx/block/op 的 props（对应 JS L5492-5500）。</summary>
        public void FillAllDefaults()
        {
            foreach (var ctx in LayaContexts)
            {
                Defs.FillDefaults(ctx.StrOf("typeId"), ctx.Get("props"));
                var blocks = ctx.Get("blocks");
                if (blocks != null) foreach (var blk in blocks.Items) Defs.FillDefaults(blk.StrOf("typeId"), blk.Get("props"));
            }
            foreach (var op in LayaOperators) Defs.FillDefaults(op.StrOf("typeId"), op.Get("props"));
        }

        private Jval FindCtx(int id) { foreach (var c in LayaContexts) if ((int)c.NumOf("id") == id) return c; return null; }

        private Jval FindBlock(int ctxId, int blockId)
        {
            foreach (var c in LayaContexts)
            {
                if ((int)c.NumOf("id") != ctxId) continue;
                var blocks = c.Get("blocks");
                if (blocks != null) foreach (var b in blocks.Items) if ((int)b.NumOf("id") == blockId) return b;
            }
            return null;
        }
        private string CustomAttrType(string name)
        {
            if (name == null) return null;
            foreach (var a in LayaCustomAttributes) if (a.StrOf("name") == name) return a.StrOf("type");
            return null;
        }
        private static string BuiltinAttrHint(string name)
        {
            string t; return (name != null && VfxMaps.BUILTIN_ATTR_TYPE_HINT.TryGetValue(name, out t)) ? t : null;
        }

        // blockLinkSlotId（对应 JS L4931-4961）
        private string BlockLinkSlotId(SlotRef target, string swizzle)
        {
            string blockSlotId = "value";
            string spn = target.SlotPropName != null ? target.SlotPropName.ToLowerInvariant() : "";
            var tblock = FindBlock(target.CtxId, target.BlockId);
            string tbType = tblock != null ? tblock.StrOf("typeId") : null;
            if (Regex.IsMatch(spn, "^[ab](_[xyzw])?$") && tbType == "setAttribute") blockSlotId = spn.StartsWith("a") ? "value" : "b_value";
            if (tbType == "setPositionShape" && spn == "radius") return "block_" + target.BlockId + "_radius";
            string slotId = "block_" + target.BlockId + "_" + blockSlotId;
            string sw = swizzle ?? "";
            if (sw == "") { var m = Regex.Match(spn, "_([xyzw])$"); if (m.Success) sw = m.Groups[1].Value; }
            if (sw != "" && Regex.IsMatch(sw, "^[xyzw]$") && (blockSlotId == "value" || blockSlotId == "b_value") && tblock != null
                && (tbType == "setAttribute" || tbType == "setAttributeCurve" || tbType == "attributeFromMap"))
            {
                string attrName = tblock.Get("props") != null ? tblock.Get("props").StrOf("attribute") : null;
                string attrType = CustomAttrType(attrName) ?? (attrName != null ? BuiltinAttrHint(attrName) : null);
                if (attrType == "vec2" || attrType == "vec3" || attrType == "vec4" || attrType == "color") slotId += "_" + sw;
            }
            return slotId;
        }

        private Jval ResolveTargetInfo(SlotRef target)
        {
            if (target.BlockId != 0) return Jval.Obj().Set("nodeId", target.CtxId).Set("slotId", BlockLinkSlotId(target, ""));
            if (target.Dir == "in" && target.OpTypeId != null)
            {
                string realSlotId = target.RealSlotId;
                if (realSlotId == null) { var inputs = Defs.GetInputs(target.OpTypeId); realSlotId = (inputs != null && target.SlotIdx < inputs.Length) ? inputs[target.SlotIdx] : "a"; }
                return Jval.Obj().Set("nodeId", target.LayaId).Set("slotId", realSlotId);
            }
            return null;
        }

        private void CollectLinksFlat(string slotID, List<string> outList)
        {
            var ent = Get(slotID); if (ent == null) return;
            foreach (var lid in UnityYamlParser.GetLinkedSlots(ent)) outList.Add(lid);
            foreach (var cid in UnityYamlParser.GetRefArrayField(ent.Body, "m_Children")) CollectLinksFlat(cid, outList);
        }
        private static void PushOut(Jval layaOp, string slotName, List<Jval> infoArr)
        {
            if (infoArr.Count == 0) return;
            var outp = layaOp.Get("output"); if (outp == null) { outp = Jval.Obj(); layaOp.Set("output", outp); }
            var slot = outp.Get(slotName); if (slot == null) { slot = Jval.Obj().Set("infoArr", Jval.Arr()); outp.Set(slotName, slot); }
            foreach (var info in infoArr) slot.Get("infoArr").Push(info);
        }

        /// <summary>连接 builtin / LogicalNot / getProperty(parameterToOp) 的 output link（对应 JS L4966-5092）。</summary>
        public void LinkBuiltinLogicalNotParameters()
        {
            // 16a builtin
            foreach (var e in BuiltinSlotToOp)
            {
                var layaOp = FindOp(e.LayaOpId); if (layaOp == null) continue;
                var links = new List<string>(); CollectLinksFlat(e.SlotID, links);
                var infoArr = new List<Jval>();
                foreach (var lid0 in links) foreach (var lid in ResolveSlotAliases(lid0)) { SlotRef t; if (SlotToOpId.TryGetValue(lid, out t)) { var info = ResolveTargetInfo(t); if (info != null) infoArr.Add(info); } }
                PushOut(layaOp, "out", infoArr);
            }
            // 16a2 LogicalNot
            foreach (var e in LogicalNotInfo)
            {
                var layaOp = FindOp(e.SubtractOpId); if (layaOp == null) continue;
                var links = new List<string>(); CollectLinksFlat(e.OutSlotID, links);
                var infoArr = new List<Jval>();
                foreach (var lid0 in links) foreach (var lid in ResolveSlotAliases(lid0)) { SlotRef t; if (SlotToOpId.TryGetValue(lid, out t)) { var info = ResolveTargetInfo(t); if (info != null) infoArr.Add(info); } }
                PushOut(layaOp, "out", infoArr);
            }
            // 16a3 getProperty（带 swizzle 分组）
            foreach (var e in ParameterToOp)
            {
                var layaOp = FindOp(e.LayaOpId); if (layaOp == null) continue;
                var allLinked = new List<KeyValuePair<string, string>>();  // (lid, swizzle)
                Action<string, string> collect = null;
                collect = (slotID, swizzle) =>
                {
                    var ent = Get(slotID); if (ent == null) return;
                    foreach (var lid in UnityYamlParser.GetLinkedSlots(ent)) allLinked.Add(new KeyValuePair<string, string>(lid, swizzle));
                    foreach (var cid in UnityYamlParser.GetRefArrayField(ent.Body, "m_Children"))
                    {
                        var cEnt = Get(cid); string cName = cEnt != null ? UnityYamlParser.GetSlotPropertyName(cEnt) : null;
                        string childSw = (cName != null && Regex.IsMatch(cName, "^[xyzw]$", RegexOptions.IgnoreCase)) ? cName.ToLowerInvariant() : swizzle;
                        collect(cid, childSw);
                    }
                };
                foreach (var sid in e.OutputSlots) collect(sid, "");
                var slotsByName = new Dictionary<string, List<Jval>>();
                foreach (var link in allLinked)
                {
                    string sourceSlotName = link.Value != "" ? "value_" + link.Value : "value";
                    List<Jval> infoArr; if (!slotsByName.TryGetValue(sourceSlotName, out infoArr)) { infoArr = new List<Jval>(); slotsByName[sourceSlotName] = infoArr; }
                    foreach (var lid in ResolveSlotAliases(link.Key)) { SlotRef t; if (SlotToOpId.TryGetValue(lid, out t)) { var info = ResolveTargetInfo(t); if (info != null) infoArr.Add(info); } }
                }
                foreach (var kv in slotsByName) PushOut(layaOp, kv.Key, kv.Value);
            }
        }

        /// <summary>输出组装：拼装最终 Laya VFX 资源对象（对应 JS L5751-5784）。</summary>
        public Jval BuildOutput(string yamlText)
        {
            // initialEventName
            var im = Regex.Match(yamlText, @"m_InitialEventName:[ \t]*([^\r\n]*)");
            string initialEvent = (im.Success && im.Groups[1].Value.Trim() != "") ? im.Groups[1].Value.Trim() : "OnPlay";
            // 校验：initialEventName 必须匹配图里某事件，否则回退 "in" / spawn 事件（对应 JS L5728-5750）
            {
                var evtNames = new HashSet<string>();
                foreach (var e in LayaEvents) if (e.StrOf("typeId") == "event") { var en = e.Get("props") != null ? e.Get("props").StrOf("eventName") : null; if (en != null) evtNames.Add(en); }
                if (!evtNames.Contains(initialEvent))
                {
                    if (evtNames.Contains("in")) initialEvent = "in";
                    else
                    {
                        var spawnIds = new HashSet<int>();
                        foreach (var c in LayaContexts)
                        {
                            var bs = c.Get("blocks"); if (bs == null) continue;
                            foreach (var b in bs.Items) if (Regex.IsMatch(b.StrOf("typeId") ?? "", "^(constantRate|singleBurst|periodicBurst)$")) { spawnIds.Add((int)c.NumOf("id")); break; }
                        }
                        string best = null; int bestN = 0;
                        foreach (var e in LayaEvents)
                        {
                            if (e.StrOf("typeId") != "event") continue;
                            var en = e.Get("props") != null ? e.Get("props").StrOf("eventName") : null; if (en == null) continue;
                            var fl = e.Get("flowLinks"); var evt = fl != null ? fl.Get("evt") : null;
                            int n = 0;
                            if (evt != null) { var arr = evt.IsArray ? evt.Items : new List<Jval> { evt }; foreach (var l in arr) if (l != null && spawnIds.Contains((int)l.NumOf("targetId"))) n++; }
                            if (n > bestN) { bestN = n; best = en; }
                        }
                        if (best != null) initialEvent = best;
                    }
                }
            }

            var events = Jval.Arr(); foreach (var e in LayaEvents) events.Push(e);
            var contexts = Jval.Arr(); foreach (var c in LayaContexts) contexts.Push(c);
            var operators = Jval.Arr(); foreach (var o in LayaOperators) operators.Push(o);
            var properties = Jval.Arr(); foreach (var p in LayaProperties) properties.Push(p);
            var customAttrs = Jval.Arr(); foreach (var a in LayaCustomAttributes) customAttrs.Push(a);

            // flipbookPlay frameCount 对齐
            double totalFrames = 16;
            foreach (var c in LayaContexts)
            {
                if (Regex.IsMatch(c.StrOf("typeId") ?? "", "^output(Billboard|Mesh|StaticMesh|ComposedParticle|Cube)$"))
                {
                    var fb = c.Get("props") != null ? c.Get("props").Get("flipbookSize") : null;
                    if (fb != null && fb.NumOf("x") > 0 && fb.NumOf("y") > 0)
                    {
                        double f = Math.Round(fb.NumOf("x") * fb.NumOf("y"));
                        if (f > totalFrames) totalFrames = f;
                    }
                }
            }
            foreach (var c in LayaContexts)
            {
                var blocks = c.Get("blocks");
                if (blocks != null) foreach (var b in blocks.Items) if (b.StrOf("typeId") == "flipbookPlay") b.Get("props").Set("frameCount", totalFrames);
            }

            return Jval.Obj()
                .Set("autoID", _nextLayaId)
                .Set("events", events)
                .Set("contexts", contexts)
                .Set("operators", operators)
                .Set("props", Jval.Obj()
                    .Set("fixedDeltaTime", false).Set("exactFixedTime", false).Set("ignoreTimeScale", false)
                    .Set("preWarmTotalTime", 0).Set("preWarmStepCount", 0).Set("preWarmDeltaTime", 0)
                    .Set("initialEventName", initialEvent))
                .Set("properties", properties)
                .Set("customAttributes", customAttrs);
        }

        /// <summary>conformToSphere 默认值兜底：center 仍为 (0,0,0) 时，从 graph properties 里找含 sphere 的 default 填 center/radius（对应 JS L5193-5231）。</summary>
        public void FixConformToSphereDefaults()
        {
            Jval sphereDefault = null;
            foreach (var p in LayaProperties)
            {
                var d = p.Get("default");
                if (d == null || !d.IsObject) continue;
                var sphere = d.Get("sphere") ?? d;
                if (sphere != null && sphere.IsObject && (sphere.Get("transform") != null || sphere.Get("center") != null)
                    && sphere.Get("radius") != null && sphere.Get("radius").IsNumber) { sphereDefault = sphere; break; }
            }
            if (sphereDefault == null) return;
            foreach (var ctx in LayaContexts)
            {
                var blocks = ctx.Get("blocks"); if (blocks == null) continue;
                foreach (var b in blocks.Items)
                {
                    if (b.StrOf("typeId") != "conformToSphere") continue;
                    var bp = b.Get("props"); if (bp == null) { bp = Jval.Obj(); b.Set("props", bp); }
                    var c = bp.Get("center");
                    bool isDefault = c == null || (c.NumOf("x") == 0 && c.NumOf("y") == 0 && c.NumOf("z") == 0);
                    if (!isDefault) continue;
                    var t = sphereDefault.Get("transform");
                    var pos = t != null ? t.Get("position") : null;
                    if (pos == null) pos = sphereDefault.Get("center");
                    if (pos != null && pos.IsObject)
                        bp.Set("center", Jval.Obj().Set("x", pos.NumOf("x")).Set("y", pos.NumOf("y")).Set("z", pos.NumOf("z")));
                    if (sphereDefault.Get("radius") != null && sphereDefault.Get("radius").IsNumber)
                        bp.Set("radius", sphereDefault.NumOf("radius"));
                }
            }
        }

        /// <summary>N-ary op（multiply/add/subtract/divide）重复 slot 重分配（对应 JS L5786-5834）。</summary>
        public void FixNaryDuplicateSlots()
        {
            var NARY = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
            var NARY_OPS = new HashSet<string> { "multiply", "add", "subtract", "divide" };
            var opMap = new Dictionary<int, Jval>();
            foreach (var o in LayaOperators) opMap[(int)o.NumOf("id")] = o;
            var incomingByOp = new Dictionary<int, List<Jval>>();   // targetOpId → [link...]（保持遍历顺序）
            foreach (var op in LayaOperators)
            {
                var outp = op.Get("output"); if (outp == null) continue;
                foreach (var slotKey in outp.Keys)
                {
                    var arr = outp.Get(slotKey) != null ? outp.Get(slotKey).Get("infoArr") : null;
                    if (arr == null || !arr.IsArray) continue;
                    foreach (var link in arr.Items)
                    {
                        Jval target; if (!opMap.TryGetValue((int)link.NumOf("nodeId"), out target)) continue;
                        if (!NARY_OPS.Contains(target.StrOf("typeId"))) continue;
                        int tid = (int)target.NumOf("id");
                        List<Jval> list; if (!incomingByOp.TryGetValue(tid, out list)) { list = new List<Jval>(); incomingByOp[tid] = list; }
                        list.Add(link);
                    }
                }
            }
            foreach (var kv in incomingByOp)
            {
                var used = new HashSet<string>();
                var dups = new List<Jval>();
                foreach (var link in kv.Value)
                {
                    string sid = link.StrOf("slotId");
                    if (used.Contains(sid)) dups.Add(link); else used.Add(sid);
                }
                foreach (var link in dups)
                    foreach (var slot in NARY)
                        if (!used.Contains(slot)) { link.Set("slotId", slot); used.Add(slot); break; }
            }
        }

        /// <summary>post-fix：GPU event 下游 mesh receiver 强制继承 source（对应 JS L6145-6242）。</summary>
        // 钉墙类语义：补 velocity(source=Source) + update.updatePosition=false；billboard receiver 不动。
        public void FixTriggerEventMeshReceivers()
        {
            var ctxById = new Dictionary<int, Jval>();
            foreach (var c in LayaContexts) ctxById[(int)c.NumOf("id")] = c;
            Func<Jval, List<Jval>> asTargets = (o) =>
            {
                var list = new List<Jval>();
                if (o == null) return list;
                if (o.IsArray) { foreach (var x in o.Items) list.Add(x); } else list.Add(o);
                return list;
            };
            var receiverInitIds = new HashSet<int>();
            foreach (var evt in LayaEvents)
            {
                if (evt.StrOf("typeId") != "gpuEvent") continue;
                var fl = evt.Get("flowLinks"); if (fl == null) continue;
                foreach (var t in asTargets(fl.Get("output")))
                {
                    if (t == null || t.Get("targetId") == null) continue;
                    Jval tCtx; if (!ctxById.TryGetValue((int)t.NumOf("targetId"), out tCtx)) continue;
                    if (tCtx.StrOf("typeId") != "initialize") continue;
                    // init → update → output 走一遍，output 是 mesh-based 才 patch
                    bool hasMeshOutput = false;
                    var initFl = tCtx.Get("flowLinks");
                    foreach (var ut in asTargets(initFl != null ? initFl.Get("output") : null))
                    {
                        if (ut == null || ut.Get("targetId") == null) continue;
                        Jval upCtx; if (!ctxById.TryGetValue((int)ut.NumOf("targetId"), out upCtx)) continue;
                        if (upCtx.StrOf("typeId") != "update") continue;
                        var upFl = upCtx.Get("flowLinks");
                        foreach (var ot in asTargets(upFl != null ? upFl.Get("output") : null))
                        {
                            if (ot == null || ot.Get("targetId") == null) continue;
                            Jval outCtx; if (!ctxById.TryGetValue((int)ot.NumOf("targetId"), out outCtx)) continue;
                            if (Regex.IsMatch(outCtx.StrOf("typeId") ?? "", "^output(Mesh|StaticMesh|ShaderGraphMesh|ComposedParticle)$")) { hasMeshOutput = true; break; }
                        }
                        if (hasMeshOutput) break;
                    }
                    if (hasMeshOutput) receiverInitIds.Add((int)t.NumOf("targetId"));
                }
            }
            foreach (var initId in receiverInitIds)
            {
                Jval initCtx; if (!ctxById.TryGetValue(initId, out initCtx)) continue;
                var blocks = initCtx.Get("blocks"); if (blocks == null) { blocks = Jval.Arr(); initCtx.Set("blocks", blocks); }
                bool hasExplicitSlotVelocity = false, hasVelocity = false;
                int maxId = 0;
                foreach (var b in blocks.Items)
                {
                    var bp = b.Get("props");
                    if (b.StrOf("typeId") == "setAttribute" && bp != null && bp.StrOf("attribute") == "velocity")
                    {
                        hasVelocity = true;
                        if (bp.StrOf("source") == "Slot") hasExplicitSlotVelocity = true;
                    }
                    if (b.Get("id") != null && b.NumOf("id") > maxId) maxId = (int)b.NumOf("id");
                }
                if (!hasVelocity)
                {
                    blocks.Push(Jval.Obj()
                        .Set("id", maxId + 200).Set("typeId", "setAttribute").Set("enabled", true)
                        .Set("props", Jval.Obj()
                            .Set("attribute", "velocity").Set("source", "Source").Set("composition", "Overwrite")
                            .Set("random", "Off").Set("channels", 7)
                            .Set("_values", Jval.Obj().Set("x", 0).Set("y", 0).Set("z", 0))));
                }
                if (!hasExplicitSlotVelocity)
                {
                    var initFl = initCtx.Get("flowLinks");
                    foreach (var t in asTargets(initFl != null ? initFl.Get("output") : null))
                    {
                        if (t == null || t.Get("targetId") == null) continue;
                        Jval upCtx; if (!ctxById.TryGetValue((int)t.NumOf("targetId"), out upCtx)) continue;
                        if (upCtx.StrOf("typeId") == "update" && upCtx.Get("props") != null)
                        {
                            var up = upCtx.Get("props");
                            if (!(up.Get("updatePosition") != null && up.Get("updatePosition").IsBool && !up.BoolOf("updatePosition")))
                                up.Set("updatePosition", false);
                        }
                    }
                }
            }
        }

        /// <summary>主入口：编排整个转换流程并返回 Laya VFX 资源对象。</summary>
        public Jval Convert(string yamlText)
        {
            InlineSubgraphOperators();   // 必须在分类前：克隆的 sub op 一起进入 ID 分配（对应 JS L1247）
            BuildClassificationAndIds();
            ConvertOperators();
            ExpandBuiltinLogicalNotParameters();
            BuildCustomAttributes();
            ConvertContexts();
            LinkOperators();
            LinkBuiltinLogicalNotParameters();
            BuildProperties();
            FixConformToSphereDefaults();   // 16.2.5（对应 JS L5193，在 properties 之后 / IDE workaround 之前）
            ApplyIdeWorkaround();
            ShiftUiData();
            InsertEmptyUpdateContexts();
            FillAllDefaults();
            BuildEvents();
            ApplyAngleHandedness();
            FixNaryDuplicateSlots();        // 17（对应 JS L5786，在 setSpawnEventAttribute reroute 之前）
            RerouteSpawnEventAttribute();
            RerouteePositionBlocks();
            InjectMeshSizeFix();
            InjectSetPositionMeshScale();
            InjectPerParticleColorIndex();
            FixMaterializeOverlay();
            FixTriggerEventMeshReceivers(); // 对应 JS L6145（最后一个 post-fix）
            return BuildOutput(yamlText);
        }
    }
}
