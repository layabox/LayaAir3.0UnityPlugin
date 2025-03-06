inline half3 LayaPreMultiplyAlpha(half3 diffColor, half alpha, half oneMinusReflectivity, out half outModifiedAlpha)
{
	#if defined(TRANSPARENTBLEND)
		diffColor *= alpha;
		#if (SHADER_TARGET < 30)
			outModifiedAlpha = alpha;
		#else
			outModifiedAlpha = 1 - oneMinusReflectivity + alpha * oneMinusReflectivity;
		#endif
	#else
		outModifiedAlpha = alpha;
	#endif
	return diffColor;
}

float2 LayaTransform_Tex(float2 tex, float4 name)
{
	return tex.xy * name.xy + name.zw;
}
float4 layaTexCoords(VertexInput v)
{
	float4 texcoord;
	texcoord.xy = LayaTransform_Tex(v.uv0, tilingOffset); 
	texcoord.zw = LayaTransform_Tex(v.uv0,tilingOffset);
	return texcoord;
}


half3 LayaNormalizePerVertexNormal(float3 n) 
{
#if (SHADER_TARGET < 30) || UNITY_STANDARD_SIMPLE
	return normalize(n);
#else
	return n;
#endif
}

float3 LayaNormalizePerPixelNormal(float3 n)
{
#if (SHADER_TARGET < 30) || UNITY_STANDARD_SIMPLE
	return n;
#else
	return normalize((float3)n);
#endif
}
half3 LayaUnpackScaleNormal(half4 packednormal, half bumpScale)
{
		packednormal.x *= packednormal.w;
	
		half3 normal;
		normal.xy = (packednormal.xy * 2 - 1);
			normal.xy *= bumpScale;
		normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
		return normal;

}

half3 LayaNormalInTangentSpace(float4 texcoords)
{
	half3 normalTangent = LayaUnpackScaleNormal(tex2D(normalTexture, texcoords.xy), normalTextureScale);
	return normalTangent;
}
inline float3 LayaPerPixelWorldNormal(float4 i_tex, float4 tangentToWorld[3])
{
#ifdef NORMALTEXTURE
	half3 tangent = tangentToWorld[0].xyz;
	half3 binormal = tangentToWorld[1].xyz;
	half3 normal = tangentToWorld[2].xyz;

	half3 normalTangent = LayaNormalInTangentSpace(i_tex);
	float3 normalWorld =LayaNormalizePerPixelNormal(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z); // @TODO: see if we can squeeze this normalize on SM2.0 as well
	
#else
	float3 normalWorld = normalize(tangentToWorld[2].xyz);
#endif
	return normalWorld;
}

half3 LayaEmission(float2 uv)
{
#ifndef _EMISSION
	return 0;
#else
	return tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
#endif
}


half3 in_ViewDir4Parallax(VertexOutputForwardBase i)
{
	half3 viewdir;
#ifdef PARALLAXTEXTURE
	viewdir = LayaNormalizePerPixelNormal(half3(i.tangentToWorldAndPackedData[0].w, i.tangentToWorldAndPackedData[1].w, i.tangentToWorldAndPackedData[2].w));
#else
	viewdir = half3(0, 0, 0);
#endif
	return viewdir;
}

half3 in_ViewDir4Parallax_Fwdadd(VertexOutputForwardAdd i)
{
	half3 viewdir;
#ifdef PARALLAXTEXTURE
	viewdir = LayaNormalizePerPixelNormal(i.viewDirForParallax.xyz);
#else
	viewdir = half3(0, 0, 0);
#endif
	return viewdir;
}


half2 LayaParallaxOffset1Step(half h, half height, half3 viewDir)
{
	h = h * height - height / 2.0;
	half3 v = normalize(viewDir);
	v.z += 0.42;
	return h * (v.xy / v.z);
}


