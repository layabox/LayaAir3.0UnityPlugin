// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Laya/Sky Box" {
Properties {
	tintColor("Tint Color", Color) = (.5, .5, .5, .5)
   exposure("Exposure", Range(0, 8)) = 1.0
    rotation ("Rotation", Range(0, 360)) = 0
    [NoScaleOffset] textureCube("Cubemap   (HDR)", Cube) = "grey" {}
}

SubShader {
    Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
    Cull Off ZWrite Off

    Pass {

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 2.0

        samplerCUBE textureCube;
        half4 tintColor;
        half exposure;
        float rotation;

        float3 RotateAroundYInDegrees (float3 vertex, float degrees)
        {
            float alpha = degrees * 3.141593 / 180.0;
            float sina, cosa;
            sincos(alpha, sina, cosa);
            float2x2 m = float2x2(cosa, -sina, sina, cosa);
            return float3(mul(m, vertex.xz), vertex.y).xzy;
        }

        struct appdata_t {
            float4 vertex : POSITION;
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float3 texcoord : TEXCOORD0;
        };

        v2f vert (appdata_t v)
        {
            v2f o;
            float3 rotated = RotateAroundYInDegrees(v.vertex, rotation);
            o.vertex = UnityObjectToClipPos(rotated);
            o.texcoord = v.vertex.xyz;
            return o;
        }

        fixed4 frag (v2f i) : SV_Target
        {
            half4 tex = texCUBE (textureCube, i.texcoord);
            half3 c = tex.rgb;
            c = c * tintColor.rgb * 2.0;
            c *= exposure;
            return half4(c, 1);
        }
        ENDCG
    }
}


Fallback Off

}
