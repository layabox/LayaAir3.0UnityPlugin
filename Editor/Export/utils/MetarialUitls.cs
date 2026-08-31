using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Util;


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
            // Effect/particle shaders without explicit _Cull are typically double-sided (Cull Off)
            string shaderName = material.shader?.name?.ToLower() ?? "";
            if (shaderName.Contains("additive") || shaderName.Contains("particle") || shaderName.Contains("alphablend"))
                return 0; // Off
            // Transparent-queue shaders also default to Cull Off (matches Unity effect shader convention)
            if (material.renderQueue >= 3000)
                return 0; // Off
            return 2;
        }
    }

    public static int GetBlend(Material material)
    {
        // Standard Shader方式：关键字 _ALPHABLEND_ON
        if (material.IsKeywordEnabled("_ALPHABLEND_ON"))
            return 1;

        // 渲染队列优先：透明队列(>=3000)视为开启混合（即使_SrcBlend/_DstBlend=One/Zero）
        // 理由：自定义特效shader可能未正确设置_SrcBlend/_DstBlend，以renderQueue作为可靠指示
        if (material.renderQueue >= 3000)
            return 1;

        // 不透明队列：通过_SrcBlend/_DstBlend精确判断
        if (material.HasProperty("_SrcBlend") && material.HasProperty("_DstBlend"))
        {
            int src = material.GetInt("_SrcBlend");
            int dst = material.GetInt("_DstBlend");
            if (src == 1 && dst == 0)   // One, Zero = 完全不透明
                return 0;
            return 1;
        }

        return 0;
    }

    public static int GetSrcBlend(Material material)
    {
        if (material.HasProperty("_SrcBlend"))
        {
            int unitySrc = material.GetInt("_SrcBlend");
            // 特效shader修正：透明队列(>=3000)但_SrcBlend=One/_DstBlend=Zero时，强制使用SrcAlpha
            // 这种情况通常是Unity材质未正确配置混合因子，以AlphaBlend为默认
            if (unitySrc == 1 && material.renderQueue >= 3000)
            {
                int unitydst = material.HasProperty("_DstBlend") ? material.GetInt("_DstBlend") : -1;
                if (unitydst == 0)
                    return 6; // SrcAlpha（透明材质的正确默认值）
            }
            switch (unitySrc)
            {
                case 0:
                    return 0;  // Zero -> Zero
                case 1:
                    return 1;  // One -> One
                case 2:
                    return 4;  // DstColor -> DstColor
                case 3:
                    return 2;  // SrcColor -> SrcColor
                case 4:
                    return 5;  // OneMinusDstColor -> OneMinusDstColor
                case 5:
                    return 6;  // SrcAlpha -> SrcAlpha
                case 6:
                    return 7;  // OneMinusSrcAlpha -> OneMinusSrcAlpha [FIXED: was 3]
                case 7:
                    return 8;  // DstAlpha -> DstAlpha
                case 8:
                    return 9;  // OneMinusDstAlpha -> OneMinusDstAlpha
                case 9:
                    return 4;  // SrcAlphaSaturate -> DstColor (近似)
                case 10:
                    return 3;  // OneMinusSrcColor -> OneMinusSrcColor [FIXED: was 7]
                default:
                    return 6; // 默认SrcAlpha
            }
        }
        else
        {
            return 6; // 无属性时默认SrcAlpha（透明材质）
        }
    }
    public static int GetDstBlend(Material material)
    {
        if (material.HasProperty("_DstBlend"))
        {
            int unityDst = material.GetInt("_DstBlend");
            // 特效shader修正：透明队列(>=3000)但_SrcBlend=One/_DstBlend=Zero时，强制使用OneMinusSrcAlpha
            // 这种情况通常是Unity材质未正确配置混合因子，以AlphaBlend为默认
            if (unityDst == 0 && material.renderQueue >= 3000)
            {
                int unitySrc = material.HasProperty("_SrcBlend") ? material.GetInt("_SrcBlend") : -1;
                if (unitySrc == 1)
                    return 7; // OneMinusSrcAlpha（透明材质的正确默认值）
            }
            switch (unityDst)
            {
                case 0:
                    return 0;  // Zero -> Zero
                case 1:
                    return 1;  // One -> One
                case 2:
                    return 4;  // DstColor -> DstColor
                case 3:
                    return 2;  // SrcColor -> SrcColor
                case 4:
                    return 5;  // OneMinusDstColor -> OneMinusDstColor
                case 5:
                    return 6;  // SrcAlpha -> SrcAlpha
                case 6:
                    return 7;  // OneMinusSrcAlpha -> OneMinusSrcAlpha [FIXED: was 3]
                case 7:
                    return 8;  // DstAlpha -> DstAlpha
                case 8:
                    return 9;  // OneMinusDstAlpha -> OneMinusDstAlpha
                case 9:
                    return 4;  // SrcAlphaSaturate -> DstColor (近似)
                case 10:
                    return 3;  // OneMinusSrcColor -> OneMinusSrcColor [FIXED: was 7]
                default:
                    return 7; // 默认OneMinusSrcAlpha（透明材质）
            }
        }
        else
        {
            // Fallback based on shader name
            string shaderName = material.shader?.name?.ToLower() ?? "";
            if (shaderName.Contains("additive"))
                return 1; // One (additive blending: Src One Dst One)
            return 7; // 无属性时默认OneMinusSrcAlpha（透明材质）
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
        if (material.shader.name.StartsWith("Laya/") && material.HasProperty("_Mode")) {
            return material.GetInt("_Mode");
        }

        string result = material.GetTag("RenderType", true);
        if (result == "Opaque")
        {
            return 0;
        }
        else if (result == "Cutout" || result == "TransparentCutout")
        {
            return 1;
        }
        else if (result == "Transparent" || result == "Fade")
        {
            // 区分 Additive 和 AlphaBlend：通过混合因子或 shader 名称判断
            return DetectTransparentRenderMode(material);
        }
        else
        {
            // 无 RenderType tag 时，尝试从 shader 名称和混合因子推断
            return DetectRenderModeFromShader(material);
        }
    }

    /// <summary>
    /// 检测透明材质的具体渲染模式：Additive(3) / AlphaBlend(2)
    /// 通过混合因子和 shader 名称综合判断
    /// </summary>
    public static int DetectTransparentRenderMode(Material material)
    {
        // 优先通过 shader 名称判断
        string shaderName = material.shader.name.ToLower();
        if (shaderName.Contains("additive"))
        {
            return 3; // Additive
        }

        // 通过混合因子判断：DstBlend=One 为 Additive，DstBlend=OneMinusSrcAlpha 为 AlphaBlend
        if (material.HasProperty("_DstBlend"))
        {
            int dstBlend = material.GetInt("_DstBlend");
            // Unity BlendMode: 1=One (Additive), 6=OneMinusSrcAlpha (AlphaBlend)
            if (dstBlend == 1)
            {
                return 3; // Additive
            }
        }

        return 2; // 默认 AlphaBlend
    }

    /// <summary>
    /// 无 RenderType tag 时，从 shader 名称和混合因子推断渲染模式
    /// </summary>
    private static int DetectRenderModeFromShader(Material material)
    {
        string shaderName = material.shader.name.ToLower();

        // shader 名称包含 additive
        if (shaderName.Contains("additive"))
        {
            return 3; // Additive
        }

        // shader 名称包含粒子/特效相关关键字，进一步用混合因子判断
        if (shaderName.Contains("particle") || shaderName.Contains("effect") ||
            shaderName.Contains("transparent") || shaderName.Contains("alpha"))
        {
            if (material.HasProperty("_DstBlend"))
            {
                int dstBlend = material.GetInt("_DstBlend");
                if (dstBlend == 1)
                {
                    return 3; // Additive
                }
                return 2; // AlphaBlend
            }
            return 2; // 默认 AlphaBlend
        }

        return 0; // 默认 Opaque
    }
}
internal class MetarialUitls 
{
    public static Dictionary<string, PropDatasConfig> MaterialPropsConfigs;
    public static Dictionary<string, PropDatasConfig> CPUParticleMaterialPropsConfigs;

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

