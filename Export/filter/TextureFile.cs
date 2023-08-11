using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using FileUtil = Util.FileUtil;


// [DllImport("OpenEXRPlugin")]
// private static extern void EncodeHDR(Texture2D texture, string fileName);

internal class TextureFile : FileData
{
    [DllImport("msvcrt.dll")]
    public static extern double frexp(double val, out int eptr);

    public static int JPGQuality = 75;
    public static List<string> ConvertOriginalTextureTypeList;

    public static void init()
    {

        ConvertOriginalTextureTypeList = new List<string>();

        ConvertOriginalTextureTypeList.Add(".jpeg");
        ConvertOriginalTextureTypeList.Add(".JPEG");

        ConvertOriginalTextureTypeList.Add(".bmp");
        ConvertOriginalTextureTypeList.Add(".BMP");

        ConvertOriginalTextureTypeList.Add(".png");
        ConvertOriginalTextureTypeList.Add(".PNG");

        ConvertOriginalTextureTypeList.Add(".jpg");
        ConvertOriginalTextureTypeList.Add(".JPG");

        ConvertOriginalTextureTypeList.Add(".hdr");
        ConvertOriginalTextureTypeList.Add(".HDR");
    }
    public static string LOGHEAD = "LayaAir3D UnityPlugin: ";
    private Texture2D _texture;
    private JSONObject _constructParams;
    private JSONObject _propertyParams;
    private int _format;
    private bool _rgbmEncoding;
    private bool _isNormal;
    private bool _isCopy;
    public TextureFile(string originPath, Texture2D texture, bool isNormal) : base(null)
    {
        this._texture = texture;
        this._isNormal = isNormal;
        this.updatePath(originPath);
        this.getTextureInfo();
    }

    private void getTextureInfo()
    {
        Texture2D texture = this._texture;
        this._constructParams = new JSONObject(JSONObject.Type.ARRAY);
        this._propertyParams = new JSONObject(JSONObject.Type.ARRAY);

        string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
        TextureImporter import = AssetImporter.GetAtPath(path) as TextureImporter;
        if (import == null)
        {
            FileUtil.setStatuse(false);
            Debug.LogError(LOGHEAD + path + " can't export   You should check the texture file format");
        }
        else
        {
            import.textureType = TextureImporterType.Default;
            import.isReadable = true;
            AssetDatabase.ImportAsset(path);
        }
        JSONObject importData = new JSONObject(JSONObject.Type.OBJECT);
        if (this._isNormal || import.textureType == TextureImporterType.NormalMap)
        {
            importData.AddField("sRGB", false);
        }
        else if (import.sRGBTexture)
        {
            importData.AddField("sRGB", true);
        }
        if (this._format == 3)
        {
            importData.AddField("npot", 1);
            JSONObject platformPC = new JSONObject(JSONObject.Type.OBJECT);
            platformPC.AddField("format", "BC1");
            platformPC.AddField("quality", 2);
            importData.AddField("platformPC", platformPC);
        }
        else
        {
            importData.AddField("npot", 1);
            JSONObject platformPC = new JSONObject(JSONObject.Type.OBJECT);
            platformPC.AddField("format", "BC3");
            platformPC.AddField("quality", 2);
            importData.AddField("platformPC", platformPC);
        }
        /* if (import.generateMipsInLinearSpace)
         {*/
        importData.AddField("generateMipmap", true);
        importData.AddField("mipmapFilter", 1);

        int anisoLevel = 1;
        if (import != null)
        {
            anisoLevel = texture.anisoLevel;
        }
        anisoLevel = Math.Min(anisoLevel * 4, 32);
        importData.AddField("anisoLevel", anisoLevel);
        /*}*/
        if (import.alphaSource == TextureImporterAlphaSource.FromInput)
        {
            importData.AddField("alphaChannel", true);
        }
        this.m_metaData.AddField("importer", importData);
        this._constructParams.Add(texture.width);
        this._constructParams.Add(texture.height);

        this._constructParams.Add(this._format);
        this._constructParams.Add(import.mipmapEnabled);
        if (import.textureType == TextureImporterType.NormalMap || import.isReadable == false || import.textureCompression != TextureImporterCompression.Uncompressed)
        {
            this._constructParams.Add(false);
        }
        else
        {
            this._constructParams.Add(true);
        }

        if (texture.filterMode == FilterMode.Point)
        {
            this._propertyParams.AddField("filterMode", 0);
        }
        else if (texture.filterMode == FilterMode.Bilinear)
        {
            this._propertyParams.AddField("filterMode", 1);
        }
        else if (texture.filterMode == FilterMode.Trilinear)
        {
            this._propertyParams.AddField("filterMode", 2);
        }
        else
        {
            this._propertyParams.AddField("filterMode", 1);
        }

        //wrapMode
        if (texture.wrapMode == TextureWrapMode.Repeat)
        {
            this._propertyParams.AddField("wrapModeU", 0);
            this._propertyParams.AddField("wrapModeV", 0);
        }
        else if (texture.wrapMode == TextureWrapMode.Clamp)
        {
            this._propertyParams.AddField("wrapModeU", 1);
            this._propertyParams.AddField("wrapModeV", 1);
        }
        else
        {
            this._propertyParams.AddField("wrapModeU", 0);
            this._propertyParams.AddField("wrapModeV", 0);
        }

        this._propertyParams.AddField("anisoLevel", anisoLevel);
    }

