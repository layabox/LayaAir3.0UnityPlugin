using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VFX 主转换器（Unity .vfx → Laya .laya.vfx）。C# 移植自 unity-vfx-to-laya.js。
    ///
    /// 该类持有 JS 版散落在模块作用域的全局可变状态（byID / entries / ... ），
    /// 原来的 function 变成方法。分子阶段填充：
    ///   Stage 2.3a（本文件初版）：byID 索引 + 常量折叠层（evalConst / resolveSetAttrScalar /
    ///                              resolveAttrComponentRange / readSpawnNumberSlot）。
    ///   Stage 2.3b：block 转换器（convertSetAttribute / convertSpawnBlock / convertOrient ...）。
    ///   Stage 2.4：主驱动（inlineSubgraphOperators / 上下文遍历 / link pass / 输出组装）+ ResoureMap。
    /// </summary>
    public partial class VfxConverter
    {
        public readonly List<VfxEntry> Entries;
        public readonly Dictionary<string, VfxEntry> ById = new Dictionary<string, VfxEntry>();
        public readonly List<string> Warnings = new List<string>();

        public VfxConverter(List<VfxEntry> entries)
        {
            Entries = entries;
            foreach (var e in entries)
                if (e.FileID != null) ById[e.FileID] = e;   // 后者覆盖前者（与 JS Map.set 一致）
        }

        public VfxEntry Get(string id) { VfxEntry e; return (id != null && ById.TryGetValue(id, out e)) ? e : null; }
        private void Warn(string msg) { Warnings.Add(msg); }

        // ── 常量折叠：沿 input slot 的 link 递归求值算子链的常量结果 ──
        /// <summary>沿 input slot 的 link 递归求值算子链的常量结果，返回 null 表示非常量（逐粒子/无法折叠）。对应 JS evalConstOperatorFromInputSlot。</summary>
        public double? EvalConstOperatorFromInputSlot(string slotID, int depth = 0)
        {
            if (depth > 8) return null;
            var slot = Get(slotID);
            if (slot == null) return null;
            var linked = UnityYamlParser.GetLinkedSlots(slot);
            if (linked.Count == 0)
            {
                var v = UnityYamlParser.GetSlotInlineValue(slot);
                return v.IsNumber ? (double?)v.Num : null;
            }
            string srcSlotID = linked[0];
            var srcSlot = Get(srcSlotID);
            if (srcSlot == null) return null;

            // 子 slot owner=0，需解析到 master slot 再读真正 op owner
            string ownerOpID = null;
            string masterSlotID = UnityYamlParser.GetRefField(srcSlot.Body, "m_MasterSlot");
            if (masterSlotID != null && masterSlotID != "0")
            {
                var masterSlot = Get(masterSlotID);
                if (masterSlot != null)
                {
                    var m = Regex.Match(masterSlot.Body, @"m_MasterData:\s*\n\s*m_Owner:\s*\{fileID:\s*(\d+)");
                    if (m.Success) ownerOpID = m.Groups[1].Value;
                }
            }
            if (ownerOpID == null)
            {
                var m = Regex.Match(srcSlot.Body, @"m_Owner:\s*\{fileID:\s*(\d+)");
                if (m.Success) ownerOpID = m.Groups[1].Value;
            }
            if (ownerOpID == null || ownerOpID == "0") return null;
            var ownerOp = Get(ownerOpID);
            if (ownerOp == null) return null;

            if (ownerOp.ClassType == "VFXInlineOperator")
            {
                var ins = UnityYamlParser.GetRefArrayField(ownerOp.Body, "m_InputSlots");
                if (ins.Count == 0) return null;
                var v = UnityYamlParser.GetSlotInlineValue(Get(ins[0]));
                return v.IsNumber ? (double?)v.Num : null;
            }

            if (ownerOp.ClassType == "VFXParameter")
            {
                var v = UnityYamlParser.GetSlotInlineValue(srcSlot);
                if (v.IsNumber) return v.Num;
                if (v.IsObject)
                {
                    string propName = UnityYamlParser.GetSlotPropertyName(srcSlot);
                    if (propName != null)
                    {
                        var comp = v.Get(propName);
                        if (comp != null && comp.IsNumber) return comp.Num;
                    }
                }
                string parentSlotID = UnityYamlParser.GetRefField(srcSlot.Body, "m_Parent");
                if (parentSlotID != null && parentSlotID != "0")
                {
                    var parentSlot = Get(parentSlotID);
                    if (parentSlot != null)
                    {
                        var pv = UnityYamlParser.GetSlotInlineValue(parentSlot);
                        string propName = UnityYamlParser.GetSlotPropertyName(srcSlot);
                        if (pv.IsObject && propName != null)
                        {
                            var comp = pv.Get(propName);
                            if (comp != null && comp.IsNumber) return comp.Num;
                        }
                    }
                }
                return null;
            }

            if (ownerOp.ClassType == "Remap")
            {
                var ins = UnityYamlParser.GetRefArrayField(ownerOp.Body, "m_InputSlots");
                if (ins.Count >= 5)
                {
                    var In = EvalConstOperatorFromInputSlot(ins[0], depth + 1);
                    var oMin = EvalConstOperatorFromInputSlot(ins[1], depth + 1);
                    var oMax = EvalConstOperatorFromInputSlot(ins[2], depth + 1);
                    var nMin = EvalConstOperatorFromInputSlot(ins[3], depth + 1);
                    var nMax = EvalConstOperatorFromInputSlot(ins[4], depth + 1);
                    if (In != null && oMin != null && oMax != null && nMin != null && nMax != null)
                    {
                        double denom = (oMax.Value - oMin.Value);
                        if (denom == 0) denom = 1;
                        return nMin.Value + (In.Value - oMin.Value) * (nMax.Value - nMin.Value) / denom;
                    }
                }
                return null;
            }

            // 二元数学算子
            Func<double, double, double> fn = null;
            switch (ownerOp.ClassType)
            {
                case "Multiply": fn = (a, b) => a * b; break;
                case "Add": fn = (a, b) => a + b; break;
                case "Subtract": fn = (a, b) => a - b; break;
                case "Divide": fn = (a, b) => b != 0 ? a / b : 0; break;
                case "Maximum": fn = (a, b) => Math.Max(a, b); break;
                case "Minimum": fn = (a, b) => Math.Min(a, b); break;
            }
            if (fn == null) return null;
            var ins2 = UnityYamlParser.GetRefArrayField(ownerOp.Body, "m_InputSlots");
            if (ins2.Count < 2) return null;
            var av = EvalConstOperatorFromInputSlot(ins2[0], depth + 1);
            var bv = EvalConstOperatorFromInputSlot(ins2[1], depth + 1);
            if (av == null || bv == null) return null;
            return fn(av.Value, bv.Value);
        }

        /// <summary>把 setAttribute 标量 slot 求值为常量 double?（null 表示非常量/逐粒子，回退 inline）。对应 JS resolveSetAttrScalar。</summary>
        // 对应 JS 返回 {const:v}/null；range 路径当前实现未启用（保留注释语义）。
        public double? ResolveSetAttrScalar(string slotID)
        {
            var slot = Get(slotID);
            if (slot == null) return null;
            if (UnityYamlParser.GetLinkedSlots(slot).Count == 0)
            {
                var v = UnityYamlParser.GetSlotInlineValue(slot);
                return v.IsNumber ? (double?)v.Num : null;
            }
            var c = EvalConstOperatorFromInputSlot(slotID);
            if (c != null) return c;
            var vv = UnityYamlParser.GetSlotInlineValue(slot);
            return vv.IsNumber ? (double?)vv.Num : null;
        }

        public struct Range { public double Min, Max; public Range(double a, double b) { Min = a; Max = b; } }

        /// <summary>把属性分量 slot 求值为 {min,max} 范围，支持 operator 驱动（Random/常量）。对应 JS resolveAttrComponentRange。</summary>
        public Range? ResolveAttrComponentRange(string slotID)
        {
            var slot = Get(slotID);
            if (slot == null) return null;
            if (UnityYamlParser.GetLinkedSlots(slot).Count == 0)
            {
                var v = UnityYamlParser.GetSlotInlineValue(slot);
                double n;
                if (v.IsNumber) n = v.Num;
                else
                {
                    // 分量 slot 常无自身 inline，回退父 master inline 对应分量
                    string parentSlotID = UnityYamlParser.GetRefField(slot.Body, "m_Parent");
                    var parentSlot = (parentSlotID != null && parentSlotID != "0") ? Get(parentSlotID) : null;
                    var pv = parentSlot != null ? UnityYamlParser.GetSlotInlineValue(parentSlot) : Jval.Null();
                    string propName = UnityYamlParser.GetSlotPropertyName(slot);
                    if (pv.IsObject && propName != null && pv.Get(propName) != null && pv.Get(propName).IsNumber)
                        n = pv.Get(propName).Num;
                    else n = 0;
                }
                return new Range(n, n);
            }
            string upID = UnityYamlParser.GetLinkedSlots(slot)[0];
            var up = Get(upID);
            string masterID = up != null ? (UnityYamlParser.GetSlotMaster(up) ?? upID) : null;
            var master = masterID != null ? Get(masterID) : null;
            string ownerID = master != null ? UnityYamlParser.GetSlotOwner(master) : null;
            var owner = ownerID != null ? Get(ownerID) : null;
            if (owner != null && owner.ClassType == "Random")
            {
                var ins = UnityYamlParser.GetRefArrayField(owner.Body, "m_InputSlots");
                Func<int, double> rd = i =>
                {
                    double? r = (i < ins.Count && ins[i] != null) ? EvalConstOperatorFromInputSlot(ins[i]) : null;
                    if (r == null && i < ins.Count && ins[i] != null)
                    {
                        var v = UnityYamlParser.GetSlotInlineValue(Get(ins[i]));
                        r = v.IsNumber ? (double?)v.Num : null;
                    }
                    return r != null ? r.Value : 0;
                };
                double mn = rd(0), mx = rd(1);
                int constant = UnityYamlParser.GetIntField(owner.Body, "constant") ?? 0;
                if (constant == 1)
                {
                    // 按 op fileID 确定性哈希烘成固定伪随机值（等价某次播放的合法取值）
                    string s = ownerID.Length > 7 ? ownerID.Substring(ownerID.Length - 7) : ownerID;
                    double sv;
                    double seedNum = (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out sv) && sv != 0) ? sv : 1;
                    double rr = Math.Abs((Math.Sin(seedNum * 12.9898) * 43758.5453) % 1);
                    double v = mn + (mx - mn) * rr;
                    return new Range(v, v);
                }
                return new Range(mn, mx);
            }
            var c = EvalConstOperatorFromInputSlot(slotID);
            if (c != null) return new Range(c.Value, c.Value);
            var vv = UnityYamlParser.GetSlotInlineValue(slot);
            double nn = vv.IsNumber ? vv.Num : 0;
            return new Range(nn, nn);
        }

        /// <summary>读 spawn 数值 slot，返回 number(Jval) / object(Jval) / null。对应 JS readSpawnNumberSlot。</summary>
        public Jval ReadSpawnNumberSlot(string slotID)
        {
            var slot = Get(slotID);
            if (slot == null) return null;
            var linked = UnityYamlParser.GetLinkedSlots(slot);
            if (linked.Count == 0)
            {
                var v = UnityYamlParser.GetSlotInlineValue(slot);
                if (v.IsNumber) return v;
                return v.IsObject ? v : null;
            }
            var evaluated = EvalConstOperatorFromInputSlot(slotID);
            if (evaluated != null) return Jval.Of(evaluated.Value);
            var vv = UnityYamlParser.GetSlotInlineValue(slot);
            return vv.IsNumber ? vv : null;
        }
    }
}
