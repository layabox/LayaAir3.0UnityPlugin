using System;
using System.Collections.Generic;

namespace LayaAir3.Converter
{
    /// <summary>
    /// Unity AnimationCurve / Gradient → Laya 曲线/渐变格式的纯转换函数。
    /// C# 移植自 unity-vfx-to-laya.js 的 convertUnityCurveToLaya / unityGradientToLayaStops /
    /// maxNormalizeStripGradient / stopsToIdeGradient / resolveUpstreamInlineCurveGradient。
    /// 全部操作 Jval（Unity 侧 inline JSON / Laya 侧输出结构），无转换器全局状态依赖。
    /// </summary>
    public static class VfxCurveGradient
    {
        // f.field ?? default：字段存在且为数字则取其值（含 0），否则取 default。数值字段等价 ??。
        private static double N(Jval o, string key, double dflt)
        {
            var v = o != null ? o.Get(key) : null;
            return (v != null && v.IsNumber) ? v.Num : dflt;
        }

        /// <summary>Unity AnimationCurve JSON 转 Laya frameData（每关键帧 7 个 float）。</summary>
        public static Jval ConvertUnityCurveToLaya(Jval unityJson)
        {
            var frames = unityJson != null ? unityJson.Get("frames") : null;
            if (frames == null || !frames.IsArray)
            {
                // default: 0→1 线性 curve（2 关键帧）
                return Jval.Obj().Set("frameData", NumArr(
                    0, 0, 0, 1, 0.333, 0.333, 0,
                    1, 1, 1, 0, 0.333, 0.333, 0));
            }
            var outArr = Jval.Arr();
            foreach (var f in frames.Items)
            {
                outArr.Push(Jval.Of(N(f, "time", 0)));
                outArr.Push(Jval.Of(N(f, "value", 0)));
                outArr.Push(Jval.Of(N(f, "inTangent", 0)));
                outArr.Push(Jval.Of(N(f, "outTangent", 0)));
                outArr.Push(Jval.Of(N(f, "inWeight", 0.333)));
                outArr.Push(Jval.Of(N(f, "outWeight", 0.333)));
                outArr.Push(Jval.Of(N(f, "weightedMode", 0)));
            }
            return Jval.Obj().Set("frameData", outArr);
        }

        /// <summary>Unity Gradient（colorKeys 与 alphaKeys 分离）转 Laya stops [{t, color:{r,g,b,a}}]。</summary>
        public static Jval UnityGradientToLayaStops(Jval unityGradient)
        {
            var colorKeys = ArrOf(unityGradient, "colorKeys");
            var alphaKeys = ArrOf(unityGradient, "alphaKeys");
            var timeSet = new SortedSet<double>();
            foreach (var k in colorKeys) timeSet.Add(N(k, "time", 0));
            foreach (var k in alphaKeys) timeSet.Add(N(k, "time", 0));
            if (timeSet.Count == 0)
            {
                return Jval.Arr(
                    Jval.Obj().Set("t", 0).Set("color", Col(1, 1, 1, 1)),
                    Jval.Obj().Set("t", 1).Set("color", Col(1, 1, 1, 0)));
            }
            var sortedTimes = new List<double>(timeSet);
            var outStops = Jval.Arr();
            foreach (var t in sortedTimes)
            {
                double[] c = SampleColor(colorKeys, t);
                double a = SampleAlpha(alphaKeys, t);
                outStops.Push(Jval.Obj().Set("t", t).Set("color", Col(c[0], c[1], c[2], a)));
            }
            return outStops;
        }

        // 返回 [r,g,b]
        private static double[] SampleColor(List<Jval> colorKeys, double t)
        {
            if (colorKeys.Count == 0) return new double[] { 1, 1, 1 };
            var first = colorKeys[0];
            var last = colorKeys[colorKeys.Count - 1];
            if (t <= N(first, "time", 0)) return Rgb(first.Get("color"));
            if (t >= N(last, "time", 0)) return Rgb(last.Get("color"));
            for (int i = 0; i < colorKeys.Count - 1; i++)
            {
                var k0 = colorKeys[i]; var k1 = colorKeys[i + 1];
                double t0 = N(k0, "time", 0), t1 = N(k1, "time", 0);
                if (t >= t0 && t <= t1)
                {
                    double f = (t - t0) / (t1 - t0 + 1e-8);
                    var c0 = Rgb(k0.Get("color")); var c1 = Rgb(k1.Get("color"));
                    return new double[]
                    {
                        c0[0] + (c1[0] - c0[0]) * f,
                        c0[1] + (c1[1] - c0[1]) * f,
                        c0[2] + (c1[2] - c0[2]) * f,
                    };
                }
            }
            return Rgb(last.Get("color"));
        }

        private static double SampleAlpha(List<Jval> alphaKeys, double t)
        {
            if (alphaKeys.Count == 0) return 1;
            var first = alphaKeys[0];
            var last = alphaKeys[alphaKeys.Count - 1];
            if (t <= N(first, "time", 0)) return N(first, "alpha", 1);
            if (t >= N(last, "time", 0)) return N(last, "alpha", 1);
            for (int i = 0; i < alphaKeys.Count - 1; i++)
            {
                var k0 = alphaKeys[i]; var k1 = alphaKeys[i + 1];
                double t0 = N(k0, "time", 0), t1 = N(k1, "time", 0);
                if (t >= t0 && t <= t1)
                {
                    double f = (t - t0) / (t1 - t0 + 1e-8);
                    double a0 = N(k0, "alpha", 1), a1 = N(k1, "alpha", 1);
                    return a0 + (a1 - a0) * f;
                }
            }
            return N(last, "alpha", 1);
        }