        // Load CPU particle override config
        CPUParticleMaterialPropsConfigs = new Dictionary<string, PropDatasConfig>();
        string cpuConfigPath = Util.FileUtil.getPluginResUrl("MetarialPropData_CPUParticle.json");
        if (File.Exists(cpuConfigPath))
        {
            JSONObject cpuMetaDatas = JSONObject.Create(File.ReadAllText(cpuConfigPath));
            int cpuCount = cpuMetaDatas.Count;
            for (int i = 0; i < cpuCount; i++)
            {
                string key = cpuMetaDatas.keys[i];
                if (key.StartsWith("_")) continue; // skip _comment etc.
                JSONObject mJdata = cpuMetaDatas.GetField(key);
                PropDatasConfig propdata = new PropDatasConfig(mJdata.GetField("targeName").str);
                CPUParticleMaterialPropsConfigs.Add(key, propdata);
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
                            defined = texture.GetField("defind").str;
                        if (texture.GetField("isNormal") != null)
                            isNormal = texture.GetField("isNormal").b;
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
                            propdata.addColorProps(uName, lName, color.GetField("hdrName").str);
                        else
                            propdata.addColorProps(uName, lName);
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
                            isGama = floatData.GetField("isgama").b;
                        string rule = null;
                        if (floatData.GetField("rule") != null)
                            rule = floatData.GetField("rule").str;
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
                            propdata.addDefineds(uName, lName, from, definedData.GetField("deflat").n);
                        else
                            propdata.addDefineds(uName, lName, from);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get material config for CPU particle mode: override table first, fallback to main table.
    /// </summary>
    public static PropDatasConfig getCPUParticleConfig(string shaderName)
    {
        if (CPUParticleMaterialPropsConfigs != null && CPUParticleMaterialPropsConfigs.ContainsKey(shaderName))
            return CPUParticleMaterialPropsConfigs[shaderName];
        return getMetarialConfig(shaderName);
    }

    public static PropDatasConfig getMetarialConfig(string shaderName)
    {
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            // 启用了自定义Shader导出时，未注册的shader是预期情况（走CustomShaderExporter），静默跳过
            if (!ExportConfig.EnableCustomShaderExport)
            {
                FileUtil.setStatuse(false);
                Debug.LogWarning("LayaAir3D: Shader config not found: " + shaderName);
            }
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
    public static void WriteMetarial(Material material, JSONObject jsonData, ResoureMap resoureMap, MaterialFile materialFile = null, bool isCPUParticle = false)
    {
        string shaderName = material.shader.name;

        // CPU 粒子模式：优先使用 CPU 覆盖配置表
        PropDatasConfig propsData = null;
        if (isCPUParticle)
        {
            propsData = getCPUParticleConfig(shaderName);
        }

        if (propsData == null)
        {
            // 检查是否有内置配置
            if (!MaterialPropsConfigs.ContainsKey(shaderName))
            {
                // 优先走自定义Shader导出（包括粒子渲染器使用的自定义shader）
                if (ExportConfig.EnableCustomShaderExport)
                {
                    CustomShaderExporter.WriteAutoCustomShaderMaterial(material, jsonData, resoureMap, materialFile);
                    return;
                }

                // 未启用自定义Shader导出时，粒子渲染器材质回退到Laya内置粒子shader
                if (materialFile != null && materialFile.IsUsedByParticleSystem())
                {
                    ExportLogger.Warning($"Particle material '{material.name}' uses unregistered shader '{shaderName}', fallback to Laya particle shader. Enable 'Custom Shader Export' for better results.");
                    WriteParticleMaterialGeneric(material, jsonData, resoureMap);
                    return;
                }

                FileUtil.setStatuse(false);
                Debug.LogErrorFormat(material, "LayaAir3D Warning : not get the shader config " + shaderName + ". Enable 'Custom Shader Export' to auto-export custom shaders.");
                return;
            }

            propsData = MaterialPropsConfigs[shaderName];
        }

        // 粒子材质使用特殊的导出逻辑（仅 GPU/Shuriken 模式）
        if (!isCPUParticle && propsData.materalName == "PARTICLESHURIKEN")
        {
            WriteParticleMaterial(material, jsonData, resoureMap, propsData);
            return;
        }
        
        jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        jsonData.AddField("props", props);
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
            Texture text1 = material.GetTexture(plist.Key);
            if (text1 != null)
            {
                // 检查是否是内置资源
                string texPath = UnityEditor.AssetDatabase.GetAssetPath(text1.GetInstanceID());
                if (ResoureMap.IsBuiltinResource(texPath))
                {
                    string builtinExportPath;
                    if (!ResoureMap.TryGetBuiltinTextureExportPath(text1, out builtinExportPath))
                    {
                        Debug.LogWarning("LayaAir3D: Skipping unsupported built-in texture '" + text1.name + "': " + texPath);
                        continue;
                    }
                }
                
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = resoureMap.GetTextureFile(text1, tConfig.isNormal);
                if(textureFile == null)
                {
                    Debug.LogWarning("LayaAir3D: Failed to export texture: " + texPath);
                    continue;
                }
                texture.Add(textureFile.jsonObject(tConfig.keyName));
            }
        }
        props.AddField("textures", texture);
        props.AddField("materialRenderMode", PropDatasConfig.GetRenderModule(material));

        var needSetBlinnPhongSpecular = propsData.materalName == "BLINNPHONG";
        foreach (var cList in propsData.colorLists) {
            if (!material.HasProperty(cList.Key)) {
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
            if (!needSetBlinnPhongSpecular && cList.Value != "u_MaterialSpecular") {
                needSetBlinnPhongSpecular = false;
            }
            props.AddField(cList.Value, colorValue);
        }
        if (needSetBlinnPhongSpecular) {
            JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
            colorValue.Add(0.2f);
            colorValue.Add(0.2f);
            colorValue.Add(0.2f);
            colorValue.Add(1f);
            props.AddField("u_MaterialSpecular", colorValue);
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

    public static void WriteSkyMetarial(Material material, JSONObject jsonData, ResoureMap resoureMap)
    {
        string materialPath = AssetsUtil.GetMaterialPath(material);
        if (string.IsNullOrEmpty(materialPath))
        {
            Debug.LogWarning("LayaAir3D: Material path is null or empty");
            return;
        }
        // 修复：安全地获取不带扩展名的路径
        int dotIndex = materialPath.LastIndexOf('.');
        string cubeMapPath = (dotIndex >= 0 ? materialPath.Substring(0, dotIndex) : materialPath) + ".cubemap";
        JsonFile cubeMapData = new JsonFile(cubeMapPath, new JSONObject(JSONObject.Type.OBJECT));
        resoureMap.AddExportFile(cubeMapData);
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            FileUtil.setStatuse(false);
            Debug.LogWarning("LayaAir3D: Shader config not found: " + shaderName);
            return;
        }
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = resoureMap.GetTextureFile(material.GetTexture(plist.Key), tConfig.isNormal);
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


        jsonData.AddField("version", "LAYAMATERIAL:04");

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
        foreach (var flist in propsData.floatLists)
        {
            props.AddField(flist.Value.keyName, material.GetFloat(flist.Key));

        }
        jsonData.AddField("props", props);
    }

    /// <summary>
    /// 导出粒子材质 - 使用 PARTICLESHURIKEN 类型
    /// 参考 ParticleMaterial.lmat 模板结构:
    /// {"version":"LAYAMATERIAL:04","props":{"textures":[{"name":"u_texture"}],"type":"PARTICLESHURIKEN",
    /// "renderQueue":3000,"materialRenderMode":2,"s_Cull":2,"s_Blend":1,"s_BlendSrc":6,"s_BlendDst":7,
    /// "s_DepthTest":1,"s_DepthWrite":false,"u_Tintcolor":[0.5,0.5,0.5,1],"defines":["TINTCOLOR"]}}
    /// </summary>
    public static void WriteParticleMaterial(Material material, JSONObject jsonData, ResoureMap resoureMap, PropDatasConfig propsData)
    {
        jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        jsonData.AddField("props", props);
        
        // 纹理数组
        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        bool hasValidTexture = false;
        
        // 尝试从配置的属性列表获取纹理
        foreach (var plist in propsData.pictureList)
        {
            Texture tex = material.GetTexture(plist.Key);
            if (tex != null)
            {
                TextureConfig tConfig = plist.Value;
                string texPath = UnityEditor.AssetDatabase.GetAssetPath(tex.GetInstanceID());
                if (!ResoureMap.IsBuiltinResource(texPath))
                {
                    TextureFile textureFile = resoureMap.GetTextureFile(tex, tConfig.isNormal);
                    if (textureFile != null)
                    {
                                        textures.Add(textureFile.jsonObject("u_texture"));
                        hasValidTexture = true;
                    }
                }
            }
        }

        // 如果配置列表没有找到纹理，尝试直接从 _MainTex 获取
        if (!hasValidTexture && material.HasProperty("_MainTex"))
        {
            Texture tex = material.GetTexture("_MainTex");
            if (tex != null)
            {
                string texPath = UnityEditor.AssetDatabase.GetAssetPath(tex.GetInstanceID());
                if (!ResoureMap.IsBuiltinResource(texPath))
                {
                    TextureFile textureFile = resoureMap.GetTextureFile(tex, false);
                    if (textureFile != null)
                    {
                        textures.Add(textureFile.jsonObject("u_texture"));
                        hasValidTexture = true;
                    }
                }
            }
        }

        props.AddField("textures", textures);

        // 使用 Laya 内置粒子材质类型
        props.AddField("type", "PARTICLESHURIKEN");
        
        // 渲染队列 - 粒子通常使用透明队列
        props.AddField("renderQueue", material.renderQueue > 0 ? material.renderQueue : 3000);
        
        // 材质渲染模式 - 通过混合因子自动区分 Additive(3) / AlphaBlend(2)
        props.AddField("materialRenderMode", PropDatasConfig.DetectTransparentRenderMode(material));
        
        // 剔除模式 - 粒子默认双面 (0=Off, 1=Front, 2=Back)
        int cullMode = 0; // 默认 Off (粒子通常双面渲染)
        if (material.HasProperty("_Cull"))
        {
            cullMode = material.GetInt("_Cull");
        }
        props.AddField("s_Cull", cullMode);
        
        // 混合模式 - 粒子开启混合
        props.AddField("s_Blend", 1);
        
        // 源混合因子和目标混合因子
        int srcBlend = GetParticleSrcBlend(material);
        int dstBlend = GetParticleDstBlend(material);
        props.AddField("s_BlendSrc", srcBlend);
        props.AddField("s_BlendDst", dstBlend);
        
        // 深度测试 - 默认开启 (1=Less)
        int depthTest = 1;
        if (material.HasProperty("_ZTest"))
        {
            // Unity ZTest 值转换为 LayaAir 值
            int zTest = material.GetInt("_ZTest");
            depthTest = ConvertZTestToLaya(zTest);
        }
        props.AddField("s_DepthTest", depthTest);
        
        // 深度写入 - 粒子通常关闭深度写入
        bool depthWrite = false;
        if (material.HasProperty("_ZWrite"))
        {
            depthWrite = material.GetInt("_ZWrite") == 1;
        }
        props.AddField("s_DepthWrite", depthWrite);
        
        // 颜色属性 - 尝试从多个可能的属性获取
        Color tintColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        bool hasColor = false;
        
        // 尝试从配置的颜色属性获取
        foreach (var cList in propsData.colorLists)
        {
            if (material.HasProperty(cList.Key))
            {
                tintColor = material.GetColor(cList.Key);
                hasColor = true;
                break;
            }
        }
        
        // 如果配置没有找到，尝试常见的颜色属性
        if (!hasColor)
        {
            if (material.HasProperty("_TintColor"))
            {
                tintColor = material.GetColor("_TintColor");
                hasColor = true;
            }
            else if (material.HasProperty("_Color"))
            {
                tintColor = material.GetColor("_Color");
                hasColor = true;
            }
            else if (material.HasProperty("_BaseColor"))
            {
                tintColor = material.GetColor("_BaseColor");
                hasColor = true;
            }
        }
        
        JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
        colorValue.Add(tintColor.r);
        colorValue.Add(tintColor.g);
        colorValue.Add(tintColor.b);
        colorValue.Add(tintColor.a);
        props.AddField("u_Tintcolor", colorValue);

        // TilingOffset - 如果有的话
        if (material.HasProperty("_MainTex_ST"))
        {
            Vector4 tilingOffset = material.GetVector("_MainTex_ST");
            JSONObject tillOffData = new JSONObject(JSONObject.Type.ARRAY);
            tillOffData.Add(tilingOffset.x);
            tillOffData.Add(tilingOffset.y);
            tillOffData.Add(tilingOffset.z);
            tillOffData.Add(tilingOffset.w);
            props.AddField("u_TilingOffset", tillOffData);
        }
        else if (material.HasProperty("_MainTex"))
        {
            Vector2 tiling = material.GetTextureScale("_MainTex");
            Vector2 offset = material.GetTextureOffset("_MainTex");
            JSONObject tillOffData = new JSONObject(JSONObject.Type.ARRAY);
            tillOffData.Add(tiling.x);
            tillOffData.Add(tiling.y);
            tillOffData.Add(offset.x);
            tillOffData.Add(offset.y);
            props.AddField("u_TilingOffset", tillOffData);
        }
        
        // Defines
        JSONObject defines = new JSONObject(JSONObject.Type.ARRAY);
        
        // 如果有颜色，添加 TINTCOLOR define
        if (hasColor)
        {
            defines.Add("TINTCOLOR");
        }
        
        if (hasValidTexture)
        {
            defines.Add("DIFFUSEMAP");
        }
        
        // 检查 ADDTIVEFOG
        if (material.HasProperty("_Mode") && material.GetInt("_Mode") == 0)
        {
            defines.Add("ADDTIVEFOG");
        }
        
        props.AddField("defines", defines);
    }

    /// <summary>
    /// 将 Unity 内置粒子 Shader 名称映射到 LayaAir 3.0 shader 名称
    /// Particles/Additive系列 → Effect_Basic_Additive
    /// 其他（AlphaBlended / Standard / default）→ Effect_Basic_AlphaBlend
    /// </summary>
    public static string GetLayaShaderNameForParticle(Material material)
    {
        string shaderName = material.shader.name.ToLower();
        if (shaderName.Contains("additive"))
            return "Effect_Basic_Additive";
        return "Effect_Basic_AlphaBlend";
    }

    /// <summary>
    /// 通用粒子材质导出：当粒子渲染器使用的材质无法通过注册配置或内置检测导出时，
    /// 回退到 Laya 内置粒子 shader（根据混合模式选择 AlphaBlend 或 Additive）
    /// </summary>
    public static void WriteParticleMaterialGeneric(Material material, JSONObject jsonData, ResoureMap resoureMap)
    {
        jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        jsonData.AddField("props", props);

        // 纹理 - 尝试从常见的贴图属性获取
        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        bool hasValidTexture = false;
        string[] texPropNames = { "_MainTex", "_BaseMap", "_AlbedoTexture" };
        foreach (string propName in texPropNames)
        {
            if (!material.HasProperty(propName)) continue;
            Texture tex = material.GetTexture(propName);
            if (tex == null) continue;
            string texPath = UnityEditor.AssetDatabase.GetAssetPath(tex.GetInstanceID());
            if (ResoureMap.IsBuiltinResource(texPath)) continue;
            TextureFile textureFile = resoureMap.GetTextureFile(tex, false);
            if (textureFile != null)
            {
                textures.Add(textureFile.jsonObject("u_texture"));
                hasValidTexture = true;
                break;
            }
        }
        props.AddField("textures", textures);

        // 使用 Laya 内置粒子材质类型
        props.AddField("type", "PARTICLESHURIKEN");

        // 渲染队列
        props.AddField("renderQueue", material.renderQueue > 0 ? material.renderQueue : 3000);

        // 材质渲染模式
        props.AddField("materialRenderMode", PropDatasConfig.DetectTransparentRenderMode(material));

        // 剔除模式 - 粒子默认双面
        int cullMode = 0;
        if (material.HasProperty("_Cull"))
            cullMode = material.GetInt("_Cull");
        props.AddField("s_Cull", cullMode);

        // 混合设置
        props.AddField("s_Blend", 1);
        props.AddField("s_BlendSrc", GetParticleSrcBlend(material));
        props.AddField("s_BlendDst", GetParticleDstBlend(material));

        // 深度
        props.AddField("s_DepthTest", 1);
        bool depthWrite = false;
        if (material.HasProperty("_ZWrite"))
            depthWrite = material.GetInt("_ZWrite") == 1;
        props.AddField("s_DepthWrite", depthWrite);

        // 颜色
        Color tintColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        bool hasColor = false;
        string[] colorPropNames = { "_TintColor", "_Color", "_BaseColor" };
        foreach (string propName in colorPropNames)
        {
            if (material.HasProperty(propName))
            {
                tintColor = material.GetColor(propName);
                hasColor = true;
                break;
            }
        }
        JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
        colorValue.Add(tintColor.r);
        colorValue.Add(tintColor.g);
        colorValue.Add(tintColor.b);
        colorValue.Add(tintColor.a);
        props.AddField("u_Tintcolor", colorValue);

        // Defines
        JSONObject defines = new JSONObject(JSONObject.Type.ARRAY);
        if (hasColor) defines.Add("TINTCOLOR");
        if (hasValidTexture) defines.Add("DIFFUSEMAP");
        props.AddField("defines", defines);
    }

    /// <summary>
    /// 获取粒子材质的源混合因子 (LayaAir 格式)
    /// LayaAir BlendFactor: 0=Zero, 1=One, 2=SrcColor, 3=OneMinusSrcColor, 4=DstColor,
    /// 5=OneMinusDstColor, 6=SrcAlpha, 7=OneMinusSrcAlpha, 8=DstAlpha, 9=OneMinusDstAlpha
    /// </summary>
    private static int GetParticleSrcBlend(Material material)
    {
        if (material.HasProperty("_SrcBlend"))
        {
            int srcBlend = material.GetInt("_SrcBlend");
            return ConvertUnityBlendToLaya(srcBlend);
        }
        
        // 根据 shader 名称判断默认值
        string shaderName = material.shader.name.ToLower();
        if (shaderName.Contains("additive"))
        {
            return 6; // SrcAlpha
        }
        else if (shaderName.Contains("premultiply"))
        {
            return 1; // One
        }
        else if (shaderName.Contains("multiply"))
        {
            return 4; // DstColor
        }
        
        return 6; // 默认 SrcAlpha
    }
    
    /// <summary>
    /// 获取粒子材质的目标混合因子 (LayaAir 格式)
    /// </summary>
    private static int GetParticleDstBlend(Material material)
    {
        if (material.HasProperty("_DstBlend"))
        {
            int dstBlend = material.GetInt("_DstBlend");
            return ConvertUnityBlendToLaya(dstBlend);
        }
        
        // 根据 shader 名称判断默认值
        string shaderName = material.shader.name.ToLower();
        if (shaderName.Contains("additive"))
        {
            return 1; // One
        }
        else if (shaderName.Contains("multiply"))
        {
            return 0; // Zero
        }
        
        return 7; // 默认 OneMinusSrcAlpha
    }
    
    /// <summary>
    /// Unity BlendMode 转换为 LayaAir BlendFactor
    /// Unity: 0=Zero, 1=One, 2=DstColor, 3=SrcColor, 4=OneMinusDstColor, 5=SrcAlpha,
    /// 6=OneMinusSrcAlpha, 7=DstAlpha, 8=OneMinusDstAlpha, 9=SrcAlphaSaturate, 10=OneMinusSrcColor
    /// LayaAir: 0=Zero, 1=One, 2=SrcColor, 3=OneMinusSrcColor, 4=DstColor,
    /// 5=OneMinusDstColor, 6=SrcAlpha, 7=OneMinusSrcAlpha, 8=DstAlpha, 9=OneMinusDstAlpha
    /// </summary>
    private static int ConvertUnityBlendToLaya(int unityBlend)
    {
        switch (unityBlend)
        {
            case 0: return 0;  // Zero -> Zero
            case 1: return 1;  // One -> One
            case 2: return 4;  // DstColor -> DstColor
            case 3: return 2;  // SrcColor -> SrcColor
            case 4: return 5;  // OneMinusDstColor -> OneMinusDstColor
            case 5: return 6;  // SrcAlpha -> SrcAlpha
            case 6: return 7;  // OneMinusSrcAlpha -> OneMinusSrcAlpha [FIXED: was 3]
            case 7: return 8;  // DstAlpha -> DstAlpha
            case 8: return 9;  // OneMinusDstAlpha -> OneMinusDstAlpha
            case 9: return 6;  // SrcAlphaSaturate -> SrcAlpha (近似)
            case 10: return 3; // OneMinusSrcColor -> OneMinusSrcColor [FIXED: was 7]
            default: return 1; // 默认 One
        }
    }
    
    /// <summary>
    /// Unity ZTest 转换为 LayaAir DepthTest
    /// Unity: 0=Disabled, 1=Never, 2=Less, 3=Equal, 4=LessEqual, 5=Greater, 6=NotEqual, 7=GreaterEqual, 8=Always
    /// LayaAir: 0=Off, 1=Less, 2=Equal, 3=LessEqual, 4=Greater, 5=NotEqual, 6=GreaterEqual, 7=Always
    /// </summary>
    private static int ConvertZTestToLaya(int unityZTest)
    {
        switch (unityZTest)
        {
            case 0: return 0;  // Disabled -> Off
            case 1: return 0;  // Never -> Off (近似)
            case 2: return 1;  // Less -> Less
            case 3: return 2;  // Equal -> Equal
            case 4: return 3;  // LessEqual -> LessEqual
            case 5: return 4;  // Greater -> Greater
            case 6: return 5;  // NotEqual -> NotEqual
            case 7: return 6;  // GreaterEqual -> GreaterEqual
            case 8: return 7;  // Always -> Always
            default: return 1; // 默认 Less
        }
    }

}
