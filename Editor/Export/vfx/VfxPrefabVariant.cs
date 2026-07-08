using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// prefab-variant 转换：把 Unity prefab 上 VisualEffect 组件的属性覆盖
    /// （纹理 / mesh / 数值 / gradient / initialEventName）烘进已转换好的 base .laya.vfx，
    /// 产出按 prefab 名命名的变体 .laya.vfx。
    ///
    /// 1:1 移植自 LayaVFXSample/tools/convert-uni-vfx.js 的 phasePrefabVariant 链
    /// （parseVisualEffectBlock / parsePropertySheetArray / parse*Value /
    ///   applyPrefabOverridesToVfx / 七个 sync 函数），bug-for-bug 保持与 JS 产物一致。
    ///
    /// 零 Unity 依赖：只用 System.*；JSON 走同目录体系的 Jval。
    /// 文件遍历 / 写盘由调用方（ConverterWindow 等）负责；本类只做「文本进 → 文本出」。
    /// </summary>
    public static class VfxPrefabVariant
    {
        // ─── 数据结构 ────────────────────────────────────────────

        /// <summary>m_PropertySheet 子数组里的一条 override（只收 m_Overridden=1 的）。</summary>
        public class SheetItem
        {
            public string Name;   // 属性名（m_Name）
            public Jval Value;    // 解析后的值：number / bool / [x,y(,z,w)] / {guid} / {stops:[...]}
        }

        /// <summary>从 prefab YAML 提取出的 VisualEffect 组件块。</summary>
        public class VisualEffectBlock
        {
            public string AssetGuid;           // m_Asset 的 guid（base .vfx）
            public string InitialEventName;    // 非空才采用（JS：不看 m_InitialEventNameOverriden 标志）
            public List<SheetItem> Floats = new List<SheetItem>();
            public List<SheetItem> Vec2s = new List<SheetItem>();
            public List<SheetItem> Vec3s = new List<SheetItem>();
            public List<SheetItem> Vec4s = new List<SheetItem>();
            public List<SheetItem> Uints = new List<SheetItem>();
            public List<SheetItem> Ints = new List<SheetItem>();
            public List<SheetItem> Bools = new List<SheetItem>();
            public List<SheetItem> Gradients = new List<SheetItem>();
            public List<SheetItem> NamedObjects = new List<SheetItem>();
        }

        /// <summary>applyPrefabOverridesToVfx 的统计结果（对应 JS 返回的 {applied, missing}）。</summary>
        public struct ApplyResult
        {
            public int Applied;
            public int Missing;
        }

        // 特殊 mesh 的 size 缩放补偿表（uuid → factor），与 JS SPECIAL_MESH_SCALE_UUIDS 一致。
        // f042c66d…: AlienStatue fbx 子 mesh @lm0（fbxtool 5x 过大），VFX 雕像统一用这个。
        // 7c179d7c…: 已废弃的 AlienStatue-Statue.lm（烤躺下），恒不命中，保留仅作记录。
        private static readonly Dictionary<string, double> SPECIAL_MESH_SCALE_UUIDS = new Dictionary<string, double>
        {
            { "f042c66d-1de8-44b5-af1b-0058442f1316", 0.2 },
            { "7c179d7c-5ba7-4ba6-a7d2-27b0e41f94c0", 0.01 },
        };

        // ─── 公开入口 ────────────────────────────────────────────

        /// <summary>
        /// 一站式：base .laya.vfx JSON 文本 + prefab YAML 文本 + mapping（guid → {layaUuid,...}）
        /// → 变体 .laya.vfx JSON 文本（JSON.stringify(x, null, 2) 风格）。
        /// prefab 里没有 VisualEffect 组件时返回 null（对应 JS phase 的 skipped）。
        /// </summary>
        public static string ConvertOne(string vfxJsonText, string prefabYamlText, Dictionary<string, Jval> mapping)
        {
            var vfx = Jval.Parse(vfxJsonText);
            var applied = ApplyPrefabOverrides(vfx, prefabYamlText, guid =>
            {
                Jval m;
                return (mapping != null && guid != null && mapping.TryGetValue(guid, out m)) ? m : null;
            });
            return applied == null ? null : applied.Serialize(2);
        }

        /// <summary>
        /// 核心入口：把 prefab YAML 里的 VisualEffect 覆盖应用到 vfxJson（原地修改），
        /// 编排顺序与 JS phasePrefabVariant 完全一致（apply → 七个 sync）。
        /// 找不到 VisualEffect 块返回 null，否则返回 vfxJson 本身。
        /// </summary>
        public static Jval ApplyPrefabOverrides(Jval vfxJson, string prefabYamlText, Func<string, Jval> assetMappingLookup)
        {
            var vfxBlock = ParseVisualEffectBlock(prefabYamlText);
            if (vfxBlock == null) return null;

            // apply overrides
            ApplyPrefabOverridesToVfx(vfxJson, vfxBlock, assetMappingLookup);
            // 把 override 的 Mesh 属性同步到 props.mesh 渲染字段 + 补 size 缩放（自动化 Statue 模型）
            SyncMeshRenderFieldFromProperty(vfxJson);
            // 把 override 的纹理属性（SwarmTexture/PatternTexture 等）同步到 output 的 props.mainTexture
            SyncMainTextureFromProperty(vfxJson);
            // 把 override 的 flipbook vec2 属性（SwarmFlipbookSizeXY 等）同步到 output 的 props.flipbookSize
            SyncFlipbookFromProperty(vfxJson);
            // 据（override 后）flipbookSize 修同系统 init 的 texIndex 随机 max = 总帧数（否则只显帧0）
            SyncTexIndexMaxFromFlipbook(vfxJson);
            // 给 materialize swarm 的 Circle position 注入 source-time 上扫（sweep）+缩半径（否则堆成平亮盘）
            InjectSwarmSweep(vfxJson, vfxBlock);
            // 把绑定的纹理属性（override 后）默认镜像进 shaderPropertyDefaults（自动化 8 张纹理）
            MirrorTextureBindingsToDefaults(vfxJson);
            // 把暴露属性注册表（override 后权威值）同步进内联 VFXParameter.defaultValue（修 bool/color 被烘成 false/null）
            SyncExposedDefaultsToInlineNodes(vfxJson);

            return vfxJson;
        }

        // ─── 路径规则（对应 phasePrefabVariant 编排里的文件名逻辑） ───

        /// <summary>base Unity .vfx 相对路径 → base .laya.vfx 相对路径（JS：baseRel.replace(/\.vfx$/i, ".laya.vfx")）。</summary>
        public static string BaseLayaRelPath(string baseVfxRelPath)
        {
            return Regex.Replace(baseVfxRelPath.Replace('\\', '/'), @"\.vfx$", ".laya.vfx", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// prefab 相对路径 → 变体输出相对路径：prefab 子目录 + prefab 名 + ".laya.vfx"
        /// （JS：path.join(path.dirname(prefabRel), path.parse(prefabRel).name + ".laya.vfx")）。
        /// </summary>
        public static string VariantOutRelPath(string prefabRelPath)
        {
            string rel = prefabRelPath.Replace('\\', '/');
            int slash = rel.LastIndexOf('/');
            string dir = slash >= 0 ? rel.Substring(0, slash + 1) : "";
            string fileName = rel.Substring(slash + 1);
            int dot = fileName.LastIndexOf('.');
            string name = dot >= 0 ? fileName.Substring(0, dot) : fileName;   // path.parse().name
            return dir + name + ".laya.vfx";
        }

        /// <summary>
        /// 合并多个 mapping JSON 文本：按文件名字母序（Ordinal，与 JS Array.sort 默认一致）排序后
        /// 依次 Object.assign，后者覆盖前者 —— 与主转换器 _loadAssetMappings 语义一致。
        /// </summary>
        public static Dictionary<string, Jval> MergeMappings(IEnumerable<KeyValuePair<string, string>> fileNameToJsonText)
        {
            var merged = new Dictionary<string, Jval>();
            foreach (var kv in fileNameToJsonText.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var j = Jval.Parse(kv.Value);
                if (j == null || !j.IsObject) continue;
                foreach (var guid in j.Keys.ToList()) merged[guid] = j.Get(guid);
            }
            return merged;
        }

        // ─── prefab YAML 解析 ────────────────────────────────────

        /// <summary>
        /// 从 prefab YAML 里抽 VisualEffect 段（m_Asset / m_InitialEventName / m_PropertySheet）。
        /// 对应 JS parseVisualEffectBlock。
        /// </summary>
        public static VisualEffectBlock ParseVisualEffectBlock(string yamlText)
        {
            // VisualEffect entry header: `--- !u!2083052967 &<id>\nVisualEffect:`
            // （JS 无 m 标志的 $ = 输入绝对结尾 → C# 用 \z）
            var m = Regex.Match(yamlText, @"---\s*!u!2083052967\s+&\d+\s*\nVisualEffect:\s*\n([\s\S]*?)(?=\n---\s*!u!|\z)");
            if (!m.Success) return null;
            string body = m.Groups[1].Value;

            var assetM = Regex.Match(body, @"m_Asset:\s*\{[^}]*guid:\s*([a-f0-9]+)");
            if (!assetM.Success) return null;
            string assetGuid = assetM.Groups[1].Value;

            // prefab 的 m_InitialEventName 非空即采用，不看 m_InitialEventNameOverriden 标志：
            // UNI 包的 prefab 普遍是 Overriden=0 + 有效值（如 create）。空值回退 asset 默认。
            // （值用 [^\r\n]* 抓整行，与 JS 一致；JS 行尾 $ 在 LF 文件下等价省略）
            var evtM = Regex.Match(body, @"^\s*m_InitialEventName:[ \t]*([^\r\n]*)", RegexOptions.Multiline);
            string initialEventName = null;
            if (evtM.Success)
            {
                string v = evtM.Groups[1].Value.Trim();
                if (v.Length > 0) initialEventName = v;
            }

            var blk = new VisualEffectBlock { AssetGuid = assetGuid, InitialEventName = initialEventName };
            // m_PropertySheet 段解析 — 各 sub array
            blk.Floats = ParsePropertySheetArray(body, "m_Float", ParseFloatValue);
            blk.Vec2s = ParsePropertySheetArray(body, "m_Vector2f", ParseVec2Value);
            blk.Vec3s = ParsePropertySheetArray(body, "m_Vector3f", ParseVec3Value);
            blk.Vec4s = ParsePropertySheetArray(body, "m_Vector4f", ParseVec4Value);
            blk.Uints = ParsePropertySheetArray(body, "m_Uint", ParseFloatValue);
            blk.Ints = ParsePropertySheetArray(body, "m_Int", ParseFloatValue);
            blk.Bools = ParsePropertySheetArray(body, "m_Bool", ParseBoolValue);
            blk.Gradients = ParsePropertySheetArray(body, "m_Gradient", ParseGradientValue);
            blk.NamedObjects = ParsePropertySheetArray(body, "m_NamedObject", ParseNamedObjectValue);
            return blk;
        }

        /// <summary>
        /// 通用 m_PropertySheet 子数组解析：抓 m_&lt;Name&gt;: \n m_Array: \n [items...]。
        /// 每个 item：- m_Value: &lt;...&gt;\n m_Name: &lt;name&gt;\n m_Overridden: 0/1，只取 overridden=1 的。
        /// 对应 JS parsePropertySheetArray。
        /// </summary>
        public static List<SheetItem> ParsePropertySheetArray(string body, string arrayName, Func<string, Jval> valueParser)
        {
            var items = new List<SheetItem>();
            // section start: `    m_Float:\n      m_Array:\n`（典型 8 空格缩进体系里的 4/6 空格层级）
            // 结尾 lookahead：下一个同级 m_Xxx、新 entry header (---)、或文件绝对结尾（JS 的 $(?![\s\S]) → \z）
            var re = new Regex("^\\s{4}" + Regex.Escape(arrayName) + ":\\s*\\n\\s{6}m_Array:\\s*\\n([\\s\\S]*?)(?=^\\s{4}m_\\w+:|^---|\\z)", RegexOptions.Multiline);
            var m = re.Match(body);
            if (!m.Success) return items;
            string itemsBody = m.Groups[1].Value;
            // 拆 items：每个 item 以 `      - m_Value:` 开头
            var itemRe = new Regex(@"^\s{6}- m_Value:([\s\S]*?)(?=^\s{6}- m_Value:|\z)", RegexOptions.Multiline);
            foreach (Match im in itemRe.Matches(itemsBody))
            {
                string itemBody = im.Groups[1].Value;
                var nameM = Regex.Match(itemBody, @"m_Name:\s*(.+)$", RegexOptions.Multiline);
                var overM = Regex.Match(itemBody, @"m_Overridden:\s*(\d)");
                if (!nameM.Success) continue;
                bool overridden = overM.Success && overM.Groups[1].Value == "1";
                if (!overridden) continue;   // 只取 explicitly overridden 的
                var value = valueParser(itemBody);
                if (value != null)   // JS 的 undefined → 这里用 null 表示「解析失败，跳过」
                {
                    items.Add(new SheetItem { Name = nameM.Groups[1].Value.Trim(), Value = value });
                }
            }
            return items;
        }

        // 数字字面量（与 JS 各 parse*Value 的 regex 完全一致，带指数）
        private const string NUM = @"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?";

        public static Jval ParseFloatValue(string itemBody)
        {
            var m = Regex.Match(itemBody, @"^\s*(" + NUM + ")");
            return m.Success ? Jval.Of(JsParseNumber(m.Groups[1].Value)) : null;
        }

        public static Jval ParseBoolValue(string itemBody)
        {
            var m = Regex.Match(itemBody, @"^\s*(0|1)");
            return m.Success ? Jval.Of(m.Groups[1].Value == "1") : null;
        }

        public static Jval ParseVec2Value(string itemBody)
        {
            var m = Regex.Match(itemBody, @"\{x:\s*(" + NUM + @"),\s*y:\s*(" + NUM + @")\}");
            return m.Success ? Jval.Arr(Jval.Of(JsParseNumber(m.Groups[1].Value)), Jval.Of(JsParseNumber(m.Groups[2].Value))) : null;
        }

        public static Jval ParseVec3Value(string itemBody)
        {
            var m = Regex.Match(itemBody, @"\{x:\s*(" + NUM + @"),\s*y:\s*(" + NUM + @"),\s*z:\s*(" + NUM + @")\}");
            return m.Success ? Jval.Arr(Jval.Of(JsParseNumber(m.Groups[1].Value)), Jval.Of(JsParseNumber(m.Groups[2].Value)), Jval.Of(JsParseNumber(m.Groups[3].Value))) : null;
        }

        public static Jval ParseVec4Value(string itemBody)
        {
            var m = Regex.Match(itemBody, @"\{x:\s*(" + NUM + @"),\s*y:\s*(" + NUM + @"),\s*z:\s*(" + NUM + @"),\s*w:\s*(" + NUM + @")\}");
            return m.Success ? Jval.Arr(Jval.Of(JsParseNumber(m.Groups[1].Value)), Jval.Of(JsParseNumber(m.Groups[2].Value)), Jval.Of(JsParseNumber(m.Groups[3].Value)), Jval.Of(JsParseNumber(m.Groups[4].Value))) : null;
        }

        public static Jval ParseNamedObjectValue(string itemBody)
        {
            // - m_Value: {fileID: ..., guid: <guid>, type: 3}
            var m = Regex.Match(itemBody, @"\{[^}]*guid:\s*([a-f0-9]+)");
            return m.Success ? Jval.Obj().Set("guid", m.Groups[1].Value) : null;
        }

        // 一维 stop 的中间结构（gradient 解析用）
        private struct ColorStop { public double t, r, g, b; }
        private struct AlphaStop { public double t, a; }

        /// <summary>Unity gradient YAML → Laya stops 数组（{stops:[{t,color:{r,g,b,a}}...]}）。对应 JS parseGradientValue。</summary>
        public static Jval ParseGradientValue(string itemBody)
        {
            // 解析 key0-7 颜色、ctime0-7 / atime0-7 时间戳 + m_NumColorKeys / m_NumAlphaKeys
            var numColorM = Regex.Match(itemBody, @"m_NumColorKeys:\s*(\d+)");
            var numAlphaM = Regex.Match(itemBody, @"m_NumAlphaKeys:\s*(\d+)");
            if (!numColorM.Success || !numAlphaM.Success) return null;
            int numColor = int.Parse(numColorM.Groups[1].Value, CultureInfo.InvariantCulture);
            int numAlpha = int.Parse(numAlphaM.Groups[1].Value, CultureInfo.InvariantCulture);

            var colorStops = new List<ColorStop>();
            var alphaStops = new List<AlphaStop>();
            for (int i = 0; i < 8; i++)
            {
                // 注意：颜色数字 regex 与 JS 一样不带指数（-?\d+(?:\.\d+)?）
                var keyM = Regex.Match(itemBody, "key" + i + @":\s*\{r:\s*(-?\d+(?:\.\d+)?),\s*g:\s*(-?\d+(?:\.\d+)?),\s*b:\s*(-?\d+(?:\.\d+)?),\s*a:\s*(-?\d+(?:\.\d+)?)\}");
                if (!keyM.Success) break;
                // ctime/atime 在 key 出现位置之后查（与 JS itemBody.slice(keyM.index) 一致）
                string tail = itemBody.Substring(keyM.Index);
                var ctimeM = Regex.Match(tail, "ctime" + i + @":\s*(\d+)");
                var atimeM = Regex.Match(tail, "atime" + i + @":\s*(\d+)");
                if (i < numColor && ctimeM.Success)
                {
                    colorStops.Add(new ColorStop
                    {
                        t = JsParseNumber(ctimeM.Groups[1].Value) / 65535,
                        r = JsParseNumber(keyM.Groups[1].Value),
                        g = JsParseNumber(keyM.Groups[2].Value),
                        b = JsParseNumber(keyM.Groups[3].Value),
                    });
                }
                if (i < numAlpha && atimeM.Success)
                {
                    alphaStops.Add(new AlphaStop
                    {
                        t = JsParseNumber(atimeM.Groups[1].Value) / 65535,
                        a = JsParseNumber(keyM.Groups[4].Value),
                    });
                }
            }
            if (colorStops.Count == 0) return null;
            // JS Array.sort 是稳定排序 → 用 OrderBy（稳定），不能用 List.Sort（不稳定）
            colorStops = colorStops.OrderBy(s => s.t).ToList();
            alphaStops = alphaStops.OrderBy(s => s.t).ToList();

            // 合并 color + alpha 时间戳，在每个 t 上 lerp 另一轨道值
            var tSet = new HashSet<double>();
            foreach (var s in colorStops) tSet.Add(JsToFixed6(s.t));
            foreach (var s in alphaStops) tSet.Add(JsToFixed6(s.t));
            var ts = tSet.ToList();
            ts.Sort();

            Func<double, double[]> sampleColor = (t) =>
            {
                if (t <= colorStops[0].t) { var s = colorStops[0]; return new[] { s.r, s.g, s.b }; }
                if (t >= colorStops[colorStops.Count - 1].t) { var s = colorStops[colorStops.Count - 1]; return new[] { s.r, s.g, s.b }; }
                for (int i = 0; i < colorStops.Count - 1; i++)
                {
                    var a = colorStops[i]; var b = colorStops[i + 1];
                    if (t >= a.t && t <= b.t)
                    {
                        double f = (t - a.t) / (b.t - a.t);
                        return new[] { a.r + (b.r - a.r) * f, a.g + (b.g - a.g) * f, a.b + (b.b - a.b) * f };
                    }
                }
                return new double[] { 1, 1, 1 };
            };
            Func<double, double> sampleAlpha = (t) =>
            {
                if (alphaStops.Count == 0) return 1;
                if (t <= alphaStops[0].t) return alphaStops[0].a;
                if (t >= alphaStops[alphaStops.Count - 1].t) return alphaStops[alphaStops.Count - 1].a;
                for (int i = 0; i < alphaStops.Count - 1; i++)
                {
                    var a = alphaStops[i]; var b = alphaStops[i + 1];
                    if (t >= a.t && t <= b.t)
                    {
                        double f = (t - a.t) / (b.t - a.t);
                        return a.a + (b.a - a.a) * f;
                    }
                }
                return 1;
            };

            var stops = Jval.Arr();
            foreach (double t in ts)
            {
                var rgb = sampleColor(t);
                double a = sampleAlpha(t);
                stops.Push(Jval.Obj()
                    .Set("t", t)
                    .Set("color", Jval.Obj().Set("r", rgb[0]).Set("g", rgb[1]).Set("b", rgb[2]).Set("a", a)));
            }
            return Jval.Obj().Set("stops", stops);
        }

        // ─── 覆盖应用（对应 JS applyPrefabOverridesToVfx） ─────────

        // 类型标签（对应 JS allItems 的 type 字段）
        private class TypedItem
        {
            public string Name;
            public string Type;
            public Jval Value;
        }

        /// <summary>把 prefab override 数据 apply 到 base .laya.vfx JSON（in-place）。</summary>
        public static ApplyResult ApplyPrefabOverridesToVfx(Jval vfxJson, VisualEffectBlock vfxBlock, Func<string, Jval> mapping)
        {
            var result = new ApplyResult { Applied = 0, Missing = 0 };
            var properties = vfxJson.Get("properties");
            if (properties == null || !properties.IsArray) return result;

            // 建 name → property entry 反查（JS Map：后者覆盖前者）
            var propByName = BuildPropByName(vfxJson);

            // 建 block id / operator id → 实体 反查（用于把 gradient 覆盖刷到绑定它的内联 gradient）
            var blockById = new Dictionary<string, Jval>();
            foreach (var c in ArrItems(vfxJson.Get("contexts")))
                foreach (var b in ArrItems(c.Get("blocks")))
                    blockById[JsString(b.Get("id"))] = b;
            var opById = new Dictionary<string, Jval>();
            foreach (var o in ArrItems(vfxJson.Get("operators")))
                opById[JsString(o.Get("id"))] = o;

            // 找绑定到某 gradient property 的两类目标：
            //   1) setAttributeCurve/setAttribute(color) block —— getProperty 连到 block_<id>_value（compute shader 用 block 内联 gradient）
            //   2) sampleGradient 算子 —— getProperty 连到算子节点（ShaderGraph 输出走 evaluator 取算子内联 gradient）
            // 两者运行时都用各自的「内联 gradient」，所以 gradient property 覆盖必须同步刷进去，
            // 否则 strip/粒子/墙面保持 base 灰白色。
            Func<string, Tuple<List<Jval>, List<Jval>>> gradientTargetsBoundTo = (propName) =>
            {
                var blocks = new List<Jval>();
                var gops = new List<Jval>();
                foreach (var op in ArrItems(vfxJson.Get("operators")))
                {
                    var props = op.Get("props");
                    if (op.StrOf("typeId") != "getProperty" || props == null || !JsStrictEq(props.Get("property"), propName)) continue;
                    var o = op.Get("output");
                    if (o == null || !o.IsObject) continue;   // JS 的 op.output || {}
                    foreach (var k in o.Keys.ToList())
                    {
                        foreach (var i in ArrItems(o.Get(k) == null ? null : o.Get(k).Get("infoArr")))
                        {
                            var slotId = i.Get("slotId");
                            string sid = (slotId != null && slotId.IsString) ? slotId.Str : (JsTruthy(slotId) ? JsString(slotId) : "");   // JS i.slotId || ""
                            var mm = Regex.Match(sid, @"^block_(\d+)_value$");
                            if (mm.Success)
                            {
                                Jval b;
                                if (blockById.TryGetValue(mm.Groups[1].Value, out b))
                                {
                                    var bp = b.Get("props");
                                    if (bp != null && bp.StrOf("attribute") == "color" &&
                                        (b.StrOf("typeId") == "setAttributeCurve" || b.StrOf("typeId") == "setAttribute")) blocks.Add(b);
                                }
                            }
                            else
                            {
                                Jval tn;
                                if (opById.TryGetValue(JsString(i.Get("nodeId")), out tn) && tn.StrOf("typeId") == "sampleGradient") gops.Add(tn);
                            }
                        }
                    }
                }
                return Tuple.Create(blocks, gops);
            };

            // 找绑定到某 property 的所有 block 目标（getProperty 连到 block_<id>_value）— 通用版
            Func<string, List<Jval>> blockTargetsBoundTo = (propName) =>
            {
                var blocks = new List<Jval>();
                foreach (var op in ArrItems(vfxJson.Get("operators")))
                {
                    var props = op.Get("props");
                    if (op.StrOf("typeId") != "getProperty" || props == null || !JsStrictEq(props.Get("property"), propName)) continue;
                    var o = op.Get("output");
                    if (o == null || !o.IsObject) continue;
                    foreach (var k in o.Keys.ToList())
                    {
                        foreach (var i in ArrItems(o.Get(k) == null ? null : o.Get(k).Get("infoArr")))
                        {
                            var slotId = i.Get("slotId");
                            string sid = (slotId != null && slotId.IsString) ? slotId.Str : (JsTruthy(slotId) ? JsString(slotId) : "");
                            var mm = Regex.Match(sid, @"^block_(\d+)_value$");
                            if (mm.Success)
                            {
                                Jval b;
                                if (blockById.TryGetValue(mm.Groups[1].Value, out b)) blocks.Add(b);
                            }
                        }
                    }
                }
                return blocks;
            };

            // float override 同步刷进 CPU 侧 spawn block（runtime parser 静态读 props.rate/count，
            // 不消费 getProperty 链接）。GPU 侧 block 的连接由 IDE 编译成 uniform，不需要也不能盲刷。
            Func<string, double, int> flushFloatToSpawnBlocks = (propName, num) =>
            {
                int flushed = 0;
                foreach (var b in blockTargetsBoundTo(propName))
                {
                    var bp = b.Get("props");
                    string tid = b.StrOf("typeId");
                    if (tid == "constantRate" && bp != null && IsNum(bp.Get("rate")))
                    {
                        bp.Set("rate", num); flushed++;
                    }
                    else if (tid == "singleBurst" && bp != null && IsNum(bp.Get("count")))
                    {
                        bp.Set("count", num); flushed++;
                    }
                    else if (tid == "periodicBurst" && bp != null && IsNum(bp.Get("count")))
                    {
                        bp.Set("count", num); flushed++;
                    }
                    else if (tid == "triggerEvent" && bp != null && bp.StrOf("eventType") == "OverTime" && IsNum(bp.Get("param")))
                    {
                        // GPU 事件 spawn（如 swarm）：triggerEvent OverTime 的 param = 每秒触发率（= SwarmSpawnRate）
                        bp.Set("param", num); flushed++;
                    }
                }
                return flushed;
            };

            // initialEventName 写到 vfxJson.props（.laya.vfx props.initialEventName）
            if (vfxBlock.InitialEventName != null)
            {
                var rootProps = vfxJson.Get("props");
                if (rootProps == null || !JsTruthy(rootProps)) { rootProps = Jval.Obj(); vfxJson.Set("props", rootProps); }
                rootProps.Set("initialEventName", vfxBlock.InitialEventName);
            }

            // 按 JS allItems 顺序展平（float→vec2→vec3→vec4→uint→int→bool→gradient→namedObject）
            var allItems = new List<TypedItem>();
            foreach (var it in vfxBlock.Floats) allItems.Add(new TypedItem { Name = it.Name, Type = "float", Value = it.Value });
            foreach (var it in vfxBlock.Vec2s) allItems.Add(new TypedItem { Name = it.Name, Type = "vec2", Value = it.Value });
            foreach (var it in vfxBlock.Vec3s) allItems.Add(new TypedItem { Name = it.Name, Type = "vec3", Value = it.Value });
            foreach (var it in vfxBlock.Vec4s) allItems.Add(new TypedItem { Name = it.Name, Type = "vec4", Value = it.Value });
            foreach (var it in vfxBlock.Uints) allItems.Add(new TypedItem { Name = it.Name, Type = "uint", Value = it.Value });
            foreach (var it in vfxBlock.Ints) allItems.Add(new TypedItem { Name = it.Name, Type = "int", Value = it.Value });
            foreach (var it in vfxBlock.Bools) allItems.Add(new TypedItem { Name = it.Name, Type = "bool", Value = it.Value });
            foreach (var it in vfxBlock.Gradients) allItems.Add(new TypedItem { Name = it.Name, Type = "gradient", Value = it.Value });
            foreach (var it in vfxBlock.NamedObjects) allItems.Add(new TypedItem { Name = it.Name, Type = "namedObject", Value = it.Value });

            foreach (var item in allItems)
            {
                Jval prop;
                if (!propByName.TryGetValue(item.Name, out prop)) { result.Missing++; continue; }   // override 字段不在 .vfx properties 里（孤立 override），跳过
                switch (item.Type)
                {
                    case "float":
                    case "uint":
                    case "int":
                        {
                            double num = JsNumber(item.Value);
                            prop.Set("default", Jval.Arr(Jval.Of(num)));
                            flushFloatToSpawnBlocks(item.Name, num);
                            prop.Set("prefabOverridden", true); result.Applied++;
                            break;
                        }
                    case "bool":
                        prop.Set("default", Jval.Arr(Jval.Of(JsTruthy(item.Value) ? 1 : 0)));
                        prop.Set("prefabOverridden", true); result.Applied++;
                        break;
                    case "vec2":
                    case "vec3":
                    case "vec4":
                        {
                            // JS 的 item.value.slice() = 浅拷贝数组（元素是数字）
                            var copy = Jval.Arr();
                            foreach (var e in ArrItems(item.Value)) copy.Push(CloneJval(e));
                            prop.Set("default", copy);
                            prop.Set("prefabOverridden", true); result.Applied++;
                            break;
                        }
                    case "gradient":
                        {
                            var stops = (item.Value != null) ? item.Value.Get("stops") : null;
                            if (stops != null && stops.IsArray)
                            {
                                prop.Set("default", Jval.Obj().Set("stops", stops));
                                // 同步刷到绑定该 gradient property 的 color block + sampleGradient 算子内联
                                // （compute / evaluator 都用各自内联，不刷则保持 base 灰白）
                                var gt = gradientTargetsBoundTo(item.Name);
                                foreach (var b in gt.Item1) b.Get("props").Set("gradient", Jval.Obj().Set("stops", stops));
                                foreach (var g in gt.Item2) g.Get("props").Set("gradient", Jval.Obj().Set("stops", stops));
                                prop.Set("prefabOverridden", true); result.Applied++;
                            }
                            break;
                        }
                    case "namedObject":
                        {
                            string guid = (item.Value != null) ? item.Value.StrOf("guid") : null;
                            if (!string.IsNullOrEmpty(guid))
                            {
                                var mp = (mapping != null) ? mapping(guid) : null;
                                string layaUuid = (mp != null) ? mp.StrOf("layaUuid") : null;
                                if (mp != null && !string.IsNullOrEmpty(layaUuid))   // JS：mp && mp.layaUuid（空串 falsy → missing）
                                {
                                    // 必须带 res:// 前缀 — runtime VFXAssetParser 直接 Laya.loader.load(d[0])，
                                    // 裸 uuid 会报 "unsupported suffix" 加载 null
                                    prop.Set("default", Jval.Arr(Jval.Of(layaUuid.StartsWith("res://") ? layaUuid : "res://" + layaUuid)));
                                    prop.Set("prefabOverridden", true); result.Applied++;
                                }
                                else
                                {
                                    result.Missing++;
                                }
                            }
                            break;
                        }
                }
            }

            // ── C: SkinnedMesh 系统（bolts/sparks/surge）的随机Y position 列，应用 prefab override 的
            //   VFXSpawnHeight + VFXCenterOffset。base 烘焙时用 VFXSpawnHeight=1 → _values.b_y = mult；
            //   这里用 override 值重烘：half = override × mult，center = VFXCenterOffset →
            //   Y∈[cy-half, cy+half]，X=cx，Z=cz。
            //   只动「同 context 内有 transformPosition 块」的随机Y position（即 A 机制的列），不误伤别的随机位置。
            var ovSpawnH = allItems.FirstOrDefault(it => it.Name == "VFXSpawnHeight");
            var ovCenter = allItems.FirstOrDefault(it => it.Name == "VFXCenterOffset");
            if (ovSpawnH != null || ovCenter != null)
            {
                double h = (ovSpawnH != null) ? JsNumber(ovSpawnH.Value) : 1;
                Jval cc = (ovCenter != null) ? ovCenter.Value : Jval.Arr(Jval.Of(0), Jval.Of(0), Jval.Of(0));
                double cx = ElemNumOr0(cc, 0), cy = ElemNumOr0(cc, 1), cz = ElemNumOr0(cc, 2);   // JS 的 cc[i] ?? 0
                int rebaked = 0;
                foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
                {
                    bool hasTransform = ArrItems(ctx.Get("blocks")).Any(b => b.StrOf("typeId") == "transformPosition");
                    if (!hasTransform) continue;
                    foreach (var b in ArrItems(ctx.Get("blocks")))
                    {
                        var bp = b.Get("props");
                        Jval vals = (bp != null) ? bp.Get("_values") : null;
                        if (b.StrOf("typeId") == "setAttribute" && bp != null && bp.StrOf("attribute") == "position"
                            && bp.StrOf("random") == "Per Component" && JsTruthy(vals)
                            && IsNum(vals.Get("b_y")))
                        {
                            double mult = Math.Abs(vals.Get("b_y").Num);   // base 烘焙: base(1)×mult = mult
                            double half = h * mult;
                            bp.Set("_values", Jval.Obj()
                                .Set("x", cx).Set("y", cy - half).Set("z", cz)
                                .Set("b_x", cx).Set("b_y", cy + half).Set("b_z", cz));
                            rebaked++;
                        }
                    }
                }
                if (rebaked > 0) result.Applied++;
            }

            return result;
        }

        // ─── 七个 sync 函数 ──────────────────────────────────────

        /// <summary>
        /// prefab override 改的是 Mesh **属性**（properties[].default），但运行时渲染用 ctx.props.mesh。
        /// base 转换器把 mesh slot 链接的暴露属性名记成 ctx.props._meshProperty，据此同步 props.mesh
        /// 并补 size 缩放（SPECIAL_MESH_SCALE_UUIDS）。对应 JS syncMeshRenderFieldFromProperty。
        /// </summary>
        public static void SyncMeshRenderFieldFromProperty(Jval vfxJson)
        {
            var propByName = BuildPropByName(vfxJson);
            double maxBlockId = 0;
            foreach (var c in ArrItems(vfxJson.Get("contexts")))
                foreach (var b in ArrItems(c.Get("blocks")))
                {
                    var id = b.Get("id");
                    maxBlockId = Math.Max(maxBlockId, (id != null && id.IsNumber) ? id.Num : 0);   // JS b.id || 0
                }
            foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
            {
                var ctxProps = ctx.Get("props");
                var propNameJ = (ctxProps != null) ? ctxProps.Get("_meshProperty") : null;
                if (!JsTruthy(propNameJ)) continue;
                Jval prop = null;
                if (propNameJ.IsString) propByName.TryGetValue(propNameJ.Str, out prop);
                var dflt = (prop != null) ? prop.Get("default") : null;
                var v = (dflt != null && dflt.IsArray) ? dflt.At(0) : null;
                if (v == null || !v.IsString || !Regex.IsMatch(v.Str, @"^res://")) continue;   // 仅当属性被 override 成真 mesh 才同步
                ctxProps.Set("mesh", v.Str);
                // size 补偿：按 mesh uuid 查 SPECIAL_MESH_SCALE_UUIDS（幂等，跨重转不重复）
                var m = Regex.Match(v.Str, "[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
                double factor;
                if (!m.Success || !SPECIAL_MESH_SCALE_UUIDS.TryGetValue(m.Value, out factor)) continue;
                var blocks = ctx.Get("blocks");
                if (blocks == null || !JsTruthy(blocks)) { blocks = Jval.Arr(); ctx.Set("blocks", blocks); }   // JS ctx.blocks = ctx.blocks || []
                if (ArrItems(blocks).Any(b => { var f = b.Get("_meshSizeFix"); return f != null && !f.IsNull; })) continue;   // JS ._meshSizeFix != null
                maxBlockId += 500;
                // unshift：插到最前
                blocks.Items.Insert(0, Jval.Obj()
                    .Set("id", maxBlockId)
                    .Set("typeId", "setAttribute")
                    .Set("enabled", true)
                    .Set("_meshSizeFix", factor)
                    .Set("props", Jval.Obj()
                        .Set("attribute", "size").Set("source", "Slot").Set("composition", "Multiply")
                        .Set("random", "Off").Set("channels", 7)
                        .Set("_values", Jval.Obj().Set("x", factor).Set("value", factor))));
            }
        }

        /// <summary>
        /// 把 override 的纹理属性同步到 output 的 ctx.props.mainTexture
        /// （base 转换器记的 ctx.props._mainTextureProperty 指路）。对应 JS syncMainTextureFromProperty。
        /// </summary>
        public static void SyncMainTextureFromProperty(Jval vfxJson)
        {
            var propByName = BuildPropByName(vfxJson);
            foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
            {
                var ctxProps = ctx.Get("props");
                var propNameJ = (ctxProps != null) ? ctxProps.Get("_mainTextureProperty") : null;
                if (!JsTruthy(propNameJ)) continue;
                Jval prop = null;
                if (propNameJ.IsString) propByName.TryGetValue(propNameJ.Str, out prop);
                var dflt = (prop != null) ? prop.Get("default") : null;
                var v = (dflt != null && dflt.IsArray) ? dflt.At(0) : null;
                if (v == null || !v.IsString || !Regex.IsMatch(v.Str, @"^res://")) continue;   // 仅当属性被 override 成真纹理才同步
                ctxProps.Set("mainTexture", v.Str);
            }
        }

        /// <summary>
        /// 把 override 的 flipbook vec2 属性同步到 output 的 ctx.props.flipbookSize
        /// （base 转换器记的 ctx.props._flipbookProperty 指路）。对应 JS syncFlipbookFromProperty。
        /// </summary>
        public static void SyncFlipbookFromProperty(Jval vfxJson)
        {
            var propByName = BuildPropByName(vfxJson);
            foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
            {
                var ctxProps = ctx.Get("props");
                var propNameJ = (ctxProps != null) ? ctxProps.Get("_flipbookProperty") : null;
                if (!JsTruthy(propNameJ)) continue;
                Jval prop = null;
                if (propNameJ.IsString) propByName.TryGetValue(propNameJ.Str, out prop);
                var d = (prop != null) ? prop.Get("default") : null;
                if (d == null || !d.IsArray || d.Count < 2) continue;
                double x = JsNumber(d.At(0)), y = JsNumber(d.At(1));
                if (!(x > 0) || !(y > 0)) continue;
                ctxProps.Set("flipbookSize", Jval.Obj().Set("x", x).Set("y", y));
            }
        }

        /// <summary>
        /// 据（override 后）flipbookSize 把同系统 init 的 setAttribute(texIndex) 随机 max 改成总帧数
        /// （引擎 flipbook 取帧 = floor(mod(texIndex, 总帧数))，[0,1] 几乎全是帧0）。
        /// 对应 JS syncTexIndexMaxFromFlipbook。
        /// </summary>
        public static void SyncTexIndexMaxFromFlipbook(Jval vfxJson)
        {
            var contexts = vfxJson.Get("contexts");
            var flowsInto = BuildFlowsInto(contexts);
            foreach (var outCtx in ArrItems(contexts))
            {
                var op = outCtx.Get("props");
                if (op == null || !JsTruthy(op.Get("_flipbookProperty")) || !JsTruthy(op.Get("flipbookSize"))) continue;
                var fb = op.Get("flipbookSize");
                double fx = JsNumber(fb != null ? fb.Get("x") : null), fy = JsNumber(fb != null ? fb.Get("y") : null);
                // JS 的 Number(fb.x) || 1（NaN/0 → 1）
                double total = (double.IsNaN(fx) || fx == 0 ? 1 : fx) * (double.IsNaN(fy) || fy == 0 ? 1 : fy);
                if (!(total > 1)) continue;
                // 从 output 反向 BFS 找同系统所有 initialize ctx
                foreach (var ini in BfsUpstreamInits(outCtx, flowsInto))
                {
                    foreach (var b in ArrItems(ini.Get("blocks")))
                    {
                        var bp = b.Get("props");
                        if (b.StrOf("typeId") == "setAttribute" && bp != null && bp.StrOf("attribute") == "texIndex"
                            && JsTruthy(bp.Get("random")) && !JsStrictEq(bp.Get("random"), "Off") && JsTruthy(bp.Get("_values")))
                        {
                            var vals = bp.Get("_values");
                            vals.Set("y", total);
                            vals.Set("b_value", total);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// materialize swarm 上扫（sweep）注入：给 swarm 的 setPositionShape Circle 注入 props.ySweep
        /// （引擎 ShapePosition.ts 据此 emit Y=base+height×(u_TotalTime/dur)）。
        /// base/height/dur 来自 materialize 属性；band 优先取 prefab override sheet 里的 SwarmSpawnWidthMax。
        /// 对应 JS injectSwarmSweep。
        /// </summary>
        public static void InjectSwarmSweep(Jval vfxJson, VisualEffectBlock vfxBlock)
        {
            var propByName = BuildPropByName(vfxJson);
            Func<string, double, double> getNum = (name, def) =>
            {
                Jval p;
                if (!propByName.TryGetValue(name, out p)) return def;
                var d = p.Get("default");
                if (d != null && d.IsArray) return JsNumber(d.At(0));   // JS Number(p.default[0])（空数组 → NaN）
                if (d != null && d.IsNumber) return d.Num;
                return def;
            };
            Func<string, double, double> getV3y = (name, def) =>
            {
                Jval p;
                if (propByName.TryGetValue(name, out p))
                {
                    var d = p.Get("default");
                    if (d != null && d.IsArray && d.Count >= 2) return JsNumber(d.At(1));
                }
                return def;
            };
            double centerY = getV3y("VFXCenterOffset", 0);
            double H = getNum("VFXSpawnHeight", 0);
            // 扫掠速度要跟 scan 网状的 materialize 动画一致，用 VFXAnimationDuration（materialize 形成动画时长）
            // 而非 Duration（整体特效时长）。
            double duration = getNum("VFXAnimationDuration", getNum("Duration", 4));
            if (!(H > 0)) return;   // 没 materialize 列高 → 非 sweep 系统，跳过
            // SwarmSpawnWidthMax 常是 [miss]（非 base 属性），从 prefab override sheet 读
            double? widthMax = null;
            foreach (var it in vfxBlock.Floats)
            {
                if (it.Name == "SwarmSpawnWidthMax") widthMax = JsNumber(it.Value);
            }
            var flowsInto = BuildFlowsInto(vfxJson.Get("contexts"));
            foreach (var outCtx in ArrItems(vfxJson.Get("contexts")))
            {
                var op = outCtx.Get("props");
                if (op == null || !JsTruthy(op.Get("_flipbookProperty"))) continue;   // swarm 标志（flipbook 颗粒）
                foreach (var ini in BfsUpstreamInits(outCtx, flowsInto))
                {
                    foreach (var b in ArrItems(ini.Get("blocks")))
                    {
                        var bp = b.Get("props");
                        if (b.StrOf("typeId") == "setPositionShape" && bp != null && bp.StrOf("shape") == "Circle")
                        {
                            // 只注入 Y 上扫让粒子按 spawn 时刻分布到不同高度（穹顶在 leading edge + 下方稀疏 trail），
                            // 不缩半径。band = SwarmSpawnWidthMax（Unity 的垂直随机带厚度），兜底用 H 的小比例。
                            double band = (widthMax != null && widthMax > 0) ? widthMax.Value : Math.Max(H * 0.12, 0.15);
                            bp.Set("ySweep", Jval.Obj()
                                .Set("base", centerY - H / 2)
                                .Set("height", H)
                                .Set("duration", duration)
                                .Set("band", band));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 把绑定到 shader uniform 的纹理类暴露属性（Texture2D），其（override 后）默认 res:// 镜像进
        /// ctx.props.shaderPropertyDefaults[uniform]（材质创建时直接 setTexture 的 Path B）。
        /// 对应 JS mirrorTextureBindingsToDefaults。
        /// </summary>
        public static void MirrorTextureBindingsToDefaults(Jval vfxJson)
        {
            var propByName = BuildPropByName(vfxJson);
            foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
            {
                var ctxProps = ctx.Get("props");
                var bind = (ctxProps != null) ? ctxProps.Get("shaderPropertyBindings") : null;
                if (bind == null || !bind.IsObject) continue;
                foreach (var exposedName in bind.Keys.ToList())
                {
                    var uniform = bind.Get(exposedName);
                    Jval prop;
                    if (!propByName.TryGetValue(exposedName, out prop) || prop.StrOf("type") != "Texture2D") continue;
                    var dflt = prop.Get("default");
                    var v = (dflt != null && dflt.IsArray) ? dflt.At(0) : null;
                    if (v == null || !v.IsString || !Regex.IsMatch(v.Str, @"^res://")) continue;
                    var defaults = ctxProps.Get("shaderPropertyDefaults");
                    if (defaults == null || !JsTruthy(defaults)) { defaults = Jval.Obj(); ctxProps.Set("shaderPropertyDefaults", defaults); }
                    // JS 对象键 = String(uniform)
                    string uKey = (uniform != null && uniform.IsString) ? uniform.Str : JsString(uniform);
                    var cur = defaults.Get(uKey);
                    if (cur == null || !(cur.IsString && cur.Str == v.Str))   // JS 严格 !==
                    {
                        defaults.Set(uKey, v.Str);
                    }
                }
            }
        }

        /// <summary>
        /// 把暴露属性注册表（prefabOverridden 后的权威默认值）同步进各 output 内联 VFXParameter.defaultValue。
        /// 只同步 bool/color（转换器把内联 defaultValue 烤成 Unity 原始默认 false/null 的两类中招）。
        /// 对应 JS syncExposedDefaultsToInlineNodes。
        /// </summary>
        public static void SyncExposedDefaultsToInlineNodes(Jval vfxJson)
        {
            var propByName = BuildPropByName(vfxJson);
            foreach (var ctx in ArrItems(vfxJson.Get("contexts")))
            {
                var ctxProps = ctx.Get("props");
                var exprs = (ctxProps != null) ? ctxProps.Get("shaderPropertyExpressions") : null;
                if (exprs == null || !exprs.IsObject) continue;
                foreach (var slotKey in exprs.Keys.ToList())
                {
                    var slot = exprs.Get(slotKey);
                    var nodes = (slot != null) ? slot.Get("nodes") : null;
                    if (nodes == null || !nodes.IsObject) continue;
                    foreach (var nodeKey in nodes.Keys.ToList())
                    {
                        var node = nodes.Get(nodeKey);
                        if (node == null || !JsTruthy(node) || node.StrOf("kind") != "VFXParameter" || !JsTruthy(node.Get("exposedName"))) continue;
                        var exposedName = node.Get("exposedName");
                        Jval prop = null;
                        if (exposedName.IsString) propByName.TryGetValue(exposedName.Str, out prop);
                        var d = (prop != null) ? prop.Get("default") : null;
                        if (prop == null || d == null || !d.IsArray) continue;
                        Jval val;
                        string ptype = prop.StrOf("type");
                        if (ptype == "bool") val = Jval.Of(JsTruthy(d.At(0)));   // JS !!d[0]
                        else if (ptype == "color")
                        {
                            // JS {r:d[0],g:d[1],b:d[2],a:d[3]}：越界元素是 undefined，
                            // JSON.stringify 会省略该键 → 这里只在下标存在时写键
                            val = Jval.Obj();
                            string[] keys = { "r", "g", "b", "a" };
                            for (int i = 0; i < 4; i++)
                                if (i < d.Count) val.Set(keys[i], CloneJval(d.At(i)));
                        }
                        else continue;   // 只同步 bool/color
                        // JS：JSON.stringify(node.defaultValue) !== JSON.stringify(val)
                        //（defaultValue 缺失时 stringify 是 undefined，恒不等 → 触发写入）
                        string oldStr = node.Has("defaultValue") ? node.Get("defaultValue").Serialize(0) : null;
                        string newStr = val.Serialize(0);
                        if (oldStr != newStr) node.Set("defaultValue", val);
                    }
                }
            }
        }

        // ─── 内部工具（模仿 JS 语义） ────────────────────────────

        /// <summary>建 name → property entry 反查（JS 的 Map：后写覆盖先写）。</summary>
        private static Dictionary<string, Jval> BuildPropByName(Jval vfxJson)
        {
            var map = new Dictionary<string, Jval>();
            foreach (var p in ArrItems(vfxJson.Get("properties")))
            {
                var n = p.Get("name");
                if (n != null && n.IsString) map[n.Str] = p;   // 非字符串 name 的 Map 键在后续字符串查询下永不命中，等价忽略
            }
            return map;
        }

        /// <summary>反向 flowLinks：targetId → [上游 ctx]（键用 JS String() 语义序列化）。</summary>
        private static Dictionary<string, List<Jval>> BuildFlowsInto(Jval contexts)
        {
            var flowsInto = new Dictionary<string, List<Jval>>();
            foreach (var c in ArrItems(contexts))
            {
                var fl = c.Get("flowLinks");
                if (fl == null || !fl.IsObject) continue;   // JS c.flowLinks || {}
                foreach (var k in fl.Keys.ToList())
                {
                    var lk = fl.Get(k);
                    var t = (lk != null && JsTruthy(lk)) ? lk.Get("targetId") : null;   // JS fl[k] && fl[k].targetId
                    if (t == null || t.IsNull) continue;   // JS t == null（undefined/null）
                    string key = JsString(t);
                    List<Jval> list;
                    if (!flowsInto.TryGetValue(key, out list)) { list = new List<Jval>(); flowsInto[key] = list; }
                    list.Add(c);
                }
            }
            return flowsInto;
        }

        /// <summary>从 output ctx 沿反向 flowLinks BFS，收集同系统所有 initialize ctx（顺序与 JS 一致）。</summary>
        private static List<Jval> BfsUpstreamInits(Jval outCtx, Dictionary<string, List<Jval>> flowsInto)
        {
            var seen = new HashSet<string> { JsString(outCtx.Get("id")) };
            var frontier = new List<Jval> { outCtx };
            var inits = new List<Jval>();
            while (frontier.Count > 0)
            {
                var next = new List<Jval>();
                foreach (var c in frontier)
                {
                    List<Jval> srcs;
                    if (!flowsInto.TryGetValue(JsString(c.Get("id")), out srcs)) continue;
                    foreach (var src in srcs)
                    {
                        string sid = JsString(src.Get("id"));
                        if (seen.Contains(sid)) continue;
                        seen.Add(sid);
                        if (src.StrOf("typeId") == "initialize") inits.Add(src);
                        next.Add(src);
                    }
                }
                frontier = next;
            }
            return inits;
        }

        /// <summary>数组元素枚举（null/非数组 → 空，模仿 JS 的 (x || []) 防御）。</summary>
        private static IEnumerable<Jval> ArrItems(Jval v)
        {
            if (v == null || !v.IsArray) yield break;
            // 用索引遍历，允许循环体内 unshift/append（与 JS for..of 快照差异在本逻辑里不触发）
            foreach (var it in v.Items.ToList()) yield return it;
        }

        /// <summary>typeof x === "number"。</summary>
        private static bool IsNum(Jval v) { return v != null && v.IsNumber; }

        /// <summary>JS 真值判断（truthy）。</summary>
        private static bool JsTruthy(Jval v)
        {
            if (v == null || v.IsNull) return false;
            if (v.IsBool) return v.Bool;
            if (v.IsNumber) return v.Num != 0 && !double.IsNaN(v.Num);
            if (v.IsString) return v.Str.Length > 0;
            return true;   // 对象/数组恒真
        }

        /// <summary>JS 严格相等（===）对字符串常量的比较。</summary>
        private static bool JsStrictEq(Jval v, string s)
        {
            return v != null && v.IsString && v.Str == s;
        }

        /// <summary>JS String(x)（这里只需覆盖 id 场景：数字/字符串/undefined/null）。</summary>
        private static string JsString(Jval v)
        {
            if (v == null) return "undefined";
            if (v.IsNull) return "null";
            if (v.IsBool) return v.Bool ? "true" : "false";
            if (v.IsNumber) return double.IsNaN(v.Num) ? "NaN" : Jval.FormatNumber(v.Num);
            if (v.IsString) return v.Str;
            return v.IsArray ? "" : "[object Object]";
        }

        /// <summary>JS Number(x) 语义（undefined→NaN、null→0、数组按元素数折叠）。</summary>
        private static double JsNumber(Jval v)
        {
            if (v == null) return double.NaN;          // undefined
            if (v.IsNull) return 0;                    // null
            if (v.IsNumber) return v.Num;
            if (v.IsBool) return v.Bool ? 1 : 0;
            if (v.IsString)
            {
                string s = v.Str.Trim();
                if (s.Length == 0) return 0;
                double d;
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
            }
            if (v.IsArray)
            {
                if (v.Count == 0) return 0;
                if (v.Count == 1) return JsNumber(v.At(0));
                return double.NaN;
            }
            return double.NaN;   // 对象
        }

        /// <summary>JS 的 cc[i] ?? 0：越界/null 元素回退 0，其余按数字读。</summary>
        private static double ElemNumOr0(Jval arr, int i)
        {
            Jval e = (arr != null && arr.IsArray) ? arr.At(i) : null;
            if (e == null || e.IsNull) return 0;   // undefined/null → 0
            return JsNumber(e);
        }

        /// <summary>JS 的字符串数字解析（parse*Value 用，值域是标准浮点字面量）。</summary>
        private static double JsParseNumber(string s)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
        }

        /// <summary>模仿 JS Number(t.toFixed(6))：按十进制 6 位小数取整后回读。</summary>
        private static double JsToFixed6(double v)
        {
            return double.Parse(v.ToString("F6", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>深拷贝（vec 的 slice() / color 元素复制用；数字/字符串等值语义）。</summary>
        private static Jval CloneJval(Jval v)
        {
            if (v == null) return null;
            if (v.IsNull) return Jval.Null();
            if (v.IsBool) return Jval.Of(v.Bool);
            if (v.IsNumber) return Jval.Of(v.Num);
            if (v.IsString) return Jval.Of(v.Str);
            if (v.IsArray)
            {
                var a = Jval.Arr();
                foreach (var it in v.Items) a.Push(CloneJval(it));
                return a;
            }
            var o = Jval.Obj();
            foreach (var k in v.Keys) o.Set(k, CloneJval(v.Get(k)));
            return o;
        }
    }
}
