//#if UNITY_EDITOR  
using System;
using UnityEngine;
using UnityEditor;

class LayaShaderGUI : ShaderGUI
{


    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        material.shader = newShader;
        material.EnableKeyword("EnableLighting");
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
    }
    public enum RenderMode
    {
        /**渲染状态_不透明。*/
        Opaque = 0,
        /**渲染状态_透明测试。*/
        Cutout = 1,
        /**渲染状态_透明混合。*/
        Transparent = 2,
        /**渲染状态_自定义。*/
        Custom = 3
    }

    public enum SrcBlendMode
    {
        //Blend factor is (0, 0, 0, 0).
        Zero = 0,
        //Blend factor is (1, 1, 1, 1).
        One = 1,
        //Blend factor is (Rd, Gd, Bd, Ad).
        DstColor = 2,
        //Blend factor is (Rs, Gs, Bs, As).
        SrcColor = 3,
        //Blend factor is (1 - Rd, 1 - Gd, 1 - Bd, 1 - Ad).
        OneMinusDstColor = 4,
        //Blend factor is (As, As, As, As).
        SrcAlpha = 5,
        //Blend factor is (1 - Rs, 1 - Gs, 1 - Bs, 1 - As).
        OneMinusSrcColor = 6,
        //Blend factor is (Ad, Ad, Ad, Ad).
        DstAlpha = 7,
        //Blend factor is (1 - Ad, 1 - Ad, 1 - Ad, 1 - Ad).
        OneMinusDstAlpha = 8,
        //Blend factor is (f, f, f, 1); where f = min(As, 1 - Ad).
        SrcAlphaSaturate = 9,
        //Blend factor is (1 - As, 1 - As, 1 - As, 1 - As).
        OneMinusSrcAlpha = 10
    }

    public enum DstBlendMode
    {
        //Blend factor is (0, 0, 0, 0).
        Zero = 0,
        //Blend factor is (1, 1, 1, 1).
        One = 1,
        //Blend factor is (Rd, Gd, Bd, Ad).
        DstColor = 2,
        //Blend factor is (Rs, Gs, Bs, As).
        SrcColor = 3,
        //Blend factor is (1 - Rd, 1 - Gd, 1 - Bd, 1 - Ad).
        OneMinusDstColor = 4,
        //Blend factor is (As, As, As, As).
        SrcAlpha = 5,
        //Blend factor is (1 - Rs, 1 - Gs, 1 - Bs, 1 - As).
        OneMinusSrcColor = 6,
        //Blend factor is (Ad, Ad, Ad, Ad).
        DstAlpha = 7,
        //Blend factor is (1 - Ad, 1 - Ad, 1 - Ad, 1 - Ad).
        OneMinusDstAlpha = 8,
        //Blend factor is (f, f, f, 1); where f = min(As, 1 - Ad).
        SrcAlphaSaturate = 9,
        //Blend factor is (1 - As, 1 - As, 1 - As, 1 - As).
        OneMinusSrcAlpha = 10
    }

    public enum CullMode
    {
        CULL_NONE = 0,
        CULL_FRONT = 1,
        CULL_BACK = 2,
    }

    public enum DepthWrite
    {
        OFF = 0,
        ON = 1
    }

    public enum DepthTest
    {
        OFF = 0,
        Never = 1,
        LESS = 2,
        EQUAL = 3,
        LEQUAL = 4,
        GREATER = 5,
        NOTEQUAL = 6,
        GEQUAL = 7,
        ALWAYS = 8
    }

    public enum LightingMode
    {
        ON = 0,
        OFF = 1,
    }

    MaterialProperty lighting = null;

    MaterialProperty albedoTexture = null;
    MaterialProperty albedoColor = null;
    MaterialProperty albedoIntensity = null;

    MaterialProperty specularTexture = null;
    MaterialProperty specularColor = null;
    MaterialProperty specularShininess = null;

    MaterialProperty normalTexture = null;

    MaterialProperty cullMode = null;
    MaterialProperty renderMode = null;

    MaterialProperty alphaTest = null;
    MaterialProperty alphaCutoff = null;

    MaterialProperty alphaBlend = null;
    MaterialProperty srcBlendMode = null;
    MaterialProperty dstBlendMode = null;
    MaterialProperty depthWrite = null;
    MaterialProperty depthTest = null;

    MaterialEditor m_MaterialEditor;

    MaterialProperty isVertexColor = null;

    //miner SubSurfaceScattering
    MaterialProperty enableSubsurfaceScattering = null;
    MaterialProperty thinknessSource = null;
    //miner SubSurfaceScattering Property
    MaterialProperty transmissionrate = null;
    MaterialProperty IBackDiffuse = null;
    MaterialProperty IBackScale = null;
    MaterialProperty lightInsideAttenuation = null;
    MaterialProperty thinknessTexture = null;

    bool m_FirstTimeApply = true;

    public void FindProperties(MaterialProperty[] props)
    {
        lighting = FindProperty("_Lighting", props);

        albedoTexture = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);
        albedoIntensity = FindProperty("_AlbedoIntensity", props);

        specularTexture = FindProperty("_SpecGlossMap", props);
        specularColor = FindProperty("_SpecColor", props);
        specularShininess = FindProperty("_Shininess", props);

        normalTexture = FindProperty("_BumpMap", props);

        renderMode = FindProperty("_Mode", props);
        cullMode = FindProperty("_Cull", props);

        alphaTest = FindProperty("_AlphaTest", props, false);
        alphaCutoff = FindProperty("_Cutoff", props, false);

        alphaBlend = FindProperty("_AlphaBlend", props, false);
        srcBlendMode = FindProperty("_SrcBlend", props);
        dstBlendMode = FindProperty("_DstBlend", props);

        depthWrite = FindProperty("_ZWrite", props);
        depthTest = FindProperty("_ZTest", props);

        isVertexColor = FindProperty("_IsVertexColor", props);

       //miner SubSurfaceScattering
        enableSubsurfaceScattering = FindProperty("_enableSubsurfaceScattering", props);
        thinknessSource = FindProperty("_ThinknessSource",props);
        transmissionrate = FindProperty("transmissionRate",props);
        IBackDiffuse = FindProperty("backDiffuse",props);
        IBackScale = FindProperty("backScale",props);
        lightInsideAttenuation = FindProperty("transmissionColor",props);
        thinknessTexture = FindProperty("thinknessTexture", props);
    }



    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        // render the default gui
        FindProperties(props);
        m_MaterialEditor = materialEditor;
        Material material = materialEditor.target as Material;

        ShaderPropertiesGUI(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        // Use default labelWidth
        EditorGUIUtility.labelWidth = 0f;

        // Detect any changes to the material
        EditorGUI.BeginChangeCheck();
        {
            //renderMode
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.renderModeText, GUILayout.Width(120));
            var mode = (RenderMode)renderMode.floatValue;
            mode = (RenderMode)EditorGUILayout.Popup((int)mode, Styles.renderModeNames);
            GUILayout.EndHorizontal();

            //lightingMode
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.lightModeText, GUILayout.Width(120));
            var light = (LightingMode)lighting.floatValue;
            light = (LightingMode)EditorGUILayout.Popup((int)light, Styles.lightingNames);
            GUILayout.EndHorizontal();

            //IsVertexColor
            m_MaterialEditor.ShaderProperty(isVertexColor, Styles.enableVertexColor);

            //Primary properties
            GUILayout.Label(Styles.PrimaryText, EditorStyles.boldLabel);

            //albedo
            m_MaterialEditor.TexturePropertySingleLine(Styles.albedoText, albedoTexture, albedoColor);
            //albedo Intensity 
            m_MaterialEditor.ShaderProperty(albedoIntensity, Styles.albedoIntensityText, MaterialEditor.kMiniTextureFieldLabelIndentLevel);

            if (lighting.floatValue == 0)
            {
                //specular
                m_MaterialEditor.TexturePropertySingleLine(Styles.specularText, specularTexture, specularColor);
                //specular Shininess
                m_MaterialEditor.ShaderProperty(specularShininess, Styles.specularShininessText, MaterialEditor.kMiniTextureFieldLabelIndentLevel);

                //mormal
                m_MaterialEditor.TexturePropertySingleLine(Styles.normalMapText, normalTexture);
            }

            //scaleAndOffset
            m_MaterialEditor.TextureScaleOffsetProperty(albedoTexture);

            m_MaterialEditor.ShaderProperty(enableSubsurfaceScattering, Styles.enableSubsurfaceScattering);
            if (enableSubsurfaceScattering.floatValue==1.0){
                m_MaterialEditor.TexturePropertySingleLine(Styles.thinknessTexture, thinknessTexture, lightInsideAttenuation);
                m_MaterialEditor.ShaderProperty(transmissionrate, Styles.transmissionrate, MaterialEditor.kMiniTextureFieldLabelIndentLevel);
                m_MaterialEditor.ShaderProperty(IBackDiffuse,Styles.backdiffuse, MaterialEditor.kMiniTextureFieldLabelIndentLevel);
                m_MaterialEditor.ShaderProperty(IBackScale,Styles.backScale, MaterialEditor.kMiniTextureFieldLabelIndentLevel);
            }
            
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));

            //Advanced properties
            GUILayout.Label(Styles.AdvancedText, EditorStyles.boldLabel);
            //alphaTest
            m_MaterialEditor.ShaderProperty(alphaTest, Styles.alphaTestText);
            if (alphaTest.floatValue == 1)
            {
                m_MaterialEditor.ShaderProperty(alphaCutoff, Styles.alphaCutoffText, MaterialEditor.kMiniTextureFieldLabelIndentLevel + 1);
            }

            //alphaBlend
            m_MaterialEditor.ShaderProperty(alphaBlend, Styles.alphaBlendText);
            var dstMode = (DstBlendMode)dstBlendMode.floatValue;
            var srcMode = (SrcBlendMode)srcBlendMode.floatValue;
            if (alphaBlend.floatValue == 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(20));
                srcMode = (SrcBlendMode)EditorGUILayout.Popup((int)srcMode, Styles.srcBlendNames);
                dstMode = (DstBlendMode)EditorGUILayout.Popup((int)dstMode, Styles.dstBlendNames);
                GUILayout.EndHorizontal();
            }

            //depthWrite
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.depthWriteText, GUILayout.Width(120));
            var depthW = (DepthWrite)depthWrite.floatValue;
            depthW = (DepthWrite)EditorGUILayout.Popup((int)depthW, Styles.depthWriteNames);
            GUILayout.EndHorizontal();

            //depthTest
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.depthTestText, GUILayout.Width(120));
            var depthT = (DepthTest)depthTest.floatValue;
            depthT = (DepthTest)EditorGUILayout.Popup((int)depthT, Styles.depthTestNames);
            GUILayout.EndHorizontal();

            //cullMode
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.cullModeText, GUILayout.Width(120));
            var cull = (CullMode)cullMode.floatValue;
            cull = (CullMode)EditorGUILayout.Popup((int)cull, Styles.cullModeNames);
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                m_MaterialEditor.RegisterPropertyChangeUndo("Rendering Mode");

                //renderMode
                renderMode.floatValue = (float)mode;
                
                //lightMode
                lighting.floatValue = (float)light;
                material.SetInt("_Lighting", (int)light);

                if (lighting.floatValue == 0)
                {
                    material.EnableKeyword("EnableLighting");
                }
                else
                {
                    material.DisableKeyword("EnableLighting");
                }

                //cullMode
                cullMode.floatValue = (float)cull;
                material.SetInt("_Cull", (int)cull);

                if ((RenderMode)material.GetFloat("_Mode") == RenderMode.Custom)
                {
                    //alphaTest
                    if (alphaTest.floatValue == 1)
                    {
                        material.EnableKeyword("EnableAlphaCutoff");
                        material.EnableKeyword("_ALPHATEST_ON");
                    }
                    else
                    {
                        material.DisableKeyword("EnableAlphaCutoff");
                        material.DisableKeyword("_ALPHATEST_ON");
                    }

                    //alphaBlend
                    if (alphaBlend.floatValue == 1)
                    {
                        srcBlendMode.floatValue = (float)srcMode;
                        dstBlendMode.floatValue = (float)dstMode;
                        material.SetInt("_SrcBlend", (int)srcMode);
                        material.SetInt("_DstBlend", (int)dstMode);
                        material.EnableKeyword("_ALPHABLEND_ON");
                        material.SetInt("_AlphaBlend", 1);
                    }
                    else
                    {
                        material.DisableKeyword("_ALPHABLEND_ON");
                        material.SetInt("_AlphaBlend", 0);
                        material.SetInt("_SrcBlend", (int)1);
                        material.SetInt("_DstBlend", (int)0);
                    }

                    //depthWrite
                    depthWrite.floatValue = (float)depthW;
                    material.SetInt("_ZWrite", (int)depthW);

                    //depthTest
                    depthTest.floatValue = (float)depthT;
                    material.SetInt("_ZTest", (int)depthT);

                }

                if (specularTexture.textureValue != null)
                {
                    material.EnableKeyword("SpecularTexture");
                }
                else
                {
                    material.DisableKeyword("SpecularTexture");
                }

                if (normalTexture.textureValue != null)
                {
                    material.EnableKeyword("NormalTexture");
                }
                else
                {
                    material.DisableKeyword("NormalTexture");
                }

                if (isVertexColor.floatValue == 1)
                {
                    material.EnableKeyword("ENABLEVERTEXCOLOR");
                }
                else
                {
                    material.DisableKeyword("ENABLEVERTEXCOLOR");
                }

                if (enableSubsurfaceScattering.floatValue == 1.0)
                {
                    material.EnableKeyword("ENABLETRANSMISSION");
                }
                else
                {
                    material.DisableKeyword("ENABLETRANSMISSION");
                }
                if(thinknessTexture.textureValue!=null){
                    material.EnableKeyword("THICKNESSMAP");
                }
                else{
                    material.DisableKeyword("THICKNESSMAP");
                }

                onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
            }
        }
        m_MaterialEditor.RenderQueueField();
    }

    public void onChangeRender(Material material, RenderMode mode)
    {
        
        switch (mode)
        {
            case RenderMode.Opaque:
                material.SetInt("_Mode", 0);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 0);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;  
                break;
            case RenderMode.Cutout:
                material.SetInt("_Mode", 1);
                material.SetInt("_AlphaTest", 1);
                material.SetInt("_AlphaBlend", 0);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.SetInt("_ZTest", 4);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
            case RenderMode.Transparent:
                material.SetInt("_Mode", 2);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 1);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
            case RenderMode.Custom:
                material.SetInt("_Mode", 3);
                break;
            default:
                material.SetInt("_Mode", 0);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 0);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                break;
        }
        if (lighting != null)
        {
            if (lighting.floatValue == 0)
            {
                material.EnableKeyword("EnableLighting");
            }
            else
            {
                material.DisableKeyword("EnableLighting");
            }
        } 
    }

    public static class Styles
    {
        public static GUIStyle optionsButton = "PaneOptions";
        public static GUIContent uvSetLabel = new GUIContent("UV Set");
        public static GUIContent[] uvSetOptions = new GUIContent[] { new GUIContent("UV channel 0"), new GUIContent("UV channel 1") };

        public static string emptyTootip = "";
        public static GUIContent albedoText = new GUIContent("Albedo", "Albedo (RGB) and Transparency (A)");
        public static GUIContent albedoIntensityText = new GUIContent("Intensity", "Albedo Intensity");
        public static GUIContent specularText = new GUIContent("Specular", "Specular (RGB) and Transparency (A)");
        public static GUIContent specularShininessText = new GUIContent("Shininess", "Specular Range");
        public static GUIContent normalMapText = new GUIContent("Normal Map", "Normal Map");

        public static GUIContent lightModeText = new GUIContent("Lighting", "Lighting");
        public static GUIContent cullModeText = new GUIContent("Cull", "CullMode");
        public static GUIContent renderModeText = new GUIContent("RenderMode", "RenderMode");
        public static GUIContent alphaTestText = new GUIContent("AlphaTest", "AlphaTest");
        public static GUIContent alphaCutoffText = new GUIContent("Alpha Cutoff", "Threshold for alpha cutoff");
        public static GUIContent alphaBlendText = new GUIContent("AlphaBlend", "AlphaBlend");
        public static GUIContent depthWriteText = new GUIContent("DepthWrite", "DepthWrite");
        public static GUIContent depthTestText = new GUIContent("DepthTest", "DepthTest");

        public static string whiteSpaceString = " ";
        public static string PrimaryText = "Primary Properties";
        public static string AdvancedText = "Advanced Properties";
        public static GUIContent enableVertexColor = new GUIContent("Enable VertexColor", "Enable VertexColor");

        public static GUIContent enableSubsurfaceScattering = new GUIContent("Enable Transmission", "Enable Transmission");
        public static GUIContent transmissionrate = new GUIContent("Transmission Rate", "Transmission Rate");
        public static GUIContent backdiffuse = new GUIContent("Back Diffuse","Back Diffuse");
        public static GUIContent backScale = new GUIContent("Back Scale","Back Scale");
        public static GUIContent lightInsideAttenuation = new GUIContent("Transmission Color", "Transmission Color");
        public static GUIContent thinknessTexture = new GUIContent("Thinkness Texture", "Thinkness Texture");

        public static readonly string[] srcBlendNames = Enum.GetNames(typeof(SrcBlendMode));
        public static readonly string[] dstBlendNames = Enum.GetNames(typeof(DstBlendMode));
        public static readonly string[] renderModeNames = Enum.GetNames(typeof(RenderMode));
        public static readonly string[] cullModeNames = Enum.GetNames(typeof(CullMode));
        public static readonly string[] depthWriteNames = Enum.GetNames(typeof(DepthWrite));
        public static readonly string[] depthTestNames = Enum.GetNames(typeof(DepthTest));
        public static readonly string[] lightingNames = Enum.GetNames(typeof(LightingMode));
    }
}
//#endif  
