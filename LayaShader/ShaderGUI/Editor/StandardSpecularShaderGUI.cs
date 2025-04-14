using System;
using UnityEngine;
using UnityEditor;
public class StandardSpecularShaderGUI : ShaderGUI
{

    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        SetupMaterialWithBlendMode(material, (BlendMode)material.GetFloat("_Mode"));
        string[] shaderKeywords = material.shaderKeywords;
        for (int i = 0; i < shaderKeywords.Length; i++)
        {
            material.DisableKeyword(shaderKeywords[i]);
        }
        if (oldShader.name == "Standard" || oldShader.name == "Standard (Specular setup)")
        {

            material.SetColor("_Color", material.GetColor("_Color"));
            material.SetTexture("albedoTexture", material.GetTexture("_MainTex"));
            material.SetFloat("smoothness", material.GetFloat("_Glossiness"));
            material.SetFloat("smoothnessTextureScale", material.GetFloat("_GlossMapScale"));
            smoothnessMapChannelGUI = (SmoothnessMapChannel)material.GetFloat("_SmoothnessTextureChannel");
            material.SetInt("_SmoothnessMapChannelGUI", (int)smoothnessMapChannelGUI);
            //if (oldShader.name == "Standard")
            //{
            //    material.SetFloat("metallic", material.GetFloat("_Metallic"));
            //    material.SetTexture("metallicGlossTexture", material.GetTexture("_MetallicGlossMap"));
            //}
            if (oldShader.name == "Standard (Specular setup)")
            {
                material.SetColor("specularColor", material.GetColor("_SpecColor"));
                material.SetTexture("specularTexture", material.GetTexture("_SpecGlossMap"));
            }
            material.SetFloat("normalTextureScale", material.GetFloat("_BumpScale"));
            material.SetTexture("normalTexture", material.GetTexture("_BumpMap"));
            material.SetFloat("parallaxTextureScale", material.GetFloat("_Parallax"));
            material.SetTexture("parallaxTexture", material.GetTexture("_ParallaxMap"));
            material.SetFloat("occlusionTextureStrength", material.GetFloat("_OcclusionStrength"));
            material.SetTexture("occlusionTexture", material.GetTexture("_OcclusionMap"));
            material.SetColor("_EmissionColor", material.GetColor("_EmissionColor"));
            material.SetTexture("_EmissionMap", material.GetTexture("_EmissionMap"));
        }
        else
        {
           
            material.SetInt("_SmoothnessMapChannelGUI", (int)SmoothnessMapChannel.SpecularAlpha);
          
        }
        SetMaterialKeywords(material);
        material.shader = newShader;
    }
    public enum BlendMode
    {
        Opaque,
        Cutout,
        Fade,   // 旧的阿尔法混合模式，菲涅耳不影响透明度
        Transparent // 预乘
    }
    public enum SmoothnessMapChannel
    {
        SpecularAlpha,
        AlbedoAlpha
    }
    MaterialProperty _Color = null;
    MaterialProperty albedoMap = null;
    MaterialProperty alphaCutoff = null;
    MaterialProperty smoothness = null;
    MaterialProperty smoothnessScale = null;
    MaterialProperty smoothnessMapChannel = null;
    MaterialProperty specularColor = null;
    MaterialProperty specularMap = null;
    //MaterialProperty highlights = null;
    //MaterialProperty reflections = null;
    MaterialProperty bumpScale = null;
    MaterialProperty bumpMap = null;
    MaterialProperty heigtMapScale = null;
    MaterialProperty heightMap = null;
    MaterialProperty occlusionStrength = null;
    MaterialProperty occlusionMap = null;
    MaterialProperty _EmissionColor = null;
    MaterialProperty emissionMap = null;
    //MaterialProperty detailMask = null;
    //MaterialProperty detailAlbedoMap = null;
    //MaterialProperty detailNormalMapScale = null;
    //MaterialProperty detailNormalMap = null;
    //MaterialProperty uvSetSecondary = null;
    MaterialProperty tilingOffset = null;
    MaterialProperty blendMode = null;

    MaterialEditor m_MaterialEditor;

    bool m_FirstTimeApply = true;
    SmoothnessMapChannel smoothnessMapChannelGUI = SmoothnessMapChannel.SpecularAlpha;
    public void FindProperties(MaterialProperty[] props)
    {
        _Color = FindProperty("_Color", props);
        albedoMap = FindProperty("albedoTexture", props);
        alphaCutoff = FindProperty("_Cutoff", props);
        smoothness = FindProperty("smoothness", props);
        smoothnessScale = FindProperty("smoothnessTextureScale", props, false);
       // smoothnessMapChannel = FindProperty("_SmoothnessTextureChannel", props, false);
        specularColor = FindProperty("specularColor", props);
        specularMap = FindProperty("specularTexture", props);
        //highlights = FindProperty("_SpecularHighlights", props);
        //reflections = FindProperty("_GlossyReflections", props);
        bumpScale = FindProperty("normalTextureScale", props);
        bumpMap = FindProperty("normalTexture", props);
        heigtMapScale = FindProperty("parallaxTextureScale", props);
        heightMap = FindProperty("parallaxTexture", props);
        occlusionStrength = FindProperty("occlusionTextureStrength", props);
        occlusionMap = FindProperty("occlusionTexture", props);
        _EmissionColor = FindProperty("_EmissionColor", props);
        emissionMap = FindProperty("_EmissionMap", props);
        tilingOffset = FindProperty("tilingOffset", props);
        //detailMask = FindProperty("_DetailMask", props);
        //detailAlbedoMap = FindProperty("_DetailAlbedoMap", props);
        //detailNormalMapScale = FindProperty("_DetailNormalMapScale", props);
        //detailNormalMap = FindProperty("_DetailNormalMap", props);
        //uvSetSecondary = FindProperty("_UVSec", props);
        blendMode = FindProperty("_Mode", props);

    }
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        FindProperties(props);
        m_MaterialEditor = materialEditor;
        Material material = materialEditor.target as Material;
        if (m_FirstTimeApply)
        {
            MaterialChanged(material);
            m_FirstTimeApply = false;
            if(material.HasProperty("_SmoothnessMapChannelGUI"))
            smoothnessMapChannelGUI = (SmoothnessMapChannel)material.GetInt("_SmoothnessMapChannelGUI");
        }
        ShaderPropertiesGUI(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        EditorGUIUtility.labelWidth = 0f;
        EditorGUI.BeginChangeCheck();
        {
            BlendModePopup();
            GUILayout.Label(Styles.primaryMapsText, EditorStyles.boldLabel);
            DoAlbedoArea(material);
            DoSpecularArea(material);
            DoNormalArea();
            m_MaterialEditor.TexturePropertySingleLine(Styles.heightMapText, heightMap, heightMap.textureValue != null ? heigtMapScale : null);
            m_MaterialEditor.TexturePropertySingleLine(Styles.occlusionText, occlusionMap, occlusionMap.textureValue != null ? occlusionStrength : null);
            //m_MaterialEditor.TexturePropertySingleLine(Styles.detailMaskText, detailMask);
            DoEmissionArea(material);

            //EditorGUI.BeginChangeCheck();
            //m_MaterialEditor.TextureScaleOffsetProperty(albedoMap);
            //if (EditorGUI.EndChangeCheck())
            //    emissionMap.textureScaleAndOffset = albedoMap.textureScaleAndOffset; // Apply the main texture scale and offset to the emission texture as well, for Enlighten's sake

            //EditorGUILayout.Space();
            GUILayout.Label(Styles.tillingOffset);
            m_MaterialEditor.VectorProperty(tilingOffset, "");

            //GUILayout.Label(Styles.secondaryMapsText, EditorStyles.boldLabel);
            //m_MaterialEditor.TexturePropertySingleLine(Styles.detailAlbedoText, detailAlbedoMap);
            //m_MaterialEditor.TexturePropertySingleLine(Styles.detailNormalMapText, detailNormalMap, detailNormalMapScale);
            //m_MaterialEditor.TextureScaleOffsetProperty(detailAlbedoMap);
            //m_MaterialEditor.ShaderProperty(uvSetSecondary, Styles.uvSetLabel.text);


            //GUILayout.Label(Styles.forwardText, EditorStyles.boldLabel);
            //if (highlights != null)
            //    m_MaterialEditor.ShaderProperty(highlights, Styles.highlightsText);
            //if (reflections != null)
            //    m_MaterialEditor.ShaderProperty(reflections, Styles.reflectionsText);
        }
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var obj in blendMode.targets)
                MaterialChanged((Material)obj);
        }
        EditorGUILayout.Space();

    }
    void BlendModePopup()
    {
        
        EditorGUI.showMixedValue = blendMode.hasMixedValue;
        var mode = (BlendMode)blendMode.floatValue;
        EditorGUI.BeginChangeCheck();
        mode = (BlendMode)EditorGUILayout.Popup(Styles.renderingMode, (int)mode, Styles.blendNames);
        if (EditorGUI.EndChangeCheck())
        {
            m_MaterialEditor.RegisterPropertyChangeUndo("Rendering Mode");
            blendMode.floatValue = (float)mode;
        }
        EditorGUI.showMixedValue = false;
    }

    void DoNormalArea()
    {
        m_MaterialEditor.TexturePropertySingleLine(Styles.normalMapText, bumpMap, bumpMap.textureValue != null ? bumpScale : null);
        if (bumpScale.floatValue != 1 && UnityEditorInternal.InternalEditorUtility.IsMobilePlatform(EditorUserBuildSettings.activeBuildTarget))
            if (m_MaterialEditor.HelpBoxWithButton(
                EditorGUIUtility.TrTextContent("Bump scale is not supported on mobile platforms"),
                EditorGUIUtility.TrTextContent("Fix Now")))
            {
                bumpScale.floatValue = 1;
            }
    }
    void DoAlbedoArea(Material material)
    {
        m_MaterialEditor.TexturePropertySingleLine(Styles.albedoText, albedoMap, _Color);
        if (((BlendMode)material.GetFloat("_Mode") == BlendMode.Cutout))
        {
            m_MaterialEditor.ShaderProperty(alphaCutoff, Styles.alphaCutoffText.text, MaterialEditor.kMiniTextureFieldLabelIndentLevel + 1);
        }
    }

    void DoEmissionArea(Material material)
    {
        
        if (m_MaterialEditor.EmissionEnabledProperty())
            {
                bool hadEmissionTexture = emissionMap.textureValue != null;

                
            m_MaterialEditor.TexturePropertyWithHDRColor(Styles.emissionText, emissionMap, _EmissionColor, false);

                
            float brightness = _EmissionColor.colorValue.maxColorComponent;
            if (emissionMap.textureValue != null && !hadEmissionTexture && brightness <= 0f)
                _EmissionColor.colorValue = Color.white;

                
             //  m_MaterialEditor.LightmapEmissionFlagsProperty(MaterialEditor.kMiniTextureFieldLabelIndentLevel, true);
            }
    }
    static void MaterialChanged(Material material)
    {
        SetupMaterialWithBlendMode(material, (BlendMode)material.GetFloat("_Mode"));
        SetMaterialKeywords(material);
    }

    void DoSpecularArea(Material material)
    {
        bool hasGlossMap = false;
        hasGlossMap = specularMap.textureValue != null;
        m_MaterialEditor.TexturePropertySingleLine(Styles.specularMapText, specularMap, hasGlossMap ? null : specularColor);
        bool showSmoothnessScale = hasGlossMap;
        if (smoothnessMapChannel != null)
        {
            int smoothnessChannel = (int)smoothnessMapChannel.floatValue;
            if (smoothnessChannel == (int)SmoothnessMapChannel.AlbedoAlpha)
                showSmoothnessScale = true;
        }
        int indentation = 2; // align with labels of texture properties
        m_MaterialEditor.ShaderProperty(showSmoothnessScale ? smoothnessScale : smoothness, showSmoothnessScale ? Styles.smoothnessScaleText : Styles.smoothnessText, indentation);
        ++indentation;


        //miner12.23
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(27));
        //GUILayout.Label("Source",GUILayout.Width(153));

        smoothnessMapChannelGUI = (SmoothnessMapChannel)EditorGUILayout.EnumPopup("Source", smoothnessMapChannelGUI, GUILayout.ExpandWidth(true));

        if (smoothnessMapChannelGUI == SmoothnessMapChannel.AlbedoAlpha)
        {
            material.EnableKeyword("SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA");
        }
        else
        {
            material.DisableKeyword("SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA");
        }
        material.SetInt("_SmoothnessMapChannelGUI", (int)smoothnessMapChannelGUI);
        GUILayout.EndHorizontal();
        //if (smoothnessMapChannel != null)
        //    m_MaterialEditor.ShaderProperty(smoothnessMapChannel, Styles.smoothnessMapChannelText, indentation);
    }

    static void SetKeyword(Material m, string keyword, bool state)
    {
        if (state)
            m.EnableKeyword(keyword);
        else
            m.DisableKeyword(keyword);
    }

    public static void SetupMaterialWithBlendMode(Material material, BlendMode blendMode)
    {
        switch (blendMode)
        {
            case BlendMode.Opaque:
                material.SetOverrideTag("RenderType", "");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.DisableKeyword("ALPHATEST");
             
                material.DisableKeyword("TRANSPARENTBLEND");
                material.renderQueue = -1;
                break;
            case BlendMode.Cutout:
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.EnableKeyword("ALPHATEST");
            
                material.DisableKeyword("TRANSPARENTBLEND");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
            case BlendMode.Fade:
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("ALPHATEST");
       
                material.DisableKeyword("TRANSPARENTBLEND");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
            case BlendMode.Transparent:
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("ALPHATEST");
        
                material.EnableKeyword("TRANSPARENTBLEND");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
        }
    }
    //static SmoothnessMapChannel GetSmoothnessMapChannel(Material material)
    //{
    //    int ch = (int)material.GetFloat("_SmoothnessTextureChannel");
    //    if (ch == (int)SmoothnessMapChannel.AlbedoAlpha)
    //        return SmoothnessMapChannel.AlbedoAlpha;
    //    else
    //        return SmoothnessMapChannel.SpecularMetallicAlpha;
    //}

    
    static void SetMaterialKeywords(Material material)
    {
        SetKeyword(material, "NORMALTEXTURE", material.GetTexture("normalTexture") || material.GetTexture("_DetailNormalMap"));
        if(material.HasProperty("specularTexture"))
        SetKeyword(material, "SPECULARGLOSSTEXTURE", material.GetTexture("specularTexture"));
        SetKeyword(material, "PARALLAXTEXTURE", material.GetTexture("parallaxTexture"));
        //SetKeyword(material, "_DETAIL_MULX2", material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap"));
      //  MaterialEditor.FixupEmissiveFlag(material);
        bool shouldEmissionBeEnabled = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
        SetKeyword(material, "_EMISSION", shouldEmissionBeEnabled);
        //if (material.HasProperty("_SmoothnessTextureChannel"))
        //{
        //    SetKeyword(material, "SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA", GetSmoothnessMapChannel(material) == SmoothnessMapChannel.AlbedoAlpha);
        //}
    }



    private static class Styles
    {
        public static GUIContent uvSetLabel = EditorGUIUtility.TrTextContent("UV Set");

        public static GUIContent albedoText = EditorGUIUtility.TrTextContent("Albedo", "Albedo (RGB) and Transparency (A)");
        public static GUIContent alphaCutoffText = EditorGUIUtility.TrTextContent("Alpha Cutoff", "Threshold for alpha cutoff");
        public static GUIContent specularMapText = EditorGUIUtility.TrTextContent("Specular", "Specular (RGB) and Smoothness (A)");
        public static GUIContent smoothnessText = EditorGUIUtility.TrTextContent("Smoothness", "Smoothness value");
        public static GUIContent smoothnessScaleText = EditorGUIUtility.TrTextContent("Smoothness", "Smoothness scale factor");
        public static GUIContent smoothnessMapChannelText = EditorGUIUtility.TrTextContent("Source", "Smoothness texture and channel");
        public static GUIContent highlightsText = EditorGUIUtility.TrTextContent("Specular Highlights", "Specular Highlights");
        public static GUIContent reflectionsText = EditorGUIUtility.TrTextContent("Reflections", "Glossy Reflections");
        public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map", "Normal Map");
        public static GUIContent heightMapText = EditorGUIUtility.TrTextContent("Height Map", "Height Map (G)");
        public static GUIContent occlusionText = EditorGUIUtility.TrTextContent("Occlusion", "Occlusion (G)");
        public static GUIContent emissionText = EditorGUIUtility.TrTextContent("Color", "Emission (RGB)");
        public static GUIContent detailMaskText = EditorGUIUtility.TrTextContent("Detail Mask", "Mask for Secondary Maps (A)");
        public static GUIContent detailAlbedoText = EditorGUIUtility.TrTextContent("Detail Albedo x2", "Albedo (RGB) multiplied by 2");
        public static GUIContent detailNormalMapText = EditorGUIUtility.TrTextContent("Normal Map", "Normal Map");
        public static GUIContent tillingOffset = EditorGUIUtility.TrTextContent("TillingOffset", "Texture tilling and offset");

        public static string primaryMapsText = "Main Maps";
        public static string secondaryMapsText = "Secondary Maps";
        public static string forwardText = "Forward Rendering Options";
        public static string renderingMode = "Rendering Mode";
        public static string advancedText = "Advanced Options";
        public static readonly string[] blendNames = Enum.GetNames(typeof(BlendMode));
    }

}
