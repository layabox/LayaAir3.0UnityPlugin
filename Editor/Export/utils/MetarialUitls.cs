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

/// <summary>
/// JSON "properties" 中支持的基础材质属性类型。
/// Texture 仍由 textures 配置导出；Tiling/Offset 是纹理附加行为，不属于基础类型。
/// </summary>
public enum MaterialPropertyValueType
{
    Float,
    Int,
    Bool,
    Vector2,
    Vector3,
    Vector4,
    Color
}

public class MaterialPropertyConfig
{
    public string unityName;
    public string layaName;
    public MaterialPropertyValueType valueType;
    public bool isGamma;
    public string hdrName;
    public string rule;
}

//贴图配置
public class TextureConfig
{
    public string keyName;
    public bool isNormal;
    public string tilingOffsetName;
    public TextureConfig(string keyname, bool isNomal, string tilingOffsetName = null)
    {
        this.keyName = keyname;
        this.isNormal = isNomal;
        this.tilingOffsetName = tilingOffsetName;
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
    private List<MaterialPropertyConfig> _propertyLists;
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
        this._propertyLists = new List<MaterialPropertyConfig>();
    }

    public void addTextureProps(string uprops, string lprops, string definde = null, bool isnormal = false,
        string tilingOffsetName = null)
    {
        this._pictureList.Add(uprops, new TextureConfig(lprops, isnormal, tilingOffsetName));
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

    public void addProperty(MaterialPropertyConfig property)
    {
        this._propertyLists.Add(property);
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

    public List<MaterialPropertyConfig> propertyLists
    {
        get
        {
            return this._propertyLists;
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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.Cull;
        if (material.HasProperty("_BUILTIN_CullMode"))
        {
            return material.GetInt("_BUILTIN_CullMode");
        }
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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.Blend;

        // Legacy fallback for graphs whose active target is not supported.
        if (material.HasProperty("_BUILTIN_Surface"))
            return material.GetInt("_BUILTIN_Surface") != 0 || material.renderQueue >= 3000 ? 1 : 0;

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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.SrcRGB;
        if (material.HasProperty("_BUILTIN_SrcBlend"))
            return UnityBlendFactorToLaya(material.GetInt("_BUILTIN_SrcBlend"), 6);

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
            return UnityBlendFactorToLaya(unitySrc, 6);
        }
        else
        {
            return 6; // 无属性时默认SrcAlpha（透明材质）
        }
    }
    public static int GetDstBlend(Material material)
    {
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.DstRGB;
        if (material.HasProperty("_BUILTIN_DstBlend"))
            return UnityBlendFactorToLaya(material.GetInt("_BUILTIN_DstBlend"), 7);

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
            return UnityBlendFactorToLaya(unityDst, 7);
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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.DepthWrite;
        if (material.HasProperty("_BUILTIN_ZWrite"))
            return material.GetInt("_BUILTIN_ZWrite") != 0;

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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.DepthTest;
        if (material.HasProperty("_BUILTIN_ZTest"))
            return UnityZTestToLaya(material.GetInt("_BUILTIN_ZTest"));

        if (material.HasProperty("_ZTest"))
        {
            return UnityZTestToLaya(material.GetInt("_ZTest"));
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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.AlphaTest;
        if (material.HasProperty("_BUILTIN_AlphaClip"))
            return material.GetInt("_BUILTIN_AlphaClip") != 0;

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
        if (material.HasProperty("_Alpha_Clip_Threshold"))
            return material.GetFloat("_Alpha_Clip_Threshold");

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
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.RenderMode;
        if (material.shader.name.StartsWith("Laya/") && material.HasProperty("_Mode")) {
            return material.GetInt("_Mode");
        }

        if (material.HasProperty("_BUILTIN_Surface"))
        {
            bool transparent = material.GetInt("_BUILTIN_Surface") != 0 || material.renderQueue >= 3000;
            if (transparent)
                return DetectTransparentRenderMode(material);
            return GetAlphaTest(material) ? 1 : 0;
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
    /// 检测透明材质的具体渲染模式。
    /// 只有完整状态匹配 Laya 标准模式时才返回标准模式，其他组合使用 Custom(5)。
    /// </summary>
    public static int DetectTransparentRenderMode(Material material)
    {
        ShaderGraphRenderState graphState;
        if (ShaderGraphRenderState.TryResolve(material, out graphState)) return graphState.RenderMode;
        int srcBlend = GetSrcBlend(material);
        int dstBlend = GetDstBlend(material);
        bool depthWrite = GetZWrite(material);

        if (!depthWrite && srcBlend == 6 && dstBlend == 7)
            return 2; // Alpha Blend: SrcAlpha / OneMinusSrcAlpha

        if (!depthWrite && srcBlend == 6 && dstBlend == 1)
            return 3; // Additive: SrcAlpha / One

        // Premultiply (One / OneMinusSrcAlpha), Multiply and all other non-standard states.
        return 5;
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

    internal static void WriteRenderState(Material material, JSONObject props)
    {
        if (ShaderGraphRenderState.TryWrite(material, props)) return;

        // Keep existing ShaderLab/mapped-material behavior outside the ShaderGraph fix.
        props.AddField("materialRenderMode", GetRenderModule(material));
        props.AddField("s_Cull", GetCull(material));
        props.AddField("s_Blend", GetBlend(material));
        props.AddField("s_BlendSrc", GetSrcBlend(material));
        props.AddField("s_BlendDst", GetDstBlend(material));
        props.AddField("s_DepthTest", GetZTest(material));
        props.AddField("s_DepthWrite", GetZWrite(material));
        props.AddField("alphaTest", GetAlphaTest(material));
        props.AddField("alphaTestValue", GetAlphaTestValue(material));
        props.AddField("renderQueue", material.renderQueue);
    }

    private static int UnityBlendFactorToLaya(int unityBlend, int fallback)
    {
        switch ((UnityEngine.Rendering.BlendMode)unityBlend)
        {
            case UnityEngine.Rendering.BlendMode.Zero: return 0;
            case UnityEngine.Rendering.BlendMode.One: return 1;
            case UnityEngine.Rendering.BlendMode.DstColor: return 4;
            case UnityEngine.Rendering.BlendMode.SrcColor: return 2;
            case UnityEngine.Rendering.BlendMode.OneMinusDstColor: return 5;
            case UnityEngine.Rendering.BlendMode.SrcAlpha: return 6;
            case UnityEngine.Rendering.BlendMode.OneMinusSrcColor: return 3;
            case UnityEngine.Rendering.BlendMode.DstAlpha: return 8;
            case UnityEngine.Rendering.BlendMode.OneMinusDstAlpha: return 9;
            case UnityEngine.Rendering.BlendMode.SrcAlphaSaturate: return 6;
            case UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha: return 7;
            default: return fallback;
        }
    }

    private static int UnityZTestToLaya(int unityZTest)
    {
        switch ((UnityEngine.Rendering.CompareFunction)unityZTest)
        {
            case UnityEngine.Rendering.CompareFunction.Disabled:
            case UnityEngine.Rendering.CompareFunction.Never: return 0;
            case UnityEngine.Rendering.CompareFunction.Less: return 1;
            case UnityEngine.Rendering.CompareFunction.Equal: return 2;
            case UnityEngine.Rendering.CompareFunction.LessEqual: return 3;
            case UnityEngine.Rendering.CompareFunction.Greater: return 4;
            case UnityEngine.Rendering.CompareFunction.NotEqual: return 5;
            case UnityEngine.Rendering.CompareFunction.GreaterEqual: return 6;
            case UnityEngine.Rendering.CompareFunction.Always: return 7;
            default: return 3;
        }
    }
}
internal class MetarialUitls 
{
    private const string ProjectMaterialMappingsAssetSuffix = "/Editor/LayaAir/MaterialMappings.json";

    public static Dictionary<string, PropDatasConfig> MaterialPropsConfigs;
    public static Dictionary<string, PropDatasConfig> CPUParticleMaterialPropsConfigs;

    private static bool TryParsePropertyValueType(string typeName, out MaterialPropertyValueType valueType)
    {
        valueType = MaterialPropertyValueType.Float;
        if (string.IsNullOrEmpty(typeName))
            return false;

        switch (typeName.Trim().ToLowerInvariant())
        {
            case "float":
            case "range":
            case "number":
                valueType = MaterialPropertyValueType.Float;
                return true;
            case "int":
            case "integer":
                valueType = MaterialPropertyValueType.Int;
                return true;
            case "bool":
            case "boolean":
                valueType = MaterialPropertyValueType.Bool;
                return true;
            case "vector2":
            case "vec2":
                valueType = MaterialPropertyValueType.Vector2;
                return true;
            case "vector3":
            case "vec3":
                valueType = MaterialPropertyValueType.Vector3;
                return true;
            case "vector":
            case "vector4":
            case "vec4":
                valueType = MaterialPropertyValueType.Vector4;
                return true;
            case "color":
                valueType = MaterialPropertyValueType.Color;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 解析新版 properties 数组。旧版 textures/colors/floats/tillOffset 配置继续保留，
    /// 因而已有映射无需迁移；自定义 Shader 可用显式 type 配置基础 uniform。
    /// </summary>
    private static void ParsePropertyMappings(JSONObject materialData, PropDatasConfig propData, string shaderName)
    {
        JSONObject properties = materialData["properties"];
        if (properties == null)
            return;

        if (!properties.IsArray)
        {
            Debug.LogWarning($"LayaAir3D: Material mapping 'properties' must be an array: {shaderName}");
            return;
        }

        for (int i = 0; i < properties.Count; i++)
        {
            JSONObject property = properties[i];
            if (property == null)
            {
                Debug.LogWarning($"LayaAir3D: Material property mapping is null: {shaderName}[{i}]");
                continue;
            }

            JSONObject unityNameField = property.GetField("uName");
            JSONObject layaNameField = property.GetField("layaName");
            JSONObject typeField = property.GetField("type");
            if (unityNameField == null || layaNameField == null || typeField == null ||
                string.IsNullOrEmpty(unityNameField.str) || string.IsNullOrEmpty(layaNameField.str) ||
                string.IsNullOrEmpty(typeField.str))
            {
                Debug.LogWarning($"LayaAir3D: Material property mapping requires uName, layaName and type: {shaderName}[{i}]");
                continue;
            }

            MaterialPropertyValueType valueType;
            if (!TryParsePropertyValueType(typeField.str, out valueType))
            {
                Debug.LogWarning($"LayaAir3D: Unsupported material property type '{typeField.str}': {shaderName}[{i}]");
                continue;
            }

            MaterialPropertyConfig config = new MaterialPropertyConfig();
            config.unityName = unityNameField.str;
            config.layaName = layaNameField.str;
            config.valueType = valueType;

            JSONObject gammaField = property.GetField("isGamma");
            config.isGamma = gammaField != null && gammaField.b;

            JSONObject hdrField = property.GetField("hdrName");
            config.hdrName = hdrField != null ? hdrField.str : null;

            JSONObject ruleField = property.GetField("rule");
            config.rule = ruleField != null ? ruleField.str : null;

            propData.addProperty(config);
        }
    }

    private static JSONObject GetFirstField(JSONObject data, params string[] fieldNames)
    {
        if (data == null)
            return null;
        foreach (string fieldName in fieldNames)
        {
            JSONObject field = data.GetField(fieldName);
            if (field != null)
                return field;
        }
        return null;
    }

    private static string GetRequiredString(JSONObject data, params string[] fieldNames)
    {
        JSONObject field = GetFirstField(data, fieldNames);
        return field != null ? field.str : null;
    }

    /// <summary>
    /// Parse one project material mapping entry. Project mappings accept the corrected
    /// "targetName" spelling while retaining the legacy built-in "targeName" spelling.
    /// </summary>
    private static bool TryParseProjectMaterialMapping(
        string shaderName,
        JSONObject materialData,
        out PropDatasConfig propData)
    {
        propData = null;
        if (materialData == null)
        {
            Debug.LogWarning($"LayaAir3D: Project material mapping is null: {shaderName}");
            return false;
        }

        string targetName = GetRequiredString(materialData, "targetName", "targeName");
        if (string.IsNullOrEmpty(targetName))
        {
            Debug.LogWarning($"LayaAir3D: Project material mapping requires targetName: {shaderName}");
            return false;
        }

        propData = new PropDatasConfig(targetName);

        JSONObject textures = materialData["textures"];
        if (textures != null)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                JSONObject texture = textures[i];
                string unityName = GetRequiredString(texture, "uName");
                string layaName = GetRequiredString(texture, "layaName");
                if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(layaName))
                {
                    Debug.LogWarning($"LayaAir3D: Project texture mapping requires uName and layaName: {shaderName}[{i}]");
                    continue;
                }

                string define = GetRequiredString(texture, "define", "defind");
                JSONObject isNormalField = texture.GetField("isNormal");
                bool isNormal = isNormalField != null && isNormalField.b;
                string tilingOffsetName = GetRequiredString(texture, "tilingOffset");
                propData.addTextureProps(unityName, layaName, define, isNormal, tilingOffsetName);
            }
        }

        JSONObject colors = materialData["colors"];
        if (colors != null)
        {
            for (int i = 0; i < colors.Count; i++)
            {
                JSONObject color = colors[i];
                string unityName = GetRequiredString(color, "uName");
                string layaName = GetRequiredString(color, "layaName");
                if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(layaName))
                {
                    Debug.LogWarning($"LayaAir3D: Project color mapping requires uName and layaName: {shaderName}[{i}]");
                    continue;
                }
                propData.addColorProps(unityName, layaName, GetRequiredString(color, "hdrName"));
            }
        }

        JSONObject floats = materialData["floats"];
        if (floats != null)
        {
            for (int i = 0; i < floats.Count; i++)
            {
                JSONObject floatData = floats[i];
                string unityName = GetRequiredString(floatData, "uName");
                string layaName = GetRequiredString(floatData, "layaName");
                if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(layaName))
                {
                    Debug.LogWarning($"LayaAir3D: Project float mapping requires uName and layaName: {shaderName}[{i}]");
                    continue;
                }

                JSONObject gammaField = GetFirstField(floatData, "isGamma", "isgama");
                bool isGamma = gammaField != null && gammaField.b;
                propData.addFloatProps(unityName, layaName, isGamma, GetRequiredString(floatData, "rule"));
            }
        }

        JSONObject tillOffsets = GetFirstField(materialData, "tilingOffset", "tillOffset");
        if (tillOffsets != null)
        {
            for (int i = 0; i < tillOffsets.Count; i++)
            {
                JSONObject tillOffset = tillOffsets[i];
                string unityName = GetRequiredString(tillOffset, "uName");
                string layaName = GetRequiredString(tillOffset, "layaName");
                if (!string.IsNullOrEmpty(unityName) && !string.IsNullOrEmpty(layaName))
                    propData.addTillOffsetProps(unityName, layaName);
            }
        }

        ParsePropertyMappings(materialData, propData, shaderName);

        JSONObject defines = GetFirstField(materialData, "defines", "defineds");
        if (defines != null)
        {
            for (int i = 0; i < defines.Count; i++)
            {
                JSONObject define = defines[i];
                string unityName = GetRequiredString(define, "uName");
                string layaName = GetRequiredString(define, "layaName");
                JSONObject fromField = define != null ? define.GetField("from") : null;
                if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(layaName) || fromField == null)
                {
                    Debug.LogWarning($"LayaAir3D: Project define mapping requires uName, layaName and from: {shaderName}[{i}]");
                    continue;
                }

                JSONObject defaultField = GetFirstField(define, "default", "deflat");
                propData.addDefineds(
                    unityName,
                    layaName,
                    (DefindsFrom)fromField.n,
                    defaultField != null ? defaultField.n : 0f);
            }
        }

        JSONObject rules = materialData["rules"];
        if (rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                JSONObject rule = rules[i];
                string name = GetRequiredString(rule, "name");
                string target = GetRequiredString(rule, "target");
                string targetNameForRule = GetRequiredString(rule, "targetName");
                string ruleType = GetRequiredString(rule, "ruleType");
                string ruleKeyName = GetRequiredString(rule, "ruleKeyName");
                JSONObject dataField = rule != null ? rule.GetField("data") : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(target) ||
                    string.IsNullOrEmpty(targetNameForRule) || string.IsNullOrEmpty(ruleType) ||
                    string.IsNullOrEmpty(ruleKeyName) || dataField == null)
                {
                    Debug.LogWarning($"LayaAir3D: Invalid project material rule: {shaderName}[{i}]");
                    continue;
                }

                ConditionConfig ruleConfig = new ConditionConfig();
                ruleConfig.target = target;
                ruleConfig.targetName = targetNameForRule;
                ruleConfig.ruleType = ruleType;
                ruleConfig.ruleKeyName = ruleKeyName;
                ruleConfig.data = ruleType == "texture" ? (object)dataField.b : dataField.n;
                propData.rules[name] = ruleConfig;
            }
        }

        return true;
    }

