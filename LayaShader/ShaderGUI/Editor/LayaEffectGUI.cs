#if UNITY_EDITOR 
using System;
using UnityEngine;
using UnityEditor;

class LayaEffectGUI : ShaderGUI
{
    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        material.shader = newShader;
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
    }
    public enum RenderMode
    {
        Additive = 0,
        AlphaBlended = 1
    }

    MaterialProperty renderMode = null;
    MaterialProperty diffuseTexture = null;
    MaterialProperty diffuseColor = null;

    MaterialEditor m_MaterialEditor;


    public void FindProperties(MaterialProperty[] props)
    {
        diffuseTexture = FindProperty("_MainTex", props);
        diffuseColor = FindProperty("_TintColor", props);

        renderMode = FindProperty("_Mode", props);
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

    public void ShaderPropertiesGUI(Material material,bool enablemode = false)
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

            //Diffuse
            m_MaterialEditor.ShaderProperty(diffuseColor, Styles.MainColorText, MaterialEditor.kMiniTextureFieldLabelIndentLevel);

            //Diffuse
            m_MaterialEditor.TexturePropertySingleLine(Styles.DiffuseTextureText, diffuseTexture);

            //scaleAndOffset
            m_MaterialEditor.TextureScaleOffsetProperty(diffuseTexture);

            if (EditorGUI.EndChangeCheck()||enablemode)
            {
                m_MaterialEditor.RegisterPropertyChangeUndo("Rendering Mode");

                //renderMode
                renderMode.floatValue = (float)mode;
                onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
            }
        }
        m_MaterialEditor.RenderQueueField();
    }

    public void onChangeRender(Material material, RenderMode mode)
    {
        switch (mode)
        {
            case RenderMode.Additive:
                material.SetInt("_Mode", 0);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 1);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.EnableKeyword("ADDTIVEFOG");
                break;
            case RenderMode.AlphaBlended:
                material.SetInt("_Mode", 1);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 1);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.DisableKeyword("ADDTIVEFOG");
                break;
            default:
                material.SetInt("_Mode", 1);
                material.SetInt("_AlphaTest", 0);
                material.SetInt("_AlphaBlend", 1);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", 4);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.EnableKeyword("ADDTIVEFOG");
                break;
        }
    }

    public static class Styles
    {
        public static GUIStyle optionsButton = "PaneOptions";
        public static GUIContent uvSetLabel = new GUIContent("UV Set");
        public static GUIContent[] uvSetOptions = new GUIContent[] { new GUIContent("UV channel 0"), new GUIContent("UV channel 1") };

        public static string emptyTootip = "";
        public static GUIContent MainColorText = new GUIContent("Color", "Color");
        public static GUIContent DiffuseTextureText = new GUIContent("Texture", "Texture");

        public static readonly string[] renderModeNames = Enum.GetNames(typeof(RenderMode));

        public static GUIContent renderModeText = new GUIContent("RenderMode", "RenderMode");
    }
}
#endif