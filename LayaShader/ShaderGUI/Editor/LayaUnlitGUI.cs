//#if UNITY_EDITOR 
using System;
using UnityEngine;
using UnityEditor;

class LayaUnlitGUI : ShaderGUI
{
    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        material.shader = newShader;
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
    }
    public enum RenderMode
    {
        Opaque = 0,
        Cutout = 1,
        Transparent = 2
    }

    public enum CullMode
    {
        CULL_NONE = 0,
        CULL_FRONT = 1,
        CULL_BACK = 2,
    }

    MaterialProperty renderMode = null;
    MaterialProperty albedoTexture = null;
    MaterialProperty albedoColor = null;
    MaterialProperty albedoIntensity = null;
    MaterialProperty alphaCutoff = null;
    MaterialProperty cullMode = null;
    MaterialProperty isVertexColor = null;

    MaterialEditor m_MaterialEditor;

    
   

    public void FindProperties(MaterialProperty[] props)
    {
        renderMode = FindProperty("_Mode", props);
        albedoTexture = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);
        albedoIntensity = FindProperty("_AlbedoIntensity", props);
        isVertexColor = FindProperty("_IsVertexColor", props);
        alphaCutoff = FindProperty("_Cutoff", props, false);
        cullMode = FindProperty("_Cull", props);
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        // render the default gui
        FindProperties(props);
        m_MaterialEditor = materialEditor;
      
        Material material = materialEditor.target as Material;

        //if (m_FirstTimeApply)
        //{
        //    onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
        //    m_FirstTimeApply = false;
        //}

        ShaderPropertiesGUI(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        // Use default labelWidth
        EditorGUIUtility.labelWidth = 0f;

        // Detect any changes to the material
        EditorGUI.BeginChangeCheck();
        
            //renderMode
            GUILayout.BeginHorizontal();
            GUILayout.Label(Styles.renderModeText, GUILayout.Width(120));
            var mode = (RenderMode)renderMode.floatValue;
            mode = (RenderMode)EditorGUILayout.Popup((int)mode, Styles.renderModeNames);
            GUILayout.EndHorizontal();
            //enableVertexColor
            m_MaterialEditor.ShaderProperty(isVertexColor, Styles.enableVertexColor);
            //albedo
            m_MaterialEditor.TexturePropertySingleLine(Styles.albedoText, albedoTexture, albedoColor);
            //albedo Intensity 
            m_MaterialEditor.ShaderProperty(albedoIntensity, Styles.albedoIntensityText, MaterialEditor.kMiniTextureFieldLabelIndentLevel);

            //alphaTest
            if (renderMode.floatValue == 1)
            {
                m_MaterialEditor.ShaderProperty(alphaCutoff, Styles.alphaCutoffText, MaterialEditor.kMiniTextureFieldLabelIndentLevel);
            }

            //scaleAndOffset
            m_MaterialEditor.TextureScaleOffsetProperty(albedoTexture);

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

            if (isVertexColor.floatValue==1.0)
            {


                material.EnableKeyword("ENABLEVERTEXCOLOR");
                //material.SetInt("_IsVertexColor", 1);

            }
            else
            {
                material.DisableKeyword("ENABLEVERTEXCOLOR");
                //material.SetInt("_IsVertexColor", 0);

            }


           
            onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));

                //cullMode
                cullMode.floatValue = (float)cull;
                material.SetInt("_Cull", (int)cull);
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
                material.DisableKeyword("ADDTIVEFOG");
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
                material.DisableKeyword("ADDTIVEFOG");
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
                material.DisableKeyword("ADDTIVEFOG");
                material.DisableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
                material.DisableKeyword("ADDTIVEFOG");
                material.DisableKeyword("EnableAlphaCutoff");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                break;
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

        public static readonly string[] renderModeNames = Enum.GetNames(typeof(RenderMode));

        public static GUIContent renderModeText = new GUIContent("RenderMode", "RenderMode");
        public static GUIContent alphaCutoffText = new GUIContent("Alpha Cutoff", "Threshold for alpha cutoff");
        public static GUIContent cullModeText = new GUIContent("Cull", "CullMode");
        public static readonly string[] cullModeNames = Enum.GetNames(typeof(CullMode));
        public static GUIContent enableVertexColor = new GUIContent("Enable VertexColor","Enable VertexColor");
    }
}
//#endif
