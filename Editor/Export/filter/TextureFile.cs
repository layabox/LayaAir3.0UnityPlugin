using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FileUtil = Util.FileUtil;


internal enum LayaTextureImportFormat {
    /**纹理格式_R8G8B8A8。*/
    R8G8B8A8 = 0,
    /**纹理格式_R8G8B8。*/
    R8G8B8 = 1,
    /** 压缩纹理格式 */
    COMPRESSED = 10
}

enum LayaTextureFormat {
    /**纹理格式_R8G8B8。*/
    R8G8B8 = 0,
    /**纹理格式_R8G8B8A8。*/
    R8G8B8A8 = 1,
    /** 压缩纹理 */
    COMPRESSED = 4, // DXT5 
}

internal enum WrapMode
{
    /** 循环平铺。*/
    Repeat = 0,
    /** 超过UV边界后采用最后一个像素。*/
    Clamp = 1,
    /** 镜像采样 */
    Mirrored = 2
}

// [DllImport("OpenEXRPlugin")]
// private static extern void EncodeHDR(Texture2D texture, string fileName);

internal class TextureFile : FileData
{
    private const string DefaultParticleTemplateGuid = "6017d482bbe642eebca96d26180f4130";

    [DllImport("msvcrt.dll")]
    public static extern double frexp(double val, out int eptr);

    public static int JPGQuality = 75;
    public static string LOGHEAD = "LayaAir3D: ";
    private Texture2D texture;
    private JSONObject constructParams;
    private JSONObject propertyParams;
    private bool rgbmEncoding;
    private bool isNormal;
    private LayaTextureImportFormat importFormat;
    private bool hasAlphaChannel;
    private bool m_isBuiltinTexture;
    private bool m_isDefaultParticleTexture;
    private bool m_forceReadable;

    // 导出前保存的原始导入设置，SaveFile 结束后用于还原，确保不污染 Unity 项目资源
    private string              m_importerPath           = null;
    private TextureImporterType m_origTextureType        = TextureImporterType.Default;
    private bool                m_origIsReadable         = false;
    private bool                m_importSettingsModified = false;
    // 是否作为 Laya 2D 精灵纹理导出（meta 只含 textureType:2，不生成 3D constructParams）
    private bool                m_isSpriteTexture        = false;

    public TextureFile(string originPath, Texture2D texture, bool isNormal,
                       bool isSpriteTexture = false) : base(null) {
        this.texture          = texture;
        this.isNormal         = isNormal;
        this.m_isSpriteTexture = isSpriteTexture;
        this.m_isBuiltinTexture = texture != null && ResoureMap.IsBuiltinResource(
            AssetDatabase.GetAssetPath(texture.GetInstanceID()));
        this.m_isDefaultParticleTexture = this.m_isBuiltinTexture &&
            ResoureMap.IsDefaultParticleTexture(texture);
        // updatePath 内部会调用 getOutFilePath，后者依赖 hasAlphaChannel 来决定
        // 输出扩展名（.png 或 .jpg）。但 getTextureInfo 才会准确设置 hasAlphaChannel，
        // 晚于 updatePath 执行，导致 hasAlphaChannel 始终是默认值 false，所有纹理
        // 输出路径都被错误地定为 .jpg。
        // 修复：用源文件扩展名提前预判 alpha，jpg/jpeg 肯定无 alpha，其余按格式判断。
        if (texture != null) {
            string srcExt = Path.GetExtension(originPath).ToLower();
            if (srcExt == ".jpg" || srcExt == ".jpeg") {
                this.hasAlphaChannel = false;
            } else {
                this.hasAlphaChannel = GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
            }
        } else {
            this.hasAlphaChannel = true; // 保守默认 png
        }
        this.updatePath(originPath);
        this.getTextureInfo();
    }

    /// <summary>
    /// 当无法获取TextureImporter时，使用默认值初始化纹理信息
    /// </summary>
    private void setImporterMetadata(JSONObject importerData) {
        // FileData 会读取已存在的 .meta；重复导出时必须替换旧字段，不能继续追加同名 key。
        while (this.m_metaData.keys != null && this.m_metaData.keys.Contains("importer")) {
            this.m_metaData.RemoveField("importer");
        }
        this.m_metaData.AddField("importer", importerData);
    }

