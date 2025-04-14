

#ifndef LAYA_GC_INCLUDE
#define LAYA_GC_INCLUDE

#if (NORMALTEXTURE || DIRLIGHTMAP_COMBINED || PARALLAXTEXTURE)
#define _TANGENT_TO_WORLD 1
#endif

#if (_DETAIL_MULX2 || _DETAIL_MUL || _DETAIL_ADD || _DETAIL_LERP)
#define _DETAIL 1
#endif


half4       _Color;
half        _Cutoff;

sampler2D   albedoTexture;
float4      tilingOffset;

// sampler2D   _DetailAlbedoMap;
// float4      _DetailAlbedoMap_ST;

sampler2D   normalTexture;
half        normalTextureScale;

// sampler2D   _DetailMask;
// sampler2D   _DetailNormalMap;
// half        _DetailNormalMapScale;

sampler2D   specularTexture;
float4      specularColor;
sampler2D   metallicGlossTexture;
half        metallic;
float       smoothness;
float       smoothnessTextureScale;

sampler2D   occlusionTexture;
half        occlusionTextureStrength;

sampler2D   parallaxTexture;
half        parallaxTextureScale;

half4       _EmissionColor;
sampler2D   _EmissionMap;



#include "AutoLight.cginc"
#include "Lighting.cginc"
#include "UnityCG.cginc" 
#include "libs/LayaPBRStruct.cginc"
#include "libs/LayaPBRUtil.cginc"
#include "libs/LayaBRDF.cginc"
#include "UnityShaderVariables.cginc"
#include "UnityStandardBRDF.cginc"

#if !defined (UNITY_BRDF_PBS) 
#if SHADER_TARGET < 30 || defined(SHADER_TARGET_SURFACE_ANALYSIS) 
#define UNITY_BRDF_PBS BRDF3_Unity_PBS
#elif defined(UNITY_PBS_USE_BRDF3)
#define UNITY_BRDF_PBS BRDF3_Unity_PBS
#elif defined(UNITY_PBS_USE_BRDF2)
#define UNITY_BRDF_PBS BRDF2_Unity_PBS
#elif defined(UNITY_PBS_USE_BRDF1)
#define UNITY_BRDF_PBS BRDF1_Unity_PBS
#else
#error something broke in auto-choosing BRDF
#endif
#endif





VertexOutputForwardBase vertForwardBase(VertexInput v)
{
	VertexOutputForwardBase o;	
	o = (VertexOutputForwardBase)0;
	float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
	o.tangentToWorldAndPackedData[0].w = posWorld.x;
	o.tangentToWorldAndPackedData[1].w = posWorld.y;
	o.tangentToWorldAndPackedData[2].w = posWorld.z;
	o.posWorld = posWorld.xyz;
	o.pos = UnityObjectToClipPos(v.vertex);
	o.eyeVec.xyz = LayaNormalizePerVertexNormal(posWorld.xyz - _WorldSpaceCameraPos);
	o.tex = layaTexCoords(v);
	float3 normalWorld = UnityObjectToWorldNormal(v.normal);

	#ifdef _TANGENT_TO_WORLD
		float4 tangentWorld = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
		float3x3 tangentToWorld = CreateTangentToWorldPerVertex(normalWorld, tangentWorld.xyz, tangentWorld.w);
		o.tangentToWorldAndPackedData[0].xyz = tangentToWorld[0];
		o.tangentToWorldAndPackedData[1].xyz = tangentToWorld[1];
		o.tangentToWorldAndPackedData[2].xyz = tangentToWorld[2];
	#else
		o.tangentToWorldAndPackedData[0].xyz = 0;
		o.tangentToWorldAndPackedData[1].xyz = 0;
		o.tangentToWorldAndPackedData[2].xyz = normalWorld;
	#endif
		UNITY_TRANSFER_LIGHTING(o, v.uv1);
		o.ambientOrLightmapUV = LayaVertexGIForward(v, posWorld, normalWorld);
	#ifdef PARALLAXTEXTURE
		TANGENT_SPACE_ROTATION;
		half3 viewDirForParallax = mul(rotation, ObjSpaceViewDir(v.vertex));
		o.tangentToWorldAndPackedData[0].w = viewDirForParallax.x;
		o.tangentToWorldAndPackedData[1].w = viewDirForParallax.y;
		o.tangentToWorldAndPackedData[2].w = viewDirForParallax.z;
	#endif
		UNITY_TRANSFER_FOG_COMBINED_WITH_EYE_VEC(o, o.pos);

	return o;
}


