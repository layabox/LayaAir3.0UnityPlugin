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

    public TextureFile(string originPath, Texture2D texture, bool isNormal) : base(null) {
        this.texture = texture;
        this.isNormal = isNormal;
        this.updatePath(originPath);
        this.getTextureInfo();
    }

    private void getTextureInfo() {
        this.constructParams = new JSONObject(JSONObject.Type.ARRAY);
        this.propertyParams = new JSONObject(JSONObject.Type.ARRAY);

        string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
        TextureImporter import = AssetImporter.GetAtPath(path) as TextureImporter;
        if (import == null) {
            FileUtil.setStatuse(false);
            Debug.LogError(LOGHEAD + path + " can't export   You should check the texture file format");
        } else {
            import.textureType = TextureImporterType.Default;
            import.isReadable = true;
            AssetDatabase.ImportAsset(path);
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
        
        this.importFormat = LayaTextureImportFormat.R8G8B8A8;
        if (GraphicsFormatUtility.IsCompressedFormat(format)) {
            this.importFormat = LayaTextureImportFormat.COMPRESSED;
        } else if (!this.hasAlphaChannel) {
            this.importFormat = LayaTextureImportFormat.R8G8B8;
        }

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
            this.m_metaData.AddField("importer", importData);
        }

        if (true) { // constructParams
            this.constructParams.Add(texture.width); // width
            this.constructParams.Add(texture.height); // height
            // format
            LayaTextureFormat fmt = LayaTextureFormat.R8G8B8A8;
            if (importFormat == LayaTextureImportFormat.COMPRESSED) {
                fmt = LayaTextureFormat.COMPRESSED; // DX5
            } else if (!hasAlphaChannel) {
                fmt = LayaTextureFormat.R8G8B8; // RGB
            }
            this.constructParams.Add((int)fmt);
            // mipmap
            this.constructParams.Add(import.mipmapEnabled);

            // canRead
            if (import.textureType == TextureImporterType.NormalMap || import.isReadable == false || import.textureCompression != TextureImporterCompression.Uncompressed) {
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

    public override void SaveFile(Dictionary<string, FileData> exportFiles) {
        base.saveMeta();
        string filePath = this.filePath;
        if (this.rgbmEncoding) {
            Color[] pixels = this.texture.GetPixels(0);
            if (QualitySettings.activeColorSpace == ColorSpace.Gamma) {
                Debug.Log("Current color space is gamma.. Your Img will change to Linear Space");
                gammaColorsToLinear(pixels);
            }
            this.exportHDRFile(this.outPath, pixels, this.texture.height, this.texture.width);
        } else if (this.hasAlphaChannel) {
            Texture2D uncompressedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            uncompressedTexture.SetPixels(texture.GetPixels()); // 将压缩纹理的像素复制到未压缩纹理
            uncompressedTexture.Apply();
            byte[] bytes = uncompressedTexture.EncodeToPNG();
            File.WriteAllBytes(this.outPath, bytes);
        } else {
            Texture2D uncompressedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            uncompressedTexture.SetPixels(texture.GetPixels()); // 将压缩纹理的像素复制到未压缩纹理
            uncompressedTexture.Apply();
            byte[] bytes = uncompressedTexture.EncodeToJPG();
            File.WriteAllBytes(this.outPath, bytes);
        }
    }
}
