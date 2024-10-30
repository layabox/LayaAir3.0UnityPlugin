using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Util;


public enum DefindsFrom
{
    floatValue = 0,
    keyWorld = 1,
    HasProps = 2,
    TextureValue = 3
}

public enum RenderMode
{
    Opaque,
    Cutout,
    Fade,   // Old school alpha-blending mode, fresnel does not affect amount of transparency
    Transparent, // Physically plausible transparency mode, implemented as alpha pre-multiply
    Additive,
    Subtractive,
    Modulate
}

//Defind 配置
public class DefindsValue
{
    private string _keyName;
    private DefindsFrom _from;
    private float _value;
    public DefindsValue(string keyName, DefindsFrom from, float value)
    {
        this._keyName = keyName;
        this._from = from;
        this._value = value;
    }
    public string keyName
    {
        get
        {
            return this._keyName;
        }
    }

    public DefindsFrom from
    {
        get
        {
            return this._from;
        }
    }

    public float data
    {
        get
        {
            return this._value;
        }
    }
}

//浮点数配置
public class FloatConfig
{
    public string keyName;
    public bool isGamma;
    public string rule;

}

//贴图配置
public class TextureConfig
{
    public string keyName;
    public bool isNormal;
    public TextureConfig(string keyname, bool isNomal)
    {
        this.keyName = keyname;
        this.isNormal = isNomal;
    }
}

public enum BlendFactor
{
    /** (0, 0, 0, 0)*/
    Zero,
    /** (1, 1, 1, 1)*/
    One,
    /** (Rs, Gs, Bs, As) */
    SourceColor,
    /** (1 - Rs, 1 - Gs, 1 - Bs, 1 - As)*/
    OneMinusSourceColor,
    /** (Rd, Gd, Bd, Ad)*/
    DestinationColor,
    /** (1 - Rd, 1 - Gd, 1 - Bd, 1 - Ad)*/
    OneMinusDestinationColor,
    /** (As, As, As, As)*/
    SourceAlpha,
    /** (1 - As, 1 - As, 1 - As, 1 - As)*/
    OneMinusSourceAlpha,
    /** (Ad, Ad, Ad, Ad)*/
    DestinationAlpha,
    /** (1 - Ad, 1 - Ad, 1 - Ad, 1 - Ad)*/
    OneMinusDestinationAlpha,
    /** (min(As, 1 - Ad), min(As, 1 - Ad), min(As, 1 - Ad), 10)*/
    SourceAlphaSaturate,
    /** (Rc, Gc, Bc, Ac)*/
    BlendColor,
    /** (1 - Rc, 1 - Gc, 1 - Bc, 1 - Ac)*/
    OneMinusBlendColor
}
public class ConditionConfig
{
    public object data; //判断参数
    public string target; //unity 属性 or laya 属性
    public string targetName; // 属性名
    public string ruleType;//判断属性类型
    public string ruleKeyName; //判断属性名
}

public class PropDatasConfig
{
    private Dictionary<string, ConditionConfig> _rules;
    private Dictionary<string, TextureConfig> _pictureList;
    private Dictionary<string, FloatConfig> _floatLists;
    private Dictionary<string, string> _colorLists;
    private Dictionary<string, string> _colorHdrLists;
    private Dictionary<string, string> _tillOffsetLists;
    private Dictionary<string, DefindsValue> _defindsLists;
    private string _materName;
    public string blendSrc;
    public string blendDst;
    public string cull;
    public string cutoff;
    public string zTest;
    public string zWrite;
    public PropDatasConfig(string lmaterName)
    {
        this._materName = lmaterName;
        this._rules = new Dictionary<string, ConditionConfig>();
        this._pictureList = new Dictionary<string, TextureConfig>();
        this._floatLists = new Dictionary<string, FloatConfig>();
        this._colorLists = new Dictionary<string, string>();
        this._colorHdrLists = new Dictionary<string, string>();
        this._tillOffsetLists = new Dictionary<string, string>();
        this._defindsLists = new Dictionary<string, DefindsValue>();
        this.blendSrc = "_SrcBlend";
        this.blendDst = "_DstBlend";
        this.cull = "_Cull";
        this.cutoff = "_Cutoff";
        this.zTest = "_ZTest";
        this.zWrite = "_ZWrite";
    }

