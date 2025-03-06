#include "Lighting.cginc"
#include "AutoLight.cginc"
#include "UnityCG.cginc" 

float4 _Color;
sampler2D _MainTex;
half _AlbedoIntensity;
float4 _MainTex_ST;
sampler2D _BumpMap;
sampler2D _SpecGlossMap;
half _Shininess;
half _Cutoff;

#if ENABLETRANSMISSION
	float transmissionRate;
	float backDiffuse;
	float backScale;
	float4 transmissionColor;
	#if THICKNESSMAP
		sampler2D thinknessTexture;
	#endif
#endif

struct a2v {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float4 texcoord : TEXCOORD0;
	float4 texcoord1: TEXCOORD1;

	float4 color : COLOR;
};
			
struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float2 uv2: TEXCOORD1;  
	float3 worldPos : TEXCOORD2;
	float3 lightDir: TEXCOORD3;
	float3 viewDir : TEXCOORD4;
	float4 color : COLOR;
	SHADOW_COORDS(5)
	UNITY_FOG_COORDS(6)
};

#if ENABLETRANSMISSION
	inline float3 SubSurfaceIBack(float3 lightDir,float3 viewDir,float2 uv){
		float3 H = normalize(lightDir);
		float VdotH = pow(saturate(dot(viewDir,-H)),backDiffuse)*backScale;
		float3 I;
		#if THICKNESSMAP
			I = transmissionColor.rgb*VdotH*(tex2D(thinknessTexture,uv).r);
		#else
			I = transmissionColor.rgb*VdotH;
		#endif
		return I;
	}
#endif




inline void layaBlinnPhongLighting (in fixed3 lightDir, in fixed3 viewDir, in fixed3 normal, out fixed4 diffuse, out fixed4 specular)
{
	half3 h = normalize (lightDir + viewDir);

	fixed diff = max (0, dot (normal, lightDir));

	float nh = max (0, dot (normal, h));

	diffuse = _LightColor0 * max(0, dot(normal, lightDir));

	specular =  _LightColor0 * pow (nh, _Shininess * 128.0);
}
			
v2f vert(a2v v) {

	v2f o;

	o.pos = UnityObjectToClipPos(v.vertex);

	o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		
	o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);

	o.uv2 = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;  

	o.color = v.color;

	fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);  
	fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);  
	fixed3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w; 
	float3x3 worldToTangent = float3x3(worldTangent, worldBinormal, worldNormal);
	o.lightDir = mul(worldToTangent, WorldSpaceLightDir(v.vertex));
	o.viewDir = mul(worldToTangent, WorldSpaceViewDir(v.vertex));

	TRANSFER_SHADOW(o);
	UNITY_TRANSFER_FOG(o, o.pos);

	return o;
}
			
fixed4 frag(v2f i) : SV_Target {

	fixed4 mainTexColor = tex2D(_MainTex, i.uv);
	fixed4 albedo = mainTexColor * _Color * _AlbedoIntensity;
	
	#if EnableAlphaCutoff
		clip (albedo.a - _Cutoff);
	#endif

	fixed4 lightMapColor = fixed4(0.0,0.0,0.0,0.0);
	#if defined(LIGHTMAP_ON)
		lightMapColor = fixed4(DecodeLightmap (UNITY_SAMPLE_TEX2D(unity_Lightmap, i.uv2)), 1.0); 
	#endif

	fixed4 diffuse;
	fixed4 specular;

	#if defined(EnableLighting)
		fixed3 lightDir = normalize(i.lightDir);
		fixed3 viewDir = normalize(i.viewDir);
		fixed3 normal = UnpackNormal(tex2D(_BumpMap, i.uv));
		layaBlinnPhongLighting(lightDir, viewDir, normal, diffuse, specular);
	#else
		diffuse = fixed4(0.0,0.0,0.0,0.0);
		specular = fixed4(0.0,0.0,0.0,0.0);
	#endif
				
	diffuse = diffuse * albedo;

	#if defined(SpecularTexture)
		specular = specular * tex2D(_SpecGlossMap, i.uv) * _SpecColor;
	#else
		specular = specular * mainTexColor.a * _SpecColor;
	#endif

	UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
	
	fixed4 color;
#if defined(ENABLETRANSMISSION)&&defined(EnableLighting)
	diffuse.rgb *= transmissionRate;
#endif
	#if ENABLEVERTEXCOLOR
	color = fixed4((diffuse + specular).rgb * atten, albedo.a)*i.color;
	#else
	color = fixed4((diffuse + specular).rgb * atten, albedo.a);
	#endif
	#if defined(ENABLETRANSMISSION)&&defined(EnableLighting)
		color.rgb += SubSurfaceIBack(lightDir, viewDir, i.uv)*_LightColor0.rgb*(1.0-transmissionRate)*atten;
	#endif

	UNITY_APPLY_FOG(i.fogCoord, color);
	//color = clamp(color, 0, 1);
	return color;
}
