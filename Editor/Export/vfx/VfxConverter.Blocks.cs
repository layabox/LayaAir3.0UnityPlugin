using System.Collections.Generic;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 的 block 转换器（Stage 2.3b）：convertSetAttribute / convertAttributeFromCurve /
    /// convertOrient。都返回 Laya block 的 props（Jval），依赖 byID + 映射 + 常量折叠 + curve/gradient。
    /// </summary>
    public partial class VfxConverter
    {
        // o[key] 存在且为数字则取其值（含 0），否则 dflt（模仿 x ?? dflt，数值语义）
        private static double Nz1(Jval o, string key, double dflt)
        {
            var v = o != null ? o.Get(key) : null;
            return (v != null && v.IsNumber) ? v.Num : dflt;
        }
        // o[k1] ?? o[k2] ?? dflt
        private static double NzChain(Jval o, double dflt, string k1, string k2)
        {
            var v1 = o != null ? o.Get(k1) : null;
            if (v1 != null && v1.IsNumber) return v1.Num;
            var v2 = o != null ? o.Get(k2) : null;
            if (v2 != null && v2.IsNumber) return v2.Num;
            return dflt;
        }

        private static readonly int[] UNITY_CHANNELS_TO_LAYA = { 1, 2, 4, 3, 5, 6, 7 };

        // Unity spaceable wrapper {position|direction|vector|center: {x,y,z}} → 内层 vec3
        private static Jval UnwrapSpaceableValue(Jval v)
        {
            if (v == null || !v.IsObject) return v;
            var keys = new List<string>(v.Keys);
            if (keys.Count == 1)
            {
                string k = keys[0].ToLowerInvariant();
                if (k == "position" || k == "direction" || k == "vector" || k == "center")
                {
                    var inner = v.Get(keys[0]);
                    if (inner != null && inner.IsObject && (inner.Get("x") != null || inner.Get("y") != null || inner.Get("z") != null))
                        return inner;
                }
            }
            return v;
        }

        /// <summary>转换 SetAttribute block，返回 Laya block 的 props（含 float/color/vec3 分支与 handedness 处理）。</summary>
        public Jval ConvertSetAttribute(VfxEntry blockEntry)
        {
            string attribute = VfxMaps.NormalizeAttrName(UnityYamlParser.GetStringField(blockEntry.Body, "attribute") ?? "color");
            int compInt = UnityYamlParser.GetIntField(blockEntry.Body, "Composition") ?? 0;
            int srcInt = UnityYamlParser.GetIntField(blockEntry.Body, "Source") ?? 0;
            int randInt = UnityYamlParser.GetIntField(blockEntry.Body, "Random") ?? 0;
            string composition = Lookup(VfxMaps.COMPOSITION_MAP, compInt, "Overwrite");
            string source = Lookup(VfxMaps.SOURCE_MAP, srcInt, "Slot");
            string random = Lookup(VfxMaps.RANDOM_MAP, randInt, "Off");
            int unityChannels = UnityYamlParser.GetIntField(blockEntry.Body, "channels") ?? 6;
            int channels = (unityChannels >= 0 && unityChannels < 7) ? UNITY_CHANNELS_TO_LAYA[unityChannels] : 7;

            var inputSlots = UnityYamlParser.GetRefArrayField(blockEntry.Body, "m_InputSlots");
            var slotA = inputSlots.Count > 0 && inputSlots[0] != null ? Get(inputSlots[0]) : null;
            var slotB = inputSlots.Count > 1 && inputSlots[1] != null ? Get(inputSlots[1]) : null;
            var valA = UnwrapSpaceableValue(UnityYamlParser.GetSlotInlineValue(slotA));
            var valB = UnwrapSpaceableValue(UnityYamlParser.GetSlotInlineValue(slotB));

            var values = Jval.Obj();
            var props = Jval.Obj()
                .Set("attribute", attribute).Set("source", source).Set("composition", composition)
                .Set("random", random).Set("channels", channels).Set("_values", values);
            string t = VfxMaps.AttrType(attribute);

            if (t == "float")
            {
                double? ra = (inputSlots.Count > 0 && inputSlots[0] != null) ? ResolveSetAttrScalar(inputSlots[0]) : null;
                double? rb = (inputSlots.Count > 1 && inputSlots[1] != null) ? ResolveSetAttrScalar(inputSlots[1]) : null;
                if (random != "Off" && rb != null)
                {
                    double x = (ra != null) ? ra.Value : 0;
                    values.Set("x", x);
                    values.Set("value", x);
                    values.Set("y", rb.Value);
                    values.Set("b_value", rb.Value);
                }
                else
                {
                    double v = (ra != null) ? ra.Value
                        : (valA != null && valA.IsNumber ? valA.Num
                           : (valA != null && valA.IsObject && valA.Get("x") != null && valA.Get("x").IsNumber ? valA.Get("x").Num : 1));
                    values.Set("x", v);
                    values.Set("value", v);
                }
            }
            else if (t == "color")
            {
                var c = (valA != null && valA.IsObject) ? valA : null;
                values.Set("r", NzChain(c, 1, "r", "x"));
                values.Set("g", NzChain(c, 1, "g", "y"));
                values.Set("b", NzChain(c, 1, "b", "z"));
                values.Set("a", NzChain(c, 1, "a", "w"));
                if (random != "Off" && valB != null && valB.IsObject)
                {
                    values.Set("b_r", NzChain(valB, 1, "r", "x"));
                    values.Set("b_g", NzChain(valB, 1, "g", "y"));
                    values.Set("b_b", NzChain(valB, 1, "b", "z"));
                    values.Set("b_a", NzChain(valB, 1, "a", "w"));
                }
            }
            else if (t == "vec3")
            {
                bool opDriven = false;
                if (slotA != null)
                {
                    var childIDs = UnityYamlParser.GetRefArrayField(slotA.Body, "m_Children");
                    var comp = new Dictionary<string, string>();
                    bool anyLinked = false;
                    foreach (var cid in childIDs)
                    {
                        var ce = Get(cid); if (ce == null) continue;
                        string cn = (UnityYamlParser.GetSlotPropertyName(ce) ?? "").ToLowerInvariant();
                        if (cn != "x" && cn != "y" && cn != "z") continue;
                        comp[cn] = cid;
                        if (UnityYamlParser.GetLinkedSlots(ce).Count > 0) anyLinked = true;
                    }
                    if (anyLinked)
                    {
                        var res = new Dictionary<string, Range>();
                        bool anyRange = false;
                        foreach (var ax in new[] { "x", "y", "z" })
                        {
                            string cid;
                            if (comp.TryGetValue(ax, out cid))
                                res[ax] = ResolveAttrComponentRange(cid) ?? new Range(0, 0);
                            else
                            {
                                double mv = (valA != null && valA.IsObject && valA.Get(ax) != null && valA.Get(ax).IsNumber) ? valA.Get(ax).Num : 0;
                                res[ax] = new Range(mv, mv);
                            }
                            if (res[ax].Min != res[ax].Max) anyRange = true;
                        }
                        props.Set("channels", 7);
                        if (anyRange)
                        {
                            props.Set("random", "Per Component");
                            values = Jval.Obj()
                                .Set("x", res["x"].Min).Set("y", res["y"].Min).Set("z", res["z"].Min)
                                .Set("b_x", res["x"].Max).Set("b_y", res["y"].Max).Set("b_z", res["z"].Max);
                        }
                        else
                        {
                            props.Set("random", "Off");
                            values = Jval.Obj().Set("x", res["x"].Min).Set("y", res["y"].Min).Set("z", res["z"].Min);
                        }
                        props.Set("_values", values);
                        opDriven = true;
                    }
                }
                if (!opDriven)
                {
                    double fill = (attribute == "scale") ? 1 : 0;
                    values.Set("x", fill).Set("y", fill).Set("z", fill);
                    WriteAxis(values, valA, "", unityChannels);
                    if (random != "Off" && valB != null && !valB.IsNull)
                    {
                        values.Set("b_x", fill).Set("b_y", fill).Set("b_z", fill);
                        WriteAxis(values, valB, "b_", unityChannels);
                    }
                    if (attribute == "scale") props.Set("channels", 7);
                }
            }

            // handedness：velocity z 翻转
            if (attribute == "velocity" && t == "vec3")
            {
                FlipNum(values, "z");
                FlipNum(values, "b_z");
            }
            // handedness：angle rx/ry 取反
            if (attribute == "angle" && t == "vec3")
            {
                foreach (var k in new[] { "x", "y", "b_x", "b_y" }) FlipNum(values, k);
            }
            return props;
        }

        private static void FlipNum(Jval o, string key)
        {
            var v = o.Get(key);
            if (v != null && v.IsNumber) o.Set(key, -v.Num);
        }

        // 按 unityChannels 把 src(标量/Vector2/vec3) 写到 vals 的 prefix+轴
        private static void WriteAxis(Jval vals, Jval src, string prefix, int unityChannels)
        {
            if (src == null) return;
            if (src.IsNumber)
            {
                if (unityChannels == 0) vals.Set(prefix + "x", src.Num);
                else if (unityChannels == 1) vals.Set(prefix + "y", src.Num);
                else if (unityChannels == 2) vals.Set(prefix + "z", src.Num);
            }
            else if (src.IsObject)
            {
                if (unityChannels == 3) { vals.Set(prefix + "x", Nz1(src, "x", 0)); vals.Set(prefix + "y", Nz1(src, "y", 0)); }
                else if (unityChannels == 4) { vals.Set(prefix + "x", Nz1(src, "x", 0)); vals.Set(prefix + "z", Nz1(src, "y", 0)); }
                else if (unityChannels == 5) { vals.Set(prefix + "y", Nz1(src, "x", 0)); vals.Set(prefix + "z", Nz1(src, "y", 0)); }
                else { vals.Set(prefix + "x", Nz1(src, "x", 0)); vals.Set(prefix + "y", Nz1(src, "y", 0)); vals.Set(prefix + "z", Nz1(src, "z", 0)); }
            }
        }

        /// <summary>转换 AttributeFromCurve/Gradient block，返回 Laya block 的 props（含 curve/gradient 与 vec3 多通道曲线）。</summary>
        public Jval ConvertAttributeFromCurve(VfxEntry blockEntry)
        {
            string attribute = VfxMaps.NormalizeAttrName(UnityYamlParser.GetStringField(blockEntry.Body, "attribute") ?? "size");
            int compInt = UnityYamlParser.GetIntField(blockEntry.Body, "Composition") ?? 0;
            int sampleModeInt = UnityYamlParser.GetIntField(blockEntry.Body, "SampleMode") ?? 0;
            string composition = Lookup(VfxMaps.COMPOSITION_MAP, compInt, "Overwrite");
            string[] SAMPLE_MODE_MAP = { "OverLife", "BySpeed", "Random", "RandomConstantPerParticle", "Custom" };
            string sampleMode = (sampleModeInt >= 0 && sampleModeInt < SAMPLE_MODE_MAP.Length) ? SAMPLE_MODE_MAP[sampleModeInt] : "OverLife";

            var inputSlots = UnityYamlParser.GetRefArrayField(blockEntry.Body, "m_InputSlots");
            var curveSlot = inputSlots.Count > 0 && inputSlots[0] != null ? Get(inputSlots[0]) : null;
            var curveJson = UnityYamlParser.GetSlotInlineValue(curveSlot);
            string curveSlotType = UnityYamlParser.GetSlotType(curveSlot) ?? "";
            if (curveSlot != null && UnityYamlParser.GetLinkedSlots(curveSlot).Count > 0)
            {
                string upID = UnityYamlParser.GetLinkedSlots(curveSlot)[0];
                var up = Get(upID);
                string masterID = up != null ? (UnityYamlParser.GetSlotMaster(up) ?? upID) : null;
                var master = masterID != null ? Get(masterID) : null;
                string ownerID = master != null ? UnityYamlParser.GetSlotOwner(master) : null;
                var owner = ownerID != null ? Get(ownerID) : null;
                if (owner != null && owner.ClassType == "VFXParameter")
                {
                    var authoritative = UnityYamlParser.GetSlotInlineValue(master);
                    if (authoritative != null && authoritative.IsObject) curveJson = authoritative;
                }
            }

            var props = Jval.Obj()
                .Set("attribute", attribute).Set("sampleMode", sampleMode).Set("composition", composition)
                .Set("value", 0).Set("minSpeed", 0).Set("maxSpeed", 1);

            bool isGradient = curveSlotType.IndexOf("Gradient", System.StringComparison.OrdinalIgnoreCase) >= 0
                && curveJson != null && curveJson.IsObject
                && curveJson.Get("colorKeys") != null && curveJson.Get("colorKeys").IsArray;
            if (isGradient)
            {
                var alphaKeys = curveJson.Get("alphaKeys");
                if (alphaKeys == null || !alphaKeys.IsArray)
                    alphaKeys = Jval.Arr(
                        Jval.Obj().Set("alpha", 1).Set("time", 0),
                        Jval.Obj().Set("alpha", 1).Set("time", 1));
                props.Set("gradient", Jval.Obj().Set("colorKeys", curveJson.Get("colorKeys")).Set("alphaKeys", alphaKeys));
            }
            else
            {
                props.Set("curve", VfxCurveGradient.ConvertUnityCurveToLaya(curveJson));
                var VEC3_CURVE_ATTRS = new HashSet<string> { "scale", "angle", "velocity", "angularVelocity", "targetPosition" };
                if (VEC3_CURVE_ATTRS.Contains(attribute))
                {
                    var identity = Jval.Obj().Set("frameData", NumArr(0, 1, 0, 0, 0.333, 0.333, 0, 1, 1, 0, 0, 0.333, 0.333, 0));
                    int? chInt = UnityYamlParser.GetIntField(blockEntry.Body, "channels");
                    string[][] CH_SETS = {
                        new[]{"x"}, new[]{"y"}, new[]{"z"}, new[]{"x","y"}, new[]{"x","z"}, new[]{"y","z"}, new[]{"x","y","z"}
                    };
                    int chIdx = chInt != null ? chInt.Value : 6;
                    string[] chSet = (chIdx >= 0 && chIdx < CH_SETS.Length) ? CH_SETS[chIdx] : new[] { "x", "y", "z" };
                    var byCh = new Dictionary<string, Jval>();
                    for (int si = 0; si < chSet.Length; si++)
                    {
                        Jval jv = (si < inputSlots.Count && inputSlots[si] != null) ? UnityYamlParser.GetSlotInlineValue(Get(inputSlots[si])) : null;
                        byCh[chSet[si]] = (jv != null && jv.Get("frames") != null && jv.Get("frames").IsArray) ? VfxCurveGradient.ConvertUnityCurveToLaya(jv) : null;
                    }
                    Jval cX = byCh.ContainsKey("x") && byCh["x"] != null ? byCh["x"] : identity;
                    Jval cY = byCh.ContainsKey("y") && byCh["y"] != null ? byCh["y"] : identity;
                    Jval cZ = byCh.ContainsKey("z") && byCh["z"] != null ? byCh["z"] : identity;
                    props.Set("curve", cX);
                    string sx = cX.Serialize(0);
                    if (cY.Serialize(0) != sx) props.Set("curveY", cY);
                    if (cZ.Serialize(0) != sx) props.Set("curveZ", cZ);
                    int mask = 0;
                    foreach (var c in chSet) mask |= (c == "x" ? 1 : c == "y" ? 2 : 4);
                    props.Set("channels", mask);
                }
            }
            return props;
        }

        /// <summary>转换 Orient block，返回 Laya block 的 props（映射 Unity Orient.Mode 与 axes 枚举）。</summary>
        public Jval ConvertOrient(VfxEntry blockEntry)
        {
            int modeInt = UnityYamlParser.GetIntField(blockEntry.Body, "mode") ?? 0;
            int axesInt = UnityYamlParser.GetIntField(blockEntry.Body, "axes") ?? 4;
            string[] AXES_MAP = { "XY", "YZ", "ZX", "YX", "ZY", "XZ" };
            string axes = (axesInt >= 0 && axesInt < AXES_MAP.Length) ? AXES_MAP[axesInt] : "ZY";

            if (modeInt == 4)
            {
                var inputSlots = UnityYamlParser.GetRefArrayField(blockEntry.Body, "m_InputSlots");
                string s0 = inputSlots.Count > 0 ? inputSlots[0] : null; // axes[0] 主轴 (Unity AxisZ)
                string s1 = inputSlots.Count > 1 ? inputSlots[1] : null; // axes[1] 次轴 (Unity AxisY)

                // 逐粒子轴来源识别：Advanced 的两轴各自可能连到「velocity 属性」或「径向法线 = position - center」。
                // 旧启发式(primaryLinked→Along Velocity)把「被连的主轴」一律当成 velocity，
                // 对 OrientAdvanced 模板(AxisZ=径向法线、AxisY=velocity)会算错朝向。这里保住 Advanced 并透传真实来源。
                Jval centerA, centerB;
                string srcA = ClassifyOrientAxisSource(s0, out centerA);
                string srcB = ClassifyOrientAxisSource(s1, out centerB);
                if (srcA != null || srcB != null)
                {
                    var slotA0 = s0 != null ? Get(s0) : null;
                    var slotB0 = s1 != null ? Get(s1) : null;
                    var a0 = Unwrap(slotA0 != null ? UnityYamlParser.GetSlotInlineValue(slotA0) : null);
                    var b0 = Unwrap(slotB0 != null ? UnityYamlParser.GetSlotInlineValue(slotB0) : null);
                    var o = Jval.Obj().Set("mode", "Advanced").Set("axes", axes)
                        .Set("axisSourceA", srcA ?? "static")
                        .Set("axisSourceB", srcB ?? "static");
                    if (srcA == "position") o.Set("axisCenterA", centerA ?? Vec3O(0, 0, 0));
                    if (srcB == "position") o.Set("axisCenterB", centerB ?? Vec3O(0, 0, 0));
                    o.Set("customAxisA", (a0 != null && a0.IsObject) ? Vec3O(Nz1(a0, "x", 0), Nz1(a0, "y", 0), Nz1(a0, "z", 1)) : Vec3O(0, 0, 1));
                    o.Set("customAxisB", (b0 != null && b0.IsObject) ? Vec3O(Nz1(b0, "x", 0), Nz1(b0, "y", 1), Nz1(b0, "z", 0)) : Vec3O(0, 1, 0));
                    return o;
                }

                var primarySlot = s0 != null ? Get(s0) : null;
                bool primaryLinked = primarySlot != null && UnityYamlParser.GetLinkedSlots(primarySlot).Count > 0;
                if (primaryLinked)
                    return Jval.Obj().Set("mode", "Along Velocity").Set("axes", axes);
                var slotA = s0 != null ? Get(s0) : null;
                var slotB = s1 != null ? Get(s1) : null;
                var valA = slotA != null ? UnityYamlParser.GetSlotInlineValue(slotA) : null;
                var valB = slotB != null ? UnityYamlParser.GetSlotInlineValue(slotB) : null;
                var a = Unwrap(valA);
                var b = Unwrap(valB);
                return Jval.Obj()
                    .Set("mode", "Advanced").Set("axes", axes)
                    .Set("customAxisA", (a != null && a.IsObject) ? Vec3O(Nz1(a, "x", 0), Nz1(a, "y", 0), Nz1(a, "z", 1)) : Vec3O(0, 0, 1))
                    .Set("customAxisB", (b != null && b.IsObject) ? Vec3O(Nz1(b, "x", 0), Nz1(b, "y", 1), Nz1(b, "z", 0)) : Vec3O(0, 1, 0));
            }
            if (modeInt == 6)
                return Jval.Obj().Set("mode", "Along Velocity").Set("axes", "YX");
            var MODE_MAP = new Dictionary<int, string>
            {
                { 0, "Face Camera Plane" }, { 1, "Face Camera Position" }, { 2, "Look At Position" },
                { 3, "Look At Line" }, { 5, "Fixed Axis" }, { 6, "Along Velocity" },
            };
            string mode;
            return Jval.Obj().Set("mode", MODE_MAP.TryGetValue(modeInt, out mode) ? mode : "Face Camera Plane");
        }

        private static Jval Unwrap(Jval v)
        {
            if (v != null && v.IsObject && v.Get("direction") != null) return v.Get("direction");
            return v;
        }

        /// <summary>识别 orient 轴输入槽的逐粒子来源: "velocity" / "position"(径向法线) / null(静态或未知)。center 输出径向来源的中心(默认原点)。</summary>
        private string ClassifyOrientAxisSource(string inputSlotID, out Jval center)
        {
            center = null;
            var slot = inputSlotID != null ? Get(inputSlotID) : null;
            if (slot == null) return null;
            var linked = UnityYamlParser.GetLinkedSlots(slot);
            if (linked.Count == 0) return null;   // 内联常量，非逐粒子
            return ResolveOrientAxisOwner(linked[0], out center, 0);
        }

        /// <summary>沿 orient 轴连线追溯上游算子，判定其语义来源。支持 velocity / position 属性，以及 Subtract(position, center) 径向法线。</summary>
        private string ResolveOrientAxisOwner(string srcSlotID, out Jval center, int depth)
        {
            center = null;
            if (depth > 6 || srcSlotID == null) return null;
            var srcSlot = Get(srcSlotID);
            if (srcSlot == null) return null;
            string masterID = UnityYamlParser.GetSlotMaster(srcSlot) ?? srcSlotID;
            var master = Get(masterID) ?? srcSlot;
            string ownerID = UnityYamlParser.GetSlotOwner(master);
            var owner = ownerID != null ? Get(ownerID) : null;
            if (owner == null) return null;

            // VFXAttributeParameter / getAttribute: 直接读 attribute 字段(不依赖精确 class 名)
            string attr = UnityYamlParser.GetStringField(owner.Body, "attribute");
            if (attr == "velocity") return "velocity";
            if (attr == "position") { center = Vec3O(0, 0, 0); return "position"; }

            // Subtract(a, b): a=position 属性 & b=常量 → 径向法线，center=b
            if (owner.ClassType == "Subtract")
            {
                var ins = UnityYamlParser.GetRefArrayField(owner.Body, "m_InputSlots");
                if (ins.Count >= 2)
                {
                    Jval ignore;
                    string aSrc = ClassifyOrientAxisSource(ins[0], out ignore);
                    if (aSrc == "position")
                    {
                        center = ReadInlineVec3(ins[1]) ?? Vec3O(0, 0, 0);
                        return "position";
                    }
                }
            }
            return null;
        }

        private Jval ReadInlineVec3(string slotID)
        {
            var s = slotID != null ? Get(slotID) : null;
            if (s == null) return null;
            var d = Unwrap(UnityYamlParser.GetSlotInlineValue(s));
            return (d != null && d.IsObject) ? Vec3O(Nz1(d, "x", 0), Nz1(d, "y", 0), Nz1(d, "z", 0)) : null;
        }
        private static Jval Vec3O(double x, double y, double z) { return Jval.Obj().Set("x", x).Set("y", y).Set("z", z); }
        private static Jval NumArr(params double[] nums) { var a = Jval.Arr(); foreach (var n in nums) a.Push(Jval.Of(n)); return a; }
        private static string Lookup(Dictionary<int, string> m, int key, string dflt) { string v; return m.TryGetValue(key, out v) && v != null ? v : dflt; }
    }
}
