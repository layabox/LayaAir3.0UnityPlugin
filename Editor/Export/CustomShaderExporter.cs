using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// LayaAir Shader类型枚举
/// 对应LayaAir引擎中的 ShaderFeatureType
/// </summary>
public enum LayaShaderType
{
    None = -1,          // 无类型
    Default = 0,        // 默认类型
    D3 = 1,             // 3D渲染Shader（PBR、BlinnPhong、Unlit等3D物体材质）
    D2_primitive = 2,   // 2D图元Shader
    D2_TextureSV = 3,   // 2D纹理Shader
    D2_BaseRenderNode2D = 4, // 2D基础渲染节点Shader
    PostProcess = 5,    // 后处理Shader
    Sky = 6,            // 天空盒Shader
    Effect = 7          // 特效Shader（粒子系统材质、Trail拖尾等）
}

/// <summary>
/// LayaAir 材质类型（对应MetarialPropData.json中的targeName）
/// </summary>
public enum LayaMaterialType
{
    Unknown,            // 未知类型
    PBR,                // PBR材质 (Standard, URP/Lit等)
    Unlit,              // 无光照材质
    BLINNPHONG,         // BlinnPhong光照材质
    SkyBox,             // 天空盒材质
    SkyProcedural,      // 程序化天空盒
    SkyPanoramic,       // 全景天空盒
    PARTICLESHURIKEN,   // 粒子材质
    Custom              // 自定义材质（需要生成自定义Shader）
}

/// <summary>
/// 自定义Shader导出器 - 将Unity自定义Shader导出为LayaAir .shader文件
/// 并导出使用该Shader的材质
/// 
/// 转换规则参考: Unity2Laya_ShaderMapping.md
/// 示例参考: FishStandard_Base_ori.shader -> FishStandard_Base.shader
/// </summary>
internal class CustomShaderExporter
{
    // ==================== 混合架构支持 ====================

    // 映射引擎实例
    private static ShaderMappingEngine mappingEngine = null;

    // 是否使用映射表模式
    private static bool useMappingTableMode = false;

    // 映射表模式是否已初始化
    private static bool mappingEngineInitialized = false;

    // 项目级配置统一放在 Assets/**/Editor/LayaAir 下，由 AssetDatabase 发现。
    private const string ProjectShaderMappingsAssetSuffix = "/Editor/LayaAir/ShaderMappings.json";
    private const string ProjectShaderTemplatesAssetDirectory = "/Editor/LayaAir/ShaderTemplates/";

    // 性能统计
    private static System.Diagnostics.Stopwatch conversionTimer = new System.Diagnostics.Stopwatch();
    private static long builtInConversionTime = 0;
    private static long mappingTableConversionTime = 0;

    // ==================== Laya 渲染模式配置 ====================

    /// <summary>
    /// Laya MaterialRenderMode 预定义渲染模式（字段名与 .lmat 中的 s_ 前缀属性对应）
    /// </summary>
    private class LayaRenderModeConfig
    {
        public int value;
        public int renderQueue;
        public bool alphaTest;
        public int s_Cull;          // 0=NONE, 1=FRONT, 2=BACK
        public int s_Blend;         // 0=DISABLE, 1=ENABLE_ALL, 2=ENABLE_SEPERATE
        public int s_BlendSrc;      // Laya BlendParam（仅 s_Blend>0 时有效）
        public int s_BlendDst;      // Laya BlendParam（仅 s_Blend>0 时有效）
        public int s_DepthTest;     // 0=OFF, 1=LESS, 2=EQUAL, 3=LEQUAL, ...
        public bool s_DepthWrite;
        public string[] defines;    // 额外 defines
    }

    // 渲染模式配置表（从 JSON 加载或使用内置默认值）
    private static Dictionary<string, LayaRenderModeConfig> _layaRenderModes = null;

    /// <summary>
    /// 获取 Laya 渲染模式配置（懒加载）
    /// </summary>
    private static Dictionary<string, LayaRenderModeConfig> GetLayaRenderModes()
    {
        if (_layaRenderModes != null)
            return _layaRenderModes;

        _layaRenderModes = new Dictionary<string, LayaRenderModeConfig>();

        // 尝试从配置文件加载
        string configPath = Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/Editor/Mappings/laya_render_modes.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var root = new JSONObject(json);
                var modes = root.GetField("renderModes");
                if (modes != null)
                {
                    foreach (string key in modes.keys)
                    {
                        var m = modes.GetField(key);
                        var config = new LayaRenderModeConfig();
                        config.value = (int)m.GetField("value").i;
                        config.renderQueue = (int)m.GetField("renderQueue").i;

                        var alphaTestField = m.GetField("alphaTest");
                        config.alphaTest = alphaTestField != null && alphaTestField.b;

                        var cullField = m.GetField("s_Cull");
                        config.s_Cull = cullField != null ? (int)cullField.i : 2;

                        config.s_Blend = (int)m.GetField("s_Blend").i;
                        config.s_DepthTest = (int)m.GetField("s_DepthTest").i;
                        config.s_DepthWrite = m.GetField("s_DepthWrite").b;

                        var blendSrcField = m.GetField("s_BlendSrc");
                        config.s_BlendSrc = blendSrcField != null ? (int)blendSrcField.i : 0;
                        var blendDstField = m.GetField("s_BlendDst");
                        config.s_BlendDst = blendDstField != null ? (int)blendDstField.i : 0;

                        var definesField = m.GetField("defines");
                        if (definesField != null && definesField.IsArray)
                        {
                            config.defines = new string[definesField.Count];
                            for (int i = 0; i < definesField.Count; i++)
                                config.defines[i] = definesField[i].str;
                        }

                        _layaRenderModes[key] = config;
                    }
                    ExportLogger.Log($"LayaAir3D: Loaded {_layaRenderModes.Count} render modes from config");
                    return _layaRenderModes;
                }
            }
            catch (System.Exception e)
            {
                ExportLogger.Warning($"LayaAir3D: Failed to load render modes config: {e.Message}");
            }
        }

        // 内置默认值（与 laya_render_modes.json 及 Laya 编辑器实际 .lmat 输出保持一致）
        _layaRenderModes["OPAQUE"] = new LayaRenderModeConfig
            { value = 0, renderQueue = 2000, s_Cull = 2, s_Blend = 0, s_DepthTest = 1, s_DepthWrite = true };
        _layaRenderModes["CUTOUT"] = new LayaRenderModeConfig
            { value = 1, renderQueue = 2450, alphaTest = true, s_Cull = 2, s_Blend = 0, s_DepthTest = 1, s_DepthWrite = true };
        _layaRenderModes["TRANSPARENT"] = new LayaRenderModeConfig
            { value = 2, renderQueue = 3000, s_Cull = 2, s_Blend = 1, s_BlendSrc = 6, s_BlendDst = 7, s_DepthTest = 1, s_DepthWrite = false };
        _layaRenderModes["ADDTIVE"] = new LayaRenderModeConfig
            { value = 3, renderQueue = 3000, s_Cull = 2, s_Blend = 1, s_BlendSrc = 6, s_BlendDst = 1, s_DepthTest = 1, s_DepthWrite = false, defines = new[] { "ADDTIVEFOG" } };
        _layaRenderModes["ALPHABLENDED"] = new LayaRenderModeConfig
            { value = 4, renderQueue = 3000, s_Cull = 2, s_Blend = 1, s_BlendSrc = 6, s_BlendDst = 7, s_DepthTest = 1, s_DepthWrite = false };
        _layaRenderModes["CUSTOM"] = new LayaRenderModeConfig
            { value = 5, renderQueue = 2000, s_Cull = 2, s_Blend = 0, s_DepthTest = 1, s_DepthWrite = true };

        ExportLogger.Log("LayaAir3D: Using built-in render modes defaults");
        return _layaRenderModes;
    }

    /// <summary>
    /// 根据 materialRenderMode 值获取对应的渲染模式配置
    /// </summary>
    private static LayaRenderModeConfig GetRenderModeByValue(int modeValue)
    {
        var modes = GetLayaRenderModes();
        foreach (var kvp in modes)
        {
            if (kvp.Value.value == modeValue)
                return kvp.Value;
        }
        return null;
    }

    // ==================== 原有字段 ====================

    // 已导出的Shader缓存，避免重复导出
    private static HashSet<string> exportedShaders = new HashSet<string>();

    // ==================== 模板shader导出上下文 ====================
    // ExportMaterialFile 开始时设置，结束时清空，供子方法使用

    // 当前模板shader的uniformMap变量名集合（用于过滤和大小写修正）
    private static HashSet<string> _currentTemplateVarNames = null;

    // 当前模板shader的属性名覆盖映射（特定shader与通用映射不同的属性名）
    private static Dictionary<string, string> _currentTemplatePropertyOverrides = null;

    // 当前模板shader中 RadioGroup Int 属性的 members 映射
    // key: Laya变量名（如 "LayerType"），value: members数组（如 ["EFFECT_LAYER_ONE", "EFFECT_LAYER_TWO", "EFFECT_LAYER_THREE"]）
    private static Dictionary<string, string[]> _currentTemplateRadioGroups = null;

    // Unity到Laya的属性名映射表（扩展版）
    private static readonly Dictionary<string, string> PropertyNameMappings = new Dictionary<string, string>
    {
        // 基础颜色/纹理
        { "_MainTex", "u_AlbedoTexture" },
        { "_BaseMap", "u_AlbedoTexture" },
        { "_Color", "u_AlbedoColor" },
        { "_BaseColor", "u_AlbedoColor" },
        { "_ColorIntensity", "u_ColorIntensity" },
        { "_Alpha", "u_Alpha" },
        
        // 粒子/Effect专用（参考Unity2Laya_ShaderMapping.md）
        { "_TintColor", "u_TintColor" },           // [HDR] 粒子/特效着色，默认(0.5,0.5,0.5,0.5)配合*2.0公式
        { "_UVScrollTex", "u_UVScrollTex" },       // 主纹理UV滚动速度(xy)
        { "_UVScrollMask", "u_UVScrollMask" },     // Mask纹理UV滚动速度(xy)
        { "_UVScroll", "u_UVScroll" },             // Effect_Basic 系列的 UV 滚动速度（float2）
        { "_LayerColor", "u_LayerColor" },
        { "_LayerMultiplier", "u_LayerMultiplier" },

        // Effect特效纹理层（与 FeatureInferenceRules/TextureDefineMappings 对应）
        { "_DetailTex", "u_DetailTex" },           // 第一层Detail纹理
        { "_DetailTex2", "u_DetailTex2" },         // 第二层Detail纹理
        { "_DistortTex", "u_DistortTex" },         // 扭曲纹理
        // 注：_DistortTex0 通过默认规则转换为 u_DistortTex0，不需要显式映射
        // 切勿映射到 u_DistortTex，否则 GLSL 代码中的 u_DistortTex0 引用会变成未声明标识符
        { "_RimMap", "u_RimMap" },                 // 边缘光贴图
        { "_GradientMap", "u_GradientMap" },       // 渐变贴图
        { "_VertexAmplitudeTex", "u_VertexAmplitudeTex" }, // 顶点振幅纹理

        // float分量型UV滚动（_Scroll0X/Y, _Scroll1X/Y 等，与 UVScrollFloatNamePatterns 对应）
        { "_Scroll0X", "u_Scroll0X" },
        { "_Scroll0Y", "u_Scroll0Y" },
        { "_Scroll1X", "u_Scroll1X" },
        { "_Scroll1Y", "u_Scroll1Y" },
        { "_Scroll2X", "u_Scroll2X" },
        { "_Scroll2Y", "u_Scroll2Y" },

        // 法线
        { "_BumpMap", "u_NormalTexture" },
        { "_NormalMap", "u_NormalTexture" },
        { "_BumpScale", "u_NormalScale" },
        { "_NormalScale", "u_NormalScale" },
        
        // Standard/URP PBR纹理（默认 _X→u_X 规则会生成错误名称）
        { "_MetallicGlossMap", "u_MetallicGlossTexture" },
        { "_OcclusionMap", "u_OcclusionTexture" },
        { "_EmissionMap", "u_EmissionTexture" },
        { "_ParallaxMap", "u_ParallaxTexture" },
        { "_DetailAlbedoMap", "u_DetailAlbedoTexture" },
        { "_DetailNormalMap", "u_DetailNormalTexture" },
        { "_SpecGlossMap", "u_SpecularTexture" },

        // HDRP
        { "_BaseColorMap", "u_AlbedoTexture" },
        { "_EmissiveColor", "u_EmissionColor" },
        { "_EmissiveColorMap", "u_EmissionTexture" },
        { "_MaskMap", "u_MetallicGlossTexture" },

        // PBR属性
        { "_Metallic", "u_Metallic" },
        { "_MetallicRemapMin", "u_MetallicRemapMin" },
        { "_MetallicRemapMax", "u_MetallicRemapMax" },
        { "_Smoothness", "u_Smoothness" },
        { "_SmoothnessRemapMin", "u_SmoothnessRemapMin" },
        { "_SmoothnessRemapMax", "u_SmoothnessRemapMax" },
        { "_OcclusionStrength", "u_OcclusionStrength" },
        { "_MAER", "u_MAER" },
        
        // Alpha测试
        { "_Cutoff", "u_AlphaTestValue" },
        { "_AlphaCutoff", "u_AlphaTestValue" },
        
        // 自发光
        { "_EmissionColor", "u_EmissionColor" },
        { "_EmissionTexture", "u_EmissionTexture" },
        { "_EmissionScale", "u_EmissionIntensity" },

        // Standard shader 浮点
        { "_Parallax", "u_ParallaxScale" },
        { "_DetailNormalMapScale", "u_DetailNormalScale" },
        { "_Glossiness", "u_Smoothness" },
        { "_GlossMapScale", "u_Smoothness" },

        // Skybox
        { "_Tint", "u_TintColor" },

        // Mask贴图
        { "_Mask", "u_Mask" },
        
        // IBL
        { "_IBLMap", "u_IBLMap" },
        { "_IBLMapColor", "u_IBLMapColor" },
        { "_IBLMapIntensity", "u_IBLMapIntensity" },
        { "_IBLMapPower", "u_IBLMapPower" },
        { "_IBLMapRotateX", "u_IBLMapRotateX" },
        { "_IBLMapRotateY", "u_IBLMapRotateY" },
        { "_IBLMapRotateZ", "u_IBLMapRotateZ" },
        
        // Matcap
        { "_MatcapMap", "u_MatcapMap" },
        { "_MatcapAngle", "u_MatcapAngle" },
        { "_MatcapStrength", "u_MatcapStrength" },
        { "_MatcapPow", "u_MatcapPow" },
        { "_MatcapColor", "u_MatcapColor" },
        
        // Matcap Add
        { "_MatcapAddMap", "u_MatcapAddMap" },
        { "_MatcapAddAngle", "u_MatcapAddAngle" },
        { "_MatcapAddStrength", "u_MatcapAddStrength" },
        { "_MatcapAddPow", "u_MatcapAddPow" },
        { "_MatcapAddColor", "u_MatcapAddColor" },
        
        // NPR卡通渲染
        { "_MedColor", "u_MedColor" },
        { "_MedThreshold", "u_MedThreshold" },
        { "_MedSmooth", "u_MedSmooth" },
        { "_ShadowColor", "u_ShadowColor" },
        { "_ShadowThreshold", "u_ShadowThreshold" },
        { "_ShadowSmooth", "u_ShadowSmooth" },
        { "_ReflectColor", "u_ReflectColor" },
        { "_ReflectThreshold", "u_ReflectThreshold" },
        { "_ReflectSmooth", "u_ReflectSmooth" },
        { "_GIIntensity", "u_GIIntensity" },
        
        // 高光
        { "_SpecularHighlights", "u_SpecularHighlights" },
        { "_GGXSpecular", "u_GGXSpecular" },
        { "_SpecularColor", "u_SpecularColor" },
        { "_SpecularIntensity", "u_SpecularIntensity" },
        { "_SpecularLightOffset", "u_SpecularLightOffset" },
        { "_SpecularThreshold", "u_SpecularThreshold" },
        { "_SpecularSmooth", "u_SpecularSmooth" },
        
        // Fresnel
        { "_DirectionalFresnel", "u_DirectionalFresnel" },
        { "_FresnelColor", "u_FresnelColor" },
        { "_fresnelOffset", "u_fresnelOffset" },
        { "_FresnelThreshold", "u_FresnelThreshold" },
        { "_FresnelSmooth", "u_FresnelSmooth" },
        { "_FresnelIntensity", "u_FresnelIntensity" },
        { "_FresnelMetallic", "u_FresnelMetallic" },
        { "_FresnelFit", "u_FresnelFit" },
        
        // Rim边缘光
        { "_RimColor", "u_RimColor" },
        { "_RimPower", "u_RimPower" },
        { "_RimIntensity", "u_RimIntensity" },
        { "_RimStart", "u_RimStart" },
        { "_RimEnd", "u_RimEnd" },
        { "_RimOffset", "u_RimOffset" },
        
        // HSV调整
        { "_AdjustHSV", "u_AdjustHSV" },
        { "_AdjustHue", "u_AdjustHue" },
        { "_AdjustSaturation", "u_AdjustSaturation" },
        { "_AdjustValue", "u_AdjustValue" },
        
        // 对比度
        { "_OriginalColor", "u_OriginalColor" },
        { "_Contrast", "u_Contrast" },
        { "_ContrastScale", "u_ContrastScale" },
        
        // Tonemapping
        { "_u_ToneWeight", "u_ToneWeight" },
        { "_u_WhitePoint", "u_WhitePoint" },
        
        // 自定义光照
        { "_SelfLight", "u_SelfLight" },
        { "_SelfLightDir", "u_SelfLightDir" },
    };

    // ==================== 特定模板shader的属性名覆盖映射 ====================
    // 当通用 PropertyNameMappings 的结果与预转换模板中的实际变量名不符时，使用此覆盖表
    // 键：layaShaderName（由 GenerateLayaShaderName 生成）
    // 值：Unity属性名 → 模板中的精确 Laya 变量名
    private static readonly Dictionary<string, Dictionary<string, string>> TemplatePropertyOverrides
        = new Dictionary<string, Dictionary<string, string>>
    {
        // Effect_AParticleShader: _Color → u_MainColor（通用映射为 u_AlbedoColor）
        {
            "Effect_AParticleShader", new Dictionary<string, string>
            {
                { "_Color",     "u_MainColor" },
                { "_BaseColor", "u_MainColor" },
                { "_TintColor", "u_MainColor" },
            }
        },
        // Sanguo_distort_add: 主纹理 u_texture, 颜色 u_Color, 扭曲纹理 u_distort_tex
        {
            "Sanguo_Sanguo_particle_distort_add", new Dictionary<string, string>
            {
                { "_MainTex",    "u_texture" },
                { "_BaseMap",    "u_texture" },
                { "_Color",      "u_Color" },
                { "_BaseColor",  "u_Color" },
                { "_TintColor",  "u_Color" },
                { "_DistortTex", "u_distort_tex" },
                { "_DistortTex0","u_distort_tex" },
            }
        },
        // Sanguo_distort_alphaBlend: 同上
        {
            "Sanguo_Sanguo_particle_distort_alphaBlend", new Dictionary<string, string>
            {
                { "_MainTex",    "u_texture" },
                { "_BaseMap",    "u_texture" },
                { "_Color",      "u_Color" },
                { "_BaseColor",  "u_Color" },
                { "_TintColor",  "u_Color" },
                { "_DistortTex", "u_distort_tex" },
                { "_DistortTex0","u_distort_tex" },
            }
        },
        // Artist_Effect_FullEffect: 主纹理 u_texture
        {
            "Artist_Effect_Effect_FullEffect", new Dictionary<string, string>
            {
                { "_MainTex", "u_texture" },
                { "_BaseMap", "u_texture" },
                // DistortTex0（带数字后缀）在模板中是 u_DistortTex（不带后缀）
                { "_DistortTex0", "u_DistortTex" },
                { "_DistortStrength0", "u_DistortStrength" },
            }
        },
        // Artist_Effect_MoHuSeSan: Mask贴图小写 u_mask
        {
            "Artist_Effect_Effect_MoHuSeSan", new Dictionary<string, string>
            {
                { "_Mask", "u_mask" },
            }
        },
    };

    // ==================== RadioGroup 枚举属性 → defines 硬编码映射表 ====================
    // 用于特定模板Shader中，枚举型属性（Int/Float）按枚举值导出对应的宏定义
    // key1: layaShaderName，key2: Laya属性名（layaName），value: defines数组（下标即枚举值，0-based）
    // 示例：LayerType=2 → defines["EFFECT_LAYER_THREE"]
    private static readonly Dictionary<string, Dictionary<string, string[]>> RadioGroupDefineMappings
        = new Dictionary<string, Dictionary<string, string[]>>
    {
        {
            "Artist_Effect_Effect_FullEffect", new Dictionary<string, string[]>
            {
                // [KeywordEnum(One, Two, Three)] _LayerType (Int): 0=ONE, 1=TWO, 2=THREE
                { "LayerType", new[] { "EFFECT_LAYER_ONE", "EFFECT_LAYER_TWO", "EFFECT_LAYER_THREE" } },
                // [KeywordEnum(Default, Clamp, Repeat)] _WrapMode (Float): 0=DEFAULT, 1=CLAMP, 2=REPEAT
                { "WrapMode", new[] { "EFFECT_WRAPMODE_DEFAULT", "EFFECT_WRAPMODE_CLAMP", "EFFECT_WRAPMODE_REPEAT" } },
            }
        },
    };

    // ==================== 模板Shader Keyword → Define 映射 ====================
    // Unity keyword（如 _USEDISTORT0_ON）→ 模板Shader define（如 EFFECT_DISTORT）
    // RadioGroup 枚举类的 keyword 已由 RadioGroupDefineMappings 处理，此处只映射 bool 开关型 keyword
    private static readonly Dictionary<string, Dictionary<string, string>> TemplateKeywordMappings
        = new Dictionary<string, Dictionary<string, string>>
    {
        {
            "Artist_Effect_Effect_FullEffect", new Dictionary<string, string>
            {
                { "_USEDISTORT0_ON", "EFFECT_DISTORT" },
                { "_USEDISSOLVE_ON", "EFFECT_DISSOLVE" },
                { "_USEFADEEDGE_ON", "EFFECT_FADE_EDGE" },
                { "_USEDISSOLVEDISTORT_ON", "EFFECT_DISSOLVE_DISTORT" },
                { "_USERIM_ON", "EFFECT_RIM" },
                { "_USERIMMAP_ON", "EFFECT_RIM_MAP" },
                { "_USELIGHTING_ON", "EFFECT_LIGHTING" },
                { "_USEVERTEXOFFSET_ON", "EFFECT_VERTEX_OFFSET" },
                { "_USEPOLAR_ON", "EFFECT_POLAR" },
                { "_USEGRADIENTMAP0_ON", "EFFECT_GRADIENT_MAP" },
                { "_USENORMALMAPFORRIM_ON", "EFFECT_NORMAL_MAP_RIM" },
                { "_ROTATIONTEX_ON", "EFFECT_ROTATION" },
                { "_ROTATIONTEXTWO_ON", "EFFECT_ROTATION_TWO" },
                { "_ROTATIONTEXTHREE_ON", "EFFECT_ROTATION_THREE" },
                { "_ROTATIONTEXFOUR_ON", "EFFECT_ROTATION_FOUR" },
            }
        },
    };

    // 当前模板shader的 Keyword 映射（ExportMaterialFile 开始时设置）
    private static Dictionary<string, string> _currentTemplateKeywordMappings = null;

    // ==================== 模板Shader Float→Vector2 组合规则 ====================
    // 将两个 Unity Float 属性合并为一个 Laya Vector2 属性
    // 适用于模板shader中 Vector2 类型的 uniform（如 u_Scroll0），对应 Unity 中分开的 X/Y float
    private struct Vector2AssemblyRule
    {
        public string xProp;    // Unity float 属性名（X分量）
        public string yProp;    // Unity float 属性名（Y分量）
        public string layaName; // Laya Vector2 属性名
    }

    private static readonly Dictionary<string, Vector2AssemblyRule[]> TemplateVector2Assemblies
        = new Dictionary<string, Vector2AssemblyRule[]>
    {
        {
            "Artist_Effect_Effect_FullEffect", new Vector2AssemblyRule[]
            {
                new Vector2AssemblyRule { xProp = "_Scroll0X", yProp = "_Scroll0Y", layaName = "u_Scroll0" },
                new Vector2AssemblyRule { xProp = "_Scroll1X", yProp = "_Scroll1Y", layaName = "u_Scroll1" },
                new Vector2AssemblyRule { xProp = "_Scroll2X", yProp = "_Scroll2Y", layaName = "u_Scroll2" },
                new Vector2AssemblyRule { xProp = "_Distort0X", yProp = "_Distort0Y", layaName = "u_DistortScroll" },
                new Vector2AssemblyRule { xProp = "_RotateCenterX", yProp = "_RotateCenterY", layaName = "u_RotateCenter" },
                new Vector2AssemblyRule { xProp = "_TranslationX", yProp = "_TranslationY", layaName = "u_Translation" },
                new Vector2AssemblyRule { xProp = "_RotateCenterX02", yProp = "_RotateCenterY02", layaName = "u_RotateCenter02" },
                new Vector2AssemblyRule { xProp = "_TranslationX02", yProp = "_TranslationY02", layaName = "u_Translation02" },
                new Vector2AssemblyRule { xProp = "_RotateCenterX03", yProp = "_RotateCenterY03", layaName = "u_RotateCenter03" },
                new Vector2AssemblyRule { xProp = "_TranslationX03", yProp = "_TranslationY03", layaName = "u_Translation03" },
                new Vector2AssemblyRule { xProp = "_RotateCenterX04", yProp = "_RotateCenterY04", layaName = "u_RotateCenter04" },
                new Vector2AssemblyRule { xProp = "_TranslationX04", yProp = "_TranslationY04", layaName = "u_Translation04" },
                new Vector2AssemblyRule { xProp = "_DissolveDistortX", yProp = "_DissolveDistortY", layaName = "u_DissolveDistortScroll" },
                new Vector2AssemblyRule { xProp = "_VertexAmplitudeTexScroll0X", yProp = "_VertexAmplitudeTexScroll0Y", layaName = "u_VertexAmplitudeTexScroll" },
            }
        },
    };

    // ⭐ UV滚动向量型属性的Laya名称集合（从PropertyNameMappings派生，键含"UVScroll"的条目）
    // 可配置：向PropertyNameMappings添加含"UVScroll"键名的条目即可自动扩展此集合
    // 对应 builtin_unity_to_laya.json → particle_mappings.effect_behaviors.uv_scroll_vector
    private static readonly HashSet<string> UVScrollVectorLayaNames = new HashSet<string>(
        PropertyNameMappings
            .Where(kv => kv.Key.Contains("UVScroll"))
            .Select(kv => kv.Value)
    );

    // ⭐ float分量型UV滚动属性名匹配模式（兼容 _Scroll0X/_Scroll0Y 等系列）
    // 可配置：向此数组添加模式字符串即可扩展
    // 对应 builtin_unity_to_laya.json → particle_mappings.effect_behaviors.uv_scroll_float.name_patterns
    private static readonly string[] UVScrollFloatNamePatterns = { "Scroll" };

    // ==================== 2D/UI Shader 专用映射 ====================
    // 2D内置属性列表（不导出到uniformMap，由引擎自动提供）
    // 对应 builtin_unity_to_laya.json → ui2d_mappings.skip_properties + builtin_sampler
    private static readonly HashSet<string> UI2DBuiltinProperties = new HashSet<string>
    {
        "_MainTex", "_MainTex_ST",
        "_StencilComp", "_Stencil", "_StencilOp", "_StencilWriteMask", "_StencilReadMask",
        "_ColorMask", "_UseUIAlphaClip"
    };

    // 2D属性名映射（覆盖通用的 PropertyNameMappings，2D shader 使用不同的 uniform 命名）
    // 对应 builtin_unity_to_laya.json → ui2d_mappings.variables
    private static readonly Dictionary<string, string> UI2DPropertyNameMappings = new Dictionary<string, string>
    {
        { "_TintColor", "u_TintColor" },
        { "_Color", "u_TintColor" },
        { "_BaseColor", "u_TintColor" },
    };

    /// <summary>
    /// 检查是否是2D内置属性（不需要导出到uniformMap）
    /// </summary>
    private static bool Is2DBuiltinProperty(string unityName)
    {
        return UI2DBuiltinProperties.Contains(unityName);
    }

    /// <summary>
    /// 获取2D shader的属性名映射（优先使用2D映射，再fallback到通用映射）
    /// </summary>
    private static string Get2DPropertyName(string unityName)
    {
        if (UI2DPropertyNameMappings.TryGetValue(unityName, out string layaName))
            return layaName;
        if (PropertyNameMappings.TryGetValue(unityName, out string generalName))
            return generalName;
        // 默认映射: _PropertyName → u_PropertyName
        return "u_" + unityName.TrimStart('_');
    }

    // ⭐ 属性名→GLSL宏定义的推断规则（替代 InferDefinesFromProperties 中的硬编码 if/else 链）
    // 可配置：增删此数组条目来控制 shader feature define 的自动推断，无需修改推断逻辑
    // 对应 builtin_unity_to_laya.json → shader_feature_inferences 文档节
    private struct FeatureInferenceRule
    {
        public string[] patterns;  // 命中条件：属性名(小写)包含其中任意一个字符串
        public string exclude;     // 排除条件：属性名包含此字符串时跳过（null=无排除）
        public string[] defines;   // 触发时添加的 GLSL #define 列表
    }

    private static readonly FeatureInferenceRule[] FeatureInferenceRules =
    {
        // Layer纹理叠加层数（detailtex2 必须在 detailtex 之前，exclude 保证互斥）
        new FeatureInferenceRule { patterns = new[]{"detailtex2"}, exclude = null,        defines = new[]{"LAYERTYPE_THREE","LAYERTYPE_TWO","LAYERTYPE_ONE"} },
        new FeatureInferenceRule { patterns = new[]{"detailtex"},  exclude = "detailtex2", defines = new[]{"LAYERTYPE_TWO","LAYERTYPE_ONE"} },

        // Dissolve（溶解）
        new FeatureInferenceRule { patterns = new[]{"dissolve"},        exclude = null,     defines = new[]{"USEDISSOLVE"} },
        new FeatureInferenceRule { patterns = new[]{"fadeedge"},        exclude = null,     defines = new[]{"USEFADEEDGE"} },
        new FeatureInferenceRule { patterns = new[]{"dissolvedistort"}, exclude = null,     defines = new[]{"USEDISSOLVEDISTORT"} },

        // Distort（扭曲，排除 dissolvedistort 避免误触发）
        new FeatureInferenceRule { patterns = new[]{"distorttex"},      exclude = "dissolve", defines = new[]{"USEDISTORT0"} },

        // Rim（边缘光）
        new FeatureInferenceRule { patterns = new[]{"rim"},             exclude = null,     defines = new[]{"USERIM"} },
        new FeatureInferenceRule { patterns = new[]{"rimmap"},          exclude = "mask",   defines = new[]{"USERIMMAP"} },

        // 光照
        new FeatureInferenceRule { patterns = new[]{"effectmainlight","lighting"},          exclude = null, defines = new[]{"USELIGHTING"} },

        // 顶点位移
        new FeatureInferenceRule { patterns = new[]{"vertexoffset","vertexamplitude"},      exclude = null, defines = new[]{"USEVERTEXOFFSET"} },

        // 极坐标
        new FeatureInferenceRule { patterns = new[]{"polar"},           exclude = null,     defines = new[]{"USEPOLAR"} },

        // 渐变贴图
        new FeatureInferenceRule { patterns = new[]{"gradientmap"},     exclude = null,     defines = new[]{"USEGRADIENTMAP0"} },

        // NormalMap（用于Rim光照，单独属性如 _NormalTexture / _RimNormalMap 均命中）
        new FeatureInferenceRule { patterns = new[]{"normaltexture"},   exclude = null,     defines = new[]{"USENORMALMAPFORRIM"} },

        // CustomData（Unity自定义粒子数据通道）
        new FeatureInferenceRule { patterns = new[]{"customdata"},      exclude = null,     defines = new[]{"USECUSTOMDATA"} },

        // WrapMode（纹理环绕模式选择）
        new FeatureInferenceRule { patterns = new[]{"wrapmode"},        exclude = null,     defines = new[]{"WRAPMODE_CLAMP","WRAPMODE_REPEAT"} },

        // NPR（非真实感渲染）
        new FeatureInferenceRule { patterns = new[]{"medcolor","shadowthreshold"}, exclude = null, defines = new[]{"USENPR"} },

        // 自发光
        new FeatureInferenceRule { patterns = new[]{"emission"},        exclude = null,     defines = new[]{"EMISSION"} },

        // 漩涡效果（AParticleShader 有预转换模板故规则不实际生效，为完备性添加）
        new FeatureInferenceRule { patterns = new[]{"swirl"},           exclude = null,     defines = new[]{"SWIRL"} },

        // USE_MASK 开关
        new FeatureInferenceRule { patterns = new[]{"use_mask","usemask"}, exclude = null,  defines = new[]{"USE_MASK"} },
    };

    // Unity到Laya的纹理宏定义映射表
    // 说明：只有与默认规则（PROPNAME.ToUpper()）不同，或需要明确指定的条目才需要在此注册
    // 默认规则：_PropName → PROPNAME（结尾含TEX/MAP/TEXTURE则不加后缀，否则加MAP后缀）
    // ⚠️ 特别注意：含数字后缀的属性（如_DetailTex2）默认规则会产生错误（DETAILTEX2MAP），必须在此显式注册
    private static readonly Dictionary<string, string> TextureDefineMappings = new Dictionary<string, string>
    {
        // 基础贴图（默认规则结果不符合Laya约定，需要覆盖）
        { "_MainTex", "ALBEDOTEXTURE" },
        { "_BaseMap", "ALBEDOTEXTURE" },
        { "_BumpMap", "NORMALTEXTURE" },
        { "_NormalMap", "NORMALTEXTURE" },
        { "_MAER", "MAERMAP" },
        { "_Mask", "MASKMAP" },
        { "_IBLMap", "IBLMAP" },
        { "_MatcapMap", "MATCAPMAP" },
        { "_MatcapAddMap", "MATCAPADDMAP" },
        { "_EmissionTexture", "EMISSIONTEXTURE" },

        // Standard/URP PBR纹理宏（默认规则生成的宏名不符合 Laya 约定）
        { "_MetallicGlossMap", "METALLICGLOSSTEXTURE" },
        { "_OcclusionMap", "OCCLUSIONTEXTURE" },
        { "_EmissionMap", "EMISSIONTEXTURE" },
        { "_ParallaxMap", "PARALLAXTEXTURE" },
        { "_DetailAlbedoMap", "DETAILTEXTURE" },
        { "_DetailNormalMap", "DETAILNORMAL" },
        { "_SpecGlossMap", "SPECULARMAP" },
        { "_BaseColorMap", "ALBEDOTEXTURE" },
        { "_MaskMap", "METALLICGLOSSTEXTURE" },
        { "_EmissiveColorMap", "EMISSIONTEXTURE" },

        // 显式注册（默认规则恰好正确，但保证一致性）
        { "_AlbedoTexture", "ALBEDOTEXTURE" },
        { "_NormalTexture", "NORMALTEXTURE" },

        // Effect特效纹理（与 FeatureInferenceRules 的 LAYERTYPE_* 等 defines 对应）
        // _DetailTex2 ⚠️ 必须显式注册：默认规则会生成 DETAILTEX2MAP（错误），正确应为 DETAILTEX2
        { "_DetailTex2", "DETAILTEX2" },
        // 以下条目默认规则也能正确生成，此处显式注册仅为提高可读性和配置一致性
        { "_DetailTex", "DETAILTEX" },
        { "_DistortTex", "DISTORTTEX" },
        // _DistortTex0 ⚠️：数字后缀导致默认规则生成 DISTORTTEX0MAP（错误），必须显式注册
        { "_DistortTex0", "DISTORTTEX0" },
        { "_RimMap", "RIMMAP" },
        { "_GradientMap", "GRADIENTMAP" },
        { "_VertexAmplitudeTex", "VERTEXAMPLITUDETEX" },
    };

    /// <summary>
    /// 清除导出缓存（每次导出开始时调用）
    /// </summary>
    public static void ClearCache()
    {
        exportedShaders.Clear();
        // Project mappings are editable assets. Reload them for every export instead
        // of keeping a stale static mapping engine for the whole Unity domain.
        mappingEngine = null;
        useMappingTableMode = false;
        mappingEngineInitialized = false;
    }

    /// <summary>
    /// 确保指定的 LayaAir shader 文件已被导出到 resource map。
    /// 供内置粒子材质路径（WriteParticleMaterial / WriteBuiltinParticleMaterial）调用，
    /// 这两个路径不走 WriteAutoCustomShaderMaterial，所以需要手动触发 shader 文件导出。
    /// </summary>
    public static void EnsureLayaShaderExported(string layaShaderName, ResoureMap resoureMap)
    {
        if (resoureMap == null) return;
        string outputPath = "Shaders/" + layaShaderName + ".shader";
        if (resoureMap.HaveFileData(outputPath)) return; // 已存在，跳过

        string template = TryLoadPreConvertedTemplate(layaShaderName);
        if (template == null)
        {
            ExportLogger.Warning($"LayaAir3D: No pre-converted template for particle shader '{layaShaderName}', shader file will not be exported.");
            return;
        }
        ShaderFile shaderFile = new ShaderFile(outputPath, template);
        resoureMap.AddExportFile(shaderFile);
        ExportLogger.Log($"LayaAir3D: Auto-exported particle shader template: {outputPath}");
    }

    /// <summary>
    /// 根据 MaterialFile 的 renderer 使用类型，返回 shader 名称后缀。
    /// MeshRenderer → _D3, ParticleSystem → _Effect, 2D/无后缀, 未知/无后缀。
    /// </summary>
    private static string GetShaderTypeSuffix(MaterialFile materialFile)
    {
        if (materialFile == null) return "";
        if (materialFile.IsUsedBy2DComponent()) return ""; // 2D 已有独立类型处理
        if (materialFile.IsCPUParticle()) return "_D3"; // CPU粒子走Mesh管线，使用D3类型
        if (materialFile.IsUsedByParticleSystem()) return "_Effect";
        if (materialFile.IsUsedByMeshRenderer()) return "_D3";
        return "";
    }

    /// <summary>
    /// 自动导出自定义Shader材质
    /// </summary>
    public static void WriteAutoCustomShaderMaterial(Material material, JSONObject jsonData, ResoureMap resoureMap, MaterialFile materialFile = null)
    {
        if (material == null)
        {
            Debug.LogError("LayaAir3D: Invalid material for custom shader export");
            return;
        }

        Shader shader = material.shader;
        string shaderName = shader.name;

        // 生成LayaAir Shader名称（去除路径分隔符，转换为合法名称）
        string baseLayaShaderName = GenerateLayaShaderName(shaderName);

        // ★ 根据 renderer 类型和模板存在性决定最终 shader 名称
        string typeSuffix = GetShaderTypeSuffix(materialFile);
        string layaShaderName = baseLayaShaderName + typeSuffix;

        // 模板优先命名：如果带后缀的精确模板不存在，查找对应命名约定的模板
        if (typeSuffix.Length > 0 && TryLoadPreConvertedTemplate(layaShaderName) == null)
        {
            if (typeSuffix == "_D3")
            {
                // MeshRenderer：查找 Mesh_ 前缀模板
                string meshName = "Mesh_" + baseLayaShaderName;
                if (TryLoadPreConvertedTemplate(meshName) != null)
                {
                    layaShaderName = meshName;
                    ExportLogger.Log($"LayaAir3D: Using Mesh template '{meshName}' for D3 variant");
                }
            }
            else if (typeSuffix == "_Effect")
            {
                // ParticleRenderer：查找 Particle_ 前缀模板（与 D3 查找 Mesh_ 一致）
                string particleName = "Particle_" + baseLayaShaderName;
                if (TryLoadPreConvertedTemplate(particleName) != null)
                {
                    layaShaderName = particleName;
                    ExportLogger.Log($"LayaAir3D: Using Particle template '{particleName}' for Effect variant");
                }
                else if (TryLoadPreConvertedTemplate(baseLayaShaderName) != null)
                {
                    layaShaderName = baseLayaShaderName;
                    ExportLogger.Log($"LayaAir3D: Using base template '{baseLayaShaderName}' for Effect variant");
                }
            }
        }

        ExportLogger.Log($"LayaAir3D: Exporting custom shader material: {material.name} (Shader: {shaderName} -> {layaShaderName})");

        // 导出Shader文件（如果还没导出过）
        string cacheKey = shaderName + "|" + layaShaderName;
        if (!exportedShaders.Contains(cacheKey))
        {
            ExportShaderFile(shader, layaShaderName, resoureMap, materialFile, baseLayaShaderName);
            exportedShaders.Add(cacheKey);
        }

        // 导出材质文件 (⭐ 传递materialFile以支持粒子Mesh模式检测)
        ExportMaterialFile(material, shader, layaShaderName, jsonData, resoureMap, materialFile, baseLayaShaderName);
    }

    /// <summary>
    /// 生成LayaAir Shader名称
    /// </summary>
    private static string GenerateLayaShaderName(string unityShaderName)
    {
        // 移除路径分隔符，转换为合法名称
        string name = unityShaderName.Replace("/", "_").Replace(" ", "_").Replace("-", "_");
        // 移除特殊字符
        name = Regex.Replace(name, @"[^a-zA-Z0-9_]", "");
        return name;
    }

    /// <summary>
    /// 导出Shader文件
    /// </summary>
    private static void ExportShaderFile(Shader shader, string layaShaderName,
        ResoureMap resoureMap, MaterialFile materialFile = null,
        string baseLayaShaderName = null)
    {
        // 收集Shader属性
        List<ShaderProperty> properties = CollectShaderProperties(shader);

        // ⭐ 优先检查是否有预转换的GLSL模板文件（template/{layaShaderName}.shader）
        // 预转换模板用于复杂的自定义shader（如FishStandard_Base），这些shader的HLSL代码
        // 包含BRDFData等Unity SRP专有结构体，无法被通用HLSL→GLSL转换器正确处理
        string preConvertedTemplate = TryLoadPreConvertedTemplate(layaShaderName);
        // ★ Fallback 查找链（当精确名称的模板不存在时）
        if (preConvertedTemplate == null && baseLayaShaderName != null && baseLayaShaderName != layaShaderName)
        {
            // Fallback 1: D3 变体尝试 "Mesh_" 前缀命名约定
            // 例如: Artist_Effect_Effect_FullEffect_D3 → Mesh_Artist_Effect_Effect_FullEffect
            if (layaShaderName.EndsWith("_D3"))
            {
                preConvertedTemplate = TryLoadPreConvertedTemplate("Mesh_" + baseLayaShaderName);
            }

            // Fallback 1b: Effect 变体尝试 "Particle_" 前缀命名约定
            // 例如: Artist_Effect_Effect_FullEffect_Effect → Particle_Artist_Effect_Effect_FullEffect
            if (preConvertedTemplate == null && layaShaderName.EndsWith("_Effect"))
            {
                preConvertedTemplate = TryLoadPreConvertedTemplate("Particle_" + baseLayaShaderName);
            }

            // Fallback 2: 尝试同类型的原始名模板（仅当模板 shaderType 与目标一致时才安全使用）
            // 例如: Effect 模板不能用于 D3 变体（attributeMap/VS 结构完全不同）
            if (preConvertedTemplate == null)
            {
                string baseTemplate = TryLoadPreConvertedTemplate(baseLayaShaderName);
                if (baseTemplate != null)
                {
                    // 检查模板的 shaderType 是否与目标类型兼容
                    string targetShaderType = layaShaderName.EndsWith("_D3") ? "D3" : layaShaderName.EndsWith("_Effect") ? "Effect" : null;
                    bool isCompatible = true;
                    if (targetShaderType != null)
                    {
                        var typeMatch = Regex.Match(baseTemplate, @"shaderType:\s*(\w+)");
                        if (typeMatch.Success && typeMatch.Groups[1].Value != targetShaderType)
                        {
                            // 类型不兼容（如 Effect 模板用于 D3 变体），跳过模板，交给代码生成
                            isCompatible = false;
                            ExportLogger.Log($"LayaAir3D: Skipping incompatible base template for '{layaShaderName}' (template is {typeMatch.Groups[1].Value}, need {targetShaderType})");
                        }
                    }

                    if (isCompatible)
                        preConvertedTemplate = baseTemplate;
                }
            }

            // 对找到的 fallback 模板进行名称替换
            if (preConvertedTemplate != null)
            {
                // 从模板的 name: 字段提取实际 shader 名称（可能与 baseLayaShaderName 不同）
                // 例如 Mesh_Artist_Effect_Effect_FullEffect.shader 内部 name 是 "Mesh_FullEffect"
                string templateShaderName = baseLayaShaderName;
                var nameMatch = Regex.Match(preConvertedTemplate, @"name:\s*""?(\w+)""?");
                if (nameMatch.Success)
                    templateShaderName = nameMatch.Groups[1].Value;

                preConvertedTemplate = preConvertedTemplate.Replace(templateShaderName + "VS", layaShaderName + "VS");
                preConvertedTemplate = preConvertedTemplate.Replace(templateShaderName + "FS", layaShaderName + "FS");
                // 兼容 name:"xxx" 和 name:xxx 两种格式
                preConvertedTemplate = Regex.Replace(preConvertedTemplate,
                    @"name:\s*""?" + Regex.Escape(templateShaderName) + @"""?",
                    "name:" + layaShaderName);
                preConvertedTemplate = preConvertedTemplate.Replace("SHADER_NAME " + templateShaderName, "SHADER_NAME " + layaShaderName);

                // 确保 shaderType 与目标类型一致
                string finalTargetType = layaShaderName.EndsWith("_D3") ? "D3" : layaShaderName.EndsWith("_Effect") ? "Effect" : null;
                if (finalTargetType != null)
                {
                    preConvertedTemplate = Regex.Replace(preConvertedTemplate,
                        @"shaderType:\s*(D3|Effect|Default|None|Sky|PostProcess|D2_BaseRenderNode2D|D2_TextureSV|D2_primitive)",
                        "shaderType:" + finalTargetType);
                }
            }
        }

        // 尝试读取Unity Shader源代码
        string shaderPath = AssetDatabase.GetAssetPath(shader);
        string shaderSourceCode = null;

        if (!string.IsNullOrEmpty(shaderPath) && File.Exists(shaderPath))
        {
            shaderSourceCode = File.ReadAllText(shaderPath);
            ExportLogger.Log($"LayaAir3D: Read shader source from: {shaderPath}");
        }

        // 生成Shader文件内容
        string shaderContent;
        if (preConvertedTemplate != null)
        {
            // 使用预转换的GLSL模板文件（已手动优化，直接使用）
            shaderContent = preConvertedTemplate;
            ExportLogger.Log($"LayaAir3D: Using pre-converted GLSL template for shader: {layaShaderName}");
        }
        else if (!string.IsNullOrEmpty(shaderSourceCode))
        {
            // 有源代码，进行HLSL到GLSL的转换（ConvertUnityShaderToLaya内部已调用FormatShaderContent）
            shaderContent = ConvertUnityShaderToLaya(layaShaderName, properties, shader.name, shaderSourceCode, materialFile);
        }
        else
        {
            // 没有源代码，使用模板生成
            shaderContent = GenerateShaderFileContent(layaShaderName, properties, shader.name, materialFile);
            // 格式化模板生成的shader内容
            shaderContent = FormatShaderContent(shaderContent);
        }

        // ⭐ 注入Unity URP结构体定义（BRDFData/InputData等）
        shaderContent = InjectURPStructDefinitions(shaderContent);

        // ⭐ 修复FS中对varying变量的赋值（GLSL中varying在FS是只读的）
        shaderContent = FixFragmentVaryingLValueAssignment(shaderContent);

        // ⭐ 修复vec3*vec4类型不匹配（GLSL不支持HLSL的隐式截断）
        shaderContent = FixVec3TimesVec4Operations(shaderContent);

        // ⭐ 修复GLSL类型不匹配问题 (v_Texcoord0的vec4/vec2转换)
        shaderContent = FixShaderTypeMismatch(shaderContent);

        // ⭐ 全面的类型检查和自动修复 (检测所有赋值中的类型不匹配)
        shaderContent = ComprehensiveTypeCheck(shaderContent);

        // ⭐ 修复 v_MeshColor 单通道 → 完整 vec4 乘法
        shaderContent = FixMeshColorSingleChannel(shaderContent);

        // ⭐ 验证shader内容，检测潜在的类型不匹配问题
        ValidateShaderContent(shaderContent, layaShaderName);

        // ⭐ 警告 FS 中的调试 early-return
        WarnDebugEarlyReturn(shaderContent, layaShaderName);

        // ⭐ 修复 { discard; }; 尾部分号导致的 dangling else 问题
        shaderContent = FixDanglingElseSemicolon(shaderContent);

        // ⭐ 修复 } else { 大括号缩进问题（防止模板或转换器生成格式不一致的代码）
        shaderContent = FixBraceElseFormatting(shaderContent);

        // ⭐ 警告 VS 中同一 varying 被多次赋值
        WarnDuplicateVaryingAssignments(shaderContent, layaShaderName);

        // 创建Shader文件 - 放置在Shaders文件夹中
        string outputPath = "Shaders/" + layaShaderName + ".shader";
        ShaderFile shaderFile = new ShaderFile(outputPath, shaderContent);
        resoureMap.AddExportFile(shaderFile);

        ExportLogger.Log($"LayaAir3D: Generated shader file: {outputPath} (ShaderType detected from: {shader.name})");
    }

    /// <summary>
    /// 尝试加载预转换的 GLSL 模板。项目模板优先于插件内置模板。
    /// 项目模板必须放在 Assets/**/Editor/LayaAir/ShaderTemplates/ 下。
    /// 预转换模板用于含BRDFData等Unity SRP专有结构体的复杂自定义shader。
    /// 返回null表示没有找到对应模板，调用方应退回到HLSL转换流程
    /// </summary>
    private static string FindUniqueProjectAssetPath(string searchFilter, string requiredSuffix, string displayName)
    {
        string[] guids = AssetDatabase.FindAssets(searchFilter, new[] { "Assets" });
        var matchingPaths = new List<string>();
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (assetPath.EndsWith(requiredSuffix, System.StringComparison.OrdinalIgnoreCase) &&
                !matchingPaths.Contains(assetPath))
            {
                matchingPaths.Add(assetPath);
            }
        }

        if (matchingPaths.Count == 0)
            return null;

        matchingPaths.Sort(System.StringComparer.OrdinalIgnoreCase);
        if (matchingPaths.Count > 1)
        {
            Util.FileUtil.setStatuse(false);
            Debug.LogError(
                $"LayaAir3D: Multiple project {displayName} files were found. " +
                $"Keep only one Assets/**{requiredSuffix}: " +
                string.Join(", ", matchingPaths.ToArray()));
            return null;
        }

        return matchingPaths[0];
    }

    private static string TryLoadPreConvertedTemplate(string shaderName)
    {
        try
        {
            string expectedAssetName = shaderName + ".shader.txt";

            // Project-owned templates are explicitly scoped and override package templates.
            string projectTemplateSuffix = ProjectShaderTemplatesAssetDirectory + expectedAssetName;
            string projectTemplateAssetPath = FindUniqueProjectAssetPath(
                shaderName,
                projectTemplateSuffix,
                $"shader template '{shaderName}'");
            if (!string.IsNullOrEmpty(projectTemplateAssetPath))
            {
                TextAsset projectTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(projectTemplateAssetPath);
                if (projectTextAsset != null)
                {
                    string normalizedProjectText = projectTextAsset.text.Replace("\uFEFF", "").TrimStart();
                    if (normalizedProjectText.StartsWith("Shader3D Start"))
                    {
                        ExportLogger.Log(
                            $"LayaAir3D: Found project GLSL template for '{shaderName}' at: {projectTemplateAssetPath}");
                        return normalizedProjectText;
                    }
                }

                Debug.LogWarning(
                    $"LayaAir3D: Project shader template is not a valid Laya Shader3D template: {projectTemplateAssetPath}");
            }

            // AssetDatabase can read text assets from virtual UPM package paths even when
            // the package has no physical directory under the project's Packages folder.
            // This exporter is distributed as com.layaair.export-tool. Loading the
            // package-relative asset path works for embedded, registry and file: UPM
            // packages, while keeping the Laya shader hidden from Unity's shader importer.
            string packageAssetPath = "Packages/com.layaair.export-tool/Editor/Mappings/ShaderTemplates/" + expectedAssetName;
            TextAsset packageTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packageAssetPath);
            if (packageTextAsset != null)
            {
                string normalizedPackageText = packageTextAsset.text.Replace("\uFEFF", "").TrimStart();
                if (normalizedPackageText.StartsWith("Shader3D Start"))
                {
                    ExportLogger.Log($"LayaAir3D: Found package GLSL template for '{shaderName}' at: {packageAssetPath}");
                    return normalizedPackageText;
                }
            }

        }
        catch (System.Exception)
        {
            // Continue with direct file-system lookup below.
        }

        var possiblePaths = new List<string>();
        string[] fileNames = new string[]
        {
            shaderName + ".shader",
            // Use a text suffix for package templates so Unity does not try to compile
            // LayaAir Shader3D syntax as a Unity shader asset.
            shaderName + ".shader.txt",
        };

        string[] legacyRoots = new string[]
        {
            // 搜索 Editor/Mappings/ShaderTemplates/ 目录（内置预转换GLSL模板库）
            Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/Editor/Mappings/ShaderTemplates"),
            Path.Combine(Application.dataPath.Replace("/Assets", ""), "Assets/LayaAir3.0UnityPlugin/Editor/Mappings/ShaderTemplates"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets/LayaAir3.0UnityPlugin/Editor/Mappings/ShaderTemplates"),
        };

        foreach (string root in legacyRoots)
            foreach (string fileName in fileNames)
                possiblePaths.Add(Path.Combine(root, fileName));

        // Local/file UPM packages do not physically exist under the project's Packages
        // directory. Resolve the package installation path from its virtual asset path so
        // bundled templates work for both embedded and file: package installations.
        try
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                "Packages/com.layaair.export-tool/Editor/Export/CustomShaderExporter.cs");
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                string packageTemplateRoot = Path.Combine(packageInfo.resolvedPath, "Editor/Mappings/ShaderTemplates");
                foreach (string fileName in fileNames)
                    possiblePaths.Add(Path.Combine(packageTemplateRoot, fileName));
            }
        }
        catch (System.Exception)
        {
            // Older Unity versions may not expose package information for embedded assemblies.
        }

        foreach (var path in possiblePaths)
        {
            if (!File.Exists(path)) continue;

            string content = File.ReadAllText(path);
            // 只使用以 "Shader3D Start" 开头的预转换GLSL文件，排除Unity原始HLSL（以 "Shader " 开头）
            // 注意：部分文件带有UTF-8 BOM（\uFEFF），TrimStart()不会自动去除，需要显式处理
            string normalized = content.Replace("\uFEFF", "").TrimStart();
            if (normalized.StartsWith("Shader3D Start"))
            {
                ExportLogger.Log($"LayaAir3D: Found pre-converted GLSL template for '{shaderName}' at: {path}");
                return normalized; // 返回已去除BOM的内容
            }
        }

        return null;
    }

    /// <summary>
    /// 从模板shader内容中解析 uniformMap 的所有变量名（保留原始大小写）
    /// uniformMap 格式示例：
    ///   uniformMap: {
    ///     u_texture: { type: Texture2D, ... },
    ///     u_Color: { type: Color, ... },
    ///   }
    /// </summary>
    private static HashSet<string> ParseTemplateUniformMapNames(string templateContent)
    {
        var result = new HashSet<string>();
        if (string.IsNullOrEmpty(templateContent)) return result;

        // 找到 uniformMap: 位置
        int start = templateContent.IndexOf("uniformMap:", System.StringComparison.Ordinal);
        if (start < 0) return result;

        // 找到 uniformMap 块的起始 {
        int braceStart = templateContent.IndexOf('{', start);
        if (braceStart < 0) return result;

        // 找到匹配的结束 }（处理嵌套大括号）
        int depth = 0;
        int braceEnd = -1;
        for (int i = braceStart; i < templateContent.Length; i++)
        {
            if (templateContent[i] == '{') depth++;
            else if (templateContent[i] == '}')
            {
                depth--;
                if (depth == 0) { braceEnd = i; break; }
            }
        }
        if (braceEnd < 0) return result;

        string block = templateContent.Substring(braceStart + 1, braceEnd - braceStart - 1);

        // 提取属性名：每行中「标识符 :」格式（即 uniformMap 的 key）
        var matches = Regex.Matches(block, @"^\s*(\w+)\s*:", RegexOptions.Multiline);
        foreach (Match m in matches)
            result.Add(m.Groups[1].Value);

        return result;
    }

    /// <summary>
    /// 从模板shader内容中解析所有 RadioGroup Int 属性及其 options.members。
    /// 格式示例（单行）：
    ///   LayerType: { type: "Int", ..., inspector: "RadioGroup", options: { members: ["EFFECT_LAYER_ONE", "EFFECT_LAYER_TWO", "EFFECT_LAYER_THREE"] } },
    /// 返回：key = Laya变量名，value = members数组（index对应Int值）
    /// </summary>
    private static Dictionary<string, string[]> ParseTemplateRadioGroups(string templateContent)
    {
        var result = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(templateContent)) return result;

        // 每个 RadioGroup 属性在一行内，匹配：varName: { ... inspector: "RadioGroup" ... members: ["A","B","C"] ... }
        var pattern = new Regex(
            @"(\w+)\s*:\s*\{[^}]*inspector\s*:\s*""RadioGroup""[^}]*members\s*:\s*\[([^\]]+)\]",
            RegexOptions.None
        );

        foreach (Match m in pattern.Matches(templateContent))
        {
            string varName = m.Groups[1].Value;
            string[] members = m.Groups[2].Value
                .Split(',')
                .Select(s => s.Trim().Trim('"'))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (members.Length > 0)
                result[varName] = members;
        }

        return result;
    }

    #region HLSL to GLSL Converter

    /// <summary>
    /// 判断shader是否是粒子系统shader（而非mesh shader）
    /// </summary>
    private static bool IsParticleShader(LayaMaterialType materialType, string unityShaderName, string sourceCode, MaterialFile materialFile = null)
    {
        // ⭐ 优先级1: 检查实际的渲染器组件类型（最准确的方法）
        if (materialFile != null)
        {
            bool usedByParticle = materialFile.IsUsedByParticleSystem();
            bool usedByMesh = materialFile.IsUsedByMeshRenderer();

            ExportLogger.Log($"LayaAir3D: Checking renderer usage for '{unityShaderName}' - UsedByParticle: {usedByParticle}, UsedByMesh: {usedByMesh}");

            // ⭐ 关键：只要被ParticleSystemRenderer使用，就判定为粒子shader
            // 即使同时被MeshRenderer使用，粒子系统优先（因为粒子系统的Mesh渲染模式）
            if (usedByParticle)
            {
                ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (Used by ParticleSystemRenderer): {unityShaderName}");
                return true;
            }

            // 明确只被MeshRenderer使用
            if (usedByMesh && !usedByParticle)
            {
                ExportLogger.Log($"LayaAir3D: Detected as MeshShader (Only used by MeshRenderer/SkinnedMeshRenderer): {unityShaderName}");
                return false;
            }

            // 如果没有任何renderer使用信息（usedByParticle和usedByMesh都为false）
            // 继续使用后续的启发式检测
            ExportLogger.Log($"LayaAir3D: No renderer usage info found, using heuristic detection for: {unityShaderName}");
        }
        else
        {
            ExportLogger.Log($"LayaAir3D: MaterialFile is null, using heuristic detection for: {unityShaderName}");
        }

        // 优先级2: 明确的粒子材质类型
        if (materialType == LayaMaterialType.PARTICLESHURIKEN)
        {
            ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (MaterialType: PARTICLESHURIKEN)");
            return true;
        }

        // 优先级3: shader名称检查
        string lowerName = unityShaderName.ToLower();

        // ⭐ 强mesh特征检测：如果源码包含明确的mesh-only特征，不论名称如何都判为mesh shader
        // 这解决了名称含"particle"但实际是mesh shader的问题（如 Effect/AParticleShader）
        if (!string.IsNullOrEmpty(sourceCode))
        {
            bool hasStrongMeshFeatures =
                (sourceCode.Contains("worldNormal") && sourceCode.Contains("UnityObjectToClipPos")) ||
                (sourceCode.Contains("worldEye") && sourceCode.Contains("worldNormal")) ||
                (sourceCode.Contains("_RIMLIGHT_ON") && sourceCode.Contains("worldNormal")) ||
                (sourceCode.Contains("worldPos") && sourceCode.Contains("NORMAL") && sourceCode.Contains("UnityObjectToClipPos"));

            if (hasStrongMeshFeatures)
            {
                ExportLogger.Log($"LayaAir3D: Detected as MeshShader (strong mesh features in source: worldNormal/worldEye/rimlight with UnityObjectToClipPos): {unityShaderName}");
                return false;
            }
        }

        // 明确包含Particle关键字（最明确的粒子shader标识）
        if (lowerName.Contains("particle") || lowerName.Contains("shurike") || lowerName.Contains("trail"))
        {
            ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (Name contains particle keywords): {unityShaderName}");
            return true;
        }

        // Artist_Effect系列是粒子特效shader
        if (lowerName.Contains("artist") && lowerName.Contains("effect"))
        {
            ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (Artist_Effect series): {unityShaderName}");
            return true;
        }

        // BR_Effect系列也是粒子特效shader
        if (lowerName.Contains("br_effect") || lowerName.Contains("breffect"))
        {
            ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (BR_Effect series): {unityShaderName}");
            return true;
        }

        // 包含"effect"但明确不是mesh shader的特征
        if (lowerName.Contains("effect"))
        {
            // 粒子相关关键字
            if (lowerName.Contains("particle") ||
                lowerName.Contains("shurike") ||
                lowerName.Contains("trail") ||
                lowerName.Contains("additive") ||
                lowerName.Contains("alpha blend") ||
                lowerName.Contains("multiply") ||
                lowerName.Contains("blend"))
            {
                ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (Effect shader with particle keywords): {unityShaderName}");
                return true;
            }
        }

        // 优先级3: 检查源码特征（仅在名称不明确时使用）
        if (!string.IsNullOrEmpty(sourceCode))
        {
            // 粒子shader的明确特征（高优先级）
            bool hasParticleFeature = sourceCode.Contains("ParticleSystem") ||
                                     sourceCode.Contains("PARTICLE_") ||
                                     sourceCode.Contains("Billboard") ||
                                     sourceCode.Contains("_RENDERMODE_") ||
                                     sourceCode.Contains("a_DirectionTime") ||
                                     sourceCode.Contains("a_ShapePosition");

            if (hasParticleFeature)
            {
                ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (has particle features in code)");
                return true;
            }

            // Mesh shader的明显特征
            bool hasMeshVertexInput = (sourceCode.Contains(": POSITION") || sourceCode.Contains(": SV_POSITION")) &&
                                      (sourceCode.Contains(": TEXCOORD") || sourceCode.Contains("TEXCOORD0"));

            // Mesh shader的常见函数调用
            bool hasMeshFunction = sourceCode.Contains("UnityObjectToClipPos") ||
                                  sourceCode.Contains("mul(UNITY_MATRIX_MVP") ||
                                  sourceCode.Contains("mul(unity_MatrixMVP") ||
                                  sourceCode.Contains("mul(unity_ObjectToWorld");

            // ⭐ 重要：只有在明确是mesh特征，且shader名称不包含effect关键字时，才判定为mesh shader
            // 因为很多粒子shader也使用标准顶点输入格式
            if ((hasMeshVertexInput || hasMeshFunction) && !lowerName.Contains("effect"))
            {
                ExportLogger.Log($"LayaAir3D: Detected as MeshShader (has mesh features, no effect keyword)");
                return false;
            }
        }

        // 优先级4: Effect类型的默认判定
        // ⭐ 重要：包含"effect"的shader，默认判定为粒子shader（因为大多数effect shader用于粒子系统）
        if (lowerName.Contains("effect"))
        {
            ExportLogger.Log($"LayaAir3D: Detected as ParticleShader (Effect shader - default to particle): {unityShaderName}");
            return true;
        }

        // 5. 其他情况默认为mesh shader
        ExportLogger.Log($"LayaAir3D: Defaulting to MeshShader (cannot determine)");
        return false;
    }

    /// <summary>
    /// 将Unity Shader源代码转换为LayaAir Shader
    /// </summary>
    private static string ConvertUnityShaderToLaya(string layaShaderName, List<ShaderProperty> properties,
        string unityShaderName, string sourceCode, MaterialFile materialFile = null)
    {
        StringBuilder sb = new StringBuilder();

        // 检测材质类型
        LayaMaterialType materialType = DetectMaterialType(unityShaderName);

        // 解析Unity Shader代码
        ShaderParseResult parseResult = ParseUnityShader(sourceCode);

        // 保存properties到parseResult，供后续使用
        parseResult.properties = properties;

        // CPU 粒子由 CpuParticle3DRenderer 生成普通 Mesh 顶点数据，不能使用
        // Shuriken GPU 粒子的 attributeMap / particleShuriKenSpriteVS.glsl。
        // 因此 CPU 粒子保留粒子材质属性，但顶点与 Shader 生成必须走普通 D3 分支。
        bool isCpuParticle = materialFile != null && materialFile.IsCPUParticle();
        bool isParticleShader = IsParticleShader(materialType, unityShaderName, sourceCode, materialFile);
        parseResult.isParticleBillboard = isParticleShader && !isCpuParticle;

        // 根据组件类型和isParticleBillboard结果确定ShaderType
        LayaShaderType shaderType;
        // ★ 2D/UI 组件优先检测（在粒子检测之前）
        if (materialFile != null && materialFile.IsUsedBy2DComponent())
        {
            shaderType = LayaShaderType.D2_BaseRenderNode2D;
            ExportLogger.Log($"LayaAir3D: 2D component detected, using ShaderType: D2_BaseRenderNode2D");
        }
        else if (isCpuParticle)
        {
            shaderType = LayaShaderType.D3;
            ExportLogger.Log($"LayaAir3D: CPU particle detected, using ordinary D3 mesh shader path: {layaShaderName}");
        }
        else if (parseResult.isParticleBillboard)
        {
            // Shuriken GPU 粒子使用 Effect + 粒子专用顶点格式。
            shaderType = LayaShaderType.Effect;
            ExportLogger.Log($"LayaAir3D: Particle shader detected, using ShaderType: Effect");
        }
        else if (materialType == LayaMaterialType.Custom)
        {
            shaderType = DetectCustomShaderType(unityShaderName, properties);
        }
        else
        {
            shaderType = GetShaderTypeFromMaterialType(materialType);
        }

        string shaderTypeStr = GetShaderTypeString(shaderType);

        ExportLogger.Log($"LayaAir3D: Converting shader '{unityShaderName}' - MaterialType: {materialType}, ShaderType: {shaderType}, IsParticle: {parseResult.isParticleBillboard}");
        
        // ==================== Shader3D 配置块 ====================
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{layaShaderName},");
        sb.AppendLine(parseResult.isParticleBillboard ? "    enableInstancing:true," : "    enableInstancing:false,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        // uniformMap
        sb.AppendLine("    uniformMap:{");
        GenerateUniformMapFromProperties(sb, properties, parseResult, shaderType);
        sb.AppendLine("    },");

        // defines - 从shader_feature和multi_compile提取
        sb.AppendLine("    defines: {");
        GenerateDefinesFromParseResult(sb, parseResult, shaderType);
        sb.AppendLine("    },");
        
        // attributeMap - 根据shader类型声明不同的顶点属性
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader的attributeMap
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_DirectionTime: Vector4,");
            sb.AppendLine("        a_MeshPosition: Vector3,");
            sb.AppendLine("        a_MeshColor: Vector4,");
            sb.AppendLine("        a_MeshTextureCoordinate: Vector2,");
            sb.AppendLine("        a_ShapePositionStartLifeTime: Vector4,");
            sb.AppendLine("        a_CornerTextureCoordinate: Vector4,");
            sb.AppendLine("        a_StartColor: Vector4,");
            sb.AppendLine("        a_EndColor: Vector4,");
            sb.AppendLine("        a_StartSize: Vector3,");
            sb.AppendLine("        a_StartRotation0: Vector3,");
            sb.AppendLine("        a_StartSpeed: Float,");
            sb.AppendLine("        a_Random0: Vector4,");
            sb.AppendLine("        a_Random1: Vector4,");
            sb.AppendLine("        a_SimulationWorldPostion: Vector3,");
            sb.AppendLine("        a_SimulationWorldRotation: Vector4,");
            sb.AppendLine("        a_SimulationUV: Vector4");
            sb.AppendLine("    },");
        }
        else if (shaderType == LayaShaderType.D2_BaseRenderNode2D || shaderType == LayaShaderType.D2_TextureSV)
        {
            // ★ 2D shader的attributeMap（参考 baseRender2D.json 模板）
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_position: Vector4,");
            sb.AppendLine("        a_color: Vector4,");
            sb.AppendLine("        a_uv: Vector2");
            sb.AppendLine("    },");
        }
        else
        {
            // Mesh shader的attributeMap（标准Laya mesh属性，类型须与引擎定义一致）
            // 参考: a_Position=loc0 Vector4, a_Color=loc1 Vector4, a_Texcoord0=loc2 Vector2,
            //       a_Normal=loc3 Vector3, a_Tangent0=loc4 Vector4,
            //       a_BoneIndices=loc5 Vector4, a_BoneWeights=loc6 Vector4, a_Texcoord1=loc7 Vector2
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_Position: Vector4,");  // 必须为Vector4，骨骼蒙皮时引擎以vec4使用
            sb.AppendLine("        a_Normal: Vector3,");
            sb.AppendLine("        a_Color: Vector4,");
            sb.AppendLine("        a_Texcoord0: Vector2,");

            // 如果有自定义数据，添加UV1
            bool hasCustomData = false;
            foreach (var prop in properties)
            {
                if (prop.unityName.Contains("CustomData") || prop.unityName.Contains("_Custom"))
                {
                    hasCustomData = true;
                    break;
                }
            }

            if (hasCustomData)
            {
                sb.AppendLine("        a_Tangent0: Vector4,");
                sb.AppendLine("        a_BoneIndices: Vector4,");
                sb.AppendLine("        a_BoneWeights: Vector4,");
                sb.AppendLine("        a_Texcoord1: Vector2");
            }
            else
            {
                sb.AppendLine("        a_Tangent0: Vector4,");
                sb.AppendLine("        a_BoneIndices: Vector4,");
                sb.AppendLine("        a_BoneWeights: Vector4");
            }

            sb.AppendLine("    },");
        }
        
        // styles（为动态渲染状态属性提供默认值）
        string stylesBlock = GenerateStylesBlock(parseResult);
        if (stylesBlock != null)
        {
            sb.Append(stylesBlock);
        }

        // shaderPass
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{layaShaderName}VS,");
        string renderStateBlock = GenerateRenderStateBlock(parseResult);
        if (renderStateBlock != null)
        {
            sb.AppendLine($"            FS:{layaShaderName}FS,");
            sb.AppendLine("            statefirst: true,");
            sb.Append(renderStateBlock);
        }
        else
        {
            sb.AppendLine($"            FS:{layaShaderName}FS");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        // ==================== GLSL 代码块 ====================
        sb.AppendLine("GLSL Start");

        if (shaderType == LayaShaderType.D2_BaseRenderNode2D || shaderType == LayaShaderType.D2_TextureSV)
        {
            // ★ 2D shader使用专用的BaseRender2D模板生成VS/FS
            Generate2DBaseRenderVertexShader(sb, layaShaderName);
            Generate2DBaseRenderFragmentShader(sb, layaShaderName, parseResult, properties);
        }
        else
        {
            // 生成顶点着色器（会收集所有varying并保存到parseResult）
            GenerateConvertedVertexShader(sb, layaShaderName, parseResult);

            // 生成片元着色器（使用VS中保存的varying，确保一致）
            GenerateConvertedFragmentShader(sb, layaShaderName, parseResult);
        }

        sb.AppendLine("GLSL End");
        
        // 验证和清理生成的代码
        string result = sb.ToString();
        result = ValidateAndCleanGLSL(result);

        // 格式化shader内容
        result = FormatShaderContent(result);

        // 生成转换总结报告
        GenerateConversionSummary(parseResult, properties, result);

        return result;
    }

    /// <summary>
    /// 生成shader转换总结报告
    /// </summary>
    private static void GenerateConversionSummary(ShaderParseResult parseResult, List<ShaderProperty> properties, string resultCode)
    {
        ExportLogger.Log("==================== Shader Export Summary ====================");
        ExportLogger.Log($"Shader Name: {parseResult.shaderName}");
        ExportLogger.Log($"Shader Type: {(parseResult.isParticleBillboard ? "Particle System" : "Mesh Effect")}");

        // 架构模式信息
        if (useMappingTableMode)
        {
            ExportLogger.Log($"Architecture: Hybrid (Mapping Table Mode)");
            ExportLogger.Log($"  └─ Mapping table rules applied first");
            ExportLogger.Log($"  └─ Built-in rules as fallback");
        }
        else
        {
            ExportLogger.Log($"Architecture: Built-in (Hardcoded Rules Mode)");
            ExportLogger.Log($"  └─ All rules from C# code");
        }

        ExportLogger.Log("");

        // 统计信息
        int propertyCount = properties != null ? properties.Count : 0;
        int defineCount = parseResult.shaderFeatures.Count + parseResult.multiCompiles.Count;
        int varyingCount = parseResult.collectedVaryings != null ? parseResult.collectedVaryings.Count : 0;
        int codeLines = resultCode.Split('\n').Length;

        ExportLogger.Log($"Properties: {propertyCount}");
        ExportLogger.Log($"Defines: {defineCount}");
        ExportLogger.Log($"Varyings: {varyingCount}");
        ExportLogger.Log($"Total Lines: {codeLines}");
        ExportLogger.Log("");

        // 功能检测
        ExportLogger.Log("Detected Features:");
        List<string> features = new List<string>();

        if (HasPropertyByName(parseResult, "DetailTex2")) features.Add("✓ 3-Layer Textures");
        else if (HasPropertyByName(parseResult, "DetailTex")) features.Add("✓ 2-Layer Textures");
        else features.Add("✓ Single Texture");

        if (HasPropertyByName(parseResult, "Dissolve")) features.Add("✓ Dissolve Effect");
        if (HasPropertyByName(parseResult, "FadeEdge")) features.Add("✓ Fade Edge");
        if (HasPropertyByName(parseResult, "Distort")) features.Add("✓ Distortion");
        if (HasPropertyByName(parseResult, "Rim")) features.Add("✓ Rim Lighting");
        if (HasPropertyByName(parseResult, "Lighting") || HasPropertyByName(parseResult, "MainLight")) features.Add("✓ Custom Lighting");
        if (HasPropertyByName(parseResult, "VertexOffset") || HasPropertyByName(parseResult, "VertexAmplitude")) features.Add("✓ Vertex Offset");
        if (HasPropertyByName(parseResult, "Rotate") || HasPropertyByName(parseResult, "RotateAngle")) features.Add("✓ UV Rotation");
        if (HasPropertyByName(parseResult, "Scroll")) features.Add("✓ UV Scrolling");
        if (HasPropertyByName(parseResult, "NormalMap")) features.Add("✓ Normal Mapping");
        if (HasPropertyByName(parseResult, "CustomData")) features.Add("✓ Custom Data");
        if (HasPropertyByName(parseResult, "Polar")) features.Add("✓ Polar Coordinates");
        if (HasPropertyByName(parseResult, "GradientMap")) features.Add("✓ Gradient Remap");

        foreach (var feature in features)
        {
            ExportLogger.Log($"  {feature}");
        }

        if (features.Count == 0)
        {
            ExportLogger.Log("  (No special effects detected)");
        }

        ExportLogger.Log("");

        // 警告和建议
        List<string> warnings = new List<string>();
        List<string> suggestions = new List<string>();

        if (resultCode.Contains("vec3(0.0)") || resultCode.Contains("vec4(0.0)"))
        {
            warnings.Add("⚠ Hardcoded zero vectors detected - mesh may not be visible");
        }

        if (!parseResult.isParticleBillboard && !resultCode.Contains("vertex.positionOS"))
        {
            warnings.Add("⚠ Mesh shader does not use vertex.positionOS - may have positioning issues");
        }

        if (varyingCount == 0)
        {
            warnings.Add("⚠ No varyings detected - VS to FS data passing may not work");
        }

        if (varyingCount > 16)
        {
            suggestions.Add("ℹ Many varyings ({varyingCount}) - consider optimizing to reduce interpolator usage");
        }

        if (propertyCount > 50)
        {
            suggestions.Add($"ℹ Many properties ({propertyCount}) - shader may be complex");
        }

        if (parseResult.isParticleBillboard && resultCode.Contains("Particle.shader template not found"))
        {
            warnings.Add("⚠ Particle template not found - using fallback code (may be incomplete)");
        }

        if (warnings.Count > 0)
        {
            ExportLogger.Log("Warnings:");
            foreach (var warning in warnings)
            {
                Debug.LogWarning($"  {warning}");
            }
            ExportLogger.Log("");
        }

        if (suggestions.Count > 0)
        {
            ExportLogger.Log("Suggestions:");
            foreach (var suggestion in suggestions)
            {
                ExportLogger.Log($"  {suggestion}");
            }
            ExportLogger.Log("");
        }

        // 转换率估计
        int estimatedConversionRate = EstimateConversionRate(parseResult, resultCode);
        string rateColor = estimatedConversionRate >= 80 ? "Good" :
                          estimatedConversionRate >= 60 ? "Fair" : "Poor";

        ExportLogger.Log($"Estimated Conversion Rate: ~{estimatedConversionRate}% ({rateColor})");
        ExportLogger.Log("");

        // 性能统计
        if (useMappingTableMode)
        {
            ExportLogger.Log("Performance:");
            ExportLogger.Log($"  Mapping Table: {mappingTableConversionTime}ms");
            if (builtInConversionTime > 0)
            {
                ExportLogger.Log($"  Built-in Fallback: {builtInConversionTime}ms");
                ExportLogger.Log($"  Total: {mappingTableConversionTime + builtInConversionTime}ms");
            }
            ExportLogger.Log("");
        }
        else
        {
            ExportLogger.Log("Performance:");
            ExportLogger.Log($"  Built-in Conversion: {builtInConversionTime}ms");
            ExportLogger.Log("");
        }

        if (estimatedConversionRate < 80)
        {
            ExportLogger.Log("Note: Conversion rate below 80% - manual adjustment may be needed");
            ExportLogger.Log("Please test the exported shader in LayaAir and verify rendering");
        }
        else
        {
            ExportLogger.Log("Shader export looks good! Test in LayaAir to confirm");
        }

        ExportLogger.Log("===============================================================");
    }

    /// <summary>
    /// 估算shader转换率
    /// </summary>
    private static int EstimateConversionRate(ShaderParseResult parseResult, string resultCode)
    {
        int score = 100;

        // 基础检查
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader检查
            if (!resultCode.Contains("computeParticlePosition") && !resultCode.Contains("center"))
                score -= 30; // 缺少粒子位置计算

            if (!resultCode.Contains("computeParticleColor") && !resultCode.Contains("v_Color"))
                score -= 10; // 缺少粒子颜色

            if (!resultCode.Contains("Billboard") && !resultCode.Contains("corner"))
                score -= 15; // 缺少Billboard计算
        }
        else
        {
            // Mesh shader检查
            if (!resultCode.Contains("vertex.positionOS") && !resultCode.Contains("a_Position"))
                score -= 40; // 缺少顶点位置

            if (resultCode.Contains("vec3(0.0)") || resultCode.Contains("vec4(0.0)"))
                score -= 20; // 有硬编码零向量

            if (!resultCode.Contains("getWorldMatrix") && !resultCode.Contains("worldMat"))
                score -= 15; // 缺少世界变换
        }

        // 通用检查
        if (parseResult.collectedVaryings == null || parseResult.collectedVaryings.Count == 0)
            score -= 15; // 没有varying

        if (!resultCode.Contains("gl_Position"))
            score -= 25; // 缺少位置输出

        if (!resultCode.Contains("gl_FragColor") && !resultCode.Contains("out"))
            score -= 10; // 缺少颜色输出

        // Unity特有函数未转换
        if (resultCode.Contains("UnityObjectToClipPos"))
            score -= 10;

        if (resultCode.Contains("UNITY_MATRIX"))
            score -= 5;

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// Unity Shader解析结果
    /// </summary>
    private class ShaderParseResult
    {
        public string shaderName;
        public List<string> shaderFeatures = new List<string>();  // shader_feature定义
        public List<List<string>> shaderFeatureGroups = new List<List<string>>(); // 互斥关键字组
        public List<string> multiCompiles = new List<string>();   // multi_compile定义
        public List<string> includes = new List<string>();        // include文件
        public Dictionary<string, string> variables = new Dictionary<string, string>(); // 变量声明
        public string vertexStructName = "";  // appdata结构体名称
        public string vertexStruct = "";      // appdata结构体内容
        public string v2fStructName = "";     // v2f结构体名称
        public string v2fStruct = "";         // v2f结构体内容
        public string vertexCode = "";        // 顶点着色器代码
        public string fragmentCode = "";      // 片元着色器代码
        public List<string> customFunctions = new List<string>(); // 自定义函数
        public Dictionary<string, VaryingInfo> varyings = new Dictionary<string, VaryingInfo>(); // varying映射
        public bool usesVertexColor = false;  // 是否使用顶点颜色
        public bool usesUV = false;           // 是否使用UV
        public bool isParticleBillboard = false;  // 是否是粒子Billboard模式
        public Dictionary<string, string> collectedVaryings = new Dictionary<string, string>(); // 收集到的所有varying（VS生成后传递给FS）
        public string varyingDeclarations = "";  // VS生成的varying声明字符串，FS直接复用
        public List<ShaderProperty> properties = new List<ShaderProperty>(); // Shader属性列表

        // 渲染状态（从SubShader/Pass块解析）
        public string blendSrc = null;
        public string blendDst = null;
        public string blendSrcAlpha = null;   // 分离混合时的Alpha通道
        public string blendDstAlpha = null;
        public string blendOp = null;
        public string blendOpAlpha = null;
        public string zWrite = null;          // "On" / "Off"
        public string zTest = null;           // "LEqual" 等
        public string cullMode = null;        // "Back" / "Front" / "Off"
    }

    /// <summary>
    /// Varying信息
    /// </summary>
    private class VaryingInfo
    {
        public string originalName;   // 原始名称（如 uv, color）
        public string glslName;       // GLSL名称（如 v_Texcoord0）
        public string glslType;       // GLSL类型（如 vec2）
        public string semantic;       // 语义（如 TEXCOORD0）
    }

    // ==================== 渲染状态映射辅助方法 ====================

    /// <summary>
    /// Unity BlendMode 数值 → LayaAir 字符串
    /// </summary>
    private static string UnityBlendFactorToLayaString(int value)
    {
        switch (value)
        {
            case 0: return "Zero";
            case 1: return "One";
            case 2: return "DestinationColor";
            case 3: return "SourceColor";
            case 4: return "OneMinusDestinationColor";
            case 5: return "SourceAlpha";
            case 6: return "OneMinusSourceAlpha";
            case 7: return "DestinationAlpha";
            case 8: return "OneMinusDestinationAlpha";
            case 9: return "SourceAlphaSaturate";
            case 10: return "OneMinusSourceColor";
            default: return null;
        }
    }

    /// <summary>
    /// Unity BlendMode 名称 → LayaAir 字符串（字面值情况，名称基本一致）
    /// </summary>
    private static string UnityBlendFactorNameToLayaString(string name)
    {
        switch (name)
        {
            case "Zero": return "Zero";
            case "One": return "One";
            case "DstColor": return "DestinationColor";
            case "SrcColor": return "SourceColor";
            case "OneMinusDstColor": return "OneMinusDestinationColor";
            case "SrcAlpha": return "SourceAlpha";
            case "OneMinusSrcAlpha": return "OneMinusSourceAlpha";
            case "DstAlpha": return "DestinationAlpha";
            case "OneMinusDstAlpha": return "OneMinusDestinationAlpha";
            case "SrcAlphaSaturate": return "SourceAlphaSaturate";
            case "OneMinusSrcColor": return "OneMinusSourceColor";
            default: return name; // 未知名称直接返回
        }
    }

    /// <summary>
    /// Unity BlendOp 数值 → LayaAir 字符串
    /// </summary>
    private static string UnityBlendOpToLayaString(int value)
    {
        switch (value)
        {
            case 0: return "Add";
            case 1: return "Subtract";
            case 2: return "Reverse_substract";
            case 3: return "Min";
            case 4: return "Max";
            default: return null;
        }
    }

    /// <summary>
    /// Unity BlendOp 名称 → LayaAir 字符串
    /// </summary>
    private static string UnityBlendOpNameToLayaString(string name)
    {
        switch (name)
        {
            case "Add": return "Add";
            case "Sub": return "Subtract";
            case "RevSub": return "Reverse_substract";
            case "Min": return "Min";
            case "Max": return "Max";
            default: return name;
        }
    }

    /// <summary>
    /// Unity ZTest 数值 → LayaAir 字符串
    /// </summary>
    private static string UnityZTestToLayaString(int value)
    {
        switch (value)
        {
            case 0:
            case 1: return "Never";
            case 2: return "Less";
            case 3: return "Equal";
            case 4: return "LessEqual";
            case 5: return "Greater";
            case 6: return "NotEqual";
            case 7: return "GreaterEqual";
            case 8: return "Always";
            default: return null;
        }
    }

    /// <summary>
    /// Unity ZTest 名称 → LayaAir 字符串
    /// </summary>
    private static string UnityZTestNameToLayaString(string name)
    {
        switch (name)
        {
            case "Off":
            case "Never": return "Never";
            case "Less": return "Less";
            case "Equal": return "Equal";
            case "LEqual": return "LessEqual";
            case "Greater": return "Greater";
            case "NotEqual": return "NotEqual";
            case "GEqual": return "GreaterEqual";
            case "Always": return "Always";
            default: return name;
        }
    }

    /// <summary>
    /// Unity Cull 数值 → LayaAir 字符串
    /// </summary>
    private static string UnityCullToLayaString(int value)
    {
        switch (value)
        {
            case 0: return "Off";
            case 1: return "Front";
            case 2: return "Back";
            default: return null;
        }
    }

    /// <summary>
    /// Unity Cull 名称 → LayaAir 字符串
    /// </summary>
    private static string UnityCullNameToLayaString(string name)
    {
        switch (name)
        {
            case "Off": return "Off";
            case "Front": return "Front";
            case "Back": return "Back";
            default: return name;
        }
    }

    // ==================== 渲染状态 → Laya int 映射辅助方法 ====================

    /// <summary>
    /// 判断渲染状态 token 是否是属性引用（如 [_DstBlend]）
    /// </summary>
    private static bool IsPropertyReference(string token)
    {
        return !string.IsNullOrEmpty(token) && token.StartsWith("[") && token.EndsWith("]");
    }

    /// <summary>
    /// Unity BlendMode 数值 → Laya BlendFactor 整数
    /// </summary>
    // ─── BlendMode 动态映射 ───
    private static readonly Dictionary<string, int> BlendModeNameToLayaInt = new Dictionary<string, int>
    {
        { "Zero", 0 }, { "One", 1 },
        { "DstColor", 4 }, { "SrcColor", 2 },
        { "OneMinusDstColor", 5 }, { "OneMinusSrcColor", 3 },
        { "SrcAlpha", 6 }, { "OneMinusSrcAlpha", 7 },
        { "DstAlpha", 8 }, { "OneMinusDstAlpha", 9 },
        { "SrcAlphaSaturate", 6 },
    };
    private static readonly Dictionary<int, int> UnityBlendToLayaMap = BuildEnumMap<UnityEngine.Rendering.BlendMode>(BlendModeNameToLayaInt);

    private static int UnityBlendFactorToLayaInt(int value)
    {
        if (UnityBlendToLayaMap.TryGetValue(value, out int layaVal))
            return layaVal;
        return 1; // 默认 One
    }

    /// <summary>
    /// 供其他类调用的 Unity BlendMode → Laya BlendFactor 转换（动态映射）
    /// </summary>
    internal static int UnityBlendToLaya(int unityBlend)
    {
        return UnityBlendFactorToLayaInt(unityBlend);
    }

    // ─── BlendOp 动态映射 ───
    private static readonly Dictionary<string, int> BlendOpNameToLayaInt = new Dictionary<string, int>
    {
        { "Add", 0 }, { "Subtract", 1 }, { "ReverseSubtract", 2 }, { "Min", 3 }, { "Max", 4 },
    };
    private static readonly Dictionary<int, int> UnityBlendOpToLayaMap = BuildEnumMap<UnityEngine.Rendering.BlendOp>(BlendOpNameToLayaInt);

    private static int UnityBlendOpToLayaInt(int value)
    {
        if (UnityBlendOpToLayaMap.TryGetValue(value, out int v)) return v;
        return 0; // 默认 Add
    }

    // ─── CullMode 动态映射 ───
    private static readonly Dictionary<string, int> CullNameToLayaInt = new Dictionary<string, int>
    {
        { "Off", 0 }, { "Front", 1 }, { "Back", 2 },
    };
    private static readonly Dictionary<int, int> UnityCullToLayaMap = BuildEnumMap<UnityEngine.Rendering.CullMode>(CullNameToLayaInt);

    private static int UnityCullToLayaInt(int value)
    {
        if (UnityCullToLayaMap.TryGetValue(value, out int v)) return v;
        return 2; // 默认 Back
    }

    // ─── CompareFunction (ZTest) 动态映射 ───
    private static readonly Dictionary<string, int> CompareFuncNameToLayaInt = new Dictionary<string, int>
    {
        { "Disabled", 0 }, { "Never", 0 },
        { "Less", 1 }, { "Equal", 2 }, { "LessEqual", 3 },
        { "Greater", 4 }, { "NotEqual", 5 }, { "GreaterEqual", 6 }, { "Always", 7 },
    };
    private static readonly Dictionary<int, int> UnityZTestToLayaMap = BuildEnumMap<UnityEngine.Rendering.CompareFunction>(CompareFuncNameToLayaInt);

    private static int UnityZTestToLayaInt(int value)
    {
        if (UnityZTestToLayaMap.TryGetValue(value, out int v)) return v;
        return 3; // 默认 LEQUAL
    }

    /// <summary>
    /// 通用工具：遍历 Unity 枚举的运行时值，结合名称→Laya值映射表，构建 int→int 动态映射
    /// </summary>
    private static Dictionary<int, int> BuildEnumMap<TEnum>(Dictionary<string, int> nameToLaya) where TEnum : struct
    {
        var map = new Dictionary<int, int>();
        foreach (TEnum e in System.Enum.GetValues(typeof(TEnum)))
        {
            string name = e.ToString();
            if (nameToLaya.TryGetValue(name, out int layaVal))
            {
                int key = System.Convert.ToInt32(e);
                if (!map.ContainsKey(key))
                    map[key] = layaVal;
            }
        }
        return map;
    }

    /// <summary>
    /// Unity BlendFactor 名称 → Laya BlendFactor 整数
    /// </summary>
    private static int UnityBlendFactorNameToLayaInt(string name)
    {
        switch (name)
        {
            case "Zero": return 0;
            case "One": return 1;
            case "DstColor": return 4;
            case "SrcColor": return 2;
            case "OneMinusDstColor": return 5;
            case "SrcAlpha": return 6;
            case "OneMinusSrcAlpha": return 7;
            case "DstAlpha": return 8;
            case "OneMinusDstAlpha": return 9;
            case "SrcAlphaSaturate": return 6;
            case "OneMinusSrcColor": return 3;
            default: return 1;
        }
    }

    /// <summary>
    /// Unity CullMode 名称 → Laya Cull 整数
    /// </summary>
    private static int UnityCullNameToLayaInt(string name)
    {
        switch (name)
        {
            case "Off": return 0;
            case "Front": return 1;
            case "Back": return 2;
            default: return 2;
        }
    }

    /// <summary>
    /// 从属性引用 token 中提取属性名（如 "[_DstBlend]" → "_DstBlend"）
    /// </summary>
    private static string ExtractPropertyName(string token)
    {
        if (IsPropertyReference(token))
            return token.Substring(1, token.Length - 2);
        return null;
    }

    /// <summary>
    /// 从 Shader properties 中获取属性引用的默认整数值
    /// </summary>
    private static int GetPropertyDefaultInt(string propName, List<ShaderProperty> properties)
    {
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                if (prop.unityName == propName)
                {
                    return prop.defaultInt != 0 ? prop.defaultInt : (int)prop.defaultFloat;
                }
            }
        }
        return 0;
    }

    // ==================== 渲染状态解析与模式匹配 ====================

    /// <summary>
    /// 解析后的实际生效渲染状态（合并 shader 硬编码值和材质动态值后的 Laya 参数）
    /// </summary>
    private class ResolvedRenderState
    {
        // Blend 模式: 0=DISABLE, 1=ENABLE_ALL, 2=ENABLE_SEPERATE
        public int s_Blend;

        // Simple blend 参数 (s_Blend=1 时使用)
        public int s_BlendSrc;
        public int s_BlendDst;

        // Separate blend 参数 (s_Blend=2 时使用)
        public int s_BlendSrcRGB;
        public int s_BlendDstRGB;
        public int s_BlendSrcAlpha;
        public int s_BlendDstAlpha;

        // Blend 运算 (0=Add)
        public int blendOp;
        public int blendOpAlpha;

        // Cull: 0=NONE, 1=FRONT, 2=BACK
        public int s_Cull = 2;

        // Depth
        public bool s_DepthWrite = true;
        public int s_DepthTest = 1; // 默认 LESS

        // 标记哪些值有来源（shader中有声明）
        public bool hasBlend;
        public bool hasBlendOp;
        public bool hasCull;
        public bool hasZWrite;
        public bool hasZTest;
    }

    /// <summary>
    /// 修正 .mat 文件中序列化的过期 _DstBlend 属性值。
    ///
    /// Unity 特效/粒子 shader 的 _DstBlend 属性声明默认值通常为 10 (OneMinusSrcColor)，
    /// 但 GUI 脚本在运行时会根据 _Mode 覆盖：Translucent → 6 (OneMinusSrcAlpha), Additive → 1 (One)。
    /// .mat 序列化保留的仍是声明默认值 10，导致导出时产生错误的 OneMinusSourceColor。
    ///
    /// 仅当 _DstBlend == 10 时修正（这是已知的序列化旧值），其他值视为 GUI 已正确写入，不干预。
    /// 这样既适用于所有使用此约定的特效 shader，又不影响 Standard 等 _DstBlend=0 的 shader。
    /// </summary>
    /// <summary>
    /// 运行时安全获取 Unity BlendMode 枚举的整数值。
    /// 使用 System.Enum.Parse 在运行时解析，避免编译期常量内联导致的版本兼容问题。
    /// </summary>
    private static Dictionary<string, int> BuildShaderGUIOverrides(Material material, string layaShaderName)
    {
        // 当前不需要 GUI 脚本属性覆盖。
        // UnityBlendFactorToLayaInt 已改为运行时动态映射（基于枚举名称），
        // 可正确处理不同 Unity 版本中枚举整数值不同的情况。
        return null;
    }

    /// <summary>
    /// 将 ShaderParseResult 中的渲染状态 token 与 Material 的实际属性值合并，
    /// 得到该材质实际生效的 Laya 渲染参数
    /// </summary>
    /// <param name="propertyOverrides">
    /// GUI 脚本属性值覆盖：某些 shader 的 CustomEditor GUI 会在运行时修改材质属性（如 _DstBlend），
    /// 但 .mat 序列化的是旧值。此字典提供 GUI 脚本逻辑修正后的实际值。
    /// </param>
    private static ResolvedRenderState ResolveRenderState(ShaderParseResult parseResult, Material material,
        Dictionary<string, int> propertyOverrides = null)
    {
        var resolved = new ResolvedRenderState();

        // Blend
        if (parseResult.blendSrc != null)
        {
            resolved.hasBlend = true;
            bool isSeparate = parseResult.blendSrcAlpha != null;
            resolved.s_Blend = isSeparate ? 2 : 1;

            int srcRGB = ResolveBlendFactor(parseResult.blendSrc, material, propertyOverrides);
            int dstRGB = ResolveBlendFactor(parseResult.blendDst, material, propertyOverrides);

            if (isSeparate)
            {
                resolved.s_BlendSrcRGB = srcRGB;
                resolved.s_BlendDstRGB = dstRGB;
                resolved.s_BlendSrcAlpha = ResolveBlendFactor(parseResult.blendSrcAlpha, material, propertyOverrides);
                resolved.s_BlendDstAlpha = ResolveBlendFactor(parseResult.blendDstAlpha, material, propertyOverrides);
            }
            else
            {
                resolved.s_BlendSrc = srcRGB;
                resolved.s_BlendDst = dstRGB;
            }
        }

        // BlendOp
        if (parseResult.blendOp != null)
        {
            resolved.hasBlendOp = true;
            resolved.blendOp = ResolveBlendOp(parseResult.blendOp, material);
            resolved.blendOpAlpha = parseResult.blendOpAlpha != null
                ? ResolveBlendOp(parseResult.blendOpAlpha, material)
                : resolved.blendOp;
        }

        // Cull
        if (parseResult.cullMode != null)
        {
            resolved.hasCull = true;
            resolved.s_Cull = ResolveCull(parseResult.cullMode, material);
        }

        // ZWrite
        if (parseResult.zWrite != null)
        {
            resolved.hasZWrite = true;
            resolved.s_DepthWrite = ResolveZWrite(parseResult.zWrite, material);
        }

        // ZTest
        if (parseResult.zTest != null)
        {
            resolved.hasZTest = true;
            resolved.s_DepthTest = ResolveZTest(parseResult.zTest, material);
        }

        return resolved;
    }

    /// <summary>
    /// ShaderGraph assets contain graph JSON rather than ShaderLab pass text, so ParseRenderState
    /// cannot find Blend/Cull/ZWrite/ZTest directives. ShaderGraph serializes the effective Built-in
    /// target state onto the material as _BUILTIN_* properties; resolve those values directly.
    /// </summary>
    private static ResolvedRenderState ResolveShaderGraphRenderState(Material material)
    {
        var resolved = new ResolvedRenderState();

        int surface = GetFirstMaterialInt(material, material.renderQueue >= 3000 ? 1 : 0,
            "_BUILTIN_Surface", "_Surface", "_SurfaceType");
        bool transparent = surface != 0 || material.renderQueue >= 3000;

        resolved.hasBlend = true;
        resolved.s_Blend = transparent ? 1 : 0;
        if (transparent)
        {
            int srcBlend;
            int dstBlend;
            bool hasSrc = TryGetFirstMaterialInt(material, out srcBlend,
                "_BUILTIN_SrcBlend", "_SrcBlend");
            bool hasDst = TryGetFirstMaterialInt(material, out dstBlend,
                "_BUILTIN_DstBlend", "_DstBlend");

            if (hasSrc && hasDst)
            {
                resolved.s_BlendSrc = UnityBlendFactorToLayaInt(srcBlend);
                resolved.s_BlendDst = UnityBlendFactorToLayaInt(dstBlend);
            }
            else
            {
                // ShaderGraph AlphaMode: 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply.
                int blendMode = GetFirstMaterialInt(material, 0, "_BUILTIN_Blend", "_Blend", "_BlendMode");
                switch (blendMode)
                {
                    case 1: // Premultiply
                        resolved.s_BlendSrc = 1; // One
                        resolved.s_BlendDst = 7; // OneMinusSrcAlpha
                        break;
                    case 2: // Additive
                        resolved.s_BlendSrc = 6; // SrcAlpha
                        resolved.s_BlendDst = 1; // One
                        break;
                    case 3: // Multiply
                        resolved.s_BlendSrc = 4; // DstColor
                        resolved.s_BlendDst = 0; // Zero
                        break;
                    default: // Alpha
                        resolved.s_BlendSrc = 6; // SrcAlpha
                        resolved.s_BlendDst = 7; // OneMinusSrcAlpha
                        break;
                }
            }
        }

        int cull = GetFirstMaterialInt(material, 2,
            "_BUILTIN_CullMode", "_CullMode", "_Cull");
        resolved.hasCull = true;
        resolved.s_Cull = UnityCullToLayaInt(cull);

        int zWrite = GetFirstMaterialInt(material, transparent ? 0 : 1,
            "_BUILTIN_ZWrite", "_ZWrite");
        resolved.hasZWrite = true;
        resolved.s_DepthWrite = zWrite != 0;

        int zTest = GetFirstMaterialInt(material, 4,
            "_BUILTIN_ZTest", "_ZTest");
        resolved.hasZTest = true;
        resolved.s_DepthTest = UnityZTestToLayaInt(zTest);

        return resolved;
    }

    private static int GetFirstMaterialInt(Material material, int fallback, params string[] propertyNames)
    {
        int value;
        return TryGetFirstMaterialInt(material, out value, propertyNames) ? value : fallback;
    }

    private static bool TryGetFirstMaterialInt(Material material, out int value, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (material.HasProperty(propertyName))
            {
                value = material.GetInt(propertyName);
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// 解析 blend factor token → Laya BlendParam int
    /// </summary>
    private static int ResolveBlendFactor(string token, Material material,
        Dictionary<string, int> propertyOverrides = null)
    {
        if (IsPropertyReference(token))
        {
            string pn = ExtractPropertyName(token);
            // 优先使用 GUI 脚本覆盖值（修正序列化过期值）
            int val;
            if (propertyOverrides != null && propertyOverrides.TryGetValue(pn, out val))
            {
                return UnityBlendFactorToLayaInt(val);
            }
            val = material.HasProperty(pn) ? material.GetInt(pn) : 0;
            return UnityBlendFactorToLayaInt(val);
        }
        return UnityBlendFactorNameToLayaInt(token);
    }

    /// <summary>
    /// 解析 blend op token → Laya BlendEquation int
    /// </summary>
    private static int ResolveBlendOp(string token, Material material)
    {
        if (IsPropertyReference(token))
        {
            string pn = ExtractPropertyName(token);
            int val = material.HasProperty(pn) ? material.GetInt(pn) : 0;
            return UnityBlendOpToLayaInt(val);
        }
        switch (token)
        {
            case "Add": return 0;
            case "Sub": return 1;
            case "RevSub": return 2;
            case "Min": return 3;
            case "Max": return 4;
            default: return 0;
        }
    }

    /// <summary>
    /// 解析 cull token → Laya Cull int
    /// </summary>
    private static int ResolveCull(string token, Material material)
    {
        if (IsPropertyReference(token))
        {
            string pn = ExtractPropertyName(token);
            int val = material.HasProperty(pn) ? material.GetInt(pn) : 2;
            return UnityCullToLayaInt(val);
        }
        return UnityCullNameToLayaInt(token);
    }

    /// <summary>
    /// 解析 zwrite token → bool
    /// </summary>
    private static bool ResolveZWrite(string token, Material material)
    {
        if (IsPropertyReference(token))
        {
            string pn = ExtractPropertyName(token);
            int val = material.HasProperty(pn) ? material.GetInt(pn) : 1;
            return val != 0;
        }
        return !string.Equals(token, "Off", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 ztest token → Laya DepthTest int
    /// </summary>
    private static int ResolveZTest(string token, Material material)
    {
        if (IsPropertyReference(token))
        {
            string pn = ExtractPropertyName(token);
            int val = material.HasProperty(pn) ? material.GetInt(pn) : 4; // Unity 默认 LessEqual
            return UnityZTestToLayaInt(val);
        }
        // Laya: 0=OFF, 1=LESS, 2=EQUAL, 3=LEQUAL, 4=GREATER, 5=NOTEQUAL, 6=GEQUAL, 7=ALWAYS
        switch (token)
        {
            case "Never": return 0;
            case "Less": return 1;
            case "Equal": return 2;
            case "LEqual": return 3;
            case "Greater": return 4;
            case "NotEqual": return 5;
            case "GEqual": return 6;
            case "Always": return 7;
            default: return 3; // LEQUAL
        }
    }

    /// <summary>
    /// 将解析后的渲染状态与预定义模式进行匹配
    /// 匹配规则：s_Blend + s_BlendSrc/Dst（blend>0时）+ s_DepthWrite 必须一致
    /// s_Cull 和 s_DepthTest 不参与匹配
    /// </summary>
    /// <returns>匹配的模式配置，或 null 表示需要 CUSTOM 模式</returns>
    private static LayaRenderModeConfig MatchRenderMode(ResolvedRenderState resolved)
    {
        // Separate blend → 预定义模式全是简单混合(s_Blend=0或1)，不可能匹配
        if (resolved.s_Blend == 2)
            return null;

        // 非 Add 的 BlendOp → 预定义模式不支持自定义 BlendOp
        if (resolved.hasBlendOp && (resolved.blendOp != 0 || resolved.blendOpAlpha != 0))
            return null;

        var modes = GetLayaRenderModes();
        foreach (var kvp in modes)
        {
            // 跳过 CUSTOM 模式（它是匹配失败的 fallback）
            if (kvp.Key == "CUSTOM")
                continue;

            var mode = kvp.Value;

            // s_Blend 必须一致
            if (mode.s_Blend != resolved.s_Blend)
                continue;

            // blend 开启时检查混合因子
            if (mode.s_Blend > 0)
            {
                if (mode.s_BlendSrc != resolved.s_BlendSrc)
                    continue;
                if (mode.s_BlendDst != resolved.s_BlendDst)
                    continue;
            }

            // 深度写入必须一致
            if (mode.s_DepthWrite != resolved.s_DepthWrite)
                continue;

            return mode; // 匹配成功
        }

        return null; // 无匹配 → CUSTOM
    }

    /// <summary>
    /// 解析渲染状态值，处理属性引用（如 [_SrcBlend]）和字面值
    /// </summary>
    /// <param name="token">渲染状态token，可能是 "[_PropertyName]" 或字面值如 "SrcAlpha"</param>
    /// <param name="properties">Shader属性列表</param>
    /// <param name="converter">数值→字符串转换函数（用于属性引用情况）</param>
    /// <param name="nameConverter">名称→字符串转换函数（用于字面值情况）</param>
    /// <returns>LayaAir渲染状态字符串，解析失败返回null</returns>
    private static string ResolveRenderStateValue(string token, List<ShaderProperty> properties,
        System.Func<int, string> converter, System.Func<string, string> nameConverter)
    {
        if (string.IsNullOrEmpty(token)) return null;

        // 属性引用：[_PropertyName]
        if (token.StartsWith("[") && token.EndsWith("]"))
        {
            string propName = token.Substring(1, token.Length - 2);
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    if (prop.unityName == propName)
                    {
                        // 优先使用defaultInt，然后defaultFloat
                        int intVal = prop.defaultInt != 0 ? prop.defaultInt : (int)prop.defaultFloat;
                        return converter(intVal);
                    }
                }
            }
            ExportLogger.Warning($"LayaAir3D: RenderState property reference '{propName}' not found in shader properties");
            return null;
        }

        // 字面值
        return nameConverter(token);
    }

    /// <summary>
    /// 解析Unity Shader源代码
    /// </summary>
    private static ShaderParseResult ParseUnityShader(string sourceCode)
    {
        ShaderParseResult result = new ShaderParseResult();
        
        // 提取Shader名称
        var nameMatch = Regex.Match(sourceCode, @"Shader\s+""([^""]+)""");
        if (nameMatch.Success)
        {
            result.shaderName = nameMatch.Groups[1].Value;
        }
        
        // 提取CGPROGRAM/HLSLPROGRAM块
        var cgMatch = Regex.Match(sourceCode, @"(CGPROGRAM|HLSLPROGRAM)(.*?)(ENDCG|ENDHLSL)", 
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        if (cgMatch.Success)
        {
            string cgCode = cgMatch.Groups[2].Value;
            
            // 提取shader_feature
            var featureMatches = Regex.Matches(cgCode, @"#pragma\s+shader_feature[_\w]*\s+(.+)");
            foreach (Match m in featureMatches)
            {
                string features = m.Groups[1].Value.Trim();
                var parts = features.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> group = new List<string>();
                foreach (var part in parts)
                {
                    if (part.Contains(".")) continue; // 跳过文件名
                    group.Add(part);
                    if (!part.StartsWith("_") || part.Contains("_ON"))
                    {
                        result.shaderFeatures.Add(part);
                    }
                }
                // 同一行有 ≥2 个关键字 → 互斥组
                if (group.Count > 1)
                    result.shaderFeatureGroups.Add(group);
            }
            
            // 提取multi_compile
            var multiMatches = Regex.Matches(cgCode, @"#pragma\s+multi_compile[_\w]*\s+(.+)");
            foreach (Match m in multiMatches)
            {
                string compiles = m.Groups[1].Value.Trim();
                result.multiCompiles.AddRange(compiles.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            }
            
            // 提取include
            var includeMatches = Regex.Matches(cgCode, @"#include\s+""([^""]+)""");
            foreach (Match m in includeMatches)
            {
                result.includes.Add(m.Groups[1].Value);
            }
            
            // 提取变量声明（支持条件编译块内的变量）
            ExtractVariables(cgCode, result);
            
            // 提取appdata结构体
            var appdataMatch = Regex.Match(cgCode, 
                @"struct\s+(appdata\w*|a2v|VertexInput)\s*\{([^}]+)\}", 
                RegexOptions.Singleline);
            if (appdataMatch.Success)
            {
                result.vertexStructName = appdataMatch.Groups[1].Value;
                result.vertexStruct = appdataMatch.Groups[2].Value;
            }
            
            // 提取v2f结构体
            var v2fMatch = Regex.Match(cgCode, 
                @"struct\s+(v2f|VertexOutput|Varyings)\s*\{([^}]+)\}", 
                RegexOptions.Singleline);
            if (v2fMatch.Success)
            {
                result.v2fStructName = v2fMatch.Groups[1].Value;
                result.v2fStruct = v2fMatch.Groups[2].Value;
                ParseVaryings(result);
            }
            
            // 提取顶点着色器函数 - 使用括号平衡
            result.vertexCode = ExtractFunctionBody(cgCode, "vert");
            
            // 提取片元着色器函数 - 使用括号平衡
            result.fragmentCode = ExtractFunctionBody(cgCode, "frag");
            
            // 提取自定义函数 - 使用平衡括号匹配
            ExtractCustomFunctions(cgCode, result);
            
            // 检测是否使用顶点颜色
            result.usesVertexColor = DetectVertexColorUsage(cgCode, result);
            
            // 检测是否使用UV
            result.usesUV = DetectUVUsage(cgCode, result);
        }

        // 从SubShader/Pass块中解析渲染状态（在CGPROGRAM之外）
        ParseRenderState(sourceCode, result);

        return result;
    }

    /// <summary>
    /// 从Unity Shader源码的SubShader/Pass块中解析渲染状态指令
    /// </summary>
    private static void ParseRenderState(string sourceCode, ShaderParseResult result)
    {
        // 移除注释以避免匹配注释中的渲染状态
        string cleaned = Regex.Replace(sourceCode, @"//.*$", "", RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"/\*.*?\*/", "", RegexOptions.Singleline);

        // 移除CGPROGRAM/HLSLPROGRAM块（渲染状态在这些块之外）
        cleaned = Regex.Replace(cleaned, @"(CGPROGRAM|HLSLPROGRAM).*?(ENDCG|ENDHLSL)",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 解析 Blend 指令
        // 格式1: Blend SrcFactor DstFactor
        // 格式2: Blend SrcFactor DstFactor, SrcFactorA DstFactorA
        // 注意：Unity shader 中属性引用 [_Prop] 前可能没有空格，如 "Blend One[_DstBlend] ,One Zero"
        // 使用 (?:[ \t]+|(?=\[)) 在 token 之间允许"有空格"或"紧跟["两种情况
        var blendMatch = Regex.Match(cleaned,
            @"\bBlend[ \t]+(\w+|\[\w+\])(?:[ \t]+|(?=\[))(\w+|\[\w+\])(?:[ \t]*,[ \t]*(\w+|\[\w+\])(?:[ \t]+|(?=\[))(\w+|\[\w+\]))?",
            RegexOptions.IgnoreCase);
        if (blendMatch.Success)
        {
            string src = blendMatch.Groups[1].Value.Trim();
            string dst = blendMatch.Groups[2].Value.Trim();

            // 排除 "Blend Off" 这种情况
            if (!string.Equals(src, "Off", StringComparison.OrdinalIgnoreCase))
            {
                result.blendSrc = src;
                result.blendDst = dst;

                // 分离混合
                if (blendMatch.Groups[3].Success && blendMatch.Groups[3].Value.Trim().Length > 0)
                {
                    result.blendSrcAlpha = blendMatch.Groups[3].Value.Trim();
                    result.blendDstAlpha = blendMatch.Groups[4].Value.Trim();
                }
            }
        }

        // 解析 BlendOp 指令
        // 格式1: BlendOp Op
        // 格式2: BlendOp OpRGB, OpAlpha
        var blendOpMatch = Regex.Match(cleaned,
            @"\bBlendOp(?:[ \t]+|(?=\[))(\w+|\[\w+\])(?:[ \t]*,[ \t]*(\w+|\[\w+\]))?",
            RegexOptions.IgnoreCase);
        if (blendOpMatch.Success)
        {
            result.blendOp = blendOpMatch.Groups[1].Value.Trim();
            if (blendOpMatch.Groups[2].Success && blendOpMatch.Groups[2].Value.Trim().Length > 0)
            {
                result.blendOpAlpha = blendOpMatch.Groups[2].Value.Trim();
            }
        }

        // 解析 ZWrite 指令 — "ZWrite[_ZWrite]" 无空格情况
        var zWriteMatch = Regex.Match(cleaned, @"\bZWrite(?:[ \t]+|(?=\[))(\w+|\[\w+\])", RegexOptions.IgnoreCase);
        if (zWriteMatch.Success)
        {
            result.zWrite = zWriteMatch.Groups[1].Value.Trim();
        }

        // 解析 ZTest 指令
        var zTestMatch = Regex.Match(cleaned, @"\bZTest(?:[ \t]+|(?=\[))(\w+|\[\w+\])", RegexOptions.IgnoreCase);
        if (zTestMatch.Success)
        {
            result.zTest = zTestMatch.Groups[1].Value.Trim();
        }

        // 解析 Cull 指令 — "Cull[_Cull]" 无空格情况
        var cullMatch = Regex.Match(cleaned, @"\bCull(?:[ \t]+|(?=\[))(\w+|\[\w+\])", RegexOptions.IgnoreCase);
        if (cullMatch.Success)
        {
            result.cullMode = cullMatch.Groups[1].Value.Trim();
        }

    }

    /// <summary>
    /// 判断是否有任何渲染状态需要生成
    /// </summary>
    private static bool HasRenderState(ShaderParseResult parseResult)
    {
        return parseResult.blendSrc != null ||
               parseResult.blendOp != null ||
               parseResult.zWrite != null ||
               parseResult.zTest != null ||
               parseResult.cullMode != null;
    }

    /// <summary>
    /// 为动态（属性引用）渲染状态参数生成 styles 块，提供默认值
    /// 只处理 [_Prop] 形式的属性引用，硬编码值由 renderState 处理
    /// </summary>
    private static string GenerateStylesBlock(ShaderParseResult parseResult)
    {
        if (!HasRenderState(parseResult))
            return null;

        var props = parseResult.properties;
        List<string> entries = new List<string>();

        // materialRenderMode 默认值（CUSTOM=5，材质可根据匹配结果覆盖）
        entries.Add("        materialRenderMode: { default: 5 }");

        // Blend 动态参数默认值
        if (parseResult.blendSrc != null)
        {
            bool isSeparate = parseResult.blendSrcAlpha != null;
            // s_Blend 默认值：2=Separate, 1=Enable
            entries.Add($"        s_Blend: {{ default: {(isSeparate ? 2 : 1)} }}");

            if (isSeparate)
            {
                if (IsPropertyReference(parseResult.blendSrc))
                {
                    string propName = ExtractPropertyName(parseResult.blendSrc);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendSrcRGB: {{ default: {defVal} }}");
                }
                if (IsPropertyReference(parseResult.blendDst))
                {
                    string propName = ExtractPropertyName(parseResult.blendDst);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendDstRGB: {{ default: {defVal} }}");
                }
                if (IsPropertyReference(parseResult.blendSrcAlpha))
                {
                    string propName = ExtractPropertyName(parseResult.blendSrcAlpha);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendSrcAlpha: {{ default: {defVal} }}");
                }
                if (IsPropertyReference(parseResult.blendDstAlpha))
                {
                    string propName = ExtractPropertyName(parseResult.blendDstAlpha);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendDstAlpha: {{ default: {defVal} }}");
                }
            }
            else
            {
                if (IsPropertyReference(parseResult.blendSrc))
                {
                    string propName = ExtractPropertyName(parseResult.blendSrc);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendSrc: {{ default: {defVal} }}");
                }
                if (IsPropertyReference(parseResult.blendDst))
                {
                    string propName = ExtractPropertyName(parseResult.blendDst);
                    int defVal = UnityBlendFactorToLayaInt(GetPropertyDefaultInt(propName, props));
                    entries.Add($"        s_BlendDst: {{ default: {defVal} }}");
                }
            }
        }

        // Cull 动态参数
        if (IsPropertyReference(parseResult.cullMode))
        {
            string propName = ExtractPropertyName(parseResult.cullMode);
            int defVal = UnityCullToLayaInt(GetPropertyDefaultInt(propName, props));
            entries.Add($"        s_Cull: {{ default: {defVal} }}");
        }

        // ZWrite 动态参数
        if (IsPropertyReference(parseResult.zWrite))
        {
            string propName = ExtractPropertyName(parseResult.zWrite);
            int defVal = GetPropertyDefaultInt(propName, props);
            entries.Add($"        s_DepthWrite: {{ default: {(defVal == 0 ? "false" : "true")} }}");
        }

        // ZTest 动态参数
        if (IsPropertyReference(parseResult.zTest))
        {
            string propName = ExtractPropertyName(parseResult.zTest);
            int defVal = UnityZTestToLayaInt(GetPropertyDefaultInt(propName, props));
            entries.Add($"        s_DepthTest: {{ default: {defVal} }}");
        }

        // 安全检查：没有 entries 则不生成 styles 块（正常情况下不会触发）
        if (entries.Count == 0)
            return null;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("    styles: {");
        for (int i = 0; i < entries.Count; i++)
        {
            if (i < entries.Count - 1)
                sb.AppendLine(entries[i] + ",");
            else
                sb.AppendLine(entries[i]);
        }
        sb.AppendLine("    },");
        return sb.ToString();
    }

    /// <summary>
    /// 生成renderState代码块，返回生成的字符串（如果没有有效条目则返回null）
    /// </summary>
    private static string GenerateRenderStateBlock(ShaderParseResult parseResult)
    {
        if (!HasRenderState(parseResult))
            return null;

        var props = parseResult.properties;
        List<string> entries = new List<string>();

        // Blend 状态 — 属性引用 [_Prop] 不写入 shader renderState，由材质控制
        if (parseResult.blendSrc != null)
        {
            bool isSeparate = parseResult.blendSrcAlpha != null;

            if (isSeparate)
            {
                entries.Add("                blend: \"Seperate\"");

                // 只写入硬编码值，跳过属性引用
                if (!IsPropertyReference(parseResult.blendSrc))
                {
                    string srcRGB = UnityBlendFactorNameToLayaString(parseResult.blendSrc);
                    if (srcRGB != null) entries.Add($"                srcBlendRGB: \"{srcRGB}\"");
                }
                if (!IsPropertyReference(parseResult.blendDst))
                {
                    string dstRGB = UnityBlendFactorNameToLayaString(parseResult.blendDst);
                    if (dstRGB != null) entries.Add($"                dstBlendRGB: \"{dstRGB}\"");
                }
                if (!IsPropertyReference(parseResult.blendSrcAlpha))
                {
                    string srcA = UnityBlendFactorNameToLayaString(parseResult.blendSrcAlpha);
                    if (srcA != null) entries.Add($"                srcBlendAlpha: \"{srcA}\"");
                }
                if (!IsPropertyReference(parseResult.blendDstAlpha))
                {
                    string dstA = UnityBlendFactorNameToLayaString(parseResult.blendDstAlpha);
                    if (dstA != null) entries.Add($"                dstBlendAlpha: \"{dstA}\"");
                }

                // 分离BlendOp
                if (parseResult.blendOp != null && !IsPropertyReference(parseResult.blendOp))
                {
                    string opRGB = UnityBlendOpNameToLayaString(parseResult.blendOp);
                    if (opRGB != null) entries.Add($"                blendEquationRGB: \"{opRGB}\"");
                }
                if (parseResult.blendOpAlpha != null && !IsPropertyReference(parseResult.blendOpAlpha))
                {
                    string opA = UnityBlendOpNameToLayaString(parseResult.blendOpAlpha);
                    if (opA != null) entries.Add($"                blendEquationAlpha: \"{opA}\"");
                }
            }
            else
            {
                entries.Add("                blend: \"Enable\"");

                // 只写入硬编码值，跳过属性引用
                if (!IsPropertyReference(parseResult.blendSrc))
                {
                    string src = UnityBlendFactorNameToLayaString(parseResult.blendSrc);
                    if (src != null) entries.Add($"                srcBlend: \"{src}\"");
                }
                if (!IsPropertyReference(parseResult.blendDst))
                {
                    string dst = UnityBlendFactorNameToLayaString(parseResult.blendDst);
                    if (dst != null) entries.Add($"                dstBlend: \"{dst}\"");
                }

                // 简单BlendOp
                if (parseResult.blendOp != null && !IsPropertyReference(parseResult.blendOp))
                {
                    string op = UnityBlendOpNameToLayaString(parseResult.blendOp);
                    if (op != null) entries.Add($"                blendEquation: \"{op}\"");
                }
            }
        }

        // ZWrite — 属性引用跳过，由材质控制
        if (parseResult.zWrite != null && !IsPropertyReference(parseResult.zWrite))
        {
            if (string.Equals(parseResult.zWrite, "Off", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add("                depthWrite: false");
            }
            // ZWrite On is default, skip
        }

        // ZTest — 属性引用跳过，由材质控制
        if (parseResult.zTest != null && !IsPropertyReference(parseResult.zTest))
        {
            string zt = UnityZTestNameToLayaString(parseResult.zTest);
            if (zt != null && zt != "LessEqual") // LessEqual is default
            {
                entries.Add($"                depthTest: \"{zt}\"");
            }
        }

        // Cull — 属性引用跳过，由材质控制
        if (parseResult.cullMode != null && !IsPropertyReference(parseResult.cullMode))
        {
            string cull = UnityCullNameToLayaString(parseResult.cullMode);
            if (cull != null && cull != "Back") // Back is default
            {
                entries.Add($"                cull: \"{cull}\"");
            }
        }

        // 没有有效条目则不生成renderState块
        if (entries.Count == 0)
            return null;

        // 构建renderState块
        StringBuilder rsb = new StringBuilder();
        rsb.AppendLine("            renderState: {");
        for (int i = 0; i < entries.Count; i++)
        {
            if (i < entries.Count - 1)
                rsb.AppendLine(entries[i] + ",");
            else
                rsb.AppendLine(entries[i]);
        }
        rsb.AppendLine("            }");
        return rsb.ToString();
    }

    /// <summary>
    /// 检测是否使用顶点颜色
    /// </summary>
    private static bool DetectVertexColorUsage(string cgCode, ShaderParseResult result)
    {
        // 检查appdata/v2f结构体中是否有COLOR语义
        if (!string.IsNullOrEmpty(result.vertexStruct))
        {
            if (Regex.IsMatch(result.vertexStruct, @":\s*COLOR\d*\s*;", RegexOptions.IgnoreCase))
                return true;
        }
        if (!string.IsNullOrEmpty(result.v2fStruct))
        {
            if (Regex.IsMatch(result.v2fStruct, @":\s*COLOR\d*\s*;", RegexOptions.IgnoreCase))
                return true;
        }
        
        // 检查代码中是否使用了顶点颜色相关的变量
        // v.color, i.color, o.color, vertex.color, input.color 等
        string[] colorPatterns = new string[]
        {
            @"\bv\.color\b",
            @"\bi\.color\b",
            @"\bo\.color\b",
            @"\bvertex\.color\b",
            @"\binput\.color\b",
            @"\bIN\.color\b",
            @"\bOUT\.color\b",
            @"\bv\.vcolor\b",
            @"\bi\.vcolor\b",
            @"\bvertexColor\b",
            @"\bv_VertexColor\b",
            @"\ba_Color\b"
        };
        
        foreach (var pattern in colorPatterns)
        {
            if (Regex.IsMatch(cgCode, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        
        // 检查varying映射中是否有颜色
        foreach (var kvp in result.varyings)
        {
            if (kvp.Value.semantic != null && kvp.Value.semantic.StartsWith("COLOR", StringComparison.OrdinalIgnoreCase))
                return true;
            if (kvp.Key.ToLower().Contains("color"))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// 检测是否使用UV
    /// </summary>
    private static bool DetectUVUsage(string cgCode, ShaderParseResult result)
    {
        // 检查appdata/v2f结构体中是否有TEXCOORD语义
        if (!string.IsNullOrEmpty(result.vertexStruct))
        {
            if (Regex.IsMatch(result.vertexStruct, @":\s*TEXCOORD\d*\s*;", RegexOptions.IgnoreCase))
                return true;
        }
        if (!string.IsNullOrEmpty(result.v2fStruct))
        {
            if (Regex.IsMatch(result.v2fStruct, @":\s*TEXCOORD\d*\s*;", RegexOptions.IgnoreCase))
                return true;
        }
        
        // 检查代码中是否使用了UV相关的变量
        string[] uvPatterns = new string[]
        {
            @"\bv\.uv\b",
            @"\bi\.uv\b",
            @"\bo\.uv\b",
            @"\bv\.texcoord\b",
            @"\bi\.texcoord\b",
            @"\btex2D\s*\(",
            @"\btexture2D\s*\(",
            @"\bTRANSFORM_TEX\s*\("
        };
        
        foreach (var pattern in uvPatterns)
        {
            if (Regex.IsMatch(cgCode, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// 提取函数体（使用括号平衡）
    /// </summary>
    private static string ExtractFunctionBody(string cgCode, string funcName)
    {
        // 匹配函数声明
        var funcPattern = $@"\b\w+\s+{funcName}\s*\([^)]*\)(?:\s*:\s*\w+)?\s*\{{";
        var match = Regex.Match(cgCode, funcPattern, RegexOptions.Singleline);
        
        if (!match.Success)
            return "";
        
        int braceStart = match.Index + match.Length - 1;
        int braceCount = 1;
        int i = braceStart + 1;
        
        while (i < cgCode.Length && braceCount > 0)
        {
            char c = cgCode[i];
            if (c == '{') braceCount++;
            else if (c == '}') braceCount--;
            i++;
        }
        
        if (braceCount == 0)
        {
            // 返回函数体内容（不包括外层大括号）
            return cgCode.Substring(braceStart + 1, i - braceStart - 2);
        }
        
        return "";
    }

    /// <summary>
    /// 提取变量声明
    /// </summary>
    private static void ExtractVariables(string cgCode, ShaderParseResult result)
    {
        // 匹配各种变量声明格式
        var patterns = new[]
        {
            @"^\s*(sampler2D|samplerCUBE)\s+(\w+)\s*;",
            @"^\s*(float4?|half4?|int|fixed4?|float4x4|half4x4|float3x3|half3x3|float2|half2|float3|half3)\s+(\w+)\s*;",
            @"^\s*(float4?|half4?|int|fixed4?)\s+(\w+)\s*\[\s*\d+\s*\]\s*;"  // 数组
        };
        
        foreach (var pattern in patterns)
        {
            var varMatches = Regex.Matches(cgCode, pattern, RegexOptions.Multiline);
            foreach (Match m in varMatches)
            {
                string varType = m.Groups[1].Value;
                string varName = m.Groups[2].Value;
                if (!result.variables.ContainsKey(varName))
                {
                    result.variables[varName] = varType;
                }
            }
        }
    }

    /// <summary>
    /// 解析v2f结构体中的varying
    /// </summary>
    private static void ParseVaryings(ShaderParseResult result)
    {
        if (string.IsNullOrEmpty(result.v2fStruct))
            return;
        
        // 解析结构体成员
        var memberMatches = Regex.Matches(result.v2fStruct, 
            @"(float4?|half4?|fixed4?|float2|half2|float3|half3)\s+(\w+)\s*:\s*(\w+)");
        
        int texcoordIndex = 0;
        foreach (Match m in memberMatches)
        {
            string type = m.Groups[1].Value;
            string name = m.Groups[2].Value;
            string semantic = m.Groups[3].Value.ToUpper();
            
            // 跳过SV_POSITION
            if (semantic == "SV_POSITION" || semantic == "POSITION")
                continue;
            
            var info = new VaryingInfo
            {
                originalName = name,
                glslType = ConvertTypeToGLSL(type),
                semantic = semantic
            };
            
            // 根据语义确定GLSL名称
            if (semantic.StartsWith("TEXCOORD"))
            {
                int idx = 0;
                if (semantic.Length > 8)
                    int.TryParse(semantic.Substring(8), out idx);
                info.glslName = $"v_Texcoord{idx}";
            }
            else if (semantic == "COLOR" || semantic == "COLOR0")
            {
                info.glslName = "v_VertexColor";
            }
            else if (semantic == "NORMAL")
            {
                info.glslName = "v_NormalWS";
            }
            else
            {
                info.glslName = "v_" + name;
            }
            
            result.varyings[name] = info;
        }
    }

    /// <summary>
    /// 提取自定义函数（使用括号平衡）
    /// </summary>
    private static void ExtractCustomFunctions(string cgCode, ShaderParseResult result)
    {
        // 匹配函数声明 - 支持更多返回类型
        var funcPattern = @"(float2?|float3?|float4?|half2?|half3?|half4?|fixed2?|fixed3?|fixed4?|void|int|bool|float4x4|float3x3|mat4|mat3)\s+(\w+)\s*\([^)]*\)\s*\{";
        var matches = Regex.Matches(cgCode, funcPattern);
        
        HashSet<string> addedFunctions = new HashSet<string>();
        
        foreach (Match m in matches)
        {
            string funcName = m.Groups[2].Value;
            
            // 跳过vert和frag以及已添加的函数
            if (funcName == "vert" || funcName == "frag" || addedFunctions.Contains(funcName))
                continue;
            
            // 找到函数体（使用括号平衡）
            int startIndex = m.Index;
            int braceStart = cgCode.IndexOf('{', startIndex);
            if (braceStart < 0) continue;
            
            int braceCount = 1;
            int i = braceStart + 1;
            while (i < cgCode.Length && braceCount > 0)
            {
                if (cgCode[i] == '{') braceCount++;
                else if (cgCode[i] == '}') braceCount--;
                i++;
            }
            
            if (braceCount == 0)
            {
                string funcCode = cgCode.Substring(startIndex, i - startIndex);
                result.customFunctions.Add(funcCode);
                addedFunctions.Add(funcName);
            }
        }
    }

    // LayaAir引擎内置Uniform变量列表（不需要在材质中重新定义）
    // 参考 LayaAir_BuiltIn_Uniforms.md 文档
    private static readonly HashSet<string> EngineBuiltInUniforms = new HashSet<string>
    {
        // 场景相关 (SceneCommon.glsl)
        "u_Time", "u_FogParams", "u_FogColor", "u_GIRotate", "u_DirationLightCount",

        // 相机相关 (CameraCommon.glsl)
        "u_CameraPos", "u_View", "u_Projection", "u_ViewProjection",
        "u_CameraDirection", "u_CameraUp", "u_Viewport", "u_ProjectionParams",
        "u_OpaqueTextureParams", "u_ZBufferParams",
        "u_CameraDepthTexture", "u_CameraDepthNormalsTexture", "u_CameraOpaqueTexture",

        // 精灵/物体相关 (Sprite3DCommon.glsl)
        "u_WorldMat", "u_WorldInvertFront",

        // 光照相关 (Lighting.glsl)
        "u_DirLightColor", "u_DirLightDirection", "u_DirLightMode",
        "u_PointLightColor", "u_PointLightPos", "u_PointLightRange", "u_PointLightMode",
        "u_SpotLightColor", "u_SpotLightPos", "u_SpotLightDirection", "u_SpotLightRange", "u_SpotLightSpot", "u_SpotLightMode",
        "u_LightBuffer", "u_LightClusterBuffer",

        // 阴影相关 (ShadowCommon.glsl)
        "u_ShadowLightDirection", "u_ShadowBias", "u_ShadowSplitSpheres", "u_ShadowMatrices",
        "u_ShadowMapSize", "u_ShadowParams", "u_SpotShadowMapSize", "u_SpotViewProjectMatrix",
        "u_ShadowMap", "u_SpotShadowMap",

        // 全局光照相关 (globalIllumination.glsl)
        "u_AmbientColor", "u_IblSH", "u_IBLTex", "u_IBLRoughnessLevel", "u_AmbientIntensity", "u_ReflectionIntensity",
        "u_AmbientSHAr", "u_AmbientSHAg", "u_AmbientSHAb", "u_AmbientSHBr", "u_AmbientSHBg", "u_AmbientSHBb", "u_AmbientSHC",
        "u_ReflectTexture", "u_ReflectCubeHDRParams",
        "u_LightMap", "u_LightMapDirection", "u_LightmapScaleOffset",
        "u_SpecCubeProbePosition", "u_SpecCubeBoxMax", "u_SpecCubeBoxMin",

        // 体积光照探针 (VolumetricGI.glsl)
        "u_VolGIProbeCounts", "u_VolGIProbeStep", "u_VolGIProbeStartPosition", "u_VolGIProbeParams",
        "u_ProbeIrradiance", "u_ProbeDistance",

        // 粒子系统相关 (particleShuriKenSpriteVS.glsl)
        // ⭐ 这些uniform由particleShuriKenSpriteVS.glsl提供，不要在uniformMap中重复声明
        "u_CurrentTime", "u_Gravity", "u_DragConstanct",
        "u_WorldPosition", "u_WorldRotation",
        "u_ScalingMode", "u_PositionScale", "u_SizeScale", "u_SimulationSpace",
        "u_VOLSpaceType", "u_VOLVelocityConst", "u_VOLVelocityConstMax",
        // Gradient相关的uniform也由particleShuriKenSpriteVS.glsl提供
        "u_ColorOverLifeGradientAlphas", "u_ColorOverLifeGradientColors", "u_ColorOverLifeGradientRanges",
        "u_MaxColorOverLifeGradientAlphas", "u_MaxColorOverLifeGradientColors", "u_MaxColorOverLifeGradientRanges",
        "u_SOLSizeGradient", "u_SOLSizeGradientMax",
        "u_SOLSizeGradientX", "u_SOLSizeGradientY", "u_SOLSizeGradientZ",
        "u_SOLSizeGradientMaxX", "u_SOLSizeGradientMaxY", "u_SOLSizeGradientMaxZ",
        "u_VOLVelocityGradientX", "u_VOLVelocityGradientY", "u_VOLVelocityGradientZ",
        "u_VOLVelocityGradientMaxX", "u_VOLVelocityGradientMaxY", "u_VOLVelocityGradientMaxZ",

        // Unity RenderPipeline特有，Laya粒子不需要
        "u_Stencil", "u_StencilComp", "u_StencilOp", "u_StencilReadMask", "u_StencilWriteMask", "u_ColorMask"
    };

    /// <summary>
    /// 检查是否是引擎内置Uniform
    /// </summary>
    private static bool IsEngineBuiltInUniform(string uniformName)
    {
        return EngineBuiltInUniforms.Contains(uniformName);
    }

    /// <summary>
    /// 从属性和解析结果生成uniformMap
    /// </summary>
    private static void GenerateUniformMapFromProperties(StringBuilder sb, List<ShaderProperty> properties, ShaderParseResult parseResult, LayaShaderType shaderType = LayaShaderType.D3)
    {
        bool is2D = (shaderType == LayaShaderType.D2_BaseRenderNode2D || shaderType == LayaShaderType.D2_TextureSV);

        // 2D shader不需要3D的默认uniform（u_AlphaTestValue, u_TilingOffset等）
        if (!is2D)
        {
            sb.AppendLine("        // Basic");
            sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
            sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");
        }

        // 已添加的属性（包括引擎内置的，避免重复）
        HashSet<string> addedProps = new HashSet<string>(EngineBuiltInUniforms);
        if (!is2D)
        {
            addedProps.Add("u_AlphaTestValue");
            addedProps.Add("u_TilingOffset");
        }

        sb.AppendLine();
        sb.AppendLine(is2D ? "        // 2D Shader Properties" : "        // Shader Properties");

        // 收集需要_ST的纹理
        List<string> texturesNeedingST = new List<string>();

        foreach (var prop in properties)
        {
            // 2D shader：跳过内置属性（_MainTex → u_baseRender2DTexture由引擎提供、stencil相关等）
            if (is2D && Is2DBuiltinProperty(prop.unityName))
            {
                ExportLogger.Log($"LayaAir3D: [2D] Skipping built-in 2D property: {prop.unityName}");
                continue;
            }

            // 2D shader：使用2D专用属性名映射
            string effectiveLayaName = is2D ? Get2DPropertyName(prop.unityName) : prop.layaName;

            // 跳过引擎内置变量
            if (IsEngineBuiltInUniform(effectiveLayaName))
            {
                ExportLogger.Log($"LayaAir3D: Skipping engine built-in uniform: {effectiveLayaName}");
                continue;
            }

            // 2D shader：跳过3D默认属性（u_AlbedoTexture, u_AlbedoColor等由_MainTex/_Color映射而来）
            if (is2D && (effectiveLayaName == "u_AlbedoTexture" || effectiveLayaName == "u_AlbedoColor"
                || effectiveLayaName == "u_AlbedoIntensity"))
            {
                ExportLogger.Log($"LayaAir3D: [2D] Skipping 3D default uniform: {effectiveLayaName} (from {prop.unityName})");
                continue;
            }

            if (addedProps.Contains(effectiveLayaName))
                continue;
            addedProps.Add(effectiveLayaName);

            // 2D shader使用覆盖后的layaName生成uniform行
            if (is2D && effectiveLayaName != prop.layaName)
            {
                // 创建临时属性副本，使用2D映射后的名称
                var prop2D = prop;
                string uniformLine = GenerateUniformLineWithName(prop2D, effectiveLayaName);
                sb.AppendLine($"        {uniformLine}");
            }
            else
            {
                string uniformLine = GenerateUniformLine(prop);
                sb.AppendLine($"        {uniformLine}");
            }

            // 2D shader不需要纹理的_ST（TilingOffset）
            if (!is2D && prop.type == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                texturesNeedingST.Add(prop.layaName);
            }
        }

        // 生成纹理的_ST uniform（用于TRANSFORM_TEX）- 2D shader跳过
        if (!is2D)
        {
            sb.AppendLine();
            sb.AppendLine("        // Texture Tiling/Offset");
            foreach (var texName in texturesNeedingST)
            {
                string stName = texName + "_ST";
                if (!addedProps.Contains(stName) && !IsEngineBuiltInUniform(stName))
                {
                    sb.AppendLine($"        {stName}: {{ type: Vector4, default: [1, 1, 0, 0] }},");
                    addedProps.Add(stName);
                }
            }
        }
        
        // 添加常用的_ST（可能在代码中使用但不在属性中）
        // 注意：主纹理的Tiling/Offset使用u_TilingOffset（Laya粒子/特效约定），不需要u_MainTex_ST/u_AlbedoTexture_ST
        // u_NormalTexture_ST/u_DetailTex_ST只在实际包含对应纹理时才通过texturesNeedingST添加
        // （此处intentionally为空，避免生成无关的ST uniform）

        // ⭐ 添加Scroll相关uniforms（如果检测到有Scroll属性或者是粒子shader）
        // 2D shader不需要UV Scroll相关uniforms
        // 如果shader使用Vector4类型的UVScroll（_UVScrollTex/_UVScrollMask），
        // 则已通过properties循环生成了u_UVScrollTex/u_UVScrollMask，不需要float分量形式
        // 使用精确Laya名匹配（数据驱动），配置见 UVScrollVectorLayaNames / UVScrollFloatNamePatterns
        bool hasVectorUVScroll = HasAnyPropertyByLayaName(parseResult, UVScrollVectorLayaNames);
        if (!is2D && (parseResult.isParticleBillboard || UVScrollFloatNamePatterns.Any(p => HasPropertyByName(parseResult, p))) && !hasVectorUVScroll)
        {
            // 基础Scroll uniforms (Layer 0) - 仅用于float分量型scroll（非Vector4型）
            if (!addedProps.Contains("u_Scroll0X"))
            {
                sb.AppendLine($"        u_Scroll0X: {{ type: Float, default: 0.0 }},");
                addedProps.Add("u_Scroll0X");
            }
            if (!addedProps.Contains("u_Scroll0Y"))
            {
                sb.AppendLine($"        u_Scroll0Y: {{ type: Float, default: 0.0 }},");
                addedProps.Add("u_Scroll0Y");
            }

            // Scroll1 uniforms (Layer 1) - 如果有Scroll1或DetailTex属性
            if (HasPropertyByName(parseResult, "Scroll1") || HasPropertyByName(parseResult, "DetailTex"))
            {
                if (!addedProps.Contains("u_Scroll1X"))
                {
                    sb.AppendLine($"        u_Scroll1X: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Scroll1X");
                }
                if (!addedProps.Contains("u_Scroll1Y"))
                {
                    sb.AppendLine($"        u_Scroll1Y: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Scroll1Y");
                }
            }

            // Scroll2 uniforms (Layer 2) - 如果有Scroll2或DetailTex2属性
            if (HasPropertyByName(parseResult, "Scroll2") || HasPropertyByName(parseResult, "DetailTex2"))
            {
                if (!addedProps.Contains("u_Scroll2X"))
                {
                    sb.AppendLine($"        u_Scroll2X: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Scroll2X");
                }
                if (!addedProps.Contains("u_Scroll2Y"))
                {
                    sb.AppendLine($"        u_Scroll2Y: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Scroll2Y");
                }
            }

            // Distort Scroll uniforms - 如果有Distort0属性
            if (HasPropertyByName(parseResult, "Distort0"))
            {
                if (!addedProps.Contains("u_Distort0X"))
                {
                    sb.AppendLine($"        u_Distort0X: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Distort0X");
                }
                if (!addedProps.Contains("u_Distort0Y"))
                {
                    sb.AppendLine($"        u_Distort0Y: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_Distort0Y");
                }
            }

            // DissolveDistort Scroll uniforms - 如果有DissolveDistort属性
            if (HasPropertyByName(parseResult, "DissolveDistort"))
            {
                if (!addedProps.Contains("u_DissolveDistortX"))
                {
                    sb.AppendLine($"        u_DissolveDistortX: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_DissolveDistortX");
                }
                if (!addedProps.Contains("u_DissolveDistortY"))
                {
                    sb.AppendLine($"        u_DissolveDistortY: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_DissolveDistortY");
                }
            }

            // VertexAmplitudeTex Scroll uniforms - 如果有VertexAmplitudeTex属性
            if (HasPropertyByName(parseResult, "VertexAmplitudeTex"))
            {
                if (!addedProps.Contains("u_VertexAmplitudeTexScroll0X"))
                {
                    sb.AppendLine($"        u_VertexAmplitudeTexScroll0X: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_VertexAmplitudeTexScroll0X");
                }
                if (!addedProps.Contains("u_VertexAmplitudeTexScroll0Y"))
                {
                    sb.AppendLine($"        u_VertexAmplitudeTexScroll0Y: {{ type: Float, default: 0.0 }},");
                    addedProps.Add("u_VertexAmplitudeTexScroll0Y");
                }
            }
        }

        // ⭐ 不需要手动添加粒子系统uniforms
        // 所有粒子系统相关的uniform（u_CurrentTime, u_Gravity, u_WorldPosition等）
        // 都由particleShuriKenSpriteVS.glsl提供，已添加到EngineBuiltInUniforms列表中
        // 重复声明会导致编译错误
    }

    /// <summary>
    /// 从解析结果生成defines
    /// </summary>
    private static void GenerateDefinesFromParseResult(StringBuilder sb, ShaderParseResult parseResult, LayaShaderType shaderType = LayaShaderType.D3)
    {
        HashSet<string> addedDefines = new HashSet<string>();
        bool is2D = (shaderType == LayaShaderType.D2_BaseRenderNode2D || shaderType == LayaShaderType.D2_TextureSV);

        // 粒子shader使用TINTCOLOR/ADDTIVEFOG/RENDERMODE_MESH（参考Particle.shader模板），非粒子使用COLOR/ENABLEVERTEXCOLOR
        if (is2D)
        {
            // ★ 2D shader使用 BASERENDER2D（参考 baseRender2D.json 模板）
            sb.AppendLine("        BASERENDER2D: { type: bool, default: true },");
            addedDefines.Add("BASERENDER2D");
        }
        else if (parseResult.isParticleBillboard)
        {
            // ⭐ 粒子mesh模式：添加RENDERMODE_MESH define（用于区分mesh和billboard模式）
            sb.AppendLine("        RENDERMODE_MESH: { type: bool, default: false },");
            addedDefines.Add("RENDERMODE_MESH");

            sb.AppendLine("        TINTCOLOR: { type: bool, default: true },");
            addedDefines.Add("TINTCOLOR");
            sb.AppendLine("        ADDTIVEFOG: { type: bool, default: true },");
            addedDefines.Add("ADDTIVEFOG");
        }
        else
        {
            // 如果使用了UV，添加UV宏
            if (parseResult.usesUV)
            {
                sb.AppendLine("        UV: { type: bool, default: true },");
                addedDefines.Add("UV");
            }
        }
        
        // 从shader_feature互斥组提取defines（第一个default:true，其余false）
        foreach (var group in parseResult.shaderFeatureGroups)
        {
            for (int i = 0; i < group.Count; i++)
            {
                string clean = NormalizeDefineName(group[i]);
                if (!string.IsNullOrEmpty(clean) && !addedDefines.Contains(clean))
                {
                    bool isDefault = (i == 0);
                    sb.AppendLine($"        {clean}: {{ type: bool, default: {isDefault.ToString().ToLower()} }},");
                    addedDefines.Add(clean);
                }
            }
        }

        // 从shader_feature提取defines（互斥组已处理的会被跳过）
        foreach (var feature in parseResult.shaderFeatures)
        {
            string cleanFeature = NormalizeDefineName(feature);

            if (!string.IsNullOrEmpty(cleanFeature) && !addedDefines.Contains(cleanFeature))
            {
                sb.AppendLine($"        {cleanFeature}: {{ type: bool, default: false }},");
                addedDefines.Add(cleanFeature);
            }
        }
        
        // 从multi_compile提取defines
        foreach (var compile in parseResult.multiCompiles)
        {
            string cleanCompile = NormalizeDefineName(compile);
            
            if (!string.IsNullOrEmpty(cleanCompile) && !addedDefines.Contains(cleanCompile))
            {
                sb.AppendLine($"        {cleanCompile}: {{ type: bool, default: false }},");
                addedDefines.Add(cleanCompile);
            }
        }
        
        // 从properties推断defines
        InferDefinesFromProperties(sb, parseResult, addedDefines);

        // 添加常用的defines（可能在代码中使用）
        // 粒子Effect shader常用：LAYERTYPE_ONE/TWO/THREE, WRAPMODE_DEFAULT/CLAMP/REPEAT
        string[] commonDefines = {
            "LAYERTYPE_ONE", "LAYERTYPE_TWO", "LAYERTYPE_THREE",
            "WRAPMODE_DEFAULT", "WRAPMODE_CLAMP", "WRAPMODE_REPEAT", "WRAPMODE_MIRROR",
            "USERIM", "USERIMMAP", "USENPR", "EMISSION", "USELIGHTING",
            "USEVERTEXOFFSET", "USEDISSOLVE", "USEFADEEDGE", "USEDISTORT0",
            "USECUSTOMDATA", "USEGRADIENTMAP0", "USENORMALMAPFORRIM", "USEDISSOLVEDISTORT",
            "ROTATIONTEX", "ROTATIONTEXTWO", "ROTATIONTEXTHREE", "ROTATIONTEXFOUR",
            "USEPOLAR"
        };
        foreach (var def in commonDefines)
        {
            if (!addedDefines.Contains(def))
            {
                // 检查代码中是否使用了这个宏
                bool usedInCode = false;
                if (!string.IsNullOrEmpty(parseResult.vertexCode) && parseResult.vertexCode.Contains(def))
                    usedInCode = true;
                if (!string.IsNullOrEmpty(parseResult.fragmentCode) && parseResult.fragmentCode.Contains(def))
                    usedInCode = true;
                foreach (var func in parseResult.customFunctions)
                {
                    if (func.Contains(def))
                    {
                        usedInCode = true;
                        break;
                    }
                }

                if (usedInCode)
                {
                    sb.AppendLine($"        {def}: {{ type: bool, default: false }},");
                    addedDefines.Add(def);
                }
            }
        }

        // ⭐ 通用方案：从代码中的 #ifdef/#ifndef/#if defined() 自动检测所有引用的 define
        // 解决 §7.3 类问题：defines 名称与 GLSL 代码不匹配、缺失 define 等
        AutoDetectDefinesFromCode(sb, parseResult, addedDefines);
    }

    /// <summary>
    /// 从 shader 代码中自动检测所有 #ifdef/#ifndef/#if defined() 引用的宏名称，
    /// 补充缺失的 defines 并修正名称不匹配（如 USEPOLAR→POLAR）
    /// </summary>
    private static void AutoDetectDefinesFromCode(StringBuilder sb, ShaderParseResult parseResult, HashSet<string> addedDefines)
    {
        // 收集所有代码文本
        var allCode = new StringBuilder();
        if (!string.IsNullOrEmpty(parseResult.vertexCode))
            allCode.AppendLine(parseResult.vertexCode);
        if (!string.IsNullOrEmpty(parseResult.fragmentCode))
            allCode.AppendLine(parseResult.fragmentCode);
        foreach (var func in parseResult.customFunctions)
            allCode.AppendLine(func);
        string code = allCode.ToString();
        if (string.IsNullOrEmpty(code))
            return;

        // 提取代码中所有 #ifdef / #ifndef / #if defined() 引用的宏名
        var codeDefines = new HashSet<string>();
        // #ifdef XXX / #ifndef XXX
        foreach (Match m in Regex.Matches(code, @"#\s*ifn?def\s+(\w+)"))
            codeDefines.Add(m.Groups[1].Value);
        // #if defined(XXX) / #elif defined(XXX)
        foreach (Match m in Regex.Matches(code, @"defined\s*\(\s*(\w+)\s*\)"))
            codeDefines.Add(m.Groups[1].Value);

        // 过滤掉 GLSL/Unity 内建宏和粒子系统基础宏（这些由引擎自动提供）
        string[] builtinMacros = {
            "GL_ES", "HIGHP", "MEDIUMP", "LOWP",
            "RENDERMODE_MESH", "TINTCOLOR", "ADDTIVEFOG",
            "COLOR", "UV", "ENABLEVERTEXCOLOR"
        };
        foreach (var b in builtinMacros)
            codeDefines.Remove(b);

        // 构建 "USE前缀 → 已添加define" 的反向查找
        // 例如 addedDefines 含 "USEPOLAR" → usePrefixMap["POLAR"] = "USEPOLAR"
        var usePrefixMap = new Dictionary<string, string>();
        foreach (var existing in addedDefines)
        {
            if (existing.StartsWith("USE") && existing.Length > 3)
            {
                string stripped = existing.Substring(3); // USEPOLAR → POLAR
                if (!usePrefixMap.ContainsKey(stripped))
                    usePrefixMap[stripped] = existing;
            }
        }

        foreach (var codeDef in codeDefines)
        {
            if (addedDefines.Contains(codeDef))
                continue; // 已存在，无需处理

            // 检查是否有 USE 前缀版本已经在 defines 中
            // 例如代码中用 #ifdef POLAR 但 defines 中已有 USEPOLAR
            if (usePrefixMap.TryGetValue(codeDef, out string existingWithPrefix))
            {
                // 替换 StringBuilder 中的旧名称为代码实际引用的名称
                string oldLine = $"        {existingWithPrefix}: ";
                string newLine = $"        {codeDef}: ";
                string sbStr = sb.ToString();
                if (sbStr.Contains(oldLine))
                {
                    sb.Clear();
                    sb.Append(sbStr.Replace(oldLine, newLine));
                    addedDefines.Remove(existingWithPrefix);
                    addedDefines.Add(codeDef);
                    ExportLogger.Log($"LayaAir3D: Renamed define '{existingWithPrefix}' → '{codeDef}' to match code #ifdef");
                }
                continue;
            }

            // 也检查反向：代码中用 #ifdef USEXXX 但 defines 中已有 XXX
            if (codeDef.StartsWith("USE") && codeDef.Length > 3)
            {
                string stripped = codeDef.Substring(3);
                if (addedDefines.Contains(stripped))
                    continue; // XXX 已存在，USEXXX 是等价的
            }

            // 全新的、未覆盖的 define → 添加为 default:false
            sb.AppendLine($"        {codeDef}: {{ type: bool, default: false }},");
            addedDefines.Add(codeDef);
            ExportLogger.Log($"LayaAir3D: Auto-detected missing define '{codeDef}' from code #ifdef");
        }
    }

    /// <summary>
    /// 从properties推断需要的defines（数据驱动，规则定义在 FeatureInferenceRules 中）
    /// </summary>
    private static void InferDefinesFromProperties(StringBuilder sb, ShaderParseResult parseResult, HashSet<string> addedDefines)
    {
        HashSet<string> inferredDefines = new HashSet<string>();

        foreach (var prop in parseResult.properties)
        {
            string propName = prop.unityName.ToLower();

            // --- 数据驱动的通用规则匹配 ---
            foreach (var rule in FeatureInferenceRules)
            {
                // 排除条件检查
                if (rule.exclude != null && propName.Contains(rule.exclude))
                    continue;

                // 命中条件检查（任一 pattern 匹配即触发）
                bool matched = false;
                foreach (var pattern in rule.patterns)
                {
                    if (propName.Contains(pattern)) { matched = true; break; }
                }
                if (!matched) continue;

                // 添加关联的 defines
                foreach (var def in rule.defines)
                    inferredDefines.Add(def);
            }

            // --- 特殊规则：旋转纹理（按后缀数字区分旋转层，逻辑含变体分支） ---
            // 可扩展：如需新增旋转层，在此添加对应的 else if 分支
            if (propName.Contains("rotateangle"))
            {
                if      (propName.Contains("02")) inferredDefines.Add("ROTATIONTEXTWO");
                else if (propName.Contains("03")) inferredDefines.Add("ROTATIONTEXTHREE");
                else if (propName.Contains("04")) inferredDefines.Add("ROTATIONTEXFOUR");
                else if (!propName.Contains("0")) inferredDefines.Add("ROTATIONTEX"); // 排除02/03/04，保留基础旋转
            }

            // --- 特殊规则：NormalMap+Rim 组合属性（AND 条件，如 _RimNormalMap）---
            if (propName.Contains("normalmap") && propName.Contains("rim"))
                inferredDefines.Add("USENORMALMAPFORRIM");
        }

        // 输出推断出的 defines
        foreach (var def in inferredDefines.OrderBy(d => d))
        {
            if (!addedDefines.Contains(def))
            {
                sb.AppendLine($"        {def}: {{ type: bool, default: false }},");
                addedDefines.Add(def);
            }
        }
    }

    /// <summary>
    /// 从函数代码中提取函数名
    /// </summary>
    private static string ExtractFunctionName(string funcCode)
    {
        var match = Regex.Match(funcCode, @"\b(\w+)\s*\(");
        if (match.Success)
        {
            string name = match.Groups[1].Value;
            // 跳过类型名
            string[] types = { "float", "vec2", "vec3", "vec4", "mat2", "mat3", "mat4", "int", "bool", "void", "half", "fixed" };
            if (!Array.Exists(types, t => t == name))
            {
                return name;
            }
            // 如果第一个匹配是类型，找下一个
            match = Regex.Match(funcCode, @"\b(?:float|vec2|vec3|vec4|mat2|mat3|mat4|int|bool|void|half|fixed)\s+(\w+)\s*\(");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>
    /// 从完整的shader内容中提取指定函数的代码
    /// </summary>
    private static string ExtractFunctionFromContent(string content, string functionName)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(functionName))
            return null;

        // 匹配函数签名：返回类型 函数名(...)
        var signaturePattern = $@"(?:vec2|vec3|vec4|float|int|bool|void|mat2|mat3|mat4)\s+{Regex.Escape(functionName)}\s*\([^\)]*\)";
        var match = Regex.Match(content, signaturePattern);

        if (!match.Success)
            return null;

        int funcStart = match.Index;
        int braceStart = content.IndexOf('{', match.Index + match.Length);

        if (braceStart < 0)
            return null;

        // 使用括号计数找到匹配的右花括号
        int braceCount = 1;
        int i = braceStart + 1;

        while (i < content.Length && braceCount > 0)
        {
            if (content[i] == '{')
                braceCount++;
            else if (content[i] == '}')
                braceCount--;
            i++;
        }

        if (braceCount == 0)
        {
            return content.Substring(funcStart, i - funcStart);
        }

        return null;
    }

    /// <summary>
    /// 标准化宏定义名称
    /// </summary>
    private static string NormalizeDefineName(string feature)
    {
        string cleanFeature = feature.Trim();

        // 跳过空的或者__开头的内部宏
        if (string.IsNullOrEmpty(cleanFeature) || cleanFeature.StartsWith("__"))
            return null;

        // ⭐ 修复：跳过以#开头的非法标识符（如#pragma、#include等）
        if (cleanFeature.StartsWith("#"))
            return null;

        // ⭐ 过滤文件名（如 UnityCG.cginc 从 #include 泄漏到 defines）
        string[] invalidExts = { ".cginc", ".hlsl", ".glsl", ".shader", ".cg", ".compute" };
        foreach (var ext in invalidExts)
            if (cleanFeature.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return null;

        // ⭐ C标识符合法性检查（defines 必须是合法的 C 标识符）
        if (!Regex.IsMatch(cleanFeature, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return null;

        // ⭐ 修复：跳过pragma指令的参数关键词（这些不是defines）
        string[] pragmaKeywords = {
            "pragma", "include", "define", "ifdef", "ifndef", "endif",
            "vertex", "fragment", "geometry", "hull", "domain",
            "target", "only_renderers", "exclude_renderers",
            "multi_compile", "shader_feature", "require",
            "skip_variants", "hardware_tier_variants",
            // 平台名称
            "xbox360", "xboxone", "ps3", "ps4", "ps5", "psp2",
            "n3ds", "wiiu", "switch", "gles", "gles3",
            "metal", "vulkan", "d3d9", "d3d11", "d3d12",
            "glcore", "xboxseries", "ps5", "stadia"
        };
        string lowerFeature = cleanFeature.ToLower();
        foreach (var keyword in pragmaKeywords)
        {
            if (lowerFeature == keyword || lowerFeature.Contains(keyword + "_"))
                return null;
        }

        // 移除前导下划线
        if (cleanFeature.StartsWith("_"))
            cleanFeature = cleanFeature.Substring(1);

        // 处理_ON后缀
        if (cleanFeature.EndsWith("_ON"))
            cleanFeature = cleanFeature.Substring(0, cleanFeature.Length - 3);

        // 跳过常见的Unity内部宏
        string[] skipMacros = {
            "INSTANCING", "PROCEDURAL", "DOTS", "STEREO",
            "FOG_LINEAR", "FOG_EXP", "FOG_EXP2",
            "SHADOWS_SCREEN", "SHADOWS_SOFT", "SHADOWS_HARD"
        };
        foreach (var skip in skipMacros)
        {
            if (cleanFeature.Contains(skip))
                return null;
        }

        return cleanFeature;
    }

    /// <summary>
    /// 预处理：转换所有代码并收集所有varying
    /// 确保VS和FS使用相同的varying声明
    /// </summary>
    private static void PreprocessAndCollectVaryings(ShaderParseResult parseResult)
    {
        // 收集基础varying
        Dictionary<string, string> allVaryings = CollectAllVaryings(parseResult);
        
        // 转换顶点着色器代码并提取varying
        if (!string.IsNullOrEmpty(parseResult.vertexCode))
        {
            string convertedVertCode = ConvertVertexShaderCode(parseResult.vertexCode, parseResult);
            ExtractVaryingsFromCode(convertedVertCode, allVaryings);
        }
        
        // 总是从默认顶点代码中提取varying（因为可能会用到）
        StringBuilder defaultVS = new StringBuilder();
        GenerateDefaultVertexCode(defaultVS, parseResult);
        ExtractVaryingsFromCode(defaultVS.ToString(), allVaryings);
        
        // 转换片元着色器代码并提取varying
        if (!string.IsNullOrEmpty(parseResult.fragmentCode))
        {
            string convertedFragCode = ConvertFragmentShaderCode(parseResult.fragmentCode, parseResult);
            ExtractVaryingsFromCode(convertedFragCode, allVaryings);
        }
        
        // 总是从默认片元代码中提取varying（因为可能会用到）
        StringBuilder defaultFS = new StringBuilder();
        GenerateDefaultFragmentCode(defaultFS, parseResult);
        ExtractVaryingsFromCode(defaultFS.ToString(), allVaryings);
        
        // 转换自定义函数并提取varying
        foreach (var func in parseResult.customFunctions)
        {
            string convertedFunc = ConvertHLSLFunction(func);
            ExtractVaryingsFromCode(convertedFunc, allVaryings);
        }
        
        // 保存收集到的varying
        parseResult.collectedVaryings = allVaryings;
    }

    /// <summary>
    /// 生成转换后的顶点着色器
    /// </summary>
    private static void GenerateConvertedVertexShader(StringBuilder sb, string shaderName, ShaderParseResult parseResult)
    {
        // 转换代码
        string convertedVertCode = "";
        List<string> convertedFunctions = new List<string>();
        
        if (!string.IsNullOrEmpty(parseResult.vertexCode))
        {
            convertedVertCode = ConvertVertexShaderCode(parseResult.vertexCode, parseResult);
        }
        
        // 转换自定义函数 - 包含全部函数（而不是仅直接引用的）
        // 原因：VS中的函数可能有传递依赖（如perlinNoise调用randomVec），
        // 只包含vertexCode直接引用的函数会导致被调用的辅助函数缺失
        foreach (var func in parseResult.customFunctions)
        {
            convertedFunctions.Add(ConvertHLSLFunction(func));
        }
        
        // 收集所有varying并保存到parseResult，确保VS和FS使用相同的varying
        Dictionary<string, string> allVaryings = CollectAllVaryings(parseResult);
        
        // 从VS转换后的代码中提取varying
        ExtractVaryingsFromCode(convertedVertCode, allVaryings);
        foreach (var func in convertedFunctions)
        {
            ExtractVaryingsFromCode(func, allVaryings);
        }
        
        // 同时从FS代码中提取varying（确保VS和FS的varying完全一致）
        if (!string.IsNullOrEmpty(parseResult.fragmentCode))
        {
            string convertedFragCode = ConvertFragmentShaderCode(parseResult.fragmentCode, parseResult);
            ExtractVaryingsFromCode(convertedFragCode, allVaryings);
        }
        
        // 从所有自定义函数中提取varying
        foreach (var func in parseResult.customFunctions)
        {
            string convertedFunc = ConvertHLSLFunction(func);
            ExtractVaryingsFromCode(convertedFunc, allVaryings);
        }

        // ⭐ 关键修复：如果是粒子shader，添加粒子专用的varying
        // ParticleShaderTemplate使用的varying: v_Color, v_TextureCoordinate, v_ScreenPos, v_Texcoord0
        // 这些varying不会从Unity代码中提取（因为粒子shader使用ParticleShaderTemplate，不使用Unity转换代码）
        if (parseResult.isParticleBillboard)
        {
            ExportLogger.Log("LayaAir3D: Adding particle-specific varyings");

            // 添加粒子专用的varying（如果还没有）
            if (!allVaryings.ContainsKey("v_Color"))
            {
                allVaryings["v_Color"] = "vec4";
                ExportLogger.Log("LayaAir3D: Added varying vec4 v_Color");
            }

            if (!allVaryings.ContainsKey("v_TextureCoordinate"))
            {
                allVaryings["v_TextureCoordinate"] = "vec2";
                ExportLogger.Log("LayaAir3D: Added varying vec2 v_TextureCoordinate");
            }

            if (!allVaryings.ContainsKey("v_ScreenPos"))
            {
                allVaryings["v_ScreenPos"] = "vec4";
                ExportLogger.Log("LayaAir3D: Added varying vec4 v_ScreenPos");
            }

            // ⭐ 关键修复：粒子mesh模式需要v_MeshColor传递顶点颜色
            if (!allVaryings.ContainsKey("v_MeshColor"))
            {
                allVaryings["v_MeshColor"] = "vec4";
                ExportLogger.Log("LayaAir3D: Added varying vec4 v_MeshColor for particle mesh mode");
            }

            // ⭐ 关键修复：粒子shader需要v_Texcoord0为vec4（用于Scroll功能的.zw分量）
            if (!allVaryings.ContainsKey("v_Texcoord0"))
            {
                allVaryings["v_Texcoord0"] = "vec4";
                ExportLogger.Log("LayaAir3D: Added varying vec4 v_Texcoord0 for particle shader");
            }
            else if (allVaryings["v_Texcoord0"] == "vec2")
            {
                // 如果已经存在但是vec2，强制改为vec4
                allVaryings["v_Texcoord0"] = "vec4";
                ExportLogger.Log("LayaAir3D: Changed v_Texcoord0 from vec2 to vec4 for particle shader");
            }

            // ⭐ 优化：移除粒子shader不需要的varying
            // v_PositionCS和v_PositionWS来自Unity mesh shader，粒子shader中从未使用
            // v_Texcoord2/3/8用于法线贴图（TBN矩阵），仅在USELIGHTING时需要
            // v_Texcoord7用于自定义数据，仅在USECUSTOMDATA时需要
            // 保留这些varying的声明，但它们可能不会被赋值（除非启用相应的特性）

            string[] unusedVaryingsForParticles = { "v_PositionCS", "v_PositionWS" };
            foreach (var unusedVarying in unusedVaryingsForParticles)
            {
                if (allVaryings.ContainsKey(unusedVarying))
                {
                    allVaryings.Remove(unusedVarying);
                    ExportLogger.Log($"LayaAir3D: Removed unused varying for particle shader: {unusedVarying}");
                }
            }
        }

        // 保存到parseResult，供FS使用
        parseResult.collectedVaryings = allVaryings;

        // 开始生成shader
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        
        // 根据是否是粒子shader选择不同的includes
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader的VS includes（参考Particle.shader模板）

            // ⭐ 包含particleShuriKenSpriteVS.glsl以获得正确的uniform和宏声明
            // 这个include提供了：
            // 1. gradient函数所需的uniform（Buffer类型等）
            // 2. COLORCOUNT和COLORCOUNT_HALF宏定义
            // 必须在MathGradient.glsl之前include
            sb.AppendLine("    #include \"Camera.glsl\";");
            sb.AppendLine("    #include \"particleShuriKenSpriteVS.glsl\";");
            sb.AppendLine();

            // ⭐ 不需要手动定义COLORCOUNT宏，particleShuriKenSpriteVS.glsl已经定义了

            sb.AppendLine("    #include \"Math.glsl\";");
            sb.AppendLine("    #include \"MathGradient.glsl\";");
            sb.AppendLine("    #include \"Color.glsl\";");
            sb.AppendLine("    #include \"Scene.glsl\";");
            sb.AppendLine("    #include \"SceneFogInput.glsl\";");
        }
        else
        {
            // 标准shader的includes
            sb.AppendLine("    #include \"Math.glsl\";");
            sb.AppendLine("    #include \"Scene.glsl\";");
            sb.AppendLine("    #include \"SceneFogInput.glsl\";");
            sb.AppendLine("    #include \"Camera.glsl\";");
            sb.AppendLine("    #include \"Sprite3DVertex.glsl\";");
            sb.AppendLine("    #include \"VertexCommon.glsl\";");
        }
        sb.AppendLine();
        
        // 生成varying声明并保存，供FS直接复用
        StringBuilder varyingSb = new StringBuilder();
        GenerateVaryingDeclarationsFromDict(varyingSb, allVaryings);
        parseResult.varyingDeclarations = varyingSb.ToString();

        // 粒子shader：顶点颜色varying统一为v_Color（参考i.vcolor->v_Color）
        if (parseResult.isParticleBillboard)
        {
            string decl = parseResult.varyingDeclarations;
            decl = Regex.Replace(decl, @"varying\s+vec4\s+v_VertexColor\s*;", "varying vec4 v_Color;");
            decl = Regex.Replace(decl, @"varying\s+vec4\s+v_Texcoord1\s*;", "varying vec4 v_Color;");
            var lines = decl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>();
            var uniqueLines = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !seen.Contains(trimmed))
                {
                    seen.Add(trimmed);
                    uniqueLines.Add("    " + trimmed);
                }
            }
            parseResult.varyingDeclarations = string.Join("\n", uniqueLines) + "\n";

            // ⭐ 在去重后，将v_MeshColor用条件编译包裹（参考AI版本shader）
            // 格式：紧凑，没有空行，v_MeshColor在所有varying之后
            if (parseResult.varyingDeclarations.Contains("varying vec4 v_MeshColor"))
            {
                // 移除v_MeshColor所在行（包括前后空行）
                string declWithoutMeshColor = Regex.Replace(parseResult.varyingDeclarations,
                    @"\n?\s*varying\s+vec4\s+v_MeshColor\s*;\n?", "");

                // 在末尾添加条件编译版本（紧凑格式）
                parseResult.varyingDeclarations = declWithoutMeshColor +
                    "\n#ifdef RENDERMODE_MESH\n    varying vec4 v_MeshColor;\n#endif\n";

                ExportLogger.Log("LayaAir3D: Wrapped v_MeshColor with conditional compilation (moved to end)");
            }
        }
        
        sb.Append(parseResult.varyingDeclarations);
        sb.AppendLine();

        // ⭐ 关键修复：粒子函数库必须在main函数之前添加（全局作用域）
        // 在GLSL中，不能在main()函数内部定义其他函数
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader：添加粒子函数库（在main之前）
            try
            {
                ExportLogger.Log("LayaAir3D: Adding particle function library (before main function)");
                sb.Append(ParticleShaderTemplate.GetParticleVertexFunctions());
                sb.AppendLine();
            }
            catch (Exception e)
            {
                Debug.LogError($"LayaAir3D: Failed to add particle functions: {e.Message}");
            }
        }
        else
        {
            // Mesh shader：添加必要的辅助函数（TransformUV等）
            GenerateHelperFunctions(sb, parseResult);
        }

        // 添加转换后的自定义函数
        foreach (var func in convertedFunctions)
        {
            sb.AppendLine(IndentCode(func, "    "));
            sb.AppendLine();
        }

        // 生成main函数
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        
        // 粒子shader不使用Vertex结构体，直接使用粒子attribute
        if (!parseResult.isParticleBillboard)
        {
            sb.AppendLine("        Vertex vertex;");
            sb.AppendLine("        getVertexParams(vertex);");
            sb.AppendLine();
        }
        
        // ⭐ 关键修复：如果是粒子shader，强制使用ParticleShaderTemplate，不使用转换后的mesh代码
        // 即使Unity源码被成功解析，Unity的Artist_Effect shader使用mesh inputs (POSITION, TEXCOORD0)
        // 但在LayaAir中必须使用粒子Billboard代码（a_DirectionTime, a_ShapePositionStartLifeTime等）
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader：强制使用ParticleShaderTemplate，不使用转换后的Unity代码
            ExportLogger.Log("LayaAir3D: Particle shader detected - using ParticleShaderTemplate (ignoring Unity converted code)");
            GenerateParticleVertexCode(sb, parseResult);
        }
        else if (!string.IsNullOrEmpty(convertedVertCode))
        {
            // Mesh shader：使用转换后的Unity代码
            sb.AppendLine(IndentCode(convertedVertCode, "        "));
        }
        else
        {
            // Mesh shader：没有Unity代码，使用默认mesh代码
            GenerateMeshVertexCode(sb, parseResult);
        }

        sb.AppendLine();

        // ⭐ 优化：粒子shader的ParticleShaderTemplate已经包含remapPositionZ和FogHandle
        // 避免重复添加，只对非粒子shader添加这些调用
        if (!parseResult.isParticleBillboard)
        {
            sb.AppendLine("        gl_Position = remapPositionZ(gl_Position);");
            sb.AppendLine();
            sb.AppendLine("    #ifdef FOG");
            sb.AppendLine("        FogHandle(gl_Position.z);");
            sb.AppendLine("    #endif");
        }

        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成转换后的片元着色器
    /// </summary>
    private static void GenerateConvertedFragmentShader(StringBuilder sb, string shaderName, ShaderParseResult parseResult)
    {
        // 转换代码
        string convertedFragCode = "";
        List<string> convertedFunctions = new List<string>();
        HashSet<string> addedFunctionNames = new HashSet<string>();
        
        if (!string.IsNullOrEmpty(parseResult.fragmentCode))
        {
            convertedFragCode = ConvertFragmentShaderCode(parseResult.fragmentCode, parseResult);
        }
        
        // 转换自定义函数（去重）
        foreach (var func in parseResult.customFunctions)
        {
            string funcName = ExtractFunctionName(func);
            if (!string.IsNullOrEmpty(funcName) && addedFunctionNames.Contains(funcName))
                continue;
            
            string convertedFunc = ConvertHLSLFunction(func);
            convertedFunctions.Add(convertedFunc);
            
            if (!string.IsNullOrEmpty(funcName))
                addedFunctionNames.Add(funcName);
        }
        
        // 开始生成shader
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        
        // 根据是否是粒子shader选择不同的includes（参考手动可行的Particle模板）
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader的FS includes
            sb.AppendLine("    #include \"Scene.glsl\";");
            sb.AppendLine("    #include \"SceneFog.glsl\";");
            sb.AppendLine("    #include \"Color.glsl\";");
            sb.AppendLine("    #include \"Camera.glsl\";");
        }
        else
        {
            // 标准shader的FS includes
            sb.AppendLine("    #include \"Color.glsl\";");
            sb.AppendLine("    #include \"Scene.glsl\";");
            sb.AppendLine("    #include \"SceneFog.glsl\";");
            sb.AppendLine("    #include \"Camera.glsl\";");
            sb.AppendLine("    #include \"Sprite3DFrag.glsl\";");
        }
        sb.AppendLine();
        
        // 直接使用VS中保存的varying声明字符串，确保VS和FS完全一致
        if (!string.IsNullOrEmpty(parseResult.varyingDeclarations))
        {
            sb.Append(parseResult.varyingDeclarations);
        }
        else
        {
            // 如果VS没有保存varying声明，重新生成（兜底逻辑）
            Dictionary<string, string> allVaryings = parseResult.collectedVaryings;
            if (allVaryings == null || allVaryings.Count == 0)
            {
                allVaryings = CollectAllVaryings(parseResult);
                ExtractVaryingsFromCode(convertedFragCode, allVaryings);
                foreach (var func in convertedFunctions)
                {
                    ExtractVaryingsFromCode(func, allVaryings);
                }
            }
            GenerateVaryingDeclarationsFromDict(sb, allVaryings);
        }
        sb.AppendLine();
        
        // 添加转换后的自定义函数
        foreach (var func in convertedFunctions)
        {
            sb.AppendLine(IndentCode(func, "    "));
            sb.AppendLine();
        }
        
        // 生成main函数
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        
        // 添加转换后的片元着色器代码
        if (!string.IsNullOrEmpty(convertedFragCode))
        {
            sb.AppendLine(IndentCode(convertedFragCode, "        "));
        }
        else
        {
            // 默认片元处理
            GenerateDefaultFragmentCode(sb, parseResult);
        }

        // ⭐ 粒子系统mesh模式：在最终输出前乘以mesh顶点颜色（参考AI版本shader）
        if (parseResult.isParticleBillboard)
        {
            sb.AppendLine();
            sb.AppendLine("    #ifdef RENDERMODE_MESH");
            sb.AppendLine("        // Multiply by mesh vertex color in mesh mode");
            sb.AppendLine("        gl_FragColor *= v_MeshColor;");
            sb.AppendLine("    #endif");
        }

        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        gl_FragColor.rgb = scenUnlitFog(gl_FragColor.rgb);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 收集所有需要的varying（用于确保VS和FS声明一致）
    /// </summary>
    private static Dictionary<string, string> CollectAllVaryings(ShaderParseResult parseResult)
    {
        Dictionary<string, string> varyings = new Dictionary<string, string>();
        
        // 1. 从解析结果的varyings映射中收集
        foreach (var kvp in parseResult.varyings)
        {
            var info = kvp.Value;
            if (!varyings.ContainsKey(info.glslName))
            {
                varyings[info.glslName] = info.glslType;
            }
        }
        
        // 2. 从顶点着色器代码中提取（o.xxx = ...）
        if (!string.IsNullOrEmpty(parseResult.vertexCode))
        {
            var outputMatches = Regex.Matches(parseResult.vertexCode, @"\bo\.(\w+)\s*=");
            foreach (Match m in outputMatches)
            {
                string varName = m.Groups[1].Value;
                
                // 跳过已知的位置输出
                if (varName == "vertex" || varName == "pos" || varName == "position")
                    continue;
                
                // 检查是否已经在varyings映射中
                string glslName = "v_" + varName;
                foreach (var kvp in parseResult.varyings)
                {
                    if (kvp.Key == varName)
                    {
                        glslName = kvp.Value.glslName;
                        break;
                    }
                }
                
                if (!varyings.ContainsKey(glslName))
                {
                    varyings[glslName] = InferVaryingType(varName, parseResult);
                }
            }
        }
        
        // 3. 从片元着色器代码中提取（i.xxx）
        if (!string.IsNullOrEmpty(parseResult.fragmentCode))
        {
            var inputMatches = Regex.Matches(parseResult.fragmentCode, @"\bi\.(\w+)\b");
            foreach (Match m in inputMatches)
            {
                string varName = m.Groups[1].Value;
                string glslName = "v_" + varName;
                
                foreach (var kvp in parseResult.varyings)
                {
                    if (kvp.Key == varName)
                    {
                        glslName = kvp.Value.glslName;
                        break;
                    }
                }
                
                if (!varyings.ContainsKey(glslName))
                {
                    varyings[glslName] = InferVaryingType(varName, parseResult);
                }
            }
        }
        
        // 4. 从自定义函数中提取
        foreach (var func in parseResult.customFunctions)
        {
            // 检查 i.xxx 模式
            var funcInputMatches = Regex.Matches(func, @"\bi\.(\w+)\b");
            foreach (Match m in funcInputMatches)
            {
                string varName = m.Groups[1].Value;
                string glslName = "v_" + varName;
                
                foreach (var kvp in parseResult.varyings)
                {
                    if (kvp.Key == varName)
                    {
                        glslName = kvp.Value.glslName;
                        break;
                    }
                }
                
                if (!varyings.ContainsKey(glslName))
                {
                    varyings[glslName] = InferVaryingType(varName, parseResult);
                }
            }
            
            // 检查 o.xxx 模式
            var funcOutputMatches = Regex.Matches(func, @"\bo\.(\w+)\s*=");
            foreach (Match m in funcOutputMatches)
            {
                string varName = m.Groups[1].Value;
                if (varName == "vertex" || varName == "pos" || varName == "position")
                    continue;
                    
                string glslName = "v_" + varName;
                foreach (var kvp in parseResult.varyings)
                {
                    if (kvp.Key == varName)
                    {
                        glslName = kvp.Value.glslName;
                        break;
                    }
                }
                
                if (!varyings.ContainsKey(glslName))
                {
                    varyings[glslName] = InferVaryingType(varName, parseResult);
                }
            }
        }
        
        // 5. 添加基本varying（确保始终存在）
        // ⭐ 修复：粒子shader需要v_Texcoord0为vec4（用于.zw存储滚动UV）
        if (!varyings.ContainsKey("v_Texcoord0"))
        {
            // 粒子shader或者使用了Scroll相关的shader都需要vec4（.zw存储滚动UV）
            // Vector4型UVScroll和float分量型均需要vec4
            bool needsVec4 = parseResult.isParticleBillboard ||
                            HasAnyPropertyByLayaName(parseResult, UVScrollVectorLayaNames) ||
                            UVScrollFloatNamePatterns.Any(p => HasPropertyByName(parseResult, p)) ||
                            parseResult.vertexCode.Contains("u_Scroll0X") ||
                            parseResult.vertexCode.Contains("u_Scroll0Y");
            varyings["v_Texcoord0"] = needsVec4 ? "vec4" : "vec2";
        }
        if (!varyings.ContainsKey("v_VertexColor"))
            varyings["v_VertexColor"] = "vec4";
        if (!varyings.ContainsKey("v_PositionWS"))
            varyings["v_PositionWS"] = "vec3";
        if (!varyings.ContainsKey("v_PositionCS"))
            varyings["v_PositionCS"] = "vec4";
        if (!varyings.ContainsKey("v_ScreenPos"))
            varyings["v_ScreenPos"] = "vec4";

        return varyings;
    }

    /// <summary>
    /// 生成varying声明
    /// </summary>
    private static void GenerateVaryingDeclarations(StringBuilder sb, ShaderParseResult parseResult)
    {
        // 收集所有需要的varying
        Dictionary<string, string> allVaryings = CollectAllVaryings(parseResult);
        
        // 按名称排序以确保VS和FS输出顺序一致
        var sortedVaryings = allVaryings.OrderBy(kvp => kvp.Key).ToList();
        
        foreach (var kvp in sortedVaryings)
        {
            sb.AppendLine($"    varying {kvp.Value} {kvp.Key};");
        }
    }

    /// <summary>
    /// 从字典生成varying声明
    /// </summary>
    private static void GenerateVaryingDeclarationsFromDict(StringBuilder sb, Dictionary<string, string> varyings)
    {
        if (varyings == null || varyings.Count == 0)
            return;

        // ⭐ 按名称排序，但v_MeshColor要放在最后（参考AI辅助转换的shader）
        var sortedVaryings = varyings.OrderBy(kvp => kvp.Key).ToList();

        // 分离v_MeshColor
        string meshColorType = null;
        var normalVaryings = new List<KeyValuePair<string, string>>();

        foreach (var kvp in sortedVaryings)
        {
            if (kvp.Key == "v_MeshColor")
            {
                meshColorType = kvp.Value;
            }
            else
            {
                normalVaryings.Add(kvp);
            }
        }

        // 先输出所有普通varying
        foreach (var kvp in normalVaryings)
        {
            sb.AppendLine($"    varying {kvp.Value} {kvp.Key};");
        }

        // 最后输出v_MeshColor（不带条件编译，后续处理）
        if (meshColorType != null)
        {
            sb.AppendLine($"    varying {meshColorType} v_MeshColor;");
        }
    }

    /// <summary>
    /// 从转换后的GLSL代码中提取varying引用
    /// </summary>
    private static void ExtractVaryingsFromCode(string code, Dictionary<string, string> varyings)
    {
        if (string.IsNullOrEmpty(code))
            return;
        
        // 匹配 v_xxx 模式（varying引用）
        var matches = Regex.Matches(code, @"\bv_(\w+)\b");
        foreach (Match m in matches)
        {
            string glslName = "v_" + m.Groups[1].Value;
            if (!varyings.ContainsKey(glslName))
            {
                // 推断类型
                string varName = m.Groups[1].Value;
                string glslType = InferVaryingTypeFromName(varName);
                varyings[glslName] = glslType;
            }
        }
    }

    /// <summary>
    /// 根据变量名推断varying类型（不依赖parseResult）
    /// </summary>
    private static string InferVaryingTypeFromName(string varName)
    {
        string lowerName = varName.ToLower();
        
        if (lowerName.Contains("uv") || lowerName.Contains("texcoord"))
            return "vec2";
        if (lowerName.Contains("color") || lowerName.Contains("col") || lowerName == "vertexcolor")
            return "vec4";
        if (lowerName.Contains("normal") || lowerName.Contains("tangent") || 
            lowerName.Contains("dir") || lowerName.Contains("view"))
            return "vec3";
        if (lowerName == "positionws" || lowerName == "worldpos")
            return "vec3";
        if (lowerName.Contains("screen") || lowerName.Contains("grab") || 
            lowerName == "positioncs" || lowerName == "screenpos")
            return "vec4";
        
        // 默认vec4
        return "vec4";
    }

    /// <summary>
    /// 推断varying类型
    /// </summary>
    private static string InferVaryingType(string varName, ShaderParseResult parseResult)
    {
        string lowerName = varName.ToLower();
        
        // 根据名称推断类型
        if (lowerName.Contains("uv") || lowerName.Contains("texcoord"))
            return "vec2";
        if (lowerName.Contains("color") || lowerName.Contains("col"))
            return "vec4";
        if (lowerName.Contains("normal") || lowerName.Contains("tangent") || 
            lowerName.Contains("pos") || lowerName.Contains("dir") || lowerName.Contains("view"))
            return "vec3";
        if (lowerName.Contains("screen") || lowerName.Contains("grab"))
            return "vec4";
        
        // 默认vec4
        return "vec4";
    }

    /// <summary>
    /// 生成默认顶点代码
    /// </summary>
    private static void GenerateDefaultVertexCode(StringBuilder sb, ShaderParseResult parseResult)
    {
        if (parseResult.isParticleBillboard)
        {
            // 粒子系统：使用Particle.shader模板的完整代码
            GenerateParticleVertexCode(sb, parseResult);
        }
        else
        {
            // 标准Mesh：生成mesh shader的顶点代码
            GenerateMeshVertexCode(sb, parseResult);
        }
    }

    /// <summary>
    /// 生成辅助函数（TransformUV等）
    /// </summary>
    private static void GenerateHelperFunctions(StringBuilder sb, ShaderParseResult parseResult)
    {
        // 检查是否需要TransformUV函数（在顶点偏移、UV变换、UV滚动等场景中使用）
        bool needsTransformUV = HasPropertyByName(parseResult, "VertexAmplitude") ||
                                HasPropertyByName(parseResult, "VertexOffset") ||
                                HasPropertyByName(parseResult, "_ST") ||
                                parseResult.isParticleBillboard ||
                                HasAnyPropertyByLayaName(parseResult, UVScrollVectorLayaNames) ||
                                UVScrollFloatNamePatterns.Any(p => HasPropertyByName(parseResult, p));

        if (needsTransformUV)
        {
            sb.AppendLine("    // UV变换辅助函数");
            sb.AppendLine("    vec2 TransformUV(vec2 uv, vec4 tilingOffset) {");
            sb.AppendLine("        return uv * tilingOffset.xy + tilingOffset.zw;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // 检查是否需要UV旋转函数
        bool needsRotateUV = HasPropertyByName(parseResult, "Rotate") ||
                            HasPropertyByName(parseResult, "RotateAngle");

        if (needsRotateUV)
        {
            sb.AppendLine("    // UV旋转辅助函数");
            sb.AppendLine("    vec2 rotateUV(vec2 uv, float centerX, float centerY, float angle) {");
            sb.AppendLine("        float rad = angle * 0.01745329; // degrees to radians");
            sb.AppendLine("        float cosAngle = cos(rad);");
            sb.AppendLine("        float sinAngle = sin(rad);");
            sb.AppendLine("        vec2 center = vec2(centerX, centerY);");
            sb.AppendLine("        vec2 delta = uv - center;");
            sb.AppendLine("        mat2 rotMat = mat2(cosAngle, -sinAngle, sinAngle, cosAngle);");
            sb.AppendLine("        return (rotMat * delta) + center;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // 检查是否需要Polar坐标转换
        bool needsPolar = HasPropertyByName(parseResult, "Polar") ||
                         HasPropertyByName(parseResult, "UsePolar");

        if (needsPolar)
        {
            sb.AppendLine("    // Polar坐标转换函数");
            sb.AppendLine("    vec2 polarCoordinates(vec2 uv, vec2 center, float radialScale, float lengthScale) {");
            sb.AppendLine("        vec2 delta = uv - center;");
            sb.AppendLine("        float radius = length(delta) * 2.0 * radialScale;");
            sb.AppendLine("        float angle = atan(delta.x, delta.y) * (1.0 / 6.28318530718) * lengthScale;");
            sb.AppendLine("        return vec2(radius, angle);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// 生成Mesh shader的顶点代码
    /// </summary>
    private static void GenerateMeshVertexCode(StringBuilder sb, ShaderParseResult parseResult)
    {
        // ⭐ 简化版：只生成基本的mesh变换代码
        // 根据实际的varying来决定需要生成哪些代码

        sb.AppendLine("        vec3 positionOS = vertex.positionOS;");
        sb.AppendLine();

        // 顶点偏移处理（如果有）
        if (HasPropertyByName(parseResult, "VertexOffset") || HasPropertyByName(parseResult, "VertexAmplitude"))
        {
            sb.AppendLine("    #ifdef USEVERTEXOFFSET");
            sb.AppendLine("        // 顶点偏移效果");
            sb.AppendLine("        vec4 vertexAmplitudeTex = texture2D(u_VertexAmplitudeTex, ");
            sb.AppendLine("            TransformUV(vertex.texCoord0, u_VertexAmplitudeTex_ST) + ");
            sb.AppendLine("            fract(vec2(u_VertexAmplitudeTexScroll0X, u_VertexAmplitudeTexScroll0Y) * u_Time));");
            sb.AppendLine("        vec4 vertexAmplitudeMaskTex = texture2D(u_VertexAmplitudeMaskTex, ");
            sb.AppendLine("            TransformUV(vertex.texCoord0, u_VertexAmplitudeMaskTex_ST));");
            sb.AppendLine();
            sb.AppendLine("        if (u_VertexOffsetMode == 1.0) {");
            sb.AppendLine("            // axis mode - 沿任意方向偏移");
            sb.AppendLine("            positionOS += u_VertexAmplitude * (2.0 * vertexAmplitudeTex.rgb - 1.0) * vertexAmplitudeMaskTex.r;");
            sb.AppendLine("        } else {");
            sb.AppendLine("            // normal mode - 沿法线方向偏移");
            sb.AppendLine("            positionOS += vertex.normalOS * u_VertexAmplitude * vertexAmplitudeTex.r * vertexAmplitudeMaskTex.r;");
            sb.AppendLine("        }");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }

        // 标准mesh变换
        sb.AppendLine("        mat4 worldMat = getWorldMatrix();");
        sb.AppendLine("        vec4 positionWS = worldMat * vec4(positionOS, 1.0);");

        // 只在有v_PositionWS varying时才生成赋值
        if (parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_PositionWS"))
        {
            sb.AppendLine("        v_PositionWS = positionWS.xyz / positionWS.w;");
            sb.AppendLine();
            sb.AppendLine("        gl_Position = getPositionCS(v_PositionWS);");
        }
        else
        {
            sb.AppendLine("        gl_Position = getPositionCS(positionWS.xyz / positionWS.w);");
        }

        // 只在有v_PositionCS varying时才生成赋值
        if (parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_PositionCS"))
        {
            sb.AppendLine("        v_PositionCS = gl_Position;");
        }

        // 只在有v_ScreenPos varying时才生成赋值
        if (parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_ScreenPos"))
        {
            sb.AppendLine("        v_ScreenPos = gl_Position * 0.5 + vec4(0.5 * gl_Position.w);");
        }
        sb.AppendLine();

        // UV坐标 - 根据v_Texcoord0的类型决定如何赋值
        if (parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord0"))
        {
            string texcoordType = parseResult.collectedVaryings["v_Texcoord0"];
            if (texcoordType == "vec4")
            {
                // vec4类型，可以同时存储两套UV
                sb.AppendLine("        // UV坐标");
                sb.AppendLine("        v_Texcoord0.xy = vertex.texCoord0;");
                sb.AppendLine("        v_Texcoord0.zw = vec2(0.0); // 预留");
            }
            else if (texcoordType == "vec2")
            {
                // vec2类型，只存储一套UV
                sb.AppendLine("        // UV坐标");
                sb.AppendLine("        v_Texcoord0 = vertex.texCoord0;");
            }
            sb.AppendLine();
        }

        // 顶点颜色 - 使用正确的varying名称
        if (parseResult.collectedVaryings != null)
        {
            if (parseResult.collectedVaryings.ContainsKey("v_Color"))
            {
                sb.AppendLine("        // 顶点颜色");
                sb.AppendLine("        v_Color = vertex.vertexColor;");
                sb.AppendLine();
            }
            else if (parseResult.collectedVaryings.ContainsKey("v_VertexColor"))
            {
                sb.AppendLine("        // 顶点颜色");
                sb.AppendLine("        v_VertexColor = vertex.vertexColor;");
                sb.AppendLine();
            }
        }

        // 法线和切线（用于Rim和光照）- 只在需要时生成
        if (HasPropertyByName(parseResult, "Rim") || HasPropertyByName(parseResult, "Lighting") ||
            HasPropertyByName(parseResult, "NormalMap"))
        {
            sb.AppendLine("        // 法线变换");
            sb.AppendLine("        mat3 normalMat = transpose(inverse(mat3(worldMat)));");
            sb.AppendLine();

            // 只在对应的varying存在时才生成代码
            bool hasTexcoord3 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord3");
            bool hasTexcoord2 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord2");
            bool hasTexcoord8 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord8");

            if (hasTexcoord3 || hasTexcoord2 || hasTexcoord8)
            {
                sb.AppendLine("    #ifdef USENORMALMAPFORRIM");
                sb.AppendLine("        vec3 normalWorld = normalize(normalMat * vertex.normalOS);");
                sb.AppendLine("        vec3 tangentWorld = normalize((worldMat * vec4(vertex.tangentOS.xyz, 0.0)).xyz);");
                sb.AppendLine("        float tangentSign = vertex.tangentOS.w;");
                sb.AppendLine("        vec3 binormalWorld = cross(normalWorld, tangentWorld) * tangentSign;");
                if (hasTexcoord3) sb.AppendLine("        v_Texcoord3 = normalWorld;");
                if (hasTexcoord8) sb.AppendLine("        v_Texcoord8 = tangentWorld;");
                sb.AppendLine("    #endif");
                sb.AppendLine();

                if (hasTexcoord2)
                {
                    sb.AppendLine("    #ifdef USERIM");
                    sb.AppendLine("        #ifdef USENORMALMAPFORRIM");
                    sb.AppendLine("            v_Texcoord2 = binormalWorld;");
                    sb.AppendLine("        #else");
                    sb.AppendLine("            // 视空间法线（用于Rim效果）");
                    sb.AppendLine("            mat3 viewIT = transpose(inverse(mat3(u_View * worldMat)));");
                    sb.AppendLine("            v_Texcoord2 = normalize(viewIT * vertex.normalOS);");
                    sb.AppendLine("        #endif");
                    sb.AppendLine("    #endif");
                    sb.AppendLine();
                }

                if (hasTexcoord3)
                {
                    sb.AppendLine("    #ifdef USELIGHTING");
                    sb.AppendLine("        v_Texcoord3 = normalize(normalMat * vertex.normalOS);");
                    sb.AppendLine("    #endif");
                    sb.AppendLine();
                }
            }
        }

        // UV滚动（多层纹理）- 只在对应varying存在时生成
        bool hasTexcoord4 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord4");
        if (hasTexcoord4)
        {
            sb.AppendLine("    #if defined(LAYERTYPE_TWO) || defined(LAYERTYPE_THREE)");
            sb.AppendLine("        v_Texcoord4.xy = fract(vec2(u_Scroll1X, u_Scroll1Y) * u_Time);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
            sb.AppendLine("    #ifdef LAYERTYPE_THREE");
            sb.AppendLine("        v_Texcoord4.zw = fract(vec2(u_Scroll2X, u_Scroll2Y) * u_Time);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }

        // Distort UV滚动 - 只在对应varying存在时生成
        bool hasTexcoord6 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord6");
        if (hasTexcoord6)
        {
            sb.AppendLine("    #ifdef USEDISTORT0");
            sb.AppendLine("        v_Texcoord6.xy = fract(vec2(u_Distort0X, u_Distort0Y) * u_Time);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
            sb.AppendLine("    #ifdef USEDISSOLVEDISTORT");
            sb.AppendLine("        v_Texcoord6.zw = fract(vec2(u_DissolveDistortX, u_DissolveDistortY) * u_Time);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }

        // UV旋转预计算 - 只在对应varying存在时生成
        bool hasTexcoord9 = parseResult.collectedVaryings != null && parseResult.collectedVaryings.ContainsKey("v_Texcoord9");
        if (hasTexcoord9 && (HasPropertyByName(parseResult, "Rotation") || HasPropertyByName(parseResult, "RotateAngle")))
        {
            sb.AppendLine("    #ifdef ROTATIONTEX");
            sb.AppendLine("        float rad1 = u_RotateAngle * 0.01745329;");
            sb.AppendLine("        v_Texcoord9.x = cos(rad1);");
            sb.AppendLine("        v_Texcoord9.y = sin(rad1);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
            sb.AppendLine("    #ifdef ROTATIONTEXTWO");
            sb.AppendLine("        float rad2 = u_RotateAngle02 * 0.01745329;");
            sb.AppendLine("        v_Texcoord9.z = cos(rad2);");
            sb.AppendLine("        v_Texcoord9.w = sin(rad2);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }

        // ⭐ 移除CustomData处理 - 这需要a_Texcoord1属性，但简单shader没有
        // 只有明确需要CustomData且attributeMap中有a_Texcoord1时才处理
        // if (HasPropertyByName(parseResult, "CustomData"))
        // {
        //     sb.AppendLine("    #ifdef USECUSTOMDATA");
        //     sb.AppendLine("        v_Texcoord7.xy = vertex.texCoord0.zw;  // CustomData from UV0.zw");
        //     sb.AppendLine("        v_Texcoord7.zw = vertex.texCoord1;     // CustomData from UV1.xy");
        //     sb.AppendLine("    #endif");
        //     sb.AppendLine();
        // }
    }

    /// <summary>
    /// 生成粒子系统顶点代码（使用Particle.shader模板）
    /// 注意：只生成main函数的body，不生成函数定义（函数定义已在main之前添加）
    /// </summary>
    private static void GenerateParticleVertexCode(StringBuilder sb, ShaderParseResult parseResult)
    {
        // ⭐ 使用ParticleShaderTemplate（最可靠的方法）
        // 注意：函数库已经在main函数之前添加过了，这里只添加main函数body
        try
        {
            ExportLogger.Log("LayaAir3D: Generating particle main function body from ParticleShaderTemplate");

            // ⭐ 关键：不再添加函数库（已在main之前添加）
            // sb.Append(ParticleShaderTemplate.GetParticleVertexFunctions());  // ← 注释掉

            // 获取完整的main函数
            string mainFunc = ParticleShaderTemplate.GetParticleVertexMainFunction();

            // 移除main函数的开头声明（因为外层已经有了void main() {）
            if (mainFunc.Contains("void main()"))
            {
                int bodyStart = mainFunc.IndexOf("{");
                if (bodyStart != -1)
                {
                    // 提取main函数体内容（不包括void main()和最外层的{}）
                    int bodyEnd = mainFunc.LastIndexOf("}");
                    if (bodyEnd > bodyStart)
                    {
                        mainFunc = mainFunc.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
                    }
                }
            }

            // 添加main函数body（已经正确缩进）
            sb.AppendLine(mainFunc);

            // 添加特效相关的varying赋值
            AddEffectVaryingAssignments(sb, parseResult);

            ExportLogger.Log("LayaAir3D: Successfully generated particle main body using ParticleShaderTemplate");
            return;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"LayaAir3D: Failed to use ParticleShaderTemplate: {e.Message}");
            Debug.LogWarning($"LayaAir3D: Stack trace: {e.StackTrace}");
            Debug.LogWarning("LayaAir3D: Falling back to template file extraction");
        }

        // Fallback 1: 尝试从Particle.shader模板文件读取
        string[] possiblePaths = new string[]
        {
            Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/template/Particle.shader"),
            Path.Combine(Application.dataPath.Replace("/Assets", ""), "Assets/LayaAir3.0UnityPlugin/template/Particle.shader"),
            Path.Combine(Directory.GetCurrentDirectory(), "template/Particle.shader"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets/LayaAir3.0UnityPlugin/template/Particle.shader")
        };

        string particleTemplatePath = null;
        string particleVSCode = null;

        // 尝试所有可能的路径
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                particleTemplatePath = path;
                ExportLogger.Log($"LayaAir3D: Found Particle.shader template file at: {path}");
                break;
            }
        }

        if (particleTemplatePath != null)
        {
            try
            {
                string particleTemplate = File.ReadAllText(particleTemplatePath);
                // 提取main函数的内容（粒子系统的核心计算代码）
                particleVSCode = ExtractParticleMainFunction(particleTemplate);

                if (!string.IsNullOrEmpty(particleVSCode))
                {
                    ExportLogger.Log("LayaAir3D: Successfully extracted particle VS code from template file");
                    sb.AppendLine(particleVSCode);

                    // 添加特效相关的varying赋值
                    AddEffectVaryingAssignments(sb, parseResult);
                    return;
                }
                else
                {
                    Debug.LogWarning("LayaAir3D: Failed to extract particle main function from template file");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"LayaAir3D: Failed to read Particle.shader template: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("LayaAir3D: Particle.shader template file not found in any expected location");
            Debug.LogWarning("LayaAir3D: Searched paths:");
            foreach (var path in possiblePaths)
            {
                Debug.LogWarning($"  - {path}");
            }
        }

        // Fallback 2: 使用内置的粒子系统代码（简化版但已验证正确）
        Debug.LogWarning("LayaAir3D: Using built-in particle code as final fallback");
        GenerateBuiltInParticleVertexCode(sb, parseResult);
    }

    /// <summary>
    /// 从Particle.shader提取main函数内容（增强版）
    /// </summary>
    private static string ExtractParticleMainFunction(string particleTemplate)
    {
        try
        {
            // 查找"void main()"到"}"的内容（在ParticleVS中）
            int vsStart = particleTemplate.IndexOf("#defineGLSL ParticleVS");
            if (vsStart == -1)
            {
                // 尝试其他可能的标记
                vsStart = particleTemplate.IndexOf("#defineGLSL");
                if (vsStart == -1)
                {
                    Debug.LogWarning("LayaAir3D: Cannot find #defineGLSL in Particle.shader template");
                    return null;
                }
            }

            // 查找void main()
            int mainStart = particleTemplate.IndexOf("void main()", vsStart);
            if (mainStart == -1)
            {
                Debug.LogWarning("LayaAir3D: Cannot find void main() in Particle.shader template");
                return null;
            }

            // 查找main函数的开始花括号
            int mainBodyStart = particleTemplate.IndexOf("{", mainStart);
            if (mainBodyStart == -1)
            {
                Debug.LogWarning("LayaAir3D: Cannot find opening brace for main() function");
                return null;
            }

            // 计数花括号来找到匹配的结束花括号
            int braceCount = 0;
            int pos = mainBodyStart;
            bool inString = false;
            bool inComment = false;
            bool inLineComment = false;
            char prevChar = '\0';

            while (pos < particleTemplate.Length)
            {
                char c = particleTemplate[pos];

                // 处理字符串内的字符（忽略引号内的花括号）
                if (c == '"' && prevChar != '\\')
                {
                    inString = !inString;
                }
                // 处理注释
                else if (!inString && c == '/' && pos + 1 < particleTemplate.Length)
                {
                    if (particleTemplate[pos + 1] == '/')
                    {
                        inLineComment = true;
                        pos++;
                    }
                    else if (particleTemplate[pos + 1] == '*')
                    {
                        inComment = true;
                        pos++;
                    }
                }
                else if (inLineComment && c == '\n')
                {
                    inLineComment = false;
                }
                else if (inComment && c == '*' && pos + 1 < particleTemplate.Length && particleTemplate[pos + 1] == '/')
                {
                    inComment = false;
                    pos++;
                }
                // 只在非字符串、非注释中计数花括号
                else if (!inString && !inComment && !inLineComment)
                {
                    if (c == '{') braceCount++;
                    if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            // 提取main函数体（不包括最外层的{}）
                            string mainBody = particleTemplate.Substring(mainBodyStart + 1, pos - mainBodyStart - 1);

                            // 验证提取的代码
                            if (mainBody.Trim().Length < 10)
                            {
                                Debug.LogWarning("LayaAir3D: Extracted main function body is too short, might be invalid");
                                return null;
                            }

                            ExportLogger.Log($"LayaAir3D: Successfully extracted {mainBody.Length} characters from particle main function");
                            return mainBody;
                        }
                    }
                }

                prevChar = c;
                pos++;
            }

            Debug.LogWarning("LayaAir3D: Cannot find closing brace for main() function (unbalanced braces)");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"LayaAir3D: Exception while extracting particle main function: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 添加特效相关的varying赋值（在粒子计算之后）
    /// </summary>
    private static void AddEffectVaryingAssignments(StringBuilder sb, ShaderParseResult parseResult)
    {
        // 这些赋值应该在粒子计算完成后、gl_Position=remapPositionZ之前执行

        // 预计算UV滚动标志（用于决定是否需要默认UV赋值）
        // 使用精确Laya名匹配（数据驱动），配置见 UVScrollVectorLayaNames / UVScrollFloatNamePatterns
        bool hasVectorUVScrollVarying = HasAnyPropertyByLayaName(parseResult, UVScrollVectorLayaNames);
        bool hasFloatScroll = UVScrollFloatNamePatterns.Any(p => HasPropertyByName(parseResult, p));

        sb.AppendLine();
        sb.AppendLine("        // 特效相关的varying赋值");

        // 只有当没有UV滚动覆盖时才需要默认的v_Texcoord0赋值
        // 自定义粒子shader的FS代码由Unity HLSL转换而来，自带TRANSFORM_TEX（采样时应用TilingOffset），
        // 因此VS传递原始UV即可，不做TransformUV，避免TilingOffset被双重应用
        if (!hasVectorUVScrollVarying && !hasFloatScroll &&
            parseResult.collectedVaryings != null &&
            parseResult.collectedVaryings.ContainsKey("v_Texcoord0"))
        {
            string texcoordType = parseResult.collectedVaryings["v_Texcoord0"];
            if (texcoordType == "vec4")
            {
                sb.AppendLine("        v_Texcoord0.xy = v_TextureCoordinate;");
                sb.AppendLine("        v_Texcoord0.zw = v_TextureCoordinate;");
            }
            else if (texcoordType == "vec2")
            {
                sb.AppendLine("        v_Texcoord0 = v_TextureCoordinate;");
            }
        }

        // 屏幕坐标（如果需要）
        if (HasPropertyByName(parseResult, "Distort") || HasPropertyByName(parseResult, "Screen") ||
            parseResult.collectedVaryings.ContainsKey("v_ScreenPos"))
        {
            sb.AppendLine("        v_ScreenPos.xy = (gl_Position.xy + gl_Position.w) * 0.5;");
            sb.AppendLine("        v_ScreenPos.zw = gl_Position.zw;");
        }

        // ⭐ 修复：移除v_Texcoord5赋值
        // 原代码：sb.AppendLine("        v_Texcoord5 = gl_Position;");
        // 问题：v_Texcoord5没有在varying中声明，且FS中未使用
        // 屏幕坐标已经通过v_ScreenPos传递，不需要额外的v_Texcoord5

        // UV滚动：区分Vector4型（_UVScrollTex/_UVScrollMask）和float分量型（_Scroll0X/_Scroll0Y）
        if (hasVectorUVScrollVarying)
        {
            // ⭐ Vector4 UVScroll模式（如BR/TY系列特效shader）
            // Unity: TRANSFORM_TEX(uv, _MainTex) + _Time.y * _UVScrollTex.xy
            //        TRANSFORM_TEX(uv, _Mask)    + _Time.y * _UVScrollMask.xy
            // Laya：主纹理用u_TilingOffset（Laya粒子/特效约定），Mask用u_[Mask]_ST
            // ⭐ 修复：动态查找实际的UV scroll属性名（而非硬编码u_UVScrollTex）
            string mainScrollUniform = null;
            string maskScrollUniform = null;
            foreach (var prop in parseResult.properties)
            {
                if (UVScrollVectorLayaNames.Contains(prop.layaName))
                {
                    // 区分主纹理scroll和Mask scroll
                    if (prop.unityName.Contains("Mask") || prop.unityName.Contains("mask"))
                        maskScrollUniform = prop.layaName;
                    else
                        mainScrollUniform = prop.layaName;
                }
            }

            // 主纹理UV scroll
            if (mainScrollUniform != null)
            {
                sb.AppendLine($"        v_Texcoord0.xy = TransformUV(v_TextureCoordinate, u_TilingOffset) + u_Time * {mainScrollUniform}.xy;");
            }
            else
            {
                sb.AppendLine("        v_Texcoord0.xy = TransformUV(v_TextureCoordinate, u_TilingOffset);");
            }

            // Mask纹理UV scroll（仅在shader实际有mask scroll属性时生成）
            if (maskScrollUniform != null)
            {
                // 找到Mask纹理对应的ST名称
                string maskSTName = "u_Mask_ST"; // 默认
                foreach (var prop in parseResult.properties)
                {
                    if ((prop.unityName.Contains("Mask") || prop.unityName.Contains("mask")) &&
                        prop.type == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        maskSTName = prop.layaName + "_ST";
                        break;
                    }
                }
                sb.AppendLine($"        v_Texcoord0.zw = TransformUV(v_TextureCoordinate, {maskSTName}) + u_Time * {maskScrollUniform}.xy;");
            }
            else
            {
                sb.AppendLine("        v_Texcoord0.zw = v_TextureCoordinate;");
            }
        }
        else if (hasFloatScroll)
        {
            // float分量型scroll（u_Scroll0X/Y形式）
            sb.AppendLine("        v_Texcoord0.xy = TransformUV(v_TextureCoordinate, u_TilingOffset);");
            sb.AppendLine("        v_Texcoord0.zw = fract(vec2(u_Scroll0X, u_Scroll0Y) * u_Time);");

            if (HasPropertyByName(parseResult, "Scroll1") || HasPropertyByName(parseResult, "DetailTex"))
            {
                sb.AppendLine("    #if defined(LAYERTYPE_TWO) || defined(LAYERTYPE_THREE)");
                sb.AppendLine("        v_Texcoord4.xy = fract(vec2(u_Scroll1X, u_Scroll1Y) * u_Time);");
                sb.AppendLine("    #endif");
            }

            if (HasPropertyByName(parseResult, "Scroll2") || HasPropertyByName(parseResult, "DetailTex2"))
            {
                sb.AppendLine("    #ifdef LAYERTYPE_THREE");
                sb.AppendLine("        v_Texcoord4.zw = fract(vec2(u_Scroll2X, u_Scroll2Y) * u_Time);");
                sb.AppendLine("    #endif");
            }

            if (HasPropertyByName(parseResult, "Distort0"))
            {
                sb.AppendLine("    #ifdef USEDISTORT0");
                sb.AppendLine("        v_Texcoord6.xy = fract(vec2(u_Distort0X, u_Distort0Y) * u_Time);");
                sb.AppendLine("    #endif");
            }

            if (HasPropertyByName(parseResult, "DissolveDistort"))
            {
                sb.AppendLine("    #ifdef USEDISSOLVEDISTORT");
                sb.AppendLine("        v_Texcoord6.zw = fract(vec2(u_DissolveDistortX, u_DissolveDistortY) * u_Time);");
                sb.AppendLine("    #endif");
            }
        }

        // 旋转预计算
        if (HasPropertyByName(parseResult, "RotateAngle"))
        {
            sb.AppendLine("    #ifdef ROTATIONTEX");
            sb.AppendLine("        float rad1 = u_RotateAngle * 0.01745329;");
            sb.AppendLine("        v_Texcoord9.x = cos(rad1);");
            sb.AppendLine("        v_Texcoord9.y = sin(rad1);");
            sb.AppendLine("    #endif");

            if (HasPropertyByName(parseResult, "RotateAngle02"))
            {
                sb.AppendLine("    #ifdef ROTATIONTEXTWO");
                sb.AppendLine("        float rad2 = u_RotateAngle02 * 0.01745329;");
                sb.AppendLine("        v_Texcoord9.z = cos(rad2);");
                sb.AppendLine("        v_Texcoord9.w = sin(rad2);");
                sb.AppendLine("    #endif");
            }
        }
    }

    /// <summary>
    /// 生成内置的粒子系统顶点代码（作为后备方案）
    /// 注意：此方法在GenerateParticleVertexCode内部调用，此时外层已经有 void main() { 包裹
    /// 因此只输出main函数体，不输出函数库（已在main之前添加）也不输出void main()声明
    /// </summary>
    private static void GenerateBuiltInParticleVertexCode(StringBuilder sb, ShaderParseResult parseResult)
    {
        // 获取完整的main函数，然后提取body（和GenerateParticleVertexCode主路径相同的逻辑）
        string mainFunc = ParticleShaderTemplate.GetParticleVertexMainFunction();

        // 移除void main()包裹，只保留函数体
        if (mainFunc.Contains("void main()"))
        {
            int bodyStart = mainFunc.IndexOf("{");
            if (bodyStart != -1)
            {
                int bodyEnd = mainFunc.LastIndexOf("}");
                if (bodyEnd > bodyStart)
                {
                    mainFunc = mainFunc.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
                }
            }
        }

        sb.AppendLine(mainFunc);

        // 添加特效相关的varying赋值
        AddEffectVaryingAssignments(sb, parseResult);
    }

    /// <summary>
    /// 检查是否有指定名称的属性（部分字符串匹配，同时检查Unity名和Laya名）
    /// </summary>
    private static bool HasPropertyByName(ShaderParseResult parseResult, string namePattern)
    {
        foreach (var prop in parseResult.properties)
        {
            if (prop.unityName.Contains(namePattern) || prop.layaName.Contains(namePattern))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否有Laya名称在指定集合中的属性（精确匹配Laya名）
    /// 用于数据驱动的属性检测，避免硬编码部分字符串匹配
    /// </summary>
    private static bool HasAnyPropertyByLayaName(ShaderParseResult parseResult, HashSet<string> layaNames)
    {
        foreach (var prop in parseResult.properties)
        {
            if (layaNames.Contains(prop.layaName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 生成默认片元代码
    /// </summary>
    private static void GenerateDefaultFragmentCode(StringBuilder sb, ShaderParseResult parseResult)
    {
        sb.AppendLine("        vec4 color = vec4(1.0);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("        vec2 uv = v_Texcoord0;");
        sb.AppendLine("    #else");
        sb.AppendLine("        vec2 uv = vec2(0.0);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef MAINTEX");
        sb.AppendLine("        color = texture2D(u_MainTex, uv);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        #ifdef ENABLEVERTEXCOLOR");
        sb.AppendLine("        color *= v_VertexColor;");
        sb.AppendLine("        #endif");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        gl_FragColor = color;");
    }

    /// <summary>
    /// 转换HLSL类型到GLSL类型
    /// </summary>
    private static string ConvertTypeToGLSL(string hlslType)
    {
        switch (hlslType.ToLower())
        {
            case "float": return "float";
            case "float2": case "half2": case "fixed2": return "vec2";
            case "float3": case "half3": case "fixed3": return "vec3";
            case "float4": case "half4": case "fixed4": return "vec4";
            case "half": case "fixed": return "float";
            case "float4x4": case "half4x4": return "mat4";
            case "float3x3": case "half3x3": return "mat3";
            case "float2x2": case "half2x2": return "mat2";
            case "sampler2d": return "sampler2D";
            case "samplercube": return "samplerCube";
            case "int": return "int";
            case "bool": return "bool";
            default: return "vec4";
        }
    }

    /// <summary>
    /// 初始化映射引擎（混合架构）
    /// </summary>
    private static string ProjectAssetPathToAbsolutePath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
        string projectRoot = projectDirectory != null
            ? projectDirectory.FullName
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(
            projectRoot,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindBundledShaderMappingPath()
    {
        try
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                "Packages/com.layaair.export-tool/Editor/Export/CustomShaderExporter.cs");
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                string packagePath = Path.Combine(
                    packageInfo.resolvedPath,
                    "Editor/Mappings/builtin_unity_to_laya.json");
                if (File.Exists(packagePath))
                    return packagePath;
            }
        }
        catch (System.Exception)
        {
            // Continue with legacy Assets installation paths.
        }

        string[] legacyPaths = new[]
        {
            Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/Editor/Mappings/builtin_unity_to_laya.json"),
            Path.Combine(Directory.GetCurrentDirectory(),
                "Assets/LayaAir3.0UnityPlugin/Editor/Mappings/builtin_unity_to_laya.json")
        };
        foreach (string path in legacyPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void InitializeMappingEngine()
    {
        if (mappingEngineInitialized)
            return;

        mappingEngineInitialized = true;

        // 项目级转换规则与材质映射采用相同的 Assets/Editor/LayaAir 发现机制。
        string projectMappingAssetPath = FindUniqueProjectAssetPath(
            "ShaderMappings",
            ProjectShaderMappingsAssetSuffix,
            "shader mapping");
        string projectMappingPath = ProjectAssetPathToAbsolutePath(projectMappingAssetPath);
        bool hasUserMappings = !string.IsNullOrEmpty(projectMappingPath) && File.Exists(projectMappingPath);

        if (hasUserMappings)
        {
            ExportLogger.Log("==================== Shader Mapping Engine ====================");
            ExportLogger.Log("LayaAir3D: Custom shader mappings detected");
            ExportLogger.Log("LayaAir3D: Initializing mapping table mode...");

            // 初始化映射引擎
            mappingEngine = new ShaderMappingEngine();

            // 加载内置映射表
            string builtinMappingPath = FindBundledShaderMappingPath();
            if (!string.IsNullOrEmpty(builtinMappingPath))
            {
                if (mappingEngine.LoadMappings(builtinMappingPath))
                {
                    ExportLogger.Log("LayaAir3D: ✓ Loaded builtin mappings");
                }
            }
            else
            {
                Debug.LogWarning($"LayaAir3D: Builtin mapping file not found: {builtinMappingPath}");
            }

            // 加载用户映射表（可覆盖内置规则）
            if (mappingEngine.LoadMappings(projectMappingPath))
            {
                ExportLogger.Log($"LayaAir3D: ✓ Loaded custom mappings from: {projectMappingAssetPath}");
                useMappingTableMode = true;
            }

            if (useMappingTableMode)
            {
                ExportLogger.Log("LayaAir3D: Mapping table mode ENABLED");
                ExportLogger.Log("LayaAir3D: Priority: Custom rules → Builtin rules → Built-in code fallback");
            }
            else
            {
                Debug.LogWarning("LayaAir3D: Failed to load mappings, falling back to built-in mode");
            }

            ExportLogger.Log("===============================================================");
        }
        else
        {
            ExportLogger.Log("LayaAir3D: No custom mappings found, using built-in conversion mode");
            ExportLogger.Log(
                "LayaAir3D: To enable mapping table mode, create " +
                "Assets/**/Editor/LayaAir/ShaderMappings.json");
            useMappingTableMode = false;
        }
    }

    /// <summary>
    /// 检查是否需要回退到内置规则
    /// </summary>
    private static bool CheckNeedsFallback(string code)
    {
        // 检查常见的Unity特有标识符
        string[] unityIdentifiers = new string[]
        {
            "UnityObjectToClipPos",
            "UnityObjectToWorldNormal",
            "UnityWorldToObjectDir",
            "UNITY_MATRIX_MVP",
            "UNITY_MATRIX_MV",
            "UNITY_MATRIX_V",
            "UNITY_MATRIX_P",
            "UNITY_MATRIX_IT_MV",
            "TRANSFER_SHADOW",
            "SHADOW_COORDS",
            "SHADOW_ATTENUATION"
        };

        foreach (var identifier in unityIdentifiers)
        {
            if (code.Contains(identifier))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 应用内置转换规则作为回退
    /// </summary>
    private static string ApplyBuiltInConversionAsFallback(string code)
    {
        // Unity矩阵转换
        code = Regex.Replace(code, @"\bUNITY_MATRIX_MVP\b", "(u_Projection * u_View * u_World)");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_MV\b", "(u_View * u_World)");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_V\b", "u_View");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_P\b", "u_Projection");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_IT_MV\b", "transpose(inverse(u_View * u_World))");

        // UnityObjectToClipPos - 直接展开为矩阵乘法，避免函数调用的类型不匹配问题
        code = Regex.Replace(code, @"UnityObjectToClipPos\s*\(\s*([^)]+)\s*\)",
            m => {
                string arg = m.Groups[1].Value.Trim();
                // 如果参数已经是vec4，直接使用；否则转换为vec4
                if (arg.StartsWith("vec4(") || arg.Contains(".xyzw"))
                    return $"(u_ViewProjection * u_WorldMat * {arg})";
                else
                    return $"(u_ViewProjection * u_WorldMat * vec4({arg}, 1.0))";
            });

        // UnityObjectToWorldNormal
        code = Regex.Replace(code, @"UnityObjectToWorldNormal\s*\(\s*([^)]+)\s*\)",
            m => $"normalize((transpose(inverse(getWorldMatrix())) * vec4({m.Groups[1].Value}, 0.0)).xyz)");

        // Unity阴影相关（简化处理）
        code = Regex.Replace(code, @"TRANSFER_SHADOW\s*\([^)]*\)\s*;?", "");
        code = Regex.Replace(code, @"SHADOW_COORDS\s*\([^)]*\)", "");
        code = Regex.Replace(code, @"SHADOW_ATTENUATION\s*\([^)]*\)", "1.0");

        return code;
    }

    /// <summary>
    /// 转换HLSL函数到GLSL
    /// </summary>
    private static string ConvertHLSLFunction(string hlslFunc)
    {
        string code = ConvertHLSLToGLSL(hlslFunc);

        return code;
    }

    /// <summary>
    /// 转换顶点着色器代码
    /// </summary>
    private static string ConvertVertexShaderCode(string hlslCode, ShaderParseResult parseResult)
    {
        string code = ConvertHLSLToGLSL(hlslCode);
        
        // 替换输出变量 o.xxx
        code = Regex.Replace(code, @"\bo\.vertex\s*=", "gl_Position =");
        code = Regex.Replace(code, @"\bo\.pos\s*=", "gl_Position =");
        
        // 替换varying输出（o.xxx -> v_xxx，使用映射表）
        foreach (var kvp in parseResult.varyings)
        {
            string pattern = $@"\bo\.{Regex.Escape(kvp.Key)}\b";
            code = Regex.Replace(code, pattern, kvp.Value.glslName);
        }
        // 通用替换
        code = Regex.Replace(code, @"\bo\.(\w+)", "v_$1");
        
        // 替换输入变量 v.xxx（根据appdata结构体名称）
        string inputVar = "v";
        // 先处理特殊情况：v.vertex.xyz 直接转换为 vertex.positionOS（避免多余的vec4包装）
        code = Regex.Replace(code, $@"\b{inputVar}\.vertex\.xyz\b", "vertex.positionOS");
        code = Regex.Replace(code, $@"\b{inputVar}\.vertex\.xy\b", "vertex.positionOS.xy");
        code = Regex.Replace(code, $@"\b{inputVar}\.vertex\.xz\b", "vertex.positionOS.xz");
        // 然后处理普通情况：v.vertex 转换为 vec4(vertex.positionOS, 1.0)
        code = Regex.Replace(code, $@"\b{inputVar}\.vertex\b", "vec4(vertex.positionOS, 1.0)");
        code = Regex.Replace(code, $@"\b{inputVar}\.normal\b", "vertex.normalOS");
        code = Regex.Replace(code, $@"\b{inputVar}\.tangent\b", "vertex.tangentOS");
        code = Regex.Replace(code, $@"\b{inputVar}\.uv0\b", "vertex.texCoord0");
        code = Regex.Replace(code, $@"\b{inputVar}\.uv1\b", "vertex.texCoord1");
        code = Regex.Replace(code, $@"\b{inputVar}\.uv\b", "vertex.texCoord0");
        code = Regex.Replace(code, $@"\b{inputVar}\.texcoord0?\b", "vertex.texCoord0");
        code = Regex.Replace(code, $@"\b{inputVar}\.texcoord1\b", "vertex.texCoord1");
        code = Regex.Replace(code, $@"\b{inputVar}\.color\b", "vertex.vertexColor");
        code = Regex.Replace(code, $@"\b{inputVar}\.vcolor\b", "vertex.vertexColor");
        
        // 粒子Billboard模式：特殊变量转换
        if (parseResult.isParticleBillboard)
        {
            // ========== 粒子系统没有Vertex结构体，所有vertex.xxx都需要替换 ==========
            
            // 位置相关
            code = Regex.Replace(code, @"\bvertex\.positionOS\b", "vec3(0.0)"); // 粒子位置由粒子系统计算
            code = Regex.Replace(code, @"\bvertex\.position\b", "vec3(0.0)");
            
            // 法线相关
            code = Regex.Replace(code, @"\bvertex\.normalOS\b", "vec3(0.0, 0.0, 1.0)"); // 粒子默认法线朝向相机
            code = Regex.Replace(code, @"\bvertex\.normal\b", "vec3(0.0, 0.0, 1.0)");
            
            // 切线相关
            code = Regex.Replace(code, @"\bvertex\.tangentOS\b", "vec4(1.0, 0.0, 0.0, 1.0)");
            code = Regex.Replace(code, @"\bvertex\.tangent\b", "vec4(1.0, 0.0, 0.0, 1.0)");
            
            // 顶点颜色 -> 粒子起始颜色
            code = Regex.Replace(code, @"\bvertex\.vertexColor\b", "a_StartColor");
            code = Regex.Replace(code, @"\bvertex\.color\b", "a_StartColor");
            
            // UV坐标 -> a_CornerTextureCoordinate.zw（粒子UV）
            // texCoord0
            code = Regex.Replace(code, @"\bvertex\.texCoord0\.xy\b", "a_CornerTextureCoordinate.zw");
            code = Regex.Replace(code, @"\bvertex\.texCoord0\.x\b", "a_CornerTextureCoordinate.z");
            code = Regex.Replace(code, @"\bvertex\.texCoord0\.y\b", "a_CornerTextureCoordinate.w");
            code = Regex.Replace(code, @"\bvertex\.texCoord0\b(?!\.)", "a_CornerTextureCoordinate.zw");
            code = Regex.Replace(code, @"\bvertex\.texCoord0\.xyzw\b", "vec4(a_CornerTextureCoordinate.zw, 0.0, 0.0)");
            code = Regex.Replace(code, @"\bvertex\.texCoord0\.zw\b", "vec2(0.0, 0.0)");
            
            // texCoord1 -> 粒子CustomData使用a_SimulationUV.zw（参考手动可行版本）
            code = Regex.Replace(code, @"\bvertex\.texCoord1\.xy\b", "a_SimulationUV.zw");
            code = Regex.Replace(code, @"\bvertex\.texCoord1\.zw\b", "a_SimulationUV.zw");
            code = Regex.Replace(code, @"\bvertex\.texCoord1\b(?!\.)", "a_SimulationUV.zw");
            
            // uv, uv0, uv1 等简写形式
            code = Regex.Replace(code, @"\bvertex\.uv0\b", "a_CornerTextureCoordinate.zw");
            code = Regex.Replace(code, @"\bvertex\.uv1\b", "a_CornerTextureCoordinate.zw");
            code = Regex.Replace(code, @"\bvertex\.uv\b", "a_CornerTextureCoordinate.zw");
            
            // 捕获所有剩余的 vertex.xxx 并替换为合理的默认值
            // 这是一个兜底处理，避免遗漏
            code = Regex.Replace(code, @"\bvertex\.(\w+)\b", match =>
            {
                string member = match.Groups[1].Value.ToLower();
                switch (member)
                {
                    case "positionos":
                    case "position":
                        return "vec3(0.0)";
                    case "normalos":
                    case "normal":
                        return "vec3(0.0, 0.0, 1.0)";
                    case "tangentos":
                    case "tangent":
                        return "vec4(1.0, 0.0, 0.0, 1.0)";
                    case "vertexcolor":
                    case "color":
                        return "a_StartColor";
                    case "texcoord0":
                    case "texcoord1":
                    case "texcoord":
                    case "uv":
                    case "uv0":
                    case "uv1":
                        return "a_CornerTextureCoordinate.zw";
                    default:
                        // 未知的vertex成员，返回零向量
                        return "vec4(0.0)";
                }
            });
            
            // ========== 粒子系统Uniform替换 ==========
            
            // u_WorldMat -> 粒子系统没有这个，粒子位置已经是世界空间
            code = Regex.Replace(code, @"\bu_WorldMat\s*\*\s*vec4\s*\(\s*([^,]+)\s*,\s*1\.0\s*\)", "vec4($1, 1.0)");
            code = Regex.Replace(code, @"\bu_WorldMat\b", "mat4(1.0)"); // 单位矩阵作为fallback
            
            // getWorldMatrix() -> 粒子系统没有这个函数
            code = Regex.Replace(code, @"\bgetWorldMatrix\s*\(\s*\)", "mat4(1.0)");
            
            // getPositionCS -> 粒子系统使用 u_Projection * u_View
            code = Regex.Replace(code, @"\bgetPositionCS\s*\(\s*([^)]+)\s*\)", m => {
                string arg = m.Groups[1].Value.Trim();
                // 检查参数是否已经是vec4
                if (arg.StartsWith("vec4(") || arg.Contains(".xyzw") || arg == "v_PositionWS_vec4")
                    return $"(u_Projection * u_View * {arg})";
                else
                    return $"(u_Projection * u_View * vec4({arg}, 1.0))";
            });
            
            // initPixelParams, getVertexParams 等函数在粒子中不存在
            code = Regex.Replace(code, @"\bgetVertexParams\s*\(\s*\w+\s*\)\s*;?", "// getVertexParams not available in particle shader");
            code = Regex.Replace(code, @"\binitPixelParams\s*\(\s*\w+\s*,\s*\w+\s*\)\s*;?", "// initPixelParams not available in particle shader");
            
            // ========== Unity粒子变量到LayaAir粒子变量映射 ==========

            // ⭐ 修复：_Time -> u_Time (LayaAir引擎内置时间变量，粒子系统也使用u_Time)
            // Unity _Time: float4 (t/20, t, t*2, t*3)
            // LayaAir u_Time: float (场景运行时间)

            // ⭐ 修复1：先处理vec4赋值的情况（类型转换）
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*_Time\s*\+\s*_TimeEditor\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*_TimeEditor\s*\+\s*_Time\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*_Time\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");

            // ⭐ 修复2：处理分量访问
            code = Regex.Replace(code, @"\b_Time\.y\b", "u_Time");
            code = Regex.Replace(code, @"\b_Time\.x\b", "(u_Time * 0.05)");
            code = Regex.Replace(code, @"\b_Time\.z\b", "(u_Time * 2.0)");
            code = Regex.Replace(code, @"\b_Time\.w\b", "(u_Time * 3.0)");
            code = Regex.Replace(code, @"\b_Time\.g\b", "u_Time");
            code = Regex.Replace(code, @"\b_Time\.r\b", "(u_Time * 0.05)");
            code = Regex.Replace(code, @"\b_Time\.b\b", "(u_Time * 2.0)");
            code = Regex.Replace(code, @"\b_Time\.a\b", "(u_Time * 3.0)");

            // ⭐ 修复3：其他情况的_Time转换为u_Time
            code = Regex.Replace(code, @"\b_Time\b", "u_Time");

            // ⭐ 修复4：_TimeEditor是Unity编辑器专用变量，运行时不存在
            code = Regex.Replace(code, @"\bu_Time\s*\+\s*_TimeEditor\b", "u_Time /* editor-only var removed */");
            code = Regex.Replace(code, @"\b_TimeEditor\s*\+\s*u_Time\b", "u_Time /* editor-only var removed */");
            code = Regex.Replace(code, @"\b_TimeEditor\b", "0.0 /* editor-only */");

            // Unity矩阵 -> 粒子系统矩阵（粒子已经在世界空间）
            code = Regex.Replace(code, @"\bUNITY_MATRIX_MVP\b", "(u_Projection * u_View)");
            code = Regex.Replace(code, @"\bUNITY_MATRIX_VP\b", "(u_Projection * u_View)");
            code = Regex.Replace(code, @"\bUNITY_MATRIX_V\b", "u_View");
            code = Regex.Replace(code, @"\bUNITY_MATRIX_P\b", "u_Projection");
            code = Regex.Replace(code, @"\bu_ViewProjection\b", "(u_Projection * u_View)");
            
            // 相机位置
            code = Regex.Replace(code, @"\b_WorldSpaceCameraPos\b", "u_CameraPos");
            
            // ========== PixelParams/Pixel结构体也不存在 ==========
            code = Regex.Replace(code, @"\bpixel\.positionWS\b", "v_PositionWS");
            code = Regex.Replace(code, @"\bpixel\.normalWS\b", "vec3(0.0, 0.0, 1.0)");
            code = Regex.Replace(code, @"\bpixel\.tangentWS\b", "vec3(1.0, 0.0, 0.0)");
            code = Regex.Replace(code, @"\bpixel\.bitangentWS\b", "vec3(0.0, 1.0, 0.0)");
            code = Regex.Replace(code, @"\bpixel\.viewDirWS\b", "normalize(u_CameraPos - v_PositionWS)");
            code = Regex.Replace(code, @"\bpixel\.(\w+)\b", "vec4(0.0)"); // 其他pixel成员
            
            // ========== Vertex/PixelParams声明也要移除 ==========
            code = Regex.Replace(code, @"Vertex\s+vertex\s*;\s*\n?", "");
            code = Regex.Replace(code, @"PixelParams\s+pixel\s*;\s*\n?", "");
            
            // Varying映射：粒子顶点颜色用v_Color（参考i.vcolor->v_Color）
            code = Regex.Replace(code, @"\bv_VertexColor\b", "v_Color");
            code = Regex.Replace(code, @"(\bv_Texcoord1\s*)=(\s*a_StartColor\b)", "v_Color =$2");  // VS输出
        }
        
        // Unity特有函数 - 使用平衡括号
        code = ConvertUnityVertexFunctions(code);
        
        // TRANSFORM_TEX - 使用平衡括号
        code = ConvertTransformTex(code);
        
        // UNITY_TRANSFER_FOG - 移除
        code = Regex.Replace(code, @"UNITY_TRANSFER_FOG\s*\([^)]*\)\s*;?", "");
        
        // 移除return o;
        code = Regex.Replace(code, @"\breturn\s+o\s*;", "");
        
        // 移除v2f o; 声明（已经在main中处理）
        code = Regex.Replace(code, @"(v2f|VertexOutput|Varyings)\s+o\s*;\s*\n?", "");
        
        // 移除无效的类型声明（如 vec2 o; 等转换后的残留）
        code = Regex.Replace(code, @"(vec2|vec3|vec4|float|int)\s+o\s*;\s*\n?", "");
        
        // 修复屏幕坐标引用（v_vertex -> gl_Position 或 v_PositionCS）
        code = Regex.Replace(code, @"\bv_vertex\b", "gl_Position");
        code = Regex.Replace(code, @"\bv_pos\b(?!\w)", "gl_Position");
        
        // 粒子shader后处理：替换ConvertUnityVertexFunctions生成的u_WorldMat
        if (parseResult.isParticleBillboard)
        {
            // ConvertUnityVertexFunctions会生成 u_ViewProjection * u_WorldMat，需要替换
            // 粒子系统中位置已经是世界空间，不需要u_WorldMat
            code = Regex.Replace(code, @"\bu_ViewProjection\s*\*\s*u_WorldMat\b", "(u_Projection * u_View)");
            code = Regex.Replace(code, @"\bu_WorldMat\b", "mat4(1.0)");
            
            // 替换normalize((u_WorldMat * vec4(xxx, 0.0)).xyz) -> normalize(xxx)
            code = Regex.Replace(code, @"normalize\s*\(\s*\(\s*mat4\s*\(\s*1\.0\s*\)\s*\*\s*vec4\s*\(\s*([^,]+)\s*,\s*0\.0\s*\)\s*\)\.xyz\s*\)", "normalize($1)");
            
            // 替换(mat4(1.0) * vec4(xxx, 1.0)) -> vec4(xxx, 1.0)
            code = Regex.Replace(code, @"\(\s*mat4\s*\(\s*1\.0\s*\)\s*\*\s*(vec4\s*\([^)]+\))\s*\)", "$1");
        }
        
        // 确保屏幕坐标varying被设置
        if (!code.Contains("v_ScreenPos =") && !code.Contains("v_ScreenPos="))
        {
            // 在gl_Position赋值后添加屏幕坐标计算
            code = Regex.Replace(code, 
                @"(gl_Position\s*=\s*[^;]+;)", 
                "$1\n        v_ScreenPos = gl_Position * 0.5 + vec4(0.5 * gl_Position.w);");
        }
        
        // 确保v_PositionCS被设置
        if (!code.Contains("v_PositionCS =") && !code.Contains("v_PositionCS="))
        {
            code = Regex.Replace(code, 
                @"(gl_Position\s*=\s*[^;]+;)", 
                "$1\n        v_PositionCS = gl_Position;");
        }
        
        return code;
    }

    /// <summary>
    /// 转换Unity顶点着色器特有函数
    /// </summary>
    private static string ConvertUnityVertexFunctions(string code)
    {
        // UnityObjectToClipPos
        code = ConvertFunctionWithBalancedParens(code, "UnityObjectToClipPos", (args) =>
        {
            string arg = args.Trim();
            // 确保参数是vec4类型
            if (arg.StartsWith("vec4(") || arg.Contains(".xyzw"))
                return $"(u_ViewProjection * u_WorldMat * {arg})";
            else if (arg.EndsWith(".xyz") || arg.EndsWith(".rgb"))
                return $"(u_ViewProjection * u_WorldMat * vec4({arg}, 1.0))";
            else
                return $"(u_ViewProjection * u_WorldMat * vec4({arg}, 1.0))";
        });
        
        // UnityObjectToWorldNormal
        code = ConvertFunctionWithBalancedParens(code, "UnityObjectToWorldNormal", (args) =>
        {
            return $"normalize((u_WorldMat * vec4({args}, 0.0)).xyz)";
        });
        
        // UnityObjectToWorldDir
        code = ConvertFunctionWithBalancedParens(code, "UnityObjectToWorldDir", (args) =>
        {
            return $"normalize((u_WorldMat * vec4({args}, 0.0)).xyz)";
        });
        
        // UnityWorldToObjectDir
        code = ConvertFunctionWithBalancedParens(code, "UnityWorldToObjectDir", (args) =>
        {
            return $"normalize((inverse(u_WorldMat) * vec4({args}, 0.0)).xyz)";
        });
        
        // ComputeScreenPos
        code = ConvertFunctionWithBalancedParens(code, "ComputeScreenPos", (args) =>
        {
            return $"({args} * 0.5 + 0.5)";
        });
        
        // ComputeGrabScreenPos
        code = ConvertFunctionWithBalancedParens(code, "ComputeGrabScreenPos", (args) =>
        {
            return $"({args} * 0.5 + 0.5)";
        });
        
        // SafeNormalize
        code = ConvertFunctionWithBalancedParens(code, "SafeNormalize", (args) =>
        {
            return $"normalize({args} + vec3(0.0001))";
        });
        
        return code;
    }

    /// <summary>
    /// 转换TRANSFORM_TEX宏
    /// </summary>
    private static string ConvertTransformTex(string code)
    {
        return ConvertFunctionWithBalancedParens(code, "TRANSFORM_TEX", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
            {
                string uv = parts[0].Trim();
                string texName = parts[1].Trim();

                // ⭐ 修复：移除可能的前缀（_或u_）
                // Unity: TRANSFORM_TEX(uv, _distort_tex)
                // 在ConvertHLSLToGLSL之后可能已被转换为: TRANSFORM_TEX(uv, u_distort_tex)
                if (texName.StartsWith("_"))
                    texName = texName.Substring(1);
                else if (texName.StartsWith("u_"))
                    texName = texName.Substring(2);

                // 特殊纹理名映射（参考Unity2Laya_ShaderMapping.md：主纹理用u_TilingOffset）
                string stName;
                if (texName == "MainTex" || texName == "BaseMap" || texName == "AlbedoTexture")
                    stName = "u_TilingOffset";  // 粒子/Effect主纹理统一用u_TilingOffset
                else if (texName == "BumpMap" || texName == "NormalMap" || texName == "NormalTexture")
                    stName = "u_NormalTexture_ST";
                else if (texName == "FadeEdgeTexture")
                    stName = "u_FadeEdgeTexture_ST";
                else if (texName == "DissolveDistortTex")
                    stName = "u_DissolveDistortTex_ST";
                else if (texName == "GradientMapTex0" || texName == "GradientMapTex0Map")
                    stName = "u_GradientMapTex0_ST";
                else
                    stName = $"u_{texName}_ST";

                // 如果uv包含运算符，需要用括号包围以保证正确的运算优先级
                // 例如: TRANSFORM_TEX(a + b, tex) -> ((a + b) * tex_ST.xy + tex_ST.zw)
                string uvExpr = uv;
                if (uv.Contains("+") || uv.Contains("-") || uv.Contains("*") || uv.Contains("/"))
                {
                    // 检查是否已经被括号包围
                    if (!uv.TrimStart().StartsWith("(") || !uv.TrimEnd().EndsWith(")"))
                        uvExpr = $"({uv})";
                }

                return $"({uvExpr} * {stName}.xy + {stName}.zw)";
            }
            return args;
        });
    }

    /// <summary>
    /// 转换片元着色器代码
    /// </summary>
    private static string ConvertFragmentShaderCode(string hlslCode, ShaderParseResult parseResult)
    {
        string code = ConvertHLSLToGLSL(hlslCode);
        
        // 替换输入变量（i.xxx -> v_xxx，使用映射表）
        foreach (var kvp in parseResult.varyings)
        {
            string pattern = $@"\bi\.{Regex.Escape(kvp.Key)}\b";
            code = Regex.Replace(code, pattern, kvp.Value.glslName);
        }
        // 通用替换
        code = Regex.Replace(code, @"\bi\.(\w+)", "v_$1");
        
        // 修复屏幕坐标引用
        code = Regex.Replace(code, @"\bv_vertex\b", "v_ScreenPos");
        code = Regex.Replace(code, @"\bv_screenPos\b", "v_ScreenPos");
        code = Regex.Replace(code, @"\bv_grabPos\b", "v_ScreenPos");
        
        // ========== 粒子shader特殊处理 ==========
        if (parseResult.isParticleBillboard)
        {
            // 粒子shader中没有pixel/PixelParams结构体
            // pixel.xxx -> 对应的varying或默认值
            code = Regex.Replace(code, @"\bpixel\.positionWS\b", "v_PositionWS");
            code = Regex.Replace(code, @"\bpixel\.normalWS\b", "vec3(0.0, 0.0, 1.0)");
            code = Regex.Replace(code, @"\bpixel\.tangentWS\b", "vec3(1.0, 0.0, 0.0)");
            code = Regex.Replace(code, @"\bpixel\.bitangentWS\b", "vec3(0.0, 1.0, 0.0)");
            code = Regex.Replace(code, @"\bpixel\.viewDirWS\b", "normalize(u_CameraPos - v_PositionWS)");
            code = Regex.Replace(code, @"\bpixel\.uv0\b", "v_TextureCoordinate");
            code = Regex.Replace(code, @"\bpixel\.uv\b", "v_TextureCoordinate");
            code = Regex.Replace(code, @"\bpixel\.color\b", "v_Color");
            code = Regex.Replace(code, @"\bpixel\.vertexColor\b", "v_Color");
            
            // 捕获所有剩余的 pixel.xxx
            code = Regex.Replace(code, @"\bpixel\.(\w+)\b", match =>
            {
                string member = match.Groups[1].Value.ToLower();
                switch (member)
                {
                    case "positionws":
                        return "v_PositionWS";
                    case "normalws":
                        return "vec3(0.0, 0.0, 1.0)";
                    case "tangentws":
                        return "vec3(1.0, 0.0, 0.0)";
                    case "bitangentws":
                        return "vec3(0.0, 1.0, 0.0)";
                    case "viewdirws":
                        return "normalize(u_CameraPos - v_PositionWS)";
                    case "uv0":
                    case "uv":
                    case "uv1":
                        return "v_TextureCoordinate";
                    case "color":
                    case "vertexcolor":
                        return "v_Color";
                    default:
                        return "vec4(0.0)";
                }
            });
            
            // 移除PixelParams声明
            code = Regex.Replace(code, @"PixelParams\s+pixel\s*;\s*\n?", "");
            code = Regex.Replace(code, @"\binitPixelParams\s*\([^)]*\)\s*;?", "");
            
            // ⭐ 修复：粒子shader中的时间变量统一使用u_Time（引擎内置变量）
            // Unity _Time: float4 (t/20, t, t*2, t*3)
            // LayaAir u_Time: float (场景运行时间)

            // ⭐ 修复1：先处理vec4赋值的情况（类型转换）
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*_Time\s*\+\s*u_TimeEditor\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*u_TimeEditor\s*\+\s*_Time\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*_Time\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*u_Time\s*\+\s*u_TimeEditor\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
            code = Regex.Replace(code,
                @"(float4|vec4)\s+(\w+)\s*=\s*u_TimeEditor\s*\+\s*u_Time\b",
                "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");

            // ⭐ 修复2：处理分量访问
            code = Regex.Replace(code, @"\b_Time\.y\b", "u_Time");
            code = Regex.Replace(code, @"\b_Time\.x\b", "(u_Time * 0.05)");
            code = Regex.Replace(code, @"\b_Time\.z\b", "(u_Time * 2.0)");
            code = Regex.Replace(code, @"\b_Time\.w\b", "(u_Time * 3.0)");
            code = Regex.Replace(code, @"\b_Time\.g\b", "u_Time");
            code = Regex.Replace(code, @"\b_Time\.r\b", "(u_Time * 0.05)");
            code = Regex.Replace(code, @"\b_Time\.b\b", "(u_Time * 2.0)");
            code = Regex.Replace(code, @"\b_Time\.a\b", "(u_Time * 3.0)");

            // ⭐ 修复3：其他情况的_Time转换为u_Time
            code = Regex.Replace(code, @"\b_Time\b", "u_Time");

            // ⭐ 修复4：_TimeEditor是Unity编辑器专用变量，运行时不存在（FS）
            code = Regex.Replace(code, @"\bu_Time\s*\+\s*u_TimeEditor\b", "u_Time /* editor-only var removed */");
            code = Regex.Replace(code, @"\bu_TimeEditor\s*\+\s*u_Time\b", "u_Time /* editor-only var removed */");
            code = Regex.Replace(code, @"\bu_TimeEditor\b", "0.0 /* editor-only */");

            // 相机位置
            code = Regex.Replace(code, @"\b_WorldSpaceCameraPos\b", "u_CameraPos");
            
            // 主纹理_ST：粒子主纹理统一用u_TilingOffset（参考Unity2Laya_ShaderMapping.md）
            code = Regex.Replace(code, @"\bu_MainTex_ST\b", "u_TilingOffset");
            code = Regex.Replace(code, @"\bu_AlbedoTexture_ST\b", "u_TilingOffset");
            
            // 法线贴图_ST：Unity可能用u_NormalMap_ST，Laya粒子模板用u_NormalTexture_ST
            code = Regex.Replace(code, @"\bu_NormalMap_ST\b", "u_NormalTexture_ST");
            
            // Varying映射：粒子颜色用v_Color（参考：i.vcolor->v_Color）
            code = Regex.Replace(code, @"\bv_VertexColor\b", "v_Color");

            // ⭐ 修复：Uniform颜色映射（与uniformMap中的定义保持一致）
            // 粒子shader uniformMap中定义的是u_AlbedoColor，但代码中可能使用u_Color
            code = Regex.Replace(code, @"\bu_Color\b", "u_AlbedoColor");
            code = Regex.Replace(code, @"\bv_Texcoord1\b", "v_Color");  // 当v_Texcoord1用于顶点颜色时
            
            // 修复语法错误：mat3(transpose); (inverse(u_View)) 等
            code = Regex.Replace(code, @"\(\s*mat3\s*\(\s*transpose\s*\)\s*;\s*\(\s*inverse\s*\(\s*u_View\s*\)\s*\)", "mat3(inverse(transpose(u_View)))");
            code = Regex.Replace(code, @"\(\s*mat3\s*\(\s*transpose\s*\)\s*;\s*\(\s*inverse\s*\(\s*u_View\s*\*\s*mat4\s*\(\s*1\.0\s*\)\s*\)\s*\)", "mat3(inverse(transpose(u_View)))");
        }
        
        // 替换return为gl_FragColor赋值
        code = Regex.Replace(code, @"\breturn\s+([^;]+)\s*;", "gl_FragColor = $1;");
        
        // UNITY_APPLY_FOG - 移除（在main函数末尾统一处理）
        code = Regex.Replace(code, @"UNITY_APPLY_FOG[_\w]*\s*\([^)]*\)\s*;?", "");
        
        // 移除facing参数相关
        code = Regex.Replace(code, @",?\s*float\s+facing\s*:\s*VFACE", "");
        code = Regex.Replace(code, @"\bfacing\b", "1.0");

        // ⭐ v_Texcoord0的处理已经在ApplySystematicTypeFixes的阶段6中智能处理
        // 不在这里强制替换，避免错误地把vec4变成vec2

        // 最后一次修复.rgb和.a运算中的vec4变量
        code = Regex.Replace(code,
            @"(\.(?:rgb|a)\s*[\*\+\-/]?=\s*)([^;]+);",
            match => {
                string prefix = match.Groups[1].Value;
                string expr = match.Groups[2].Value;
                bool isRgb = prefix.Contains("rgb");

                // 根据是.rgb还是.a来添加相应的分量访问
                if (isRgb)
                {
                    expr = Regex.Replace(expr, @"\bv_Color(?!\.)", "v_Color.rgb");
                    expr = Regex.Replace(expr, @"\bu_TintColor(?!\.)", "u_TintColor.rgb");
                    expr = Regex.Replace(expr, @"\bu_AlbedoColor(?!\.)", "u_AlbedoColor.rgb");
                    expr = Regex.Replace(expr, @"\bu_BaseColor(?!\.)", "u_BaseColor.rgb");
                }
                else
                {
                    expr = Regex.Replace(expr, @"\bv_Color(?!\.)", "v_Color.a");
                    expr = Regex.Replace(expr, @"\bu_TintColor(?!\.)", "u_TintColor.a");
                    expr = Regex.Replace(expr, @"\bu_AlbedoColor(?!\.)", "u_AlbedoColor.a");
                    expr = Regex.Replace(expr, @"\bu_BaseColor(?!\.)", "u_BaseColor.a");
                }

                // 清理重复的分量访问
                expr = Regex.Replace(expr, @"\.rgb\.rgb", ".rgb");
                expr = Regex.Replace(expr, @"\.a\.a", ".a");

                return $"{prefix}{expr};";
            });

        return code;
    }

    /// <summary>
    /// HLSL到GLSL的通用代码转换（混合架构）
    /// </summary>
    private static string ConvertHLSLToGLSL(string hlslCode)
    {
        // 初始化映射引擎
        InitializeMappingEngine();

        string code = hlslCode;

        // ==================== 混合架构：映射表优先模式 ====================
        if (useMappingTableMode && mappingEngine != null && mappingEngine.HasRules())
        {
            conversionTimer.Restart();

            // 优先使用映射表转换
            code = mappingEngine.ApplyMappings(code, "all");

            mappingTableConversionTime = conversionTimer.ElapsedMilliseconds;

            ExportLogger.Log($"LayaAir3D: Mapping table applied - {mappingEngine.GetStatistics()}");

            // 检查是否还有未转换的Unity特有标识符
            bool needsFallback = CheckNeedsFallback(code);

            if (needsFallback)
            {
                ExportLogger.Log("LayaAir3D: Some Unity-specific code not covered by mapping table, applying built-in fallback...");

                conversionTimer.Restart();

                // 使用内置规则作为回退
                code = ApplyBuiltInConversionAsFallback(code);

                builtInConversionTime = conversionTimer.ElapsedMilliseconds;
            }

            return code;
        }

        // ==================== 内置模式：硬编码规则 ====================

        conversionTimer.Restart();

        // ⭐ 修复错误的swizzle访问 (CRITICAL FIX)
        // vec2没有.z和.w分量，vec3没有.w分量，这是Unity shader代码转换错误导致的
        // 例如：u_TilingOffset.xy.z -> u_TilingOffset.z
        code = Regex.Replace(code, @"(\w+)\.xy\.z\b", "$1.z");
        code = Regex.Replace(code, @"(\w+)\.xy\.w\b", "$1.w");
        code = Regex.Replace(code, @"(\w+)\.xyz\.w\b", "$1.w");
        code = Regex.Replace(code, @"(\w+)\.x\.y\b", "$1.y");
        code = Regex.Replace(code, @"(\w+)\.x\.z\b", "$1.z");
        code = Regex.Replace(code, @"(\w+)\.x\.w\b", "$1.w");
        code = Regex.Replace(code, @"(\w+)\.y\.z\b", "$1.z");
        code = Regex.Replace(code, @"(\w+)\.y\.w\b", "$1.w");
        code = Regex.Replace(code, @"(\w+)\.z\.w\b", "$1.w");

        // ============================================
        // 类型转换（注意顺序，先转换长的）
        // ============================================
        code = Regex.Replace(code, @"\bfloat4x4\b", "mat4");
        code = Regex.Replace(code, @"\bfloat3x3\b", "mat3");
        code = Regex.Replace(code, @"\bfloat2x2\b", "mat2");
        code = Regex.Replace(code, @"\bhalf4x4\b", "mat4");
        code = Regex.Replace(code, @"\bhalf3x3\b", "mat3");
        code = Regex.Replace(code, @"\bhalf2x2\b", "mat2");
        code = Regex.Replace(code, @"\bfloat4\b", "vec4");
        code = Regex.Replace(code, @"\bfloat3\b", "vec3");
        code = Regex.Replace(code, @"\bfloat2\b", "vec2");
        code = Regex.Replace(code, @"\bhalf4\b", "vec4");
        code = Regex.Replace(code, @"\bhalf3\b", "vec3");
        code = Regex.Replace(code, @"\bhalf2\b", "vec2");
        code = Regex.Replace(code, @"\bhalf\b(?!\s*\()", "float"); // 避免匹配half()构造函数
        code = Regex.Replace(code, @"\bfixed4\b", "vec4");
        code = Regex.Replace(code, @"\bfixed3\b", "vec3");
        code = Regex.Replace(code, @"\bfixed2\b", "vec2");
        code = Regex.Replace(code, @"\bfixed\b(?!\s*\()", "float");
        code = Regex.Replace(code, @"\bsamplerCUBE\b", "samplerCube");
        
        // ============================================
        // 纹理采样函数（使用平衡括号匹配）
        // ============================================
        code = ConvertTextureSampling(code);

        // ============================================
        // ⭐ sRGB线性化函数修复（GLSL不支持vec3 <= scalar的分量比较）
        // HLSL: color <= 0.04045 ? color / 12.92 : pow((color + 0.055) / 1.055, 2.4)
        // GLSL: 需要用step()实现分量级的条件选择
        // ============================================
        // 先处理 half3/float3 版本（类型转换前可能出现half3）
        code = Regex.Replace(code,
            @"(half3|float3)\s+(\w+)\s*\(\s*(half3|float3)\s+(\w+)\s*\)\s*\{[^}]*?\4\s*<=\s*0\.04045[^}]*?\}",
            m => ConvertGamma22ToLinear(m.Value));
        // 再处理已转换后的 vec3 版本
        code = Regex.Replace(code,
            @"vec3\s+(\w+)\s*\(\s*vec3\s+(\w+)\s*\)\s*\{[^}]*?\2\s*<=\s*0\.04045[^}]*?\}",
            m => ConvertGamma22ToLinear(m.Value));

        // ============================================
        // 数学函数
        // ============================================
        code = Regex.Replace(code, @"\blerp\b", "mix");
        code = ConvertSaturate(code); // 使用平衡括号处理saturate
        code = Regex.Replace(code, @"\bfrac\b", "fract");
        code = Regex.Replace(code, @"\bddx\b", "dFdx");
        code = Regex.Replace(code, @"\bddy\b", "dFdy");
        code = Regex.Replace(code, @"\brsqrt\b", "inversesqrt");
        
        // atan2(y, x) -> atan(y, x) - GLSL的atan支持两个参数
        code = Regex.Replace(code, @"\batan2\b", "atan");
        
        // mul函数 - 使用平衡括号匹配
        code = ConvertMulFunction(code);
        
        // sincos函数转换 - 使用平衡括号匹配处理更复杂的情况
        code = ConvertSinCosFunction(code);
        
        // clip函数转换为discard - 使用平衡括号
        code = ConvertClipFunction(code);
        
        // ============================================
        // 类型转换语法修复 (float3x3)x -> mat3(x)
        // ============================================
        code = ConvertTypeCastSyntax(code);
        
        // ============================================
        // Unity内置变量（在变量名转换之前处理）
        // 参考 LayaAir_BuiltIn_Uniforms.md 文档
        // ============================================
        
        // 时间变量 (SceneCommon.glsl)
        // Unity _Time: float4 (t/20, t, t*2, t*3)
        // LayaAir u_Time: float 场景运行时间（秒）

        // ⭐ 修复1：先处理vec4赋值的情况（类型转换）
        // Unity: vec4 time = _Time + _TimeEditor;
        // LayaAir: vec4 time = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0);
        code = Regex.Replace(code,
            @"(float4|vec4)\s+(\w+)\s*=\s*_Time\s*\+\s*_TimeEditor\b",
            "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");
        code = Regex.Replace(code,
            @"(float4|vec4)\s+(\w+)\s*=\s*_TimeEditor\s*\+\s*_Time\b",
            "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");

        // ⭐ 修复2：处理分量访问（必须在整体替换之前）
        code = Regex.Replace(code, @"_Time\.y", "u_Time");      // t -> u_Time
        code = Regex.Replace(code, @"_Time\.x", "(u_Time * 0.05)"); // t/20
        code = Regex.Replace(code, @"_Time\.z", "(u_Time * 2.0)");  // t*2
        code = Regex.Replace(code, @"_Time\.w", "(u_Time * 3.0)");  // t*3
        code = Regex.Replace(code, @"_Time\.g", "u_Time");      // t
        code = Regex.Replace(code, @"_Time\.r", "(u_Time * 0.05)");
        code = Regex.Replace(code, @"_Time\.b", "(u_Time * 2.0)");
        code = Regex.Replace(code, @"_Time\.a", "(u_Time * 3.0)");

        // ⭐ 修复3：处理剩余的vec4类型赋值（没有_TimeEditor的情况）
        code = Regex.Replace(code,
            @"(float4|vec4)\s+(\w+)\s*=\s*_Time\b",
            "$1 $2 = vec4(u_Time * 0.05, u_Time, u_Time * 2.0, u_Time * 3.0)");

        // ⭐ 修复4：处理其他情况的_Time（不是vec4赋值，也不是分量访问）
        // 这种情况通常是错误的，但为了兼容性，转换为u_Time
        code = Regex.Replace(code, @"\b_Time\b", "u_Time");

        // ⭐ 修复5：_TimeEditor是Unity编辑器专用变量，运行时不存在
        // 移除所有包含_TimeEditor的加法表达式（如果还有的话）
        code = Regex.Replace(code, @"\bu_Time\s*\+\s*_TimeEditor\b", "u_Time /* editor-only var removed */");
        code = Regex.Replace(code, @"\b_TimeEditor\s*\+\s*u_Time\b", "u_Time /* editor-only var removed */");
        code = Regex.Replace(code, @"\b_Time\s*\+\s*_TimeEditor\b", "u_Time /* editor-only var removed */");
        code = Regex.Replace(code, @"\b_TimeEditor\s*\+\s*_Time\b", "u_Time /* editor-only var removed */");
        // 单独出现的_TimeEditor替换为0.0
        code = Regex.Replace(code, @"\b_TimeEditor\b", "0.0 /* editor-only */");

        // _SinTime, _CosTime
        code = Regex.Replace(code, @"_SinTime\.w", "sin(u_Time)");
        code = Regex.Replace(code, @"_SinTime\.z", "sin(u_Time * 0.5)");
        code = Regex.Replace(code, @"_SinTime\.y", "sin(u_Time * 0.25)");
        code = Regex.Replace(code, @"_SinTime\.x", "sin(u_Time * 0.125)");
        code = Regex.Replace(code, @"_CosTime\.w", "cos(u_Time)");
        code = Regex.Replace(code, @"_CosTime\.z", "cos(u_Time * 0.5)");
        code = Regex.Replace(code, @"_CosTime\.y", "cos(u_Time * 0.25)");
        code = Regex.Replace(code, @"_CosTime\.x", "cos(u_Time * 0.125)");
        
        // 矩阵变量 (CameraCommon.glsl, Sprite3DCommon.glsl)
        code = Regex.Replace(code, @"\bunity_MatrixInvV\b", "inverse(u_View)");
        code = Regex.Replace(code, @"\bunity_WorldTransformParams\.w\b", "1.0");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_IT_MV\b", "transpose(inverse(u_View * u_WorldMat))");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_MVP\b", "(u_ViewProjection * u_WorldMat)");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_MV\b", "(u_View * u_WorldMat)");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_V\b", "u_View");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_P\b", "u_Projection");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_M\b", "u_WorldMat");
        code = Regex.Replace(code, @"\bUNITY_MATRIX_VP\b", "u_ViewProjection");
        code = Regex.Replace(code, @"\bunity_MatrixVP\b", "u_ViewProjection");
        code = Regex.Replace(code, @"\bunity_ObjectToWorld\b", "u_WorldMat");
        code = Regex.Replace(code, @"\bunity_WorldToObject\b", "inverse(u_WorldMat)");
        
        // 相机变量 (CameraCommon.glsl)
        code = Regex.Replace(code, @"\b_WorldSpaceCameraPos\b", "u_CameraPos");
        code = Regex.Replace(code, @"\b_ProjectionParams\b", "u_ProjectionParams");
        code = Regex.Replace(code, @"\b_ZBufferParams\b", "u_ZBufferParams");
        code = Regex.Replace(code, @"\b_ScreenParams\b", "u_Viewport");
        
        // 光照变量 (Lighting.glsl)
        // 注意：文档中是 u_DirLightDirection，不是 u_DirationLightDirection
        code = Regex.Replace(code, @"\b_WorldSpaceLightPos0\b", "u_DirLightDirection");
        code = Regex.Replace(code, @"\b_LightColor0\b", "vec4(u_DirLightColor, 1.0)");
        
        // 雾效变量 (SceneCommon.glsl)
        code = Regex.Replace(code, @"\bunity_FogParams\b", "u_FogParams");
        code = Regex.Replace(code, @"\bunity_FogColor\b", "u_FogColor");
        
        // 环境光 (globalIllumination.glsl)
        code = Regex.Replace(code, @"\bunity_AmbientSky\b", "u_AmbientColor");
        code = Regex.Replace(code, @"\bunity_AmbientEquator\b", "u_AmbientColor");
        code = Regex.Replace(code, @"\bunity_AmbientGround\b", "u_AmbientColor");
        code = Regex.Replace(code, @"\bUNITY_LIGHTMODEL_AMBIENT\b", "u_AmbientColor");
        
        // 光照贴图 (globalIllumination.glsl)
        code = Regex.Replace(code, @"\bunity_Lightmap\b", "u_LightMap");
        code = Regex.Replace(code, @"\bunity_LightmapST\b", "u_LightmapScaleOffset");
        
        // 反射探针 (globalIllumination.glsl)
        code = Regex.Replace(code, @"\bunity_SpecCube0\b", "u_IBLTex");
        code = Regex.Replace(code, @"\bunity_SpecCube0_HDR\b", "u_ReflectCubeHDRParams");
        
        // ============================================
        // 变量名转换 (_XXX -> u_XXX)
        // 注意：需要避免转换已经是u_开头的，以及特殊前缀
        // ============================================
        // ⭐ 修复：匹配所有以_开头的变量名（包括小写字母开头的）
        // 原规则：@"(?<![a-zA-Z0-9_])_([A-Z]\w*)\b" 只匹配大写字母开头
        // 新规则：匹配大写或小写字母开头的变量名
        code = Regex.Replace(code, @"(?<![a-zA-Z0-9_])_([a-zA-Z]\w*)\b", "u_$1");

        // ============================================
        // 特殊纹理名称映射（在通用转换之后）
        // ============================================
        code = Regex.Replace(code, @"\bu_MainTex\b", "u_AlbedoTexture");
        code = Regex.Replace(code, @"\bu_BaseMap\b", "u_AlbedoTexture");
        code = Regex.Replace(code, @"\bu_Color\b(?!\.)", "u_AlbedoColor"); // 避免匹配 u_Color.xxx
        code = Regex.Replace(code, @"\bu_BaseColor\b", "u_AlbedoColor");
        code = Regex.Replace(code, @"\bu_BumpMap\b", "u_NormalTexture");
        code = Regex.Replace(code, @"\bu_NormalMap\b", "u_NormalTexture");
        code = Regex.Replace(code, @"\bu_BumpScale\b", "u_NormalScale");
        code = Regex.Replace(code, @"\bu_Cutoff\b", "u_AlphaTestValue");
        code = Regex.Replace(code, @"\bu_EmissionMap\b", "u_EmissionTexture");
        
        // 修复双前缀 u_u_ -> u_
        code = Regex.Replace(code, @"\bu_u_", "u_");
        
        // ============================================
        // 条件编译转换
        // ============================================
        // 处理复杂的条件编译：#ifdef A || defined(B) -> #if defined(A) || defined(B)
        code = Regex.Replace(code, @"#ifdef\s+(\w+)\s*\|\|\s*defined\s*\(\s*(\w+)\s*\)", "#if defined($1) || defined($2)");
        code = Regex.Replace(code, @"#ifdef\s+(\w+)\s*&&\s*defined\s*\(\s*(\w+)\s*\)", "#if defined($1) && defined($2)");
        
        // 修复 #elif 后面直接跟宏名的错误语法
        // #elif MACRO_NAME -> #elif defined(MACRO_NAME)
        // 注意：要排除已经是 defined() 形式的
        code = Regex.Replace(code, @"#elif\s+(?!defined\s*\()(\w+)\s*$", "#elif defined($1)", RegexOptions.Multiline);
        
        // 简单的条件编译
        code = Regex.Replace(code, @"#if\s+defined\s*\(\s*(\w+)\s*\)\s*$", "#ifdef $1", RegexOptions.Multiline);
        code = Regex.Replace(code, @"#if\s+!\s*defined\s*\(\s*(\w+)\s*\)\s*$", "#ifndef $1", RegexOptions.Multiline);
        code = Regex.Replace(code, @"#elif\s+defined\s*\(\s*(\w+)\s*\)\s*$", "#elif defined($1)", RegexOptions.Multiline);
        
        // ============================================
        // 移除不支持的Unity宏
        // ============================================
        code = Regex.Replace(code, @"UNITY_FOG_COORDS\s*\(\s*\d+\s*\)\s*;?", "");
        code = Regex.Replace(code, @"UNITY_VERTEX_INPUT_INSTANCE_ID\s*;?", "");
        code = Regex.Replace(code, @"UNITY_VERTEX_OUTPUT_STEREO\s*;?", "");
        code = Regex.Replace(code, @"UNITY_SETUP_INSTANCE_ID\s*\([^)]*\)\s*;?", "");
        code = Regex.Replace(code, @"UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO\s*\([^)]*\)\s*;?", "");
        code = Regex.Replace(code, @"UNITY_INITIALIZE_OUTPUT\s*\([^)]*\)\s*;?", "");
        code = Regex.Replace(code, @"UNITY_BRANCH\s*", "");
        code = Regex.Replace(code, @"UNITY_UNROLL\s*", "");
        code = Regex.Replace(code, @"UNITY_LOOP\s*", "");
        
        // ============================================
        // 修复宏名称
        // ============================================
        // u_XXX_ON -> XXX (宏开关)
        code = Regex.Replace(code, @"\bu_(\w+)_ON\b", "$1");
        // _XXX_ON -> XXX (可能残留的)
        code = Regex.Replace(code, @"\b_(\w+)_ON\b", "$1");
        
        // 修复条件编译中的宏名（移除u_前缀）
        code = Regex.Replace(code, @"#ifdef\s+u_([A-Z]\w*)\b", "#ifdef $1");
        code = Regex.Replace(code, @"#ifndef\s+u_([A-Z]\w*)\b", "#ifndef $1");
        code = Regex.Replace(code, @"#if\s+defined\s*\(\s*u_([A-Z]\w*)\s*\)", "#if defined($1)");
        code = Regex.Replace(code, @"#elif\s+defined\s*\(\s*u_([A-Z]\w*)\s*\)", "#elif defined($1)");
        code = Regex.Replace(code, @"defined\s*\(\s*u_([A-Z]\w*)\s*\)", "defined($1)");
        
        // 修复 #elif 直接跟 u_XXX 的情况（先转换为 defined() 形式，再移除 u_ 前缀）
        // 这种情况可能在前面的转换中产生
        code = Regex.Replace(code, @"#elif\s+u_([A-Z]\w*)\s*$", "#elif defined($1)", RegexOptions.Multiline);
        
        // ============================================
        // 常量转换
        // ============================================
        code = Regex.Replace(code, @"\bHALF_MIN\b", "0.00006103515625");
        code = Regex.Replace(code, @"\bHALF_MAX\b", "65504.0");
        code = Regex.Replace(code, @"\bFLT_MIN\b", "1.175494351e-38");
        code = Regex.Replace(code, @"\bFLT_MAX\b", "3.402823466e+38");
        code = Regex.Replace(code, @"\bFLT_EPSILON\b", "1.192092896e-07");
        code = Regex.Replace(code, @"(?<![A-Z_])PI\b", "3.14159265359");
        code = Regex.Replace(code, @"\bTWO_PI\b", "6.28318530718");
        code = Regex.Replace(code, @"\bHALF_PI\b", "1.57079632679");
        code = Regex.Replace(code, @"\bINV_PI\b", "0.31830988618");
        code = Regex.Replace(code, @"\bINV_TWO_PI\b", "0.15915494309");
        
        // ============================================
        // Unity URP/HDRP 函数转换
        // ============================================
        // GlossyEnvironmentReflection - 简化为反射采样
        code = ConvertGlossyEnvironmentReflection(code);
        
        // GetViewForwardDir
        code = ConvertFunctionWithBalancedParens(code, "GetViewForwardDir", (args) =>
        {
            return "normalize(u_CameraPos - v_PositionWS)";
        });
        
        // TransformObjectToWorld
        code = ConvertFunctionWithBalancedParens(code, "TransformObjectToWorld", (args) =>
        {
            return $"(u_WorldMat * vec4({args}, 1.0)).xyz";
        });
        
        // TransformWorldToObject
        code = ConvertFunctionWithBalancedParens(code, "TransformWorldToObject", (args) =>
        {
            return $"(inverse(u_WorldMat) * vec4({args}, 1.0)).xyz";
        });
        
        // TransformObjectToWorldDir
        code = ConvertFunctionWithBalancedParens(code, "TransformObjectToWorldDir", (args) =>
        {
            return $"normalize((u_WorldMat * vec4({args}, 0.0)).xyz)";
        });
        
        // TransformObjectToWorldNormal
        code = ConvertFunctionWithBalancedParens(code, "TransformObjectToWorldNormal", (args) =>
        {
            return $"normalize((u_WorldMat * vec4({args}, 0.0)).xyz)";
        });

        // TRANSFORM_TEX - 纹理坐标转换宏
        code = ConvertTransformTex(code);

        // ============================================
        // 数字后缀处理（移除f/h后缀）
        // 注意：使用负向后顾断言确保数字前面不是字母，避免将 v2f 转换为 v2.0
        // ============================================
        code = Regex.Replace(code, @"(?<![a-zA-Z_])(\d+\.\d*)[fFhH]\b", "$1");
        code = Regex.Replace(code, @"(?<![a-zA-Z_])(\d+)[fFhH]\b", "$1.0");
        
        // ============================================
        // 修复float类型与整数比较的问题
        // GLSL中float不能直接与int比较，需要将整数转换为浮点数
        // 例如: if (u_Mode == 1) -> if (u_Mode == 1.0)
        // ============================================
        // 匹配 u_XXX == 整数 或 u_XXX != 整数 等比较
        code = Regex.Replace(code, @"(\bu_\w+\s*)(==|!=|<|>|<=|>=)\s*(\d+)(?!\.)", "$1$2 $3.0");
        // 匹配 整数 == u_XXX 等比较
        code = Regex.Replace(code, @"(?<!\.)(\d+)\s*(==|!=|<|>|<=|>=)(\s*u_\w+\b)", "$1.0 $2$3");
        // 匹配其他可能的变量与整数比较（更通用的情况）
        // 注意：这里要小心不要影响数组索引等合法的整数使用
        // 只处理比较运算符两边的情况
        code = Regex.Replace(code, @"(\b[a-zA-Z_]\w*\s*)(==|!=)\s*(\d+)(?!\.|\s*\])", "$1$2 $3.0");
        code = Regex.Replace(code, @"(?<!\.)(\d+)\s*(==|!=)(\s*[a-zA-Z_]\w*\b)(?!\s*\[)", "$1.0 $2$3");

        // ============================================
        // 修复特定模式的类型不匹配错误
        // HLSL允许隐式类型转换，但GLSL不允许
        // 针对性地修复常见的类型错误模式
        // ============================================

        // 修复模式: vec2 var = scalar_expr * scalar_expr;
        // 例如: vec2 distortOffset = distortCol.r * u_QD;
        // 转换为: vec2 distortOffset = vec2(distortCol.r * u_QD);
        code = Regex.Replace(code,
            @"(vec2\s+\w+\s*=\s*)(?!vec[234]\()([^;]+?)(\s*\*\s*[^;]+?)(;)",
            match => {
                string prefix = match.Groups[1].Value;
                string expr = match.Groups[2].Value + match.Groups[3].Value;
                string semicolon = match.Groups[4].Value;

                // 检查表达式是否包含成员访问符（点号）或者已经有vec构造函数
                // 如果表达式看起来像是标量运算（包含.r .g .b .a或.x .y .z .w等swizzle）
                if ((expr.Contains(".r") || expr.Contains(".g") || expr.Contains(".b") || expr.Contains(".a") ||
                     expr.Contains(".x") || expr.Contains(".y") || expr.Contains(".z") || expr.Contains(".w")) &&
                    !expr.TrimStart().StartsWith("vec") &&
                    !expr.Contains("vertex.texCoord") && // 不处理已经是vec2的成员
                    !expr.Contains("v_Texcoord"))        // 不处理varying
                {
                    return $"{prefix}vec2({expr.Trim()}){semicolon}";
                }
                return match.Value;
            });

        // 修复模式: vec3 var = scalar; 或 vec3 var = vec2_expr;
        // 但要非常小心，不要误处理正确的代码

        // 修复模式: vec4 var = vec3_expr;
        // 例如: vec4 c = u_TintColor * v_VertexColor * m * n * 2.0;
        // 这种通常是正确的（vec4 * vec4 = vec4），不需要修复
        // 只修复明显错误的情况，如 vec4 var = vertex.positionOS (vec3);
        code = Regex.Replace(code,
            @"(vec4\s+\w+\s*=\s*)(?!vec[234]\()(\w+\.positionOS)(;)",
            "$1vec4($2, 1.0)$3");

        // ============================================
        // 清理多余空行
        // ============================================
        code = Regex.Replace(code, @"\n\s*\n\s*\n", "\n\n");

        // ============================================
        // ⭐ 修复粒子shader特有的类型问题
        // ============================================

        // 修复1: texture2D中v_Texcoord0需要.xy（当它是vec4时）
        // 匹配所有 texture2D(xxx, v_Texcoord0xxx) 但 v_Texcoord0 后面不是 .xy/.zw
        code = Regex.Replace(code,
            @"texture2D\s*\(\s*(\w+)\s*,\s*v_Texcoord0(?!\.)",
            "texture2D($1, v_Texcoord0.xy");

        // 修复2: 向量vec4类型uniform在vec2运算时需要.xy
        // 匹配: u_UVScroll/u_TilingOffset等在运算或函数参数中，但后面不是.xy/.zw/.xyz/.xyzw
        code = Regex.Replace(code,
            @"\b(u_UVScroll|u_TilingOffset|u_Scroll)(?!\.)([\s\)\+\-\*,])",
            match =>
            {
                string varName = match.Groups[1].Value;
                string suffix = match.Groups[2].Value;
                return $"{varName}.xy{suffix}";
            });

        // 修复3: 整数字面量乘法转换为浮点数
        // 匹配: col.rgb *= 2 * v_Color 或 *= 2 * v_Color 等模式
        // 非常重要：必须在处理向量操作之前先把整数转为浮点数
        code = Regex.Replace(code,
            @"(\*=)\s*([0-9]+)(?!\.)\s*\*",
            "$1 $2.0 *");

        // 修复: = 2* v_Color (等号后紧跟整数乘v_Color)
        code = Regex.Replace(code,
            @"(=\s*)([0-9]+)(?!\.)\s*\*\s*(v_Color|u_TintColor|v_VertexColor)\b",
            "$1$2.0 * $3");

        // 修复: 一般情况的 整数 * v_Color
        code = Regex.Replace(code,
            @"([^\d\.])\s*([0-9]+)(?!\.)\s*\*\s*(v_Color|u_TintColor|v_VertexColor)(?!\.)",
            match =>
            {
                string prefix = match.Groups[1].Value;
                string num = match.Groups[2].Value;
                string varName = match.Groups[3].Value;
                return $"{prefix} {num}.0 * {varName}";
            });

        // 修复4: 整数乘以向量需要加.0（更通用的情况）
        // 匹配: 2 * vec_var 或 vec_var * 2
        code = Regex.Replace(code,
            @"([^\d\.])([0-9]+)\s*\*\s*(v_\w+|u_\w+)\b",
            match =>
            {
                string prefix = match.Groups[1].Value;
                string num = match.Groups[2].Value;
                string varName = match.Groups[3].Value;
                // 只转换颜色、顶点相关的向量
                if (varName.Contains("Color") || varName.Contains("Vertex") || varName.Contains("Tint"))
                {
                    return $"{prefix}{num}.0 * {varName}";
                }
                return match.Value;
            });

        // 修复5: 确保向量分量访问（v_Color需要.rgb）
        // 匹配: 数字 * v_Color * xxx.rgb，v_Color应该是v_Color.rgb
        code = Regex.Replace(code,
            @"(\d+\.0|\d+)\s*\*\s*(v_Color|v_VertexColor)(?!\.)\s*\*\s*(\w+)\.rgb",
            "$1 * $2.rgb * $3.rgb");

        // 修复6: v_Color在任何与.rgb操作的表达式中都需要加.rgb
        // 处理 *= ... * v_Color * ...的情况
        code = Regex.Replace(code,
            @"\.rgb\s*\*=([^;]*)\b(v_Color|v_VertexColor)(?!\.)\b([^;]*?)(u_\w+)\.rgb",
            match =>
            {
                string expr1 = match.Groups[1].Value;
                string vColor = match.Groups[2].Value;
                string expr2 = match.Groups[3].Value;
                string uniform = match.Groups[4].Value;
                return $".rgb *={expr1}{vColor}.rgb{expr2}{uniform}.rgb";
            });

        // 修复7: 特殊处理 *= 数字 * v_Color * xxx.rgb 的完整模式
        code = Regex.Replace(code,
            @"\*=\s*(\d+\.0)\s*\*\s*(v_Color|v_VertexColor)(?!\.)\s*\*\s*(\w+)\.rgb",
            "*= $1 * $2.rgb * $3.rgb");

        // ============================================
        // ⭐ 系统性类型修复：处理所有向量维度不匹配问题
        // ============================================
        code = ApplySystematicTypeFixes(code);

        // 记录内置模式的转换时间
        if (!useMappingTableMode)
        {
            builtInConversionTime = conversionTimer.ElapsedMilliseconds;
        }

        return code;
    }

    /// <summary>
    /// 根据GLSL类型返回默认值
    /// </summary>
    private static string GetDefaultValueForType(string glslType)
    {
        switch (glslType)
        {
            case "float": return "0.0";
            case "vec2": return "vec2(0.0, 0.0)";
            case "vec3": return "vec3(0.0, 0.0, 0.0)";
            case "vec4": return "vec4(0.0, 0.0, 0.0, 0.0)";
            case "int": return "0";
            case "mat2": return "mat2(1.0)";
            case "mat3": return "mat3(1.0)";
            case "mat4": return "mat4(1.0)";
            default: return "0.0";
        }
    }

    /// <summary>
    /// 系统性类型修复：自动检测并修复所有向量维度不匹配问题
    /// </summary>
    private static string ApplySystematicTypeFixes(string code)
    {

        // ============================================
        // 阶段0: 移除/替换Unity平台特定代码
        // ============================================

        // 移除Unity条件编译块 - 智能保留else分支内容
        // 处理 #if UNITY_XXX ... #else ... #endif - 保留else分支
        code = Regex.Replace(code,
            @"#\s*if\s+UNITY_[^\n]+\s*\n(.*?)#\s*else\s*\n(.*?)#\s*endif",
            match => match.Groups[2].Value,  // 保留else分支的内容
            RegexOptions.Singleline);

        // 处理 #ifdef UNITY_XXX ... #else ... #endif - 保留else分支
        code = Regex.Replace(code,
            @"#\s*ifdef\s+UNITY_[^\n]+\s*\n(.*?)#\s*else\s*\n(.*?)#\s*endif",
            match => match.Groups[2].Value,  // 保留else分支的内容
            RegexOptions.Singleline);

        // 处理 #ifndef UNITY_XXX ... #else ... #endif - 保留if分支（因为UNITY_XXX不存在）
        code = Regex.Replace(code,
            @"#\s*ifndef\s+UNITY_[^\n]+\s*\n(.*?)#\s*else\s*\n(.*?)#\s*endif",
            match => match.Groups[1].Value,  // 保留if分支的内容
            RegexOptions.Singleline);

        // 处理没有else的单分支条件编译 - 完全移除
        // #if UNITY_XXX ... #endif (without else)
        code = Regex.Replace(code,
            @"#\s*if\s+UNITY_[^\n]+\s*\n.*?#\s*endif",
            "",
            RegexOptions.Singleline);

        // #ifdef UNITY_XXX ... #endif (without else)
        code = Regex.Replace(code,
            @"#\s*ifdef\s+UNITY_[^\n]+\s*\n.*?#\s*endif",
            "",
            RegexOptions.Singleline);

        // #ifndef UNITY_XXX ... #endif (without else) - 保留内容（因为UNITY_XXX不存在，条件为真）
        code = Regex.Replace(code,
            @"#\s*ifndef\s+UNITY_[^\n]+\s*\n(.*?)#\s*endif",
            match => match.Groups[1].Value,  // 保留内容
            RegexOptions.Singleline);

        // 移除带defined的复杂条件编译
        // #if defined(UNITY_XXX) ... #else ... #endif - 保留else分支
        code = Regex.Replace(code,
            @"#\s*if\s+defined\s*\(\s*UNITY_[^\)]+\).*?\n(.*?)#\s*else\s*\n(.*?)#\s*endif",
            match => match.Groups[2].Value,
            RegexOptions.Singleline);

        // #if !defined(UNITY_XXX) ... #else ... #endif - 保留if分支
        code = Regex.Replace(code,
            @"#\s*if\s+!defined\s*\(\s*UNITY_[^\)]+\).*?\n(.*?)#\s*else\s*\n(.*?)#\s*endif",
            match => match.Groups[1].Value,
            RegexOptions.Singleline);

        // 单分支defined
        code = Regex.Replace(code,
            @"#\s*if\s+defined\s*\(\s*UNITY_[^\)]+\).*?\n.*?#\s*endif",
            "",
            RegexOptions.Singleline);

        code = Regex.Replace(code,
            @"#\s*if\s+!defined\s*\(\s*UNITY_[^\)]+\).*?\n(.*?)#\s*endif",
            match => match.Groups[1].Value,
            RegexOptions.Singleline);

        // 移除Unity宏调用（UNITY_开头的函数调用）
        // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        code = Regex.Replace(code,
            @"UNITY_[A-Z_0-9]+\s*\([^)]*\)\s*;?\s*\n?",
            "");

        // 替换Unity屏幕纹理采样宏 - 保留为texture2D调用
        // UNITY_SAMPLE_SCREENSPACE_TEXTURE(tex, uv) -> texture2D(tex, uv)
        code = Regex.Replace(code,
            @"UNITY_SAMPLE_SCREENSPACE_TEXTURE\s*\(\s*([^,]+)\s*,\s*([^)]+)\)",
            "texture2D($1, $2)");

        // 替换Unity纹理采样宏（其他变体）
        code = Regex.Replace(code,
            @"UNITY_SAMPLE_TEX2D\s*\(\s*([^,]+)\s*,\s*([^)]+)\)",
            "texture2D($1, $2)");

        code = Regex.Replace(code,
            @"UNITY_SAMPLE_TEX2D_SAMPLER\s*\(\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^)]+)\)",
            "texture2D($1, $3)");

        // 移除Unity相关的空语句和多余空行
        code = Regex.Replace(code, @"\n\s*\n\s*\n+", "\n\n");

        // ============================================
        // 阶段0.5: 修复错误的变量声明链和类型不匹配
        // ============================================

        // 检测并修复类型不匹配的连续赋值
        // 例如: vec4 a = vec4 b = vec2 c = vec2(1, 2);
        // 这种情况通常是由于Unity宏替换或者换行符丢失导致的

        // 多遍处理，因为可能有多层嵌套
        for (int pass = 0; pass < 3; pass++)
        {
            // 模式1: 检测 "类型1 变量1 = 类型2 变量2 =" 的模式
            // 这会匹配链式赋值的前面部分，拆分成独立语句
            code = Regex.Replace(code,
                @"((?:vec[234]|float|int|mat[234])\s+\w+)\s*=\s*((?:vec[234]|float|int|mat[234])\s+\w+)\s*=",
                match => {
                    string decl1 = match.Groups[1].Value;  // vec4 a
                    string decl2 = match.Groups[2].Value;  // vec4 b
                    // 拆分成两个独立声明，第一个先初始化为默认值
                    string type1 = Regex.Match(decl1, @"(vec[234]|float|int|mat[234])").Value;
                    string defaultVal = GetDefaultValueForType(type1);
                    return $"{decl1} = {defaultVal};\n        {decl2} =";
                });

            // 模式2: 检测 "类型1 变量1 = 类型2 变量2 = 表达式;" 且类型不同
            code = Regex.Replace(code,
                @"(vec[234]|float|int|mat[234])\s+(\w+)\s*=\s*(vec[234]|float|int|mat[234])\s+(\w+)\s*=\s*([^;]+);",
                match => {
                    string type1 = match.Groups[1].Value;
                    string var1 = match.Groups[2].Value;
                    string type2 = match.Groups[3].Value;
                    string var2 = match.Groups[4].Value;
                    string expr = match.Groups[5].Value;

                    // 如果类型不同，拆分声明
                    if (type1 != type2)
                    {
                        string defaultVal1 = GetDefaultValueForType(type1);
                        return $"{type1} {var1} = {defaultVal1};\n        {type2} {var2} = {expr};";
                    }
                    // 类型相同，保持原样（这是合法的链式赋值）
                    return match.Value;
                });
        }

        // 模式3: 修复语句合并导致的缺少换行问题
        // vec4 screenColor01 = ...; vec4 screenColor02 = ...（缺少换行）
        code = Regex.Replace(code,
            @";\s*((?:vec[234]|float|int|mat[234])\s+\w+\s*=)",
            ";\n        $1");

        // 模式4: 清理可能产生的多余空行
        code = Regex.Replace(code, @"\n\s*\n\s*\n+", "\n\n");

        // 模式4.5: 移除未使用的varying声明（避免编译错误）
        // 检测varying声明但在代码中从未使用的变量
        var varyingMatches = Regex.Matches(code, @"varying\s+(vec[234]|float|int|mat[234])\s+(\w+)\s*;");
        foreach (Match match in varyingMatches)
        {
            string varyingName = match.Groups[2].Value;
            // 检查这个varying是否在代码中被使用（赋值或读取）
            string usagePattern = $@"\b{varyingName}\s*[=\.]";
            if (!Regex.IsMatch(code, usagePattern))
            {
                // 未使用，移除声明
                code = code.Replace(match.Value, $"// {match.Value} (unused)");
            }
        }

        // 模式5: 修复不完整的赋值语句（变量声明后面只有 = )
        // vec4 x = ); -> 删除整行
        code = Regex.Replace(code,
            @"(?:vec[234]|float|int|mat[234])\s+\w+\s*=\s*\)\s*;",
            "// removed incomplete assignment");

        // 模式6: 修复vec类型之间的不匹配赋值
        // vec4 x = expr.xy; -> vec4 x = vec4(expr.xy, 0.0, 0.0);
        // vec2 x = vec4(...); -> vec2 x = vec4(...).xy;
        // vec4 x = vec2(...); -> vec4 x = vec4(vec2(...), 0.0, 0.0);

        // 修复6a: vec4 = xxx.xy（vec2赋值给vec4）
        code = Regex.Replace(code,
            @"(vec4)\s+(\w+)\s*=\s*([^;]+\.xy)(?!\s*[,\)])\s*;",
            match => {
                string type = match.Groups[1].Value;
                string varName = match.Groups[2].Value;
                string expr = match.Groups[3].Value;
                // 如果表达式很简单（单个变量.xy），可能原意是使用整个vec4
                if (Regex.IsMatch(expr, @"^\w+\.xy$"))
                {
                    string baseVar = expr.Substring(0, expr.Length - 3);
                    return $"{type} {varName} = {baseVar};";
                }
                return $"{type} {varName} = vec4({expr}, 0.0, 0.0);";
            });

        // 修复6b: vec2 = vec4(...)（vec4赋值给vec2）
        code = Regex.Replace(code,
            @"(vec2)\s+(\w+)\s*=\s*(vec4\([^)]+\))\s*;",
            match => {
                string type = match.Groups[1].Value;
                string varName = match.Groups[2].Value;
                string expr = match.Groups[3].Value;
                return $"{type} {varName} = {expr}.xy;";
            });

        // 修复6c: vec4 = vec2(...)（vec2赋值给vec4）
        code = Regex.Replace(code,
            @"(vec4)\s+(\w+)\s*=\s*(vec2\([^)]+\))\s*;",
            match => {
                string type = match.Groups[1].Value;
                string varName = match.Groups[2].Value;
                string expr = match.Groups[3].Value;
                return $"{type} {varName} = vec4({expr}, 0.0, 0.0);";
            });

        // ============================================
        // 阶段1: 修复vec2运算中的vec4变量（自动添加.xy）
        // ============================================

        // 修复1a: vec2(...) + v_Texcoord0  -> vec2(...) + v_Texcoord0.xy
        code = Regex.Replace(code,
            @"(vec2\s*\([^)]+\))\s*([\+\-])\s*v_Texcoord0(?!\.)",
            "$1 $2 v_Texcoord0.xy");

        // 修复1b: v_Texcoord0 + vec2(...)  -> v_Texcoord0.xy + vec2(...)
        code = Regex.Replace(code,
            @"\bv_Texcoord0(?!\.)\s*([\+\-])\s*(vec2\s*\()",
            "v_Texcoord0.xy $1 $2");

        // 修复1c: texture2D参数中的vec4变量（强制修复，多次应用）
        // 第一遍：直接在texture2D第二个参数开头的v_Texcoord0
        code = Regex.Replace(code,
            @"texture2D\s*\(\s*(\w+)\s*,\s*v_Texcoord0(?!\.)",
            "texture2D($1, v_Texcoord0.xy");

        // 第二遍：texture2D内部任何位置的v_Texcoord0
        // 匹配整个texture2D调用，修复其中的v_Texcoord0
        code = Regex.Replace(code,
            @"texture2D\s*\([^)]+\)",
            match => {
                string texCall = match.Value;
                // 在这个texture2D调用内部，为所有v_Texcoord0添加.xy
                texCall = Regex.Replace(texCall, @"\bv_Texcoord0(?!\.)", "v_Texcoord0.xy");
                return texCall;
            });

        // 修复1d: vec2()构造函数的参数中出现vec4
        // vec2(expr + v_Texcoord0) -> vec2(expr + v_Texcoord0.xy)
        code = Regex.Replace(code,
            @"vec2\s*\(([^)]*)\bv_Texcoord0(?!\.)\b([^)]*)\)",
            match => {
                string content = match.Groups[1].Value + "v_Texcoord0.xy" + match.Groups[2].Value;
                return $"vec2({content})";
            });

        // ============================================
        // 阶段2: 修复vec4 uniform在vec2上下文中的使用
        // ============================================

        // 修复2a: vec2运算中的vec4 uniform（如u_UVScroll, u_TilingOffset等）
        string[] vec4Uniforms = { "u_UVScroll", "u_TilingOffset", "u_Scroll", "u_distort_tex_ST", "u_MainTex_ST" };
        foreach (var uniformName in vec4Uniforms)
        {
            // 在vec2构造函数、texture2D、或vec2运算中，自动添加.xy
            code = Regex.Replace(code,
                $@"\b{uniformName}(?!\.)([\s\)\+\-\*,;])",
                match => {
                    string suffix = match.Groups[1].Value;
                    // 检查上下文，如果在需要vec2的地方，添加.xy
                    return $"{uniformName}.xy{suffix}";
                });
        }

        // ============================================
        // 阶段3: 通用整数字面量修复（整数->浮点数）
        // ============================================

        // ⭐ 核心原则：只转换数字字面量，不触碰标识符中的数字和浮点数
        // 数字字面量定义：
        //   - 前后都不是字母、数字、点号、下划线的数字序列
        //   - 覆盖所有运算符：算术、比较、三元、逻辑等
        //   - 排除数组索引（方括号内）
        //   - 排除科学记数法中的指数部分（1e-5不转换成1e-5.0）

        // 多遍扫描，确保完全转换
        for (int pass = 0; pass < 2; pass++)
        {
            // 模式1a: 加减号（排除科学记数法） + 空格 + 整数
            code = Regex.Replace(code, @"(?<![eE])([\+\-]\s+)(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");

            // 模式1b: 其他运算符 + 空格 + 整数
            code = Regex.Replace(code, @"([\*\/%=<>!?:&|\(,]\s+)(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");

            // 模式2a: 加减号（排除科学记数法） + 整数（无空格）
            code = Regex.Replace(code, @"(?<![eE])([\+\-])(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");

            // 模式2b: 其他运算符 + 整数（无空格）
            code = Regex.Replace(code, @"([\*\/%=<>!?:&|\(,])(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");

            // 模式3: 整数 + 空格 + 运算符/分隔符
            code = Regex.Replace(code, @"(?<![0-9.a-zA-Z_])(\d+)(\s+[\+\-\*\/%<>!?:&|\),;])", "$1.0$2");

            // 模式4: 整数 + 运算符/分隔符（无空格）
            code = Regex.Replace(code, @"(?<![0-9.a-zA-Z_])(\d+)([\+\-\*\/%<>!?:&|\),;])", "$1.0$2");
        }

        // 向量/矩阵构造函数中的整数
        code = Regex.Replace(code,
            @"(vec[234]|mat[234])\s*\(([^)]+)\)",
            match => {
                string vecType = match.Groups[1].Value;
                string args = match.Groups[2].Value;
                // 转换参数列表中的整数字面量（排除科学记数法）
                args = Regex.Replace(args, @"(?<![eE])([,\(\+\-]\s*)(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");
                args = Regex.Replace(args, @"([,\(\*\/%<>!?:&|]\s*)(\d+)(?![0-9.a-zA-Z_])", "$1$2.0");
                args = Regex.Replace(args, @"(?<![0-9.a-zA-Z_])(\d+)(\s*[,\)\+\-\*\/%<>!?:&|])", "$1.0$2");
                return $"{vecType}({args})";
            });

        // ============================================
        // 阶段4: 智能修复vec3/vec4变量的分量访问
        // ============================================

        // 已知的vec4变量列表
        string[] vec4Vars = { "v_Color", "v_VertexColor", "u_TintColor", "u_AlbedoColor", "u_BaseColor", "u_Color" };

        // 修复4a: .rgb 上下文中的vec4变量
        // 匹配任何包含 .rgb 的赋值或运算语句
        code = Regex.Replace(code,
            @"((?:^|\s)(?:\w+\.)?rgb\s*[\*\+\-\/]?=\s*)([^;]+);",
            match => {
                string prefix = match.Groups[1].Value;
                string expr = match.Groups[2].Value;

                // 为所有vec4变量添加.rgb
                foreach (var vec4Var in vec4Vars)
                {
                    // 只在没有分量访问的情况下添加.rgb
                    expr = Regex.Replace(expr, $@"\b{vec4Var}(?!\.)", $"{vec4Var}.rgb");
                }

                // 清理重复
                expr = Regex.Replace(expr, @"\.rgb\.rgb", ".rgb");

                return $"{prefix}{expr};";
            }, RegexOptions.Multiline);

        // 修复4b: .rgb = 表达式中的vec4变量需要.rgb
        code = Regex.Replace(code,
            @"(\.rgb\s*=\s*)([^;]+);",
            match => {
                string prefix = match.Groups[1].Value;
                string expr = match.Groups[2].Value;

                foreach (var vec4Var in vec4Vars)
                {
                    expr = Regex.Replace(expr, $@"\b{vec4Var}(?!\.)", $"{vec4Var}.rgb");
                }

                expr = Regex.Replace(expr, @"\.rgb\.rgb", ".rgb");
                return $"{prefix}{expr};";
            });

        // 修复4c: 表达式中vec4与xxx.rgb运算的情况
        // 只要看到 xxx.rgb，那么同一运算中的vec4变量也需要.rgb
        foreach (var vec4Var in vec4Vars)
        {
            // vec4 * xxx.rgb -> vec4.rgb * xxx.rgb
            code = Regex.Replace(code,
                $@"\b{vec4Var}(?!\.)\s*\*\s*(\w+)\.rgb",
                $"{vec4Var}.rgb * $1.rgb");

            // xxx.rgb * vec4 -> xxx.rgb * vec4.rgb
            code = Regex.Replace(code,
                $@"(\w+)\.rgb\s*\*\s*{vec4Var}(?!\.)",
                $"$1.rgb * {vec4Var}.rgb");
        }

        // ============================================
        // 阶段4.5: 修复.a分量运算中的vec4变量
        // ============================================

        // 修复4.5a: .a *= 表达式中的v_Color需要.a
        code = Regex.Replace(code,
            @"(\.a\s*[\*\+\-]?=\s*)([^;]+);",
            match => {
                string prefix = match.Groups[1].Value;
                string expr = match.Groups[2].Value;

                // 在表达式中为v_Color添加.a（如果后面不是.rgb也不是.a）
                expr = Regex.Replace(expr, @"\bv_Color(?!\.(?:rgb|a))\b", "v_Color.a");
                expr = Regex.Replace(expr, @"\bv_VertexColor(?!\.(?:rgb|a))\b", "v_VertexColor.a");

                // 修复可能的重复.a.a
                expr = Regex.Replace(expr, @"v_Color\.a\.a", "v_Color.a");
                expr = Regex.Replace(expr, @"v_VertexColor\.a\.a", "v_VertexColor.a");

                return $"{prefix}{expr};";
            });

        // 修复4.5b: xxx.a * v_Color -> xxx.a * v_Color.a
        code = Regex.Replace(code,
            @"(\w+)\.a\s*\*\s*v_Color(?!\.)",
            "$1.a * v_Color.a");

        // 修复4.5c: v_Color * xxx.a -> v_Color.a * xxx.a
        code = Regex.Replace(code,
            @"\bv_Color(?!\.)\s*\*\s*(\w+)\.a\b",
            "v_Color.a * $1.a");

        // ============================================
        // 阶段4.6: 通用vec4 Color类型uniform的分量修复
        // ============================================

        // 已知的vec4/Color类型变量
        string[] vec4ColorVars = { "v_Color", "v_VertexColor", "u_TintColor", "u_AlbedoColor", "u_BaseColor", "u_Color" };

        foreach (var colorVar in vec4ColorVars)
        {
            // 在.rgb上下文中自动添加.rgb
            code = Regex.Replace(code,
                $@"\b{colorVar}(?!\.)\s*\*\s*(\w+)\.rgb",
                $"{colorVar}.rgb * $1.rgb");

            code = Regex.Replace(code,
                $@"(\w+)\.rgb\s*\*\s*{colorVar}(?!\.)",
                $"$1.rgb * {colorVar}.rgb");

            // 在.a上下文中自动添加.a
            code = Regex.Replace(code,
                $@"\b{colorVar}(?!\.(?:rgb|a))\s*\*\s*(\w+)\.a",
                $"{colorVar}.a * $1.a");

            code = Regex.Replace(code,
                $@"(\w+)\.a\s*\*\s*{colorVar}(?!\.(?:rgb|a))",
                $"$1.a * {colorVar}.a");
        }

        // ============================================
        // 阶段5: 修复vec2()构造函数中的问题
        // ============================================

        // 修复5a: vec2(vec4的分量运算)
        // 查找所有vec2(...)中的内容，检查是否有未处理的vec4变量
        code = Regex.Replace(code,
            @"vec2\s*\(\s*\(\s*([^)]+)\s*\)\s*\)",
            match => {
                string inner = match.Groups[1].Value;
                // 如果inner包含_ST但没有.xy/.zw，添加.xy
                if (inner.Contains("_ST") && !inner.Contains(".xy") && !inner.Contains(".zw"))
                {
                    inner = Regex.Replace(inner, @"\b(\w+_ST)(?!\.)", "$1.xy");
                }
                return $"vec2(({inner}))";
            });

        // ============================================
        // 阶段6: 强制修复特定的已知vec4变量在vec2上下文中的使用
        // ============================================

        // ⭐ 修复6: 智能处理v_Texcoord0的分量访问
        // 关键：根据赋值目标类型决定是否需要.xy

        // 修复6a: vec2 = v_Texcoord0 -> vec2 = v_Texcoord0.xy
        code = Regex.Replace(code,
            @"(vec2\s+\w+\s*=\s*)v_Texcoord0(?!\.)",
            "$1v_Texcoord0.xy");

        // 修复6b: 已有vec2变量赋值 = v_Texcoord0 -> = v_Texcoord0.xy
        code = Regex.Replace(code,
            @"(?<=\s)(\w+\.(?:xy|rg))\s*=\s*v_Texcoord0(?!\.)",
            "$1 = v_Texcoord0.xy");

        // 修复6c: vec4 = v_Texcoord0.xy -> vec4 = v_Texcoord0 (移除不必要的.xy)
        code = Regex.Replace(code,
            @"(vec4\s+\w+\s*=\s*)v_Texcoord0\.xy(?!\s*[,\+\-\*\/])",
            "$1v_Texcoord0");

        // 修复6d: texture2D中的v_Texcoord0如果没有分量，添加.xy
        // 已经在阶段1中处理过了，这里只清理重复

        // 修复6e: 移除可能产生的重复.xy或错误的分量组合
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.xy", "v_Texcoord0.xy");
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.zw", "v_Texcoord0.xy");
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.x(?!y)", "v_Texcoord0.x");
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.y(?!x)", "v_Texcoord0.y");
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.r", "v_Texcoord0.x");
        code = Regex.Replace(code, @"v_Texcoord0\.xy\.g", "v_Texcoord0.y");

        // ============================================
        // 阶段7: 最后清理：确保所有_ST uniforms在需要vec2的地方有.xy
        // ============================================

        // 修复7a: 在texture2D的第二个参数中，_ST应该有.xy或.zw
        code = Regex.Replace(code,
            @"texture2D\s*\([^,]+,\s*([^)]*)\)",
            match => {
                string args = match.Value;
                // 在纹理坐标参数中，为所有_ST添加.xy（如果没有）
                args = Regex.Replace(args, @"\b(\w+_ST)(?!\.(?:xy|zw))\b", "$1.xy");
                return args;
            });

        // 修复7b: vec2构造函数中的_ST
        code = Regex.Replace(code,
            @"vec2\s*\([^)]*\)",
            match => {
                string vecExpr = match.Value;
                // 在vec2(...)中为_ST添加.xy
                vecExpr = Regex.Replace(vecExpr, @"\b(\w+_ST)(?!\.(?:xy|zw))\b", "$1.xy");
                return vecExpr;
            });

        // ============================================
        // 阶段7.5: 修复texture()函数调用（GLSL ES 2.0不支持texture()）
        // ============================================

        // GLSL ES 2.0只支持texture2D(), texture2DProj(), textureCube()等
        // texture()是GLSL ES 3.0的函数，需要替换为texture2D()
        code = Regex.Replace(code,
            @"\btexture\s*\(",
            "texture2D(");

        // ============================================
        // 阶段8: 修复加法/减法运算中的向量类型不匹配
        // ============================================

        // 修复8a: vec2 + vec4 -> vec2 + vec4.xy
        // 修复8b: vec3 + vec4 -> vec3 + vec4.rgb
        // 修复8c: vec4 + vec2 -> vec4 + vec4(vec2, 0.0, 0.0)

        // 检测所有加减法运算，分析左右操作数的类型
        // 模式：(类型 变量 = 表达式 +/- 表达式)

        // 修复8a: 在加减法中，如果发现vec4变量与vec2类型运算，自动添加.xy
        code = Regex.Replace(code,
            @"(vec2\s*\([^)]+\))\s*([\+\-])\s*\bv_Texcoord0(?!\.)",
            "$1 $2 v_Texcoord0.xy");

        code = Regex.Replace(code,
            @"\bv_Texcoord0(?!\.)\s*([\+\-])\s*(vec2\s*\()",
            "v_Texcoord0.xy $1 $2");

        // 修复8b: vec4 uniforms与vec2类型运算
        foreach (var uniformName in vec4Uniforms)
        {
            // vec2(...) + u_Uniform -> vec2(...) + u_Uniform.xy
            code = Regex.Replace(code,
                $@"(vec2\s*\([^)]+\))\s*([\+\-])\s*\b{uniformName}(?!\.)",
                $"$1 $2 {uniformName}.xy");

            // u_Uniform + vec2(...) -> u_Uniform.xy + vec2(...)
            code = Regex.Replace(code,
                $@"\b{uniformName}(?!\.)\s*([\+\-])\s*(vec2\s*\()",
                $"{uniformName}.xy $1 $2");
        }

        // 修复8c: 在赋值语句中检测类型不匹配的加法
        // vec2 result = expr + v_Texcoord0 -> vec2 result = expr + v_Texcoord0.xy
        code = Regex.Replace(code,
            @"(vec2\s+\w+\s*=\s*[^;]*)([\+\-])\s*v_Texcoord0(?!\.)",
            match => {
                string prefix = match.Groups[1].Value;
                string op = match.Groups[2].Value;
                // 检查prefix中是否已经有v_Texcoord0.xy
                if (prefix.Contains("v_Texcoord0.xy"))
                    return match.Value;
                return $"{prefix}{op} v_Texcoord0.xy";
            });

        return code;
    }

    /// <summary>
    /// 将含有 vec3 <= scalar 分量比较的 Gamma22ToLinear 函数体转换为合法GLSL
    /// HLSL允许 float3 <= float（分量级比较），GLSL只允许标量比较，需改用step()
    /// </summary>
    private static string ConvertGamma22ToLinear(string funcCode)
    {
        // 提取函数签名（保留函数名和参数名），替换整个函数体
        var sigMatch = Regex.Match(funcCode, @"(vec3\s+\w+)\s*\(\s*vec3\s+(\w+)\s*\)");
        if (!sigMatch.Success)
        {
            // 尝试匹配 half3/float3 版本
            sigMatch = Regex.Match(funcCode, @"((?:half3|float3)\s+\w+)\s*\(\s*(?:half3|float3)\s+(\w+)\s*\)");
        }
        if (!sigMatch.Success) return funcCode;

        // 强制使用 vec3 作为返回类型（half3/float3 版本也统一输出为 vec3）
        string sig = Regex.Replace(sigMatch.Groups[1].Value, @"\b(?:half3|float3)\b", "vec3");
        string param = sigMatch.Groups[2].Value; // e.g. "color"

        // 确认函数体包含 <= 0.04045 模式（sRGB线性化）
        if (!funcCode.Contains("<= 0.04045") && !funcCode.Contains("<=0.04045"))
            return funcCode;

        // 用step()实现分量级条件选择，等价于原始HLSL逻辑：
        // color <= 0.04045 ? color / 12.92 : pow((color + 0.055) / 1.055, 2.4)
        return $@"{sig}(vec3 {param})
    {{
        return mix(pow(({param} + vec3(0.055)) / vec3(1.055), vec3(2.4)), {param} / vec3(12.92), step({param}, vec3(0.04045)));
    }}";
    }

    /// <summary>
    /// 转换GlossyEnvironmentReflection函数
    /// </summary>
    private static string ConvertGlossyEnvironmentReflection(string code)
    {
        return ConvertFunctionWithBalancedParens(code, "GlossyEnvironmentReflection", (args) =>
        {
            var parts = SplitFunctionArgs(args, 3);
            if (parts.Count >= 2)
            {
                // 简化为反射采样
                return $"vec3(0.0)"; // 需要反射探针支持，暂时返回0
            }
            return "vec3(0.0)";
        });
    }

    /// <summary>
    /// 转换sincos函数 - 使用平衡括号匹配
    /// sincos(angle, out s, out c) -> s = sin(angle); c = cos(angle);
    /// </summary>
    private static string ConvertSinCosFunction(string code)
    {
        // 匹配 sincos(expr, var1, var2);
        var pattern = @"sincos\s*\(";
        int startIdx = 0;
        StringBuilder result = new StringBuilder();
        
        while (startIdx < code.Length)
        {
            var match = Regex.Match(code.Substring(startIdx), pattern);
            if (!match.Success)
            {
                result.Append(code.Substring(startIdx));
                break;
            }
            
            int funcStart = startIdx + match.Index;
            result.Append(code.Substring(startIdx, match.Index));
            
            // 找到左括号位置
            int parenStart = funcStart + match.Length - 1;
            int parenCount = 1;
            int i = parenStart + 1;
            
            while (i < code.Length && parenCount > 0)
            {
                if (code[i] == '(') parenCount++;
                else if (code[i] == ')') parenCount--;
                i++;
            }
            
            if (parenCount == 0)
            {
                string args = code.Substring(parenStart + 1, i - parenStart - 2);
                var parts = SplitFunctionArgs(args, 3);
                
                if (parts.Count >= 3)
                {
                    string angle = parts[0].Trim();
                    string sinVar = parts[1].Trim();
                    string cosVar = parts[2].Trim();
                    result.Append($"{sinVar} = sin({angle}); {cosVar} = cos({angle});");
                }
                else
                {
                    result.Append($"/* sincos conversion failed: {args} */");
                }
                
                // 跳过原来的分号（已经在转换中添加了）
                if (i < code.Length && code[i] == ';')
                    i++;
                    
                startIdx = i;
            }
            else
            {
                result.Append(code.Substring(funcStart, i - funcStart));
                startIdx = i;
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// 转换类型转换语法 (type)expr -> type(expr)
    /// 例如: (mat3)matrix -> mat3(matrix)
    /// </summary>
    private static string ConvertTypeCastSyntax(string code)
    {
        // 匹配 (mat4), (mat3), (mat2), (vec4), (vec3), (vec2), (float), (int) 类型转换
        string[] types = { "mat4", "mat3", "mat2", "vec4", "vec3", "vec2", "float", "int", "bool" };
        
        foreach (var type in types)
        {
            // 匹配 (type)identifier 或 (type)(expr)
            // (mat3)someMatrix -> mat3(someMatrix)
            code = Regex.Replace(code, $@"\(\s*{type}\s*\)\s*(\w+)", $"{type}($1)");
            // (mat3)(expr) -> mat3(expr) - 这种情况括号已经存在
            code = Regex.Replace(code, $@"\(\s*{type}\s*\)\s*\(", $"{type}(");
        }
        
        return code;
    }

    /// <summary>
    /// 转换纹理采样函数（使用平衡括号）
    /// </summary>
    private static string ConvertTextureSampling(string code)
    {
        // tex2D
        code = ConvertFunctionWithBalancedParens(code, "tex2D", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
            {
                string texName = ConvertTextureName(parts[0].Trim());
                return $"texture2D({texName}, {parts[1]})";
            }
            return $"texture2D({args})";
        });
        
        // tex2Dlod
        code = ConvertFunctionWithBalancedParens(code, "tex2Dlod", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
            {
                string texName = ConvertTextureName(parts[0].Trim());
                return $"texture2D({texName}, {parts[1]}.xy)";
            }
            return $"texture2D({args})";
        });
        
        // texCUBE
        code = ConvertFunctionWithBalancedParens(code, "texCUBE", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
            {
                string texName = ConvertTextureName(parts[0].Trim());
                return $"textureCube({texName}, {parts[1]})";
            }
            return $"textureCube({args})";
        });
        
        // texCUBElod
        code = ConvertFunctionWithBalancedParens(code, "texCUBElod", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
            {
                string texName = ConvertTextureName(parts[0].Trim());
                return $"textureCube({texName}, {parts[1]}.xyz)";
            }
            return $"textureCube({args})";
        });
        
        return code;
    }

    /// <summary>
    /// 转换纹理名称
    /// </summary>
    private static string ConvertTextureName(string texName)
    {
        // 先转换 _XXX -> u_XXX
        if (texName.StartsWith("_"))
            texName = "u_" + texName.Substring(1);
        
        // 特殊纹理名映射
        switch (texName)
        {
            case "u_MainTex": return "u_AlbedoTexture";
            case "u_BaseMap": return "u_AlbedoTexture";
            case "u_BumpMap": return "u_NormalTexture";
            case "u_NormalMap": return "u_NormalTexture";
            case "u_EmissionMap": return "u_EmissionTexture";
            default: return texName;
        }
    }

    /// <summary>
    /// 转换saturate函数
    /// </summary>
    private static string ConvertSaturate(string code)
    {
        return ConvertFunctionWithBalancedParens(code, "saturate", (args) =>
        {
            return $"clamp({args}, 0.0, 1.0)";
        });
    }

    /// <summary>
    /// 转换mul函数
    /// </summary>
    private static string ConvertMulFunction(string code)
    {
        return ConvertFunctionWithBalancedParens(code, "mul", (args) =>
        {
            var parts = SplitFunctionArgs(args, 2);
            if (parts.Count >= 2)
                return $"({parts[0]} * {parts[1]})";
            return args;
        });
    }

    /// <summary>
    /// 转换clip函数
    /// </summary>
    private static string ConvertClipFunction(string code)
    {
        return ConvertFunctionWithBalancedParens(code, "clip", (args) =>
        {
            return $"if (({args}) < 0.0) {{ discard; }}";
        });
    }

    /// <summary>
    /// 使用平衡括号转换函数调用
    /// </summary>
    private static string ConvertFunctionWithBalancedParens(string code, string funcName, Func<string, string> converter)
    {
        StringBuilder result = new StringBuilder();
        int i = 0;
        
        while (i < code.Length)
        {
            // 查找函数名
            int funcStart = code.IndexOf(funcName, i);
            if (funcStart < 0)
            {
                result.Append(code.Substring(i));
                break;
            }
            
            // 检查是否是完整的函数名（前面不是字母数字下划线）
            if (funcStart > 0 && (char.IsLetterOrDigit(code[funcStart - 1]) || code[funcStart - 1] == '_'))
            {
                result.Append(code.Substring(i, funcStart - i + funcName.Length));
                i = funcStart + funcName.Length;
                continue;
            }
            
            // 检查后面是否是左括号
            int afterFunc = funcStart + funcName.Length;
            while (afterFunc < code.Length && char.IsWhiteSpace(code[afterFunc]))
                afterFunc++;
            
            if (afterFunc >= code.Length || code[afterFunc] != '(')
            {
                result.Append(code.Substring(i, afterFunc - i));
                i = afterFunc;
                continue;
            }
            
            // 找到匹配的右括号
            int parenStart = afterFunc;
            int parenCount = 1;
            int j = parenStart + 1;
            
            while (j < code.Length && parenCount > 0)
            {
                if (code[j] == '(') parenCount++;
                else if (code[j] == ')') parenCount--;
                j++;
            }
            
            if (parenCount == 0)
            {
                // 提取参数
                string args = code.Substring(parenStart + 1, j - parenStart - 2);
                string converted = converter(args);
                
                result.Append(code.Substring(i, funcStart - i));
                result.Append(converted);
                i = j;
            }
            else
            {
                // 括号不匹配，保持原样
                result.Append(code.Substring(i, j - i));
                i = j;
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// 分割函数参数（考虑嵌套括号）
    /// </summary>
    private static List<string> SplitFunctionArgs(string args, int maxParts)
    {
        List<string> parts = new List<string>();
        StringBuilder current = new StringBuilder();
        int parenCount = 0;
        int bracketCount = 0;
        
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            
            if (c == '(' || c == '[') { parenCount++; current.Append(c); }
            else if (c == ')' || c == ']') { parenCount--; current.Append(c); }
            else if (c == ',' && parenCount == 0 && parts.Count < maxParts - 1)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        
        if (current.Length > 0)
            parts.Add(current.ToString().Trim());
        
        return parts;
    }

    /// <summary>
    /// 缩进代码
    /// </summary>
    private static string IndentCode(string code, string indent)
    {
        if (string.IsNullOrEmpty(code))
            return "";
        
        var lines = code.Split(new[] { '\n' }, StringSplitOptions.None);
        StringBuilder sb = new StringBuilder();
        
        foreach (var line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                sb.AppendLine(indent + trimmed.TrimStart());
            }
            else
            {
                sb.AppendLine();
            }
        }
        
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 验证生成的GLSL代码
    /// </summary>
    private static string ValidateAndCleanGLSL(string code)
    {
        // ========== 第一步：检查括号匹配 ==========
        int parenCount = 0;
        int braceCount = 0;
        int bracketCount = 0;
        bool inString = false;
        bool inComment = false;
        bool inLineComment = false;
        char prevChar = '\0';

        foreach (char c in code)
        {
            // 跳过字符串和注释中的括号
            if (c == '"' && prevChar != '\\')
            {
                inString = !inString;
            }
            else if (!inString && c == '/' && prevChar == '/')
            {
                inLineComment = true;
            }
            else if (!inString && c == '*' && prevChar == '/')
            {
                inComment = true;
            }
            else if (inComment && c == '/' && prevChar == '*')
            {
                inComment = false;
            }
            else if (inLineComment && c == '\n')
            {
                inLineComment = false;
            }
            else if (!inString && !inComment && !inLineComment)
            {
                switch (c)
                {
                    case '(': parenCount++; break;
                    case ')': parenCount--; break;
                    case '{': braceCount++; break;
                    case '}': braceCount--; break;
                    case '[': bracketCount++; break;
                    case ']': bracketCount--; break;
                }
            }

            prevChar = c;
        }

        // 报告括号问题
        if (parenCount != 0)
        {
            Debug.LogWarning($"LayaAir3D: GLSL code has unbalanced parentheses: {parenCount} (+ indicates extra '(', - indicates extra ')')");
        }
        if (braceCount != 0)
        {
            Debug.LogWarning($"LayaAir3D: GLSL code has unbalanced braces: {braceCount}");
        }
        if (bracketCount != 0)
        {
            Debug.LogWarning($"LayaAir3D: GLSL code has unbalanced brackets: {bracketCount}");
        }

        // ========== 第二步：检查常见的GLSL错误 ==========

        // 检查是否有Unity特有的函数未转换
        string[] unityFunctions = { "UnityObjectToClipPos", "UnityWorldToClipPos", "UNITY_MATRIX_", "tex2Dlod" };
        foreach (var func in unityFunctions)
        {
            if (code.Contains(func))
            {
                Debug.LogWarning($"LayaAir3D: Found Unity-specific function '{func}' in GLSL code - may need manual conversion");
            }
        }

        // 检查是否有HLSL类型未转换
        if (code.Contains("half") && !code.Contains("// half"))
        {
            Debug.LogWarning("LayaAir3D: Found HLSL 'half' type - should be converted to 'float' in GLSL");
            code = Regex.Replace(code, @"\bhalf\b", "float");
        }
        if (code.Contains("fixed") && !code.Contains("// fixed"))
        {
            Debug.LogWarning("LayaAir3D: Found HLSL 'fixed' type - should be converted to 'float' in GLSL");
            code = Regex.Replace(code, @"\bfixed\b", "float");
        }

        // 检查是否有错误的向量构造（如vec3(0.0)应该是vec3(0.0, 0.0, 0.0)）
        var singleArgVecs = Regex.Matches(code, @"\b(vec[234])\s*\(\s*([0-9.]+)\s*\)");
        if (singleArgVecs.Count > 0)
        {
            Debug.LogWarning($"LayaAir3D: Found {singleArgVecs.Count} single-argument vector constructor(s) - these are valid but may indicate hardcoded values");
        }

        // ========== 第三步：语法清理 ==========

        // ===== 修复texture2D参数类型错误 =====
        // texture2D的第二个参数应该是vec2，不应该被包装成vec4
        // 修复: texture2D(tex, vec4(...).xy) -> texture2D(tex, ...)
        code = Regex.Replace(code, @"texture2D\s*\(\s*([^,]+),\s*vec4\s*\(([^)]+)\)\s*\.xy\s*\)", "texture2D($1, $2)");

        // ===== 修复vec2 += vec4类型不匹配 =====
        // 修复: vec2变量 += texture2D(...) -> vec2变量 += texture2D(...).xy
        code = Regex.Replace(code, @"(\b\w+UV\b)\s*\+=\s*(texture2D\s*\([^)]+\))\s*\*", "$1 += $2.xy *");

        // ===== 修复错误的分号位置 =====
        // 修复多余的单独分号行（通常在逗号后）
        code = Regex.Replace(code, @",\s*\n\s*;\s*\n", ",\n");

        // 修复单独的分号行（if/else后面的孤立分号）
        code = Regex.Replace(code, @"(if|else|else\s+if)\s*\([^)]*\)\s*\n\s*;\s*\n", "$1($2)\n");

        // ===== 修复transpose(inverse(...))语法错误 - 通用方案 =====
        // 不限定特定矩阵变量名（如u_View），匹配所有这种模式

        // ⭐ STEP 1: 首先处理最复杂的模式 - 带有mat3包装的完整错误模式
        // 通用匹配: (mat3(transpose); (inverse(任何矩阵)) * xxx)
        code = Regex.Replace(code,
            @"\(\s*mat3\s*\(\s*transpose\s*\)\s*;\s*\(\s*inverse\s*\(\s*(\w+)\s*\)\s*\)\s*\*\s*([^)]+)\s*\)",
            "mat3(transpose(inverse($1))) * $2");

        // ⭐ STEP 2: 修复分号问题（通用）
        // 修复: transpose); (inverse -> transpose(inverse
        code = Regex.Replace(code, @"transpose\s*\)\s*;\s*\(\s*inverse", "transpose(inverse");

        // ⭐ STEP 3: 修复mat3嵌套调用的语法错误（通用）
        // 修复完整模式: (mat3(transpose(inverse(xxx)) * 应该是 mat3(transpose(inverse(xxx))) *
        code = Regex.Replace(code, @"\(\s*mat3\s*\(\s*transpose\s*\(\s*inverse\s*\(\s*([^)]+)\s*\)\s*\)\s*\*", "mat3(transpose(inverse($1))) *");

        // 修复没有前置括号的情况
        code = Regex.Replace(code, @"mat3\s*\(\s*transpose\s*\(\s*inverse\s*\(\s*([^)]+)\s*\)\s*\)\s*\*", "mat3(transpose(inverse($1))) *");

        // ⭐ STEP 4: 修复另一种模式: mat3(inverse(transpose(...))) 顺序错误（通用）
        code = Regex.Replace(code, @"mat3\s*\(\s*inverse\s*\(\s*transpose\s*\(\s*([^)]+)\s*\)\s*\)\s*\)", "mat3(transpose(inverse($1)))");

        // ⭐ STEP 5: 修复缺少mat3闭合括号的情况（通用）
        // 查找: mat3(transpose(inverse(xxx)) 后面直接跟 * 或 ) ，缺少一个闭合括号
        code = Regex.Replace(code, @"mat3\s*\(\s*transpose\s*\(\s*inverse\s*\(\s*([^)]+)\s*\)\s*\)\s*\)(?=\s*[*)])", "mat3(transpose(inverse($1)))");

        // ⭐ STEP 6: 修复遗留的复杂模式（通用）
        // 修复: (mat3(...); (...)) 带有分号和多余括号的情况
        code = Regex.Replace(code, @"\(\s*mat3\s*\([^;]+transpose[^;]+\)\s*;\s*\([^)]*inverse[^)]+\)\s*\*\s*(\w+)\s*\)\)",
            m => {
                // 提取inverse中的矩阵变量名
                var inverseMatch = Regex.Match(m.Value, @"inverse\s*\(\s*(\w+)\s*\)");
                // 提取最后的被乘变量名
                var varMatch = Regex.Match(m.Value, @"\*\s*(\w+)\s*\)");

                if (inverseMatch.Success && varMatch.Success)
                {
                    string matrixName = inverseMatch.Groups[1].Value;
                    string varName = varMatch.Groups[1].Value;
                    return $"mat3(transpose(inverse({matrixName}))) * {varName}";
                }

                return m.Value;
            });

        // ⭐ 修复mix函数的类型精确性
        // 当给vec3变量赋值时，mix的参数应该是vec3而不是标量
        // 模式: vec3 xxx = mix(0.0, 1.0, ...) 或 vec3 xxx = mix(0, 1, ...)
        code = Regex.Replace(code, @"(vec3\s+\w+\s*=\s*mix\s*\()\s*0\.0\s*,\s*1\.0\s*,", "$1vec3(0.0), vec3(1.0),");
        code = Regex.Replace(code, @"(vec3\s+\w+\s*=\s*mix\s*\()\s*0\s*,\s*1\s*,", "$1vec3(0.0), vec3(1.0),");

        // 处理已存在vec3变量的赋值
        // 收集所有vec3变量（排除函数定义，避免函数名被误识别为变量名）
        var vec3VarMatches = Regex.Matches(code, @"vec3\s+(\w+)(?!\s*\()");
        var vec3Vars = new HashSet<string>();
        foreach (Match m in vec3VarMatches)
        {
            vec3Vars.Add(m.Groups[1].Value);
        }

        // 修复这些vec3变量的mix赋值
        foreach (var varName in vec3Vars)
        {
            // 模式: vec3Var = mix(0.0, 1.0, ...)
            code = Regex.Replace(code, $@"({varName}\s*=\s*mix\s*\()\s*0\.0\s*,\s*1\.0\s*,", "$1vec3(0.0), vec3(1.0),");
            code = Regex.Replace(code, $@"({varName}\s*=\s*mix\s*\()\s*0\s*,\s*1\s*,", "$1vec3(0.0), vec3(1.0),");
        }

        // mat2旋转矩阵：不交换符号，保持原始参数 mat2(c,-s,s,c)
        // 只需修复下面的乘法顺序 (v*M) → (M*v) 即可得到正确结果

        // ⭐ 修复矩阵乘法顺序 - 完全通用方案，基于类型系统
        // GLSL中矩阵乘法: mat * vec，而不是 vec * mat
        // 通过类型推断识别矩阵变量，而不是依赖命名习惯

        // 步骤1: 收集所有矩阵类型变量（mat2, mat3, mat4）
        var matrixVarPattern = @"(mat[234])\s+(\w+)\s*=";
        var matrixVarMatches = Regex.Matches(code, matrixVarPattern);
        var matrixVars = new Dictionary<string, string>(); // 变量名 -> 类型
        foreach (Match m in matrixVarMatches)
        {
            string matType = m.Groups[1].Value;
            string varName = m.Groups[2].Value;
            if (!matrixVars.ContainsKey(varName))
            {
                matrixVars.Add(varName, matType);
            }
        }

        // 步骤2: 修复这些矩阵变量的乘法顺序
        foreach (var kvp in matrixVars)
        {
            string matVar = kvp.Key;
            // 匹配: (非矩阵变量 * 矩阵变量) -> (矩阵变量 * 非矩阵变量)
            // 确保第一个操作数不是矩阵类型
            code = Regex.Replace(code,
                $@"\((\w+)\s*\*\s*{Regex.Escape(matVar)}\)",
                m => {
                    string firstOperand = m.Groups[1].Value;
                    // 如果第一个操作数也是矩阵，不修改（矩阵与矩阵相乘顺序可能是正确的）
                    if (matrixVars.ContainsKey(firstOperand))
                    {
                        return m.Value;
                    }
                    // 否则，交换顺序
                    return $"({matVar} * {firstOperand})";
                });
        }

        // ⭐ 修复变量赋值后的条件修改顺序 - 完全通用方案
        // 检测模式：变量A = 变量B; 后跟 #ifdef ... 变量A *= 变量C; #endif
        // 这种模式应该改为：#ifdef ... 变量B *= 变量C; #endif 然后 变量A = 变量B;
        // 不依赖特定变量名，适用于所有这种模式

        // 通用模式：匹配 "变量A = 变量B;" 后面跟 "#ifdef ... 变量A *= 变量C; #endif"
        var postAssignModPattern = @"(\w+)\s*=\s*(\w+)\s*;\s*\n\s*(#ifdef\s+\w+\s*\n\s*(?:\/\/[^\n]*\n\s*)?\1\s*\*=\s*\w+\s*;\s*\n\s*#endif)";
        var postAssignMatches = Regex.Matches(code, postAssignModPattern, RegexOptions.Singleline);

        foreach (Match match in postAssignMatches)
        {
            string targetVar = match.Groups[1].Value;  // 被赋值的变量
            string sourceVar = match.Groups[2].Value;  // 源变量
            string conditionalBlock = match.Groups[3].Value;  // 条件修改块

            // 提取条件宏和修改变量
            var detailMatch = Regex.Match(conditionalBlock, $@"#ifdef\s+(\w+).*?{Regex.Escape(targetVar)}\s*\*=\s*(\w+)\s*;.*?#endif", RegexOptions.Singleline);
            if (detailMatch.Success)
            {
                string defineVar = detailMatch.Groups[1].Value;
                string modifierVar = detailMatch.Groups[2].Value;

                // 查找源变量的最后一次修改（在赋值之前）
                // 模式1：sourceVar.xxx = ...
                // 模式2：sourceVar *= += -= /= ...
                var lastModPatterns = new[] {
                    $@"({Regex.Escape(sourceVar)}\.[\w]+\s*=\s*[^;]+;)\s*\n\s*{Regex.Escape(targetVar)}\s*=\s*{Regex.Escape(sourceVar)}\s*;",
                    $@"({Regex.Escape(sourceVar)}\s*[*+/\-]=\s*[^;]+;)\s*\n\s*{Regex.Escape(targetVar)}\s*=\s*{Regex.Escape(sourceVar)}\s*;"
                };

                bool isFixed = false;
                foreach (var lastModPattern in lastModPatterns)
                {
                    var lastModMatch = Regex.Match(code, lastModPattern);
                    if (lastModMatch.Success)
                    {
                        // 在源变量最后修改后，目标赋值前插入条件修改
                        string insertion = $@"
#ifdef {defineVar}
        {sourceVar} *= {modifierVar};
#endif";

                        string replacement = lastModMatch.Groups[1].Value + insertion + $"\n        {targetVar} = {sourceVar};";
                        code = Regex.Replace(code, lastModPattern, replacement);

                        // 移除原来的后置条件编译块
                        code = code.Replace(conditionalBlock, "");

                        ExportLogger.Log($"LayaAir3D: Fixed post-assignment conditional modification - {modifierVar} applied to {sourceVar} before {targetVar} assignment");
                        isFixed = true;
                        break;
                    }
                }

                // 如果找不到明确的最后修改，尝试直接在赋值前插入
                if (!isFixed)
                {
                    var simplePattern = $@"{Regex.Escape(targetVar)}\s*=\s*{Regex.Escape(sourceVar)}\s*;";
                    var simpleMatch = Regex.Match(code, simplePattern);
                    if (simpleMatch.Success)
                    {
                        string insertion = $@"#ifdef {defineVar}
        {sourceVar} *= {modifierVar};
#endif
        ";
                        string replacement = insertion + simpleMatch.Value;
                        code = code.Replace(simpleMatch.Value, replacement);

                        // 移除原来的后置条件编译块
                        code = code.Replace(conditionalBlock, "");

                        ExportLogger.Log($"LayaAir3D: Fixed post-assignment conditional modification (simple) - {modifierVar} applied before {targetVar} = {sourceVar}");
                    }
                }
            }
        }

        // 移除多余的空语句
        code = Regex.Replace(code, @";\s*;", ";");

        // 移除空的if语句（if后只有分号）
        code = Regex.Replace(code, @"if\s*\([^)]*\)\s*;\s*\n", "");
        code = Regex.Replace(code, @"else\s+if\s*\([^)]*\)\s*;\s*\n", "");
        code = Regex.Replace(code, @"else\s*;\s*\n", "");

        // 修复三元运算符和vec2构造器换行导致的逗号分号问题
        // 匹配: 逗号后跟分号（错误的语法）
        code = Regex.Replace(code, @",\s*;\s*", ", ");

        // 修复函数调用被错误换行的情况
        // 例如: func(a,\n;\nb) -> func(a,\nb)
        code = Regex.Replace(code, @",\s*\n\s*;\s*\n\s*", ",\n    ");

        // 修复u_NormalMap_ST -> u_NormalTexture_ST
        code = Regex.Replace(code, @"\bu_NormalMap_ST\b", "u_NormalTexture_ST");

        // 移除残留的无效声明（如 vec2.0 o; 等）
        code = Regex.Replace(code, @"\b(vec2|vec3|vec4|float|int|mat2|mat3|mat4)\s*\.\s*\d+\s+\w+\s*;", "");

        // 移除空的struct声明
        code = Regex.Replace(code, @"struct\s+\w+\s*\{\s*\}\s*;?", "");
        
        // 注意：不要移除"重复"的varying声明！
        // VS和FS是两个独立的shader块，它们各自都需要声明相同的varying
        // 只在同一个shader块内移除重复声明
        
        // 分别处理VS和FS块内的重复varying
        code = RemoveDuplicateVaryingsInShaderBlocks(code);
        
        // ============================================
        // 修复语法问题
        // ============================================
        
        // ⭐ 修复缺少分号的语句（在行尾的赋值/声明后）
        // 注意：必须排除if/while/for等控制流语句中的条件表达式，否则会错误地在if(condition)后加分号
        // 匹配：行首的变量赋值（不在括号内）后没有分号，后面紧跟换行
        // 使用更精确的模式：不匹配括号内的等号（避免匹配if (x == 0)这种情况）
        // code = Regex.Replace(code, @"(\w+\s*=\s*[^;{}\n]+)(\s*\n\s*(?![\s{]))", "$1;$2");
        // ⭐ 注释掉上面的正则，因为它会错误地给if (condition)添加分号
        // 如果代码是从ParticleShaderTemplate生成的，应该已经有正确的分号
        
        // 修复括号位置错误：) 后面紧跟 ( 应该有操作符或分号
        // 例如：)(  可能是漏了分号或操作符
        code = Regex.Replace(code, @"\)\s*\((?!\s*\))", "); (");
        
        // 修复双前缀问题
        code = Regex.Replace(code, @"\bu_u_", "u_");
        
        // 修复 texture2D 等函数调用缺少分号
        code = Regex.Replace(code, @"(texture2D\s*\([^)]+\))\s*\n", "$1;\n");
        code = Regex.Replace(code, @"(textureCube\s*\([^)]+\))\s*\n", "$1;\n");
        
        // 清理多余的空行
        code = Regex.Replace(code, @"\n\s*\n\s*\n", "\n\n");
        
        // 修复可能的语法问题：移除行首的逗号
        code = Regex.Replace(code, @"^\s*,", "", RegexOptions.Multiline);
        
        // 移除空的uniform块
        code = Regex.Replace(code, @"uniform\s*\{\s*\}", "");
        
        // 移除多余的分号（连续分号）
        code = Regex.Replace(code, @";\s*;+", ";");
        
        // 修复 #endif 后面可能缺少换行
        code = Regex.Replace(code, @"(#endif)([^\s\n])", "$1\n$2");

        return code;
    }

    /// <summary>
    /// 格式化shader内容，提升可读性
    /// </summary>
    private static string FormatShaderContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 第一步：基础清理
        content = Regex.Replace(content, @"[ \t]+$", "", RegexOptions.Multiline); // 移除行尾空白
        content = Regex.Replace(content, @"\n{3,}", "\n\n"); // 最多保留一个空行

        // 第二步：分离Shader3D块和GLSL块进行格式化
        var parts = SplitShaderIntoParts(content);
        var formattedParts = new List<string>();

        foreach (var part in parts)
        {
            if (part.type == ShaderPartType.Shader3D)
            {
                formattedParts.Add(FormatShader3DBlock(part.content));
            }
            else if (part.type == ShaderPartType.GLSL)
            {
                formattedParts.Add(FormatGLSLBlock(part.content));
            }
            else
            {
                formattedParts.Add(part.content);
            }
        }

        // 合并并最终清理
        string result = string.Join("\n\n", formattedParts);
        result = Regex.Replace(result, @"\n{3,}", "\n\n"); // 再次清理多余空行
        result = result.TrimEnd() + "\n"; // 确保文件末尾只有一个换行

        return result;
    }

    /// <summary>
    /// 注入Unity URP结构体定义（BRDFData、InputData等）
    /// 当HLSL→GLSL转换后的代码引用了这些URP结构体但没有定义时，自动注入GLSL版本的struct定义
    /// </summary>
    private static string InjectURPStructDefinitions(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        bool needsBRDFData = Regex.IsMatch(content, @"\bBRDFData\b");
        bool needsInputData = Regex.IsMatch(content, @"\bInputData\s+\w+");

        if (!needsBRDFData && !needsInputData)
            return content;

        // 构建需要注入的结构体代码
        StringBuilder structs = new StringBuilder();

        if (needsBRDFData)
        {
            structs.AppendLine("// Unity URP BRDFData struct (auto-injected)");
            structs.AppendLine("struct BRDFData {");
            structs.AppendLine("    vec3 diffuse;");
            structs.AppendLine("    vec3 specular;");
            structs.AppendLine("    float perceptualRoughness;");
            structs.AppendLine("    float roughness;");
            structs.AppendLine("    float roughness2;");
            structs.AppendLine("    float roughness2MinusOne;");
            structs.AppendLine("    float normalizationTerm;");
            structs.AppendLine("    float grazingTerm;");
            structs.AppendLine("};");
            structs.AppendLine();
            // InitializeBRDFData — 简化版，只做最基本的初始化
            structs.AppendLine("void InitializeBRDFData(vec4 albedo, float metallic, float specularVal, float smoothness, float alpha, out BRDFData outBRDFData) {");
            structs.AppendLine("    float oneMinusReflectivity = 1.0 - max(max(specularVal, specularVal), specularVal) * metallic;");
            structs.AppendLine("    outBRDFData.diffuse = albedo.rgb * oneMinusReflectivity;");
            structs.AppendLine("    outBRDFData.specular = mix(vec3(specularVal), albedo.rgb, metallic);");
            structs.AppendLine("    outBRDFData.perceptualRoughness = 1.0 - smoothness;");
            structs.AppendLine("    outBRDFData.roughness = max(outBRDFData.perceptualRoughness * outBRDFData.perceptualRoughness, 0.0078125);");
            structs.AppendLine("    outBRDFData.roughness2 = outBRDFData.roughness * outBRDFData.roughness;");
            structs.AppendLine("    outBRDFData.roughness2MinusOne = outBRDFData.roughness2 - 1.0;");
            structs.AppendLine("    outBRDFData.normalizationTerm = outBRDFData.roughness * 4.0 + 2.0;");
            structs.AppendLine("    outBRDFData.grazingTerm = clamp(smoothness + (1.0 - oneMinusReflectivity), 0.0, 1.0);");
            structs.AppendLine("}");
            structs.AppendLine();
        }

        if (needsInputData)
        {
            structs.AppendLine("// Unity URP InputData struct (auto-injected)");
            structs.AppendLine("struct InputData {");
            structs.AppendLine("    vec3 positionWS;");
            structs.AppendLine("    vec3 normalWS;");
            structs.AppendLine("    vec3 viewDirectionWS;");
            structs.AppendLine("    vec3 bakedGI;");
            structs.AppendLine("    float fogCoord;");
            structs.AppendLine("    vec4 shadowCoord;");
            structs.AppendLine("};");
            structs.AppendLine();
        }

        string structCode = structs.ToString();

        // 在每个GLSL section中，void main()或第一个函数定义之前注入struct
        // 查找所有 #defineGLSL 块
        string result = Regex.Replace(content, @"(#defineGLSL\s+\w+(?:VS|FS)\s*\n(?:#[^\n]*\n|#include[^\n]*\n|varying[^\n]*\n|\s*\n)*)", match =>
        {
            string section = match.Value;
            // 如果这个section的后续代码使用了BRDFData/InputData，在section头部（includes之后）注入
            return section + "\n" + structCode;
        });

        if (result != content)
        {
            ExportLogger.Log($"LayaAir3D: InjectURPStructDefinitions - injected struct definitions (BRDFData={needsBRDFData}, InputData={needsInputData})");
        }

        return result;
    }

    /// <summary>
    /// 修复FS中对varying变量的l-value赋值错误。
    /// GLSL中varying在fragment shader里是只读的，不能赋值。
    /// 此函数在FS的main()开头创建局部副本（local_TexcoordN），
    /// 然后将FS main()中所有对v_TexcoordN的引用替换为local_TexcoordN。
    /// </summary>
    private static string FixFragmentVaryingLValueAssignment(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 找到FS section: #defineGLSL xxxFS ... #endGLSL
        var fsMatch = Regex.Match(content, @"(#defineGLSL\s+\w+FS\b.*?)(void\s+main\s*\(\s*\)\s*\{)(.*?)(#endGLSL)", RegexOptions.Singleline);
        if (!fsMatch.Success)
            return content;

        string fsBeforeMain = fsMatch.Groups[1].Value;
        string mainSignature = fsMatch.Groups[2].Value;
        string mainBody = fsMatch.Groups[3].Value;
        string endTag = fsMatch.Groups[4].Value;

        // 查找FS main()中被赋值的varying变量（v_TexcoordN.xx = ...）
        var assignedVaryings = new HashSet<string>();
        var assignPattern = new Regex(@"\b(v_Texcoord\d+)\s*\.\w+\s*=(?!=)");
        foreach (Match m in assignPattern.Matches(mainBody))
        {
            assignedVaryings.Add(m.Groups[1].Value);
        }

        if (assignedVaryings.Count == 0)
            return content;

        // 同时也替换所有在FS中读取的同名varying，保持一致性
        // 构建局部变量声明
        var declarations = new StringBuilder();
        declarations.AppendLine();
        declarations.AppendLine("            // 局部副本：GLSL中varying在FS是只读的");
        foreach (var varyingName in assignedVaryings.OrderBy(v => v))
        {
            // 检测varying的类型（从FS声明中查找）
            var typeMatch = Regex.Match(fsBeforeMain, @"varying\s+(vec[234]|float|mat[234])\s+" + Regex.Escape(varyingName) + @"\b");
            string varyingType = typeMatch.Success ? typeMatch.Groups[1].Value : "vec4";
            string localName = varyingName.Replace("v_", "local_");
            declarations.AppendLine($"            {varyingType} {localName} = {varyingName};");
        }

        // 在main body中替换所有引用
        string newBody = mainBody;
        foreach (var varyingName in assignedVaryings)
        {
            string localName = varyingName.Replace("v_", "local_");
            newBody = Regex.Replace(newBody, @"\b" + Regex.Escape(varyingName) + @"\b", localName);
        }

        // 重新组装
        string newContent = content.Substring(0, fsMatch.Groups[2].Index + fsMatch.Groups[2].Length)
            + declarations.ToString()
            + newBody
            + endTag
            + content.Substring(fsMatch.Index + fsMatch.Length);

        if (newContent != content)
        {
            ExportLogger.Log($"LayaAir3D: FixFragmentVaryingLValueAssignment - 替换了 {assignedVaryings.Count} 个varying的FS引用: {string.Join(", ", assignedVaryings)}");
        }

        return newContent;
    }

    /// <summary>
    /// 修复vec3与vec4之间的乘法类型不匹配。
    /// HLSL允许vec3*vec4隐式截断，但GLSL不允许。
    /// 此函数在 .rgb/.xyz 赋值行中，为vec4类型的uniform添加.xyz swizzle。
    /// </summary>
    private static string FixVec3TimesVec4Operations(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 收集所有vec4类型的uniform（从uniformMap中的Color和Vector4类型）
        var vec4Uniforms = new HashSet<string>();
        var uniformPattern = new Regex(@"(\w+)\s*:\s*\{\s*type\s*:\s*(?:Color|Vector4)\b");
        foreach (Match m in uniformPattern.Matches(content))
        {
            vec4Uniforms.Add(m.Groups[1].Value);
        }

        if (vec4Uniforms.Count == 0)
            return content;

        // 在 .rgb 或 .xyz 赋值行中，找到没有swizzle的vec4 uniform并添加.xyz
        var lines = content.Split('\n');
        int fixCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            // 只处理期望vec3的行：包含 .rgb/.xyz 运算，或 vec3 变量声明/赋值
            if (!Regex.IsMatch(line, @"\.\s*(rgb|xyz)\s*[\*\+\-=]") &&
                !Regex.IsMatch(line, @"\bvec3\s+\w+\s*="))
                continue;

            foreach (var uniform in vec4Uniforms)
            {
                // 匹配没有swizzle后缀的uniform引用（后面不跟.xyz/.rgb/.x等）
                string pattern = @"\b" + Regex.Escape(uniform) + @"\b(?!\s*\.\s*[xyzwrgba])";
                if (Regex.IsMatch(line, pattern))
                {
                    lines[i] = Regex.Replace(lines[i], pattern, uniform + ".xyz");
                    fixCount++;
                }
            }
        }

        if (fixCount > 0)
        {
            ExportLogger.Log($"LayaAir3D: FixVec3TimesVec4Operations - 修复了 {fixCount} 处vec3*vec4类型不匹配");
            return string.Join("\n", lines);
        }

        return content;
    }

    /// <summary>
    /// 修复shader中的GLSL类型不匹配问题
    /// 主要解决v_Texcoord0 (vec4) 与 vec2 之间的类型转换问题
    /// </summary>
    private static string FixShaderTypeMismatch(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // ⭐ 0. 修复错误的swizzle访问 (CRITICAL FIX)
        // vec2没有.z和.w分量，vec3没有.w分量，这是常见的GLSL类型错误
        // 例如：u_TilingOffset.xy.z -> u_TilingOffset.z
        // 这个修复必须在最前面，因为它修复的是根本性的类型错误

        // 修复 .xy.z 和 .xy.w (vec2错误访问z/w分量)
        content = Regex.Replace(content, @"(\w+)\.xy\.z\b", "$1.z");
        content = Regex.Replace(content, @"(\w+)\.xy\.w\b", "$1.w");

        // 修复 .xyz.w (vec3错误访问w分量)
        content = Regex.Replace(content, @"(\w+)\.xyz\.w\b", "$1.w");

        // 修复 .x.y / .x.z / .y.z 等单分量后再访问其他分量的错误
        content = Regex.Replace(content, @"(\w+)\.x\.y\b", "$1.y");
        content = Regex.Replace(content, @"(\w+)\.x\.z\b", "$1.z");
        content = Regex.Replace(content, @"(\w+)\.x\.w\b", "$1.w");
        content = Regex.Replace(content, @"(\w+)\.y\.z\b", "$1.z");
        content = Regex.Replace(content, @"(\w+)\.y\.w\b", "$1.w");
        content = Regex.Replace(content, @"(\w+)\.z\.w\b", "$1.w");

        ExportLogger.Log("LayaAir3D: Applied swizzle access fix (vec2/vec3/vec4 invalid swizzle patterns)");

        // 1. 修复 texture2D() 调用中的 v_Texcoord0 -> v_Texcoord0.xy
        // 匹配: texture2D(texture_name, v_Texcoord0 ...)
        // 必须在texture2D的第二个参数位置
        content = Regex.Replace(
            content,
            @"\btexture2D\s*\(\s*([^,]+),\s*v_Texcoord0(?![.\w])",
            "texture2D($1, v_Texcoord0.xy",
            RegexOptions.Multiline
        );

        // 2. 修复 vec2(...) + v_Texcoord0 -> vec2(...) + v_Texcoord0.xy
        content = Regex.Replace(
            content,
            @"(\bvec2\s*\([^)]+\))\s*([+\-])\s*v_Texcoord0(?![.\w])",
            "$1 $2 v_Texcoord0.xy",
            RegexOptions.Multiline
        );

        // 3. 修复 v_Texcoord0 + (expr) -> v_Texcoord0.xy + (expr)
        // 匹配括号表达式，如 v_Texcoord0 + (u_Time * ...)
        content = Regex.Replace(
            content,
            @"v_Texcoord0(?![.\w])\s*([+\-])\s*\(",
            "v_Texcoord0.xy $1 (",
            RegexOptions.Multiline
        );

        // 4. 修复 v_Texcoord0 + expr（非括号） -> v_Texcoord0.xy + expr
        // 匹配到行尾或逗号、分号等结束符
        content = Regex.Replace(
            content,
            @"v_Texcoord0(?![.\w])\s*([+\-])\s*([^,;)\n]+)",
            "v_Texcoord0.xy $1 $2",
            RegexOptions.Multiline
        );

        // 5. 修复 texture2D 中的复杂表达式
        // texture2D(tex, expr + v_Texcoord0) 或 texture2D(tex, v_Texcoord0 + expr)
        // 这个规则确保texture2D的第二个参数中的v_Texcoord0都有.xy
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // 如果行中包含texture2D和v_Texcoord0（但不是v_Texcoord0.xy/.x/.y/.z/.w）
            if (line.Contains("texture2D") &&
                Regex.IsMatch(line, @"v_Texcoord0(?![.\w])"))
            {
                // 在texture2D的参数中，所有v_Texcoord0都应该是v_Texcoord0.xy
                // 匹配texture2D的第二个参数
                line = Regex.Replace(line,
                    @"(texture2D\s*\([^,]+,\s*)([^)]+)",
                    m => {
                        string prefix = m.Groups[1].Value;
                        string uvExpr = m.Groups[2].Value;
                        // 在UV表达式中，替换所有裸的v_Texcoord0为v_Texcoord0.xy
                        uvExpr = Regex.Replace(uvExpr, @"v_Texcoord0(?![.\w])", "v_Texcoord0.xy");
                        return prefix + uvExpr;
                    }
                );
                lines[i] = line;
            }

            // 处理其他包含v_Texcoord0算术运算的行
            if (Regex.IsMatch(line, @"v_Texcoord0(?![.\w])\s*[+\-]") ||
                Regex.IsMatch(line, @"[+\-]\s*v_Texcoord0(?![.\w])"))
            {
                // 检查是否是UV/坐标相关的计算（排除varying声明）
                if (!line.Contains("varying") &&
                    (line.Contains("texture2D") || line.Contains("UV") || line.Contains("uv") ||
                     line.Contains("*") || line.Contains("u_Time") || line.Contains("Scroll") ||
                     line.Contains("vec2")))
                {
                    // 替换所有算术运算中的v_Texcoord0
                    line = Regex.Replace(line, @"v_Texcoord0(?![.\w])(\s*[+\-])", "v_Texcoord0.xy$1");
                    line = Regex.Replace(line, @"([+\-]\s*)v_Texcoord0(?![.\w])", "$1v_Texcoord0.xy");
                    lines[i] = line;
                }
            }
        }
        content = string.Join("\n", lines);

        // 6. 修复嵌套的 vec2((expr + v_Texcoord0))
        content = Regex.Replace(
            content,
            @"vec2\s*\(\s*\(([^)]+\s*[+\-]\s*)v_Texcoord0(?![.\w])",
            "vec2(($1v_Texcoord0.xy",
            RegexOptions.Multiline
        );

        // 7. 修复 texture() 函数调用为 texture2D()
        // 在GLSL ES 2.0中，应该使用texture2D而不是texture
        content = Regex.Replace(
            content,
            @"\btexture\s*\(",
            "texture2D(",
            RegexOptions.Multiline
        );

        // 8. 最终清理：确保没有遗漏的v_Texcoord0在运算中
        // 再次扫描，替换任何在表达式中但没有.xy的v_Texcoord0
        content = Regex.Replace(
            content,
            @"([=+\-*/,\(]\s*)v_Texcoord0(?![.\w])(?=\s*[+\-*/,\)])",
            "$1v_Texcoord0.xy",
            RegexOptions.Multiline
        );

        // ⭐ 9. 修复vec4到vec2的赋值问题 (CRITICAL FIX FOR TYPE MISMATCH)
        // 检测并修复 "vec2 varName = vec4Value;" 这样的赋值
        // 需要自动添加 .xy 使其变成 "vec2 varName = vec4Value.xy;"
        content = FixVec4ToVec2Assignments(content);

        // ⭐ 10. 修复多余的vec2()构造函数
        // 例如: vec2(vec2_expression) -> vec2_expression
        content = RemoveRedundantVec2Constructors(content);

        // ⭐ 11. 修复函数参数类型不匹配
        // 检测函数调用中vec4传给vec2参数的情况
        content = FixFunctionParameterTypeMismatch(content);

        // ⭐ 12. 修复texture2D返回值在vec2算术运算中的类型不匹配 (CRITICAL)
        // 例如: vec2Var += texture2D(...) * strength
        // texture2D返回vec4，需要添加.xy或.rg
        content = FixTexture2DInVec2Operations(content);

        // ⭐ 13. 修复数组下标中的float类型表达式（GLSL要求整数下标）
        // HLSL允许float隐式转换为int用作数组下标，GLSL严格禁止
        // 例如: v_Texcoord7[u_X - 1.0] → v_Texcoord7[int(u_X - 1.0)]
        content = FixFloatArrayIndices(content);

        // ⭐ 14. 修复vec4到vec3的赋值问题 (CRITICAL FIX FOR TEXTURE SAMPLING)
        // HLSL: float3 mask = tex2D(...)  (tex2D返回float4，HLSL允许float4→float3截断)
        // GLSL: texture2D()返回vec4，赋值给vec3需要显式添加.rgb
        content = FixVec4ToVec3Assignments(content);

        // ⭐ 15. 修复mix()函数中标量字面量与向量类型不匹配的问题
        // HLSL lerp(0, vec3, factor) 允许标量自动广播，GLSL严格要求所有参数类型一致
        // 例如: mix(0.0, col, mask) → mix(vec3(0.0), col, mask)
        content = FixMixScalarArgs(content);

        // ⭐ 16. 修复标量变量与向量类型的乘法赋值 (float *= vec3/vec2)
        // HLSL: half alpha *= float3; (隐式取第一分量)
        // GLSL: float alpha *= vec3; 是非法的，需要 alpha *= mask.r;
        content = FixScalarTimesVectorAssignment(content);

        // ⭐ 17. 修复 HLSL 标量→向量隐式广播 (CRITICAL)
        // HLSL 允许 float 隐式提升为 float2/3/4，GLSL 不允许。
        // a) vec2/vec3/vec4 函数返回标量表达式 → return vecN(expr);
        // b) vec2/vec3/vec4 局部变量用标量初始化 → 改为 float
        content = FixScalarVectorBroadcast(content);

        ExportLogger.Log("LayaAir3D: Applied comprehensive type mismatch fixes");

        return content;
    }

    /// <summary>
    /// 修复 } else { 大括号缩进不一致问题。
    /// 扫描每个 "} else {" 行，向前查找匹配的 "if (...) {"，
    /// 若缩进不一致则统一为 if 行的缩进级别。
    /// 同时修复 #else/#endif 后代码块的多余缩进。
    /// </summary>
    private static string FixBraceElseFormatting(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        string[] lines = content.Split('\n');
        bool changed = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();

            // 检测 "} else {" 或 "} else if (...) {" 模式
            if (!trimmed.StartsWith("} else"))
                continue;

            string currentIndent = lines[i].Substring(0, lines[i].Length - trimmed.Length);

            // 向前查找匹配的 opening brace 所在行
            int braceDepth = 0;
            string matchedIfIndent = null;
            for (int j = i - 1; j >= 0; j--)
            {
                string jTrimmed = lines[j].TrimStart();
                // 跳过空行和预处理指令
                if (jTrimmed.Length == 0 || jTrimmed.StartsWith("#"))
                    continue;

                // 计算这行的大括号
                foreach (char c in jTrimmed)
                {
                    if (c == '}') braceDepth++;
                    else if (c == '{') braceDepth--;
                }

                // braceDepth < 0 表示找到了一个未关闭的 {
                if (braceDepth < 0)
                {
                    matchedIfIndent = lines[j].Substring(0, lines[j].Length - jTrimmed.Length);
                    break;
                }
            }

            // 如果找到匹配的 if 行且缩进不一致，修复 } else { 行的缩进
            if (matchedIfIndent != null && currentIndent != matchedIfIndent)
            {
                lines[i] = matchedIfIndent + trimmed;
                changed = true;

                // 同时修复 else 块内部的缩进（到对应的 } 为止）
                int extraSpaces = currentIndent.Length - matchedIfIndent.Length;
                if (extraSpaces > 0)
                {
                    int elseDepth = 0;
                    for (int k = i + 1; k < lines.Length; k++)
                    {
                        string kTrimmed = lines[k].TrimStart();
                        if (kTrimmed.Length == 0 || kTrimmed.StartsWith("#"))
                            continue;

                        foreach (char c in kTrimmed)
                        {
                            if (c == '{') elseDepth++;
                            else if (c == '}') elseDepth--;
                        }

                        // 移除多余的前导空白
                        string kIndent = lines[k].Substring(0, lines[k].Length - kTrimmed.Length);
                        if (kIndent.Length >= extraSpaces)
                        {
                            lines[k] = kIndent.Substring(extraSpaces) + kTrimmed;
                            changed = true;
                        }

                        if (elseDepth < 0)
                            break; // 已到达 else 块的结束 }
                    }
                }
            }
        }

        if (changed)
            return string.Join("\n", lines);
        return content;
    }

    /// <summary>
    /// 修复 "{ discard; };" 等尾部分号导致的 dangling else 语法错误。
    /// 在 GLSL 中，"if (cond) { discard; };" 的尾部 ";" 是一条空语句，
    /// 会截断 if-else 链，导致后续 else 无法匹配到 if。
    ///
    /// 此方法做两件事：
    /// 1. 移除 "{ ... };" 模式中多余的尾部分号
    /// 2. 修复裸 "if ... if ... else" 模式：为外层 if 添加大括号
    /// </summary>
    private static string FixDanglingElseSemicolon(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Step 1: 移除 "} ;" 模式中多余的尾部分号（如 "{ discard; };"）
        // 仅匹配有缩进的行（前面有空格/tab），避免误删struct定义的 "};":
        //   struct Foo { ... };  ← 行首无缩进，GLSL必须保留分号
        //       { discard; };    ← 有缩进，在函数体内，应移除尾部分号
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"([ \t]+)\}\s*;(\s*$)",
            "$1}$2",
            System.Text.RegularExpressions.RegexOptions.Multiline
        );

        // Step 2: 检测裸 "if (...)\n  if (...) ...\n  else" 模式并为外层 if 添加大括号
        string[] lines = content.Split('\n');
        bool changed = false;

        for (int i = 0; i < lines.Length - 2; i++)
        {
            string trimI = lines[i].TrimStart();
            string trimI1 = lines[i + 1].TrimStart();

            // 检测: 外层 if 没有 { ，下一行也是 if，后面有 else
            if (trimI.StartsWith("if (") && !trimI.Contains("{") &&
                trimI1.StartsWith("if (") && trimI1.Contains("{ discard;"))
            {
                // 查找后面的 else
                for (int j = i + 2; j < lines.Length && j <= i + 4; j++)
                {
                    string trimJ = lines[j].TrimStart();
                    if (trimJ.StartsWith("else"))
                    {
                        // 为外层 if 添加 {
                        string indent = lines[i].Substring(0, lines[i].Length - trimI.Length);
                        lines[i] = indent + trimI + " {";
                        // 内层 if 增加缩进
                        string innerIndent = indent + "    ";
                        lines[i + 1] = innerIndent + trimI1;
                        // 在 else 前添加 }
                        lines[j] = indent + "} " + trimJ;
                        changed = true;
                        break;
                    }
                    if (trimJ.Length > 0 && !trimJ.StartsWith("//") && !trimJ.StartsWith("#"))
                        break;
                }
            }
        }

        if (changed)
            return string.Join("\n", lines);
        return content;
    }

    /// <summary>
    /// 修复GLSL数组下标中的float类型表达式
    /// GLSL要求数组下标必须是整数表达式（int/uint），但HLSL允许float隐式转换
    /// 典型模式: v_Texcoord7[u_SomeCustom - 1.0]  →  v_Texcoord7[int(u_SomeCustom - 1.0)]
    /// 检测条件: 下标表达式中含有浮点数字面量（如 1.0、2.0、0.5 等）
    /// </summary>
    private static string FixFloatArrayIndices(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        return Regex.Replace(content,
            @"\b(\w+)\s*\[([^\[\]]+)\]",
            m =>
            {
                string varName = m.Groups[1].Value;
                string idx = m.Groups[2].Value.Trim();

                // 跳过已经包装了 int(...) 的情况，避免双重包装
                if (Regex.IsMatch(idx, @"^int\s*\("))
                    return m.Value;

                // 跳过纯整数字面量（如 [0], [1], [3]）
                if (Regex.IsMatch(idx, @"^\s*\d+\s*$"))
                    return m.Value;

                // 检测下标表达式中是否含有浮点数字面量
                // 匹配: 1.0  /  2.0  /  0.5  /  .5 等形式
                if (Regex.IsMatch(idx, @"\b\d+\.\d*|\b\.\d+"))
                {
                    return $"{varName}[int({idx})]";
                }

                return m.Value;
            });
    }

    /// <summary>
    /// 修复vec4到vec3的赋值问题（主要针对texture2D返回值赋给vec3变量）
    /// HLSL: float3 mask = tex2D(...)  (float4→float3隐式截断)
    /// GLSL: texture2D()返回vec4，必须显式添加.rgb分量访问
    /// 例如: vec3 mask = texture2D(u_MaskTex, uv); → vec3 mask = texture2D(u_MaskTex, uv).rgb;
    /// </summary>
    private static string FixVec4ToVec3Assignments(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 逐行处理：检测 vec3 varName = texture2D(...); 模式
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // 检测行中有 vec3 xxx = ... texture2D(...)...;
            // 且末尾不是 .rgb / .xyz 结尾的
            if (Regex.IsMatch(line, @"\bvec3\s+\w+\s*=") &&
                line.Contains("texture2D") &&
                !Regex.IsMatch(line, @"texture2D\s*\([^)]*\)\s*\.(?:rgb|xyz)"))
            {
                // 在最后一个 ) 和 ; 之间插入 .rgb
                // 匹配: texture2D(...)（最后一个不含.rgb/.xyz的纹理调用）后加.rgb
                lines[i] = Regex.Replace(line,
                    @"(texture2D\s*\([^;]*?\))\s*;",
                    m =>
                    {
                        string expr = m.Groups[1].Value;
                        // 跳过已有swizzle的
                        if (Regex.IsMatch(expr, @"\)\s*\.(?:rgb|xyz|r|g|b|a)\s*$"))
                            return m.Value;
                        return expr + ".rgb;";
                    });
            }
        }
        content = string.Join("\n", lines);

        return content;
    }

    /// <summary>
    /// 修复mix()函数中标量字面量与向量类型不匹配的问题
    /// HLSL lerp(0, vec3_expr, factor) 允许标量自动广播为vec3，GLSL要求严格类型一致
    /// 通用方案：根据上下文（赋值目标类型、参数类型）自动推断并修复标量字面量
    /// 例如: finalColor.rgb = mix(0.0, col, mask); → mix(vec3(0.0), col, mask);
    /// </summary>
    private static string FixMixScalarArgs(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 收集所有vec3/vec2变量名（用于类型推断，排除函数定义）
        var vec3Vars = new HashSet<string>();
        var vec2Vars = new HashSet<string>();
        foreach (Match m in Regex.Matches(content, @"\bvec3\s+(\w+)(?!\s*\()"))
            vec3Vars.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(content, @"\bvec2\s+(\w+)(?!\s*\()"))
            vec2Vars.Add(m.Groups[1].Value);

        // ---- Fix 1: LHS有 .rgb / .xyz 时，mix第一或第二参数的标量字面量应是vec3 ----
        // 模式: something.rgb [op]= ... mix(0.0, ...) 或 mix(..., 0.0, ...)
        // 先处理第一参数是标量的情况
        content = Regex.Replace(content,
            @"(\.(?:rgb|xyz)\s*[+\-]?=\s*[^;\n]*?)mix\s*\(\s*(0(?:\.0*)?|1(?:\.0*)?)\s*,",
            m => m.Groups[1].Value + "mix(vec3(" + m.Groups[2].Value + "),",
            RegexOptions.Multiline);

        // ---- Fix 2: mix(scalar, arg2, ...) 其中 arg2 包含 .rgb/.xyz（明确是vec3）----
        content = Regex.Replace(content,
            @"\bmix\s*\(\s*(0(?:\.0*)?|1(?:\.0*)?)\s*,\s*(\w+(?:\.\w+)*\.(?:rgb|xyz)\b)",
            m => "mix(vec3(" + m.Groups[1].Value + "), " + m.Groups[2].Value,
            RegexOptions.Multiline);

        // ---- Fix 3: mix(scalar, vecVar, ...) 其中 vecVar 是已知的 vec3 变量 ----
        if (vec3Vars.Count > 0)
        {
            foreach (var v in vec3Vars)
            {
                // mix(0.0, vec3Var, ...) → mix(vec3(0.0), vec3Var, ...)
                content = Regex.Replace(content,
                    $@"\bmix\s*\(\s*(0(?:\.0*)?|1(?:\.0*)?)\s*,\s*({Regex.Escape(v)})\s*,",
                    m => "mix(vec3(" + m.Groups[1].Value + "), " + m.Groups[2].Value + ",",
                    RegexOptions.Multiline);
            }
        }

        // ---- Fix 4: 已知的 vec3 变量作为赋值目标时，mix的标量字面量应是vec3 ----
        // vec3Var = mix(0.0, ...) 或 vec3Var = ... + mix(0.0, ...)
        if (vec3Vars.Count > 0)
        {
            foreach (var v in vec3Vars)
            {
                content = Regex.Replace(content,
                    $@"\b({Regex.Escape(v)})\s*=\s*mix\s*\(\s*(0(?:\.0*)?|1(?:\.0*)?)\s*,",
                    m => m.Groups[1].Value + " = mix(vec3(" + m.Groups[2].Value + "),",
                    RegexOptions.Multiline);
            }
        }

        return content;
    }

    /// <summary>
    /// 修复标量变量与向量类型的乘法赋值不匹配问题
    /// HLSL: half alpha *= float3;  (隐式取第一分量 .x/.r)
    /// GLSL: float alpha *= vec3;  是非法的，需要 alpha *= mask.r;
    /// 通用方案：检测 scalar_var *= vector_var，自动添加.r分量访问
    /// </summary>
    private static string FixScalarTimesVectorAssignment(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 收集所有vector类型变量（vec2/vec3/vec4），排除函数定义（避免函数名被误识别为变量名）
        var vectorVars = new Dictionary<string, string>(); // varName → vecN
        foreach (Match m in Regex.Matches(content, @"\b(vec2|vec3|vec4)\s+(\w+)(?!\s*\()"))
            vectorVars[m.Groups[2].Value] = m.Groups[1].Value;

        if (vectorVars.Count == 0)
            return content;

        // 收集所有scalar类型变量（float/int/uint）
        // 用于排除 vec3_var *= vec3_var 的情况
        var scalarVars = new HashSet<string>();
        foreach (Match m in Regex.Matches(content, @"\bfloat\s+(\w+)\b"))
            scalarVars.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(content, @"\bint\s+(\w+)\b"))
            scalarVars.Add(m.Groups[1].Value);

        // 修复: scalarVar *= vectorVar; → scalarVar *= vectorVar.r;
        content = Regex.Replace(content,
            @"\b(\w+)\s*\*=\s*(\w+)\s*;",
            m =>
            {
                string lhs = m.Groups[1].Value;
                string rhs = m.Groups[2].Value;

                // RHS必须是已知的向量变量
                if (!vectorVars.ContainsKey(rhs))
                    return m.Value;

                // LHS如果也是向量变量则跳过（向量*=向量是合法的）
                if (vectorVars.ContainsKey(lhs))
                    return m.Value;

                // LHS是标量或float变量 → 添加.r
                return $"{lhs} *= {rhs}.r;";
            });

        return content;
    }

    /// <summary>
    /// 修复 HLSL→GLSL 标量→向量隐式广播。
    /// HLSL 允许 float 自动提升为 float2/3/4，GLSL 不允许。
    /// a) vecN 函数中 return scalar_expr → return vecN(scalar_expr)
    /// b) vecN varName = scalar_expr → float varName = scalar_expr
    /// </summary>
    private static string FixScalarVectorBroadcast(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 已知返回标量的 GLSL 内置函数
        var scalarBuiltins = new HashSet<string> {
            "dot", "length", "distance", "sin", "cos", "tan", "asin", "acos", "atan",
            "pow", "exp", "log", "log2", "sqrt", "inversesqrt",
            "abs", "sign", "floor", "ceil", "fract", "mod", "min", "max",
            "clamp", "step", "smoothstep"
        };

        // ── Fix A: vecN 函数返回标量 ──
        // 找到所有 vec 返回类型的函数定义，检查其 return 语句
        var funcDefPattern = new Regex(
            @"\b(vec[234])\s+(\w+)\s*\(([^)]*)\)\s*\{",
            RegexOptions.Singleline);

        // 从后往前处理，避免替换偏移问题
        var funcMatches = funcDefPattern.Matches(content);
        for (int fi = funcMatches.Count - 1; fi >= 0; fi--)
        {
            Match funcMatch = funcMatches[fi];
            string retType = funcMatch.Groups[1].Value;   // vec2 / vec3 / vec4
            string funcParams = funcMatch.Groups[3].Value;

            // 找匹配的右大括号（简单的花括号计数）
            int braceDepth = 1;
            int bodyStart = funcMatch.Index + funcMatch.Length;
            int bodyEnd = -1;
            for (int ci = bodyStart; ci < content.Length && braceDepth > 0; ci++)
            {
                if (content[ci] == '{') braceDepth++;
                else if (content[ci] == '}') { braceDepth--; if (braceDepth == 0) bodyEnd = ci; }
            }
            if (bodyEnd < 0) continue;

            string body = content.Substring(bodyStart, bodyEnd - bodyStart);

            // 收集函数内 vec 类型的参数和局部变量名
            var vecVarNames = new HashSet<string>();
            // 参数
            foreach (Match pm in Regex.Matches(funcParams, @"\b(?:vec[234])\s+(\w+)"))
                vecVarNames.Add(pm.Groups[1].Value);
            // 局部变量
            foreach (Match lm in Regex.Matches(body, @"\b(?:vec[234])\s+(\w+)"))
                vecVarNames.Add(lm.Groups[1].Value);

            // 找所有 return 语句并检查
            var retPattern = new Regex(@"\breturn\s+([^;]+);");
            string fixedBody = body;
            var retMatches = retPattern.Matches(body);
            for (int ri = retMatches.Count - 1; ri >= 0; ri--)
            {
                Match retMatch = retMatches[ri];
                string expr = retMatch.Groups[1].Value.Trim();

                // 已经用 vec 构造器包裹 → 跳过
                if (expr.StartsWith(retType + "(")) continue;
                if (Regex.IsMatch(expr, @"^vec[234]\s*\(")) continue;

                // 如果表达式引用了任何 vec 变量 → 很可能已是向量，跳过
                bool refsVec = false;
                foreach (string vn in vecVarNames)
                {
                    if (Regex.IsMatch(expr, @"\b" + Regex.Escape(vn) + @"\b"))
                    { refsVec = true; break; }
                }
                if (refsVec) continue;

                // 表达式大概率是标量 → 用 vecN() 包裹
                string wrapped = "return " + retType + "(" + expr + ");";
                fixedBody = fixedBody.Substring(0, retMatch.Index)
                          + wrapped
                          + fixedBody.Substring(retMatch.Index + retMatch.Length);
            }

            if (fixedBody != body)
            {
                content = content.Substring(0, bodyStart) + fixedBody + content.Substring(bodyEnd);
                ExportLogger.Log($"LayaAir3D: Fixed scalar return in vec function (wrapped with {retType}())");
            }
        }

        // ── Fix B: vecN 变量用标量表达式初始化 ──
        // 模式: vecN varName = mix( dot(...), dot(...), scalar )
        //    或: vecN varName = dot(...)
        // dot() / length() / distance() 始终返回标量，mix(scalar,scalar,scalar) 也返回标量
        // 修正: 将 vecN 改为 float
        content = Regex.Replace(content,
            @"\b(vec[234])\s+(\w+)(\s*=\s*)(mix\s*\(\s*\n?\s*dot\s*\()",
            "float $2$3$4",
            RegexOptions.Multiline);

        content = Regex.Replace(content,
            @"\b(vec[234])\s+(\w+)(\s*=\s*)(dot\s*\()",
            "float $2$3$4",
            RegexOptions.Multiline);

        content = Regex.Replace(content,
            @"\b(vec[234])\s+(\w+)(\s*=\s*)(length\s*\()",
            "float $2$3$4",
            RegexOptions.Multiline);

        content = Regex.Replace(content,
            @"\b(vec[234])\s+(\w+)(\s*=\s*)(distance\s*\()",
            "float $2$3$4",
            RegexOptions.Multiline);

        return content;
    }

    /// <summary>
    /// 修复vec4到vec2的赋值问题
    /// 例如: vec2 uv = v_Texcoord0; -> vec2 uv = v_Texcoord0.xy;
    /// </summary>
    private static string FixVec4ToVec2Assignments(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 模式1: vec2 varName = vec4Value;
        // 检测vec4类型的varying/uniform变量赋值给vec2
        var vec4Variables = new HashSet<string>();

        // 收集所有vec4类型的变量
        var vec4Matches = Regex.Matches(content, @"(?:varying|uniform|attribute)\s+vec4\s+(\w+)");
        foreach (Match match in vec4Matches)
        {
            vec4Variables.Add(match.Groups[1].Value);
        }

        // 也收集函数参数中的vec4
        var funcVec4Matches = Regex.Matches(content, @"(?:in|out|inout)\s+vec4\s+(\w+)");
        foreach (Match match in funcVec4Matches)
        {
            vec4Variables.Add(match.Groups[1].Value);
        }

        // 也收集局部vec4变量声明，防止name-based heuristic误将 vec4 变量赋值添加 .xy
        // 例如 "vec4 uv = v_Texcoord0;" 中 uv 是 vec4，不应添加 .xy
        var localVec4DeclMatches = Regex.Matches(content, @"^\s*vec4\s+(\w+)\s*[=;]", RegexOptions.Multiline);
        foreach (Match match in localVec4DeclMatches)
        {
            vec4Variables.Add(match.Groups[1].Value);
        }

        // 对每个vec4变量，在赋值给vec2时自动添加.xy
        foreach (var vec4Var in vec4Variables)
        {
            // 模式: vec2 xxx = vec4Var;
            content = Regex.Replace(
                content,
                $@"\bvec2\s+\w+\s*=\s*{vec4Var}(?![.\w])\s*;",
                m => m.Value.Replace($"{vec4Var};", $"{vec4Var}.xy;")
            );

            // 模式: someVec2 = vec4Var;
            content = Regex.Replace(
                content,
                $@"(\w+)\s*=\s*{vec4Var}(?![.\w])\s*;",
                m => {
                    string lhs = m.Groups[1].Value;
                    // LHS是已知的vec4变量则跳过（避免 vec4 uv = vec4Var → vec4 uv = vec4Var.xy 的错误）
                    if (vec4Variables.Contains(lhs))
                        return m.Value;
                    // 基于变量名的启发式判断：名字包含 uv/UV/coord/Coord 则视为 vec2
                    if (lhs.Contains("UV") || lhs.Contains("uv") || lhs.Contains("coord") || lhs.Contains("Coord"))
                    {
                        return m.Value.Replace($"{vec4Var};", $"{vec4Var}.xy;");
                    }
                    return m.Value;
                }
            );
        }

        return content;
    }

    /// <summary>
    /// 移除多余的vec2()构造函数
    /// 例如: vec2(vec2_expression) -> vec2_expression
    /// </summary>
    private static string RemoveRedundantVec2Constructors(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 检测并移除 vec2(vec2(...)) 这样的嵌套
        // 注意：只移除明显冗余的情况，保留有意义的类型转换

        // 模式: vec2(something.xy) 通常是多余的
        // 因为 .xy 已经返回 vec2 了
        content = Regex.Replace(
            content,
            @"vec2\s*\(\s*(\w+\.(xy|zw))\s*\)",
            "$1"
        );

        return content;
    }

    /// <summary>
    /// 修复函数参数类型不匹配
    /// 检测函数调用中vec4传给vec2参数的情况
    /// </summary>
    private static string FixFunctionParameterTypeMismatch(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 收集所有vec4类型的变量
        var vec4Variables = new HashSet<string>();
        var vec4Matches = Regex.Matches(content, @"(?:varying|uniform|attribute)\s+vec4\s+(\w+)");
        foreach (Match match in vec4Matches)
        {
            vec4Variables.Add(match.Groups[1].Value);
        }

        // 检测常见的需要vec2参数的函数
        var vec2Functions = new[] { "texture2D", "texture", "RotateUV", "TransformUV" };

        foreach (var func in vec2Functions)
        {
            foreach (var vec4Var in vec4Variables)
            {
                // 模式: function(tex, vec4Var) -> function(tex, vec4Var.xy)
                // 第二个参数通常是UV坐标，应该是vec2
                content = Regex.Replace(
                    content,
                    $@"({func}\s*\([^,]+,\s*){vec4Var}(?![.\w])",
                    $"$1{vec4Var}.xy"
                );
            }
        }

        return content;
    }

    /// <summary>
    /// 修复texture2D返回值在vec2算术运算中的类型不匹配
    /// 例如: vec2Var += texture2D(...) * strength -> vec2Var += texture2D(...).xy * strength
    /// </summary>
    private static string FixTexture2DInVec2Operations(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 收集所有vec2类型的变量
        var vec2Variables = new HashSet<string>();

        // 从声明中收集vec2变量（使用负向前瞻排除函数定义，避免将 randomVec 等函数名收集进来）
        // 例如 "vec2 randomVec(vec2 noiseuv)" 中 randomVec 是函数名，不是变量名
        var vec2Matches = Regex.Matches(content, @"vec2\s+(\w+)(?!\s*\()");
        foreach (Match match in vec2Matches)
        {
            vec2Variables.Add(match.Groups[1].Value);
        }

        // 从varying声明中收集vec2变量
        var varyingVec2Matches = Regex.Matches(content, @"varying\s+vec2\s+(\w+)");
        foreach (Match match in varyingVec2Matches)
        {
            vec2Variables.Add(match.Groups[1].Value);
        }

        ExportLogger.Log($"LayaAir3D: Found {vec2Variables.Count} vec2 variables for texture2D fix");

        // 修复每个vec2变量的texture2D赋值
        // ⭐ 使用改进的方法：逐行处理，使用更宽松的匹配来处理嵌套括号
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            foreach (var vec2Var in vec2Variables)
            {
                // 检测: vec2Var += texture2D(...) * 或 vec2Var = texture2D(...) *
                // 使用宽松匹配：从texture2D开始到行尾的分号
                if (Regex.IsMatch(line, $@"{vec2Var}\s*[+]?=\s*texture2D\s*\("))
                {
                    // 检查是否已经有.xy或.rg
                    if (!Regex.IsMatch(line, @"texture2D\s*\([^;]+\)\s*\.(xy|rg)"))
                    {
                        // 查找texture2D调用的结束位置（找到匹配的右括号）
                        int texture2DStart = line.IndexOf("texture2D");
                        if (texture2DStart >= 0)
                        {
                            int openParen = line.IndexOf('(', texture2DStart);
                            if (openParen >= 0)
                            {
                                int parenCount = 1;
                                int closeParen = openParen + 1;

                                // 找到匹配的右括号
                                while (closeParen < line.Length && parenCount > 0)
                                {
                                    if (line[closeParen] == '(') parenCount++;
                                    else if (line[closeParen] == ')') parenCount--;
                                    closeParen++;
                                }

                                if (parenCount == 0)
                                {
                                    // 找到了匹配的右括号，在后面插入.xy
                                    // 检查右括号后是否直接是.xy或.rg
                                    if (closeParen < line.Length && line[closeParen] != '.')
                                    {
                                        lines[i] = line.Insert(closeParen, ".xy");
                                        ExportLogger.Log($"LayaAir3D: Fixed texture2D in line (with nested parens): {vec2Var} += texture2D(...).xy");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        content = string.Join("\n", lines);

        ExportLogger.Log("LayaAir3D: Applied texture2D in vec2 operations fix");

        return content;
    }

    /// <summary>
    /// 修复 v_MeshColor 单通道使用（.r/.g/.b/.a）为完整 vec4 乘法
    /// HLSL→GLSL 机械转换可能产生 gl_FragColor *= v_MeshColor.r; 丢失 GBA 通道
    /// </summary>
    private static string FixMeshColorSingleChannel(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        string before = content;
        content = Regex.Replace(content,
            @"(gl_FragColor\s*\*=\s*v_MeshColor)\.[rgba]\s*;", "$1;");

        if (content != before)
            ExportLogger.Log("LayaAir3D: Fixed v_MeshColor single channel → full vec4 multiply");

        return content;
    }

    /// <summary>
    /// 警告 FS 中的调试 early-return（gl_FragColor = vec4(...); return;）
    /// 这种模式通常是调试遗留，导致 FS 不渲染后续内容
    /// </summary>
    private static void WarnDebugEarlyReturn(string content, string shaderName)
    {
        if (string.IsNullOrEmpty(content))
            return;

        // 找 FS 部分（在 #endif 之前的 main() 函数中）
        var matches = Regex.Matches(content,
            @"gl_FragColor\s*=\s*[^;]+;\s*\n\s*return\s*;",
            RegexOptions.Multiline);

        if (matches.Count == 0)
            return;

        // 检查每个匹配是否在 main() 的末尾（最后一个 return 除外）
        string[] lines = content.Split('\n');
        int totalLines = lines.Length;

        foreach (Match m in matches)
        {
            // 计算匹配位置所在行号
            int charPos = m.Index;
            int lineNum = content.Substring(0, charPos).Split('\n').Length;

            // 检查这个 return 后面是否还有实质代码（不是 } 或空行）
            bool hasCodeAfter = false;
            for (int i = lineNum; i < totalLines; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed == "}" || trimmed == "" || trimmed.StartsWith("//"))
                    continue;
                hasCodeAfter = true;
                break;
            }

            if (hasCodeAfter)
            {
                ExportLogger.Warning($"LayaAir3D: [{shaderName}] Possible debug early-return in FS at line {lineNum}: '{m.Value.Trim()}' — subsequent code will not execute");
            }
        }
    }

    /// <summary>
    /// 警告 VS 中同一 varying 被多次赋值（早期赋值被覆盖）
    /// </summary>
    private static void WarnDuplicateVaryingAssignments(string content, string shaderName)
    {
        if (string.IsNullOrEmpty(content))
            return;

        // 提取 VS 部分（attribute/varying 声明区域到 FS 之间）
        // 查找 VS main() 函数
        var vsMainMatch = Regex.Match(content,
            @"attribute\s+\w+\s+\w+;.*?void\s+main\s*\(\s*\)\s*\{",
            RegexOptions.Singleline);

        if (!vsMainMatch.Success)
            return;

        // 从 VS main() 开始到对应的 } 结束
        int mainStart = vsMainMatch.Index + vsMainMatch.Length;
        int braceDepth = 1;
        int mainEnd = mainStart;
        for (int i = mainStart; i < content.Length && braceDepth > 0; i++)
        {
            if (content[i] == '{') braceDepth++;
            else if (content[i] == '}') braceDepth--;
            if (braceDepth == 0) mainEnd = i;
        }

        string vsMain = content.Substring(mainStart, mainEnd - mainStart);

        // 收集 varying 赋值（v_Xxx = ... 或 v_Xxx.swizzle = ...）
        var assignments = Regex.Matches(vsMain, @"(v_\w+(?:\.\w+)?)\s*=\s*[^=]");
        var assignCounts = new Dictionary<string, int>();

        foreach (Match m in assignments)
        {
            string target = m.Groups[1].Value;
            if (!assignCounts.ContainsKey(target))
                assignCounts[target] = 0;
            assignCounts[target]++;
        }

        foreach (var kvp in assignCounts)
        {
            if (kvp.Value > 1)
            {
                ExportLogger.Warning($"LayaAir3D: [{shaderName}] VS varying '{kvp.Key}' is assigned {kvp.Value} times — earlier assignments will be overwritten");
            }
        }
    }

    /// <summary>
    /// 验证shader内容，检测潜在的类型不匹配问题
    /// 这个函数不修改内容，只输出警告
    /// </summary>
    private static void ValidateShaderContent(string content, string shaderName)
    {
        if (string.IsNullOrEmpty(content))
            return;

        var issues = new List<string>();

        // 检测1: 错误的swizzle访问
        if (Regex.IsMatch(content, @"\w+\.xy\.[zw]"))
        {
            var matches = Regex.Matches(content, @"(\w+\.xy\.[zw])");
            foreach (Match match in matches)
            {
                issues.Add($"Invalid swizzle access: {match.Value}");
            }
        }

        if (Regex.IsMatch(content, @"\w+\.[xyzw]\.[xyzw]"))
        {
            var matches = Regex.Matches(content, @"(\w+\.[xyzw]\.[xyzw])");
            foreach (Match match in matches)
            {
                issues.Add($"Invalid chained swizzle: {match.Value}");
            }
        }

        // 检测2: vec4变量可能未添加.xy的情况
        var vec4Variables = new HashSet<string>();
        var vec4Matches = Regex.Matches(content, @"(?:varying|uniform|attribute)\s+vec4\s+(\w+)");
        foreach (Match match in vec4Matches)
        {
            vec4Variables.Add(match.Groups[1].Value);
        }

        foreach (var vec4Var in vec4Variables)
        {
            // 检测: vec2 xxx = vec4Var; (没有.xy)
            if (Regex.IsMatch(content, $@"vec2\s+\w+\s*=\s*{vec4Var}(?![.\w])\s*;"))
            {
                issues.Add($"Possible type mismatch: vec2 assignment from {vec4Var} without .xy");
            }

            // 检测: texture2D(..., vec4Var) (没有.xy)
            if (Regex.IsMatch(content, $@"texture2D\s*\([^,]+,\s*{vec4Var}(?![.\w])\s*\)"))
            {
                issues.Add($"Possible type mismatch: texture2D UV parameter {vec4Var} without .xy");
            }
        }

        // 检测3: vec2构造函数接收vec4
        if (Regex.IsMatch(content, @"vec2\s*\(\s*vec4\s+"))
        {
            issues.Add("Possible issue: vec2() constructor with vec4 type");
        }

        // 检测4: texture2D在vec2算术运算中没有swizzle
        if (Regex.IsMatch(content, @"\w+\s*\+=\s*texture2D\s*\([^)]+\)(?!\.xy)(?!\.rg)\s*\*"))
        {
            var matches = Regex.Matches(content, @"(\w+\s*\+=\s*texture2D\s*\([^)]+\)(?!\.xy)(?!\.rg)\s*\*[^;]+;)");
            foreach (Match match in matches)
            {
                issues.Add($"Possible type mismatch: texture2D result in vec2 operation without .xy: {match.Value.Substring(0, Math.Min(60, match.Value.Length))}...");
            }
        }

        // 输出问题
        if (issues.Count > 0)
        {
            Debug.LogWarning($"LayaAir3D: Shader '{shaderName}' validation found {issues.Count} potential issue(s):");
            foreach (var issue in issues)
            {
                Debug.LogWarning($"  - {issue}");
            }
            Debug.LogWarning("  Note: These may have been auto-fixed. Check the exported shader if compilation fails.");
        }
        else
        {
            ExportLogger.Log($"LayaAir3D: Shader '{shaderName}' validation passed (no obvious type mismatches detected)");
        }
    }

    /// <summary>
    /// ⭐ 全面的类型检查和自动修复
    /// 检测并修复所有赋值中的类型不匹配问题
    /// </summary>
    private static string ComprehensiveTypeCheck(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // 第一步：收集所有变量及其类型
        var variableTypes = new Dictionary<string, string>();

        // 收集varying/uniform/attribute声明
        var declMatches = Regex.Matches(content, @"(varying|uniform|attribute)\s+(vec2|vec3|vec4|float|int|mat2|mat3|mat4|sampler2D|samplerCube)\s+(\w+)");
        foreach (Match match in declMatches)
        {
            string varType = match.Groups[2].Value;
            string varName = match.Groups[3].Value;
            variableTypes[varName] = varType;
        }

        // 收集局部变量声明
        var localVarMatches = Regex.Matches(content, @"^\s*(vec2|vec3|vec4|float|int|mat2|mat3|mat4)\s+(\w+)\s*[=;]", RegexOptions.Multiline);
        foreach (Match match in localVarMatches)
        {
            string varType = match.Groups[1].Value;
            string varName = match.Groups[2].Value;
            if (!variableTypes.ContainsKey(varName))
            {
                variableTypes[varName] = varType;
            }
        }

        ExportLogger.Log($"LayaAir3D: ComprehensiveTypeCheck - Found {variableTypes.Count} variables");

        int fixCount = 0;

        // 第二步：修复vec4到vec2/vec3的赋值
        foreach (var kvp in variableTypes)
        {
            string varName = kvp.Key;
            string varType = kvp.Value;

            // 情况1: vec2 = vec4变量 (没有swizzle)
            if (varType == "vec4")
            {
                // vec2 xxx = vec4Var;
                var pattern1 = $@"(vec2\s+\w+\s*=\s*)({varName})(?![.\w])(\s*;)";
                if (Regex.IsMatch(content, pattern1))
                {
                    content = Regex.Replace(content, pattern1, "$1$2.xy$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed vec2 = {varName} → vec2 = {varName}.xy");
                }

                // vec2Var = vec4Var;
                foreach (var targetVar in variableTypes)
                {
                    if (targetVar.Value == "vec2")
                    {
                        var pattern2 = $@"({targetVar.Key}\s*=\s*)({varName})(?![.\w])(\s*;)";
                        if (Regex.IsMatch(content, pattern2))
                        {
                            content = Regex.Replace(content, pattern2, "$1$2.xy$3");
                            fixCount++;
                            ExportLogger.Log($"LayaAir3D: Fixed {targetVar.Key} = {varName} → {targetVar.Key} = {varName}.xy");
                        }
                    }
                    else if (targetVar.Value == "vec3")
                    {
                        var pattern3 = $@"({targetVar.Key}\s*=\s*)({varName})(?![.\w])(\s*;)";
                        if (Regex.IsMatch(content, pattern3))
                        {
                            content = Regex.Replace(content, pattern3, "$1$2.xyz$3");
                            fixCount++;
                            ExportLogger.Log($"LayaAir3D: Fixed {targetVar.Key} = {varName} → {targetVar.Key} = {varName}.xyz");
                        }
                    }
                }
            }

            // 情况2: vec3 = vec4变量
            if (varType == "vec4")
            {
                // vec3 xxx = vec4Var;
                var pattern4 = $@"(vec3\s+\w+\s*=\s*)({varName})(?![.\w])(\s*;)";
                if (Regex.IsMatch(content, pattern4))
                {
                    content = Regex.Replace(content, pattern4, "$1$2.xyz$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed vec3 = {varName} → vec3 = {varName}.xyz");
                }
            }

            // 情况3: vec3 = vec2变量 (需要扩展)
            if (varType == "vec2")
            {
                foreach (var targetVar in variableTypes)
                {
                    if (targetVar.Value == "vec3")
                    {
                        var pattern5 = $@"({targetVar.Key}\s*=\s*)({varName})(?![.\w])(\s*;)";
                        if (Regex.IsMatch(content, pattern5))
                        {
                            // vec3不能直接从vec2转换，需要用户手动修复，这里只记录
                            Debug.LogWarning($"LayaAir3D: Type mismatch detected: {targetVar.Key} (vec3) = {varName} (vec2) - Manual fix may be needed");
                        }
                    }
                    else if (targetVar.Value == "vec4")
                    {
                        var pattern6 = $@"({targetVar.Key}\s*=\s*)({varName})(?![.\w])(\s*;)";
                        if (Regex.IsMatch(content, pattern6))
                        {
                            // vec4不能直接从vec2转换，需要用户手动修复
                            Debug.LogWarning($"LayaAir3D: Type mismatch detected: {targetVar.Key} (vec4) = {varName} (vec2) - Manual fix may be needed");
                        }
                    }
                }
            }
        }

        // 第三步：修复texture2D作为UV参数（应该是vec2）
        foreach (var kvp in variableTypes)
        {
            if (kvp.Value == "vec4")
            {
                // texture2D(sampler, vec4Var) → texture2D(sampler, vec4Var.xy)
                var pattern7 = $@"(texture2D\s*\([^,]+,\s*)({kvp.Key})(?![.\w])(\s*\))";
                if (Regex.IsMatch(content, pattern7))
                {
                    content = Regex.Replace(content, pattern7, "$1$2.xy$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed texture2D UV parameter: {kvp.Key} → {kvp.Key}.xy");
                }
            }
        }

        // 第四步：修复函数参数中的类型不匹配
        // 常见函数签名：fract(vec2), mix(vec3, vec3, float), etc.
        var commonVec2Functions = new[] { "fract", "floor", "ceil", "abs", "normalize", "length" };
        foreach (var func in commonVec2Functions)
        {
            foreach (var kvp in variableTypes)
            {
                if (kvp.Value == "vec4")
                {
                    // func(vec4Var) where func expects vec2
                    var pattern8 = $@"({func}\s*\()({kvp.Key})(?![.\w])(\s*\))";
                    var matches = Regex.Matches(content, pattern8);
                    if (matches.Count > 0)
                    {
                        // 这个需要上下文判断，先记录
                        Debug.LogWarning($"LayaAir3D: Potential type mismatch in {func}({kvp.Key}) - vec4 may need swizzle");
                    }
                }
            }
        }

        // 第五步：修复构造函数中的类型不匹配
        // vec2(vec4Var) → vec2(vec4Var.xy)
        foreach (var kvp in variableTypes)
        {
            if (kvp.Value == "vec4")
            {
                var pattern9 = $@"(vec2\s*\()({kvp.Key})(?![.\w])(\s*\))";
                if (Regex.IsMatch(content, pattern9))
                {
                    content = Regex.Replace(content, pattern9, "$1$2.xy$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed vec2 constructor: vec2({kvp.Key}) → vec2({kvp.Key}.xy)");
                }

                var pattern10 = $@"(vec3\s*\()({kvp.Key})(?![.\w])(\s*\))";
                if (Regex.IsMatch(content, pattern10))
                {
                    content = Regex.Replace(content, pattern10, "$1$2.xyz$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed vec3 constructor: vec3({kvp.Key}) → vec3({kvp.Key}.xyz)");
                }
            }
            else if (kvp.Value == "vec3")
            {
                var pattern11 = $@"(vec2\s*\()({kvp.Key})(?![.\w])(\s*\))";
                if (Regex.IsMatch(content, pattern11))
                {
                    content = Regex.Replace(content, pattern11, "$1$2.xy$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed vec2 constructor: vec2({kvp.Key}) → vec2({kvp.Key}.xy)");
                }
            }
        }

        // 第六步：修复texture2D在复杂表达式中的类型不匹配
        // 模式: vec2Var += texture2D(...) * scalar (没有.xy)
        // 这个比FixTexture2DInVec2Operations更强大，能处理复杂的嵌套括号
        var vec2Vars = variableTypes.Where(kvp => kvp.Value == "vec2").Select(kvp => kvp.Key).ToList();

        foreach (var vec2Var in vec2Vars)
        {
            // 使用更宽松的正则，匹配texture2D(...) * 但没有.xy/.rg的情况
            // 查找: vec2Var += texture2D(任意内容) * 任意内容;
            // 确保texture2D后面不是 .xy 或 .rg
            var pattern12 = $@"({vec2Var}\s*\+=\s*texture2D\s*\([^;]*?\))(?!\.xy)(?!\.rg)(\s*\*\s*[^;]+;)";
            var matches = Regex.Matches(content, pattern12);

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string original = match.Value;
                    // 在texture2D的右括号后插入.xy
                    string fixed_str = match.Groups[1].Value + ".xy" + match.Groups[2].Value;
                    content = content.Replace(original, fixed_str);
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed texture2D in complex expression: {vec2Var} += texture2D(...).xy * ...");
                }
            }

            // 同样处理 = 赋值
            var pattern13 = $@"({vec2Var}\s*=\s*texture2D\s*\([^;]*?\))(?!\.xy)(?!\.rg)(\s*\*\s*[^;]+;)";
            matches = Regex.Matches(content, pattern13);

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string original = match.Value;
                    string fixed_str = match.Groups[1].Value + ".xy" + match.Groups[2].Value;
                    content = content.Replace(original, fixed_str);
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed texture2D in complex expression: {vec2Var} = texture2D(...).xy * ...");
                }
            }
        }

        // 第七步：检测可疑的变量名使用
        // 如果v_ScreenPos存在，但代码中使用了v_Texcoord5.xy / v_Texcoord5.w的模式，可能是错误
        if (variableTypes.ContainsKey("v_ScreenPos") && variableTypes.ContainsKey("v_Texcoord5"))
        {
            // 检测: xxx = v_Texcoord5.xy / v_Texcoord5.w
            if (Regex.IsMatch(content, @"\w+\s*=\s*v_Texcoord5\.xy\s*/\s*v_Texcoord5\.w"))
            {
                Debug.LogWarning($"LayaAir3D: Suspicious pattern detected: 'v_Texcoord5.xy / v_Texcoord5.w' - Should this be 'v_ScreenPos.xy / v_ScreenPos.w'?");

                // 自动修复：如果是计算screenUV的模式，替换为v_ScreenPos
                var pattern14 = @"(vec2\s+screenUV\s*=\s*)v_Texcoord5(\.xy\s*/\s*)v_Texcoord5(\.w\s*;)";
                if (Regex.IsMatch(content, pattern14))
                {
                    content = Regex.Replace(content, pattern14, "$1v_ScreenPos$2v_ScreenPos$3");
                    fixCount++;
                    ExportLogger.Log($"LayaAir3D: Fixed screenUV calculation: v_Texcoord5 → v_ScreenPos");
                }
            }
        }

        if (fixCount > 0)
        {
            ExportLogger.Log($"LayaAir3D: ComprehensiveTypeCheck applied {fixCount} automatic type fixes");
        }
        else
        {
            ExportLogger.Log($"LayaAir3D: ComprehensiveTypeCheck - No type fixes needed");
        }

        return content;
    }

    enum ShaderPartType { Header, Shader3D, GLSL }

    class ShaderPart
    {
        public ShaderPartType type;
        public string content;
    }

    /// <summary>
    /// 分离shader为不同的部分
    /// </summary>
    private static List<ShaderPart> SplitShaderIntoParts(string content)
    {
        var parts = new List<ShaderPart>();
        var lines = content.Split('\n');
        var currentPart = new StringBuilder();
        ShaderPartType currentType = ShaderPartType.Header;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed == "Shader3D Start")
            {
                if (currentPart.Length > 0)
                {
                    parts.Add(new ShaderPart { type = currentType, content = currentPart.ToString().Trim() });
                    currentPart.Clear();
                }
                currentType = ShaderPartType.Shader3D;
                currentPart.AppendLine(line);
            }
            else if (trimmed == "Shader3D End")
            {
                currentPart.AppendLine(line);
                parts.Add(new ShaderPart { type = currentType, content = currentPart.ToString().Trim() });
                currentPart.Clear();
                currentType = ShaderPartType.Header;
            }
            else if (trimmed == "GLSL Start")
            {
                if (currentPart.Length > 0)
                {
                    parts.Add(new ShaderPart { type = currentType, content = currentPart.ToString().Trim() });
                    currentPart.Clear();
                }
                currentType = ShaderPartType.GLSL;
                currentPart.AppendLine(line);
            }
            else if (trimmed == "GLSL End")
            {
                currentPart.AppendLine(line);
                parts.Add(new ShaderPart { type = currentType, content = currentPart.ToString().Trim() });
                currentPart.Clear();
                currentType = ShaderPartType.Header;
            }
            else
            {
                currentPart.AppendLine(line);
            }
        }

        if (currentPart.Length > 0)
        {
            parts.Add(new ShaderPart { type = currentType, content = currentPart.ToString().Trim() });
        }

        return parts;
    }

    /// <summary>
    /// 格式化Shader3D配置块
    /// </summary>
    private static string FormatShader3DBlock(string content)
    {
        var lines = content.Split('\n');
        var result = new StringBuilder();
        int indentLevel = 0;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed == "Shader3D Start" || trimmed == "Shader3D End")
            {
                result.AppendLine(trimmed);
            }
            else if (trimmed == "{")
            {
                result.AppendLine(trimmed);
                indentLevel++;
            }
            else if (trimmed == "}" || trimmed == "},")
            {
                indentLevel = Math.Max(0, indentLevel - 1); // 确保不会变成负数
                result.AppendLine(new string(' ', indentLevel * 4) + trimmed);
            }
            else
            {
                result.AppendLine(new string(' ', Math.Max(0, indentLevel) * 4) + trimmed); // 确保不会变成负数
            }
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 格式化GLSL代码块
    /// </summary>
    private static string FormatGLSLBlock(string content)
    {
        var lines = content.Split('\n');
        var result = new StringBuilder();
        int indentLevel = 0;
        string previousLine = "";
        bool needsBlankLine = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // 保留空行，但不连续
                if (!string.IsNullOrWhiteSpace(previousLine))
                {
                    result.AppendLine();
                    previousLine = "";
                }
                continue;
            }

            // GLSL Start/End 顶格
            if (trimmed == "GLSL Start" || trimmed == "GLSL End")
            {
                if (needsBlankLine && result.Length > 0)
                    result.AppendLine();
                result.AppendLine(trimmed);
                previousLine = trimmed;
                needsBlankLine = false;
                continue;
            }

            // 预处理指令顶格
            if (trimmed.StartsWith("#"))
            {
                // 在#define SHADER_NAME之前加空行（新函数开始）
                if (trimmed.StartsWith("#define SHADER_NAME") && !string.IsNullOrWhiteSpace(previousLine))
                {
                    result.AppendLine();
                }
                result.AppendLine(trimmed);
                previousLine = trimmed;
                needsBlankLine = false;
                continue;
            }

            // 计算缩进
            if (trimmed.EndsWith("{"))
            {
                result.AppendLine(new string(' ', Math.Max(0, indentLevel) * 4) + trimmed);
                indentLevel++;
                needsBlankLine = false;
            }
            else if (trimmed.StartsWith("}"))
            {
                indentLevel = Math.Max(0, indentLevel - 1); // 确保不会变成负数
                result.AppendLine(new string(' ', indentLevel * 4) + trimmed);
                needsBlankLine = true; // 函数结束后需要空行
            }
            else
            {
                result.AppendLine(new string(' ', Math.Max(0, indentLevel) * 4) + trimmed);
                needsBlankLine = false;
            }

            previousLine = trimmed;
        }

        return result.ToString().TrimEnd();
    }
    
    /// <summary>
    /// 在每个shader块内移除重复的varying声明
    /// VS和FS是独立的块，各自需要声明相同的varying
    /// </summary>
    private static string RemoveDuplicateVaryingsInShaderBlocks(string code)
    {
        // 匹配 #defineGLSL ... #endGLSL 块
        return Regex.Replace(code, @"(#defineGLSL\s+\w+)([\s\S]*?)(#endGLSL)", (match) =>
        {
            string header = match.Groups[1].Value;
            string body = match.Groups[2].Value;
            string footer = match.Groups[3].Value;
            
            // 在这个块内移除重复的varying声明
            HashSet<string> seenVaryings = new HashSet<string>();
            body = Regex.Replace(body, @"varying\s+(vec2|vec3|vec4|float|int|mat2|mat3|mat4)\s+(\w+)\s*;", (varyingMatch) =>
            {
                string varyingName = varyingMatch.Groups[2].Value;
                if (seenVaryings.Contains(varyingName))
                {
                    return ""; // 移除块内重复的声明
                }
                else
                {
                    seenVaryings.Add(varyingName);
                    return varyingMatch.Value; // 保留第一个
                }
            });
            
            return header + body + footer;
        });
    }

    #endregion

    /// <summary>
    /// Shader属性信息
    /// </summary>
    private class ShaderProperty
    {
        public string unityName;
        public string layaName;
        public ShaderUtil.ShaderPropertyType type;
        public string define;  // 纹理关联的宏定义
        public bool isNormal;  // 是否是法线贴图
        public bool isCubemap; // 是否是Cubemap
        public bool isHDR;    // 是否是HDR颜色（[HDR]标记）
        public float rangeMin; // Range类型的最小值
        public float rangeMax; // Range类型的最大值
        public float defaultFloat; // Float默认值
        public int defaultInt;     // Int默认值
        public Vector4 defaultVector; // Vector默认值
        public Color defaultColor; // Color默认值
        public string description; // 属性描述
    }

    /// <summary>
    /// 收集Shader属性
    /// </summary>
    private static List<ShaderProperty> CollectShaderProperties(Shader shader)
    {
        List<ShaderProperty> properties = new List<ShaderProperty>();
        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        // ⭐ 创建临时材质以读取 Unity Properties 的实际默认值
        Material tempMat = null;
        try
        {
            tempMat = new Material(shader);
        }
        catch (System.Exception e)
        {
            ExportLogger.Warning($"LayaAir3D: Failed to create temp material for default values: {e.Message}");
        }

        for (int i = 0; i < propertyCount; i++)
        {
            string propName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);

            // 跳过内部属性
            if (IsInternalProperty(propName))
                continue;

            ShaderProperty prop = new ShaderProperty();
            prop.unityName = propName;
            prop.layaName = ConvertToLayaPropertyName(propName);
            prop.type = propType;
            prop.description = ShaderUtil.GetPropertyDescription(shader, i);
            prop.isNormal = propName.ToLower().Contains("normal") || propName.ToLower().Contains("bump");
            prop.isCubemap = propName.ToLower().Contains("ibl") || propName.ToLower().Contains("cube") ||
                            prop.description.ToLower().Contains("cube");

            // 检测HDR颜色属性：Unity [HDR] 标记的Color属性，值可超过1.0，Laya需用Vector4类型
            if (propType == ShaderUtil.ShaderPropertyType.Color)
            {
                try
                {
                    // Unity 2019.4+ API: shader.GetPropertyFlags 返回 ShaderPropertyFlags 枚举
                    var flags = shader.GetPropertyFlags(i);
                    prop.isHDR = (flags & UnityEngine.Rendering.ShaderPropertyFlags.HDR) != 0;
                }
                catch (System.Exception)
                {
                    // 回退：从属性描述中推断
                    prop.isHDR = prop.description.Contains("HDR") || prop.description.Contains("hdr");
                }
            }

            // 获取Range属性的范围和默认值
            if (propType == ShaderUtil.ShaderPropertyType.Range)
            {
                prop.rangeMin = ShaderUtil.GetRangeLimits(shader, i, 1);
                prop.rangeMax = ShaderUtil.GetRangeLimits(shader, i, 2);
                prop.defaultFloat = ShaderUtil.GetRangeLimits(shader, i, 0); // 0 = default value
            }

            // ⭐ 从临时材质读取实际默认值
            if (tempMat != null)
            {
                try
                {
                    switch (propType)
                    {
                        case ShaderUtil.ShaderPropertyType.Float:
                            prop.defaultFloat = tempMat.GetFloat(propName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Int:
                            prop.defaultInt = tempMat.GetInt(propName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Color:
                            prop.defaultColor = tempMat.GetColor(propName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            prop.defaultVector = tempMat.GetVector(propName);
                            break;
                    }
                }
                catch (System.Exception)
                {
                    // 某些属性可能无法读取，忽略
                }
            }

            // 纹理属性生成宏定义
            if (propType == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                prop.define = GenerateTextureDefine(propName);

                // 检查纹理维度判断是否是Cubemap
                var texDim = ShaderUtil.GetTexDim(shader, i);
                if (texDim == UnityEngine.Rendering.TextureDimension.Cube)
                {
                    prop.isCubemap = true;
                }
            }

            properties.Add(prop);
        }

        // 清理临时材质
        if (tempMat != null)
        {
            UnityEngine.Object.DestroyImmediate(tempMat);
        }

        return properties;
    }

    /// <summary>
    /// 检查是否是内部属性（渲染状态相关，不需要导出）
    /// </summary>
    private static bool IsInternalProperty(string propName)
    {
        // 渲染状态属性
        if (propName.StartsWith("_Src") || propName.StartsWith("_Dst"))
            return true;
            
        // 常见内部属性列表
        string[] internalProps = new string[]
        {
            "_ZWrite", "_ZWriteAnimatable", "_ZTest", "_Cull", "_CullMode",
            "_Mode", "_Surface", "_SurfaceType", "_Blend", "_BlendMode",
            "_AlphaClip", "_QueueOffset", "_RenderQueueOffset",
            "_Foldout", "_FoldoutSurface", "_FoldoutSurfaceEnd", "_FoldoutAdvance",
            "_SplitLine", "_Header", "_Space"
        };
        
        foreach (var internalProp in internalProps)
        {
            if (propName == internalProp || propName.StartsWith("_Foldout"))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// 转换为LayaAir属性名
    /// </summary>
    // 主纹理名称集合：这些纹理的 _ST 对应 u_TilingOffset（Laya粒子/特效约定）
    private static readonly HashSet<string> MainTextureNames = new HashSet<string>
    {
        "_MainTex", "_BaseMap", "_AlbedoTexture", "_BaseColorMap"
    };

    private static string ConvertToLayaPropertyName(string unityName)
    {
        string layaName;

        // ① 当有模板上下文时，优先查覆盖表（处理通用映射结果与模板变量名不符的情况）
        if (_currentTemplatePropertyOverrides != null &&
            _currentTemplatePropertyOverrides.TryGetValue(unityName, out string overrideName))
        {
            // 覆盖名字直接来自 TemplatePropertyOverrides，无需二次验证
            return overrideName;
        }

        // ② 使用预定义映射表
        if (PropertyNameMappings.TryGetValue(unityName, out layaName))
        {
            return ValidateAgainstTemplate(layaName);
        }

        // ③ _ST 后缀处理：区分主纹理和其他纹理
        // 主纹理(_MainTex_ST / _BaseMap_ST / _AlbedoTexture_ST) → u_TilingOffset
        // 其他纹理(_Mask_ST / _DetailTex_ST / ...) → u_Mask_ST / u_DetailTex_ST / ...
        if (unityName.EndsWith("_ST"))
        {
            string baseName = unityName.Substring(0, unityName.Length - 3); // 去掉 "_ST"
            if (MainTextureNames.Contains(baseName))
                layaName = "u_TilingOffset";
            else
            {
                string texName = baseName.TrimStart('_');
                layaName = "u_" + texName + "_ST";
            }
            return ValidateAgainstTemplate(layaName);
        }

        // ④ 默认转换：移除下划线前缀，添加u_前缀
        string name = unityName.TrimStart('_');
        layaName = "u_" + name;
        return ValidateAgainstTemplate(layaName);
    }

    /// <summary>
    /// 在有模板上下文时，验证 layaName 是否存在于模板 uniformMap 变量集合中。
    /// - 精确匹配：直接返回
    /// - 大小写不敏感匹配：返回模板中的精确大小写名称
    /// - 无匹配：返回 null（表示此属性不在模板中，应跳过导出）
    /// 无模板上下文时直接返回原名。
    /// </summary>
    private static string ValidateAgainstTemplate(string layaName)
    {
        if (_currentTemplateVarNames == null)
            return layaName; // 无模板上下文，直接使用

        // ① 精确匹配
        if (_currentTemplateVarNames.Contains(layaName))
            return layaName;

        // ② 大小写不敏感匹配（使用模板中的精确大小写）
        foreach (string templateVar in _currentTemplateVarNames)
        {
            if (string.Equals(templateVar, layaName, System.StringComparison.OrdinalIgnoreCase))
                return templateVar;
        }

        // ③ 去掉 u_ 前缀后再匹配（处理模板中不含 u_ 前缀的属性，如 LayerType、WrapMode）
        // 默认规则会对所有属性加 u_ 前缀，但模板里的枚举/控制型属性可能直接用原名
        if (layaName.StartsWith("u_", System.StringComparison.OrdinalIgnoreCase))
        {
            string stripped = layaName.Substring(2);
            foreach (string templateVar in _currentTemplateVarNames)
            {
                if (string.Equals(templateVar, stripped, System.StringComparison.OrdinalIgnoreCase))
                    return templateVar;
            }
        }

        // 不在模板中 → 跳过此属性
        return null;
    }

    /// <summary>
    /// 生成纹理宏定义
    /// </summary>
    private static string GenerateTextureDefine(string propName)
    {
        // 使用预定义映射表
        if (TextureDefineMappings.ContainsKey(propName))
        {
            return TextureDefineMappings[propName];
        }
        
        // 默认：转换为大写并添加后缀
        string name = propName.TrimStart('_').ToUpper();
        // 如果不以MAP/TEXTURE结尾，添加MAP后缀
        if (!name.EndsWith("MAP") && !name.EndsWith("TEXTURE") && !name.EndsWith("TEX"))
        {
            name += "MAP";
        }
        return name;
    }

    /// <summary>
    /// 将Unity Keyword转换为Laya Define
    /// 规则：去掉前缀 _ 和后缀 _ON
    /// 示例：_LAYERTYPE_THREE → LAYERTYPE_THREE, _USEDISTORT0_ON → USEDISTORT0
    /// </summary>
    private static string ConvertKeywordToDefine(string unityKeyword)
    {
        if (string.IsNullOrEmpty(unityKeyword))
            return null;

        // 去掉前缀 _
        string define = unityKeyword.TrimStart('_');

        // 去掉后缀 _ON（如果有）
        if (define.EndsWith("_ON"))
        {
            define = define.Substring(0, define.Length - 3);
        }

        // 如果结果为空，返回null
        if (string.IsNullOrEmpty(define))
            return null;

        return define;
    }

    /// <summary>
    /// 检测Shader是否包含NPR特性（卡通渲染）
    /// </summary>
    private static bool HasNPRFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            // 检测NPR相关属性
            if (prop.unityName.Contains("Med") || prop.unityName.Contains("Shadow") ||
                prop.unityName.Contains("Reflect") || prop.unityName.Contains("GI") ||
                prop.unityName.Contains("Stylized") || prop.unityName.Contains("Toon"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含IBL特性
    /// </summary>
    private static bool HasIBLFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("IBL"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含Matcap特性
    /// </summary>
    private static bool HasMatcapFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("Matcap"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含Fresnel特性
    /// </summary>
    private static bool HasFresnelFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("Fresnel") || prop.unityName.Contains("fresnel"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含Rim边缘光特性
    /// </summary>
    private static bool HasRimFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("Rim") && !prop.unityName.Contains("Remap"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含HSV调整特性
    /// </summary>
    private static bool HasHSVFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("HSV") || prop.unityName.Contains("Hue") ||
                prop.unityName.Contains("Saturation"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测Shader是否包含Tonemapping特性
    /// </summary>
    private static bool HasTonemappingFeatures(List<ShaderProperty> properties)
    {
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("Tone") || prop.unityName.Contains("WhitePoint"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 根据Unity Shader名称检测LayaAir材质类型
    /// 参考 MetarialPropData.json 中的映射关系
    /// 
    /// 注意：这里只检测"内置"或"简单"的材质类型
    /// 复杂的自定义Shader（如Artist_Effect等）会被归类为Custom
    /// </summary>
    private static LayaMaterialType DetectMaterialType(string shaderName)
    {
        string lowerName = shaderName.ToLower();
        
        // ============================================
        // 1. 首先检测是否是LayaAir内置的简单粒子材质
        // 这些是MetarialPropData.json中定义的标准粒子Shader
        // ============================================
        
        // Laya内置粒子/特效材质 - 这些是简单的粒子材质
        if (lowerName == "laya/legacy/particle" ||
            lowerName == "laya/legacy/effect" ||
            lowerName == "laya/legacy/trail")
        {
            return LayaMaterialType.PARTICLESHURIKEN;
        }
        
        // Unity内置粒子材质 - 这些也是简单的粒子材质
        // 注意：必须精确匹配或以"particles/"开头
        if (lowerName.StartsWith("particles/") ||
            lowerName.StartsWith("mobile/particles/") ||
            lowerName.StartsWith("legacy shaders/particles/") ||
            lowerName.StartsWith("universal render pipeline/particles/"))
        {
            return LayaMaterialType.PARTICLESHURIKEN;
        }
        
        // ============================================
        // 2. 天空盒材质
        // ============================================
        if (lowerName.StartsWith("skybox/") || lowerName == "skybox")
        {
            if (lowerName.Contains("procedural"))
                return LayaMaterialType.SkyProcedural;
            if (lowerName.Contains("panoramic"))
                return LayaMaterialType.SkyPanoramic;
            return LayaMaterialType.SkyBox;
        }
        
        // Laya内置天空盒
        if (lowerName == "laya/legacy/sky box")
        {
            return LayaMaterialType.SkyBox;
        }
        
        // ============================================
        // 3. Unlit材质
        // ============================================
        if (lowerName.StartsWith("unlit/") || 
            lowerName == "laya/unlit" ||
            lowerName.StartsWith("universal render pipeline/unlit"))
        {
            return LayaMaterialType.Unlit;
        }
        
        // ============================================
        // 4. BlinnPhong材质
        // ============================================
        if (lowerName == "laya/blinnphong" ||
            lowerName.StartsWith("legacy shaders/diffuse") ||
            lowerName.StartsWith("legacy shaders/bumped") ||
            lowerName.StartsWith("mobile/diffuse") ||
            lowerName.StartsWith("mobile/bumped") ||
            lowerName.StartsWith("mobile/vertexlit"))
        {
            return LayaMaterialType.BLINNPHONG;
        }
        
        // ============================================
        // 5. PBR材质 - Standard, URP/Lit, HDRP/Lit等
        // ============================================
        if (lowerName == "standard" || 
            lowerName == "standard (specular setup)" ||
            lowerName == "universal render pipeline/lit" ||
            lowerName == "hdrp/lit")
        {
            return LayaMaterialType.PBR;
        }
        
        // ============================================
        // 6. 其他所有自定义Shader
        // 包括复杂的特效Shader（如Artist_Effect等）
        // 这些需要完整的自定义转换
        // ============================================
        return LayaMaterialType.Custom;
    }

    /// <summary>
    /// 根据材质类型确定ShaderType
    /// </summary>
    private static LayaShaderType GetShaderTypeFromMaterialType(LayaMaterialType materialType)
    {
        switch (materialType)
        {
            case LayaMaterialType.PARTICLESHURIKEN:
                return LayaShaderType.Effect;  // 粒子使用Effect类型
            case LayaMaterialType.SkyBox:
            case LayaMaterialType.SkyProcedural:
            case LayaMaterialType.SkyPanoramic:
                return LayaShaderType.Sky;     // 天空盒使用Sky类型
            case LayaMaterialType.PBR:
            case LayaMaterialType.BLINNPHONG:
            case LayaMaterialType.Unlit:
                return LayaShaderType.D3;      // 标准3D材质使用D3类型
            case LayaMaterialType.Custom:
            default:
                return LayaShaderType.D3;      // 自定义材质默认使用D3类型
        }
    }

    /// <summary>
    /// 根据Shader名称和属性进一步确定自定义Shader的ShaderType
    /// 用于Custom类型的Shader
    ///
    /// ⭐ 重要：只有真正的粒子系统shader才应该使用LayaShaderType.Effect
    /// Mesh特效shader（虽然名称包含effect但用于MeshRenderer）应该使用LayaShaderType.D3
    /// </summary>
    private static LayaShaderType DetectCustomShaderType(string shaderName, List<ShaderProperty> properties)
    {
        string lowerName = shaderName.ToLower();

        // ⭐ 修复：只有真正的粒子系统shader才使用Effect类型
        // Mesh特效shader虽然名称包含"effect"，但应该使用D3类型
        // 注意：这里只做初步判断，最终由IsParticleShader确认
        bool mightBeParticle = lowerName.Contains("particle") ||  // 明确的粒子关键字
                               lowerName.Contains("shurike") ||   // 粒子系统名称
                               lowerName.Contains("trail");        // 拖尾粒子

        // 如果可能是粒子，返回Effect类型
        // 但最终是否使用粒子attributeMap由IsParticleShader决定
        if (mightBeParticle)
        {
            return LayaShaderType.Effect;
        }

        // ⭐ Mesh特效shader（包含effect/fx/vfx/additive但不是粒子系统）使用D3类型
        // 例如：BR_Effect_Mask_Additive, Effect_Basic_Additive等
        // 这些shader用于MeshRenderer，应该使用标准的Mesh attributeMap
        if (lowerName.Contains("effect") ||
            lowerName.Contains("fx") ||
            lowerName.Contains("vfx") ||
            lowerName.Contains("additive") ||
            lowerName.Contains("_add"))
        {
            ExportLogger.Log($"LayaAir3D: Detected Mesh Effect shader (not particle): {shaderName} -> ShaderType: D3");
            return LayaShaderType.D3;  // ⭐ 使用D3而不是Effect
        }

        // 检测是否是后处理Shader
        if (lowerName.Contains("postprocess") ||
            lowerName.Contains("post process") ||
            lowerName.Contains("bloom") ||
            lowerName.Contains("blur") ||
            lowerName.Contains("tonemapping") ||
            lowerName.Contains("colorgrading") ||
            lowerName.Contains("vignette") ||
            lowerName.Contains("dof") ||
            lowerName.Contains("depth of field") ||
            lowerName.Contains("ssao") ||
            lowerName.Contains("screen space"))
        {
            return LayaShaderType.PostProcess;
        }

        // 检测是否是2D Shader
        if (lowerName.Contains("2d") ||
            lowerName.Contains("sprite") ||
            lowerName.Contains("ui") ||
            lowerName.Contains("canvas"))
        {
            return LayaShaderType.D2_BaseRenderNode2D;
        }

        // 默认为3D Shader
        return LayaShaderType.D3;
    }

    /// <summary>
    /// 根据Unity Shader名称和属性检测LayaAir ShaderType
    /// </summary>
    /// <param name="shaderName">Unity Shader名称</param>
    /// <param name="properties">Shader属性列表</param>
    /// <returns>LayaAir Shader类型</returns>
    private static LayaShaderType DetectShaderType(string shaderName, List<ShaderProperty> properties)
    {
        // 先检测材质类型，再根据材质类型确定ShaderType
        LayaMaterialType materialType = DetectMaterialType(shaderName);
        return GetShaderTypeFromMaterialType(materialType);
    }

    /// <summary>
    /// 获取ShaderType的字符串表示
    /// </summary>
    private static string GetShaderTypeString(LayaShaderType shaderType)
    {
        switch (shaderType)
        {
            case LayaShaderType.None: return "None";
            case LayaShaderType.Default: return "Default";
            case LayaShaderType.D3: return "D3";
            case LayaShaderType.D2_primitive: return "D2_primitive";
            case LayaShaderType.D2_TextureSV: return "D2_TextureSV";
            case LayaShaderType.D2_BaseRenderNode2D: return "D2_BaseRenderNode2D";
            case LayaShaderType.PostProcess: return "PostProcess";
            case LayaShaderType.Sky: return "Sky";
            case LayaShaderType.Effect: return "Effect";
            default: return "D3";
        }
    }

    /// <summary>
    /// 生成Shader文件内容 - 根据材质类型生成不同的Shader
    /// </summary>
    /// <param name="shaderName">LayaAir Shader名称（转换后的）</param>
    /// <param name="properties">Shader属性列表</param>
    /// <param name="unityShaderName">Unity原始Shader名称（用于类型检测）</param>
    /// <param name="materialFile">材质文件（用于检测实际使用的渲染器类型）</param>
    private static string GenerateShaderFileContent(string shaderName, List<ShaderProperty> properties, string unityShaderName = null, MaterialFile materialFile = null)
    {
        // 检测材质类型
        string shaderNameForDetection = unityShaderName ?? shaderName;
        LayaMaterialType materialType = DetectMaterialType(shaderNameForDetection);

        // 如果有MaterialFile，根据实际使用的渲染器类型来决定shader类型
        LayaShaderType shaderType;
        if (materialFile != null)
        {
            // ★ 2D/UI 组件（SpriteRenderer 或 Image）优先检测
            if (materialFile.IsUsedBy2DComponent())
            {
                shaderType = LayaShaderType.D2_BaseRenderNode2D;
                ExportLogger.Log($"LayaAir3D: Using D2_BaseRenderNode2D shader type (used by 2D component): {shaderName}");
            }
            else
            {
                bool isParticle = materialFile.IsUsedByParticleSystem();
                bool isMesh = materialFile.IsUsedByMeshRenderer();

                if (materialFile.IsCPUParticle())
                {
                    shaderType = LayaShaderType.D3;
                    ExportLogger.Log($"LayaAir3D: Using D3 shader type (CPU particle uses Mesh pipeline): {shaderName}");
                }
                else if (isParticle && !isMesh)
                {
                    shaderType = LayaShaderType.Effect;
                    ExportLogger.Log($"LayaAir3D: Using Effect shader type (used by ParticleSystemRenderer): {shaderName}");
                }
                else if (isMesh && !isParticle)
                {
                    shaderType = LayaShaderType.D3;
                    ExportLogger.Log($"LayaAir3D: Using D3 shader type (used by MeshRenderer): {shaderName}");
                }
                else
                {
                    // Fallback to material type detection
                    shaderType = GetShaderTypeFromMaterialType(materialType);
                }
            }
        }
        else
        {
            shaderType = GetShaderTypeFromMaterialType(materialType);
        }

        // 对于Custom类型，且非2D组件使用时，进一步检测ShaderType
        if (materialType == LayaMaterialType.Custom && shaderType != LayaShaderType.D2_BaseRenderNode2D)
        {
            shaderType = DetectCustomShaderType(shaderNameForDetection, properties);
        }
        
        ExportLogger.Log($"LayaAir3D: Shader '{shaderNameForDetection}' detected as MaterialType: {materialType}, ShaderType: {shaderType}");
        
        // 根据材质类型选择不同的生成策略
        switch (materialType)
        {
            case LayaMaterialType.PARTICLESHURIKEN:
                // 简单粒子材质 - 使用简化的Effect模板
                return GenerateEffectShaderContent(shaderName, properties, shaderType);
                
            case LayaMaterialType.Unlit:
                return GenerateUnlitShaderContent(shaderName, properties, shaderType);
                
            case LayaMaterialType.SkyBox:
            case LayaMaterialType.SkyProcedural:
            case LayaMaterialType.SkyPanoramic:
                return GenerateSkyShaderContent(shaderName, properties, shaderType, materialType);
                
            case LayaMaterialType.BLINNPHONG:
                return GenerateBlinnPhongShaderContent(shaderName, properties, shaderType);
                
            case LayaMaterialType.PBR:
                return GeneratePBRShaderContent(shaderName, properties, shaderType);
                
            case LayaMaterialType.Custom:
            default:
                // 自定义Shader - 根据ShaderType生成对应的基础模板
                // 注意：复杂的自定义Shader需要手动转换GLSL代码
                Debug.LogWarning($"LayaAir3D: Custom shader '{shaderNameForDetection}' detected. " +
                    "The generated shader is a basic template. You may need to manually convert the GLSL code.");
                return GenerateCustomShaderContent(shaderName, properties, shaderType);
        }
    }

    /// <summary>
    /// 生成自定义Shader内容 - 当没有源代码时使用的基础模板
    /// </summary>
    private static string GenerateCustomShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType)
    {
        StringBuilder sb = new StringBuilder();
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        // ==================== Shader3D 配置块 ====================
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:false,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        bool is2D = (shaderType == LayaShaderType.D2_BaseRenderNode2D || shaderType == LayaShaderType.D2_TextureSV);

        // uniformMap - 导出所有属性
        sb.AppendLine("    uniformMap:{");
        if (is2D)
        {
            // ★ 2D shader：只输出非内置的自定义属性
            HashSet<string> addedProps2D = new HashSet<string>();
            sb.AppendLine();
            sb.AppendLine("        // 2D Shader Properties");
            foreach (var prop in properties)
            {
                if (Is2DBuiltinProperty(prop.unityName)) continue;
                string layaName = Get2DPropertyName(prop.unityName);
                if (layaName == "u_AlbedoTexture" || layaName == "u_AlbedoColor" || layaName == "u_AlbedoIntensity")
                    continue;
                if (addedProps2D.Contains(layaName)) continue;
                addedProps2D.Add(layaName);

                string uniformLine = GenerateUniformLineWithName(prop, layaName);
                sb.AppendLine($"        {uniformLine}");
            }
        }
        else
        {
            sb.AppendLine("        // Basic");
            sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
            sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");
            // ⭐ 修复：移除u_Time定义（u_Time是引擎内置uniform，不需要在shader中定义）
            // sb.AppendLine("        u_Time: { type: Vector4, default: [0, 0, 0, 0] },");
            sb.AppendLine("        u_AlbedoColor: { type: Color, default: [1, 1, 1, 1] },");
            sb.AppendLine("        u_AlbedoIntensity: { type: Float, default: 1.0, range: [0.0, 4.0] },");

            HashSet<string> addedProps = new HashSet<string> { "u_AlphaTestValue", "u_TilingOffset", "u_AlbedoColor", "u_AlbedoIntensity" };

            // 添加所有属性
            sb.AppendLine();
            sb.AppendLine("        // Shader Properties");
            foreach (var prop in properties)
            {
                if (addedProps.Contains(prop.layaName))
                    continue;
                addedProps.Add(prop.layaName);

                string uniformLine = GenerateUniformLine(prop);
                sb.AppendLine($"        {uniformLine}");
            }
        }

        sb.AppendLine("    },");

        // defines - 根据shader类型设置不同的defines
        sb.AppendLine("    defines: {");
        if (is2D)
        {
            // ★ 2D shader的defines（参考 baseRender2D.json 模板）
            sb.AppendLine("        BASERENDER2D: { type: bool, default: true }");
        }
        else if (shaderType == LayaShaderType.Effect)
        {
            // 粒子shader的defines（参考Particle.shader模板）
            sb.AppendLine("        TINTCOLOR: { type: bool, default: true },");
            sb.AppendLine("        ADDTIVEFOG: { type: bool, default: true }");
        }
        else
        {
            sb.AppendLine("        COLOR: { type: bool, default: true },");
            sb.AppendLine("        ENABLEVERTEXCOLOR: { type: bool, default: true }");
        }
        sb.AppendLine("    },");

        // attributeMap - 根据shader类型声明不同的顶点属性
        if (is2D)
        {
            // ★ 2D shader的attributeMap（参考 baseRender2D.json 模板）
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_position: Vector4,");
            sb.AppendLine("        a_color: Vector4,");
            sb.AppendLine("        a_uv: Vector2");
            sb.AppendLine("    },");
        }
        else if (shaderType == LayaShaderType.Effect)
        {
            // 粒子shader的attributeMap
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_DirectionTime: Vector4,");
            sb.AppendLine("        a_MeshPosition: Vector3,");
            sb.AppendLine("        a_MeshColor: Vector4,");
            sb.AppendLine("        a_MeshTextureCoordinate: Vector2,");
            sb.AppendLine("        a_ShapePositionStartLifeTime: Vector4,");
            sb.AppendLine("        a_CornerTextureCoordinate: Vector4,");
            sb.AppendLine("        a_StartColor: Vector4,");
            sb.AppendLine("        a_EndColor: Vector4,");
            sb.AppendLine("        a_StartSize: Vector3,");
            sb.AppendLine("        a_StartRotation0: Vector3,");
            sb.AppendLine("        a_StartSpeed: Float,");
            sb.AppendLine("        a_Random0: Vector4,");
            sb.AppendLine("        a_Random1: Vector4,");
            sb.AppendLine("        a_SimulationWorldPostion: Vector3,");
            sb.AppendLine("        a_SimulationWorldRotation: Vector4,");
            sb.AppendLine("        a_SimulationUV: Vector4");
            sb.AppendLine("    },");
        }
        else
        {
            // ★ D3 Mesh shader 的 attributeMap（与 ConvertUnityShaderToLaya 中的 D3 分支保持一致）
            sb.AppendLine("    attributeMap: {");
            sb.AppendLine("        a_Position: Vector4,");
            sb.AppendLine("        a_Normal: Vector3,");
            sb.AppendLine("        a_Color: Vector4,");
            sb.AppendLine("        a_Texcoord0: Vector2,");
            sb.AppendLine("        a_Tangent0: Vector4,");
            sb.AppendLine("        a_BoneIndices: Vector4,");
            sb.AppendLine("        a_BoneWeights: Vector4");
            sb.AppendLine("    },");
        }

        // shaderPass
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();

        // ==================== GLSL 代码块 ====================
        sb.AppendLine("GLSL Start");

        // 根据ShaderType生成对应的基础着色器
        if (is2D)
        {
            // ★ 2D shader：使用 BaseRender2D 专用生成方法
            Generate2DBaseRenderVertexShader(sb, shaderName);
            Generate2DBaseRenderFragmentShader(sb, shaderName, null, properties);
        }
        else if (shaderType == LayaShaderType.Effect)
        {
            // Effect类型（粒子/特效）使用粒子专用的shader生成函数
            GenerateParticleBillboardVertexShader(sb, shaderName);
            GenerateParticleFragmentShader(sb, shaderName);
        }
        else
        {
            // 默认使用简单的Unlit风格
            GenerateEffectVertexShader(sb, shaderName);
            GenerateEffectFragmentShader(sb, shaderName);
        }

        sb.AppendLine("GLSL End");

        return sb.ToString();
    }

    /// <summary>
    /// 生成Effect类型Shader（粒子/特效）
    /// 粒子使用Billboard模式，UV从 a_CornerTextureCoordinate.zw 获取
    /// </summary>
    private static string GenerateEffectShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType)
    {
        StringBuilder sb = new StringBuilder();
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        // ==================== Shader3D 配置块 ====================
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:false,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        // uniformMap - 从Unity Shader属性生成（使用properties参数）
        sb.AppendLine("    uniformMap:{");

        // 添加基础粒子系统uniforms
        sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
        sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");

        // 从properties列表生成完整的uniformMap
        if (properties != null && properties.Count > 0)
        {
            foreach (var prop in properties)
            {
                string uniformLine = GenerateUniformLine(prop);
                if (!string.IsNullOrEmpty(uniformLine))
                {
                    sb.AppendLine(uniformLine);
                }

                // 为纹理属性生成_ST变量（Tiling/Offset）
                if (prop.type == ShaderUtil.ShaderPropertyType.TexEnv && !prop.layaName.EndsWith("_ST"))
                {
                    string stName = $"{prop.layaName}_ST";
                    sb.AppendLine($"        {stName}: {{ type: Vector4, default: [1, 1, 0, 0] }},");
                }
            }
        }
        else
        {
            // 如果没有properties，使用最小配置
            sb.AppendLine("        u_Tintcolor: { type: Color, default: [1, 1, 1, 1] },");
            sb.AppendLine("        u_texture: { type: Texture2D, default: \"white\", options: { define: \"DIFFUSEMAP\" } },");
        }

        sb.AppendLine("    },");

        // defines - 从properties生成（纹理defines + 特性开关）
        sb.AppendLine("    defines: {");
        sb.AppendLine("        RENDERMODE_MESH: { type: bool, default: false },");
        sb.AppendLine("        TINTCOLOR: { type: bool, default: true },");
        sb.AppendLine("        ADDTIVEFOG: { type: bool, default: true },");

        // 从properties生成defines
        if (properties != null && properties.Count > 0)
        {
            // 收集所有texture的defines
            HashSet<string> addedDefines = new HashSet<string>();

            foreach (var prop in properties)
            {
                if (prop.type == ShaderUtil.ShaderPropertyType.TexEnv && !string.IsNullOrEmpty(prop.define))
                {
                    if (!addedDefines.Contains(prop.define))
                    {
                        sb.AppendLine($"        {prop.define}: {{ type: bool, default: false }},");
                        addedDefines.Add(prop.define);
                    }
                }

                // 为Use/Enable开头的Float属性生成define
                string lowerName = prop.unityName.ToLower();
                if ((lowerName.StartsWith("use") || lowerName.StartsWith("_use")) &&
                    (prop.type == ShaderUtil.ShaderPropertyType.Float || prop.type == ShaderUtil.ShaderPropertyType.Range))
                {
                    // 例如: _UseDissolve -> USEDISSOLVE
                    string defineName = prop.unityName.TrimStart('_').ToUpper();
                    if (!addedDefines.Contains(defineName))
                    {
                        sb.AppendLine($"        {defineName}: {{ type: bool, default: false }},");
                        addedDefines.Add(defineName);
                    }
                }
            }
        }

        sb.AppendLine("    },");
        
        // attributeMap - 粒子系统的顶点属性（参考Particle.shader模板）
        sb.AppendLine("    attributeMap: {");
        sb.AppendLine("        a_DirectionTime: Vector4,");
        sb.AppendLine("        a_MeshPosition: Vector3,");
        sb.AppendLine("        a_MeshColor: Vector4,");
        sb.AppendLine("        a_MeshTextureCoordinate: Vector2,");
        sb.AppendLine("        a_ShapePositionStartLifeTime: Vector4,");
        sb.AppendLine("        a_CornerTextureCoordinate: Vector4,");
        sb.AppendLine("        a_StartColor: Vector4,");
        sb.AppendLine("        a_EndColor: Vector4,");
        sb.AppendLine("        a_StartSize: Vector3,");
        sb.AppendLine("        a_StartRotation0: Vector3,");
        sb.AppendLine("        a_StartSpeed: Float,");
        sb.AppendLine("        a_Random0: Vector4,");
        sb.AppendLine("        a_Random1: Vector4,");
        sb.AppendLine("        a_SimulationWorldPostion: Vector3,");
        sb.AppendLine("        a_SimulationWorldRotation: Vector4,");
        sb.AppendLine("        a_SimulationUV: Vector4");
        sb.AppendLine("    },");
        
        // shaderPass
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        // ==================== GLSL 代码块 ====================
        sb.AppendLine("GLSL Start");
        
        // 生成粒子Billboard顶点着色器（直接使用a_CornerTextureCoordinate）
        GenerateParticleBillboardVertexShader(sb, shaderName);
        
        // 生成粒子片元着色器（使用粒子专用includes）
        GenerateParticleFragmentShader(sb, shaderName);
        
        sb.AppendLine("GLSL End");
        
        return sb.ToString();
    }

    /// <summary>
    /// 生成粒子片元着色器（参考Particle.shader模板）
    /// 使用 ParticleShaderTemplate 提供完整的粒子颜色空间支持
    /// </summary>
    private static void GenerateParticleFragmentShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine();
        sb.AppendLine($"#define SHADER_NAME {shaderName}");
        sb.AppendLine();
        // 粒子shader的FS includes（参考Particle.shader模板）
        sb.AppendLine("#include \"Scene.glsl\";");
        sb.AppendLine("#include \"SceneFog.glsl\";");
        sb.AppendLine("#include \"Color.glsl\";");
        sb.AppendLine("#include \"Camera.glsl\";");
        sb.AppendLine();

        // varying声明（与VS保持一致，参考AI版本使用条件编译）
        sb.AppendLine("#ifdef RENDERMODE_MESH");
        sb.AppendLine("varying vec4 v_MeshColor;");
        sb.AppendLine("#endif");
        sb.AppendLine("varying vec4 v_Color;");
        sb.AppendLine("varying vec2 v_TextureCoordinate;");
        sb.AppendLine("varying vec4 v_ScreenPos;");
        sb.AppendLine();

        // ========== 粒子颜色空间常量（来自ParticleShaderTemplate） ==========
        sb.AppendLine(ParticleShaderTemplate.GetParticleFragmentConstants());
        sb.AppendLine();

        // main函数（参考引擎标准粒子shader模板）
        sb.AppendLine("void main()");
        sb.AppendLine("{");
        sb.AppendLine("    vec4 color;");
        sb.AppendLine();
        sb.AppendLine("#ifdef RENDERMODE_MESH");
        sb.AppendLine("    // Mesh mode: start with mesh vertex color");
        sb.AppendLine("    color = v_MeshColor;");
        sb.AppendLine("#else");
        sb.AppendLine("    // Billboard mode: start with white");
        sb.AppendLine("    color = vec4(1.0);");
        sb.AppendLine("#endif");
        sb.AppendLine();
        sb.AppendLine("#ifdef DIFFUSEMAP");
        sb.AppendLine("    vec4 colorT = texture2D(u_texture, v_TextureCoordinate);");
        sb.AppendLine("    #ifdef Gamma_u_texture");
        sb.AppendLine("    colorT = gammaToLinear(colorT);");
        sb.AppendLine("    #endif");
        sb.AppendLine("    color *= colorT;");
        sb.AppendLine("#endif");
        sb.AppendLine();
        // 注意：不要在这里再次乘以v_MeshColor，因为已经在初始化时设置了（6659-6665行）
        // 如果在这里再乘，会导致v_MeshColor^2，颜色变暗
        sb.AppendLine("#ifdef TINTCOLOR");
        sb.AppendLine("    color *= u_Tintcolor * c_ColorSpace * v_Color;");
        sb.AppendLine("#else");
        sb.AppendLine("    color *= v_Color;");
        sb.AppendLine("#endif");
        sb.AppendLine();
        sb.AppendLine("#ifdef ALPHATEST");
        sb.AppendLine("    if (color.a < u_AlphaTestValue)");
        sb.AppendLine("    {");
        sb.AppendLine("        discard;");
        sb.AppendLine("    }");
        sb.AppendLine("#endif");
        sb.AppendLine();
        sb.AppendLine("    gl_FragColor = color;");
        sb.AppendLine();
        sb.AppendLine("#ifdef FOG");
        sb.AppendLine("    gl_FragColor.rgb = scenUnlitFog(gl_FragColor.rgb);");
        sb.AppendLine("#endif");
        sb.AppendLine();
        sb.AppendLine("    gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成粒子Billboard顶点着色器（参考Particle.shader模板）
    /// 粒子系统不使用Vertex结构体，直接使用粒子attribute计算位置
    /// 使用 ParticleShaderTemplate 提供80%+ Unity兼容性
    /// </summary>
    private static void GenerateParticleBillboardVertexShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine();
        sb.AppendLine($"#define SHADER_NAME {shaderName}");
        sb.AppendLine();
        // 粒子shader的VS includes（参考Particle.shader模板）
        sb.AppendLine("#include \"Camera.glsl\";");
        sb.AppendLine("#include \"particleShuriKenSpriteVS.glsl\";");
        sb.AppendLine("#include \"Math.glsl\";");
        sb.AppendLine("#include \"MathGradient.glsl\";");
        sb.AppendLine("#include \"Color.glsl\";");
        sb.AppendLine("#include \"Scene.glsl\";");
        sb.AppendLine("#include \"SceneFogInput.glsl\";");
        sb.AppendLine();

        // varying声明（参考AI版本使用条件编译）
        sb.AppendLine("#ifdef RENDERMODE_MESH");
        sb.AppendLine("varying vec4 v_MeshColor;");
        sb.AppendLine("#endif");
        sb.AppendLine("varying vec4 v_Color;");
        sb.AppendLine("varying vec2 v_TextureCoordinate;");
        sb.AppendLine("varying vec4 v_ScreenPos;");
        sb.AppendLine();

        // ========== 注入完整的粒子函数库（来自ParticleShaderTemplate） ==========
        sb.Append(ParticleShaderTemplate.GetParticleVertexFunctions());

        // ========== 注入完整的main函数（来自ParticleShaderTemplate） ==========
        sb.Append(ParticleShaderTemplate.GetParticleVertexMainFunction());

        sb.AppendLine();
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    // ==================== 2D BaseRender Shader 生成 ====================

    /// <summary>
    /// 生成2D BaseRender顶点着色器（标准模板，所有2D自定义shader共用）
    /// 参考 Laya baseRender2D.json 模板格式
    /// </summary>
    private static void Generate2DBaseRenderVertexShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine();
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Sprite2DVertex.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    void main() {");
        sb.AppendLine("        vertexInfo info;");
        sb.AppendLine("        getVertexInfo(info);");
        sb.AppendLine();
        sb.AppendLine("        v_texcoord = info.uv;");
        sb.AppendLine("        v_color = info.color;");
        sb.AppendLine();
        sb.AppendLine("        #ifdef LIGHT2D_ENABLE");
        sb.AppendLine("            lightAndShadow(info);");
        sb.AppendLine("        #endif");
        sb.AppendLine();
        sb.AppendLine("        gl_Position = getPosition(info.pos);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成2D BaseRender片元着色器
    /// 从Unity fragment代码分析混色逻辑，映射到Laya 2D变量
    /// </summary>
    private static void Generate2DBaseRenderFragmentShader(StringBuilder sb, string shaderName,
        ShaderParseResult parseResult, List<ShaderProperty> properties)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine("    #if defined(GL_FRAGMENT_PRECISION_HIGH)");
        sb.AppendLine("    precision highp float;");
        sb.AppendLine("    #else");
        sb.AppendLine("    precision mediump float;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #include \"Sprite2DFrag.glsl\";");
        sb.AppendLine();

        // 声明自定义uniform（非内置的）
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                if (Is2DBuiltinProperty(prop.unityName)) continue;
                string layaName = Get2DPropertyName(prop.unityName);
                if (layaName == "u_AlbedoTexture" || layaName == "u_AlbedoColor" || layaName == "u_AlbedoIntensity")
                    continue;
                // 映射类型
                string glslType = GetGLSLTypeForProperty(prop);
                if (glslType != null)
                {
                    sb.AppendLine($"    uniform {glslType} {layaName};");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        clip();");
        sb.AppendLine("        vec4 textureColor = texture2D(u_baseRender2DTexture, v_texcoord);");

        // 根据属性生成混色逻辑
        string colorExpr = AnalyzeFragmentColorExpression(parseResult, properties);
        sb.AppendLine($"        {colorExpr}");

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("#endGLSL");
    }

    /// <summary>
    /// 获取属性对应的GLSL类型字符串
    /// </summary>
    private static string GetGLSLTypeForProperty(ShaderProperty prop)
    {
        switch (prop.type)
        {
            case ShaderUtil.ShaderPropertyType.Color:
            case ShaderUtil.ShaderPropertyType.Vector:
                return "vec4";
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                return "float";
            case ShaderUtil.ShaderPropertyType.TexEnv:
                return "sampler2D";
            case ShaderUtil.ShaderPropertyType.Int:
                return "int";
            default:
                return null;
        }
    }

    /// <summary>
    /// 分析Unity片元着色器的混色逻辑，映射为Laya 2D表达式
    ///
    /// 实现步骤：
    /// 1. 从parseResult.fragmentCode中提取return表达式
    /// 2. 将Unity变量名替换为Laya 2D变量名
    /// 3. 如果解析失败，使用基于属性列表的fallback规则
    /// </summary>
    private static string AnalyzeFragmentColorExpression(ShaderParseResult parseResult, List<ShaderProperty> properties)
    {
        // 尝试从fragment代码提取return表达式
        if (parseResult != null && !string.IsNullOrEmpty(parseResult.fragmentCode))
        {
            string fragCode = parseResult.fragmentCode;

            // 提取return语句：匹配 "return <expr>;"
            var returnMatch = System.Text.RegularExpressions.Regex.Match(fragCode, @"return\s+(.+?)\s*;");
            if (returnMatch.Success)
            {
                string expr = returnMatch.Groups[1].Value.Trim();

                // 变量名替换：Unity → Laya 2D
                // tex2D(_MainTex, ...) → textureColor（已在外部声明采样过）
                expr = System.Text.RegularExpressions.Regex.Replace(expr,
                    @"tex2D\s*\(\s*_MainTex\s*,\s*[^)]+\)", "textureColor");
                expr = System.Text.RegularExpressions.Regex.Replace(expr,
                    @"texture2D\s*\(\s*_MainTex\s*,\s*[^)]+\)", "textureColor");

                // i.color / IN.color / input.color → v_color
                expr = System.Text.RegularExpressions.Regex.Replace(expr,
                    @"(?:i|IN|input|o)\.color", "v_color");

                // i.texcoord → v_texcoord (for any remaining references)
                expr = System.Text.RegularExpressions.Regex.Replace(expr,
                    @"(?:i|IN|input|o)\.texcoord\d*", "v_texcoord");

                // Unity属性名 _Xxx → 对应Laya uniform名（优先用2D映射）
                expr = System.Text.RegularExpressions.Regex.Replace(expr,
                    @"_([A-Z][a-zA-Z0-9]*)", match =>
                    {
                        string unityName = "_" + match.Groups[1].Value;
                        return Get2DPropertyName(unityName);
                    });

                // HLSL类型到GLSL类型替换
                expr = expr.Replace("fixed4", "vec4").Replace("fixed3", "vec3")
                    .Replace("half4", "vec4").Replace("half3", "vec3")
                    .Replace("float4", "vec4").Replace("float3", "vec3");

                return $"gl_FragColor = {expr};";
            }

            // 尝试分析多行逻辑（如 fixed4 col = ...; col.rgb *= ...; return col;）
            var multiLineResult = AnalyzeMultiLineFragment(fragCode, properties);
            if (multiLineResult != null)
            {
                return multiLineResult;
            }
        }

        // Fallback：基于属性列表推断混色逻辑
        return GenerateFallbackColorExpression(properties);
    }

    /// <summary>
    /// 分析多行fragment代码（如：fixed4 col = tex * color; col.rgb *= _Intensity; return col;）
    /// </summary>
    private static string AnalyzeMultiLineFragment(string fragCode, List<ShaderProperty> properties)
    {
        // 查找赋值语句 + return模式
        // 匹配模式: type varname = expr; ... return varname;
        var assignMatch = System.Text.RegularExpressions.Regex.Match(fragCode,
            @"(?:fixed4|half4|float4|vec4)\s+(\w+)\s*=\s*(.+?)\s*;");
        if (!assignMatch.Success) return null;

        string varName = assignMatch.Groups[1].Value;
        string initExpr = assignMatch.Groups[2].Value;

        // 确认有 return varName;
        var returnCheck = System.Text.RegularExpressions.Regex.Match(fragCode,
            $@"return\s+{System.Text.RegularExpressions.Regex.Escape(varName)}\s*;");
        if (!returnCheck.Success) return null;

        StringBuilder result = new StringBuilder();

        // 转换初始化表达式
        initExpr = ConvertFragExprTo2D(initExpr);
        result.AppendLine($"vec4 {varName} = {initExpr};");

        // 查找中间的修改语句（如 col.rgb *= ...; col.a *= ...;）
        string afterInit = fragCode.Substring(assignMatch.Index + assignMatch.Length);
        string beforeReturn = afterInit.Substring(0, afterInit.IndexOf("return"));

        var modifyMatches = System.Text.RegularExpressions.Regex.Matches(beforeReturn,
            $@"{System.Text.RegularExpressions.Regex.Escape(varName)}(\.[rgba]+)\s*([\*\+\-]?=)\s*(.+?)\s*;");

        foreach (System.Text.RegularExpressions.Match mod in modifyMatches)
        {
            string swizzle = mod.Groups[1].Value;
            string op = mod.Groups[2].Value;
            string modExpr = ConvertFragExprTo2D(mod.Groups[3].Value);

            // 跳过alpha clip相关逻辑（Laya 2D由clip()处理）
            if (swizzle == ".a" && modExpr.Contains("clip"))
                continue;

            result.AppendLine($"        {varName}{swizzle} {op} {modExpr};");
        }

        result.Append($"        gl_FragColor = {varName};");
        return result.ToString();
    }

    /// <summary>
    /// 将fragment表达式中的Unity变量名转换为Laya 2D变量名
    /// </summary>
    private static string ConvertFragExprTo2D(string expr)
    {
        // tex2D(_MainTex, ...) → textureColor
        expr = System.Text.RegularExpressions.Regex.Replace(expr,
            @"tex2D\s*\(\s*_MainTex\s*,\s*[^)]+\)", "textureColor");
        expr = System.Text.RegularExpressions.Regex.Replace(expr,
            @"texture2D\s*\(\s*_MainTex\s*,\s*[^)]+\)", "textureColor");

        // i.color / IN.color → v_color
        expr = System.Text.RegularExpressions.Regex.Replace(expr,
            @"(?:i|IN|input|o)\.color", "v_color");

        // Unity属性名 _Xxx → Laya uniform名
        expr = System.Text.RegularExpressions.Regex.Replace(expr,
            @"_([A-Z][a-zA-Z0-9]*)", match =>
            {
                string unityName = "_" + match.Groups[1].Value;
                return Get2DPropertyName(unityName);
            });

        // HLSL类型到GLSL类型
        expr = expr.Replace("fixed4", "vec4").Replace("fixed3", "vec3")
            .Replace("half4", "vec4").Replace("half3", "vec3")
            .Replace("float4", "vec4").Replace("float3", "vec3");

        return expr;
    }

    /// <summary>
    /// 当无法解析fragment代码时，基于属性列表生成fallback混色表达式
    /// </summary>
    private static string GenerateFallbackColorExpression(List<ShaderProperty> properties)
    {
        if (properties != null)
        {
            bool hasTintColor = properties.Any(p => p.unityName == "_TintColor");
            bool hasColor = properties.Any(p => p.unityName == "_Color" || p.unityName == "_BaseColor");

            if (hasTintColor || hasColor)
            {
                // 有TintColor或Color → v_color * u_TintColor * textureColor
                return "gl_FragColor = v_color * u_TintColor * textureColor;";
            }
        }

        // 标准 baseRender2D 路径
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("textureColor = transspaceColor(textureColor);");
        sb.Append("        setglColor(textureColor);");
        return sb.ToString();
    }

    /// <summary>
    /// 生成普通Effect顶点着色器（非粒子）
    /// 使用 vertex.texCoord0 作为UV
    /// </summary>
    private static void GenerateEffectVertexShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Math.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFogInput.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DVertex.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"VertexCommon.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("    varying vec2 v_Texcoord0;");
        sb.AppendLine("    #endif // UV");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("    varying vec4 v_VertexColor;");
        sb.AppendLine("    #endif // COLOR");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        Vertex vertex;");
        sb.AppendLine("        getVertexParams(vertex);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("        v_Texcoord0 = TransformUV(vertex.texCoord0, u_TilingOffset);");
        sb.AppendLine("    #endif // UV");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        v_VertexColor = vertex.vertexColor;");
        sb.AppendLine("    #endif // COLOR");
        sb.AppendLine();
        sb.AppendLine("        mat4 worldMat = getWorldMatrix();");
        sb.AppendLine("        vec4 pos = (worldMat * vec4(vertex.positionOS, 1.0));");
        sb.AppendLine("        vec3 positionWS = pos.xyz / pos.w;");
        sb.AppendLine();
        sb.AppendLine("        gl_Position = getPositionCS(positionWS);");
        sb.AppendLine("        gl_Position = remapPositionZ(gl_Position);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        FogHandle(gl_Position.z);");
        sb.AppendLine("    #endif");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成Effect片元着色器
    /// </summary>
    private static void GenerateEffectFragmentShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Color.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFog.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DFrag.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("    varying vec2 v_Texcoord0;");
        sb.AppendLine("    #endif // UV");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("    varying vec4 v_VertexColor;");
        sb.AppendLine("    #endif // COLOR");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        vec2 uv = vec2(0.0);");
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("        uv = v_Texcoord0;");
        sb.AppendLine("    #endif // UV");
        sb.AppendLine();
        sb.AppendLine("        vec3 color = u_AlbedoColor.rgb;");
        sb.AppendLine("        float alpha = u_AlbedoColor.a;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef ALBEDOTEXTURE");
        sb.AppendLine("        vec4 albedoSampler = texture2D(u_AlbedoTexture, uv);");
        sb.AppendLine("        #ifdef Gamma_u_AlbedoTexture");
        sb.AppendLine("        albedoSampler = gammaToLinear(albedoSampler);");
        sb.AppendLine("        #endif // Gamma_u_AlbedoTexture");
        sb.AppendLine("        color *= albedoSampler.rgb;");
        sb.AppendLine("        alpha *= albedoSampler.a;");
        sb.AppendLine("    #endif // ALBEDOTEXTURE");
        sb.AppendLine();
        sb.AppendLine("        color *= u_AlbedoIntensity;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        #ifdef ENABLEVERTEXCOLOR");
        sb.AppendLine("        color *= v_VertexColor.rgb;");
        sb.AppendLine("        alpha *= v_VertexColor.a;");
        sb.AppendLine("        #endif // ENABLEVERTEXCOLOR");
        sb.AppendLine("    #endif // COLOR");
        sb.AppendLine();
        sb.AppendLine("    #ifdef ALPHATEST");
        sb.AppendLine("        if (alpha < u_AlphaTestValue)");
        sb.AppendLine("            discard;");
        sb.AppendLine("    #endif // ALPHATEST");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        color = scenUnlitFog(color);");
        sb.AppendLine("    #endif // FOG");
        sb.AppendLine();
        sb.AppendLine("        gl_FragColor = vec4(color, alpha);");
        sb.AppendLine("        gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成Unlit类型Shader
    /// </summary>
    private static string GenerateUnlitShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType)
    {
        // Unlit和Effect类似，但shaderType是D3
        StringBuilder sb = new StringBuilder();
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:true,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        sb.AppendLine("    uniformMap:{");
        sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
        sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");
        sb.AppendLine("        u_AlbedoColor: { type: Color, default: [1, 1, 1, 1] },");
        sb.AppendLine("        u_AlbedoTexture: { type: Texture2D, options: { define: \"ALBEDOTEXTURE\" } },");
        sb.AppendLine("        u_AlbedoIntensity: { type: Float, default: 1.0, range: [0.0, 4.0] },");
        sb.AppendLine("    },");
        
        // Unlit类型默认开启COLOR宏以支持顶点颜色
        sb.AppendLine("    defines: {");
        sb.AppendLine("        COLOR: { type: bool, default: true },");
        sb.AppendLine("        ENABLEVERTEXCOLOR: { type: bool, default: true }");
        sb.AppendLine("    },");
        
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        sb.AppendLine("GLSL Start");
        GenerateEffectVertexShader(sb, shaderName);
        GenerateEffectFragmentShader(sb, shaderName);
        sb.AppendLine("GLSL End");
        
        return sb.ToString();
    }

    /// <summary>
    /// 生成Sky类型Shader
    /// </summary>
    private static string GenerateSkyShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType, LayaMaterialType materialType)
    {
        // 天空盒Shader - 简化版本
        StringBuilder sb = new StringBuilder();
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:false,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        sb.AppendLine("    uniformMap:{");
        sb.AppendLine("        u_TintColor: { type: Color, default: [0.5, 0.5, 0.5, 0.5] },");
        sb.AppendLine("        u_Exposure: { type: Float, default: 1.0, range: [0.0, 8.0] },");
        sb.AppendLine("        u_Rotation: { type: Float, default: 0.0, range: [0.0, 360.0] },");
        if (materialType == LayaMaterialType.SkyPanoramic)
        {
            sb.AppendLine("        u_Texture: { type: Texture2D },");
        }
        else
        {
            sb.AppendLine("        u_CubeTexture: { type: TextureCube },");
        }
        sb.AppendLine("    },");
        
        sb.AppendLine("    defines: {},");
        
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        // 天空盒GLSL代码需要特殊处理，这里使用简化版本
        sb.AppendLine("GLSL Start");
        GenerateSkyVertexShader(sb, shaderName);
        GenerateSkyFragmentShader(sb, shaderName, materialType);
        sb.AppendLine("GLSL End");
        
        return sb.ToString();
    }

    /// <summary>
    /// 生成Sky顶点着色器
    /// </summary>
    private static void GenerateSkyVertexShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Math.glsl\";");
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DVertex.glsl\";");
        sb.AppendLine("    #include \"VertexCommon.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    varying vec3 v_Texcoord;");
        sb.AppendLine();
        sb.AppendLine("    vec3 RotateAroundYInDegrees(vec3 vertex, float degrees)");
        sb.AppendLine("    {");
        sb.AppendLine("        float alpha = degrees * PI / 180.0;");
        sb.AppendLine("        float sina = sin(alpha);");
        sb.AppendLine("        float cosa = cos(alpha);");
        sb.AppendLine("        mat2 m = mat2(cosa, -sina, sina, cosa);");
        sb.AppendLine("        vec2 rotated = m * vertex.xz;");
        sb.AppendLine("        return vec3(rotated.x, vertex.y, rotated.y);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        Vertex vertex;");
        sb.AppendLine("        getVertexParams(vertex);");
        sb.AppendLine();
        sb.AppendLine("        vec3 rotated = RotateAroundYInDegrees(vertex.positionOS, u_Rotation);");
        sb.AppendLine("        mat4 worldMat = getWorldMatrix();");
        sb.AppendLine("        vec4 pos = worldMat * vec4(rotated, 1.0);");
        sb.AppendLine("        gl_Position = getPositionCS(pos.xyz);");
        sb.AppendLine("        gl_Position = remapPositionZ(gl_Position);");
        sb.AppendLine("        v_Texcoord = vertex.positionOS;");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成Sky片元着色器
    /// </summary>
    private static void GenerateSkyFragmentShader(StringBuilder sb, string shaderName, LayaMaterialType materialType)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Color.glsl\";");
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DFrag.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    varying vec3 v_Texcoord;");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        if (materialType == LayaMaterialType.SkyPanoramic)
        {
            sb.AppendLine("        vec3 dir = normalize(v_Texcoord);");
            sb.AppendLine("        vec2 uv;");
            sb.AppendLine("        uv.x = atan(dir.z, dir.x) / (2.0 * PI) + 0.5;");
            sb.AppendLine("        uv.y = asin(dir.y) / PI + 0.5;");
            sb.AppendLine("        vec4 tex = texture2D(u_Texture, uv);");
        }
        else
        {
            sb.AppendLine("        vec4 tex = textureCube(u_CubeTexture, v_Texcoord);");
        }
        sb.AppendLine("        vec3 color = tex.rgb * u_TintColor.rgb * 2.0;");
        sb.AppendLine("        color *= u_Exposure;");
        sb.AppendLine("        gl_FragColor = vec4(color, 1.0);");
        sb.AppendLine("        gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成BlinnPhong类型Shader
    /// </summary>
    private static string GenerateBlinnPhongShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType)
    {
        // BlinnPhong使用简化的光照模型
        StringBuilder sb = new StringBuilder();
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:true,");
        sb.AppendLine("    supportReflectionProbe:false,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        sb.AppendLine("    uniformMap:{");
        sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
        sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");
        sb.AppendLine("        u_DiffuseColor: { type: Color, default: [1, 1, 1, 1] },");
        sb.AppendLine("        u_DiffuseTexture: { type: Texture2D, options: { define: \"DIFFUSEMAP\" } },");
        sb.AppendLine("        u_MaterialSpecular: { type: Color, default: [1, 1, 1, 1] },");
        sb.AppendLine("        u_SpecularTexture: { type: Texture2D, options: { define: \"SPECULARMAP\" } },");
        sb.AppendLine("        u_NormalTexture: { type: Texture2D, options: { define: \"NORMALTEXTURE\" } },");
        sb.AppendLine("        u_Shininess: { type: Float, default: 32.0, range: [1.0, 256.0] },");
        sb.AppendLine("        u_AlbedoIntensity: { type: Float, default: 1.0, range: [0.0, 4.0] },");
        sb.AppendLine("    },");
        
        // BlinnPhong类型默认开启COLOR宏以支持顶点颜色
        sb.AppendLine("    defines: {");
        sb.AppendLine("        COLOR: { type: bool, default: true },");
        sb.AppendLine("        ENABLEVERTEXCOLOR: { type: bool, default: true }");
        sb.AppendLine("    },");
        
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        sb.AppendLine("GLSL Start");
        GenerateBlinnPhongVertexShader(sb, shaderName);
        GenerateBlinnPhongFragmentShader(sb, shaderName);
        sb.AppendLine("GLSL End");
        
        return sb.ToString();
    }

    /// <summary>
    /// 生成BlinnPhong顶点着色器
    /// </summary>
    private static void GenerateBlinnPhongVertexShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Math.glsl\";");
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFogInput.glsl\";");
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DVertex.glsl\";");
        sb.AppendLine("    #include \"VertexCommon.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    varying vec2 v_Texcoord0;");
        sb.AppendLine("    varying vec3 v_Normal;");
        sb.AppendLine("    varying vec3 v_PositionWS;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("    varying vec4 v_VertexColor;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        Vertex vertex;");
        sb.AppendLine("        getVertexParams(vertex);");
        sb.AppendLine();
        sb.AppendLine("        v_Texcoord0 = TransformUV(vertex.texCoord0, u_TilingOffset);");
        sb.AppendLine();
        sb.AppendLine("        mat4 worldMat = getWorldMatrix();");
        sb.AppendLine("        vec4 pos = worldMat * vec4(vertex.positionOS, 1.0);");
        sb.AppendLine("        v_PositionWS = pos.xyz / pos.w;");
        sb.AppendLine("        v_Normal = normalize((worldMat * vec4(vertex.normalOS, 0.0)).xyz);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        v_VertexColor = vertex.vertexColor;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        gl_Position = getPositionCS(v_PositionWS);");
        sb.AppendLine("        gl_Position = remapPositionZ(gl_Position);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        FogHandle(gl_Position.z);");
        sb.AppendLine("    #endif");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成BlinnPhong片元着色器
    /// </summary>
    private static void GenerateBlinnPhongFragmentShader(StringBuilder sb, string shaderName)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Color.glsl\";");
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFog.glsl\";");
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DFrag.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    varying vec2 v_Texcoord0;");
        sb.AppendLine("    varying vec3 v_Normal;");
        sb.AppendLine("    varying vec3 v_PositionWS;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("    varying vec4 v_VertexColor;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        vec2 uv = v_Texcoord0;");
        sb.AppendLine("        vec3 normal = normalize(v_Normal);");
        sb.AppendLine();
        sb.AppendLine("        // Diffuse");
        sb.AppendLine("        vec4 diffuseColor = u_DiffuseColor;");
        sb.AppendLine("    #ifdef DIFFUSEMAP");
        sb.AppendLine("        vec4 diffuseTex = texture2D(u_DiffuseTexture, uv);");
        sb.AppendLine("        #ifdef Gamma_u_DiffuseTexture");
        sb.AppendLine("        diffuseTex = gammaToLinear(diffuseTex);");
        sb.AppendLine("        #endif");
        sb.AppendLine("        diffuseColor *= diffuseTex;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        #ifdef ENABLEVERTEXCOLOR");
        sb.AppendLine("        diffuseColor *= v_VertexColor;");
        sb.AppendLine("        #endif");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        // Simple directional light");
        sb.AppendLine("        vec3 lightDir = normalize(vec3(0.5, 1.0, 0.3));");
        sb.AppendLine("        float NdotL = max(dot(normal, lightDir), 0.0);");
        sb.AppendLine("        vec3 diffuse = diffuseColor.rgb * NdotL;");
        sb.AppendLine();
        sb.AppendLine("        // Specular");
        sb.AppendLine("        vec3 viewDir = normalize(u_CameraPos - v_PositionWS);");
        sb.AppendLine("        vec3 halfDir = normalize(lightDir + viewDir);");
        sb.AppendLine("        float NdotH = max(dot(normal, halfDir), 0.0);");
        sb.AppendLine("        vec3 specular = u_MaterialSpecular.rgb * pow(NdotH, u_Shininess);");
        sb.AppendLine();
        sb.AppendLine("        // Ambient");
        sb.AppendLine("        vec3 ambient = diffuseColor.rgb * 0.2;");
        sb.AppendLine();
        sb.AppendLine("        vec3 color = ambient + diffuse + specular;");
        sb.AppendLine("        color *= u_AlbedoIntensity;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef ALPHATEST");
        sb.AppendLine("        if (diffuseColor.a < u_AlphaTestValue)");
        sb.AppendLine("            discard;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        color = scenUnlitFog(color);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        gl_FragColor = vec4(color, diffuseColor.a);");
        sb.AppendLine("        gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成PBR类型Shader（完整版，支持PBR+NPR混合渲染）
    /// </summary>
    private static string GeneratePBRShaderContent(string shaderName, List<ShaderProperty> properties, LayaShaderType shaderType)
    {
        StringBuilder sb = new StringBuilder();
        
        // 检测Shader特性
        bool hasNPR = HasNPRFeatures(properties);
        bool hasIBL = HasIBLFeatures(properties);
        bool hasMatcap = HasMatcapFeatures(properties);
        bool hasFresnel = HasFresnelFeatures(properties);
        bool hasRim = HasRimFeatures(properties);
        bool hasHSV = HasHSVFeatures(properties);
        bool hasTonemapping = HasTonemappingFeatures(properties);
        
        string shaderTypeStr = GetShaderTypeString(shaderType);
        
        // ==================== Shader3D 配置块 ====================
        sb.AppendLine("Shader3D Start");
        sb.AppendLine("{");
        sb.AppendLine($"    type:Shader3D,");
        sb.AppendLine($"    name:{shaderName},");
        sb.AppendLine("    enableInstancing:true,");
        sb.AppendLine("    supportReflectionProbe:true,");
        sb.AppendLine($"    shaderType:{shaderTypeStr},");
        
        // uniformMap
        sb.AppendLine("    uniformMap:{");
        
        // 基础属性
        sb.AppendLine("        // Basic");
        sb.AppendLine("        u_AlphaTestValue: { type: Float, default: 0.5, range: [0.0, 1.0] },");
        sb.AppendLine("        u_TilingOffset: { type: Vector4, default: [1, 1, 0, 0] },");
        sb.AppendLine("        u_Alpha: { type: Float, default: 1.0, range: [0.0, 1.0] },");
        
        HashSet<string> addedProps = new HashSet<string> { "u_AlphaTestValue", "u_TilingOffset", "u_Alpha" };
        
        // 按类别分组输出属性
        GenerateUniformsByCategory(sb, properties, addedProps, "Base Color", 
            new[] { "BaseMap", "BaseColor", "MainTex", "Color", "ColorIntensity", "AlbedoTexture", "AlbedoColor" });
        
        GenerateUniformsByCategory(sb, properties, addedProps, "PBR - MAER Map",
            new[] { "MAER", "Metallic", "Smoothness", "Occlusion", "AO" });
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Normal Map",
            new[] { "Normal", "Bump" });
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Mask",
            new[] { "Mask" });
        
        if (hasIBL)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "IBL",
                new[] { "IBL" });
        }
        
        if (hasMatcap)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "Matcap",
                new[] { "Matcap" });
        }
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Emission",
            new[] { "Emission" });
        
        if (hasNPR)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "NPR Toon Shading",
                new[] { "Med", "Shadow", "Reflect", "GI" });
        }
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Specular",
            new[] { "Specular", "GGX" });
        
        if (hasFresnel)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "Fresnel",
                new[] { "Fresnel", "fresnel" });
        }
        
        if (hasRim)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "Rim Effect",
                new[] { "Rim" });
        }
        
        if (hasHSV)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "HSV Adjust",
                new[] { "HSV", "Hue", "Saturation", "Value" });
        }
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Contrast",
            new[] { "Contrast", "OriginalColor" });
        
        if (hasTonemapping)
        {
            GenerateUniformsByCategory(sb, properties, addedProps, "Tonemapping",
                new[] { "Tone", "WhitePoint" });
        }
        
        GenerateUniformsByCategory(sb, properties, addedProps, "Light Control",
            new[] { "SelfLight", "LightDir" });
        
        // 输出剩余未分类的属性
        GenerateRemainingUniforms(sb, properties, addedProps);
        
        // 添加必要的默认属性（如果Shader中没有定义）
        GenerateRequiredDefaults(sb, addedProps, hasNPR, hasFresnel, hasRim, hasHSV, hasTonemapping);
        
        sb.AppendLine("    },");
        
        // defines - PBR类型默认开启COLOR宏以支持顶点颜色
        sb.AppendLine("    defines: {");
        sb.AppendLine("        COLOR: { type: bool, default: true },");
        sb.AppendLine("        EMISSION: { type: bool, default: false },");
        sb.AppendLine("        ENABLEVERTEXCOLOR: { type: bool, default: true },");
        if (hasNPR)
        {
            sb.AppendLine("        USENPR: { type: bool, default: true }");
        }
        else
        {
            sb.AppendLine("        USENPR: { type: bool, default: false }");
        }
        sb.AppendLine("    },");
        
        // shaderPass
        sb.AppendLine("    shaderPass:[");
        sb.AppendLine("        {");
        sb.AppendLine("            pipeline:Forward,");
        sb.AppendLine($"            VS:{shaderName}VS,");
        sb.AppendLine($"            FS:{shaderName}FS");
        sb.AppendLine("        }");
        sb.AppendLine("    ]");
        sb.AppendLine("}");
        sb.AppendLine("Shader3D End");
        sb.AppendLine();
        
        // ==================== GLSL 代码块 ====================
        sb.AppendLine("GLSL Start");
        
        // 生成顶点着色器
        GenerateVertexShader(sb, shaderName, properties);
        
        // 生成片元着色器
        GenerateFragmentShader(sb, shaderName, properties, hasNPR, hasIBL, hasMatcap, hasFresnel, hasRim, hasHSV, hasTonemapping);
        
        sb.AppendLine("GLSL End");
        
        return sb.ToString();
    }

    /// <summary>
    /// 按类别生成Uniform属性
    /// </summary>
    private static void GenerateUniformsByCategory(StringBuilder sb, List<ShaderProperty> properties, 
        HashSet<string> addedProps, string categoryName, string[] keywords)
    {
        List<ShaderProperty> categoryProps = new List<ShaderProperty>();
        
        foreach (var prop in properties)
        {
            if (addedProps.Contains(prop.layaName))
                continue;
                
            foreach (var keyword in keywords)
            {
                if (prop.unityName.Contains(keyword))
                {
                    categoryProps.Add(prop);
                    break;
                }
            }
        }
        
        if (categoryProps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"        // {categoryName}");
            
            foreach (var prop in categoryProps)
            {
                if (addedProps.Contains(prop.layaName))
                    continue;
                addedProps.Add(prop.layaName);
                
                string uniformLine = GenerateUniformLine(prop);
                if (!string.IsNullOrEmpty(uniformLine))
                {
                    sb.AppendLine(uniformLine);
                }
            }
        }
    }

    /// <summary>
    /// 生成剩余未分类的Uniform属性
    /// </summary>
    private static void GenerateRemainingUniforms(StringBuilder sb, List<ShaderProperty> properties, HashSet<string> addedProps)
    {
        List<ShaderProperty> remainingProps = new List<ShaderProperty>();
        
        foreach (var prop in properties)
        {
            if (!addedProps.Contains(prop.layaName))
            {
                remainingProps.Add(prop);
            }
        }
        
        if (remainingProps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // Other Properties");
            
            foreach (var prop in remainingProps)
            {
                addedProps.Add(prop.layaName);
                string uniformLine = GenerateUniformLine(prop);
                if (!string.IsNullOrEmpty(uniformLine))
                {
                    sb.AppendLine(uniformLine);
                }
            }
        }
    }

    /// <summary>
    /// 生成必要的默认属性（确保Shader中使用的变量都有定义）
    /// </summary>
    private static void GenerateRequiredDefaults(StringBuilder sb, HashSet<string> addedProps, 
        bool hasNPR, bool hasFresnel, bool hasRim, bool hasHSV, bool hasTonemapping)
    {
        List<string> requiredDefaults = new List<string>();
        
        // 基础必需属性
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_AlbedoColor", "{ type: Color, default: [1, 1, 1, 1] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_NormalScale", "{ type: Float, default: 1.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_Metallic", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_MetallicRemapMin", "{ type: Float, default: 0.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_MetallicRemapMax", "{ type: Float, default: 1.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_Smoothness", "{ type: Float, default: 0.5, range: [0.0, 1.0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_SmoothnessRemapMin", "{ type: Float, default: 0.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_SmoothnessRemapMax", "{ type: Float, default: 1.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_OcclusionStrength", "{ type: Float, default: 1.0, range: [0.0, 1.5] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_EmissionColor", "{ type: Color, default: [0, 0, 0, 0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_EmissionIntensity", "{ type: Float, default: 1.0 }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_ColorIntensity", "{ type: Float, default: 1.0, range: [1.0, 10.0] }");
        
        // 光照控制
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_SelfLight", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_SelfLightDir", "{ type: Vector4, default: [0, 1, 0, 1] }");
        
        // 对比度
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_OriginalColor", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_Contrast", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
        AddDefaultIfMissing(requiredDefaults, addedProps, "u_ContrastScale", "{ type: Float, default: 1.0, range: [0.0, 3.0] }");
        
        // NPR必需属性
        if (hasNPR)
        {
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_MedColor", "{ type: Color, default: [1, 1, 1, 1] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_MedThreshold", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_MedSmooth", "{ type: Float, default: 0.25, range: [0.0, 0.5] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ShadowColor", "{ type: Color, default: [0, 0, 0, 1] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ShadowThreshold", "{ type: Float, default: 0.7, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ShadowSmooth", "{ type: Float, default: 0.2, range: [0.0, 0.5] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ReflectColor", "{ type: Color, default: [0.02, 0.02, 0.02, 0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ReflectThreshold", "{ type: Float, default: 0.4, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ReflectSmooth", "{ type: Float, default: 0.15, range: [0.0, 0.5] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_GIIntensity", "{ type: Float, default: 0.0, range: [0.0, 2.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularHighlights", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_GGXSpecular", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularColor", "{ type: Color, default: [1, 1, 1, 1] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularIntensity", "{ type: Float, default: 1.0 }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularLightOffset", "{ type: Vector4, default: [0, 0, 0, 0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularThreshold", "{ type: Float, default: 0.5, range: [0.1, 2.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_SpecularSmooth", "{ type: Float, default: 0.5, range: [0.0, 0.5] }");
        }
        
        // Fresnel必需属性
        if (hasFresnel)
        {
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_DirectionalFresnel", "{ type: Float, default: 0.0 }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelColor", "{ type: Color, default: [1, 0, 0, 0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_fresnelOffset", "{ type: Vector4, default: [0, 0, 0, 0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelThreshold", "{ type: Float, default: 0.5, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelSmooth", "{ type: Float, default: 0.5, range: [0.0, 0.5] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelIntensity", "{ type: Float, default: 1.0 }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelMetallic", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_FresnelFit", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
        }
        
        // Rim必需属性
        if (hasRim)
        {
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimColor", "{ type: Color, default: [1, 0.5, 0, 1] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimPower", "{ type: Float, default: 1.0, range: [0.01, 10.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimIntensity", "{ type: Float, default: 0.0 }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimStart", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimEnd", "{ type: Float, default: 1.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_RimOffset", "{ type: Vector4, default: [0, 0, 0, 0] }");
        }
        
        // HSV必需属性
        if (hasHSV)
        {
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_AdjustHSV", "{ type: Float, default: 0.0, range: [0.0, 1.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_AdjustHue", "{ type: Float, default: 0.0, range: [0.0, 360.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_AdjustSaturation", "{ type: Float, default: 1.0, range: [0.0, 1.5] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_AdjustValue", "{ type: Float, default: 1.0, range: [0.0, 1.5] }");
        }
        
        // Tonemapping必需属性
        if (hasTonemapping)
        {
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_ToneWeight", "{ type: Float, default: 0.0, range: [0.0, 3.0] }");
            AddDefaultIfMissing(requiredDefaults, addedProps, "u_WhitePoint", "{ type: Float, default: 1.0, range: [0.0, 3.0] }");
        }
        
        // 输出缺失的默认属性
        if (requiredDefaults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // Required Defaults");
            foreach (var line in requiredDefaults)
            {
                sb.AppendLine(line);
            }
        }
    }

    /// <summary>
    /// 如果属性不存在则添加默认值
    /// </summary>
    private static void AddDefaultIfMissing(List<string> defaults, HashSet<string> addedProps, string propName, string definition)
    {
        if (!addedProps.Contains(propName))
        {
            addedProps.Add(propName);
            defaults.Add($"        {propName}: {definition},");
        }
    }

    /// <summary>
    /// 生成顶点着色器
    /// </summary>
    private static void GenerateVertexShader(StringBuilder sb, string shaderName, List<ShaderProperty> properties)
    {
        sb.AppendLine($"#defineGLSL {shaderName}VS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Math.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFogInput.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DVertex.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"VertexCommon.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"PBRVertex.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        Vertex vertex;");
        sb.AppendLine("        getVertexParams(vertex);");
        sb.AppendLine();
        sb.AppendLine("        PixelParams pixel;");
        sb.AppendLine("        initPixelParams(pixel, vertex);");
        sb.AppendLine();
        sb.AppendLine("        gl_Position = getPositionCS(pixel.positionWS);");
        sb.AppendLine("        gl_Position = remapPositionZ(gl_Position);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        FogHandle(gl_Position.z);");
        sb.AppendLine("    #endif // FOG");
        sb.AppendLine("    }");
        sb.AppendLine("#endGLSL");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成片元着色器
    /// </summary>
    private static void GenerateFragmentShader(StringBuilder sb, string shaderName, List<ShaderProperty> properties,
        bool hasNPR, bool hasIBL, bool hasMatcap, bool hasFresnel, bool hasRim, bool hasHSV, bool hasTonemapping)
    {
        sb.AppendLine($"#defineGLSL {shaderName}FS");
        sb.AppendLine($"    #define SHADER_NAME {shaderName}");
        sb.AppendLine();
        sb.AppendLine("    #include \"Color.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Scene.glsl\";");
        sb.AppendLine("    #include \"SceneFog.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"Camera.glsl\";");
        sb.AppendLine("    #include \"Sprite3DFrag.glsl\";");
        sb.AppendLine();
        sb.AppendLine("    #include \"PBRMetallicFrag.glsl\";");
        sb.AppendLine();
        
        // 生成工具函数
        GenerateUtilityFunctions(sb, hasNPR, hasFresnel, hasRim, hasHSV, hasTonemapping);
        
        // 生成initSurfaceInputs函数
        GenerateInitSurfaceInputs(sb, properties);
        
        // 生成main函数
        GenerateMainFunction(sb, properties, hasNPR, hasIBL, hasMatcap, hasFresnel, hasRim, hasHSV, hasTonemapping);
        
        sb.AppendLine("#endGLSL");
    }

    /// <summary>
    /// 生成工具函数
    /// </summary>
    private static void GenerateUtilityFunctions(StringBuilder sb, bool hasNPR, bool hasFresnel, bool hasRim, bool hasHSV, bool hasTonemapping)
    {
        sb.AppendLine("    //========================================");
        sb.AppendLine("    // Utility Functions");
        sb.AppendLine("    //========================================");
        sb.AppendLine();
        
        // LinearStep函数
        sb.AppendLine("    float LinearStep(float minVal, float maxVal, float value)");
        sb.AppendLine("    {");
        sb.AppendLine("        return clamp((value - minVal) / (maxVal - minVal + 0.0001), 0.0, 1.0);");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // remapValue函数
        sb.AppendLine("    float remapValue(float x, float t1, float t2, float s1, float s2)");
        sb.AppendLine("    {");
        sb.AppendLine("        return (x - t1) / (t2 - t1) * (s2 - s1) + s1;");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // Gamma转换
        if (hasTonemapping)
        {
            sb.AppendLine("    vec3 Gamma22ToLinear(vec3 color)");
            sb.AppendLine("    {");
            sb.AppendLine("        return pow(color, vec3(2.2));");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        
        // IBL旋转函数
        sb.AppendLine("    vec3 rotateVectorXYZ(float rx, float ry, float rz, vec3 v)");
        sb.AppendLine("    {");
        sb.AppendLine("        vec3 rot = vec3(radians(rx), radians(ry), radians(rz));");
        sb.AppendLine("        return rotationByEuler(v, rot);");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // HSV函数
        if (hasHSV)
        {
            sb.AppendLine("    vec3 RGBtoHSV(vec3 rgb)");
            sb.AppendLine("    {");
            sb.AppendLine("        float cmax = max(rgb.r, max(rgb.g, rgb.b));");
            sb.AppendLine("        float cmin = min(rgb.r, min(rgb.g, rgb.b));");
            sb.AppendLine("        float delta = cmax - cmin;");
            sb.AppendLine("        vec3 hsv = vec3(0.0, 0.0, cmax);");
            sb.AppendLine("        if (delta > 0.0001)");
            sb.AppendLine("        {");
            sb.AppendLine("            hsv.y = delta / cmax;");
            sb.AppendLine("            if (rgb.r == cmax) hsv.x = (rgb.g - rgb.b) / delta;");
            sb.AppendLine("            else if (rgb.g == cmax) hsv.x = 2.0 + (rgb.b - rgb.r) / delta;");
            sb.AppendLine("            else hsv.x = 4.0 + (rgb.r - rgb.g) / delta;");
            sb.AppendLine("            hsv.x /= 6.0;");
            sb.AppendLine("            if (hsv.x < 0.0) hsv.x += 1.0;");
            sb.AppendLine("        }");
            sb.AppendLine("        return hsv;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    vec3 HSVtoRGB(vec3 hsv)");
            sb.AppendLine("    {");
            sb.AppendLine("        float h = hsv.x * 6.0;");
            sb.AppendLine("        float s = hsv.y;");
            sb.AppendLine("        float v = hsv.z;");
            sb.AppendLine("        float c = v * s;");
            sb.AppendLine("        float x = c * (1.0 - abs(mod(h, 2.0) - 1.0));");
            sb.AppendLine("        float m = v - c;");
            sb.AppendLine("        vec3 rgb;");
            sb.AppendLine("        if (h < 1.0) rgb = vec3(c, x, 0.0);");
            sb.AppendLine("        else if (h < 2.0) rgb = vec3(x, c, 0.0);");
            sb.AppendLine("        else if (h < 3.0) rgb = vec3(0.0, c, x);");
            sb.AppendLine("        else if (h < 4.0) rgb = vec3(0.0, x, c);");
            sb.AppendLine("        else if (h < 5.0) rgb = vec3(x, 0.0, c);");
            sb.AppendLine("        else rgb = vec3(c, 0.0, x);");
            sb.AppendLine("        return rgb + vec3(m);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    vec3 AdjustHSVColor(vec3 color, float hueShift, float satMult, float valMult)");
            sb.AppendLine("    {");
            sb.AppendLine("        vec3 hsv = RGBtoHSV(color);");
            sb.AppendLine("        hsv.x = mod(hsv.x + hueShift / 360.0, 1.0);");
            sb.AppendLine("        hsv.y *= satMult;");
            sb.AppendLine("        hsv.z *= valMult;");
            sb.AppendLine("        return HSVtoRGB(hsv);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        
        // Fresnel函数
        if (hasFresnel || hasRim)
        {
            sb.AppendLine("    vec3 FresnelCore(vec3 normal, vec3 viewDir, vec3 rimColor, float power, float intensity, float start, float end, vec3 offset)");
            sb.AppendLine("    {");
            sb.AppendLine("        vec3 N = normalize(normal);");
            sb.AppendLine("        vec3 V = normalize(viewDir) + offset;");
            sb.AppendLine("        float NdotV = 1.0 - clamp(dot(N, V), 0.0, 1.0);");
            sb.AppendLine("        float range = smoothstep(start, end, NdotV);");
            sb.AppendLine("        float fresnel = intensity * pow(range, power);");
            sb.AppendLine("        return clamp(rimColor * fresnel, 0.0, 1.0);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        
        // NPR Radiance函数
        if (hasNPR)
        {
            sb.AppendLine("    vec3 CalculateNPRRadiance(vec3 lightDir, vec3 lightColor, vec3 normalWS)");
            sb.AppendLine("    {");
            sb.AppendLine("        float NdotL = dot(normalWS, lightDir);");
            sb.AppendLine("        float halfLambert = NdotL * 0.5 + 0.5;");
            sb.AppendLine("        float smoothMedTone = LinearStep(u_MedThreshold - u_MedSmooth, u_MedThreshold + u_MedSmooth, halfLambert);");
            sb.AppendLine("        vec3 MedToneColor = mix(u_MedColor.rgb, vec3(1.0), smoothMedTone);");
            sb.AppendLine("        float smoothShadow = LinearStep(u_ShadowThreshold - u_ShadowSmooth, u_ShadowThreshold + u_ShadowSmooth, halfLambert);");
            sb.AppendLine("        vec3 ShadowColor = mix(u_ShadowColor.rgb, MedToneColor, smoothShadow);");
            sb.AppendLine("        float smoothReflect = LinearStep(u_ReflectThreshold - u_ReflectSmooth, u_ReflectThreshold + u_ReflectSmooth, halfLambert);");
            sb.AppendLine("        vec3 ReflectColor = mix(u_ReflectColor.rgb, ShadowColor, smoothReflect);");
            sb.AppendLine("        return lightColor * ReflectColor;");
            sb.AppendLine("    }");
            sb.AppendLine();
            
            // NPR Stylized Specular
            sb.AppendLine("    vec3 StylizedSpecular(float perceptualRoughness, vec3 specularColor, vec3 normalWS, vec3 lightDir, vec3 viewDir, vec3 specularLightOffset)");
            sb.AppendLine("    {");
            sb.AppendLine("        vec3 halfDir = SafeNormalize(normalize(lightDir + specularLightOffset) + viewDir);");
            sb.AppendLine("        float NoH = clamp(dot(normalWS, halfDir), 0.0, 1.0);");
            sb.AppendLine("        float LoH = clamp(dot(lightDir, halfDir), 0.0, 1.0);");
            sb.AppendLine("        float roughness = perceptualRoughness * perceptualRoughness;");
            sb.AppendLine("        float roughness2 = roughness * roughness;");
            sb.AppendLine("        float roughness2MinusOne = roughness2 - 1.0;");
            sb.AppendLine("        float normalizationTerm = roughness * 4.0 + 2.0;");
            sb.AppendLine("        float d = NoH * NoH * roughness2MinusOne + 1.00001;");
            sb.AppendLine("        float LoH2 = LoH * LoH;");
            sb.AppendLine("        float specularTerm = roughness2 / ((d * d) * max(0.1, LoH2) * normalizationTerm);");
            sb.AppendLine("        specularTerm = max(0.0, specularTerm - MEDIUMP_FLT_MIN);");
            sb.AppendLine("        specularTerm = min(100.0, specularTerm);");
            sb.AppendLine("        specularTerm = pow(specularTerm, u_SpecularThreshold + u_SpecularSmooth);");
            sb.AppendLine("        float specularStylize = LinearStep(u_SpecularThreshold - u_SpecularSmooth, u_SpecularThreshold + u_SpecularSmooth, specularTerm);");
            sb.AppendLine("        specularTerm = mix(specularStylize, specularTerm, u_GGXSpecular);");
            sb.AppendLine("        return specularTerm * max(vec3(0.0), u_SpecularIntensity * u_SpecularColor.rgb) * specularColor;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// 生成initSurfaceInputs函数
    /// </summary>
    private static void GenerateInitSurfaceInputs(StringBuilder sb, List<ShaderProperty> properties)
    {
        sb.AppendLine("    //========================================");
        sb.AppendLine("    // Initialize Surface Inputs");
        sb.AppendLine("    //========================================");
        sb.AppendLine();
        sb.AppendLine("    void initSurfaceInputs(inout SurfaceInputs inputs, inout PixelParams pixel)");
        sb.AppendLine("    {");
        sb.AppendLine("        inputs.alphaTest = u_AlphaTestValue;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("        vec2 uv = TransformUV(pixel.uv0, u_TilingOffset);");
        sb.AppendLine("    #else");
        sb.AppendLine("        vec2 uv = vec2(0.0);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        
        // Diffuse Color
        sb.AppendLine("        // Diffuse Color");
        sb.AppendLine("        inputs.diffuseColor = u_AlbedoColor.rgb;");
        
        // 检查是否有ColorIntensity
        bool hasColorIntensity = false;
        foreach (var prop in properties)
        {
            if (prop.unityName.Contains("ColorIntensity"))
            {
                hasColorIntensity = true;
                break;
            }
        }
        if (hasColorIntensity)
        {
            sb.AppendLine("        inputs.diffuseColor *= u_ColorIntensity;");
        }
        
        sb.AppendLine("        inputs.alpha = u_AlbedoColor.a * u_Alpha;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef COLOR");
        sb.AppendLine("        #ifdef ENABLEVERTEXCOLOR");
        sb.AppendLine("        inputs.diffuseColor *= pixel.vertexColor.xyz;");
        sb.AppendLine("        inputs.alpha *= pixel.vertexColor.a;");
        sb.AppendLine("        #endif");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef ALBEDOTEXTURE");
        sb.AppendLine("        vec4 albedoSampler = texture2D(u_AlbedoTexture, uv);");
        sb.AppendLine("        #ifdef Gamma_u_AlbedoTexture");
        sb.AppendLine("        albedoSampler = gammaToLinear(albedoSampler);");
        sb.AppendLine("        #endif");
        sb.AppendLine("        inputs.diffuseColor *= albedoSampler.rgb;");
        sb.AppendLine("        inputs.alpha *= albedoSampler.a;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        
        // Normal
        sb.AppendLine("        // Normal");
        sb.AppendLine("        inputs.normalTS = vec3(0.0, 0.0, 1.0);");
        sb.AppendLine("    #ifdef NORMALTEXTURE");
        sb.AppendLine("        vec3 normalSampler = texture2D(u_NormalTexture, uv).rgb;");
        sb.AppendLine("        normalSampler = normalize(normalSampler * 2.0 - 1.0);");
        sb.AppendLine("        normalSampler.y *= -1.0;");
        sb.AppendLine("        inputs.normalTS = normalScale(normalSampler, u_NormalScale);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        
        // PBR Properties
        sb.AppendLine("        // PBR Properties");
        sb.AppendLine("        inputs.metallic = u_Metallic;");
        sb.AppendLine("        inputs.smoothness = u_Smoothness;");
        sb.AppendLine("        inputs.occlusion = 1.0;");
        sb.AppendLine();
        sb.AppendLine("    #ifdef MAERMAP");
        sb.AppendLine("        vec4 maerSampler = texture2D(u_MAER, uv);");
        sb.AppendLine("        inputs.metallic = remapValue(maerSampler.r, 0.0, 1.0, u_MetallicRemapMin, u_MetallicRemapMax);");
        sb.AppendLine("        inputs.occlusion = mix(1.0, maerSampler.g, u_OcclusionStrength);");
        sb.AppendLine("        // Unity uses reverse remap: lerp(Max, Min, texA)");
        sb.AppendLine("        float remappedSmooth = mix(u_SmoothnessRemapMax, u_SmoothnessRemapMin, maerSampler.a);");
        sb.AppendLine("        inputs.smoothness = remappedSmooth * u_Smoothness;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        
        // Emission
        sb.AppendLine("        // Emission");
        sb.AppendLine("        inputs.emissionColor = vec3(0.0);");
        sb.AppendLine("    #ifdef EMISSION");
        sb.AppendLine("        inputs.emissionColor = u_EmissionColor.rgb * u_EmissionIntensity;");
        sb.AppendLine("        #ifdef EMISSIONTEXTURE");
        sb.AppendLine("        vec4 emissionSampler = texture2D(u_EmissionTexture, uv);");
        sb.AppendLine("        #ifdef Gamma_u_EmissionTexture");
        sb.AppendLine("        emissionSampler = gammaToLinear(emissionSampler);");
        sb.AppendLine("        #endif");
        sb.AppendLine("        inputs.emissionColor *= emissionSampler.rgb;");
        sb.AppendLine("        #endif");
        sb.AppendLine("    #endif");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成main函数
    /// </summary>
    private static void GenerateMainFunction(StringBuilder sb, List<ShaderProperty> properties,
        bool hasNPR, bool hasIBL, bool hasMatcap, bool hasFresnel, bool hasRim, bool hasHSV, bool hasTonemapping)
    {
        sb.AppendLine("    //========================================");
        sb.AppendLine("    // Main Fragment Shader");
        sb.AppendLine("    //========================================");
        sb.AppendLine();
        sb.AppendLine("    void main()");
        sb.AppendLine("    {");
        sb.AppendLine("        PixelParams pixel;");
        sb.AppendLine("        getPixelParams(pixel);");
        sb.AppendLine();
        sb.AppendLine("        SurfaceInputs inputs;");
        sb.AppendLine("        initSurfaceInputs(inputs, pixel);");
        sb.AppendLine();
        sb.AppendLine("    #ifdef UV");
        sb.AppendLine("        vec2 uv = TransformUV(pixel.uv0, u_TilingOffset);");
        sb.AppendLine("    #else");
        sb.AppendLine("        vec2 uv = vec2(0.0);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        // Get view direction and normal");
        sb.AppendLine("        vec3 positionWS = pixel.positionWS;");
        sb.AppendLine("        vec3 viewDir = normalize(u_CameraPos - positionWS);");
        sb.AppendLine();
        sb.AppendLine("        vec3 normalWS = pixel.normalWS;");
        sb.AppendLine("    #ifdef NORMALTEXTURE");
        sb.AppendLine("        normalWS = normalize(pixel.TBN * inputs.normalTS);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        // Light direction");
        sb.AppendLine("        vec3 selfLightDir = u_SelfLightDir.xyz;");
        sb.AppendLine("        float lightDirLen = length(selfLightDir);");
        sb.AppendLine("        vec3 lightDir = lightDirLen > 0.001 ? normalize(selfLightDir) : normalize(vec3(0.0, 1.0, 0.0));");
        sb.AppendLine("        vec3 lightColor = vec3(1.0);");
        sb.AppendLine();
        
        // Sample Mask
        sb.AppendLine("        // Sample Mask");
        sb.AppendLine("        vec4 maskTex = vec4(1.0);");
        sb.AppendLine("    #ifdef MASKMAP");
        sb.AppendLine("        maskTex = texture2D(u_Mask, uv);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        
        // Surface Color Calculation
        sb.AppendLine("        //========================================");
        sb.AppendLine("        // Calculate Surface Color");
        sb.AppendLine("        //========================================");
        sb.AppendLine();
        sb.AppendLine("        vec4 surfaceColor;");
        sb.AppendLine();
        
        if (hasNPR)
        {
            sb.AppendLine("    #ifdef USENPR");
            sb.AppendLine("        // NPR Toon Shading");
            sb.AppendLine("        vec3 specularLightOffset = mix(u_SpecularLightOffset.xyz, u_SelfLightDir.xyz, u_SelfLight);");
            sb.AppendLine("        vec3 radiance = CalculateNPRRadiance(lightDir, lightColor, normalWS);");
            sb.AppendLine("        float ndotl = LinearStep(u_ShadowThreshold - u_ShadowSmooth, u_ShadowThreshold + u_ShadowSmooth, dot(lightDir, normalWS) * 0.5 + 0.5);");
            sb.AppendLine();
            sb.AppendLine("        float oneMinusReflectivity = (1.0 - inputs.metallic) * 0.96;");
            sb.AppendLine("        vec3 diffuseColor = inputs.diffuseColor * oneMinusReflectivity;");
            sb.AppendLine("        vec3 specularColor = mix(vec3(0.04), inputs.diffuseColor, inputs.metallic);");
            sb.AppendLine("        float perceptualRoughness = 1.0 - inputs.smoothness;");
            sb.AppendLine();
            sb.AppendLine("        vec3 finalDiffuse = diffuseColor * radiance * inputs.occlusion;");
            sb.AppendLine("        vec3 gi = diffuseColor * inputs.occlusion * u_GIIntensity;");
            sb.AppendLine();
            sb.AppendLine("        vec3 specular = StylizedSpecular(perceptualRoughness, specularColor, normalWS, lightDir, viewDir, specularLightOffset) * radiance;");
            sb.AppendLine("        specular = mix(vec3(0.0), specular, u_SpecularHighlights);");
            sb.AppendLine();
            sb.AppendLine("        surfaceColor = vec4(finalDiffuse + gi + specular + inputs.emissionColor, inputs.alpha);");
            sb.AppendLine("    #else");
            sb.AppendLine("        // Standard PBR Flow");
            sb.AppendLine("        surfaceColor = PBR_Metallic_Flow(inputs, pixel);");
            sb.AppendLine("    #endif");
        }
        else
        {
            sb.AppendLine("        // Standard PBR Flow");
            sb.AppendLine("        surfaceColor = PBR_Metallic_Flow(inputs, pixel);");
        }
        sb.AppendLine();
        
        // IBL
        if (hasIBL)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // IBL Reflection");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("    #ifdef IBLMAP");
            sb.AppendLine("        vec3 reflectVector = reflect(-viewDir, normalWS);");
            sb.AppendLine("        vec3 rotatedReflect = rotateVectorXYZ(u_IBLMapRotateX, u_IBLMapRotateY, u_IBLMapRotateZ, reflectVector);");
            sb.AppendLine("        float iblPerceptualRoughness = 1.0 - inputs.smoothness;");
            sb.AppendLine("        float mip = (1.7 - iblPerceptualRoughness * 0.7) * iblPerceptualRoughness * 8.0;");
            sb.AppendLine("        vec4 iblSampler = textureCube(u_IBLMap, rotatedReflect, mip);");
            sb.AppendLine("        vec3 iblColor = pow(iblSampler.rgb, vec3(u_IBLMapPower));");
            sb.AppendLine("        iblColor *= u_IBLMapColor.rgb * u_IBLMapIntensity * inputs.metallic;");
            sb.AppendLine("        surfaceColor.rgb = mix(surfaceColor.rgb, surfaceColor.rgb + iblColor, maskTex.r);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }
        
        // Matcap
        if (hasMatcap)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // Matcap");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("    #ifdef MATCAPMAP");
            sb.AppendLine("        float cosmc = cos(radians(u_MatcapAngle));");
            sb.AppendLine("        float sinmc = sin(radians(u_MatcapAngle));");
            sb.AppendLine("        vec3 viewNormal = (u_View * vec4(normalWS, 0.0)).xyz;");
            sb.AppendLine("        vec2 uv_Matcap = viewNormal.xy * 0.5 + 0.5;");
            sb.AppendLine("        uv_Matcap = mat2(cosmc, -sinmc, sinmc, cosmc) * (uv_Matcap - 0.5) + 0.5;");
            sb.AppendLine("        vec4 matcapSampler = texture2D(u_MatcapMap, uv_Matcap);");
            sb.AppendLine("        vec3 matcapColor = matcapSampler.rgb * u_MatcapColor.rgb * u_MatcapStrength * 5.0;");
            sb.AppendLine("        float matcapIntensity = clamp(pow(abs(matcapSampler.r), u_MatcapPow), 0.0, 1.0);");
            sb.AppendLine("        matcapColor *= matcapIntensity;");
            sb.AppendLine("        surfaceColor.rgb = mix(surfaceColor.rgb, surfaceColor.rgb + surfaceColor.rgb * matcapColor, maskTex.g);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
            sb.AppendLine("    #ifdef MATCAPADDMAP");
            sb.AppendLine("        float cosmcAdd = cos(radians(u_MatcapAddAngle));");
            sb.AppendLine("        float sinmcAdd = sin(radians(u_MatcapAddAngle));");
            sb.AppendLine("        vec3 viewNormalAdd = (u_View * vec4(normalWS, 0.0)).xyz;");
            sb.AppendLine("        vec2 uv_MatcapAdd = viewNormalAdd.xy * 0.5 + 0.5;");
            sb.AppendLine("        uv_MatcapAdd = mat2(cosmcAdd, -sinmcAdd, sinmcAdd, cosmcAdd) * (uv_MatcapAdd - 0.5) + 0.5;");
            sb.AppendLine("        vec4 matcapAddSampler = texture2D(u_MatcapAddMap, uv_MatcapAdd);");
            sb.AppendLine("        float matcapAlAdd = clamp(pow(abs(matcapAddSampler.r), u_MatcapAddPow), 0.0, 1.0) * u_MatcapAddStrength;");
            sb.AppendLine("        matcapAlAdd *= maskTex.b;");
            sb.AppendLine("        surfaceColor.rgb = mix(surfaceColor.rgb, surfaceColor.rgb + matcapAddSampler.rgb * u_MatcapAddColor.rgb, matcapAlAdd);");
            sb.AppendLine("    #endif");
            sb.AppendLine();
        }
        
        // Fresnel
        if (hasFresnel)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // Fresnel");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("        {");
            sb.AppendLine("            float FresnelIntensity = mix(0.0, u_FresnelIntensity, maskTex.a);");
            sb.AppendLine("            vec3 viewDirWithOffset = normalize(viewDir + u_fresnelOffset.xyz);");
            sb.AppendLine("            vec3 cameraForward = -vec3(u_View[0][2], u_View[1][2], u_View[2][2]);");
            sb.AppendLine("            vec3 viewForwardDir = normalize(cameraForward + u_fresnelOffset.xyz);");
            sb.AppendLine("            float dotNormal = dot(normalWS, viewDirWithOffset);");
            sb.AppendLine("            float dotFit = dot(normalWS, viewForwardDir);");
            sb.AppendLine("            float linearStepInputNormal = 1.0 - clamp(dotNormal, 0.0, 1.0);");
            sb.AppendLine("            float linearStepInputFit = 1.0 - clamp(dotFit, 0.0, 1.0);");
            sb.AppendLine("            float linearStepNormal = LinearStep(u_FresnelThreshold - u_FresnelSmooth, u_FresnelThreshold + u_FresnelSmooth, linearStepInputNormal);");
            sb.AppendLine("            float linearStepFit = LinearStep(u_FresnelThreshold - u_FresnelSmooth, u_FresnelThreshold + u_FresnelSmooth, linearStepInputFit);");
            sb.AppendLine("            float ndotl_fresnel = LinearStep(u_ShadowThreshold - u_ShadowSmooth, u_ShadowThreshold + u_ShadowSmooth, 1.0);");
            sb.AppendLine("            ndotl_fresnel = mix(1.0, ndotl_fresnel, u_DirectionalFresnel);");
            sb.AppendLine("            float vertexColorR = 1.0;");
            sb.AppendLine("        #ifdef COLOR");
            sb.AppendLine("            vertexColorR = pixel.vertexColor.r;");
            sb.AppendLine("        #endif");
            sb.AppendLine("            float fresnelTermNormal = linearStepNormal * max(0.0, FresnelIntensity * vertexColorR) * ndotl_fresnel;");
            sb.AppendLine("            float fresnelTermFit = linearStepFit * max(0.0, FresnelIntensity * vertexColorR) * ndotl_fresnel;");
            sb.AppendLine("            float fresnelTermFinal = mix(fresnelTermNormal, fresnelTermFit, u_FresnelFit);");
            sb.AppendLine("            float roughness = 1.0;");
            sb.AppendLine("            float surfaceReduction = 1.0 / (roughness * roughness + 1.0);");
            sb.AppendLine("            float grazingTerm = 0.04;");
            sb.AppendLine("            vec3 indirectSpecular = vec3(0.1) * mix(1.0, inputs.metallic, u_FresnelMetallic);");
            sb.AppendLine("            vec3 fresnelSpecular = surfaceReduction * indirectSpecular * grazingTerm * fresnelTermFinal * u_FresnelColor.rgb;");
            sb.AppendLine("            surfaceColor.rgb += fresnelSpecular;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // Rim
        if (hasRim)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // Rim Effect");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("        if (u_RimIntensity > 0.001)");
            sb.AppendLine("        {");
            sb.AppendLine("            vec3 effectRim = FresnelCore(normalWS, viewDir, u_RimColor.rgb, u_RimPower, u_RimIntensity, u_RimStart, u_RimEnd, u_RimOffset.xyz);");
            sb.AppendLine("            surfaceColor.rgb += effectRim;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // Contrast
        sb.AppendLine("        //========================================");
        sb.AppendLine("        // Contrast");
        sb.AppendLine("        //========================================");
        sb.AppendLine();
        sb.AppendLine("        vec3 avgColor = vec3(0.5) * u_ContrastScale;");
        sb.AppendLine("        surfaceColor.rgb = mix(mix(vec3(0.5), surfaceColor.rgb, 1.1), surfaceColor.rgb, u_OriginalColor);");
        sb.AppendLine("        surfaceColor.rgb = mix(surfaceColor.rgb, mix(avgColor, surfaceColor.rgb, 1.1), u_Contrast);");
        sb.AppendLine();
        
        // HSV
        if (hasHSV)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // HSV Adjustment");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("        if (u_AdjustHSV > 0.5)");
            sb.AppendLine("        {");
            sb.AppendLine("            surfaceColor.rgb = AdjustHSVColor(surfaceColor.rgb, u_AdjustHue, u_AdjustSaturation, u_AdjustValue);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // Tonemapping
        if (hasTonemapping)
        {
            sb.AppendLine("        //========================================");
            sb.AppendLine("        // Tonemapping");
            sb.AppendLine("        //========================================");
            sb.AppendLine();
            sb.AppendLine("        if (u_ToneWeight > 0.001)");
            sb.AppendLine("        {");
            sb.AppendLine("            vec3 numerator = surfaceColor.rgb * (6.2 * surfaceColor.rgb + 0.5);");
            sb.AppendLine("            vec3 denominator = surfaceColor.rgb * (6.2 * surfaceColor.rgb + 1.2) + 0.06;");
            sb.AppendLine("            vec3 tonemapped = numerator / denominator;");
            sb.AppendLine("            tonemapped *= u_WhitePoint;");
            sb.AppendLine("            tonemapped = Gamma22ToLinear(tonemapped);");
            sb.AppendLine("            surfaceColor.rgb = mix(surfaceColor.rgb, tonemapped, u_ToneWeight);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // Fog and Output
        sb.AppendLine("        //========================================");
        sb.AppendLine("        // Fog and Output");
        sb.AppendLine("        //========================================");
        sb.AppendLine();
        sb.AppendLine("    #ifdef FOG");
        sb.AppendLine("        surfaceColor.rgb = sceneLitFog(surfaceColor.rgb);");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("    #ifdef ALPHATEST");
        sb.AppendLine("        if (surfaceColor.a < u_AlphaTestValue)");
        sb.AppendLine("            discard;");
        sb.AppendLine("    #endif");
        sb.AppendLine();
        sb.AppendLine("        surfaceColor.a = clamp(surfaceColor.a, 0.0, 1.0);");
        sb.AppendLine();
        sb.AppendLine("        gl_FragColor = surfaceColor;");
        sb.AppendLine("        gl_FragColor = outputTransform(gl_FragColor);");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// 获取纹理的默认值
    /// </summary>
    private static string GetDefaultTextureValue(string propName)
    {
        string lowerName = propName.ToLower();

        // Normal/Bump贴图
        if (lowerName.Contains("normal") || lowerName.Contains("bump"))
            return "\"bump\"";

        // Distort贴图：默认黑色（可选效果，默认关闭）
        if (lowerName.Contains("distort"))
            return "\"black\"";

        // Dissolve Amount贴图：默认黑色
        if (lowerName.Contains("dissolveamount") || lowerName.Contains("dissolve_amount"))
            return "\"black\"";

        // 黑色贴图（通常用于遮罩、AO等）
        if (lowerName.Contains("black") || lowerName.Contains("ao") ||
            lowerName.Contains("occlusion") || lowerName.Contains("shadow"))
            return "\"black\"";

        // RimMap特殊处理：默认灰色
        if (lowerName.Contains("rimmap") && !lowerName.Contains("mask"))
            return "\"gray\"";

        // 灰色贴图
        if (lowerName.Contains("gray") || lowerName.Contains("grey") ||
            lowerName.Contains("metallic"))
            return "\"gray\"";

        // 白色贴图（默认）
        return "\"white\"";
    }

    /// <summary>
    /// 获取Range类型的默认值
    /// </summary>
    private static float GetRangeDefaultValue(string propName, float min, float max)
    {
        string lowerName = propName.ToLower();

        // ⭐ RotateAngle特殊处理：默认0.0（不是range的min值-360）
        if (lowerName.Contains("rotateangle") || lowerName.Contains("rotate_angle"))
        {
            if (min <= 0.0f && max >= 0.0f)
                return 0.0f;
            return (min + max) / 2.0f; // 如果range不包含0，使用中间值
        }

        // Distort Strength特殊处理：默认0.0（可选效果，默认关闭）
        if (lowerName.Contains("distort") && lowerName.Contains("strength"))
        {
            return 0.0f;
        }

        // Dissolve Distort Strength特殊处理：默认0.0
        if (lowerName.Contains("dissolvedistortstrength"))
        {
            return 0.0f;
        }

        // EffectMainLightIntensity特殊处理：默认5.0
        if (lowerName.Contains("effectmainlightintensity") || lowerName.Contains("effect_main_light_intensity"))
        {
            if (min <= 5.0f && max >= 5.0f)
                return 5.0f;
            return max; // 如果range不包含5.0，使用最大值
        }

        // RimLevel特殊处理：默认1.0
        if (lowerName.Contains("rimlevel") || lowerName.Contains("rim_level"))
        {
            if (min <= 1.0f && max >= 1.0f)
                return 1.0f;
            return (min + max) / 2.0f;
        }

        // RimSharp特殊处理：默认2.0
        if (lowerName.Contains("rimsharp") || lowerName.Contains("rim_sharp"))
        {
            if (min <= 2.0f && max >= 2.0f)
                return 2.0f;
            return (min + max) / 2.0f;
        }

        // Dissolve/Fade Range特殊处理：默认0.1
        if ((lowerName.Contains("dissolve") || lowerName.Contains("fade") || lowerName.Contains("edge")) &&
            lowerName.Contains("range"))
        {
            if (min <= 0.1f && max >= 0.1f)
                return 0.1f;
            return (min + max) / 2.0f;
        }

        // Multiplier/Intensity类型：默认1.0
        if (lowerName.Contains("multiplier") || lowerName.Contains("intensity") ||
            lowerName.Contains("scale") || lowerName.Contains("strength"))
        {
            // 如果range包含1.0，使用1.0
            if (min <= 1.0f && max >= 1.0f)
                return 1.0f;
            // 否则使用中间值
            return (min + max) / 2.0f;
        }

        // Center/Pivot类型：默认0.5
        if (lowerName.Contains("center") || lowerName.Contains("pivot"))
        {
            if (min <= 0.5f && max >= 0.5f)
                return 0.5f;
            return (min + max) / 2.0f;
        }

        // Threshold类型：默认0.5
        if (lowerName.Contains("threshold"))
        {
            if (min <= 0.5f && max >= 0.5f)
                return 0.5f;
            return (min + max) / 2.0f;
        }

        // Alpha/Occlusion类型：默认1.0
        if (lowerName.Contains("alpha") || lowerName.Contains("occlusion"))
        {
            if (min <= 1.0f && max >= 1.0f)
                return 1.0f;
            return max; // 通常希望完全可见/无遮挡
        }

        // Smoothness/Metallic类型：默认0.0或0.5
        if (lowerName.Contains("smoothness") || lowerName.Contains("metallic") ||
            lowerName.Contains("roughness"))
        {
            return min; // 通常从最小值开始
        }

        // 默认：使用最小值
        return min;
    }

    /// <summary>
    /// 获取Float类型的默认值
    /// </summary>
    private static string GetFloatDefaultValue(string propName)
    {
        string lowerName = propName.ToLower();

        // RotateAngle特殊处理：默认0.0（不是-360）
        if (lowerName.Contains("rotateangle") || lowerName.Contains("rotate_angle"))
        {
            return "0.0";
        }

        // Distort Strength特殊处理：默认0.0（可选效果，默认关闭）
        if (lowerName.Contains("distort") && lowerName.Contains("strength"))
        {
            return "0.0";
        }

        // Multiplier/Intensity/Scale类型：默认1.0
        if (lowerName.Contains("multiplier") || lowerName.Contains("intensity") ||
            lowerName.Contains("scale") || lowerName.Contains("strength"))
        {
            return "1.0";
        }

        // Center/Pivot类型：默认0.5
        if (lowerName.Contains("center") || lowerName.Contains("pivot") ||
            lowerName.Contains("rotatecenter"))
        {
            return "0.5";
        }

        // RemapMax类型：默认1.0
        if (lowerName.Contains("remapmax") || lowerName.Contains("max"))
        {
            return "1.0";
        }

        // Alpha类型：默认1.0（完全不透明）
        if (lowerName.Contains("alpha") && !lowerName.Contains("test"))
        {
            return "1.0";
        }

        // Level类型：默认1.0
        if (lowerName.Contains("level"))
        {
            return "1.0";
        }

        // 特殊的命名约定检查
        // 如果属性名明确表示是标志位（Use/Enable），默认0.0（关闭）
        if (lowerName.StartsWith("use") || lowerName.StartsWith("enable") ||
            lowerName.StartsWith("_use") || lowerName.StartsWith("_enable"))
        {
            return "0.0";
        }

        // 默认：0.0
        return "0.0";
    }

    /// <summary>
    /// 格式化 Color 为 Laya default 字符串
    /// </summary>
    private static string FormatColorDefault(Color c)
    {
        return $"[{c.r:G}, {c.g:G}, {c.b:G}, {c.a:G}]";
    }

    /// <summary>
    /// 格式化 Vector4 为 Laya default 字符串
    /// </summary>
    private static string FormatVector4Default(Vector4 v)
    {
        return $"[{v.x:G}, {v.y:G}, {v.z:G}, {v.w:G}]";
    }

    /// <summary>
    /// 生成uniform行 - 支持Range、默认值等
    /// ⭐ 优先使用 Unity Properties 的实际默认值，仅纹理类型使用启发式推断
    /// </summary>
    private static string GenerateUniformLine(ShaderProperty prop)
    {
        string typeStr;
        string defaultValue;
        string rangeStr = "";
        string options = "";

        switch (prop.type)
        {
            case ShaderUtil.ShaderPropertyType.TexEnv:
                // 纹理类型仍用启发式（默认值是字符串 white/black/bump 等）
                if (prop.isCubemap)
                {
                    typeStr = "TextureCube";
                    defaultValue = "\"white\"";
                }
                else
                {
                    typeStr = "Texture2D";
                    defaultValue = GetDefaultTextureValue(prop.unityName);
                }

                if (!string.IsNullOrEmpty(prop.define))
                {
                    options = $", options: {{ define: \"{prop.define}\" }}";
                }
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue}{options} }},";

            case ShaderUtil.ShaderPropertyType.Color:
                if (prop.isHDR)
                {
                    // HDR Color must bypass Laya's Color gamma conversion. Unity material
                    // values are already the linear float4 consumed by the shader, and may
                    // legitimately exceed 1.0, so export them losslessly as Vector4.
                    typeStr = "Vector4";
                    defaultValue = FormatVector4Default(new Vector4(
                        prop.defaultColor.r, prop.defaultColor.g,
                        prop.defaultColor.b, prop.defaultColor.a));
                }
                else
                {
                    typeStr = "Color";
                    defaultValue = FormatColorDefault(prop.defaultColor);
                }
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue} }},";

            case ShaderUtil.ShaderPropertyType.Range:
                typeStr = "Float";
                // ⭐ 直接使用 Unity Range 的实际默认值（从 ShaderUtil.GetRangeLimits(,0) 读取）
                defaultValue = prop.defaultFloat.ToString("G");
                rangeStr = $", range: [{prop.rangeMin:G}, {prop.rangeMax:G}]";
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue}{rangeStr} }},";

            case ShaderUtil.ShaderPropertyType.Float:
                typeStr = "Float";
                // ⭐ 直接使用 Unity 的实际默认 Float 值
                defaultValue = prop.defaultFloat.ToString("G");
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue} }},";

            case ShaderUtil.ShaderPropertyType.Vector:
                typeStr = "Vector4";
                // ⭐ 直接使用 Unity 的实际默认 Vector4 值
                defaultValue = FormatVector4Default(prop.defaultVector);
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue} }},";

            case ShaderUtil.ShaderPropertyType.Int:
                typeStr = "Int";
                defaultValue = prop.defaultInt.ToString();
                return $"        {prop.layaName}: {{ type: {typeStr}, default: {defaultValue} }},";

            default:
                return null;
        }
    }

    /// <summary>
    /// 使用指定的Laya名称生成uniform行（用于2D shader等需要覆盖名称的场景）
    /// </summary>
    private static string GenerateUniformLineWithName(ShaderProperty prop, string overrideName)
    {
        string originalName = prop.layaName;
        prop.layaName = overrideName;
        string result = GenerateUniformLine(prop);
        prop.layaName = originalName;
        return result;
    }

    /// <summary>
    /// 导出材质文件
    /// </summary>
    private static void ExportMaterialFile(Material material, Shader shader, string layaShaderName,
        JSONObject jsonData, ResoureMap resoureMap, MaterialFile materialFile = null,
        string baseLayaShaderName = null)
    {
        // ⭐ 设置模板shader导出上下文（用于属性名过滤、大小写修正、RadioGroup defines推导）
        _currentTemplateVarNames = null;
        _currentTemplatePropertyOverrides = null;
        _currentTemplateRadioGroups = null;
        string templateContent = TryLoadPreConvertedTemplate(layaShaderName);
        // ★ Fallback 查找链（与 ExportShaderFile 保持一致）
        if (templateContent == null && baseLayaShaderName != null && baseLayaShaderName != layaShaderName)
        {
            // Fallback 1: D3 变体尝试 "Mesh_" 前缀命名约定
            if (layaShaderName.EndsWith("_D3"))
                templateContent = TryLoadPreConvertedTemplate("Mesh_" + baseLayaShaderName);
            // Fallback 1b: Effect 变体尝试 "Particle_" 前缀命名约定
            if (templateContent == null && layaShaderName.EndsWith("_Effect"))
                templateContent = TryLoadPreConvertedTemplate("Particle_" + baseLayaShaderName);
            // Fallback 2: 同类型原始名模板
            if (templateContent == null)
                templateContent = TryLoadPreConvertedTemplate(baseLayaShaderName);
        }
        if (templateContent != null)
        {
            _currentTemplateVarNames = ParseTemplateUniformMapNames(templateContent);
            _currentTemplateRadioGroups = ParseTemplateRadioGroups(templateContent);
            if (TemplatePropertyOverrides.TryGetValue(layaShaderName, out var overrides))
                _currentTemplatePropertyOverrides = overrides;
            else if (baseLayaShaderName != null && TemplatePropertyOverrides.TryGetValue(baseLayaShaderName, out overrides))
                _currentTemplatePropertyOverrides = overrides;
        }

        // 硬编码的 RadioGroup 映射优先级高于模板解析结果（更稳定可靠）
        if (RadioGroupDefineMappings.TryGetValue(layaShaderName, out var hardcodedGroups))
            _currentTemplateRadioGroups = hardcodedGroups;
        else if (baseLayaShaderName != null && RadioGroupDefineMappings.TryGetValue(baseLayaShaderName, out hardcodedGroups))
            _currentTemplateRadioGroups = hardcodedGroups;

        // Keyword → Define 映射上下文
        if (TemplateKeywordMappings.TryGetValue(layaShaderName, out var kwMap))
            _currentTemplateKeywordMappings = kwMap;
        else if (baseLayaShaderName != null && TemplateKeywordMappings.TryGetValue(baseLayaShaderName, out kwMap))
            _currentTemplateKeywordMappings = kwMap;

        jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        jsonData.AddField("props", props);

        // 检测材质类型
        LayaMaterialType materialType = DetectMaterialType(shader.name);
        
        // 设置材质类型为自定义Shader名称
        props.AddField("type", layaShaderName);
        
        // ==================== 渲染状态导出（两路径方案）====================
        // 流程：解析shader渲染状态 → 合成实际生效值 → 匹配预定义模式
        //   路径A（匹配成功）：materialRenderMode=N + 标准参数
        //   路径B（不匹配）：materialRenderMode=5(CUSTOM) + 完整的实际渲染状态
        LayaRenderModeConfig matchedRenderMode = null;

        string shaderPath = AssetDatabase.GetAssetPath(shader);
        string matShaderSource = null;
        if (!string.IsNullOrEmpty(shaderPath) && File.Exists(shaderPath))
        {
            try { matShaderSource = File.ReadAllText(shaderPath); }
            catch (System.Exception) { matShaderSource = null; }
        }

        if (matShaderSource != null)
        {
            bool isShaderGraph = shaderPath.EndsWith(".shadergraph", System.StringComparison.OrdinalIgnoreCase);
            ShaderParseResult matParseResult = null;
            ResolvedRenderState resolved;

            if (isShaderGraph)
            {
                // ShaderGraph source is JSON and does not contain ShaderLab pass directives.
                // Read the effective target state serialized in _BUILTIN_* material properties.
                resolved = ResolveShaderGraphRenderState(material);
                ExportLogger.Log($"LayaAir3D: Resolved ShaderGraph render state from material properties: {material.name}");
            }
            else
            {
                // 第一步：解析 shader 渲染状态（保留原始 token，区分硬编码和属性引用）
                matParseResult = new ShaderParseResult();
                ParseRenderState(matShaderSource, matParseResult);

                // 第二步：结合材质数据，解析出实际生效的 Laya 渲染参数
                // ⭐ GUI 脚本属性值修正：某些 shader 的 CustomEditor 在运行时覆盖材质属性，
                // 但 .mat 序列化的是旧值。此处模拟 GUI 脚本逻辑，生成正确的属性值。
                Dictionary<string, int> guiOverrides = BuildShaderGUIOverrides(material, baseLayaShaderName ?? layaShaderName);
                resolved = ResolveRenderState(matParseResult, material, guiOverrides);
            }

            // 第三步：与预定义模式匹配
            matchedRenderMode = MatchRenderMode(resolved);

            if (matchedRenderMode != null)
            {
                // ★ 路径 A：匹配预定义模式
                // 写入 materialRenderMode + 全部标准渲染参数（与 Laya 编辑器 .lmat 输出一致）
                props.AddField("renderQueue", material.renderQueue);
                props.AddField("materialRenderMode", matchedRenderMode.value);
                props.AddField("s_Cull", resolved.s_Cull);
                props.AddField("s_Blend", matchedRenderMode.s_Blend);
                if (matchedRenderMode.s_Blend > 0)
                {
                    props.AddField("s_BlendSrc", matchedRenderMode.s_BlendSrc);
                    props.AddField("s_BlendDst", matchedRenderMode.s_BlendDst);
                }
                props.AddField("s_DepthTest", resolved.s_DepthTest);
                props.AddField("s_DepthWrite", matchedRenderMode.s_DepthWrite);

                ExportLogger.Log($"LayaAir3D: Material '{material.name}' matched render mode " +
                    $"'{matchedRenderMode.value}' (s_Blend={matchedRenderMode.s_Blend}, " +
                    $"s_DepthWrite={matchedRenderMode.s_DepthWrite})");
            }
            else
            {
                // ★ 路径 B：CUSTOM 模式
                // 写入完整实际状态，确保 ShaderGraph 和 ShaderLab 路径的 Custom 材质行为一致。
                props.AddField("renderQueue", material.renderQueue);
                props.AddField("materialRenderMode", 5); // CUSTOM

                // s_Blend 始终写入（标识混合模式类型）
                props.AddField("s_Blend", resolved.s_Blend);

                // Custom 必须保留完整的实际混合状态。ShaderGraph 没有 ShaderLab
                // 解析结果，因此不能依赖 matParseResult 决定是否写出混合因子。
                if (resolved.s_Blend > 0)
                {
                    if (resolved.s_Blend == 2)
                    {
                        props.AddField("s_BlendSrcRGB", resolved.s_BlendSrcRGB);
                        props.AddField("s_BlendDstRGB", resolved.s_BlendDstRGB);
                        props.AddField("s_BlendSrcAlpha", resolved.s_BlendSrcAlpha);
                        props.AddField("s_BlendDstAlpha", resolved.s_BlendDstAlpha);
                    }
                    else
                    {
                        props.AddField("s_BlendSrc", resolved.s_BlendSrc);
                        props.AddField("s_BlendDst", resolved.s_BlendDst);
                    }
                }

                // Cull / ZWrite / ZTest — 始终写入材质
                props.AddField("s_Cull", resolved.s_Cull);
                props.AddField("s_DepthTest", resolved.s_DepthTest);
                props.AddField("s_DepthWrite", resolved.s_DepthWrite);

                ExportLogger.Log($"LayaAir3D: Material '{material.name}' → CUSTOM mode " +
                    $"(s_Blend={resolved.s_Blend}, s_Cull={resolved.s_Cull}, " +
                    $"s_DepthWrite={resolved.s_DepthWrite})");
            }
        }
        else
        {
            // Fallback：无法读取 shader 源码时，保留原有启发式逻辑
            props.AddField("renderQueue", material.renderQueue);
            props.AddField("materialRenderMode", PropDatasConfig.GetRenderModule(material));
            props.AddField("s_Cull", PropDatasConfig.GetCull(material));
            int blendEnabled = PropDatasConfig.GetBlend(material);
            props.AddField("s_Blend", blendEnabled);
            if (blendEnabled > 0)
            {
                props.AddField("s_BlendSrc", PropDatasConfig.GetSrcBlend(material));
                props.AddField("s_BlendDst", PropDatasConfig.GetDstBlend(material));
            }
            props.AddField("s_DepthTest", 1); // 默认 LESS
            bool zWrite = PropDatasConfig.GetZWrite(material);
            if (blendEnabled == 1 && !material.HasProperty("_ZWrite"))
                zWrite = false;
            props.AddField("s_DepthWrite", zWrite);
        }

        bool alphaTest = PropDatasConfig.GetAlphaTest(material) ||
            GetFirstMaterialInt(material, 0, "_BUILTIN_AlphaClip", "_AlphaClip") != 0;
        float alphaTestValue = material.HasProperty("_Alpha_Clip_Threshold")
            ? material.GetFloat("_Alpha_Clip_Threshold")
            : PropDatasConfig.GetAlphaTestValue(material);
        props.AddField("alphaTest", alphaTest);
        props.AddField("alphaTestValue", alphaTestValue);
        
        // 导出纹理
        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        List<string> defines = new List<string>();

        // ⭐ Unity shaderKeywords → 模板 defines 映射
        // 对于有 TemplateKeywordMappings 的 shader，直接从材质的 shaderKeywords 映射到模板定义的 define 名
        // 对于没有映射的 shader，相关功能开关通过 RadioGroup Int 属性推导（见下方 Int case 处理）
        if (_currentTemplateKeywordMappings != null)
        {
            foreach (string keyword in material.shaderKeywords)
            {
                if (_currentTemplateKeywordMappings.TryGetValue(keyword, out string define))
                {
                    if (!defines.Contains(define))
                    {
                        defines.Add(define);
                        ExportLogger.Log($"LayaAir3D: Mapped Unity keyword '{keyword}' → define '{define}'");
                    }
                }
            }
        }

        // ★ 检测是否是 2D shader
        bool is2DMaterial = materialFile != null && materialFile.IsUsedBy2DComponent();

        // 收集Shader属性用于特性检测
        List<ShaderProperty> shaderProperties = CollectShaderProperties(shader);

        // 根据材质类型和Shader特性添加必要的宏定义
        bool hasNPR = (materialType == LayaMaterialType.PBR || materialType == LayaMaterialType.Custom) && HasNPRFeatures(shaderProperties);
        bool hasEmission = false;

        // 检测shader源码中是否使用了顶点颜色，自动添加 ENABLEVERTEXCOLOR define
        bool usesVertexColor = DetectVertexColorInShader(shader);
        if (usesVertexColor && !defines.Contains("ENABLEVERTEXCOLOR"))
        {
            defines.Add("ENABLEVERTEXCOLOR");
        }
        
        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < propertyCount; i++)
        {
            string propName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);
            
            if (IsInternalProperty(propName))
                continue;

            // ★ 2D shader 使用专用属性名映射
            string layaName;
            if (is2DMaterial)
            {
                // 跳过 2D 内置属性
                if (Is2DBuiltinProperty(propName))
                    continue;
                layaName = Get2DPropertyName(propName);
                // 跳过 3D 默认属性（由通用映射产生的不适用于 2D 的名称）
                if (layaName == "u_AlbedoTexture" || layaName == "u_AlbedoColor" || layaName == "u_AlbedoIntensity")
                    continue;
            }
            else
            {
                layaName = ConvertToLayaPropertyName(propName);
            }

            // ⭐ 有模板上下文时，layaName 为 null 表示模板中不含此属性，跳过
            if (layaName == null)
                continue;

            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    ExportTextureProperty(material, propName, layaName, textures, defines, resoureMap, shader, i);

                    // ⭐ FIX 2/3: 导出纹理Tiling/Offset（通用方案）
                    // 规则：Unity的 _MainTex/_BaseMap/_AlbedoTexture → u_TilingOffset
                    //       其他纹理 _XXX → u_XXX_ST
                    // 格式：[scaleX, scaleY, offsetX, offsetY]
                    // ★ 2D shader 不需要 TilingOffset
                    if (!is2DMaterial)
                        ExportTextureTilingOffset(material, propName, layaName, props);
                    break;

                case ShaderUtil.ShaderPropertyType.Color:
                    // HDR颜色检测：查找对应的ShaderProperty获取isHDR标记
                    bool isHDRColor = false;
                    var matchedProp = shaderProperties.Find(p => p.unityName == propName);
                    if (matchedProp != null) isHDRColor = matchedProp.isHDR;
                    ExportColorProperty(material, propName, layaName, props, isHDRColor);
                    // 检测自发光
                    if (propName.Contains("Emission"))
                    {
                        Color emColor = material.GetColor(propName);
                        if (emColor.r > 0 || emColor.g > 0 || emColor.b > 0)
                        {
                            hasEmission = true;
                        }
                    }
                    break;
                    
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    // ⭐ RadioGroup：Float 类型的枚举属性（如 _WrapMode [KeywordEnum(...) Float]）
                    // 同样需要推导 define，并将 float 值舍入为整数索引
                    if (_currentTemplateRadioGroups != null &&
                        _currentTemplateRadioGroups.TryGetValue(layaName, out string[] floatRadioMembers))
                    {
                        float rawFloat = material.GetFloat(propName);
                        int floatIdx = Mathf.RoundToInt(rawFloat);
                        // 优先尝试 0-based 直接索引，超出范围时退回 1-based 偏移
                        if (floatIdx < 0 || floatIdx >= floatRadioMembers.Length)
                            floatIdx = Mathf.RoundToInt(rawFloat) - 1;

                        if (floatIdx >= 0 && floatIdx < floatRadioMembers.Length)
                        {
                            // RadioGroup 在 Laya 中以整数索引存储
                            props.AddField(layaName, floatIdx);
                            string floatRadioDefine = floatRadioMembers[floatIdx];
                            if (!string.IsNullOrEmpty(floatRadioDefine) && !defines.Contains(floatRadioDefine))
                                defines.Add(floatRadioDefine);
                        }
                        else
                        {
                            ExportFloatProperty(material, propName, layaName, props);
                        }
                    }
                    else
                    {
                        ExportFloatProperty(material, propName, layaName, props);
                    }
                    // 检测自发光强度
                    if (propName.Contains("Emission") && propName.Contains("Scale"))
                    {
                        float emScale = material.GetFloat(propName);
                        if (emScale > 0)
                        {
                            hasEmission = true;
                        }
                    }
                    break;

                case ShaderUtil.ShaderPropertyType.Int:
                    // ⭐ RadioGroup：根据 Int 值推导对应的 define，并将值转换为 Laya 兼容的 0-based 索引
                    if (_currentTemplateRadioGroups != null &&
                        _currentTemplateRadioGroups.TryGetValue(layaName, out string[] radioMembers))
                    {
                        int unityIntVal = material.GetInt(propName);
                        // 优先尝试 0-based 直接索引，超出范围时退回 1-based 偏移
                        // （Unity 枚举有时从 1 开始：1=第一项，2=第二项，3=第三项）
                        int idx = unityIntVal;
                        if (idx < 0 || idx >= radioMembers.Length)
                            idx = unityIntVal - 1;

                        if (idx >= 0 && idx < radioMembers.Length)
                        {
                            // 写入 0-based 索引（Laya RadioGroup 的约定）
                            props.AddField(layaName, idx);
                            // 添加对应的 define
                            string radioDefine = radioMembers[idx];
                            if (!string.IsNullOrEmpty(radioDefine) && !defines.Contains(radioDefine))
                                defines.Add(radioDefine);
                        }
                        else
                        {
                            // 无法映射时回退：直接写原值，不添加 define
                            ExportIntProperty(material, propName, layaName, props);
                        }
                    }
                    else
                    {
                        ExportIntProperty(material, propName, layaName, props);
                    }
                    break;

                case ShaderUtil.ShaderPropertyType.Vector:
                    ExportVectorProperty(material, propName, layaName, props);
                    break;
            }
        }

        // ⭐ 组装 Vector2 属性：将 Unity 的分离 float 属性（如 _Scroll0X/_Scroll0Y）合并为 Laya Vector2
        AssembleTemplateVector2Properties(material, layaShaderName, baseLayaShaderName, props);

        props.AddField("textures", textures);

        // 根据材质类型添加功能性宏定义
        if (materialType == LayaMaterialType.PBR || materialType == LayaMaterialType.Custom)
        {
            if (hasNPR && !defines.Contains("USENPR"))
            {
                defines.Add("USENPR");
            }
            
            if (hasEmission && !defines.Contains("EMISSION"))
            {
                defines.Add("EMISSION");
            }
            
        }
        else if (materialType == LayaMaterialType.BLINNPHONG || materialType == LayaMaterialType.Unlit)
        {
        }
        // ⭐ Note: Effect类型（粒子）的宏定义由Keywords映射自动生成，不再手动添加

        // ★ 2D shader 材质添加 BASERENDER2D define
        if (is2DMaterial)
        {
            if (!defines.Contains("BASERENDER2D"))
                defines.Add("BASERENDER2D");
        }

        // ⭐ FIX 4/4: 粒子Mesh渲染模式修复 - 添加RENDERMODE_MESH define
        // 当粒子系统使用Mesh渲染模式时，shader需要v_MeshColor变量
        // 必须添加RENDERMODE_MESH宏来启用条件编译块
        if (materialFile != null && materialFile.IsParticleMeshMode())
        {
            if (!defines.Contains("RENDERMODE_MESH"))
            {
                defines.Add("RENDERMODE_MESH");
                ExportLogger.Log($"LayaAir3D: Added RENDERMODE_MESH define for particle mesh rendering mode");
            }
        }

        // 匹配模式特有的 defines（如 ADDTIVE 模式的 ADDTIVEFOG）
        if (matchedRenderMode != null && matchedRenderMode.defines != null)
        {
            foreach (var d in matchedRenderMode.defines)
            {
                if (!defines.Contains(d))
                    defines.Add(d);
            }
        }

        // 添加宏定义
        JSONObject definesArray = new JSONObject(JSONObject.Type.ARRAY);
        foreach (string define in defines)
            definesArray.Add(define);
        props.AddField("defines", definesArray);

        ExportLogger.Log($"LayaAir3D: Exported material '{material.name}' as type '{layaShaderName}' (MaterialType: {materialType})");

        // ⭐ 清除模板shader导出上下文
        _currentTemplateVarNames = null;
        _currentTemplatePropertyOverrides = null;
        _currentTemplateRadioGroups = null;
        _currentTemplateKeywordMappings = null;
    }

    /// <summary>
    /// 组装 Vector2 属性：将 Unity 中分开存储的 float 属性对合并为 Laya 的 Vector2 属性。
    /// 例如 Unity 的 _Scroll0X + _Scroll0Y → Laya 的 u_Scroll0: [x, y]
    /// </summary>
    private static void AssembleTemplateVector2Properties(Material material, string layaShaderName, string baseLayaShaderName, JSONObject props)
    {
        Vector2AssemblyRule[] rules = null;

        if (layaShaderName != null && TemplateVector2Assemblies.TryGetValue(layaShaderName, out rules))
        {
            // found
        }
        else if (baseLayaShaderName != null && TemplateVector2Assemblies.TryGetValue(baseLayaShaderName, out rules))
        {
            // found
        }

        if (rules == null) return;

        foreach (var rule in rules)
        {
            bool hasX = material.HasProperty(rule.xProp);
            bool hasY = material.HasProperty(rule.yProp);

            if (!hasX && !hasY) continue;

            float x = hasX ? material.GetFloat(rule.xProp) : 0f;
            float y = hasY ? material.GetFloat(rule.yProp) : 0f;

            // 模板验证：只有模板中存在该属性时才导出
            if (_currentTemplateVarNames != null && !_currentTemplateVarNames.Contains(rule.layaName))
                continue;

            JSONObject vec2 = new JSONObject(JSONObject.Type.ARRAY);
            vec2.Add(x);
            vec2.Add(y);
            props.AddField(rule.layaName, vec2);

            ExportLogger.Log($"LayaAir3D: Assembled Vector2 '{rule.xProp}'+'{rule.yProp}' → '{rule.layaName}': [{x}, {y}]");
        }
    }

    /// <summary>
    /// 导出纹理属性
    /// </summary>
    private static void ExportTextureProperty(Material material, string propName, string layaName,
        JSONObject textures, List<string> defines, ResoureMap resoureMap, Shader shader = null, int propertyIndex = -1)
    {
        if (!material.HasProperty(propName)) return;
        
        Texture tex = material.GetTexture(propName);
        if (tex == null) return;
        
        string texPath = AssetDatabase.GetAssetPath(tex.GetInstanceID());
        if (string.IsNullOrEmpty(texPath)) return;
        if (ResoureMap.IsBuiltinResource(texPath)) return;
        
        bool isNormal = propName.ToLower().Contains("normal") || propName.ToLower().Contains("bump");
        bool isCubemap = false;
        
        // 检测是否是Cubemap
        if (shader != null && propertyIndex >= 0)
        {
            var texDim = ShaderUtil.GetTexDim(shader, propertyIndex);
            if (texDim == UnityEngine.Rendering.TextureDimension.Cube)
            {
                isCubemap = true;
            }
        }
        
        // 也通过属性名检测Cubemap
        if (propName.ToLower().Contains("ibl") || propName.ToLower().Contains("cube"))
        {
            isCubemap = true;
        }
        
        // Cubemap需要特殊处理
        if (isCubemap)
        {
            Cubemap cubemap = tex as Cubemap;
            if (cubemap != null)
            {
                ExportCubemapTexture(cubemap, layaName, textures, defines, propName, resoureMap);
            }
            else
            {
                Debug.LogWarning($"LayaAir3D: Property '{propName}' detected as Cubemap but texture is not Cubemap type, skipping.");
                string define = GenerateTextureDefine(propName);
                if (!defines.Contains(define)) defines.Add(define);
            }
            return;
        }
        
        TextureFile textureFile = resoureMap.GetTextureFile(tex, isNormal);
        if (textureFile != null)
        {
            textures.Add(textureFile.jsonObject(layaName));
            
            // 添加宏定义
            string define = GenerateTextureDefine(propName);
            if (!defines.Contains(define))
            {
                defines.Add(define);
            }
        }
    }

    /// <summary>
    /// 导出 Cubemap 纹理：拆为 6 面 PNG，创建 .cubemap JSON，添加到材质 textures 数组
    /// </summary>
    private static void ExportCubemapTexture(Cubemap cubemap, string layaName,
        JSONObject textures, List<string> defines, string propName, ResoureMap resoureMap)
    {
        string assetPath = AssetDatabase.GetAssetPath(cubemap);
        if (string.IsNullOrEmpty(assetPath) || ResoureMap.IsBuiltinResource(assetPath))
            return;

        int size = cubemap.width;
        string basePath = assetPath.Substring(0, assetPath.LastIndexOf('.'));
        string cubemapPath = basePath + ".cubemap";

        // 避免同一 Cubemap 重复导出
        JsonFile cubeFile;
        if (resoureMap.HaveFileData(cubemapPath))
        {
            cubeFile = resoureMap.GetFileData(cubemapPath) as JsonFile;
        }
        else
        {
            cubeFile = new JsonFile(cubemapPath, new JSONObject(JSONObject.Type.OBJECT));
            resoureMap.AddExportFile(cubeFile);

            // 确保 Cubemap 可读（与 TextureFile 处理方式一致）
            TextureImporter cubemapImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool wasReadable = true;
            if (cubemapImporter != null && !cubemapImporter.isReadable)
            {
                wasReadable = false;
                cubemapImporter.isReadable = true;
                AssetDatabase.ImportAsset(assetPath);
                // 重新获取 Cubemap 引用（reimport 后旧引用失效）
                cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
                size = cubemap.width;
            }

            // Unity CubemapFace → Laya .cubemap JSON key
            CubemapFace[] faces = {
                CubemapFace.PositiveZ, CubemapFace.NegativeZ,
                CubemapFace.NegativeX, CubemapFace.PositiveX,
                CubemapFace.PositiveY, CubemapFace.NegativeY
            };
            string[] keys = { "front", "back", "left", "right", "top", "bottom" };

            for (int fi = 0; fi < faces.Length; fi++)
            {
                CubemapFace face = faces[fi];
                string key = keys[fi];

                // 提取面像素并编码为 PNG
                Texture2D faceTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                faceTex.SetPixels(cubemap.GetPixels(face));
                faceTex.Apply();
                byte[] pngBytes = faceTex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(faceTex);

                // 用 BytesFile 注册到导出管线
                string facePath = basePath + "_" + key + ".png";
                if (!resoureMap.HaveFileData(facePath))
                {
                    resoureMap.AddExportFile(new BytesFile(facePath, pngBytes));
                }

                cubeFile.jsonData.AddField(key, "res://" + facePath);
                cubeFile.AddRegistList(facePath);
            }

            cubeFile.jsonData.AddField("cubemapSize", size);
            cubeFile.jsonData.AddField("filterMode", 1);
            cubeFile.jsonData.AddField("cubemapFileMode", "R8G8B8A8");
            cubeFile.jsonData.AddField("mipmapCoverageIBL", true);
            cubeFile.jsonData.AddField("generateMipmap", true);
            cubeFile.jsonData.AddField("sRGB", true);

            // 恢复原始 isReadable 设置
            if (cubemapImporter != null && !wasReadable)
            {
                cubemapImporter.isReadable = false;
                AssetDatabase.ImportAsset(assetPath);
            }
        }

        // 添加到 .lmat textures 数组（格式与 Skybox 路径一致）
        JSONObject constructParams = new JSONObject(JSONObject.Type.ARRAY);
        constructParams.Add(size);
        constructParams.Add(size);
        constructParams.Add(0);      // R8G8B8A8
        constructParams.Add(false);  // mipmap
        constructParams.Add(false);  // canRead
        constructParams.Add(true);   // sRGB

        JSONObject propertyParams = new JSONObject(JSONObject.Type.OBJECT);
        propertyParams.AddField("filterMode", 1);
        propertyParams.AddField("wrapModeU", 0);
        propertyParams.AddField("wrapModeV", 0);
        propertyParams.AddField("anisoLevel", 4);

        JSONObject texObj = new JSONObject(JSONObject.Type.OBJECT);
        texObj.AddField("name", layaName);
        texObj.AddField("path", "res://" + cubeFile.uuid);
        texObj.AddField("constructParams", constructParams);
        texObj.AddField("propertyParams", propertyParams);
        textures.Add(texObj);

        // 添加宏定义
        string define = GenerateTextureDefine(propName);
        if (!defines.Contains(define))
        {
            defines.Add(define);
        }
    }

    /// <summary>
    /// 导出纹理Tiling/Offset（通用方案）
    /// Unity纹理的Scale和Offset映射为Laya的_ST uniform
    /// 规则：_MainTex/_BaseMap/_AlbedoTexture → u_TilingOffset
    ///       其他纹理 _XXX → u_XXX_ST
    /// 格式：[scaleX, scaleY, offsetX, offsetY]
    /// </summary>
    private static void ExportTextureTilingOffset(Material material, string unityPropName, string layaPropName, JSONObject props)
    {
        if (!material.HasProperty(unityPropName))
            return;

        // 获取纹理的Tiling和Offset
        Vector2 scale = material.GetTextureScale(unityPropName);
        Vector2 offset = material.GetTextureOffset(unityPropName);

        // 确定Laya属性名
        string tilingOffsetName;

        // 主纹理使用 u_TilingOffset（注意：_MainTex 可能已通过覆盖表映射为 u_texture，
        // 但 tiling/offset 仍然对应 u_TilingOffset，故此处继续用 unityPropName 判断）
        if (unityPropName == "_MainTex" || unityPropName == "_BaseMap" || unityPropName == "_AlbedoTexture")
        {
            tilingOffsetName = "u_TilingOffset";
        }
        else
        {
            // 使用 Laya 属性名构造 _ST（这样 TemplatePropertyOverrides 的重命名会被正确传递）
            // 例如：_DistortTex0 → layaPropName="u_DistortTex" → tilingOffsetName="u_DistortTex_ST"
            tilingOffsetName = layaPropName + "_ST";
        }

        // ⭐ 有模板上下文时：验证 tilingOffsetName 是否在模板变量集合中（大小写不敏感）
        if (_currentTemplateVarNames != null)
        {
            if (!_currentTemplateVarNames.Contains(tilingOffsetName))
            {
                // 尝试大小写不敏感匹配（如 u_DistortTex_ST → u_distort_tex_ST）
                string matched = null;
                foreach (string templateVar in _currentTemplateVarNames)
                {
                    if (string.Equals(templateVar, tilingOffsetName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        matched = templateVar;
                        break;
                    }
                }
                if (matched != null)
                    tilingOffsetName = matched;
                else
                    return; // 模板中不含此 tiling/offset 属性，跳过
            }
        }

        // 添加到材质数据
        JSONObject tilingOffsetValue = new JSONObject(JSONObject.Type.ARRAY);
        tilingOffsetValue.Add(scale.x);
        tilingOffsetValue.Add(scale.y);
        tilingOffsetValue.Add(offset.x);
        tilingOffsetValue.Add(offset.y);

        props.AddField(tilingOffsetName, tilingOffsetValue);

        ExportLogger.Log($"LayaAir3D: Exported texture tiling/offset '{unityPropName}' as '{tilingOffsetName}': [{scale.x}, {scale.y}, {offset.x}, {offset.y}]");
    }

    /// <summary>
    /// 导出颜色属性。HDR 颜色保留 Unity shader 使用的原始线性 float4，
    /// 不归一化、不截断，并由模板声明为 Vector4 以绕过 Laya Color 的 gamma 转换。
    /// </summary>
    private static void ExportColorProperty(Material material, string propName, string layaName, JSONObject props, bool isHDR = false)
    {
        if (!material.HasProperty(propName)) return;

        JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
        if (isHDR)
        {
            // HDR values can exceed 1.0. Preserve RGB intensity and alpha exactly.
            Vector4 vec = material.GetVector(propName);
            colorValue.Add(vec.x);
            colorValue.Add(vec.y);
            colorValue.Add(vec.z);
            colorValue.Add(vec.w);
        }
        else
        {
            Color color = material.GetColor(propName);
            colorValue.Add(color.r);
            colorValue.Add(color.g);
            colorValue.Add(color.b);
            colorValue.Add(color.a);
        }
        props.AddField(layaName, colorValue);
    }

    /// <summary>
    /// 导出浮点属性
    /// </summary>
    private static void ExportFloatProperty(Material material, string propName, string layaName, JSONObject props)
    {
        if (!material.HasProperty(propName)) return;

        float value = material.GetFloat(propName);
        props.AddField(layaName, value);
    }

    /// <summary>
    /// 导出整型属性（对应 Unity ShaderPropertyType.Int，如 LayerType、WrapMode 等枚举控制量）
    /// </summary>
    private static void ExportIntProperty(Material material, string propName, string layaName, JSONObject props)
    {
        if (!material.HasProperty(propName)) return;

        int value = material.GetInt(propName);
        props.AddField(layaName, value);
    }

    /// <summary>
    /// 导出向量属性
    /// </summary>
    private static void ExportVectorProperty(Material material, string propName, string layaName, JSONObject props)
    {
        if (!material.HasProperty(propName)) return;
        
        Vector4 vec = material.GetVector(propName);
        
        JSONObject vecValue = new JSONObject(JSONObject.Type.ARRAY);
        vecValue.Add(vec.x);
        vecValue.Add(vec.y);
        vecValue.Add(vec.z);
        vecValue.Add(vec.w);
        props.AddField(layaName, vecValue);
    }

    /// <summary>
    /// 检测Shader是否使用了顶点颜色
    /// </summary>
    private static bool DetectVertexColorInShader(Shader shader)
    {
        if (shader == null) return false;
        
        try
        {
            // 获取Shader源码路径
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath)) return false;
            
            // 读取Shader源码
            string sourceCode = "";
            if (File.Exists(shaderPath))
            {
                sourceCode = File.ReadAllText(shaderPath);
            }
            
            if (string.IsNullOrEmpty(sourceCode)) return false;
            
            // 检查结构体中是否有COLOR语义
            if (Regex.IsMatch(sourceCode, @":\s*COLOR\d*\s*;", RegexOptions.IgnoreCase))
                return true;
            
            // 检查代码中是否使用了顶点颜色相关的变量
            string[] colorPatterns = new string[]
            {
                @"\bv\.color\b",
                @"\bi\.color\b",
                @"\bo\.color\b",
                @"\bvertex\.color\b",
                @"\binput\.color\b",
                @"\bIN\.color\b",
                @"\bOUT\.color\b",
                @"\bv\.vcolor\b",
                @"\bi\.vcolor\b",
                @"\bvertexColor\b",
                @"\bv_VertexColor\b",
                @"\ba_Color\b",
                @"\bappdata_full\b"  // Unity内置结构体，包含顶点颜色
            };
            
            foreach (var pattern in colorPatterns)
            {
                if (Regex.IsMatch(sourceCode, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"LayaAir3D: Failed to detect vertex color usage in shader: {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Shader文件 - 用于导出.shader文件
/// </summary>
internal class ShaderFile : FileData
{
    private string content;
    
    public ShaderFile(string path, string content) : base(path)
    {
        this.content = content;
    }
    
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        try
        {
            if (ExportConfig.PreserveExistingShaderFiles && File.Exists(this.outPath))
            {
                ExportLogger.Log($"LayaAir3D: Preserved existing shader file: {this.outPath}");
                return;
            }

            string directory = Path.GetDirectoryName(this.outPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(this.outPath, content, Encoding.UTF8);
            ExportLogger.Log($"LayaAir3D: Saved shader file: {this.outPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"LayaAir3D: Failed to save shader file: {e.Message}");
        }
    }
}