    public void addTextureProps(string uprops, string lprops, string definde = null, bool isnormal = false)
    {
        this._pictureList.Add(uprops, new TextureConfig(lprops, isnormal));
        if (definde != null)
        {
            this.addDefineds(uprops, definde, DefindsFrom.TextureValue, 0.0f);
        }
    }
    public void addFloatProps(string uprops, string lprops, bool isgamma = false, string rule = null)
    {
        FloatConfig floatdata = new FloatConfig();
        floatdata.keyName = lprops;
        floatdata.isGamma = isgamma;
        floatdata.rule = rule;
        this._floatLists.Add(uprops, floatdata);

    }
    public void addColorProps(string uprops, string lprops, string otherName = null)
    {
        this._colorLists.Add(uprops, lprops);
        if (otherName != null)
        {
            this._colorHdrLists.Add(uprops, otherName);
        }
    }

    public void addTillOffsetProps(string uprops, string lprops)
    {
        this._tillOffsetLists.Add(uprops, lprops);
    }

    public void addDefineds(string uprops, string lprops, DefindsFrom from, float value = 0)
    {
        if (this._defindsLists.ContainsKey(uprops))
        {
            return;
        }
        this._defindsLists.Add(uprops, new DefindsValue(lprops, from, value));
    }
    public Dictionary<string, ConditionConfig> rules
    {
        get
        {
            return this._rules;
        }
    }
    public Dictionary<string, FloatConfig> floatLists
    {
        get
        {
            return this._floatLists;
        }
    }

    public Dictionary<string, TextureConfig> pictureList
    {
        get
        {
            return this._pictureList;
        }
    }

    public Dictionary<string, string> colorLists
    {
        get
        {
            return this._colorLists;
        }
    }

    public Dictionary<string, string> colorHdrLists
    {
        get
        {
            return this._colorHdrLists;
        }
    }

    public Dictionary<string, string> tillOffsetLists
    {
        get
        {
            return this._tillOffsetLists;
        }
    }

    public Dictionary<string, DefindsValue> defindsLists
    {
        get
        {
            return this._defindsLists;
        }
    }

    public string materalName
    {
        get
        {
            return this._materName;
        }
    }

    public int GetCull(Material material)
    {
        if (material.HasProperty(this.cull))
        {
            return material.GetInt(this.cull);
        }
        else
        {
            return 2;
        }
    }

    public static int GetBlend(Material material)
    {
        return material.renderQueue >= 3000?1:0;
    }

    public static BlendFactor GetBlendValue(Material material,string prop)
    {
        if (material.HasProperty(prop))
        {
            switch (material.GetInt(prop))
            {
                case (int)BlendMode.Zero:
                    return BlendFactor.Zero;
                case (int)BlendMode.One:
                    return BlendFactor.One;
                case (int)BlendMode.DstColor:
                    return BlendFactor.DestinationColor;
                case (int)BlendMode.SrcColor:
                    return BlendFactor.SourceColor;
                case (int)BlendMode.OneMinusDstColor:
                    return BlendFactor.OneMinusDestinationColor;
                case (int)BlendMode.SrcAlpha:
                    return BlendFactor.SourceAlpha;
                case (int)BlendMode.OneMinusSrcColor:
                    return BlendFactor.OneMinusSourceColor;
                case (int)BlendMode.DstAlpha:
                    return BlendFactor.DestinationAlpha;
                case (int)BlendMode.OneMinusDstAlpha:
                    return BlendFactor.OneMinusDestinationAlpha;
                case (int)BlendMode.SrcAlphaSaturate:
                    return BlendFactor.SourceAlphaSaturate;
                case (int)BlendMode.OneMinusSrcAlpha:
                    return BlendFactor.OneMinusSourceAlpha;
                default:
                    return BlendFactor.Zero;
            }
        }
        else
        {
            return BlendFactor.Zero;
        }
    }