    private void initDefaultTextureInfo() {
        this.importFormat = LayaTextureImportFormat.R8G8B8A8;
        this.hasAlphaChannel = true;
        
        var sRGB = !this.isNormal;
        WrapMode wrapMode = WrapMode.Clamp;
        if (texture != null) {
            switch (texture.wrapMode) {
                case TextureWrapMode.Repeat:
                    wrapMode = WrapMode.Repeat;
                    break;
                case TextureWrapMode.Mirror:
                    wrapMode = WrapMode.Mirrored;
                    break;
            }
        }
        bool generateMipmap = texture != null && texture.mipmapCount > 1;
        int anisoLevel = texture != null ? texture.anisoLevel : 1;
        int filterMode = 1;
        if (texture != null) {
            switch (texture.filterMode) {
                case FilterMode.Point:
                    filterMode = 0;
                    break;
                case FilterMode.Trilinear:
                    filterMode = 2;
                    break;
            }
        }
        
        // 默认importer数据
        JSONObject importData = new JSONObject(JSONObject.Type.OBJECT);
        importData.AddField("sRGB", sRGB);
        importData.AddField("wrapMode", (int)wrapMode);
        importData.AddField("generateMipmap", generateMipmap);
        importData.AddField("anisoLevel", anisoLevel);
        importData.AddField("alphaChannel", hasAlphaChannel);
        
        JSONObject platformDefault = new JSONObject(JSONObject.Type.OBJECT);
        platformDefault.AddField("format", (int)this.importFormat);
        importData.AddField("platformDefault", platformDefault);
        this.setImporterMetadata(importData);
        
        // constructParams
        this.constructParams.Add(texture != null ? texture.width : 1);
        this.constructParams.Add(texture != null ? texture.height : 1);
        this.constructParams.Add((int)LayaTextureFormat.R8G8B8A8);
        this.constructParams.Add(generateMipmap); // mipmap
        this.constructParams.Add(m_forceReadable); // canRead
        this.constructParams.Add(sRGB);
        
        // propertyParams
        this.propertyParams.AddField("filterMode", filterMode);
        this.propertyParams.AddField("wrapModeU", (int)wrapMode);
        this.propertyParams.AddField("wrapModeV", (int)wrapMode);
        this.propertyParams.AddField("anisoLevel", anisoLevel);
    }

