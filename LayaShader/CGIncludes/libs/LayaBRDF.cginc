

half LayaPow5(half x)
{
	return x * x * x*x * x;
}

float3 Laya_SafeNormalize(float3 inVec)
{
	float dp3 = max(0.001f, dot(inVec, inVec));
	return inVec * rsqrt(dp3);
}


half LayaDisneyDiffuse(half NdotV, half NdotL, half LdotH, half perceptualRoughness)
{
	half fd90 = 0.5 + 2 * LdotH * LdotH * perceptualRoughness;
	half lightScatter = (1 + (fd90 - 1) * LayaPow5(1 - NdotL));
	half viewScatter = (1 + (fd90 - 1) * LayaPow5(1 - NdotV));

	return lightScatter * viewScatter;
}


// Ref: http://jcgt.org/published/0003/02/03/paper.pdf
float LayaSmithJointGGXVisibilityTerm(float NdotL, float NdotV, float roughness)
{
	float a = roughness;
	float lambdaV = NdotL * (NdotV * (1 - a) + a);
	float lambdaL = NdotV * (NdotL * (1 - a) + a);
	return 0.5f / (lambdaV + lambdaL + 1e-5f);
}

float LayaGGXTerm(float NdotH, float roughness)
{
	float a2 = roughness * roughness;
	float d = (NdotH * a2 - NdotH) * NdotH + 1.0f; 
	return UNITY_INV_PI * a2 / (d * d + 1e-7f); 
}




inline half3 LayaFresnelTerm(half3 F0, half cosA)
{
	half t = Pow5(1 - cosA);  
	return F0 + (1 - F0) * t;
}
inline half3 LayaFresnelLerp(half3 F0, half3 F90, half cosA)
{
	half t = Pow5(1 - cosA);  
	return lerp(F0, F90, t);
}


half4 BRDF1_Laya_PBS(half3 diffColor, half3 specColor, half oneMinusReflectivity, half smoothness,float3 normal, float3 viewDir,LayaLight light, LayaIndirect gi)
{
	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	float3 halfDir = Laya_SafeNormalize(float3(light.dir) + viewDir);
	half nv = abs(dot(normal, viewDir)); 
	float nl = saturate(dot(normal, light.dir));
	float nh = saturate(dot(normal, halfDir));
	half lv = saturate(dot(light.dir, viewDir));
	half lh = saturate(dot(light.dir, halfDir));
	half diffuseTerm = LayaDisneyDiffuse(nv, nl, lh, perceptualRoughness) * nl;
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	roughness = max(roughness, 0.002);
	float V = LayaSmithJointGGXVisibilityTerm(nl, nv, roughness);
	float D = LayaGGXTerm(nh, roughness);
	float specularTerm = V * D * 3.14159265359; 
#   ifdef UNITY_COLORSPACE_GAMMA
		specularTerm = sqrt(max(1e-4h, specularTerm));
#   endif
	specularTerm = max(0, specularTerm * nl);

	half surfaceReduction;
#   ifdef UNITY_COLORSPACE_GAMMA
		surfaceReduction = 1.0 - 0.28*roughness*perceptualRoughness;  
#   else
		surfaceReduction = 1.0 / (roughness*roughness + 1.0);         
#   endif


	specularTerm *= any(specColor) ? 1.0 : 0.0;

	half grazingTerm = saturate(smoothness + (1 - oneMinusReflectivity));
	half3 color = diffColor * (gi.diffuse)
	 +(diffColor*light.color * diffuseTerm)
	+ specularTerm * light.color * LayaFresnelTerm(specColor, lh)
	+ surfaceReduction * gi.specular * LayaFresnelLerp(specColor, grazingTerm, nv);
	return half4(color, 1);
}




half3 LayaBRDF3_Direct(half3 diffColor, half3 specColor, half rlPow4, half smoothness)
{
	half LUT_RANGE = 16.0;
	half specular = tex2D(unity_NHxRoughness, half2(rlPow4, SmoothnessToPerceptualRoughness(smoothness))).r * LUT_RANGE;

	return diffColor + specular * specColor;
}
half3 LayaBRDF3_Indirect(half3 diffColor, half3 specColor, LayaIndirect indirect, half grazingTerm, half fresnelTerm)
{
	half3 c = indirect.diffuse * diffColor;
	c += indirect.specular * lerp(specColor, grazingTerm, fresnelTerm);
	return c;
}



half4 BRDF3_Laya_PBS(half3 diffColor, half3 specColor, half oneMinusReflectivity, half smoothness,
	float3 normal, float3 viewDir,
	LayaLight light, LayaIndirect gi)
{
	float3 reflDir = reflect(viewDir, normal);

	half nl = saturate(dot(normal, light.dir));
	half nv = saturate(dot(normal, viewDir));


	half2 rlPow4AndFresnelTerm = Pow4(float2(dot(reflDir, light.dir), 1 - nv));  
	half rlPow4 = rlPow4AndFresnelTerm.x;

	half fresnelTerm = rlPow4AndFresnelTerm.y;

	half grazingTerm = saturate(smoothness + (1 - oneMinusReflectivity));
	half3 color = BRDF3_Direct(diffColor, specColor, rlPow4, smoothness);
	color *= light.color * nl;
	color += BRDF3_Indirect(diffColor, specColor, gi, grazingTerm, fresnelTerm);

	return half4(color, 1);
}