float4 LayaParallax(float4 texcoords, half3 viewDir)
{
#if !defined(PARALLAXTEXTURE) || (SHADER_TARGET < 30)
	return texcoords;
#else
	half h = tex2D(parallaxTexture, texcoords.xy).g;
	float2 offset = LayaParallaxOffset1Step(h, parallaxTextureScale, viewDir);
	return float4(texcoords.xy + offset, texcoords.zw + offset);
#endif

}

half LayaAlpha(float2 uv)
{
#if defined(SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA)
	return _Color.a;
#else
	return tex2D(albedoTexture, uv).a * _Color.a;
#endif
}

half3 LayaAlbedo(float4 texcoords)
{
	half3 albedo = _Color.rgb * tex2D(albedoTexture, texcoords.xy).rgb;
	return albedo;
}

half4 LayaSpecularGloss(float2 uv)
{
	half4 sg;
#ifdef SPECULARGLOSSTEXTURE
	#if defined(SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA)
		sg.rgb = tex2D(specularTexture, uv).rgb;
		sg.a = tex2D(albedoTexture, uv).a;
	#else
		sg = tex2D(specularTexture, uv);
	#endif
	sg.a *= smoothnessTextureScale;
#else
	sg.rgb = specularColor.rgb;
	#ifdef SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
		sg.a = tex2D(albedoTexture, uv).a * smoothness;
	#else
		sg.a = smoothness;
	#endif
#endif
	return sg;
}


half2 LayaMetallicGloss(float2 uv)
{
	half2 mg;
#ifdef METALLICGLOSSTEXTURE
	#ifdef SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
		mg.r = tex2D(metallicGlossTexture, uv).r;
		mg.g = tex2D(albedoTexture, uv).a;
	#else
		mg = tex2D(metallicGlossTexture, uv).ra;
	#endif
	mg.g *= smoothnessTextureScale;
#else
	mg.r = metallic;
	#ifdef SMOOTHNESSSOURCE_ALBEDOTEXTURE_ALPHA
		mg.g = tex2D(albedoTexture, uv).a * smoothness;
	#else
		mg.g = smoothness;
	#endif
#endif
	return mg;
}


half LayaSpecularStrength(half3 specular)
{
#if (SHADER_TARGET < 30)
	return specular.r; 
#else
	return max(max(specular.r, specular.g), specular.b);
#endif
}

inline half3 LayaEnergyConservationBetweenDiffuseAndSpecular(half3 albedo, half3 specColor, out half oneMinusReflectivity)
{
	oneMinusReflectivity = 1 - LayaSpecularStrength(specColor);
#if !UNITY_CONSERVE_ENERGY
	return albedo;
#elif UNITY_CONSERVE_ENERGY_MONOCHROME
	return albedo * oneMinusReflectivity;
#else
	return albedo * (half3(1, 1, 1) - specColor);
#endif
}


LayaFragmentCommonData LayaSpecularSetup(float4 i_tex)
{
	half4 specGloss = LayaSpecularGloss(i_tex.xy);
	half3 specColor = specGloss.rgb;
	half smoothness = specGloss.a;

	half oneMinusReflectivity;
	half3 diffColor = LayaEnergyConservationBetweenDiffuseAndSpecular(LayaAlbedo(i_tex), specColor, oneMinusReflectivity);

	LayaFragmentCommonData o;
	o.diffColor = diffColor;
	o.specColor = specColor;
	o.oneMinusReflectivity = oneMinusReflectivity;
	o.smoothness = smoothness;
	return o;
}


inline half LayaOneMinusReflectivityFromMetallic(half metallic)
{

	half oneMinusDielectricSpec = unity_ColorSpaceDielectricSpec.a;
	return oneMinusDielectricSpec - metallic * oneMinusDielectricSpec;
}


inline half3 LayaDiffuseAndSpecularFromMetallic(half3 albedo, half metallic, out half3 specColor, out half oneMinusReflectivity)
{

	specColor = lerp(unity_ColorSpaceDielectricSpec.rgb, albedo, metallic);
	oneMinusReflectivity = LayaOneMinusReflectivityFromMetallic(metallic);
	return albedo * oneMinusReflectivity;
}


