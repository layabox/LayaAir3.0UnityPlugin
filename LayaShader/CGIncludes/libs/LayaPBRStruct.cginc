struct VertexInput
{
	float4 vertex : POSITION;
	half3 normal    : NORMAL;
	float2 uv0      : TEXCOORD0;
	float2 uv1      : TEXCOORD1;
#ifdef _TANGENT_TO_WORLD
	half4 tangent   : TANGENT;
#endif
};
struct VertexOutputForwardBase
{

	float4 pos :	SV_POSITION;
	float4 tex :	TEXCOORD0;
	float4 eyeVec:	TEXCOORD1;	
	float4 tangentToWorldAndPackedData[3]:TEXCOORD2;
	half4 ambientOrLightmapUV :TEXCOORD5;
	UNITY_LIGHTING_COORDS(6, 7)
	float3 posWorld                     : TEXCOORD8;
};
struct LayaFragmentCommonData
{
	half3 diffColor, specColor;
	half oneMinusReflectivity, smoothness;
	float3 normalWorld;
	float3 eyeVec;
	half alpha;
	float3 posWorld;
#if UNITY_STANDARD_SIMPLE
	half3 reflUVW;
#endif

#if UNITY_STANDARD_SIMPLE
	half3 tangentSpaceNormal;
#endif
};

struct LayaLight
{
	half3 color;
	half3 dir;
	half  ndotl;
};
struct LayaIndirect
{
	half3 diffuse;
	half3 specular;
};
struct LayaGI
{
	LayaLight light;
	LayaIndirect indirect;
};


struct LayaGIInput
{
	LayaLight light; 
	float3 worldPos;
	half3 worldViewDir;
	half atten;
	half3 ambient;
	float4 lightmapUV; 
#if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION) || defined(UNITY_ENABLE_REFLECTION_BUFFERS)
	float4 boxMin[2];
#endif
#ifdef UNITY_SPECCUBE_BOX_PROJECTION
	float4 boxMax[2];
	float4 probePosition[2];
#endif
	float4 probeHDR[2];
};

struct Laya_GlossyEnvironmentData
{
	half    roughness; 
	half3   reflUVW;
};

struct VertexOutputForwardAdd
{

	float4 pos :	SV_POSITION;
	float4 tex :	TEXCOORD0;
	float4 eyeVec:	TEXCOORD1;
	float4 tangentToWorldAndLightDir[3]:TEXCOORD2;
	float3 posWorld :TEXCOORD5;

	float3 _LightCoord : TEXCOORD6;
	float4 _ShadowCoord : TEXCOORD7;
#if defined(PARALLAXTEXTURE)
	half3 viewDirForParallax            : TEXCOORD8;
#endif
	UNITY_FOG_COORDS(9)
};



struct VertexOutputBaseSimple
{
	float4 pos :	SV_POSITION;
	float4 tex                          : TEXCOORD0;
	half4 eyeVec                        : TEXCOORD1;
	half4 ambientOrLightmapUV           : TEXCOORD2;
	SHADOW_COORDS(3)
	half4 fogCoord						:TEXCOORD4; 
	half4 normalWorld                   : TEXCOORD5;
	#ifdef NORMALTEXTURE
		half3 tangentSpaceLightDir          : TEXCOORD6;
		half3 tangentSpaceEyeVec			: TEXCOORD7;
	#endif
	float3 posWorld                     : TEXCOORD8;
};


struct VertexOutputForwardAddSimple
{
	float4 pos :	SV_POSITION;
	float4 tex                          : TEXCOORD0;
	float3 posWorld                     : TEXCOORD1;

	#if !defined(NORMALTEXTURE)
		UNITY_FOG_COORDS_PACKED(2, half4) 
	#else
		UNITY_FOG_COORDS_PACKED(2, half1)
	#endif
	half3 lightDir                      : TEXCOORD3;
	#if defined(NORMALTEXTURE)
			half3 tangentSpaceEyeVec    : TEXCOORD4;
	#else
			half3 normalWorld           : TEXCOORD4;
	#endif
	UNITY_LIGHTING_COORDS(5, 6)
};