    private void getTextureInfo() {
        this.constructParams = new JSONObject(JSONObject.Type.ARRAY);
        this.propertyParams = new JSONObject(JSONObject.Type.ARRAY);

        // 检查texture是否为空
        if (texture == null) {
            FileUtil.setStatuse(false);
            Debug.LogError(LOGHEAD + "Texture is null, cannot export");
            initDefaultTextureInfo();
            return;
        }

        string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
        TextureImporter import = AssetImporter.GetAtPath(path) as TextureImporter;
        if (import == null) {
            if (m_isBuiltinTexture) {
                initDefaultTextureInfo();
                return;
            }
            FileUtil.setStatuse(false);
            Debug.LogError(LOGHEAD + path + " can't export   You should check the texture file format");
            // 使用默认值初始化，避免后续空引用
            initDefaultTextureInfo();
            return;
        } else {
            // 保存原始导入设置，SaveFile 结束后还原，确保不永久修改 Unity 资源
            m_importerPath    = path;
            m_origTextureType = import.textureType;
            m_origIsReadable  = import.isReadable;

            bool needReimport = false;

            // 只有非 Sprite 类型才改为 Default。
            // Sprite 类型若改为 Default 会在 ImportAsset 时销毁所有 sprite 子资产，
            // 导致场景里所有 SpriteRenderer.sprite 引用变成 null。
            if (import.textureType != TextureImporterType.Sprite &&
                import.textureType != TextureImporterType.Default) {
                import.textureType = TextureImporterType.Default;
                needReimport = true;
            }
            if (!import.isReadable) {
                import.isReadable = true;
                needReimport = true;
            }
            if (needReimport) {
                m_importSettingsModified = true;
                AssetDatabase.ImportAsset(path);
            }

            // ── 精灵纹理快速路径 ──────────────────────────────────────────────
            // 2D 精灵纹理在 Laya 中只需要 { "textureType": 2 }，不需要 3D 贴图的
            // constructParams / propertyParams / platformDefault 等参数。
            if (m_isSpriteTexture && !m_forceReadable) {
                JSONObject spriteImporter = new JSONObject(JSONObject.Type.OBJECT);
                spriteImporter.AddField("textureType", 2);
                this.setImporterMetadata(spriteImporter);
                // constructParams / propertyParams 保持空数组（已在方法开头初始化），
                // 精灵纹理不会被材质系统调用 jsonObject()，无需填充。
                return;
            }
        }

        var sRGB = true;
        if (this.isNormal || import.textureType == TextureImporterType.NormalMap){
            sRGB = false;
        }

        var mipmapFilter = 0;
        if (import.mipmapEnabled) {
            switch (import.mipmapFilter) {
                case TextureImporterMipFilter.KaiserFilter:
                    mipmapFilter = 2;
                    break;
                case TextureImporterMipFilter.BoxFilter:
                    mipmapFilter = 0;
                    break;
                default:
                    mipmapFilter = 1;
                    break;
            }
        }

        int anisoLevel = import.anisoLevel;

        GraphicsFormat format = texture.graphicsFormat;
        this.hasAlphaChannel = GraphicsFormatUtility.HasAlphaChannel(format);
        if (import.alphaSource == TextureImporterAlphaSource.None) {
            this.hasAlphaChannel = false;
        }
        
        // 始终导出为非压缩格式，避免 Laya 端出现压缩纹理兼容问题
        this.importFormat = this.hasAlphaChannel
            ? LayaTextureImportFormat.R8G8B8A8
            : LayaTextureImportFormat.R8G8B8;

        WrapMode wrapMode = WrapMode.Clamp;
        switch (texture.wrapMode) {
            case TextureWrapMode.Repeat:
                wrapMode = WrapMode.Repeat;
                break;
            case TextureWrapMode.Mirror:
                wrapMode = WrapMode.Mirrored;
                break;
            case TextureWrapMode.Clamp:
            default:
                wrapMode = WrapMode.Clamp;
                break;
        }

        if (true) { // import
            JSONObject importData = new JSONObject(JSONObject.Type.OBJECT);
            importData.AddField("sRGB", sRGB);
            importData.AddField("wrapMode", (int)wrapMode);
            importData.AddField("generateMipmap", import.mipmapEnabled);
            if (import.mipmapEnabled) {
                importData.AddField("mipmapFilter", mipmapFilter);
            }
            importData.AddField("anisoLevel", anisoLevel);
            importData.AddField("alphaChannel", hasAlphaChannel);
            
            if (true) { // platformDefault
                JSONObject platformDefault = new JSONObject(JSONObject.Type.OBJECT);
                // format
                platformDefault.AddField("format", (int)this.importFormat);
                // quality
                int quality = -1;
                switch (import.textureCompression) {
                    case TextureImporterCompression.CompressedLQ:
                        quality = 0;
                        break;
                    case TextureImporterCompression.Compressed:
                        quality = 1;
                        break;
                    case TextureImporterCompression.CompressedHQ:
                        quality = 2;
                        break;
                }
                if (quality != -1) {
                    platformDefault.AddField("quality", quality);
                }
                importData.AddField("platformDefault", platformDefault);
            }
            this.setImporterMetadata(importData);
        }

        if (true) { // constructParams
            this.constructParams.Add(texture.width); // width
            this.constructParams.Add(texture.height); // height
            // 格式：始终非压缩，与 importFormat 保持一致
            LayaTextureFormat fmt = hasAlphaChannel
                ? LayaTextureFormat.R8G8B8A8
                : LayaTextureFormat.R8G8B8;
            this.constructParams.Add((int)fmt);
            // mipmap
            this.constructParams.Add(import.mipmapEnabled);

            // canRead
            if (!m_forceReadable && (import.textureType == TextureImporterType.NormalMap || import.isReadable == false || import.textureCompression != TextureImporterCompression.Uncompressed)) {
                this.constructParams.Add(false);
            } else {
                this.constructParams.Add(true);
            }

            // sRGB
            this.constructParams.Add(sRGB);
        }

        if (true) { // propertyParams
            // filterMode
            var filterMode = 1;
            switch (texture.filterMode) {
                case FilterMode.Point:
                    filterMode = 0;
                    break;
                case FilterMode.Trilinear:
                    filterMode = 2;
                    break;
                case FilterMode.Bilinear:
                default:
                    filterMode = 1;
                    break;
            }
            this.propertyParams.AddField("filterMode", filterMode);
            // wrapModeU
            this.propertyParams.AddField("wrapModeU", (int)wrapMode);
            // wrapModeV
            this.propertyParams.AddField("wrapModeV", (int)wrapMode);
            // anisoLevel
            this.propertyParams.AddField("anisoLevel", anisoLevel);
        }
    }