LayaFragmentCommonData LayaMetallicSetup(float4 i_tex)
{
	half2 metallicGloss = LayaMetallicGloss(i_tex.xy);
	half metallic = metallicGloss.x;
	half smoothness = metallicGloss.y;

	half oneMinusReflectivity;
	half3 specColor;
	half3 diffColor = LayaDiffuseAndSpecularFromMetallic(LayaAlbedo(i_tex), metallic, /*out*/ specColor, /*out*/ oneMinusReflectivity);

	LayaFragmentCommonData o = (LayaFragmentCommonData)0;
	o.diffColor = diffColor;
	o.specColor = specColor;
	o.oneMinusReflectivity = oneMinusReflectivity;
	o.smoothness = smoothness;
	return o;
}
LayaFragmentCommonData LayaFragmentSetup(inout float4 i_tex, float3 i_eyeVec, half3 i_viewDirForParallax, float4 tangentToWorld[3], float3 i_posWorld)
{
	i_tex = LayaParallax(i_tex, i_viewDirForParallax);

	half alpha = LayaAlpha(i_tex.xy);
#if defined(ALPHATEST)
	clip(alpha - _Cutoff);
#endif
	LayaFragmentCommonData o;
	o = (LayaFragmentCommonData)0;
#ifdef LayaStandardPBR
	o = LayaMetallicSetup(i_tex);
#else
	o = LayaSpecularSetup(i_tex);
#endif
	o.normalWorld = LayaPerPixelWorldNormal(i_tex, tangentToWorld);
	o.eyeVec = LayaNormalizePerPixelNormal(i_eyeVec);
	o.posWorld = i_posWorld;
	o.diffColor = LayaPreMultiplyAlpha(o.diffColor, alpha, o.oneMinusReflectivity, /*out*/ o.alpha);
	return o;
}

float LayaSmoothnessToPerceptualRoughness(float smoothness)
{
	return (1 - smoothness);
}
Laya_GlossyEnvironmentData LayaGlossyEnvironmentSetup(half Smoothness, half3 worldViewDir, half3 Normal, half3 fresnel0)
{
	Laya_GlossyEnvironmentData g;
	g.roughness /* perceptualRoughness */ = LayaSmoothnessToPerceptualRoughness(Smoothness);
	g.reflUVW = reflect(-worldViewDir, Normal);

	return g;
}

half3 LayaSHEvalLinearL0L1(half4 normal)
{
	half3 x;
	x.r = dot(unity_SHAr, normal);
	x.g = dot(unity_SHAg, normal);
	x.b = dot(unity_SHAb, normal);

	return x;
}
half3 LayaSHEvalLinearL2(half4 normal)
{
	half3 x1, x2;
	half4 vB = normal.xyzz * normal.yzzx;
	x1.r = dot(unity_SHBr, vB);
	x1.g = dot(unity_SHBg, vB);
	x1.b = dot(unity_SHBb, vB);

	half vC = normal.x*normal.x - normal.y*normal.y;
	x2 = unity_SHC.rgb * vC;

	return x1 + x2;
}

half3 LayaShadeSHPerPixel(half3 normal, half3 ambient, float3 worldPos)
{
	half3 ambient_contrib = 0.0;
#if (SHADER_TARGET >= 30)
	ambient_contrib = LayaSHEvalLinearL0L1(half4(normal, 1.0));
	ambient_contrib += LayaSHEvalLinearL2(half4(normal, 1.0));
	ambient += max(half3(0, 0, 0), ambient_contrib);
#endif

#ifdef UNITY_COLORSPACE_GAMMA
	ambient = LinearToGammaSpace(ambient);
#endif
	return ambient;
}
half3 LayaShadeSH9(half4 normal)
{
	half3 res = LayaSHEvalLinearL0L1(normal);
	res += LayaSHEvalLinearL2(normal);
#   ifdef UNITY_COLORSPACE_GAMMA
	res = LinearToGammaSpace(res);
#   endif
	return res;
}

