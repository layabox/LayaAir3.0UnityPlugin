Shader "Laya/BlinnPhong" {
	Properties {
		_MainTex("Albedo Texture", 2D) = "white" {}
		_Color("Albedo Color", Color) = (1,1,1,1)
		_AlbedoIntensity("Albedo Intensity", Range(1.0, 4.0)) = 1.0

		_SpecGlossMap("Specular Texture", 2D) = "white" {}
		_SpecColor("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
		_Shininess("Specular Shininess", Range(0.01, 1)) = 0.078125
		[HideInInspector] _SpecularSource ("__source", Float) = 0.0
		_BumpMap("Normal Texture", 2D) = "bump" {}

		[ToggleOff] _AlphaTest("AlphaTest", Float) = 0.0
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.01
		[ToggleOff] _AlphaBlend("AlphaBlend", Float) = 0.0
		[ToggleOff] _IsVertexColor("vertexcolor",Float) = 0.0

		
		//SubSurface Scattering
		transmissionRate("transmissionRate",Range(0.0,1.0)) = 0.0
		backDiffuse("backDiffuse",Range(1.0,10.0)) = 1.0
		backScale("backScale",Range(0.0,10.0)) = 1.0
		transmissionColor("transmissionColor",Color) = (1.0,1.0,1.0,1.0)
		thinknessTexture("thinknessTexture",2D) = "white" {}
		//Subsurface 
		[ToggleOff] _enableSubsurfaceScattering("Subsurface Scattering",Float) = 0.0
		[HideInInspector] _ThinknessSource ("__thinknessSource", Float) = 0.0


		[HideInInspector] _Lighting ("__Lighting", Float) = 0.0
		[HideInInspector] _Cull ("__cull", Float) = 2.0
		[HideInInspector] _Mode ("__mode", Float) = 0.0
		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
		[HideInInspector] _ZTest("__zt", Float) = 4.0
		[HideInInspector] _RenderQueue("__rq", Float) = 2000.0
	}
	SubShader {		
		Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
		Pass { 
			Tags { "LightMode"="ForwardBase" }
		
			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]
			ZTest [_ZTest]
			Cull [_Cull]

			CGPROGRAM

			#pragma shader_feature SpecularTexture
			#pragma shader_feature EnableAlphaCutoff
			#pragma shader_feature EnableLighting
			#pragma shader_feature THICKNESSMAP

			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
			#pragma shader_feature ENABLEVERTEXCOLOR
			#pragma shader_feature ENABLETRANSMISSION
			

			#pragma vertex vert
			#pragma fragment frag

			#include "../CGIncludes/LayaBlinnPhongFB.cginc"

			
			ENDCG
		}
		Pass { 
			Tags { "LightMode"="ForwardAdd" }
			
			Blend One One

			CGPROGRAM

			#pragma shader_feature SpecularTexture
			#pragma shader_feature EnableAlphaCutoff
			#pragma shader_feature EnableLighting
			#pragma shader_feature THICKNESSMAP
			
			#pragma multi_compile_fwdadd
			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog
			#pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
			#pragma shader_feature ENABLEVERTEXCOLOR
			#pragma shader_feature ENABLETRANSMISSION

			#pragma vertex vert
			#pragma fragment frag

			#include "../CGIncludes/LayaBlinnPhongFA.cginc"
			
			ENDCG
		}
	} 
	CustomEditor "LayaShaderGUI"
	FallBack "Standard"
}
