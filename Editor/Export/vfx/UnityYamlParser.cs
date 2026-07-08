using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>Unity .vfx YAML 里的一个文档块（--- !u!&lt;typeNum&gt; &amp;&lt;fileID&gt;）。</summary>
    public class VfxEntry
    {
        public string FileID;
        public string TypeNum;
        public string ClassType;
        public string Body;
        /// <summary>subgraph 内联克隆出来的 entry（对应 JS _inlinedFromSub）：不走主图 properties[]/getProperty 展开。</summary>
        public bool InlinedFromSub;
    }

    /// <summary>references.RefIds 里的一条记录（rid → {class, data}）。</summary>
    public class RefIdEntry
    {
        public string Class;
        public string Data;
    }

    /// <summary>
    /// Unity .vfx (Unity YAML) 解析器。C# 移植自 unity-vfx-to-laya.js 的 parseEntries/parseRefIds +
    /// 一系列 getXField / slot 提取 / handedness 转换 helper。
    ///
    /// 这些函数直接在原始文本 body 上做正则提取（不建通用 YAML DOM）——Unity YAML 有特殊性，
    /// 通用 YAML 库不适用。结构化值（slot inline value / UIPos / transform）用 Jval 表示。
    /// </summary>
    public static class UnityYamlParser
    {
        // ── 分块 ──
        /// <summary>把整段 .vfx YAML 按文档分隔符切块，解析出所有 entry。对应 JS parseEntries。</summary>
        public static List<VfxEntry> ParseEntries(string yamlText, Dictionary<string, string> guidToClass)
        {
            var entries = new List<VfxEntry>();
            // 用正向前瞻切块（等价 JS 的 split(/^(?=--- !u!\d+ &\d+)/m)）
            var blocks = Regex.Split(yamlText, @"(?=^--- !u!\d+ &\d+)", RegexOptions.Multiline);
            var header = new Regex(@"^--- !u!(\d+) &(\d+).*\n([\s\S]*)");
            var guidRe = new Regex(@"m_Script:\s*\{[^}]*guid:\s*([0-9a-f]{32})");
            foreach (var block in blocks)
            {
                var hm = header.Match(block);
                if (!hm.Success) continue;
                string typeNum = hm.Groups[1].Value;
                string fileID = hm.Groups[2].Value;
                string body = hm.Groups[3].Value;
                var gm = guidRe.Match(body);
                string guid = gm.Success ? gm.Groups[1].Value : null;
                string classType;
                if (guid != null)
                {
                    string cls;
                    classType = (guidToClass != null && guidToClass.TryGetValue(guid, out cls)) ? cls : ("<unknown:" + guid.Substring(0, 8) + ">");
                }
                else classType = "<no-script:type" + typeNum + ">";
                entries.Add(new VfxEntry { FileID = fileID, TypeNum = typeNum, ClassType = classType, Body = body });
            }
            return entries;
        }

        // ── 字段提取 ──
        public static string GetStringField(string body, string name)
        {
            var m = Regex.Match(body, "^\\s*" + Regex.Escape(name) + ":\\s*(.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        public static int? GetIntField(string body, string name)
        {
            string v = GetStringField(body, name);
            int r;
            if (v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return v != null ? (int?)ParseIntLoose(v) : null;
        }
        public static double? GetFloatField(string body, string name)
        {
            string v = GetStringField(body, name);
            double r;
            if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            return null;
        }
        // 读嵌套 parentField 下最近的 m_SerializableObject 标量。
        public static double? GetNestedSerializableNumber(string body, string parentField)
        {
            var m = Regex.Match(body, "^\\s*" + Regex.Escape(parentField) + ":\\s*\\r?\\n[\\s\\S]*?^\\s*m_SerializableObject:\\s*(.+?)\\s*$", RegexOptions.Multiline);
            if (!m.Success) return null;
            double v;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }
        public static string GetRefField(string body, string name)
        {
            var m = Regex.Match(body, "^\\s*" + Regex.Escape(name) + ":\\s*\\{fileID:\\s*(-?\\d+)", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }
        // 注意：rid 是 19 位 >2^53 大整数；用 double 解析以复刻 JS parseInt 的精度丢失（相邻 rid 会塌成同一 double，
        //   导致 RefIds Map 键碰撞，这正是 Unity 17.3 composed-particle topology/shading 不被重写的原因）。
        public static double? GetNestedRid(string body, string fieldName)
        {
            var m = Regex.Match(body, "^\\s*" + Regex.Escape(fieldName) + ":\\s*\\n\\s+rid:\\s*(\\d+)", RegexOptions.Multiline);
            if (!m.Success) return null;
            double r; return double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) ? (double?)r : null;
        }

        /// <summary>解析 references.RefIds 块为 rid(double) 到 {class, data} 的映射（键用 double，复刻 JS parseInt 的碰撞）。</summary>
        public static Dictionary<double, RefIdEntry> ParseRefIds(string body)
        {
            var outMap = new Dictionary<double, RefIdEntry>();
            var refIdsMatch = Regex.Match(body, @"^\s*RefIds:\s*\n([\s\S]*?)(?=^\S|^---|$(?![\s\S]))", RegexOptions.Multiline);
            if (!refIdsMatch.Success) return outMap;
            string refIdsBlock = refIdsMatch.Groups[1].Value;
            var parts = Regex.Split(refIdsBlock, @"^\s*-\s*rid:\s*(\d+)\s*$", RegexOptions.Multiline);
            for (int i = 1; i < parts.Length; i += 2)
            {
                double rid; if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rid)) continue;
                string entryBody = (i + 1 < parts.Length) ? parts[i + 1] : "";
                var classMatch = Regex.Match(entryBody, @"^\s*type:\s*\{class:\s*(\w+)", RegexOptions.Multiline);
                var dataMatch = Regex.Match(entryBody, @"^\s*data:\s*\n([\s\S]*)", RegexOptions.Multiline);
                outMap[rid] = new RefIdEntry
                {
                    Class = classMatch.Success ? classMatch.Groups[1].Value : null,
                    Data = dataMatch.Success ? dataMatch.Groups[1].Value : entryBody,
                };
            }
            return outMap;
        }

        public static List<string> GetRefArrayField(string body, string name)
        {
            var outList = new List<string>();
            var m = Regex.Match(body, "^\\s*" + Regex.Escape(name) + ":\\s*\\n((?:\\s*-\\s*\\{fileID:\\s*-?\\d+[^}]*\\}\\s*\\n)*)", RegexOptions.Multiline);
            if (!m.Success) return outList;
            foreach (Match im in Regex.Matches(m.Groups[1].Value, @"\{fileID:\s*(-?\d+)"))
                outList.Add(im.Groups[1].Value);
            return outList;
        }

        public static List<string> GetOutputFlowSlot(string body)
        {
            var outList = new List<string>();
            var m = Regex.Match(body, @"^ {2}m_OutputFlowSlot:\s*\n([\s\S]*?)(?=^ {2}m_\w+:|^---|$(?![\s\S]))", RegexOptions.Multiline);
            if (!m.Success) return outList;
            foreach (Match cm in Regex.Matches(m.Groups[1].Value, @"context:\s*\{fileID:\s*(-?\d+)"))
                outList.Add(cm.Groups[1].Value);
            return outList;
        }

        public static Jval GetUIPos(string body)
        {
            var m = Regex.Match(body, @"^\s*m_UIPosition:\s*\{x:\s*(-?\d+\.?\d*),\s*y:\s*(-?\d+\.?\d*)\}", RegexOptions.Multiline);
            if (!m.Success) return Jval.Obj().Set("x", 0).Set("y", 0);
            return Jval.Obj()
                .Set("x", ParseD(m.Groups[1].Value))
                .Set("y", ParseD(m.Groups[2].Value));
        }

        /// <summary>读 slot 的 inline 值（master slot 的 m_SerializableObject），返回 Jval（Null/Bool/Number/解析对象/String）。</summary>
        public static Jval GetSlotInlineValue(VfxEntry slotEntry)
        {
            if (slotEntry == null) return Jval.Null();
            var m = Regex.Match(slotEntry.Body, @"m_SerializableObject:[ \t]*(.*)$", RegexOptions.Multiline);
            if (!m.Success) return Jval.Null();
            string raw = m.Groups[1].Value.Trim();
            if ((raw.StartsWith("'") && raw.EndsWith("'")) || (raw.StartsWith("\"") && raw.EndsWith("\"")))
                raw = raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : "";
            if (raw == "" || raw == "null") return Jval.Null();
            if (raw == "True" || raw == "true") return Jval.Of(true);
            if (raw == "False" || raw == "false") return Jval.Of(false);
            if (Regex.IsMatch(raw, @"^-?\d+\.?\d*(?:[eE]-?\d+)?$"))
                return Jval.Of(ParseD(raw));
            // JSON 值（curve/gradient 等）
            if (raw.Length > 0 && (raw[0] == '{' || raw[0] == '['))
            {
                // 注意：与 JS JSON.parse 一致，裸 Infinity/NaN 是非法 JSON（Unity broken-tangent 曲线的
                // inTangent/outTangent 会序列化成 Infinity）→ 整体按"非 JSON"处理返回原串，
                // 让上游把它当无值走 fillDefaults（Jval.Parse 是宽松解析器，会解析出部分帧造成偏差）。
                if (Regex.IsMatch(raw, @"(?<![\w""])(-?Infinity|NaN)(?![\w""])")) return Jval.Of(raw);
                var parsed = Jval.Parse(raw);
                if (parsed != null && !parsed.IsNull) return parsed;
            }
            return Jval.Of(raw);
        }

        public static string GetSlotType(VfxEntry slotEntry)
        {
            if (slotEntry == null) return null;
            var m = Regex.Match(slotEntry.Body, @"m_MasterData:\s*\n[\s\S]*?m_SerializableType:\s*([^,]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static string GetSlotPropertyName(VfxEntry slotEntry)
        {
            if (slotEntry == null) return null;
            var m = Regex.Match(slotEntry.Body, @"m_Property:\s*\n\s*name:\s*(.+?)\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static string GetSlotTypeName(VfxEntry slotEntry)
        {
            if (slotEntry == null) return null;
            var m = Regex.Match(slotEntry.Body, @"m_SerializableType:\s*(\S+)");
            if (!m.Success) return null;
            string t = m.Groups[1].Value;
            if (t.Contains("System.Single")) return "float";
            if (t.Contains("UnityEngine.Vector4")) return "vec4";
            if (t.Contains("UnityEngine.Vector3")) return "vec3";
            if (t.Contains("UnityEngine.Vector2")) return "vec2";
            if (t.Contains("UnityEngine.Color")) return "vec4";
            if (t.Contains("UnityEngine.Gradient")) return "Gradient";
            if (t.Contains("UnityEngine.AnimationCurve")) return "Curve";
            if (t.Contains("UnityEngine.Texture2D")) return "Texture2D";
            if (t.Contains("System.Boolean")) return "bool";
            if (t.Contains("System.UInt32") || t.Contains("System.Int32")) return "int";
            return null;
        }

        public static string GetSlotMaster(VfxEntry slotEntry)
        {
            if (slotEntry == null) return null;
            var m = Regex.Match(slotEntry.Body, @"m_MasterSlot:\s*\{fileID:\s*(-?\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static List<string> GetLinkedSlots(VfxEntry slotEntry)
        {
            var outList = new List<string>();
            if (slotEntry == null) return outList;
            var m = Regex.Match(slotEntry.Body, @"m_LinkedSlots:\s*\n((?:\s*-\s*\{fileID:\s*-?\d+\}\s*\n)*)");
            if (!m.Success) return outList;
            foreach (Match im in Regex.Matches(m.Groups[1].Value, @"\{fileID:\s*(-?\d+)"))
                outList.Add(im.Groups[1].Value);
            return outList;
        }

        public static string GetSlotOwner(VfxEntry slotEntry)
        {
            if (slotEntry == null) return null;
            var m = Regex.Match(slotEntry.Body, @"m_Owner:\s*\{fileID:\s*(-?\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string GetExposedName(VfxEntry entry)
        {
            if (entry == null) return null;
            var m = Regex.Match(entry.Body, @"m_ExposedName:\s*(.+?)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>沿 input slot → linkedSlot[0] → master → owner(VFXParameter) 解析出 m_ExposedName。</summary>
        public static string ResolveExposedNameForInputSlot(VfxEntry slotEntry, System.Func<string, VfxEntry> lookupByID)
        {
            if (slotEntry == null) return null;
            var linked = GetLinkedSlots(slotEntry);
            if (linked.Count == 0) return null;
            var upstreamSlot = lookupByID(linked[0]);
            if (upstreamSlot == null) return null;
            string masterID = GetSlotMaster(upstreamSlot) ?? linked[0];
            var masterSlot = lookupByID(masterID);
            if (masterSlot == null) return null;
            string ownerID = GetSlotOwner(masterSlot);
            if (ownerID == null) return null;
            var ownerEntry = lookupByID(ownerID);
            if (ownerEntry == null) return null;
            return GetExposedName(ownerEntry);
        }

        // ── handedness（Unity LHS → Laya RHS），原地修改 Jval slot 值 ──
        /// <summary>把 slot 值从 Unity 左手坐标系转成 Laya 右手坐标系（原地修改 Jval）。</summary>
        public static void ConvertUnityToLayaHandedness(Jval v)
        {
            if (v == null || !v.IsObject) return;
            var tf = v.Get("transform");
            if (tf != null && tf.IsObject) FlipTransform(tf);
            foreach (var key in new[] { "circle", "sphere", "cone", "torus" })
            {
                var sub = v.Get(key);
                if (sub != null && sub.IsObject && sub.Get("transform") != null) FlipTransform(sub.Get("transform"));
            }
            if (v.Get("center") != null || v.Get("angle") != null || v.Get("angles") != null) FlipFlatTransform(v);
        }

        public static void FlipTransform(Jval tf)
        {
            if (tf == null) return;
            var pos = tf.Get("position");
            if (pos != null && pos.IsObject && pos.Get("z") != null && pos.Get("z").IsNumber)
                pos.Set("z", -pos.NumOf("z"));
            foreach (var angKey in new[] { "angles", "rotation" })
            {
                var ang = tf.Get(angKey);
                if (ang != null && ang.IsObject)
                {
                    if (ang.Get("x") != null && ang.Get("x").IsNumber) ang.Set("x", -ang.NumOf("x"));
                    if (ang.Get("y") != null && ang.Get("y").IsNumber) ang.Set("y", -ang.NumOf("y"));
                }
            }
        }

        public static void FlipFlatTransform(Jval v)
        {
            var center = v.Get("center");
            if (center != null && center.IsObject && center.Get("z") != null && center.Get("z").IsNumber)
                center.Set("z", -center.NumOf("z"));
            foreach (var angKey in new[] { "angle", "angles" })
            {
                var ang = v.Get(angKey);
                if (ang != null && ang.IsObject)
                {
                    if (ang.Get("x") != null && ang.Get("x").IsNumber) ang.Set("x", -ang.NumOf("x"));
                    if (ang.Get("y") != null && ang.Get("y").IsNumber) ang.Set("y", -ang.NumOf("y"));
                }
            }
        }

        // ── 数值解析工具 ──
        private static double ParseD(string s)
        {
            double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }
        private static int ParseIntLoose(string s)
        {
            // 模仿 JS parseInt：读前缀数字
            var m = Regex.Match(s, @"^\s*(-?\d+)");
            int r; return m.Success && int.TryParse(m.Groups[1].Value, out r) ? r : 0;
        }
    }
}