    private static int MergeProjectMaterialMappings(
        JSONObject mappings,
        Dictionary<string, PropDatasConfig> destination,
        string sectionName)
    {
        if (mappings == null)
            return 0;

        int loadedCount = 0;
        for (int i = 0; i < mappings.Count; i++)
        {
            string shaderName = mappings.keys[i];
            if (string.IsNullOrEmpty(shaderName) || shaderName.StartsWith("_"))
                continue;

            try
            {
                PropDatasConfig propData;
                if (!TryParseProjectMaterialMapping(shaderName, mappings.GetField(shaderName), out propData))
                    continue;

                // Project entries replace a complete built-in entry with the same shader name.
                destination[shaderName] = propData;
                loadedCount++;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"LayaAir3D: Failed to load project {sectionName} mapping '{shaderName}': {exception.Message}");
            }
        }
        return loadedCount;
    }

    private static string FindProjectMaterialMappingsAssetPath()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("MaterialMappings", new[] { "Assets" });
        List<string> matchingPaths = new List<string>();
        foreach (string guid in guids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (assetPath.EndsWith(ProjectMaterialMappingsAssetSuffix, System.StringComparison.OrdinalIgnoreCase))
                matchingPaths.Add(assetPath);
        }

        if (matchingPaths.Count == 0)
            return null;

        matchingPaths.Sort(System.StringComparer.OrdinalIgnoreCase);
        if (matchingPaths.Count > 1)
        {
            FileUtil.setStatuse(false);
            Debug.LogError(
                "LayaAir3D: Multiple project material mapping files were found. " +
                "Keep only one Assets/**/Editor/LayaAir/MaterialMappings.json: " +
                string.Join(", ", matchingPaths.ToArray()));
            return null;
        }

        return matchingPaths[0];
    }

    private static void LoadProjectMaterialMappings()
    {
        string configAssetPath = FindProjectMaterialMappingsAssetPath();
        if (string.IsNullOrEmpty(configAssetPath))
            return;

        try
        {
            TextAsset configAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(configAssetPath);
            if (configAsset == null)
            {
                Debug.LogWarning($"LayaAir3D: Cannot load project material mappings: {configAssetPath}");
                return;
            }

            JSONObject root = JSONObject.Create(configAsset.text);
            if (root == null)
            {
                Debug.LogWarning($"LayaAir3D: Project material mappings JSON is empty: {configAssetPath}");
                return;
            }

            JSONObject materialMappings = root.GetField("materials");
            JSONObject cpuParticleMappings = root.GetField("cpuParticle");
            if (materialMappings == null && cpuParticleMappings == null)
            {
                Debug.LogWarning(
                    $"LayaAir3D: Project material mappings must contain 'materials' or 'cpuParticle': {configAssetPath}");
                return;
            }

            int materialCount = MergeProjectMaterialMappings(
                materialMappings,
                MaterialPropsConfigs,
                "material");
            int cpuParticleCount = MergeProjectMaterialMappings(
                cpuParticleMappings,
                CPUParticleMaterialPropsConfigs,
                "CPU particle");

            ExportLogger.Log(
                $"LayaAir3D: Loaded project material mappings from {configAssetPath} " +
                $"(materials: {materialCount}, CPU particles: {cpuParticleCount}).");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"LayaAir3D: Failed to load project material mappings '{configAssetPath}': {exception.Message}");
        }
    }

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
                    string tilingOffsetName = null;
                    if (texture.GetField("tilingOffset") != null)
                    {
                        tilingOffsetName = texture.GetField("tilingOffset").str;
                    }
                    propdata.addTextureProps(uName, lName, defined, isNormal, tilingOffsetName);
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
            ParsePropertyMappings(mJdata, propdata, key);
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
                        string tilingOffsetName = null;
                        if (texture.GetField("tilingOffset") != null)
                            tilingOffsetName = texture.GetField("tilingOffset").str;
                        propdata.addTextureProps(uName, lName, defined, isNormal, tilingOffsetName);
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
                ParsePropertyMappings(mJdata, propdata, key);
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

        // Project mappings are loaded last and replace matching built-in entries.
        // A matched mapping stays on the manual material path and therefore has higher
        // priority than CustomShaderExporter's HLSL-to-GLSL conversion fallback.
        LoadProjectMaterialMappings();
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

    private static void ReplaceMaterialField(JSONObject props, string name, JSONObject value)
    {
        if (string.IsNullOrEmpty(name) || value == null)
            return;
        if (props.GetField(name) != null)
            props.RemoveField(name);
        props.AddField(name, value);
    }

    private static void ReplaceMaterialField(JSONObject props, string name, float value)
    {
        ReplaceMaterialField(props, name, JSONObject.Create(value));
    }

    private static void ReplaceMaterialField(JSONObject props, string name, int value)
    {
        ReplaceMaterialField(props, name, JSONObject.Create(value));
    }

    private static void ReplaceMaterialField(JSONObject props, string name, bool value)
    {
        ReplaceMaterialField(props, name, JSONObject.Create(value));
    }

    private static JSONObject GetVectorValue(Vector4 value, int componentCount)
    {
        JSONObject result = new JSONObject(JSONObject.Type.ARRAY);
        result.Add(value.x);
        result.Add(value.y);
        if (componentCount >= 3) result.Add(value.z);
        if (componentCount >= 4) result.Add(value.w);
        return result;
    }

    private static bool TryGetTextureTilingOffset(Material material, string texturePropertyName, out Vector4 value)
    {
        value = new Vector4(1f, 1f, 0f, 0f);
        if (string.IsNullOrEmpty(texturePropertyName))
            return false;

        string baseTextureName = texturePropertyName;
        if (baseTextureName.EndsWith("_ST"))
            baseTextureName = baseTextureName.Substring(0, baseTextureName.Length - 3);

        if (material.HasProperty(baseTextureName))
        {
            Vector2 scale = material.GetTextureScale(baseTextureName);
            Vector2 offset = material.GetTextureOffset(baseTextureName);
            value = new Vector4(scale.x, scale.y, offset.x, offset.y);
            return true;
        }

        // 兼容旧配置中直接暴露为 Vector4 的 _ST uniform。
        if (material.HasProperty(texturePropertyName))
        {
            value = material.GetVector(texturePropertyName);
            return true;
        }

        return false;
    }

    private static void WriteTextureTilingOffset(Material material, JSONObject props,
        string texturePropertyName, string layaName)
    {
        Vector4 value;
        if (TryGetTextureTilingOffset(material, texturePropertyName, out value))
            ReplaceMaterialField(props, layaName, GetVectorValue(value, 4));
    }

    private static void ApplyPropertyRule(Material material, PropDatasConfig propsData, string ruleName,
        ref string unityName, ref string layaName)
    {
        if (string.IsNullOrEmpty(ruleName))
            return;

        ConditionConfig ruleConfig;
        if (!propsData.rules.TryGetValue(ruleName, out ruleConfig) || !getMatarialRole(material, ruleConfig))
            return;

        if (ruleConfig.target == "uName")
            unityName = ruleConfig.targetName;
        else
            layaName = ruleConfig.targetName;
    }

    private static void WriteConfiguredProperties(Material material, JSONObject props, PropDatasConfig propsData)
    {
        foreach (MaterialPropertyConfig config in propsData.propertyLists)
        {
            string unityName = config.unityName;
            string layaName = config.layaName;
            ApplyPropertyRule(material, propsData, config.rule, ref unityName, ref layaName);

            if (string.IsNullOrEmpty(unityName) || string.IsNullOrEmpty(layaName) || !material.HasProperty(unityName))
                continue;

            switch (config.valueType)
            {
                case MaterialPropertyValueType.Float:
                {
                    float value = material.GetFloat(unityName);
                    if (config.isGamma)
                        value = Mathf.LinearToGammaSpace(value);
                    ReplaceMaterialField(props, layaName, value);
                    break;
                }
                case MaterialPropertyValueType.Int:
                    ReplaceMaterialField(props, layaName, material.GetInt(unityName));
                    break;
                case MaterialPropertyValueType.Bool:
                    // Unity 的 Toggle/Boolean Shader 属性通常以 Float 保存。
                    ReplaceMaterialField(props, layaName, !Mathf.Approximately(material.GetFloat(unityName), 0f));
                    break;
                case MaterialPropertyValueType.Vector2:
                    ReplaceMaterialField(props, layaName, GetVectorValue(material.GetVector(unityName), 2));
                    break;
                case MaterialPropertyValueType.Vector3:
                    ReplaceMaterialField(props, layaName, GetVectorValue(material.GetVector(unityName), 3));
                    break;
                case MaterialPropertyValueType.Vector4:
                    ReplaceMaterialField(props, layaName, GetVectorValue(material.GetVector(unityName), 4));
                    break;
                case MaterialPropertyValueType.Color:
                {
                    Color color = material.GetColor(unityName);
                    if (!string.IsNullOrEmpty(config.hdrName))
                    {
                        Color decomposedColor;
                        float intensity;
                        GameObjectUitls.DecomposeHdrColor(color, out decomposedColor, out intensity);
                        ReplaceMaterialField(props, layaName,
                            GetVectorValue(new Vector4(decomposedColor.r, decomposedColor.g, decomposedColor.b, decomposedColor.a), 4));
                        ReplaceMaterialField(props, config.hdrName, intensity);
                    }
                    else
                    {
                        ReplaceMaterialField(props, layaName,
                            GetVectorValue(new Vector4(color.r, color.g, color.b, color.a), 4));
                    }
                    break;
                }
            }
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
        PropDatasConfig.WriteRenderState(material, props);
        JSONObject texture = new JSONObject(JSONObject.Type.ARRAY);
        foreach (var plist in propsData.pictureList)
        {
            TextureConfig tConfig = plist.Value;
            if (!material.HasProperty(plist.Key))
                continue;
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
                
                // Cubemap 与 Texture2D 共用 textures 映射，根据实际资源类型导出。
                JSONObject textureData;
                if (text1 is Cubemap cubemap)
                {
                    textureData = CustomShaderExporter.ExportCubemapTexture(cubemap, tConfig.keyName, resoureMap);
                }
                else
                {
                    TextureFile textureFile = resoureMap.GetTextureFile(text1, tConfig.isNormal);
                    textureData = textureFile != null ? textureFile.jsonObject(tConfig.keyName) : null;
                }
                if (textureData == null)
                {
                    Debug.LogWarning("LayaAir3D: Failed to export texture: " + texPath);
                    continue;
                }
                texture.Add(textureData);
            }

            // 新式配置：Tiling/Offset 作为 Texture 的附加导出行为。
            if (!string.IsNullOrEmpty(tConfig.tilingOffsetName))
                WriteTextureTilingOffset(material, props, plist.Key, tConfig.tilingOffsetName);
        }
        props.AddField("textures", texture);
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
            // 兼容旧 tillOffset 数组，但优先从 Texture Scale/Offset 读取。
            WriteTextureTilingOffset(material, props, tList.Key, tList.Value);
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

        // 新版显式基础类型映射。放在旧配置之后，以便 properties 可渐进覆盖旧字段。
        WriteConfiguredProperties(material, props, propsData);

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
        PropDatasConfig.WriteRenderState(material, props);
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
            TextureConfig tConfig = plist.Value;
            if (!material.HasProperty(plist.Key))
                continue;
            Texture tex = material.GetTexture(plist.Key);
            if (tex != null)
            {
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

            if (!string.IsNullOrEmpty(tConfig.tilingOffsetName))
                WriteTextureTilingOffset(material, props, plist.Key, tConfig.tilingOffsetName);
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
        
        // 材质渲染模式 - 标准状态匹配 Alpha/Additive，其他组合使用 Custom(5)
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
        ShaderGraphRenderState.TryWrite(material, props);
        
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

        // TilingOffset 是纹理附加行为，优先从 Texture Scale/Offset 读取。
        if (material.HasProperty("_MainTex"))
            WriteTextureTilingOffset(material, props, "_MainTex", "u_TilingOffset");
        else if (material.HasProperty("_MainTex_ST"))
            WriteTextureTilingOffset(material, props, "_MainTex_ST", "u_TilingOffset");

        WriteConfiguredProperties(material, props, propsData);
        
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
        ShaderGraphRenderState.TryWrite(material, props);

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
        if (material.HasProperty("_BUILTIN_SrcBlend") || material.HasProperty("_SrcBlend"))
            return PropDatasConfig.GetSrcBlend(material);
        
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
        if (material.HasProperty("_BUILTIN_DstBlend") || material.HasProperty("_DstBlend"))
            return PropDatasConfig.GetDstBlend(material);
        
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
