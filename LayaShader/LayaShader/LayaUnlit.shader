Shader "Laya/Unlit" {
	Properties{
		_MainTex("Albedo Texture", 2D) = "white" {}
		_Color("Albedo Color", Color) = (0.5,0.5,0.5,0.5)
		_AlbedoIntensity("Albedo Intensity", Range(1.0, 4.0)) = 1.0
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.01

		[HideInInspector] _Cull("__cull", Float) = 2.0
		[HideInInspector] _Mode("__mode", Float) = 0.0
		[ToggleOff] _IsVertexColor("vertexcolor",Float) = 0.0
		[HideInInspector] _SrcBlend("__src", Float) = 1.0
		[HideInInspector] _DstBlend("__dst", Float) = 0.0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
		[HideInInspector] _ZTest("__zt", Float) = 4.0
		[HideInInspector] _RenderQueue("__rq", Float) = 2000.0
	}
	SubShader{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Pass{
			Tags{ "LightMode" = "ForwardBase" }

			Blend[_SrcBlend][_DstBlend]
			ZWrite[_ZWrite]
			ZTest[_ZTest]
			Cull[_Cull]

			CGPROGRAM 

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#pragma multi_compile_fog
			#pragma shader_feature ADDTIVEFOG
			#pragma shader_feature EnableAlphaCutoff
			#pragma shader_feature ENABLEVERTEXCOLOR

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _Color;
			float _AlbedoIntensity;
			half _Cutoff;
			float4 _MainTex_ST;

			struct appdata_t {
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f {
				float4 pos : SV_POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
			};

			v2f vert(appdata_t v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.texcoord,_MainTex);
				UNITY_TRANSFER_FOG(o,o.pos);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 color;
				#if ENABLEVERTEXCOLOR
					color = tex2D(_MainTex, i.uv) * _Color * _AlbedoIntensity* i.color;
				#else
					color = tex2D(_MainTex, i.uv) * _Color * _AlbedoIntensity;
				#endif

			
				#if EnableAlphaCutoff
					clip(color.a - _Cutoff);
				#endif

				#if ADDTIVEFOG
					UNITY_APPLY_FOG_COLOR(i.fogCoord, color, fixed4(0, 0, 0, 0));
				#else
					UNITY_APPLY_FOG(i.fogCoord, color);
				#endif
				color = clamp(color, 0, 1);
				return color;
			}
			ENDCG
		}
	}
	CustomEditor "LayaUnlitGUI"
}
