half LayaOcclusion(float2 uv)
{
	half occ = tex2D(_OcclusionMap, uv).g;
	return LerpOneTo (occ, _OcclusionStrength);
}

half3 LayaUnpackScaleNormal(half4 packednormal, half bumpScale)
{
	half3 normal;
	packednormal.x *= packednormal.w;
	normal.xy = (packednormal.xy * 2 - 1);
	normal.xy *= bumpScale;
	normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
	return normal;
}

half2 LayaParallaxOffset(half h, half height, half3 viewDir )
{
    h = h * height - height/2.0;
    half3 v = normalize(viewDir);
    v.z += 0.42;
    return h * (v.xy / v.z);
}

half LayaSpecularStrength(half3 specular)
{
	return max(max(specular.r, specular.g), specular.b);
}

inline half3 LayaEnergyConservationBetweenDiffuseAndSpecular(half3 albedo, half3 specColor, out half oneMinusReflectivity)
{
	oneMinusReflectivity = 1 - LayaSpecularStrength(specColor);
	return albedo * oneMinusReflectivity;
}

inline half3 LayaPreMultiplyAlpha(half3 diffColor, half alpha, half oneMinusReflectivity, out half outModifiedAlpha)
{
	#if defined(ALPHAPREMULTIPLY_ON)
		diffColor *= alpha;
		outModifiedAlpha = 1 - oneMinusReflectivity + alpha * oneMinusReflectivity;
	#else
		outModifiedAlpha = alpha;
	#endif
	return diffColor;
}
