Shader "Laya/PBR(Specular)"
{
	Properties
	{
		//主颜色
		_Color("Color", Color) = (1,1,1,1)
		
		albedoTexture("Albedo", 2D) = "white" {}
		//alpha裁剪
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

		smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
		smoothnessTextureScale("Smoothness Factor", Range(0.0, 1.0)) = 1.0
		//[Enum(Specular Alpha,0,Albedo Alpha,1)] _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0

		specularColor("Specular", Color) = (0.2,0.2,0.2)
		specularTexture("Specular", 2D) = "white" {}

		//[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0

		//[ToggleOff] _GlossyReflections("Glossy Reflections", Float) = 1.0

		normalTextureScale("Scale", Float) = 1.0

		normalTexture("Normal Map", 2D) = "bump" {}

		parallaxTextureScale("Height Scale", Range(0.005, 0.08)) = 0.02
		parallaxTexture("Height Map", 2D) = "black" {}

		occlusionTextureStrength("Strength", Range(0.0, 1.0)) = 1.0
		occlusionTexture("Occlusion", 2D) = "white" {}

		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		//tillingOffset
		tilingOffset("TillingOffset",Vector) =(1.0,1.0,0.0,0.0)
		//_DetailMask("Detail Mask", 2D) = "white" {}

		//_DetailAlbedoMap("Detail Albedo x2", 2D) = "grey" {}
		//_DetailNormalMapScale("Scale", Float) = 1.0
		//_DetailNormalMap("Normal Map", 2D) = "bump" {}

		//[Enum(UV0,0,UV1,1)] _UVSec("UV Set for secondary textures", Float) = 0


		// Blending state
		[HideInInspector] _Mode("__mode", Float) = 0.0
		[HideInInspector] _SrcBlend("__src", Float) = 1.0
		[HideInInspector] _DstBlend("__dst", Float) = 0.0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
	}
    SubShader
    {
        Pass
        {
			Name "FORWARD"
			Tags {  "LightMode" = "ForwardBase" }


			Blend[_SrcBlend][_DstBlend]
			ZWrite[_ZWrite]

			CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature NORMALTEXTURE
			#pragma shader_feature _ ALPHATEST TRANSPARENTBLEND
			#pragma shader_feature _EMISSION
			#pragma shader_feature SPECULARGLOSSTEXTURE
			//#pragma shader_feature ___ _DETAIL_MULX2
			#pragma shader_feature _ SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
			// #pragma shader_feature _ _SPECULARHIGHLIGHTS_OFF
			// #pragma shader_feature _ _GLOSSYREFLECTIONS_OFF
			#pragma shader_feature PARALLAXTEXTURE

			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			// Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
			//#pragma multi_compile _ LOD_FADE_CROSSFADE

			//#pragma vertex vertBase
			//#pragma fragment fragBase
            #pragma vertex vertForwardBase
            #pragma fragment fragForwardBaseInternal
			
            #include "../CGIncludes/LayaStandardcore.cginc"

            ENDCG

        }
    }
	CustomEditor "StandardSpecularShaderGUI"
	//FallBack "Standard"
}