fixed4 fragForwardBaseInternal(VertexOutputForwardBase i) : SV_Target
{
	LayaFragmentCommonData s = LayaFragmentSetup(i.tex, i.eyeVec.xyz, in_ViewDir4Parallax(i), i.tangentToWorldAndPackedData,i.posWorld);
	LayaLight mainLight;
	mainLight.color = _LightColor0.rgb;
	mainLight.dir = _WorldSpaceLightPos0.xyz;
	UNITY_LIGHT_ATTENUATION(atten, i, s.posWorld);
	half occlusion = LayaOcclusion(i.tex.xy);
	LayaGI gi = LayaFragmentGI(s, occlusion, i.ambientOrLightmapUV, atten, mainLight);
	half4 c;
	c = BRDF1_Laya_PBS(s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness, s.normalWorld, -s.eyeVec, gi.light, gi.indirect);
	c.rgb += LayaEmission(i.tex.xy);
	float4 col;
	#if UNITY_TANGENT_ORTHONORMALIZE
		col = float4(1.0, 0.0, 0.0, 1.0);
	#else
		col = float4(0.0,1.0, 0.0, 1.0);
	#endif
	UNITY_EXTRACT_FOG_FROM_EYE_VEC(i);
	UNITY_APPLY_FOG(_unity_fogCoord, c.rgb);
	return LayaOutputForward(c, s.alpha);
}
#endif


VertexOutputForwardAdd vertForwardAdd(VertexInput v)
{
	VertexOutputForwardAdd o;
	o = (VertexOutputForwardAdd)0;
	float4 posWorld = mul(unity_ObjectToWorld, v.vertex);

	o.pos = UnityObjectToClipPos(v.vertex);
	o.tex = layaTexCoords(v);
	o.posWorld = posWorld.xyz;
	o.eyeVec.xyz = LayaNormalizePerVertexNormal(posWorld.xyz - _WorldSpaceCameraPos);
	float3 normalWorld = UnityObjectToWorldNormal(v.normal);

	#ifdef _TANGENT_TO_WORLD
		float4 tangentWorld = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
		float3x3 tangentToWorld = CreateTangentToWorldPerVertex(normalWorld, tangentWorld.xyz, tangentWorld.w);
		o.tangentToWorldAndLightDir[0].xyz = tangentToWorld[0];
		o.tangentToWorldAndLightDir[1].xyz = tangentToWorld[1];
		o.tangentToWorldAndLightDir[2].xyz = tangentToWorld[2];
	#else
		o.tangentToWorldAndLightDir[0].xyz = 0;
		o.tangentToWorldAndLightDir[1].xyz = 0;
		o.tangentToWorldAndLightDir[2].xyz = normalWorld;
	#endif
		UNITY_TRANSFER_LIGHTING(o, v.uv1);
		float3 lightDir = _WorldSpaceLightPos0.xyz - posWorld.xyz * _WorldSpaceLightPos0.w;
		#ifndef USING_DIRECTIONAL_LIGHT
			lightDir = LayaNormalizePerVertexNormal(lightDir);
		#endif
		o.tangentToWorldAndLightDir[0].w = lightDir.x;
		o.tangentToWorldAndLightDir[1].w = lightDir.y;
		o.tangentToWorldAndLightDir[2].w = lightDir.z;
	#ifdef PARALLAXTEXTURE
		TANGENT_SPACE_ROTATION;
		half3 viewDirForParallax = mul(rotation, ObjSpaceViewDir(v.vertex));
	#endif
	UNITY_TRANSFER_FOG_COMBINED_WITH_EYE_VEC(o, o.pos);
	
	return o;
}


half4 fragForwardAddInternal(VertexOutputForwardAdd i) : SV_Target
{
	LayaFragmentCommonData s = LayaFragmentSetup(i.tex, i.eyeVec.xyz, in_ViewDir4Parallax_Fwdadd(i), i.tangentToWorldAndLightDir,i.posWorld);

	LayaLight light;
	light.color = _LightColor0.rgb;
	light.dir = half3(i.tangentToWorldAndLightDir[0].w, i.tangentToWorldAndLightDir[1].w, i.tangentToWorldAndLightDir[2].w);
	UNITY_LIGHT_ATTENUATION(atten, i, s.posWorld)
	light.color *= atten;
	light.dir = normalize(light.dir);
	LayaIndirect noIndirect;
	noIndirect.diffuse = 0;
	noIndirect.specular = 0;

	half4 c = BRDF1_Laya_PBS(s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness, s.normalWorld, -s.eyeVec, light, noIndirect);
	
	UNITY_EXTRACT_FOG_FROM_EYE_VEC(i);
	UNITY_APPLY_FOG_COLOR(i.fogCoord, c.rgb, half4(0, 0, 0, 0));

	return LayaOutputForward(c, s.alpha);
}

