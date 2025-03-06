using UnityEngine;
using UnityEditor;

class LayaBlinnPhongShaderGUI : LayaShaderGUI {
    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader) {
        material.shader = newShader;
        material.EnableKeyword("EnableLighting");
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
    }

    public enum LightingMode {
        ON = 0,
        OFF = 1,
    }

    MaterialProperty lighting = null;
    MaterialProperty specularTexture = null;
    MaterialProperty specularColor = null;
    MaterialProperty specularShininess = null;
    MaterialProperty normalTexture = null;

    MaterialProperty enableSubsurfaceScattering = null;
    MaterialProperty thinknessTexture = null;


    protected override void FindProperties(MaterialProperty[] props) {
        base.FindProperties(props);
        lighting = FindProperty("_Lighting", props);
        specularTexture = FindProperty("_SpecGlossMap", props);
        specularColor = FindProperty("_SpecColor", props);
        specularShininess = FindProperty("_Shininess", props);
        normalTexture = FindProperty("_BumpMap", props);
        alphaCutoff = FindProperty("_Cutoff", props, false);
        enableSubsurfaceScattering = FindProperty("_enableSubsurfaceScattering", props);
        thinknessTexture = FindProperty("thinknessTexture", props);
    }

    protected override void MaterialPropertiesGUI(Material material) {
        base.MaterialPropertiesGUI(material);
        m_MaterialEditor.TextureProperty(specularTexture, specularTexture.displayName, false);
        m_MaterialEditor.ShaderProperty(specularColor, specularColor.displayName);
        m_MaterialEditor.ShaderProperty(specularShininess, specularShininess.displayName, MaterialEditor.kMiniTextureFieldLabelIndentLevel);
        m_MaterialEditor.TextureProperty(normalTexture, normalTexture.displayName, false);
    }

    protected override void OnMaterialChanged(Material material) {
        base.OnMaterialChanged(material);
        m_MaterialEditor.RegisterPropertyChangeUndo("Rendering Mode");
        LightingMode light = (LightingMode)lighting.floatValue;

        RenderMode mode = (RenderMode)renderMode.floatValue;

        CheckKeyword(material, "EnableLighting", light == LightingMode.ON);
        CheckKeyword(material, "NormalTexture", normalTexture.textureValue != null);
        CheckKeyword(material, "SpecularTexture", specularTexture.textureValue != null);
        CheckKeyword(material, "ENABLETRANSMISSION", enableSubsurfaceScattering.floatValue == 1.0);
        CheckKeyword(material, "THICKNESSMAP", thinknessTexture.textureValue != null);
        CheckKeyword(material, "EnableAlphaCutoff", mode == RenderMode.Cutout);
        CheckKeyword(material, "_ALPHATEST_ON", mode == RenderMode.Cutout);

        onChangeRender(material, mode);
    }

    public void onChangeRender(Material material, RenderMode mode) {
        switch (mode) {
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
        if (lighting != null) {
            var light = (LightingMode)lighting.floatValue;
            CheckKeyword(material, "EnableLighting", light == LightingMode.ON);
        }
    }
}
