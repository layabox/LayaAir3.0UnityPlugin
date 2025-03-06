using System;
using UnityEngine;
using UnityEditor;

class LayaShaderGUI : ShaderGUI
{
    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader) {
        material.shader = newShader;
    }

    public enum RenderMode {
        Opaque = 0,
        Cutout = 1,
        Transparent = 2,
    }

    public enum CullMode {
        CULL_NONE = 0,
        CULL_FRONT = 1,
        CULL_BACK = 2,
    }


    protected MaterialProperty albedoTexture = null;
    protected MaterialProperty albedoColor = null;
    protected MaterialProperty isVertexColor = null;
    protected MaterialProperty renderMode = null;
    protected MaterialProperty cullMode = null;
    protected MaterialProperty alphaCutoff = null;
    protected MaterialProperty albedoIntensity = null;

    protected MaterialEditor m_MaterialEditor;

    static readonly string[] renderModeNames = Enum.GetNames(typeof(RenderMode));
    static readonly string[] cullModeNames = Enum.GetNames(typeof(CullMode));

    protected virtual void FindProperties(MaterialProperty[] props) {
        isVertexColor = FindProperty("_IsVertexColor", props);
        albedoTexture = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);
        albedoIntensity = FindProperty("_AlbedoIntensity", props);
        alphaCutoff = FindProperty("_Cutoff", props, false);
        renderMode = FindProperty("_Mode", props);
        cullMode = FindProperty("_Cull", props);
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props) {
        FindProperties(props);
        m_MaterialEditor = materialEditor;
        Material material = materialEditor.target as Material;
        ShaderPropertiesGUI(material);
    }

    public void ShaderPropertiesGUI(Material material) {
        EditorGUI.BeginChangeCheck();
        MaterialPropertiesGUI(material);
        if (renderMode != null) RenderModeGUI(material);
        if (EditorGUI.EndChangeCheck()) {
            OnMaterialChanged(material);
        }
    }

    protected virtual void MaterialPropertiesGUI(Material material) {
        if (albedoColor != null) m_MaterialEditor.ColorProperty(albedoColor, "Albedo Color");
        if (albedoTexture != null) m_MaterialEditor.TextureProperty(albedoTexture, "Albedo Texture");
        if (albedoIntensity != null) m_MaterialEditor.ShaderProperty(albedoIntensity, "Albedo Intensity");
        if (isVertexColor != null) m_MaterialEditor.ShaderProperty(isVertexColor, "Vertex Color");
        if (alphaCutoff != null) m_MaterialEditor.ShaderProperty(alphaCutoff, "Alpha Test Value");
    }

    protected void CheckKeyword(Material material, string keyword, bool state) {
        if (state) {
            material.EnableKeyword(keyword);
        } else {
            material.DisableKeyword(keyword);
        }
    }

    protected virtual void OnMaterialChanged(Material material) {
        CheckKeyword(material, "ENABLEVERTEXCOLOR", isVertexColor != null);
    }

    protected void RenderModeGUI(Material material) {
        GUILayout.Label("Render State");
        GUILayout.BeginHorizontal();
        {
            GUILayout.Space(20);
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Render Mode", GUILayout.Width(120));
                    GUILayout.FlexibleSpace();
                    renderMode.floatValue = EditorGUILayout.Popup((int)renderMode.floatValue, renderModeNames);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Cull", GUILayout.Width(120));
                    GUILayout.FlexibleSpace();
                    cullMode.floatValue = EditorGUILayout.Popup((int)cullMode.floatValue, cullModeNames);
                }
                GUILayout.EndHorizontal();
                m_MaterialEditor.RenderQueueField();
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
    }
}
