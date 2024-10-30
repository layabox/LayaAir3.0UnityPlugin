using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;


internal class TextureFile : FileData
{
   
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
    private TextureImporter _import;
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
        string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
        TextureImporter import = AssetImporter.GetAtPath(path) as TextureImporter;
        this._import = import;


        this._propertyParams = new JSONObject(JSONObject.Type.ARRAY);

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
       

        this._constructParams = new JSONObject(JSONObject.Type.ARRAY);
        this._constructParams.Add(texture.width);
        this._constructParams.Add(texture.height);
        this._constructParams.Add(this._format);
   
        JSONObject importData = new JSONObject(JSONObject.Type.OBJECT);
      
        int anisoLevel = 1;
        bool issrgb = true;
        bool mipmapEnabled = false;
        bool isReadable = true;
        if (import == null)
        {
            this._constructParams.Add(false);
            this._constructParams.Add(true);
            importData.AddField("sRGB", true);
            importData.AddField("alphaChannel", true);
        }
        else
        {
            anisoLevel = texture.anisoLevel;
            if (this._isNormal || import.textureType == TextureImporterType.NormalMap)
            {
                issrgb = false;
            }

            mipmapEnabled = import.mipmapEnabled;

            if (this._format == 2 || this._format == 4)
            {
                importData.AddField("alphaChannel", import.alphaSource == TextureImporterAlphaSource.FromInput);
            }

            if (import.textureType == TextureImporterType.NormalMap || import.isReadable == false || import.textureCompression != TextureImporterCompression.Uncompressed)
            {
                isReadable = false;
            }

        }

        anisoLevel = Math.Min(anisoLevel * 4, 32);
        importData.AddField("sRGB", issrgb);

        this._propertyParams.AddField("anisoLevel", anisoLevel);
        this._constructParams.Add(mipmapEnabled);
       
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
        importData.AddField("generateMipmap", true);
        importData.AddField("mipmapFilter", 1);
        importData.AddField("anisoLevel", anisoLevel);
        this.m_metaData.AddField("importer", importData);
        this._constructParams.Add(isReadable);
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
        int lastIndex = origpath.LastIndexOf(".");
        string savePath = null;
        if (lastIndex > 0)
        {
            savePath = origpath.Substring(0, lastIndex);
        }
        else
        {
            savePath = origpath;
        }
        this._rgbmEncoding = ext == ".hdr" || ext == ".exr";
        if (this._rgbmEncoding)
        {
            savePath += convertIndex == -1 ? ".hdr" : ext;
        }
        else if (this._format == 0||this._format == 3)
        {
            savePath += convertIndex == -1 ? ".jpg" : ext;
        }
        else
        {
            savePath += convertIndex == -1 ? ".png" : ext;
        }
        return savePath;
    }

    public override JSONObject jsonObject(string name)
    {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("name", name);
        data.AddField("constructParams", this._constructParams);
        data.AddField("propertyParams", this._propertyParams);
        data.AddField("path", "res://" + this.uuid);
        return data;
    }

    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        string filePath = this.filePath;
        if(this._import == null)
        {
            if (this._rgbmEncoding)
            {
                TextureUtils.SaveHDRTexute2D(this._texture, this.outPath);
            }
            else
            {
                TextureUtils.SaveTexture2D(this._texture, this.outPath, true);
            }
           
        }else  if (this._isCopy){
            File.Copy(filePath, this.outPath, true);
        }
        else{
            if (this._rgbmEncoding)
            {
                TextureUtils.SaveHDRTexute2D(this._texture, this.outPath);
            }
            else
            {
                bool isPng = this._format == 1 || this._format == 5;
                TextureUtils.SaveTexture2D(this._texture, this.outPath, isPng);
            }
        }
    }
}