    override protected string getOutFilePath(string origpath) {
        if (string.IsNullOrEmpty(origpath))
        {
            return "default_texture";
        }
        string ext = Path.GetExtension(origpath).ToLower();
        int lastDotIndex = origpath.LastIndexOf(".");
        // 修复：使用 >= 0 来正确处理以点开头的文件名，并确保 lastDotIndex 有效
        string savePath = lastDotIndex >= 0 ? origpath.Substring(0, lastDotIndex) : origpath;
        this.rgbmEncoding = ext == ".hdr" || ext == ".exr";
        if (this.rgbmEncoding) {
            savePath += ".hdr";
        } else if (this.hasAlphaChannel) {
            savePath += ".png";
        } else {
            savePath += ".jpg";
        }
        return savePath;
    }

    public JSONObject jsonObject(string name)
    {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("name", name);
        data.AddField("constructParams", this.constructParams);
        data.AddField("propertyParams", this.propertyParams);
        data.AddField("path", "res://" + this.uuid);
        return data;
    }

    /// <summary>
    /// Shape Texture sampling calls Texture2D.getPixels() at runtime. Upgrade a
    /// cached texture export to a readable 3D Texture2D without permanently
    /// changing the Unity source import settings.
    /// </summary>
    public void EnsureReadable()
    {
        m_forceReadable = true;

        // A texture may have entered the shared cache through Sprite export first.
        // Rebuild its metadata as a regular Texture2D, while retaining the original
        // importer snapshot that SaveFile must restore after pixel extraction.
        if (m_isSpriteTexture)
        {
            string importerPath = m_importerPath;
            TextureImporterType originalTextureType = m_origTextureType;
            bool originalIsReadable = m_origIsReadable;
            bool importSettingsModified = m_importSettingsModified;

            m_isSpriteTexture = false;
            getTextureInfo();

            m_importerPath = importerPath;
            m_origTextureType = originalTextureType;
            m_origIsReadable = originalIsReadable;
            m_importSettingsModified = importSettingsModified || m_importSettingsModified;
        }

        if (constructParams != null && constructParams.Count > 4)
        {
            constructParams[4] = JSONObject.Create(true);
        }
    }

    private byte[] float2rgbe(float r, float g, float b)
    {
        byte[] res = new byte[4] { 0, 0, 0, 0 };
        int e = 0;
        float v = Mathf.Max(r, g, b);
        if (!(v < 1e-32))
        {
            double result = frexp(v, out e) * 256.0 / v;
            res[0] = (byte)(r * result);
            res[1] = (byte)(g * result);
            res[2] = (byte)(b * result);
            res[3] = (byte)(e + 128);
        }
        return res;
    }

    private void exportHDRFile(string filePath, Color[] colors, int height, int width)
    {
        // export HDR Color to .hdr file
        using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
        {
            //read file header
            string str = "#?RADIANCE\n";
            writer.Write(str.ToCharArray());
            str = "#?Laya HDR Writer 0.0.1\n";
            writer.Write(str.ToCharArray());
            str = "FORMAT=32-bit_rle_rgbe\n";
            writer.Write(str.ToCharArray());
            str = "\n";
            writer.Write(str.ToCharArray());
            str = "-Y " + height + " +X " + width + "\n";
            writer.Write(str.ToCharArray());
            var pixleCount = width * height;
            // if (!(width < 8 || width > 32768))
            {
                for (int i = height - 1; i >= 0; --i)
                {
                    for (int j = 0; j < width; ++j)
                    {
                        float fR = colors[i * width + j].r;
                        float fG = colors[i * width + j].g;
                        float fB = colors[i * width + j].b;

                        byte[] rgbe = float2rgbe(fR, fG, fB);
                        writer.Write(rgbe[0]);
                        writer.Write(rgbe[1]);
                        writer.Write(rgbe[2]);
                        writer.Write(rgbe[3]);
                    }
                }
            }
        }
    }

    private void gammaColorsToLinear(Color[] gColor) {
        for (var i = 0; i < gColor.Length; ++i) {
            gColor[i].r = Mathf.GammaToLinearSpace(gColor[i].r);
            gColor[i].g = Mathf.GammaToLinearSpace(gColor[i].g);
            gColor[i].b = Mathf.GammaToLinearSpace(gColor[i].b);
        }
    }