    public bool GetZWrite(Material material, bool defaultValue = true)
    {
        if (material.HasProperty(this.zTest))
        {
            return material.GetInt(this.zTest) == 1;
        }
        else
        {
            return defaultValue;
        }
    }

    public int GetZTest(Material material)
    {
        if (material.HasProperty(this.zTest))
        {
            switch (material.GetInt(this.zTest))
            {
                case 0:
                    return 0;
                case 1:
                    return 0;
                case 2:
                    return 1;
                case 3:
                    return 2;
                case 4:
                    return 3;
                case 5:
                    return 4;
                case 6:
                    return 5;
                case 7:
                    return 6;
                case 8:
                    return 7;
                default:
                    return 0;
            }
        }
        else
        {
            return 1;
        }
    }


    public float GetAlphaTestValue(Material material)
    {
        if (material.HasProperty(this.cutoff))
        {
            return material.GetFloat(this.cutoff);
        }
        else
        {
            return 0.5f;
        }
    }

}
internal class MetarialUitls 
{
    public static Dictionary<string, PropDatasConfig> MaterialPropsConfigs;
    public static string MeterialVersion = "LAYAMATERIAL:04";
    public static void init()
    {
        JSONObject metaDatas = JSONObject.Create(File.ReadAllText(Util.FileUtil.getPluginResUrl("MetarialPropData.json")));
        MaterialPropsConfigs = new Dictionary<string, PropDatasConfig>();
        int count = metaDatas.Count;
        for (int i = 0; i < count; i++)
        {
            string key = metaDatas.keys[i];
            JSONObject mJdata = metaDatas.GetField(key);
            PropDatasConfig propdata = new PropDatasConfig(mJdata.GetField("targeName").str);
            MaterialPropsConfigs.Add(key, propdata);
            JSONObject textures = mJdata["textures"];
            if (textures != null)
            {
                int texureCount = textures.Count;
                for (int tindex = 0; tindex < texureCount; tindex++)
                {
                    JSONObject texture = textures[tindex];
                    string uName = texture.GetField("uName").str;
                    string lName = texture.GetField("layaName").str;
                    string defined = null;
                    bool isNormal = false;
                    if (texture.GetField("defind") != null)
                    {
                        defined = texture.GetField("defind").str;

                    }
                    if (texture.GetField("isNormal") != null)
                    {
                        isNormal = texture.GetField("isNormal").b;

                    }
                    propdata.addTextureProps(uName, lName, defined, isNormal);
                }
            }
            JSONObject colors = mJdata["colors"];
            if (colors != null)
            {
                int colorCount = colors.Count;
                for (int cindex = 0; cindex < colorCount; cindex++)
                {
                    JSONObject color = colors[cindex];
                    string uName = color.GetField("uName").str;
                    string lName = color.GetField("layaName").str;
                    if (color.GetField("hdrName") != null)
                    {
                        propdata.addColorProps(uName, lName, color.GetField("hdrName").str);
                    }
                    else
                    {
                        propdata.addColorProps(uName, lName);
                    }
                }
            }
            JSONObject floatDatas = mJdata["floats"];
            if (floatDatas != null)
            {
                int floatCount = floatDatas.Count;
                for (int floatIndex = 0; floatIndex < floatCount; floatIndex++)
                {
                    JSONObject floatData = floatDatas[floatIndex];
                    string uName = floatData.GetField("uName").str;
                    string lName = floatData.GetField("layaName").str;
                    bool isGama = false;
                    if (floatData.GetField("isgama") != null)
                    {
                        isGama = floatData.GetField("isgama").b;
                    }
                    string rule = null;
                    if (floatData.GetField("rule") != null)
                    {
                        rule = floatData.GetField("rule").str;
                    }

                    propdata.addFloatProps(uName, lName, isGama, rule);
                }
            }
            JSONObject tillOffset = mJdata["tillOffset"];
            if (tillOffset != null)
            {
                int tillOffsetCount = tillOffset.Count;
                for (int tOffsetIndex = 0; tOffsetIndex < tillOffsetCount; tOffsetIndex++)
                {
                    JSONObject tOffsetData = tillOffset[tOffsetIndex];
                    string uName = tOffsetData.GetField("uName").str;
                    string lName = tOffsetData.GetField("layaName").str;
                    propdata.addTillOffsetProps(uName, lName);
                }
            }
            JSONObject definedDatas = mJdata["defineds"];
            if (definedDatas != null)
            {
                int definedCount = definedDatas.Count;
                for (int defindIndex = 0; defindIndex < definedCount; defindIndex++)
                {
                    JSONObject definedData = definedDatas[defindIndex];
                    string uName = definedData.GetField("uName").str;
                    string lName = definedData.GetField("layaName").str;
                    DefindsFrom from = (DefindsFrom)definedData.GetField("from").n;
                    if (definedData.GetField("deflat") != null)
                    {
                        propdata.addDefineds(uName, lName, from, definedData.GetField("deflat").n);
                    }
                    else
                    {
                        propdata.addDefineds(uName, lName, from);
                    }
                }
            }
            JSONObject rules = mJdata["rules"];
            if (rules != null)
            {
                int ruleCount = rules.Count;
                for (var rindex = 0; rindex < ruleCount; rindex++)
                {
                    JSONObject rule = rules[rindex];
                    ConditionConfig ruleConfig = new ConditionConfig();
                    ruleConfig.target = rule.GetField("target").str;
                    ruleConfig.targetName = rule.GetField("targetName").str;
                    ruleConfig.ruleType = rule.GetField("ruleType").str;
                    ruleConfig.ruleKeyName = rule.GetField("ruleKeyName").str;
                    if (ruleConfig.ruleType == "texture")
                    {
                        ruleConfig.data = rule.GetField("data").b;
                    }
                    else
                    {
                        ruleConfig.data = rule.GetField("data").n;
                    }
                    propdata.rules.Add(rule.GetField("name").str, ruleConfig);
                }
            }
            if (mJdata["cull"])
            {
                propdata.cull = mJdata["cull"].str;
            }

            if (mJdata["cutoff"])
            {
                propdata.cutoff = mJdata["cutoff"].str;
            }
            if (mJdata["zWrite"])
            {
                propdata.zWrite = mJdata["zWrite"].str;
            }

            if (mJdata["zTest"])
            {
                propdata.zTest = mJdata["zTest"].str;
            }

            if (mJdata["blendSrc"])
            {
                propdata.blendSrc = mJdata["blendSrc"].str;
            }

            if (mJdata["blendDst"])
            {
                propdata.blendDst = mJdata["blendDst"].str;
            }
        }


    }

