using System.IO;
using UnityEngine;

namespace LayaAir3.Converter
{
    /// <summary>
    /// 蓝图转换器的静态工具方法（把 .shadergraph 转成 .bps，与 JS 产物 diff 对齐）。
    /// 对应 JS unity-shader-to-laya.js 的 CLI main()。菜单入口统一收敛到 ConverterWindow 弹窗，这里不再挂 [MenuItem]。
    /// </summary>
    public static class ShaderGraphConvertMenu
    {
        /// <summary>转换单个文件，返回输出 .bps 绝对路径。</summary>
        public static string ConvertFile(string shadergraphAssetPath, ShaderGraphConverter.Options opts)
        {
            opts = opts ?? new ShaderGraphConverter.Options();
            string absIn = ToAbsolute(shadergraphAssetPath);
            string text = File.ReadAllText(absIn);
            var objects = SgIndex.ParseShadergraph(text);
            var idx = SgIndex.Build(objects);
            if (idx.GraphData == null) throw new System.Exception("GraphData not found");

            string baseName = Path.GetFileNameWithoutExtension(absIn);
            string dir = Path.GetDirectoryName(absIn);
            if (opts.SgInstanceMode && opts.SgIncludeRelPath == null)
                opts.SgIncludeRelPath = baseName + "_sgprop.glsl";

            var conv = new ShaderGraphConverter(idx, opts);
            Jval bps = conv.Convert();

            string outPath = Path.Combine(dir, baseName + ".bps");
            File.WriteAllText(outPath, bps.Serialize(2));

            if (opts.SgInstanceMode && conv.HasSgProperties)
            {
                string glsl = conv.GenerateSgPropertyGlsl();
                if (glsl != null)
                    File.WriteAllText(Path.Combine(dir, opts.SgIncludeRelPath), glsl);
            }

            if (conv.UnmappedNodes.Count > 0)
            {
                var sb = new System.Text.StringBuilder("[ShaderGraphConverter] 未映射节点类型：");
                foreach (var kv in conv.UnmappedNodes) sb.Append("\n  " + kv.Key + ": " + kv.Value);
                Debug.LogWarning(sb.ToString());
            }
            return outPath;
        }

        private static string ToAbsolute(string assetPath)
        {
            if (Path.IsPathRooted(assetPath)) return assetPath;
            // assetPath 形如 "Assets/xxx"；Application.dataPath 是 <proj>/Assets
            string projRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