    private int getFormat()
    {
        switch (this._texture.format)
        {
            case TextureFormat.DXT1:
                return 3;
            case TextureFormat.DXT5:
                return 5;
            case TextureFormat.RGB24:
                return 0;
            default:
                return 1;
        }
    }
    override protected string getOutFilePath(string origpath)
    {
        this._format = this.getFormat();
        string ext = Path.GetExtension(origpath).ToLower();
        int convertIndex = ConvertOriginalTextureTypeList.IndexOf(ext);
        this._isCopy = convertIndex != -1;
        string savePath = origpath.Substring(0, origpath.LastIndexOf("."));
        this._rgbmEncoding = ext == ".hdr" || ext == ".exr";
        if (this._rgbmEncoding)
        {
            savePath += convertIndex == -1 ? ".hdr" : ext;
        }
        else if (this._format == 0)
        {
            savePath += convertIndex == -1 ? ".jpg" : ext;
        }
        else
        {
            savePath += convertIndex == -1 ? ".png" : ext;
        }
        return savePath;
    }

    public JSONObject jsonObject(string name)
    {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("name", name);
        data.AddField("constructParams", this._constructParams);
        data.AddField("propertyParams", this._propertyParams);
        data.AddField("path", "res://" + this.uuid);
        return data;
    }

    public byte[] float2rgbe(float r, float g, float b)
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

    public void writeRGBE_rle(BinaryWriter bw, byte[] data, int numbytes)
    {
        const int MINRUNLENGTH = 4;
        int cur, beg_run, run_count, old_run_count, nonrun_count;
        byte[] buf = new byte[2] { 0, 0 };
        cur = 0;
        while (cur < numbytes)
        {
            beg_run = cur;
            run_count = old_run_count = 0;
            while ((run_count < MINRUNLENGTH) && (beg_run < numbytes))
            {
                beg_run += run_count;
                old_run_count = run_count;
                run_count = 1;
                while ((beg_run + run_count < numbytes) && (run_count < 127) && (data[beg_run] == data[beg_run + run_count]))
                    run_count++;
            }
            if ((old_run_count > 1) && (old_run_count == beg_run - cur))
            {
                buf[0] = (byte)(old_run_count + 128);   /*write short run*/
                buf[1] = data[cur];

                // byte.writeArrayBuffer(buf);
                bw.Write(buf);
                cur = beg_run;
            }
            while (cur < beg_run)
            {
                nonrun_count = beg_run - cur;
                if (nonrun_count > 128)
                    nonrun_count = 128;
                buf[0] = (byte)nonrun_count;
                bw.Write(buf[0]);
                byte[] node = new byte[nonrun_count];
                Buffer.BlockCopy(data, cur, node, 0, nonrun_count);
                bw.Write(node);
                cur += nonrun_count;
            }
            if (run_count >= MINRUNLENGTH)
            {
                buf[0] = (byte)(128 + run_count);
                buf[1] = data[beg_run];
                bw.Write(buf);
                cur += run_count;
            }
        }
    }

    public void exportHDRFile(string filePath, Color[] colors, int height, int width)
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

    public void gammaColorsToLinear(Color[] gColor)
    {
        for (var i = 0; i < gColor.Length; ++i)
        {
            gColor[i].r = Mathf.GammaToLinearSpace(gColor[i].r);
            gColor[i].g = Mathf.GammaToLinearSpace(gColor[i].g);
            gColor[i].b = Mathf.GammaToLinearSpace(gColor[i].b);
        }
    }
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        string filePath = this.filePath;
        if (this._isCopy)
        {
            File.Copy(filePath, this.outPath, true);
        }
        else{
            if (this._rgbmEncoding)
            {
                Color[] pixels = this._texture.GetPixels(0);
                if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                {
                    Debug.Log("Current color space is gamma.. Your Img will change to Linear Space");
                    gammaColorsToLinear(pixels);
                }
                this.exportHDRFile(this.outPath, pixels, this._texture.height, this._texture.width);
            }
            else if (this._format == 0)
            {
                byte[] bytes = this._texture.EncodeToJPG();
                File.WriteAllBytes(this.outPath, bytes);
            }
            else if (this._format == 1)
            {
                byte[] bytes = this._texture.EncodeToPNG();
                File.WriteAllBytes(this.outPath, bytes);
            }
            else if (this._format == 3)
            {
                string path = this.filePath;
                TextureImporter import = AssetImporter.GetAtPath(path) as TextureImporter;
                if (import == null)
                {
                    Debug.LogError(LOGHEAD + path + " can't export   You should check the texture file format");
                    return;
                }
                else
                {
                    import.textureType = TextureImporterType.Default;
                    import.isReadable = true;
                    import.textureCompression = TextureImporterCompression.Uncompressed;
                    TextureImporterPlatformSettings pc = import.GetPlatformTextureSettings("Standalone");
                    pc.overridden = true;
                    pc.format = TextureImporterFormat.RGBA32;
                    import.SetPlatformTextureSettings(pc);
                    AssetDatabase.ImportAsset(path);

                    byte[] bytes = this._texture.EncodeToPNG();
                    File.WriteAllBytes(this.outPath, bytes);
                }
            }
            else
            {
                Debug.LogError("Texture format UnSupport export!!!");
            }
        }
    }
}