        /// <summary>对 strip 颜色 HDR gradient 做 max-normalize（原地改 grad.stops 与 grad._rgbElements），返回是否有改动。</summary>
        public static bool MaxNormalizeStripGradient(Jval grad)
        {
            if (grad == null || !grad.IsObject) return false;
            bool changed = false;
            var stops = grad.Get("stops");
            if (stops != null && stops.IsArray)
            {
                foreach (var s in stops.Items)
                {
                    var c = s != null ? s.Get("color") : null;
                    if (c == null) continue;
                    double r = N(c, "r", 0), g = N(c, "g", 0), b = N(c, "b", 0);
                    double m = Math.Max(r, Math.Max(g, b));
                    if (m > 1) { c.Set("r", r / m); c.Set("g", g / m); c.Set("b", b / m); changed = true; }
                }
            }
            var rgb = grad.Get("_rgbElements");
            if (rgb != null && rgb.IsArray)
            {
                var e = rgb.Items;  // [t, r, g, b, ...]
                for (int i = 0; i + 3 < e.Count; i += 4)
                {
                    double r = Num(e[i + 1]), g = Num(e[i + 2]), b = Num(e[i + 3]);
                    double m = Math.Max(r, Math.Max(g, b));
                    if (m > 1) { e[i + 1] = Jval.Of(r / m); e[i + 2] = Jval.Of(g / m); e[i + 3] = Jval.Of(b / m); changed = true; }
                }
            }
            return changed;
        }

        /// <summary>把 stops 转成 IDE GradientField 格式。</summary>
        public static Jval StopsToIdeGradient(Jval stops)
        {
            if (stops == null || !stops.IsArray || stops.Count == 0)
            {
                return Jval.Obj()
                    .Set("_mode", 0)
                    .Set("_alphaElements", NumArr(0, 1, 1, 1))
                    .Set("_colorAlphaKeysCount", 2)
                    .Set("_rgbElements", NumArr(0, 1, 1, 1, 1, 1, 1, 1))
                    .Set("_colorRGBKeysCount", 2);
            }
            var rgbArr = Jval.Arr();
            var alphaArr = Jval.Arr();
            foreach (var s in stops.Items)
            {
                double t = N(s, "t", 0);
                var c = s.Get("color");
                double r = c != null ? N(c, "r", 1) : 1;
                double g = c != null ? N(c, "g", 1) : 1;
                double b = c != null ? N(c, "b", 1) : 1;
                var av = c != null ? c.Get("a") : null;
                double a = (av != null && av.IsNumber) ? av.Num : 1;
                rgbArr.Push(Jval.Of(t)).Push(Jval.Of(r)).Push(Jval.Of(g)).Push(Jval.Of(b));
                alphaArr.Push(Jval.Of(t)).Push(Jval.Of(a));
            }
            return Jval.Obj()
                .Set("_mode", 0)
                .Set("_alphaElements", alphaArr)
                .Set("_colorAlphaKeysCount", stops.Count)
                .Set("_rgbElements", rgbArr)
                .Set("_colorRGBKeysCount", stops.Count)
                .Set("stops", stops);
        }

        /// <summary>沿 input slot 的 link 回追到 VFXInlineOperator 的 curve/gradient 值。</summary>
        public static Jval ResolveUpstreamInlineCurveGradient(VfxEntry slotEntry, Func<string, VfxEntry> lookupByID)
        {
            if (slotEntry == null) return null;
            var linked = UnityYamlParser.GetLinkedSlots(slotEntry);
            if (linked.Count == 0) return null;
            var upstreamSlot = lookupByID(linked[0]);
            if (upstreamSlot == null) return null;
            var outputInline = UnityYamlParser.GetSlotInlineValue(upstreamSlot);
            if (outputInline != null && outputInline.IsObject
                && ((outputInline.Get("frames") != null && outputInline.Get("frames").IsArray)
                 || (outputInline.Get("colorKeys") != null && outputInline.Get("colorKeys").IsArray)))
                return outputInline;
            string masterID = UnityYamlParser.GetSlotMaster(upstreamSlot) ?? linked[0];
            var masterSlot = lookupByID(masterID);
            if (masterSlot == null) return null;
            string ownerID = UnityYamlParser.GetSlotOwner(masterSlot);
            if (ownerID == null) return null;
            var ownerEntry = lookupByID(ownerID);
            if (ownerEntry == null) return null;
            if (ownerEntry.ClassType != "VFXInlineOperator") return null;
            var inputSlots = UnityYamlParser.GetRefArrayField(ownerEntry.Body, "m_InputSlots");
            if (inputSlots.Count == 0) return null;
            var upstreamInputSlot = lookupByID(inputSlots[0]);
            if (upstreamInputSlot == null) return null;
            return UnityYamlParser.GetSlotInlineValue(upstreamInputSlot);
        }

        // ── 工具 ──
        private static double Num(Jval v) { return (v != null && v.IsNumber) ? v.Num : 0; }
        private static List<Jval> ArrOf(Jval o, string key)
        {
            var a = o != null ? o.Get(key) : null;
            return (a != null && a.IsArray) ? a.Items : new List<Jval>();
        }
        private static double[] Rgb(Jval color)
        {
            if (color == null) return new double[] { 1, 1, 1 };
            return new double[] { N(color, "r", 0), N(color, "g", 0), N(color, "b", 0) };
        }
        private static Jval Col(double r, double g, double b, double a)
        {
            return Jval.Obj().Set("r", r).Set("g", g).Set("b", b).Set("a", a);
        }
        private static Jval NumArr(params double[] nums)
        {
            var a = Jval.Arr();
            foreach (var n in nums) a.Push(Jval.Of(n));
            return a;
        }
    }
}