    public static PropDatasConfig getMetarialConfig(string shaderName)
    {
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            FileUtil.setStatuse(false);
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return null;
        }
        return MaterialPropsConfigs[shaderName];
    }
    public static bool getMatarialRole(Material material, ConditionConfig rule)
    {
        if (rule.ruleType == "texture")
        {
            return material.GetTexture(rule.ruleKeyName) != null;
        }
        else if (rule.ruleType == "float")
        {
            return material.GetFloat(rule.ruleKeyName) == (float)rule.data;
        }
        else
        {
            return false;
        }
    }

    private static void WiteBlend(Material material, PropDatasConfig configData, ref JSONObject props,ref JSONObject definds)
    {
        int renderQueue = material.renderQueue;
        props.AddField("renderQueue", renderQueue);
        RenderMode blendMode = RenderMode.Opaque;
        if(renderQueue < 2450)
        {
            props.AddField("materialRenderMode", 0);
            props.AddField("s_DepthWrite", configData.GetZWrite(material));
        }
        else if(renderQueue < 3000)
        {
            props.AddField("materialRenderMode", 1);
            props.AddField("alphaTest", true);
            props.AddField("alphaTestValue", configData.GetAlphaTestValue(material));
            props.AddField("s_DepthWrite", configData.GetZWrite(material));
        }
        else
        {
            //props.AddField("s_Blend", PropDatasConfig.GetBlend(material));
            BlendFactor blendsrc = PropDatasConfig.GetBlendValue(material, configData.blendSrc);
            BlendFactor blenddst = PropDatasConfig.GetBlendValue(material, configData.blendDst);
            if(blendsrc == BlendFactor.SourceAlpha && blenddst == BlendFactor.OneMinusSourceAlpha)
            {
                props.AddField("materialRenderMode", 2);
            }else if (blendsrc == BlendFactor.SourceAlpha && blenddst == BlendFactor.One)
            {
                props.AddField("materialRenderMode", 3);
            }
            else
            {
                props.AddField("materialRenderMode", 5);
                props.AddField("s_BlendSrc", (int)blendsrc);
                props.AddField("s_BlendDst", (int)blenddst);
            }
            props.AddField("s_DepthWrite", configData.GetZWrite(material,false));
        }
        props.AddField("s_Cull", configData.GetCull(material));
       
        props.AddField("s_DepthTest", configData.GetZTest(material));
    }
    public static void WriteMetarial(Material material, JSONObject jsonData, ResoureMap resoureMap)
    {
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            FileUtil.setStatuse(false);
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        PropDatasConfig configData = MaterialPropsConfigs[shaderName];
        jsonData.AddField("version", MetarialUitls.MeterialVersion);
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        JSONObject definds = new JSONObject(JSONObject.Type.ARRAY);
       
        JSONObject texture = new JSONObject(JSONObject.Type.ARRAY);
        foreach (var plist in configData.pictureList)
        {
            Texture text1 = material.GetTexture(plist.Key);
            if (text1 != null)
            {
                TextureConfig tConfig = plist.Value;
                FileData textureFile = resoureMap.GetTextureFile(text1, tConfig.isNormal);
                if(textureFile == null)
                {
                    Debug.LogError("资源错误");
                }
                texture.Add(textureFile.jsonObject(tConfig.keyName));
            }
        }
        props.AddField("textures", texture);

       
        jsonData.AddField("props", props);
        props.AddField("type", configData.materalName);
        WiteBlend(material, configData, ref props, ref definds);

        foreach (var cList in configData.colorLists)
        {
            if (!material.HasProperty(cList.Key))
            {
                continue;
            }
            JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
            Color color = material.GetColor(cList.Key);
            if (configData.colorHdrLists.ContainsKey(cList.Key))
            {
                Color colorf;
                float exp;
                GameObjectUitls.DecomposeHdrColor(color, out colorf, out exp);
                colorValue.Add(colorf.r);
                colorValue.Add(colorf.g);
                colorValue.Add(colorf.b);
                colorValue.Add(colorf.a);
                props.AddField(configData.colorHdrLists[cList.Key], exp);
            }
            else
            {
                colorValue.Add(color.r);
                colorValue.Add(color.g);
                colorValue.Add(color.b);
                colorValue.Add(color.a);
            }
            props.AddField(cList.Value, colorValue);
        }

        foreach (var tList in configData.tillOffsetLists)
        {
            JSONObject tillOffData = new JSONObject(JSONObject.Type.ARRAY);
            Vector4 tiling = material.GetVector(tList.Key);
            tillOffData.Add(tiling.x);
            tillOffData.Add(tiling.y);
            tillOffData.Add(tiling.z);
            tillOffData.Add(tiling.w);
            props.AddField(tList.Value, tillOffData);
        }

        foreach (var flist in configData.floatLists)
        {
            string uName = flist.Key;
            string layaName = flist.Value.keyName;
            if (flist.Value.rule != null)
            {
                ConditionConfig ruleConfig;
                if (configData.rules.TryGetValue(flist.Value.rule, out ruleConfig))
                {
                    if (getMatarialRole(material, ruleConfig))
                    {
                        if (ruleConfig.target == "uName")
                        {
                            uName = ruleConfig.targetName;
                        }
                        else
                        {
                            layaName = ruleConfig.targetName;
                        }
                    }

                }
            }
            float data = material.GetFloat(uName);
            if (flist.Value.isGamma)
            {
                data = Mathf.LinearToGammaSpace(data);
            }
            props.AddField(layaName, data);
        }

        
        List<string> defindLists = new List<string>();
        foreach (var dlist in configData.defindsLists)
        {
            if (dlist.Value.from == DefindsFrom.floatValue)
            {
                if (material.GetFloat(dlist.Key) == dlist.Value.data)
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
            else if (dlist.Value.from == DefindsFrom.TextureValue)
            {
                if (material.GetTexture(dlist.Key))
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
            else if (dlist.Value.from == DefindsFrom.keyWorld)
            {
                if (material.IsKeywordEnabled(dlist.Key))
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
        }
        if (defindLists.Contains("NORMALTEXTURE") || defindLists.Contains("DETAILNORMAL") || defindLists.Contains("NORMALMAP"))
        {
            definds.Add("NEEDTBN");
        }
        props.AddField("defines", definds);
    }

    public static void WriteSkyMetarial(Material material, JSONObject jsonData, ResoureMap resoureMap)
    {
        string materialPath = AssetsUtil.GetMaterialPath(material);
        string cubeMapPath = materialPath.Split('.')[0] + ".cubemap";
        JsonFile cubeMapData = new JsonFile(cubeMapPath, new JSONObject(JSONObject.Type.OBJECT));
        JSONObject definds = new JSONObject(JSONObject.Type.ARRAY);
        resoureMap.AddExportFile(cubeMapData);
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            FileUtil.setStatuse(false);
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                FileData textureFile = resoureMap.GetTextureFile(material.GetTexture(plist.Key), tConfig.isNormal);
                cubeMapData.jsonData.AddField(tConfig.keyName, "res://" + textureFile.filePath);
                cubeMapData.AddRegistList(textureFile.filePath);
            }
        }
        cubeMapData.jsonData.AddField("cubemapSize", 512);
        cubeMapData.jsonData.AddField("filterMode", 1);
        cubeMapData.jsonData.AddField("cubemapFileMode", "R8G8B8A8");
        cubeMapData.jsonData.AddField("mipmapCoverageIBL", true);
        cubeMapData.jsonData.AddField("generateMipmap", true);
        cubeMapData.jsonData.AddField("sRGB", true);

        jsonData.AddField("version", MetarialUitls.MeterialVersion);

        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        JSONObject constructParams = new JSONObject(JSONObject.Type.ARRAY);
        constructParams.Add(512);
        constructParams.Add(512);
        constructParams.Add(0);
        constructParams.Add(false);
        constructParams.Add(false);
        constructParams.Add(true);

        JSONObject propertyParams = new JSONObject(JSONObject.Type.OBJECT);
        propertyParams.AddField("filterMode", 1);
        propertyParams.AddField("wrapModeU", 0);
        propertyParams.AddField("wrapModeV", 0);
        propertyParams.AddField("anisoLevel", 4);
        JSONObject texture = new JSONObject(JSONObject.Type.OBJECT);
        texture.AddField("path", "res://" + cubeMapData.uuid);
        texture.AddField("constructParams", constructParams);
        texture.AddField("propertyParams", propertyParams);
        texture.AddField("name", "u_CubeTexture");
        textures.Add(texture);

        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        props.AddField("textures", textures);
        props.AddField("type", propsData.materalName);
        WiteBlend(material, propsData,ref props, ref definds);
        foreach (var cList in propsData.colorLists)
        {
            if (!material.HasProperty(cList.Key))
            {
                continue;
            }
            JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
            Color color = material.GetColor(cList.Key);
            if (propsData.colorHdrLists.ContainsKey(cList.Key))
            {
                Color colorf;
                float exp;
                GameObjectUitls.DecomposeHdrColor(color, out colorf, out exp);
                colorValue.Add(colorf.r);
                colorValue.Add(colorf.g);
                colorValue.Add(colorf.b);
                colorValue.Add(colorf.a);
                props.AddField(propsData.colorHdrLists[cList.Key], exp);
            }
            else
            {
                colorValue.Add(color.r);
                colorValue.Add(color.g);
                colorValue.Add(color.b);
                colorValue.Add(color.a);
            }
            props.AddField(cList.Value, colorValue);
        }
        foreach (var flist in propsData.floatLists)
        {
            props.AddField(flist.Value.keyName, material.GetFloat(flist.Key));

        }
        jsonData.AddField("props", props);
    }

}
