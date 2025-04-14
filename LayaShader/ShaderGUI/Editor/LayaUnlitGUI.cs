//#if UNITY_EDITOR 
using System;
using UnityEngine;
using UnityEditor;

class LayaUnlitGUI : LayaShaderGUI {
    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader) {
        material.shader = newShader;
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
    }

    protected override void FindProperties(MaterialProperty[] props) {
        base.FindProperties(props);
        albedoIntensity = null;
    }

    protected override void OnMaterialChanged(Material material) {
        base.OnMaterialChanged(material);
        onChangeRender(material, (RenderMode)material.GetFloat("_Mode"));
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
}
//#endif
