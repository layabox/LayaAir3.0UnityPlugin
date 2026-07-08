using System.Collections.Generic;

namespace LayaAir3.Converter
{
    /// <summary>
    /// .shadergraph 解析 + 索引。对应 JS unity-shader-to-laya.js 的 parseShadergraph + buildIndex。
    ///
    /// Unity ShaderGraph 序列化格式 = 多个独立 JSON 对象（各含 m_ObjectId）顺序拼接。
    /// 用深度扫描切分成一个个 JSON 段，逐段用插件 JSONObject.Create 解析，再 Jval.From 转成统一 DOM。
    /// </summary>
    public class SgIndex
    {
        public readonly Dictionary<string, Jval> ById = new Dictionary<string, Jval>();
        public Jval GraphData;
        public readonly List<Jval> Nodes = new List<Jval>();
        public readonly List<Jval> Slots = new List<Jval>();
        public readonly List<Jval> Properties = new List<Jval>();
        public readonly List<Jval> Targets = new List<Jval>();
        public List<Jval> Edges = new List<Jval>();

        /// <summary>取 m_Type 短名（去命名空间）。</summary>
        public static string ShortType(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            int i = t.LastIndexOf('.');
            return i < 0 ? t : t.Substring(i + 1);
        }

        public static string ShortType(Jval o)
        {
            return ShortType(o != null ? o.StrOf("m_Type") : null);
        }

        /// <summary>深度扫描切分多 JSON（对应 JS parseShadergraph）。</summary>
        public static List<Jval> ParseShadergraph(string text)
        {
            var objects = new List<Jval>();
            int depth = 0, start = -1;
            bool inStr = false, esc = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        string seg = text.Substring(start, i + 1 - start);
                        Jval jo = Jval.Parse(seg);
                        if (jo != null && jo.IsObject)
                            objects.Add(jo);
                        start = -1;
                    }
                }
            }
            return objects;
        }

        /// <summary>建索引（对应 JS buildIndex）。</summary>
        public static SgIndex Build(List<Jval> objects)
        {
            var idx = new SgIndex();
            foreach (var o in objects)
            {
                if (o == null || !o.IsObject) continue;
                string oid = o.StrOf("m_ObjectId");
                if (oid != null) idx.ById[oid] = o;
                string t = o.StrOf("m_Type") ?? "";
                string shortT = ShortType(t);
                if (t == "UnityEditor.ShaderGraph.GraphData") idx.GraphData = o;
                else if (EndsWith(shortT, "MaterialSlot")) idx.Slots.Add(o);
                else if (EndsWith(shortT, "Node")) idx.Nodes.Add(o);
                else if (EndsWith(shortT, "ShaderProperty") || EndsWith(shortT, "Keyword")) idx.Properties.Add(o);
                else if (EndsWith(shortT, "Target") || EndsWith(shortT, "SubTarget")) idx.Targets.Add(o);
            }
            // Edges 是 GraphData 顶层 inline 数组
            if (idx.GraphData != null)
            {
                var e = idx.GraphData.Get("m_Edges");
                if (e != null && e.IsArray) idx.Edges = e.Items;
            }
            return idx;
        }

        private static bool EndsWith(string s, string suffix)
        {
            return s != null && s.Length >= suffix.Length && s.EndsWith(suffix);
        }

        public Jval GetById(string id)
        {
            if (id == null) return null;
            Jval v;
            return ById.TryGetValue(id, out v) ? v : null;
        }
    }
}
