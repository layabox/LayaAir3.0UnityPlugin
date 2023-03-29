using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


public enum DefindsFrom
{
    floatValue = 0,
    keyWorld = 1,
    HasProps = 2,
    TextureValue = 3
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

    public static int GetCull(Material material)
    {
        if (material.HasProperty("_Cull"))
        {
            return material.GetInt("_Cull");
        }
        else
        {
            return 2;
        }
    }

    public static int GetBlend(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHABLEND_ON"))
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    public static int GetSrcBlend(Material material)
    {
        if (material.HasProperty("_SrcBlend"))
        {
            switch (material.GetInt("_SrcBlend"))
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 4;
                case 3:
                    return 2;
                case 4:
                    return 5;
                case 5:
                    return 6;
                case 6:
                    return 3;
                case 7:
                    return 8;
                case 8:
                    return 9;
                case 9:
                    return 4;
                case 10:
                    return 7;
                default:
                    return 1;
            }
        }
        else
        {
            return 1;
        }
    }
    public static int GetDstBlend(Material material)
    {
        if (material.HasProperty("_DstBlend"))
        {
            switch (material.GetInt("_DstBlend"))
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 4;
                case 3:
                    return 2;
                case 4:
                    return 5;
                case 5:
                    return 6;
                case 6:
                    return 3;
                case 7:
                    return 8;
                case 8:
                    return 9;
                case 9:
                    return 4;
                case 10:
                    return 7;
                default:
                    return 0;
            }
        }
        else
        {
            return 0;
        }
    }

    public static bool GetZWrite(Material material)
    {
        if (material.HasProperty("_ZWrite"))
        {
            if (material.GetInt("_ZWrite") == 1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return true;
        }
    }

    public static int GetZTest(Material material)
    {
        if (material.HasProperty("_ZTest"))
        {
            switch (material.GetInt("_ZTest"))
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
            return 3;
        }
    }

    public static bool GetVerterColor(Material material)
    {
        return material.GetInt("_IsVertexColor") == 0 ? false : true;
    }

    public static bool GetAlphaTest(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHATEST_ON"))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static float GetAlphaTestValue(Material material)
    {
        if (material.HasProperty("_Cutoff"))
        {
            return material.GetFloat("_Cutoff");
        }
        else
        {
            return 0.5f;
        }
    }

    public static int GetRenderModule(Material material)
    {
        string result = material.GetTag("RenderType", true);
        if (result == "Opaque")
        {
            return 0;
        }
        else if (result == "Cutout")
        {
            return 1;
        }
        else if (result == "Transparent")
        {
            return 2;
        }
        else if (result == "Fade")
        {
            return 5;
        }
        else
        {
            return 0;
        }
    }
}
internal class MetarialUitls 
{
    public static Dictionary<string, PropDatasConfig> MaterialPropsConfigs;

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
        }


    }

    public static PropDatasConfig getMetarialConfig(string shaderName)
    {
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
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
    public static void WriteMetarial(Material material, JsonFile file, Dictionary<string, FileData> exportFiles)
    {
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        file.jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        file.jsonData.AddField("props", props);
        props.AddField("type", propsData.materalName);
        props.AddField("s_Cull", PropDatasConfig.GetCull(material));
        props.AddField("s_Blend", PropDatasConfig.GetBlend(material));
        props.AddField("s_BlendSrc", PropDatasConfig.GetSrcBlend(material));
        props.AddField("s_BlendDst", PropDatasConfig.GetDstBlend(material));
        props.AddField("alphaTest", PropDatasConfig.GetAlphaTest(material));
        props.AddField("alphaTestValue", PropDatasConfig.GetAlphaTestValue(material));
        props.AddField("renderQueue", material.renderQueue);
        JSONObject texture = new JSONObject(JSONObject.Type.ARRAY);
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = GameObjectUitls.writePicture(material.GetTexture(plist.Key), exportFiles, tConfig.isNormal);
                texture.Add(textureFile.jsonObject(tConfig.keyName));
                file.AddRegistList(textureFile.filePath);
            }
        }
        props.AddField("textures", texture);
        props.AddField("materialRenderMode", PropDatasConfig.GetRenderModule(material));

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

        foreach (var tList in propsData.tillOffsetLists)
        {
            JSONObject tillOffData = new JSONObject(JSONObject.Type.ARRAY);
            Vector4 tiling = material.GetVector(tList.Key);
            tillOffData.Add(tiling.x);
            tillOffData.Add(tiling.y);
            tillOffData.Add(tiling.z);
            tillOffData.Add(tiling.w);
            props.AddField(tList.Value, tillOffData);
        }

        foreach (var flist in propsData.floatLists)
        {
            string uName = flist.Key;
            string layaName = flist.Value.keyName;
            if (flist.Value.rule != null)
            {
                ConditionConfig ruleConfig;
                if (propsData.rules.TryGetValue(flist.Value.rule, out ruleConfig))
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

        JSONObject definds = new JSONObject(JSONObject.Type.ARRAY);
        List<string> defindLists = new List<string>();
        foreach (var dlist in propsData.defindsLists)
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

    public static void WriteSkyMetarial(Material material, JsonFile file, Dictionary<string, FileData> exportFiles)
    {
        string cubeMapPath = file.filePath.Split('.')[0] + ".cubemap";
        JsonFile cubeMapData = new JsonFile(cubeMapPath, new JSONObject(JSONObject.Type.OBJECT));
        exportFiles.Add(cubeMapData.filePath, cubeMapData);
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = GameObjectUitls.writePicture(material.GetTexture(plist.Key), exportFiles, tConfig.isNormal);
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

        JSONObject materialData = file.jsonData;
        materialData.AddField("version", "LAYAMATERIAL:04");

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
        props.AddField("s_Cull", PropDatasConfig.GetCull(material));
        props.AddField("s_Blend", PropDatasConfig.GetBlend(material));
        props.AddField("s_BlendSrc", PropDatasConfig.GetSrcBlend(material));
        props.AddField("s_BlendDst", PropDatasConfig.GetDstBlend(material));
        props.AddField("alphaTest", PropDatasConfig.GetAlphaTest(material));
        props.AddField("alphaTestValue", PropDatasConfig.GetAlphaTestValue(material));
        props.AddField("renderQueue", material.renderQueue);
        props.AddField("materialRenderMode", 0);
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
        materialData.AddField("props", props);
    }

}
