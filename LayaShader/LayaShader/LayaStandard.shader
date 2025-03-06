Shader "Laya/PBR"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.01
		albedoTexture("Albedo", 2D) = "white" {}
		smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
		smoothnessTextureScale("Smoothness Scale", Range(0.0, 1.0)) = 1.0
		[Gamma] metallic("Metallic", Range(0.0, 1.0)) = 0.0
		metallicGlossTexture("Metallic", 2D) = "white" {}
		normalTextureScale("Scale", Float) = 1.0
		normalTexture("Normal Map", 2D) = "bump" {}
		parallaxTextureScale("Height Scale", Range(0.005, 0.08)) = 0.02
		parallaxTexture("Height Map", 2D) = "black" {}
		occlusionTextureStrength("Strength", Range(0.0, 1.0)) = 1.0
		occlusionTexture("Occlusion", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		tilingOffset("TillingOffset",Vector) =(1.0,1.0,0.0,0.0)
		[HideInInspector] _Mode("__mode", Float) = 0.0
		[HideInInspector] _SrcBlend("__src", Float) = 1.0
		[HideInInspector] _DstBlend("__dst", Float) = 0.0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
	}
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        Pass
        {
			Name "FORWARD"
			Tags{"LightMode"="ForwardBase"}

			Blend [_SrcBlend][_DstBlend]
			ZWrite[_ZWrite]

            CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature NORMALTEXTURE
			#pragma shader_feature _ ALPHATEST TRANSPARENTBLEND
			#pragma shader_feature _EMISSION
			#pragma shader_feature METALLICGLOSSTEXTURE
			#pragma shader_feature _ SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
			#pragma shader_feature PARALLAXTEXTURE

			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

            #pragma vertex vertForwardBase
            #pragma fragment fragForwardBaseInternal

            #pragma multi_compile_fog
			#define LayaStandardPBR  1
			
			#include "../CGIncludes/LayaStandardcore.cginc"
   
            ENDCG
        }
		Pass
		{
			Name "FORWARD_DELTA"
			Tags { "LightMode" = "ForwardAdd" }
			Blend [_SrcBlend] One
			Fog { Color(0,0,0,0)}
			ZWrite Off
			ZTest LEqual

			CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature NORMALTEXTURE
			#pragma shader_feature _ ALPHATEST TRANSPARENTBLEND
			#pragma shader_feature METALLICGLOSSTEXTURE
			#pragma shader_feature _ SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
			#pragma shader_feature _ _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature ___ _DETAIL_MULX2
			#pragma shader_feature PARALLAXTEXTURE

			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog
			
			#pragma vertex vertForwardAdd
			#pragma fragment fragForwardAddInternal
			#define LayaStandardPBR  1

			#include "UnityPBSLighting.cginc"
			#include "AutoLight.cginc"
			#include "../CGIncludes/LayaStandardcore.cginc"

			ENDCG
			
		}

			// ------------------------------------------------------------------
		 // Extracts information for lightmapping, GI (emission, albedo, ...)
		 // This pass it not used during regular rendering.
		 Pass
		 {
			 Name "META"
			 Tags { "LightMode" = "Meta" }

			 Cull Off

			 CGPROGRAM
			 #pragma vertex vert_meta
			 #pragma fragment frag_meta

			 #pragma shader_feature _EMISSION
			 #pragma shader_feature_local _METALLICGLOSSMAP
			 #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			 #pragma shader_feature_local _DETAIL_MULX2
			 #pragma shader_feature EDITOR_VISUALIZATION

			 #include "UnityStandardMeta.cginc"
			 ENDCG
		 }
    }
	CustomEditor "PBRStandardShaderGUI"
	FallBack "Standard"
}