half3 LayaShadeSHPerVertex(half3 normal, half3 ambient)
{
	#if UNITY_SAMPLE_FULL_SH_PER_PIXEL

	#elif (SHADER_TARGET < 30)
		ambient += max(half3(0, 0, 0), LayaShadeSH9(half4(normal, 1.0)));
	#endif

	return ambient;
}

inline LayaGI LayaGI_Base(LayaGIInput data, half occlusion, half3 normalWorld)
{
	LayaGI o_gi;
	o_gi = (LayaGI)0;
	o_gi.light = data.light;
	o_gi.light.color *= data.atten;
#if UNITY_SHOULD_SAMPLE_SH
	o_gi.indirect.diffuse = LayaShadeSHPerPixel(normalWorld, data.ambient, data.worldPos);
#endif
#if defined(LIGHTMAP_ON)
	half4 bakedColorTex = UNITY_SAMPLE_TEX2D(unity_Lightmap, data.lightmapUV.xy);
	half3 bakedColor = DecodeLightmap(bakedColorTex);

	#ifdef DIRLIGHTMAP_COMBINED
		fixed4 bakedDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, data.lightmapUV.xy);
		o_gi.indirect.diffuse += DecodeDirectionalLightmap(bakedColor, bakedDirTex, normalWorld);

		// #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
		// 	ResetUnityLight(o_gi.light);
		// 	o_gi.indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(o_gi.indirect.diffuse, data.atten, bakedColorTex, normalWorld);
		// #endif

	#else 
		o_gi.indirect.diffuse += bakedColor;

		#if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
			ResetUnityLight(o_gi.light);
			o_gi.indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(o_gi.indirect.diffuse, data.atten, bakedColorTex, normalWorld);
		#endif
	#endif
#endif
	o_gi.indirect.diffuse *= occlusion;
	return o_gi;
}
inline half3 LayaDecodeHDR(half4 data, half4 decodeInstructions)
{
	half alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;
#if defined(UNITY_COLORSPACE_GAMMA)
	return (decodeInstructions.x* alpha) * data.rgb;
#else
#   if defined(UNITY_USE_NATIVE_HDR)
	return decodeInstructions.x * data.rgb;
#   else
	return (decodeInstructions.x * pow(alpha, decodeInstructions.y)) * data.rgb;
#   endif
#endif
}


half3 Laya_GlossyEnvironment(UNITY_ARGS_TEXCUBE(tex), half4 hdr, Laya_GlossyEnvironmentData glossIn)
{
	half perceptualRoughness = glossIn.roughness ;
	perceptualRoughness = perceptualRoughness * (1.7 - 0.7*perceptualRoughness);
	half mip = perceptualRoughness * 6;
	half3 R = glossIn.reflUVW;
	half4 rgbm = UNITY_SAMPLE_TEXCUBE_LOD(tex, R, mip);

	return LayaDecodeHDR(rgbm, hdr);
}

float3 LayaBoxProjectedCubemapDirection(float3 worldRefl, float3 worldPos, float4 cubemapCenter, float4 boxMin, float4 boxMax)
{
	UNITY_BRANCH
		if (cubemapCenter.w > 0.0)
		{
			float3 nrdir = normalize(worldRefl);

#if 1
			float3 rbmax = (boxMax.xyz - worldPos) / nrdir;
			float3 rbmin = (boxMin.xyz - worldPos) / nrdir;

			float3 rbminmax = (nrdir > 0.0f) ? rbmax : rbmin;

#else 
			float3 rbmax = (boxMax.xyz - worldPos);
			float3 rbmin = (boxMin.xyz - worldPos);

			float3 select = step(float3(0, 0, 0), nrdir);
			float3 rbminmax = lerp(rbmax, rbmin, select);
			rbminmax /= nrdir;
#endif

			float fa = min(min(rbminmax.x, rbminmax.y), rbminmax.z);

			worldPos -= cubemapCenter.xyz;
			worldRefl = worldPos + nrdir * fa;
		}
	return worldRefl;
}

