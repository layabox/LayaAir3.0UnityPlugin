using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 的 spawn block 转换器（Stage 2.3c）：convertSpawnBlock。
    /// 依赖 byID + ReadSpawnNumberSlot（常量折叠层）。返回 Laya spawn block 的 props（Jval）。
    /// </summary>
    public partial class VfxConverter
    {
        /// <summary>转换 spawn block（ConstantRate/Burst/PeriodicBurst/VariableRate），返回 Laya spawn block 的 props。</summary>
        public Jval ConvertSpawnBlock(string cls, VfxEntry blockEntry)
        {
            var inputSlots = UnityYamlParser.GetRefArrayField(blockEntry.Body, "m_InputSlots");

            if (cls == "VFXSpawnerConstantRate")
            {
                var rate = SlotAt(inputSlots, 0);
                return Jval.Obj().Set("rate", IsNum(rate) ? rate.Num : 10);
            }

            if (cls == "VFXSpawnerBurst" || cls == "VFXSpawnerBurstOld")
            {
                int repeat = UnityYamlParser.GetIntField(blockEntry.Body, "repeat") ?? 0;
                var cnt = SlotAt(inputSlots, 0);
                var dlyOrPeriod = SlotAt(inputSlots, 1);

                // count ← Modulo(spawnState.loopIndex, N) 检测
                bool countFromLoopIndex = false;
                double countModulo = 0;
                if (inputSlots.Count > 0 && inputSlots[0] != null)
                {
                    var slot = Get(inputSlots[0]);
                    var linked = slot != null ? UnityYamlParser.GetLinkedSlots(slot) : new System.Collections.Generic.List<string>();
                    if (linked.Count > 0)
                    {
                        var linkSlot = Get(linked[0]);
                        string linkOpID = linkSlot != null ? UnityYamlParser.GetRefField(linkSlot.Body, "m_Owner") : null;
                        var linkOp = linkOpID != null ? Get(linkOpID) : null;
                        if (linkOp != null && Regex.IsMatch(linkOp.ClassType ?? "", "Modulo", RegexOptions.IgnoreCase))
                        {
                            var modIns = UnityYamlParser.GetRefArrayField(linkOp.Body, "m_InputSlots");
                            var aSlot = modIns.Count > 0 && modIns[0] != null ? Get(modIns[0]) : null;
                            var bSlot = modIns.Count > 1 && modIns[1] != null ? Get(modIns[1]) : null;
                            var aLinked = aSlot != null ? UnityYamlParser.GetLinkedSlots(aSlot) : new System.Collections.Generic.List<string>();
                            var aSrcSlot = aLinked.Count > 0 && aLinked[0] != null ? Get(aLinked[0]) : null;
                            string aSrcPropName = aSrcSlot != null ? UnityYamlParser.GetSlotPropertyName(aSrcSlot) : null;
                            if (Regex.IsMatch(aSrcPropName ?? "", "LoopIndex", RegexOptions.IgnoreCase))
                            {
                                var bVal = bSlot != null ? UnityYamlParser.GetSlotInlineValue(bSlot) : null;
                                if (bVal != null && bVal.IsNumber && bVal.Num > 0)
                                {
                                    countFromLoopIndex = true;
                                    countModulo = bVal.Num;
                                }
                            }
                        }
                    }
                }

                if (repeat == 1)
                {
                    // Periodic Burst
                    if (cnt != null && cnt.IsObject)
                        return Jval.Obj()
                            .Set("count", Jval.Obj().Set("x", Nz1(cnt, "x", 0)).Set("y", Nz1(cnt, "y", 10)))
                            .Set("delay", DelayObj(dlyOrPeriod));
                    double cntN = IsNum(cnt) ? cnt.Num : 10;
                    return Jval.Obj()
                        .Set("count", Jval.Obj().Set("x", cntN).Set("y", cntN))
                        .Set("delay", DelayObj(dlyOrPeriod));
                }

                // Single Burst (repeat=0)
                if (cnt != null && cnt.IsObject)
                    return Jval.Obj()
                        .Set("spawnMode", "Random").Set("count", 0)
                        .Set("countRange", Jval.Obj().Set("x", Nz1(cnt, "x", 0)).Set("y", Nz1(cnt, "y", 10)))
                        .Set("delayMode", "Constant").Set("delay", IsNum(dlyOrPeriod) ? dlyOrPeriod.Num : 0)
                        .Set("countFromLoopIndex", countFromLoopIndex).Set("countModulo", countModulo);
                return Jval.Obj()
                    .Set("spawnMode", "Constant").Set("count", IsNum(cnt) ? cnt.Num : 10)
                    .Set("delayMode", "Constant").Set("delay", IsNum(dlyOrPeriod) ? dlyOrPeriod.Num : 0)
                    .Set("countFromLoopIndex", countFromLoopIndex).Set("countModulo", countModulo);
            }

            if (cls == "VFXSpawnerPeriodicBurst")
            {
                var cnt = SlotAt(inputSlots, 0);
                var period = SlotAt(inputSlots, 1);
                return Jval.Obj()
                    .Set("spawnMode", "Constant").Set("count", IsNum(cnt) ? cnt.Num : 10)
                    .Set("delayMode", "Constant").Set("delay", IsNum(period) ? period.Num : 1);
            }

            if (cls == "VFXSpawnerVariableRate")
            {
                var rate = SlotAt(inputSlots, 0);
                return Jval.Obj().Set("rate", IsNum(rate) ? rate.Num : 10);
            }

            return Jval.Obj();
        }

        private Jval SlotAt(System.Collections.Generic.List<string> slots, int i)
        {
            return (i < slots.Count && slots[i] != null) ? ReadSpawnNumberSlot(slots[i]) : null;
        }
        private static bool IsNum(Jval v) { return v != null && v.IsNumber; }

        // delay: number → {x,y}; object → 原样; 否则 {x:1,y:1}
        private static Jval DelayObj(Jval d)
        {
            if (IsNum(d)) return Jval.Obj().Set("x", d.Num).Set("y", d.Num);
            if (d != null && d.IsObject) return d;
            return Jval.Obj().Set("x", 1).Set("y", 1);
        }
    }
}