    /// <summary>
    /// 读取纹理像素。普通项目纹理由 TextureImporter 保证可读；Unity 内置纹理
    /// 没有 TextureImporter，无法直接读取时通过 RenderTexture 做 GPU 回读。
    /// </summary>
    private Color[] getTexturePixels() {
        try {
            return this.texture.GetPixels(0);
        } catch (UnityException) {
            return getTexturePixelsFromGpu();
        } catch (ArgumentException) {
            return getTexturePixelsFromGpu();
        }
    }

    private Color[] getTexturePixelsFromGpu() {
        RenderTexture temporary = RenderTexture.GetTemporary(
            this.texture.width,
            this.texture.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);
        RenderTexture previous = RenderTexture.active;
        Texture2D readableTexture = new Texture2D(
            this.texture.width,
            this.texture.height,
            TextureFormat.RGBA32,
            false);
        try {
            Graphics.Blit(this.texture, temporary);
            RenderTexture.active = temporary;
            readableTexture.ReadPixels(
                new Rect(0, 0, this.texture.width, this.texture.height),
                0,
                0);
            readableTexture.Apply();
            return readableTexture.GetPixels(0);
        } finally {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            UnityEngine.Object.DestroyImmediate(readableTexture);
        }
    }

    /// <summary>
    /// Unity 的 Default-Particle 是内置资源，不同 Unity/渲染后端对 GetPixels 和
    /// Graphics.Blit 的结果并不一致。直接读取曾产生整张 RGBA(205,205,205,205)
    /// 的灰色方块，因此使用插件内置、已校验的原始 PNG 数据稳定导出。
    /// </summary>
    private void saveDefaultParticleTexture() {
        string templatePath = AssetDatabase.GUIDToAssetPath(DefaultParticleTemplateGuid);
        TextAsset template = string.IsNullOrEmpty(templatePath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);
        if (template == null || template.bytes == null || template.bytes.Length == 0) {
            FileUtil.setStatuse(false);
            throw new FileNotFoundException(
                "LayaAir3D: Built-in Default-Particle export template is missing.",
                templatePath);
        }

        File.WriteAllBytes(this.outPath, template.bytes);
    }

    public override void SaveFile(Dictionary<string, FileData> exportFiles) {
        base.saveMeta();
        if (this.m_isDefaultParticleTexture) {
            this.saveDefaultParticleTexture();
            return;
        }

        string filePath = this.filePath;
        Color[] pixels = this.getTexturePixels();
        if (this.rgbmEncoding) {
            if (QualitySettings.activeColorSpace == ColorSpace.Gamma) {
                ExportLogger.Log("Current color space is gamma.. Your Img will change to Linear Space");
                gammaColorsToLinear(pixels);
            }
            this.exportHDRFile(this.outPath, pixels, this.texture.height, this.texture.width);
        } else if (this.hasAlphaChannel) {
            Texture2D uncompressedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            uncompressedTexture.SetPixels(pixels); // 将压缩纹理的像素复制到未压缩纹理
            uncompressedTexture.Apply();
            byte[] bytes = uncompressedTexture.EncodeToPNG();
            File.WriteAllBytes(this.outPath, bytes);
            UnityEngine.Object.DestroyImmediate(uncompressedTexture);
        } else {
            Texture2D uncompressedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            uncompressedTexture.SetPixels(pixels); // 将压缩纹理的像素复制到未压缩纹理
            uncompressedTexture.Apply();
            byte[] bytes = uncompressedTexture.EncodeToJPG();
            File.WriteAllBytes(this.outPath, bytes);
            UnityEngine.Object.DestroyImmediate(uncompressedTexture);
        }

        // 像素读取完成后，还原导出前修改过的导入设置，不永久污染 Unity 项目资源
        if (m_importSettingsModified && m_importerPath != null) {
            TextureImporter importerToRestore = AssetImporter.GetAtPath(m_importerPath) as TextureImporter;
            if (importerToRestore != null) {
                importerToRestore.textureType = m_origTextureType;
                importerToRestore.isReadable  = m_origIsReadable;
                AssetDatabase.ImportAsset(m_importerPath, ImportAssetOptions.ForceUpdate);
            }
            m_importSettingsModified = false;
        }
    }
}