inline half3 LayaGI_IndirectSpecular(LayaGIInput data, half occlusion, Unity_GlossyEnvironmentData glossIn)
{
	half3 specular;
#ifdef UNITY_SPECCUBE_BOX_PROJECTION
	half3 originalReflUVW = glossIn.reflUVW;
	glossIn.reflUVW = LayaBoxProjectedCubemapDirection(originalReflUVW, data.worldPos, data.probePosition[0], data.boxMin[0], data.boxMax[0]);
#endif
#ifdef _GLOSSYREFLECTIONS_OFF
	specular = unity_IndirectSpecColor.rgb;
#else
	half3 env0 = Laya_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), data.probeHDR[0], glossIn);
	specular = env0;
#endif

	return specular * occlusion;
}
LayaGI LayaGlobalIllumination(LayaGIInput data, half occlusion, half3 normalWorld, Laya_GlossyEnvironmentData glossIn)
{
	LayaGI o_gi = LayaGI_Base(data, occlusion, normalWorld);
	o_gi.indirect.specular = LayaGI_IndirectSpecular(data, occlusion, glossIn);
	return o_gi;
}
LayaGI LayaFragmentGI(LayaFragmentCommonData s, half occlusion, half4 i_ambientOrLightmapUV, half atten, UnityLight light)
{
	LayaGIInput d;
	d.light = light;
	d.worldPos = s.posWorld;
	d.worldViewDir = -s.eyeVec;
	d.atten = atten;
#if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
	d.ambient = 0;
	d.lightmapUV = i_ambientOrLightmapUV;
#else
	d.ambient = i_ambientOrLightmapUV.rgb;
	d.lightmapUV = 0;
#endif
	d.probeHDR[0] = unity_SpecCube0_HDR;
	d.probeHDR[1] = unity_SpecCube1_HDR;
#if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
	d.boxMin[0] = unity_SpecCube0_BoxMin;
#endif

#ifdef UNITY_SPECCUBE_BOX_PROJECTION
	d.boxMax[0] = unity_SpecCube0_BoxMax;
	d.probePosition[0] = unity_SpecCube0_ProbePosition;
	d.boxMax[1] = unity_SpecCube1_BoxMax;
	d.boxMin[1] = unity_SpecCube1_BoxMin;
	d.probePosition[1] = unity_SpecCube1_ProbePosition;
#endif
	Laya_GlossyEnvironmentData g = LayaGlossyEnvironmentSetup(s.smoothness, -s.eyeVec, s.normalWorld, s.specColor);
	return LayaGlobalIllumination(d, occlusion, s.normalWorld, g);
}
half4 LayaVertexGIForward(VertexInput v, float3 posWorld, half3 normalWorld)
{
	half4 ambientOrLightmapUV = 0;
#ifdef LIGHTMAP_ON
	ambientOrLightmapUV.xy = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
	ambientOrLightmapUV.zw = 0;
#elif UNITY_SHOULD_SAMPLE_SH
	ambientOrLightmapUV.rgb = LayaShadeSHPerVertex(normalWorld, ambientOrLightmapUV.rgb);
#endif

	return ambientOrLightmapUV;
}
half4 LayaOutputForward(half4 output, half alphaFromSurface)
{
	output.a = alphaFromSurface;
	return output;
}

half LayaLerpOneTo(half b, half t)
{
	half oneMinusT = 1 - t;
	return oneMinusT + b * t;
}

half LayaOcclusion(float2 uv)
{
#if (SHADER_TARGET < 30)
	return tex2D(occlusionTexture, uv).g;
#else
	half occ = tex2D(occlusionTexture, uv).g;
	return LayaLerpOneTo(occ, occlusionTextureStrength);
#endif
}

half LayaSmoothnessToRoughness(half smoothness)
{
	return (1 - smoothness) * (1 - smoothness);
}
