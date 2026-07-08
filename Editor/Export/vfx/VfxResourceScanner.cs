using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// 在 Unity 里实时扫 .meta 文件，构建 VFX/蓝图转换器需要的全部资源映射。
    /// 对应 JS 版的 scanGuidMap / scanShaderGraphMap / scanBlueprintShaderMap / scanLayaExportedLms /
    /// scanSubgraphFiles / _loadAssetMappings / vfx-texture-variants —— 取代测试期手工生成的 json。
    ///
    /// - Unity 侧（guid 在 Unity .meta 里）：guidToClass(.cs.meta) / shaderGraphByGuid(.shadergraph.meta) /
    ///   subgraphByGuid(.vfxblock/.vfxoperator.meta) —— 扫 Unity Assets + Library/PackageCache 的 VFX/URP 包。
    /// - Laya 侧（uuid 在 Laya .meta 里）：blueprintShaderByName(.bps.meta) / lm(.lm.meta) —— 扫 LayaVFXSample/assets。
    /// - asset-mapping / texture-variants：读 LayaVFXSample/tools/ 下的 json（与 JS 一致）。
    /// - def 数据：读插件内置 Editor/Export/vfx/vfx-defs.json。
    /// </summary>
    public class VfxResourceScanner
    {
        public readonly Dictionary<string, string> GuidToClass = new Dictionary<string, string>();
        public readonly Dictionary<string, string> ShaderGraphByGuid = new Dictionary<string, string>();
        public readonly Dictionary<string, string> BlueprintShaderByName = new Dictionary<string, string>();
        public readonly Dictionary<string, string> SubgraphByGuid = new Dictionary<string, string>();
        /// <summary>subgraph guid → .vfxblock/.vfxoperator 文件绝对路径（通用子图内联加载用）</summary>
        public readonly Dictionary<string, string> SubgraphPathByGuid = new Dictionary<string, string>();
        public readonly Dictionary<string, string> LmByName = new Dictionary<string, string>();
        private readonly Dictionary<string, long> _lmMtime = new Dictionary<string, long>();
        public readonly Dictionary<string, string> LmByUuid = new Dictionary<string, string>();
        public readonly Dictionary<string, Jval> AssetMapping = new Dictionary<string, Jval>();
        public readonly Dictionary<string, string> TextureVariants = new Dictionary<string, string>();
        // uuid → Laya 侧源资源文件绝对路径（用于转换后按 res://uuid 反查并拷贝 mesh/纹理/shader 依赖）
        public readonly Dictionary<string, string> UuidToSourceFile = new Dictionary<string, string>();
        public string LayaAssetsRoot;   // LayaVFXSample/assets 绝对路径（依赖拷贝时算相对路径用）
        public LayaDefs Defs;
        public readonly List<string> Log = new List<string>();

        private static readonly Regex UnityGuid = new Regex(@"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline);
        private static readonly Regex LayaUuid = new Regex("\"uuid\"\\s*:\\s*\"([0-9a-f-]{36})\"");

        /// <summary>
        /// projectRoot = Unity 工程根（含 Assets/Library）；pluginDefsPath = vfx-defs.json 绝对路径；
        /// layaSampleDir = LayaVFXSample 根（可为 null，则 VFX 资源解析退化为未映射 guid 占位）。
        /// </summary>
        public void Build(string projectRoot, string pluginDefsPath, string layaSampleDir)
        {
            string assets = Path.Combine(projectRoot, "Assets");
            string pkgCache = Path.Combine(projectRoot, "Library", "PackageCache");
            string vfxPkg = FindPackage(pkgCache, "com.unity.visualeffectgraph");
            string urpPkg = FindPackage(pkgCache, "com.unity.render-pipelines.universal");

            // guidToClass：VFX + URP 包的 .cs.meta
            if (vfxPkg != null) ScanUnityMeta(vfxPkg, ".cs.meta", (guid, name, ap) => { if (!GuidToClass.ContainsKey(guid)) GuidToClass[guid] = name; });
            if (urpPkg != null) ScanUnityMeta(urpPkg, ".cs.meta", (guid, name, ap) => { GuidToClass[guid] = name; });
            Log.Add("guidToClass=" + GuidToClass.Count);

            // shaderGraphByGuid：Unity Assets + VFX 包 Samples~
            ScanUnityMeta(assets, ".shadergraph.meta", (guid, name, ap) => { if (!ShaderGraphByGuid.ContainsKey(guid)) ShaderGraphByGuid[guid] = name; });
            if (vfxPkg != null) ScanUnityMeta(Path.Combine(vfxPkg, "Samples~"), ".shadergraph.meta", (guid, name, ap) => { if (!ShaderGraphByGuid.ContainsKey(guid)) ShaderGraphByGuid[guid] = name; });
            Log.Add("shaderGraphByGuid=" + ShaderGraphByGuid.Count);

            // subgraphByGuid：Unity Assets + VFX 包。
            // 注意：与 JS 一致，子图只去掉 ".meta"（保留 ".vfxblock"/".vfxoperator"），因为转换器按 basename=="vortex.vfxblock" 判定。
            // 同时记录 guid→文件路径（通用子图内联 LoadSubgraph 用）。
            Action<string, string, string> subCb = (guid, name, ap) => { SubgraphByGuid[guid] = name; SubgraphPathByGuid[guid] = ap; };
            ScanUnityMeta(assets, ".vfxblock.meta", subCb, true);
            ScanUnityMeta(assets, ".vfxoperator.meta", subCb, true);
            if (vfxPkg != null) { ScanUnityMeta(vfxPkg, ".vfxblock.meta", subCb, true); ScanUnityMeta(vfxPkg, ".vfxoperator.meta", subCb, true); }
            Log.Add("subgraphByGuid=" + SubgraphByGuid.Count);

            // Laya 侧：LayaVFXSample/assets 下 .bps.meta / .lm.meta；tools 下 asset-mapping / texture-variants
            if (layaSampleDir != null && Directory.Exists(layaSampleDir))
            {
                string layaAssets = Path.Combine(layaSampleDir, "assets");
                ScanLayaMeta(layaAssets, ".bps.meta", (uuid, name, p) => { if (!BlueprintShaderByName.ContainsKey(name)) BlueprintShaderByName[name] = "res://" + uuid; });
                ScanLmMeta(Path.Combine(layaAssets, "resources"));
                LoadAssetMappings(Path.Combine(layaSampleDir, "tools"));
                LoadTextureVariants(Path.Combine(layaSampleDir, "tools", "vfx-texture-variants.json"));
                LayaAssetsRoot = layaAssets;
                ScanAllUuidFiles(layaAssets);
                Log.Add("bps=" + BlueprintShaderByName.Count + " lm=" + LmByUuid.Count + " assetMapping=" + AssetMapping.Count + " texVariants=" + TextureVariants.Count + " uuidToFile=" + UuidToSourceFile.Count);
            }
            else Log.Add("⚠ 未提供 LayaVFXSample 目录：VFX 的 .bps/.lm/纹理映射将缺失（未映射资源写占位 guid）");

            // def 数据
            if (pluginDefsPath != null && File.Exists(pluginDefsPath)) Defs = LayaDefs.FromJson(File.ReadAllText(pluginDefsPath));
        }

        /// <summary>把扫到的映射灌进一个 VfxConverter。</summary>
        public void Apply(VfxConverter conv)
        {
            conv.Defs = Defs;
            conv.GuidToClass = GuidToClass;
            conv.ShaderGraphByGuid = ShaderGraphByGuid;
            conv.BlueprintShaderByName = BlueprintShaderByName;
            conv.SubgraphNameByGuid = SubgraphByGuid;
            conv.SubgraphPathByGuid = SubgraphPathByGuid;
            foreach (var kv in LmByName) conv.LayaExportedLmByName[kv.Key] = kv.Value;
            foreach (var kv in LmByUuid) conv.LmUuidToName[kv.Key] = kv.Value;
            conv.AssetMapping = AssetMapping;
            conv.MainTextureRepeatVariant = TextureVariants;
        }

        private static string FindPackage(string pkgCache, string prefix)
        {
            if (!Directory.Exists(pkgCache)) return null;
            foreach (var d in Directory.GetDirectories(pkgCache))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith(prefix + "@")) return d;   // 排除 -config 变体（prefix 后是 @hash）
            }
            return null;
        }

        // stripOnlyMeta=true 时只去掉 ".meta"（子图保留 .vfxblock/.vfxoperator）；否则去掉整个 suffix（如 .cs.meta→类名）。
        // cb(guid, name, assetPath)：assetPath = .meta 去掉后缀的资源文件绝对路径。
        private void ScanUnityMeta(string root, string suffix, Action<string, string, string> cb, bool stripOnlyMeta = false)
        {
            if (!Directory.Exists(root)) return;
            int strip = stripOnlyMeta ? ".meta".Length : suffix.Length;
            foreach (var p in SafeEnumerate(root, "*" + suffix))
            {
                try { var m = UnityGuid.Match(File.ReadAllText(p)); if (m.Success) { string fn = Path.GetFileName(p); cb(m.Groups[1].Value, fn.Substring(0, fn.Length - strip), p.Substring(0, p.Length - 5)); } }
                catch { }
            }
        }
        private void ScanLayaMeta(string root, string suffix, Action<string, string, string> cb)
        {
            if (!Directory.Exists(root)) return;
            foreach (var p in SafeEnumerate(root, "*" + suffix))
            {
                try
                {
                    string t = File.ReadAllText(p); string uuid = null;
                    var j = Jval.Parse(t); if (j.IsObject && j.StrOf("uuid") != null) uuid = j.StrOf("uuid");
                    if (uuid == null) { var m = LayaUuid.Match(t); if (m.Success) uuid = m.Groups[1].Value; }
                    if (uuid != null) cb(uuid, Path.GetFileName(p).Substring(0, Path.GetFileName(p).Length - suffix.Length), p);
                }
                catch { }
            }
        }
        // 扫 LayaVFXSample/assets 下所有 *.meta，建 uuid → 资源文件（.meta 去掉后缀）的全量表。同 uuid 首个胜出。
        private void ScanAllUuidFiles(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var p in SafeEnumerate(root, "*.meta"))
            {
                try
                {
                    string t = File.ReadAllText(p); string uuid = null;
                    var j = Jval.Parse(t); if (j.IsObject && j.StrOf("uuid") != null) uuid = j.StrOf("uuid");
                    if (uuid == null) { var m = LayaUuid.Match(t); if (m.Success) uuid = m.Groups[1].Value; }
                    if (uuid != null && !UuidToSourceFile.ContainsKey(uuid)) UuidToSourceFile[uuid] = p.Substring(0, p.Length - 5);
                }
                catch { }
            }
        }
        private void ScanLmMeta(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var p in SafeEnumerate(root, "*.lm.meta"))
            {
                try
                {
                    var j = Jval.Parse(File.ReadAllText(p)); if (!j.IsObject || j.StrOf("uuid") == null) continue;
                    string uuid = j.StrOf("uuid");
                    string baseName = Path.GetFileName(p); baseName = baseName.Substring(0, baseName.Length - 8); // 去 ".lm.meta"
                    LmByUuid[uuid] = baseName;
                    long mtime = 0; try { string lm = p.Substring(0, p.Length - 5); if (File.Exists(lm)) mtime = File.GetLastWriteTimeUtc(lm).ToFileTimeUtc(); } catch { }
                    long ex; if (!_lmMtime.TryGetValue(baseName, out ex) || mtime > ex) { LmByName[baseName] = uuid; _lmMtime[baseName] = mtime; }
                }
                catch { }
            }
        }
        private void LoadAssetMappings(string toolsDir)
        {
            if (!Directory.Exists(toolsDir)) return;
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(toolsDir)) { string n = Path.GetFileName(f); if (Regex.IsMatch(n, @"^vfx-asset-mapping.*\.json$")) files.Add(f); }
            files.Sort();
            foreach (var f in files)
            {
                try { var j = Jval.Parse(File.ReadAllText(f)); if (j.IsObject) foreach (var k in j.Keys) AssetMapping[k] = j.Get(k); }
                catch { }
            }
        }
        private void LoadTextureVariants(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var j = Jval.Parse(File.ReadAllText(path)); var arr = j.Get("mainRepeatVariants");
                if (arr != null && arr.IsArray) foreach (var v in arr.Items) { var cu = v.StrOf("clampUuid"); var ru = v.StrOf("repeatUuid"); if (cu != null && ru != null) TextureVariants[cu] = ru; }
            }
            catch { }
        }
        // 手动递归遍历，复刻 JS readdirSync 的顺序（目录/文件按 OS 顺序交错、目录即时深入）——
        // 保证 .bps 等"同名首个胜出"的映射与 JS 选到同一份（否则 .NET EnumerateFiles 的顺序不同→选错重复项）。
        private static IEnumerable<string> SafeEnumerate(string root, string pattern)
        {
            string suffix = pattern.StartsWith("*") ? pattern.Substring(1) : pattern;
            var outFiles = new List<string>();
            Walk(root, suffix, outFiles);
            return outFiles;
        }
        private static void Walk(string dir, string suffix, List<string> outFiles)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch { return; }
            foreach (var p in entries)
            {
                if (Directory.Exists(p)) Walk(p, suffix, outFiles);
                else if (p.EndsWith(suffix, StringComparison.Ordinal)) outFiles.Add(p);
            }
        }
    }
}
