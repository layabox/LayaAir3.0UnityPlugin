using System.Collections.Generic;

namespace LayaAir3.Converter
{
    /// <summary>
    /// Laya IDE 的 VFX 节点定义数据（每个 typeId 的 inputs/outputs/affinity/properties 默认值）。
    /// C# 移植自 tools/laya-defs-loader.js：JS 版直接解析 IDE 的 VfxContextDefs/VfxBlockDefs/
    /// VfxOperatorDefs.ts；C# 版改为加载预导出的 `vfx-defs.json`（用 scratchpad/gendefs.js 从同源 .ts
    /// 生成，静态数据随 IDE 版本固定，随插件走）。
    ///
    /// JSON 结构：{ defaults:{typeId:[{name,default?}]}, inputs:{typeId:[id...]},
    ///             outputs:{typeId:[id...]}, affinity:{typeId:[stage...]} }
    /// </summary>
    public class LayaDefs
    {
        private readonly Dictionary<string, string[]> _inputs = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _outputs = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _affinity = new Dictionary<string, string[]>();
        // defaults: typeId → 有序 prop 列表 [{name, default(Jval 或 null=无default)}]
        private readonly Dictionary<string, List<KeyValuePair<string, Jval>>> _defaults = new Dictionary<string, List<KeyValuePair<string, Jval>>>();

        /// <summary>从预导出的 vfx-defs.json 文本加载 inputs/outputs/affinity/defaults。</summary>
        public static LayaDefs FromJson(string jsonText)
        {
            var d = new LayaDefs();
            var root = Jval.Parse(jsonText);
            LoadStrArrMap(root.Get("inputs"), d._inputs);
            LoadStrArrMap(root.Get("outputs"), d._outputs);
            LoadStrArrMap(root.Get("affinity"), d._affinity);
            var defs = root.Get("defaults");
            if (defs != null && defs.IsObject)
                foreach (var tid in defs.Keys)
                {
                    var arr = defs.Get(tid);
                    var list = new List<KeyValuePair<string, Jval>>();
                    if (arr != null && arr.IsArray)
                        foreach (var p in arr.Items)
                        {
                            string name = p.StrOf("name");
                            if (name == null) continue;
                            // default 键缺失 = JS 的 undefined；存在（含 JSON null）= 有默认值
                            Jval def = p.Has("default") ? p.Get("default") : null;
                            list.Add(new KeyValuePair<string, Jval>(name, def));
                        }
                    d._defaults[tid] = list;
                }
            return d;
        }

        private static void LoadStrArrMap(Jval src, Dictionary<string, string[]> dst)
        {
            if (src == null || !src.IsObject) return;
            foreach (var k in src.Keys)
            {
                var arr = src.Get(k);
                if (arr == null || !arr.IsArray) continue;
                var list = new List<string>();
                foreach (var it in arr.Items) list.Add(it.AsStr());
                dst[k] = list.ToArray();
            }
        }

        public string[] GetInputs(string typeId) { string[] v; return _inputs.TryGetValue(typeId, out v) ? v : null; }
        public string[] GetOutputs(string typeId) { string[] v; return _outputs.TryGetValue(typeId, out v) ? v : null; }
        public string[] GetAffinity(string typeId) { string[] v; return _affinity.TryGetValue(typeId, out v) ? v : null; }

        /// <summary>用 typeDef 默认值递归填充 props 缺失字段（不覆盖已有非 null 值）。原地修改并返回 props。</summary>
        public Jval FillDefaults(string typeId, Jval props)
        {
            List<KeyValuePair<string, Jval>> propDefs;
            if (!_defaults.TryGetValue(typeId, out propDefs)) return props;
            if (props == null) props = Jval.Obj();
            foreach (var p in propDefs)
            {
                if (p.Value == null) continue;                 // p.default === undefined
                var cur = props.Get(p.Key);
                if (cur != null && !cur.IsNull) continue;       // props[name] != null
                props.Set(p.Key, DeepClone(p.Value));
            }
            return props;
        }

        internal static Jval DeepClone(Jval v)
        {
            if (v == null) return Jval.Null();
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
    }
}
