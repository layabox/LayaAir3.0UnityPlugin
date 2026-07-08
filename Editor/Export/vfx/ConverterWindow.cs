using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LayaAir3.Converter
{
    /// <summary>
    /// 转换器弹窗：菜单「LayaAir3D 3.0/转换器」直接打开。两个页签：
    ///   · 单文件转换：选一个 .shadergraph / .vfx 源文件 + 输出目录，按扩展名自动走蓝图 / VFX。
    ///   · 多文件转换：选源目录 + 目标目录，递归转换目录下所有 .shadergraph / .vfx（保留相对子目录结构）。
    /// 蓝图 → .bps，VFX → .laya.vfx。
    /// </summary>
    public class ConverterWindow : EditorWindow
    {
        private enum Tab { Single = 0, Batch = 1 }
        private Tab _tab = Tab.Single;

        // 单文件
        private string _srcFile = "";
        private string _singleOutDir = "";
        // 多文件
        private string _sourceDir = "";
        private string _targetDir = "";
        // 公共
        private string _layaSampleDir = "";
        private bool _bpSupportVfx = false;
        private bool _bpMaterialProps = false;
        // 多文件专用：转换类型开关
        private bool _batchBlueprint = true;
        private bool _batchVfx = true;
        private bool _batchPrefabVariant = true;   // .vfx 转完后应用 prefab 属性覆盖生成变体 .laya.vfx

        private Vector2 _logScroll;
        private string _log = "";

        private const string PK = "LayaAir3.Converter.";

        // ── 本地化：跟随插件 Setting 的 Language 下拉（LanguageConfig，与插件其它窗口一致） ──
        // GetLanguages() 未初始化时返回中文，与插件默认一致；用户在 LayaAir3D 3.0/Setting 切换后即时生效。
        // 注：LanguageConfig 在全局命名空间，用 global:: 引用。
        private static bool Zh { get { return global::LanguageConfig.GetLanguages() == global::LanguageConfig.languages.Chinese; } }
        private static string L(string zh, string en) { return Zh ? zh : en; }

        /// <summary>菜单入口：打开转换器窗口。</summary>
        [MenuItem("LayaAir3D 3.0/VFX-SHADER Converter", false, 90)]
        public static void Open()
        {
            var w = GetWindow<ConverterWindow>();
            w.titleContent = new GUIContent(L("转换器", "Converter"));
            w.minSize = new Vector2(580, 460);
            w.Show();
        }

        private void OnEnable()
        {
            _tab = (Tab)EditorPrefs.GetInt(PK + "tab", 0);
            _srcFile = EditorPrefs.GetString(PK + "srcFile", "");
            _singleOutDir = EditorPrefs.GetString(PK + "singleOut", "");
            _sourceDir = EditorPrefs.GetString(PK + "src", Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "UNI VFX"));
            _targetDir = EditorPrefs.GetString(PK + "dst", "");
            _layaSampleDir = EditorPrefs.GetString(PK + "laya", DetectLayaSample());
            _batchBlueprint = EditorPrefs.GetBool(PK + "bp", true);
            _batchVfx = EditorPrefs.GetBool(PK + "vfx", true);
            _batchPrefabVariant = EditorPrefs.GetBool(PK + "pv", true);
            _bpSupportVfx = EditorPrefs.GetBool(PK + "bpvfx", false);
            _bpMaterialProps = EditorPrefs.GetBool(PK + "bpmat", false);
            _lastZh = Zh;
        }

        // 插件语言在别处（Setting 窗口）切换时不会重绘本窗口 → 每帧检测语言变化，变了就重绘，让切换即时反映。
        private bool _lastZh;
        private void Update()
        {
            if (_lastZh != Zh) { _lastZh = Zh; Repaint(); }
        }

        private static string DetectLayaSample()
        {
            foreach (var c in new[] { "F:/git/LayaAir3.0/LayaVFXSample", "E:/git/LayaAir3.0/LayaVFXSample" })
                if (Directory.Exists(c)) return c;
            return "";
        }

        private void OnGUI()
        {
            titleContent.text = L("转换器", "Converter");
            EditorGUILayout.Space(8);
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField(L("蓝图 / VFX 转换器", "Blueprint / VFX Converter"), title);
            EditorGUILayout.Space(6);

            int t = GUILayout.Toolbar((int)_tab, new[] { L("单文件转换", "Single File"), L("多文件转换", "Batch") }, GUILayout.Height(24));
            if (t != (int)_tab) { _tab = (Tab)t; EditorPrefs.SetInt(PK + "tab", t); }
            EditorGUILayout.Space(6);

            if (_tab == Tab.Single) DrawSingle();
            else DrawBatch();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(L("日志", "Log"), EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(120));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ── 单文件 ──────────────────────────────────────────────
        private void DrawSingle()
        {
            EditorGUILayout.HelpBox(L("选一个 .shadergraph 或 .vfx 源文件，按扩展名自动识别类型转换。输出目录留空 = 输出到源文件所在目录。",
                "Pick a .shadergraph or .vfx source file; type is auto-detected by extension. Leave output empty to write next to the source."), MessageType.Info);
            _srcFile = FileField(L("源文件", "Source File"), _srcFile);
            string kind = FileKind(_srcFile);
            if (!string.IsNullOrEmpty(_srcFile))
                EditorGUILayout.LabelField(" ", kind == "bp" ? L("识别为：蓝图 (.shadergraph → .bps)", "Detected: Blueprint (.shadergraph → .bps)")
                    : kind == "vfx" ? L("识别为：VFX (.vfx → .laya.vfx)", "Detected: VFX (.vfx → .laya.vfx)")
                    : L("⚠ 不支持的文件类型", "⚠ Unsupported file type"));
            _singleOutDir = FolderField(L("输出目录(可空)", "Output Dir (optional)"), _singleOutDir);

            DrawCommonOptions();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(kind == null || !File.Exists(_srcFile)))
                if (GUILayout.Button(L("转换", "Convert"), GUILayout.Height(30))) RunSingle(kind);
        }

        // ── 多文件 ──────────────────────────────────────────────
        private void DrawBatch()
        {
            EditorGUILayout.HelpBox(L("选「源目录」和「目标目录」，递归转换源目录下所有勾选类型的文件，输出到目标目录（保留相对子目录）。",
                "Pick a Source and Target folder. All checked file types under Source are converted recursively into Target (relative subfolders preserved)."), MessageType.Info);
            _sourceDir = FolderField(L("源目录", "Source Dir"), _sourceDir);
            _targetDir = FolderField(L("目标目录", "Target Dir"), _targetDir);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(L("转换类型", "Convert Types"), EditorStyles.boldLabel);
            _batchBlueprint = EditorGUILayout.ToggleLeft(L("蓝图  .shadergraph → .bps", "Blueprint  .shadergraph → .bps"), _batchBlueprint);
            _batchVfx = EditorGUILayout.ToggleLeft(L("VFX  .vfx → .laya.vfx", "VFX  .vfx → .laya.vfx"), _batchVfx);
            if (_batchVfx)
            {
                EditorGUI.indentLevel++;
                _batchPrefabVariant = EditorGUILayout.ToggleLeft(L("应用 prefab 属性覆盖（.prefab 上 VisualEffect 组件的覆盖 → 生成变体 .laya.vfx）",
                    "Apply prefab overrides (VisualEffect component overrides on .prefab → variant .laya.vfx)"), _batchPrefabVariant);
                EditorGUI.indentLevel--;
            }

            DrawCommonOptions();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_sourceDir) || string.IsNullOrEmpty(_targetDir) || (!_batchBlueprint && !_batchVfx)))
                if (GUILayout.Button(L("开始批量转换", "Start Batch Convert"), GUILayout.Height(30))) RunBatch();
        }

        // 蓝图选项（VFX 侧的 LayaVFXSample 资源目录自动探测，不暴露给用户）
        private void DrawCommonOptions()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(L("蓝图选项", "Blueprint Options"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _bpSupportVfx = EditorGUILayout.ToggleLeft(L("--support-vfx（VFX 用的蓝图强制 supportVFX）", "--support-vfx (force supportVFX for VFX blueprints)"), _bpSupportVfx);
            _bpMaterialProps = EditorGUILayout.ToggleLeft(L("--material-properties（属性转 uniform）", "--material-properties (properties as uniforms)"), _bpMaterialProps);
            EditorGUI.indentLevel--;
        }

        private static string FileKind(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            if (p.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)) return "bp";
            if (p.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase)) return "vfx";
            return null;
        }

        private string FileField(string label, string val)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130));
            val = EditorGUILayout.TextField(val);
            if (GUILayout.Button(L("浏览", "Browse"), GUILayout.Width(50)))
            {
                string dir = !string.IsNullOrEmpty(val) && File.Exists(val) ? Path.GetDirectoryName(val) : Application.dataPath;
                string picked = EditorUtility.OpenFilePanelWithFilters(label, dir, new[] { "ShaderGraph / VFX", "shadergraph,vfx" });
                if (!string.IsNullOrEmpty(picked)) val = picked;
                GUIUtility.keyboardControl = 0;
            }
            EditorGUILayout.EndHorizontal();
            return val;
        }

        private string FolderField(string label, string val)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130));
            val = EditorGUILayout.TextField(val);
            if (GUILayout.Button(L("浏览", "Browse"), GUILayout.Width(50)))
            {
                string picked = EditorUtility.OpenFolderPanel(label, string.IsNullOrEmpty(val) ? Application.dataPath : val, "");
                if (!string.IsNullOrEmpty(picked)) val = picked;
                GUIUtility.keyboardControl = 0;
            }
            EditorGUILayout.EndHorizontal();
            return val;
        }

        private void Persist()
        {
            EditorPrefs.SetInt(PK + "tab", (int)_tab);
            EditorPrefs.SetString(PK + "srcFile", _srcFile); EditorPrefs.SetString(PK + "singleOut", _singleOutDir);
            EditorPrefs.SetString(PK + "src", _sourceDir); EditorPrefs.SetString(PK + "dst", _targetDir);
            EditorPrefs.SetString(PK + "laya", _layaSampleDir);
            EditorPrefs.SetBool(PK + "bp", _batchBlueprint); EditorPrefs.SetBool(PK + "vfx", _batchVfx);
            EditorPrefs.SetBool(PK + "pv", _batchPrefabVariant);
            EditorPrefs.SetBool(PK + "bpvfx", _bpSupportVfx); EditorPrefs.SetBool(PK + "bpmat", _bpMaterialProps);
        }

        private VfxResourceScanner BuildScanner(System.Text.StringBuilder sb)
        {
            EditorUtility.DisplayProgressBar(L("转换器", "Converter"), L("扫描 VFX 资源映射 (.meta) ...", "Scanning VFX resource maps (.meta) ..."), 0.1f);
            var scanner = new VfxResourceScanner();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string defsPath = Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/Editor/Export/vfx/vfx-defs.json");
            // LayaVFXSample 目录自动探测（不再暴露给用户）：优先用记忆值，失效则重新探测。
            if (string.IsNullOrEmpty(_layaSampleDir) || !Directory.Exists(_layaSampleDir)) _layaSampleDir = DetectLayaSample();
            if (string.IsNullOrEmpty(_layaSampleDir)) sb.AppendLine(L("⚠ 未找到 LayaVFXSample 目录，VFX 的纹理/网格/shader 资源映射将缺失（写占位 guid）。",
                "⚠ LayaVFXSample folder not found; VFX texture/mesh/shader resource maps will be missing (placeholder guids written)."));
            else sb.AppendLine("[scan] LayaVFXSample = " + _layaSampleDir);
            scanner.Build(projectRoot, defsPath, string.IsNullOrEmpty(_layaSampleDir) ? null : _layaSampleDir);
            foreach (var l in scanner.Log) sb.AppendLine("[scan] " + l);
            if (scanner.Defs == null) { sb.AppendLine(L("✗ 找不到 vfx-defs.json：", "✗ vfx-defs.json not found: ") + defsPath); return null; }
            return scanner;
        }

        // ── 单文件执行 ──────────────────────────────────────────
        private void RunSingle(string kind)
        {
            Persist();
            var sb = new System.Text.StringBuilder();
            try
            {
                string outDir = string.IsNullOrEmpty(_singleOutDir) ? Path.GetDirectoryName(_srcFile) : _singleOutDir;
                Directory.CreateDirectory(outDir);
                string baseName = Path.GetFileNameWithoutExtension(_srcFile);
                if (kind == "bp")
                {
                    string outPath = Path.Combine(outDir, baseName + ".bps");
                    ConvertBlueprint(_srcFile, outPath);
                    sb.AppendLine("✓ [bp]  " + _srcFile + "\n     → " + outPath);
                }
                else if (kind == "vfx")
                {
                    var scanner = BuildScanner(sb);
                    if (scanner != null)
                    {
                        string outPath = Path.Combine(outDir, baseName + ".laya.vfx");
                        string json = ConvertVfx(_srcFile, outPath, scanner);
                        sb.AppendLine("✓ [vfx] " + _srcFile + "\n     → " + outPath);
                        var copied = new HashSet<string>(); var missing = new HashSet<string>(); var seen = new HashSet<string>(); var copiedLib = new HashSet<string>();
                        CopyDependencies(json, Path.Combine(outDir, "_deps"), scanner, copied, missing, seen, copiedLib);
                        ReportDeps(sb, copied, missing, copiedLib, Path.Combine(outDir, "_deps"));
                    }
                }
            }
            catch (Exception e) { sb.AppendLine(L("✗ 转换失败：", "✗ Convert failed: ") + e.Message + "\n" + e.StackTrace); }
            finally { EditorUtility.ClearProgressBar(); }
            _log = sb.ToString();
            _logScroll = new Vector2(0, float.MaxValue);
            AssetDatabase.Refresh();
        }

        // ── 多文件执行 ──────────────────────────────────────────
        private void RunBatch()
        {
            Persist();
            var sb = new System.Text.StringBuilder();
            int okBp = 0, okVfx = 0, fail = 0;
            try
            {
                if (!Directory.Exists(_sourceDir)) { _log = L("源目录不存在：", "Source dir not found: ") + _sourceDir; return; }
                Directory.CreateDirectory(_targetDir);

                VfxResourceScanner scanner = null;
                bool doVfx = _batchVfx;
                // 蓝图转换也需要 scanner：用 BlueprintShaderByName 给项目内 .bps 写与 shaderRes 一致的 .bps.meta uuid。
                if (doVfx || _batchBlueprint) { scanner = BuildScanner(sb); if (scanner == null && doVfx) doVfx = false; }

                var files = new List<string>();
                if (_batchBlueprint) files.AddRange(Directory.GetFiles(_sourceDir, "*.shadergraph", SearchOption.AllDirectories));
                if (doVfx) files.AddRange(Directory.GetFiles(_sourceDir, "*.vfx", SearchOption.AllDirectories));

                string depRoot = Path.Combine(_targetDir, "_deps");
                var copied = new HashSet<string>(); var missing = new HashSet<string>(); var seen = new HashSet<string>(); var copiedLib = new HashSet<string>();
                var inProjectShaderUuids = new HashSet<string>();   // 项目内已转换生成的蓝图 shader uuid（_deps 跳过它们）

                for (int i = 0; i < files.Count; i++)
                {
                    string f = files[i];
                    EditorUtility.DisplayProgressBar(L("转换器", "Converter"), Path.GetFileName(f) + "  (" + (i + 1) + "/" + files.Count + ")", (float)(i + 1) / Math.Max(1, files.Count));
                    string rel = f.Substring(_sourceDir.Length).TrimStart('/', '\\');
                    try
                    {
                        if (f.EndsWith(".shadergraph"))
                        {
                            string outPath = Path.Combine(_targetDir, ChangeExt(rel, ".bps"));
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            ConvertBlueprint(f, outPath, scanner, inProjectShaderUuids);
                            okBp++; sb.AppendLine("✓ [bp]  " + rel);
                        }
                        else
                        {
                            string outPath = Path.Combine(_targetDir, ChangeExt(rel, ".laya.vfx"));
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            string json = ConvertVfx(f, outPath, scanner);
                            CopyDependencies(json, depRoot, scanner, copied, missing, seen, copiedLib, inProjectShaderUuids);
                            okVfx++; sb.AppendLine("✓ [vfx] " + rel);
                        }
                    }
                    catch (Exception e) { fail++; sb.AppendLine("✗ " + rel + " : " + e.Message); }
                }
                // ── prefab 属性覆盖 → 变体 .laya.vfx（对应 JS convert-uni-vfx.js phasePrefabVariant） ──
                if (doVfx && _batchPrefabVariant)
                {
                    EditorUtility.DisplayProgressBar(L("转换器", "Converter"), L("应用 prefab 属性覆盖（变体）...", "Applying prefab overrides (variants) ..."), 0.95f);
                    int okPv = 0, skipPv = 0, failPv = 0;
                    // unity .vfx guid → 源内相对路径（找 prefab 引用的 base）
                    var vfxByGuid = new Dictionary<string, string>();
                    foreach (var vf in Directory.GetFiles(_sourceDir, "*.vfx", SearchOption.AllDirectories))
                    {
                        string meta = vf + ".meta"; if (!File.Exists(meta)) continue;
                        var gm = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(meta), @"^guid:\s*([a-f0-9]{32})", System.Text.RegularExpressions.RegexOptions.Multiline);
                        if (gm.Success) vfxByGuid[gm.Groups[1].Value] = vf.Substring(_sourceDir.Length).TrimStart('/', '\\').Replace('\\', '/');
                    }
                    foreach (var pf in Directory.GetFiles(_sourceDir, "*.prefab", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string prefabYaml = File.ReadAllText(pf);
                            var blk = VfxPrefabVariant.ParseVisualEffectBlock(prefabYaml);
                            if (blk == null) { skipPv++; continue; }
                            string baseVfxRel;
                            if (!vfxByGuid.TryGetValue(blk.AssetGuid, out baseVfxRel)) { skipPv++; continue; }
                            // base = 本次批量转换刚产出的 .laya.vfx
                            string baseLayaPath = Path.Combine(_targetDir, ChangeExt(baseVfxRel, ".laya.vfx"));
                            if (!File.Exists(baseLayaPath)) { skipPv++; continue; }
                            string prefabRel = pf.Substring(_sourceDir.Length).TrimStart('/', '\\').Replace('\\', '/');
                            string outRel = VfxPrefabVariant.VariantOutRelPath(prefabRel);
                            string csText = VfxPrefabVariant.ConvertOne(File.ReadAllText(baseLayaPath), prefabYaml, scanner.AssetMapping);
                            if (csText == null) { skipPv++; continue; }
                            string outPath = Path.Combine(_targetDir, outRel);
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            File.WriteAllText(outPath, csText);
                            okPv++; sb.AppendLine(L("✓ [变体] ", "✓ [variant] ") + prefabRel + " → " + outRel);
                            // 变体引用的资源（覆盖进来的新纹理/mesh）也拷依赖
                            CopyDependencies(csText, depRoot, scanner, copied, missing, seen, copiedLib, inProjectShaderUuids);
                        }
                        catch (Exception e) { failPv++; sb.AppendLine(L("✗ [变体] ", "✗ [variant] ") + pf.Substring(_sourceDir.Length) + " : " + e.Message); }
                    }
                    sb.AppendLine(L("prefab 变体：生成 " + okPv + "，跳过 " + skipPv + "（无 VisualEffect 覆盖/找不到 base），失败 " + failPv,
                        "Prefab variants: generated " + okPv + ", skipped " + skipPv + " (no VisualEffect override / base not found), failed " + failPv));
                }

                if (doVfx) ReportDeps(sb, copied, missing, copiedLib, depRoot);
            }
            finally { EditorUtility.ClearProgressBar(); }

            sb.AppendLine(L("\n完成：蓝图 " + okBp + "，VFX " + okVfx + "，失败 " + fail + "。输出目录：" + _targetDir,
                "\nDone: Blueprint " + okBp + ", VFX " + okVfx + ", failed " + fail + ". Output: " + _targetDir));
            _log = sb.ToString();
            _logScroll = new Vector2(0, float.MaxValue);
            AssetDatabase.Refresh();
        }

        private void ReportDeps(System.Text.StringBuilder sb, HashSet<string> copied, HashSet<string> missing, HashSet<string> copiedLib, string depRoot)
        {
            sb.AppendLine(L("\n── 依赖资源（mesh / 纹理 / shader）──", "\n── Dependencies (mesh / texture / shader) ──"));
            sb.AppendLine(L("已拷贝 " + copied.Count + " 个文件（含 .meta）→ " + depRoot, "Copied " + copied.Count + " files (incl. .meta) → " + depRoot));
            if (copiedLib.Count > 0)
                sb.AppendLine(L("已镜像 " + copiedLib.Count + " 个已编译产物 → 目标工程 library（免运行时 shader 404）",
                    "Mirrored " + copiedLib.Count + " compiled artifacts → target project library (avoids runtime shader 404)"));
            sb.AppendLine(L("提示：把 _deps 目录下的内容合并进你的 Laya 工程 assets 目录，运行时才能按 uuid 找到 mesh/纹理。",
                "Tip: merge the _deps folder into your Laya project's assets so meshes/textures resolve by uuid at runtime."));
            if (missing.Count > 0)
            {
                sb.AppendLine(L("⚠ 有 " + missing.Count + " 个资源在 LayaVFXSample 里找不到（未导出成 Laya 资源，运行时会缺失）：",
                    "⚠ " + missing.Count + " resources not found in LayaVFXSample (not exported as Laya assets; missing at runtime):"));
                int n = 0; foreach (var u in missing) { sb.AppendLine("    res://" + u); if (++n >= 30) { sb.AppendLine(L("    ...(还有 " + (missing.Count - 30) + " 个)", "    ...(and " + (missing.Count - 30) + " more)")); break; } }
                sb.AppendLine(L("    → 这些多半是还没用 Export Tool 导出的 FBX/贴图，请先导出再重转。",
                    "    → Most are FBX/textures not yet exported via Export Tool; export them first, then reconvert."));
            }
        }

        private void ConvertBlueprint(string inPath, string outPath, VfxResourceScanner scanner = null, HashSet<string> inProjectShaderUuids = null)
        {
            string text = File.ReadAllText(inPath);
            var objects = SgIndex.ParseShadergraph(text);
            var idx = SgIndex.Build(objects);
            if (idx.GraphData == null) throw new Exception("GraphData not found");
            var opts = new ShaderGraphConverter.Options { ForceSupportVFX = _bpSupportVfx, MaterialPropsMode = _bpMaterialProps };
            if (opts.SgInstanceMode) opts.SgIncludeRelPath = Path.GetFileNameWithoutExtension(outPath) + "_sgprop.glsl";
            var conv = new ShaderGraphConverter(idx, opts);
            File.WriteAllText(outPath, conv.Convert().Serialize(2));

            // 写 .bps.meta：uuid 用 VFX 转换里 shaderRes 引用的同一个（按 shader 名从 BlueprintShaderByName 取）。
            // 保证项目内 .bps 的 uuid == .laya.vfx 的 shaderRes → IDE 按同一 uuid 编译 → 运行期不再 404。
            // 该 uuid 记入 inProjectShaderUuids，令 CopyDependencies 跳过 _deps 重复拷贝（避免同 uuid 两份资源冲突）。
            if (scanner != null)
            {
                string shaderName = Path.GetFileNameWithoutExtension(outPath);
                string bpsRes;
                if (scanner.BlueprintShaderByName.TryGetValue(shaderName, out bpsRes) && bpsRes != null && bpsRes.StartsWith("res://"))
                {
                    string uuid = bpsRes.Substring("res://".Length);
                    File.WriteAllText(outPath + ".meta", "{\n  \"uuid\": \"" + uuid + "\"\n}");
                    if (inProjectShaderUuids != null) inProjectShaderUuids.Add(uuid);
                }
            }
        }

        // 返回转换产物 JSON 文本（供依赖收集用）。
        private string ConvertVfx(string inPath, string outPath, VfxResourceScanner scanner)
        {
            string yaml = File.ReadAllText(inPath);
            var entries = UnityYamlParser.ParseEntries(yaml, scanner.GuidToClass);
            var conv = new VfxConverter(entries);
            scanner.Apply(conv);
            string json = conv.Convert(yaml).Serialize(2);
            File.WriteAllText(outPath, json);
            // 逐粒子蜡池色:把 .bps 里该 SG 属性的 uniform 用法换成 vertexColor.b
            // (配合 VFX 侧 InjectPerParticleColorIndex 注入的 setAttribute(color, B, SpawnIndex))
            PatchPerParticleColorBps(_targetDir, conv.PerParticleColorPatches);
            return json;
        }

        private static readonly System.Text.RegularExpressions.Regex UuidRx =
            new System.Text.RegularExpressions.Regex(@"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");

        /// <summary>
        /// 逐粒子蜡池色 shader 侧固化:把 .bps 的 compileShader 里 SG 属性的 uniform 用法(如 vec3(_Color_Index))
        /// 换成 vertexColor.b(color.b 自由通道;VFX 侧已注入 setAttribute(color, channels=4, SpawnIndex))。
        /// 按 .bps.meta 的 uuid 严格匹配,只改命中的 .bps。
        /// ⚠ 只改 compileShader 缓存文本;若 IDE 从节点图重编译会覆盖(节点图仍是 _Color_Index 属性节点),
        ///   那种情况需要更深的节点改接(把属性节点重接到 VertexColor.b),此处未做。
        /// </summary>
        private void PatchPerParticleColorBps(string searchRoot,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> patches)
        {
            if (patches == null || patches.Count == 0 || string.IsNullOrEmpty(searchRoot) || !Directory.Exists(searchRoot)) return;
            foreach (var bps in Directory.GetFiles(searchRoot, "*.bps", SearchOption.AllDirectories))
            {
                string metaPath = bps + ".meta";
                if (!File.Exists(metaPath)) continue;
                var mm = UuidRx.Match(File.ReadAllText(metaPath));
                if (!mm.Success) continue;
                string uuid = mm.Value.ToLowerInvariant();
                string txt = null; bool dirty = false;
                foreach (var kv in patches)
                {
                    if (!string.Equals(uuid, kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (txt == null) txt = File.ReadAllText(bps);
                    string needle = "vec3(" + kv.Value + ")";
                    if (!txt.Contains(needle)) continue;
                    txt = txt.Replace(needle, "vec3(vertexColor.b)");
                    dirty = true;
                }
                if (dirty) File.WriteAllText(bps, txt);
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ResRefRx =
            new System.Text.RegularExpressions.Regex(@"res://([0-9a-fA-F\-]{36})");
        // 会被递归扫描内部 res:// 依赖的文本型 Laya 资源（.meta 总是扫）
        private static readonly HashSet<string> TextResExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".bps", ".lmat", ".lm", ".ls", ".lh", ".lmap", ".prefab", ".scene", ".taa" };

        private static IEnumerable<string> ExtractUuids(string text)
        {
            var set = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in ResRefRx.Matches(text)) set.Add(m.Groups[1].Value.ToLowerInvariant());
            return set;
        }

        /// <summary>
        /// 从转换产物 JSON 出发，递归收集所有 res://uuid 依赖闭包，把每个资源连同 .meta 拷到
        /// depRoot 下（保留相对 LayaVFXSample/assets 的路径）。copied/missing 跨文件累计去重。
        /// </summary>
        private void CopyDependencies(string producedJson, string depRoot, VfxResourceScanner scanner,
                                      HashSet<string> copiedFiles, HashSet<string> missingUuids, HashSet<string> seenUuids,
                                      HashSet<string> copiedLib, HashSet<string> inProjectShaderUuids = null)
        {
            if (string.IsNullOrEmpty(scanner.LayaAssetsRoot)) return;
            // 源工程 library（编译产物缓存）与目标工程 library：把已编译产物一并带过去，
            // 免得目标工程 IDE 尚未按需编译某个 .bps 蓝图时，运行时取 _lib_/<uuid>@0.shader 报 404。
            string srcLib = Path.Combine(Directory.GetParent(scanner.LayaAssetsRoot).FullName, "library");
            string dstLib = FindTargetLibraryDir(depRoot);
            var queue = new Queue<string>();
            foreach (var u in ExtractUuids(producedJson)) if (seenUuids.Add(u)) queue.Enqueue(u);
            while (queue.Count > 0)
            {
                string u = queue.Dequeue();
                // 该 shader 已在项目内转换生成（.bps+.meta 用同一 uuid），不再从 LayaVFXSample 拷 _deps 重复，也不算缺失。
                if (inProjectShaderUuids != null && inProjectShaderUuids.Contains(u)) continue;
                string src;
                if (!scanner.UuidToSourceFile.TryGetValue(u, out src) || !File.Exists(src)) { missingUuids.Add(u); continue; }

                string rel = src.Substring(scanner.LayaAssetsRoot.Length).TrimStart('/', '\\');
                string dst = Path.Combine(depRoot, rel);
                if (copiedFiles.Add(dst))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                    if (File.Exists(src + ".meta")) File.Copy(src + ".meta", dst + ".meta", true);
                    // 顺带镜像该 uuid 在源工程 library 里的已编译产物（<uuid>@0.shader/@1.shader/.json 等）。
                    // 二者按同一 uuid 编译、字节一致；目标 IDE 若重新编译只会生成相同内容，不冲突。
                    CopyLibraryArtifacts(u, srcLib, dstLib, copiedLib);
                }
                // 递归：文本型资源扫内容；所有资源都扫其 .meta（fbx/贴图的导入设置里也可能引用材质等）
                try { if (TextResExt.Contains(Path.GetExtension(src))) foreach (var u2 in ExtractUuids(File.ReadAllText(src))) if (seenUuids.Add(u2)) queue.Enqueue(u2); } catch { }
                try { if (File.Exists(src + ".meta")) foreach (var u2 in ExtractUuids(File.ReadAllText(src + ".meta"))) if (seenUuids.Add(u2)) queue.Enqueue(u2); } catch { }
            }
        }

        /// <summary>从 _deps 目录向上找目标 Laya 工程根（以根目录下的 *.laya 文件为标记），返回其 library 目录；找不到返回 null。</summary>
        private static string FindTargetLibraryDir(string depRoot)
        {
            try
            {
                var dir = new DirectoryInfo(depRoot);
                while (dir != null)
                {
                    if (dir.Exists && dir.GetFiles("*.laya").Length > 0)
                        return Path.Combine(dir.FullName, "library");
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        /// <summary>把某 uuid 在源工程 library 下的全部已编译产物（&lt;uuid&gt;* 文件）拷到目标工程 library 的同名前缀目录。</summary>
        private static void CopyLibraryArtifacts(string uuid, string srcLib, string dstLib, HashSet<string> copiedLib)
        {
            if (string.IsNullOrEmpty(srcLib) || string.IsNullOrEmpty(dstLib) || uuid == null || uuid.Length < 2) return;
            string pfx = uuid.Substring(0, 2);
            string srcDir = Path.Combine(srcLib, pfx);
            if (!Directory.Exists(srcDir)) return;
            string dstDir = Path.Combine(dstLib, pfx);
            try
            {
                foreach (var f in Directory.GetFiles(srcDir, uuid + "*"))
                {
                    string dstFile = Path.Combine(dstDir, Path.GetFileName(f));
                    if (!copiedLib.Add(dstFile)) continue;
                    Directory.CreateDirectory(dstDir);
                    File.Copy(f, dstFile, true);
                }
            }
            catch { }
        }

        private static string ChangeExt(string rel, string newExt)
        {
            string dir = Path.GetDirectoryName(rel);
            string baseName = Path.GetFileName(rel);
            int dot = baseName.LastIndexOf('.');
            if (dot >= 0) baseName = baseName.Substring(0, dot);
            baseName += newExt;
            return string.IsNullOrEmpty(dir) ? baseName : Path.Combine(dir, baseName);
        }
    }
}
