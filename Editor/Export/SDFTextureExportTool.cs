// LayaAir VFX SDF Texture Export Tool
// 把 Unity 烘焙的 SDF（3D RenderTexture 或 Texture3D）导出为 KTX1 标准格式
//
// 输出文件：
//   <folder>/<name>.ktx        — Khronos Texture v1（含 header + RGBA32F 体素数据）
//   <folder>/<name>.ktx.meta   — Laya 资源 UUID
//
// KTX1 spec: https://registry.khronos.org/KTX/specs/1.0/ktxspec.v1.html
// 仅支持 KTX1（不支持 KTX2 / Basis Universal）。
// Pixel format 强制 R32G32B32A32_SFloat（GL_RGBA32F），SDF 距离值在 .r 通道。

using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class SDFTextureExportTool : EditorWindow
{
    private Texture sourceTexture;
    private string outputFolder = "";
    private string outputFilename = "ExportedSDF";
    private bool exporting = false;
    private string statusMessage = "";

    // ── KTX1 magic identifier (12 bytes): «KTX 11»\r\n\x1A\n ──
    private static readonly byte[] KTX1_IDENTIFIER = new byte[] {
        0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    // ── OpenGL constants (KTX header 字段需要 GL enum 值) ──
    private const uint GL_FLOAT          = 0x1406;
    private const uint GL_RGBA           = 0x1908;
    private const uint GL_RGBA32F        = 0x8814;
    private const uint KTX_ENDIAN_LE     = 0x04030201;

    [MenuItem("LayaAir3D 3.0/Export SDF Texture (.ktx)", false, 5)]
    public static void OpenWindow()
    {
        var win = GetWindow<SDFTextureExportTool>("Export SDF for LayaAir");
        win.minSize = new Vector2(460, 280);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export Unity SDF (RenderTexture / Texture3D) → KTX1", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 检测 SDF Source 变化时自动同步 Output Filename 为源文件名
        EditorGUI.BeginChangeCheck();
        Texture newSource = (Texture)EditorGUILayout.ObjectField("SDF Source", sourceTexture, typeof(Texture), false);
        if (EditorGUI.EndChangeCheck())
        {
            sourceTexture = newSource;
            if (sourceTexture != null && !string.IsNullOrEmpty(sourceTexture.name))
            {
                outputFilename = sourceTexture.name;
            }
        }

        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string p = EditorUtility.OpenFolderPanel("Select Output Folder (LayaAir VFX assets/resources)", outputFolder, "");
            if (!string.IsNullOrEmpty(p)) outputFolder = p;
        }
        EditorGUILayout.EndHorizontal();

        outputFilename = EditorGUILayout.TextField("Output Filename", outputFilename);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Output:\n" +
            "  <folder>/<name>.ktx       — KTX1 standard (header + raw RGBA32F 3D voxels)\n" +
            "  <folder>/<name>.ktx.meta  — Laya resource UUID\n\n" +
            "Source supported: 3D RenderTexture (e.g. SDFBaker output) or Texture3D asset.\n" +
            "Pixel format forced to GL_RGBA32F. SDF distance value stored in .r channel.\n" +
            "Note: KTX2 / Basis Universal NOT supported, only KTX1.",
            MessageType.Info);

        if (sourceTexture != null)
        {
            int w, h, d;
            string srcFmt;
            if (TryGetSize(sourceTexture, out w, out h, out d, out srcFmt))
            {
                long pixelBytes = (long)w * h * d * 16;
                EditorGUILayout.LabelField($"Source: {w} × {h} × {d} ({srcFmt})", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"KTX file size ≈ {(pixelBytes + 80) / 1024} KB (header 64 + imageSize 4 + voxels)", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Source must be a 3D RenderTexture or Texture3D.", MessageType.Warning);
            }
        }

        GUI.enabled = !exporting && sourceTexture != null && !string.IsNullOrEmpty(outputFolder) && !string.IsNullOrEmpty(outputFilename);
        if (GUILayout.Button(exporting ? "Exporting..." : "Export", GUILayout.Height(30)))
        {
            DoExport();
        }
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        }
    }

    private bool TryGetSize(Texture tex, out int w, out int h, out int d, out string fmt)
    {
        if (tex is RenderTexture rt && rt.dimension == TextureDimension.Tex3D)
        {
            w = rt.width; h = rt.height; d = rt.volumeDepth; fmt = rt.graphicsFormat.ToString();
            return true;
        }
        if (tex is Texture3D t3d)
        {
            w = t3d.width; h = t3d.height; d = t3d.depth; fmt = t3d.graphicsFormat.ToString();
            return true;
        }
        w = h = d = 0; fmt = "?";
        return false;
    }

    private void DoExport()
    {
        statusMessage = "";
        int width, height, depth;
        string srcFmt;
        if (!TryGetSize(sourceTexture, out width, out height, out depth, out srcFmt))
        {
            statusMessage = "ERROR: source must be a 3D RenderTexture or Texture3D";
            return;
        }

        // 强制输出 R32G32B32A32_SFloat = GL_RGBA32F
        // SDF 单 channel 值（如 R16_SFloat）会自动复制到 .r，其他通道为 0
        GraphicsFormat dstFormat = GraphicsFormat.R32G32B32A32_SFloat;
        long pixelBytes = (long)width * height * depth * 16;

        Debug.Log($"[SDFExport] start: {sourceTexture.name} {width}x{height}x{depth} src={srcFmt} dst=GL_RGBA32F (pixels={pixelBytes/1024} KB)");

        exporting = true;
        statusMessage = $"Reading {pixelBytes / 1024} KB from GPU...";
        Repaint();

        // 3D Texture 逐层 readback：Unity 的 AsyncGPUReadback.Request 对 Texture3D 单次调用只读 1 层
        // 即使用 9 参数 bounding box 重载（z=0, depth=N）也不一定生效（实测返回 1 层数据）
        // 改为对每个 z slice 单独 Request，回调里按 z 偏移合并到全量 buffer
        int sliceBytes = width * height * 16;   // R32G32B32A32_SFloat = 16 byte/pixel
        byte[] mergedData = new byte[pixelBytes];
        int completedSlices = 0;
        bool anyError = false;

        for (int z = 0; z < depth; z++)
        {
            int sliceZ = z;   // 闭包捕获
            AsyncGPUReadback.Request(sourceTexture, 0, 0, width, 0, height, sliceZ, 1, dstFormat, (req) =>
            {
                if (anyError) return;
                if (req.hasError)
                {
                    anyError = true;
                    exporting = false;
                    statusMessage = $"ERROR: readback failed on slice z={sliceZ}";
                    Debug.LogError("[SDFExport] " + statusMessage);
                    Repaint();
                    return;
                }
                NativeArray<byte> sliceData = req.GetData<byte>();
                if (sliceData.Length != sliceBytes)
                {
                    anyError = true;
                    exporting = false;
                    statusMessage = $"ERROR: slice z={sliceZ} size mismatch — expected {sliceBytes}, got {sliceData.Length}";
                    Debug.LogError("[SDFExport] " + statusMessage);
                    Repaint();
                    return;
                }
                // 写到全量 buffer 的对应 z 偏移
                NativeArray<byte>.Copy(sliceData, 0, mergedData, sliceZ * sliceBytes, sliceBytes);
                completedSlices++;

                if (completedSlices == depth)
                {
                    // 全部读完，写文件
                    exporting = false;
                    try
                    {
                        if (!Directory.Exists(outputFolder))
                            Directory.CreateDirectory(outputFolder);

                        string ktxPath = Path.Combine(outputFolder, outputFilename + ".ktx");
                        WriteKTX1(ktxPath, width, height, depth, mergedData);

                        string metaPath = ktxPath + ".meta";
                        if (!File.Exists(metaPath))
                        {
                            JSONObject meta = new JSONObject(JSONObject.Type.OBJECT);
                            meta.AddField("uuid", System.Guid.NewGuid().ToString());
                            meta.AddField("importer", new JSONObject(JSONObject.Type.OBJECT));
                            File.WriteAllText(metaPath, meta.Print(true));
                        }

                        long fileSize = new FileInfo(ktxPath).Length;
                        statusMessage = $"OK: {ktxPath}\n     ({fileSize / 1024} KB written, {depth} slices)";
                        Debug.Log("[SDFExport] " + statusMessage);

                        AssetDatabase.Refresh();
                        Repaint();
                    }
                    catch (Exception e)
                    {
                        statusMessage = "ERROR: " + e.Message;
                        Debug.LogException(e);
                        Repaint();
                    }
                }
            });
        }
    }

    /// <summary>
    /// 写 KTX1 标准文件
    /// Header 64 字节（12 magic + 13×4 uint32 fields），后接 4 字节 imageSize + 像素数据
    /// 单 mip / 无 KeyValue 元数据 / 单 face / 非 array
    /// </summary>
    private static void WriteKTX1(string path, int width, int height, int depth, byte[] pixelData)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            // ── 12 bytes: identifier ──
            w.Write(KTX1_IDENTIFIER);

            // ── 13 × 4 bytes: header fields（little-endian uint32） ──
            w.Write(KTX_ENDIAN_LE);                  // endianness
            w.Write(GL_FLOAT);                       // glType (uncompressed → 数据类型)
            w.Write((uint)4);                        // glTypeSize (4 bytes per float)
            w.Write(GL_RGBA);                        // glFormat
            w.Write(GL_RGBA32F);                     // glInternalFormat
            w.Write(GL_RGBA);                        // glBaseInternalFormat
            w.Write((uint)width);                    // pixelWidth
            w.Write((uint)height);                   // pixelHeight
            w.Write((uint)depth);                    // pixelDepth (3D texture: 非零)
            w.Write((uint)0);                        // numberOfArrayElements (非 array)
            w.Write((uint)1);                        // numberOfFaces (普通 texture: 1)
            w.Write((uint)1);                        // numberOfMipmapLevels (无 mip)
            w.Write((uint)0);                        // bytesOfKeyValueData (无 metadata)

            // ── Mip level 0 ──
            uint imageSize = (uint)pixelData.Length;
            w.Write(imageSize);                      // 4 bytes: imageSize
            w.Write(pixelData);                      // pixel data

            // mipPadding：对齐到 4 字节（pixelData 长度是 16 倍数 → 自然对齐，padding 0）
            int padding = (3 - ((int)imageSize + 3) % 4);
            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
