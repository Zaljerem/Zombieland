Shader "Custom/ZombieBlob"
{
	Properties
	{
		_MainTex ("Blob Mask", 2D) = "white" {}
		_Color ("Tint", Color) = (1, 1, 1, 1)
		_BlobOpacityMin ("Blob Opacity Min", Range(0, 1)) = 0.28
		_BlobOpacityMax ("Blob Opacity Max", Range(0, 1)) = 0.78
		_BlobNoiseScale ("Blob Noise Scale", Float) = 0.96
		_BlobNoiseDrift ("Blob Noise Drift", Float) = 0.05
		_BlobNoiseTime ("Blob Noise Time", Float) = 0
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#include "UnityCG.cginc"

			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float _BlobOpacityMin;
			float _BlobOpacityMax;
			float _BlobNoiseScale;
			float _BlobNoiseDrift;
			float _BlobNoiseTime;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float2 worldXZ : TEXCOORD1;
			};

			v2f vert(appdata input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				output.worldXZ = mul(unity_ObjectToWorld, input.vertex).xz;
				return output;
			}

			float Hash21(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			float SmoothValueNoise(float2 p)
			{
				float2 i = floor(p);
				float2 f = frac(p);
				f = f * f * (3.0 - 2.0 * f);

				float a = Hash21(i);
				float b = Hash21(i + float2(1.0, 0.0));
				float c = Hash21(i + float2(0.0, 1.0));
				float d = Hash21(i + float2(1.0, 1.0));
				return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
			}

			float ClusteredNoise(float2 p)
			{
				float n = SmoothValueNoise(p);
				n += SmoothValueNoise(p * 1.73 + float2(19.17, -7.31)) * 0.22;
				n += SmoothValueNoise(p * 3.31 + float2(-3.43, 29.79)) * 0.08;
				n /= 1.30;

				return smoothstep(0.32, 0.68, n);
			}

			fixed4 frag(v2f input) : SV_Target
			{
				fixed4 color = tex2D(_MainTex, input.uv) * _Color;
				if (color.a <= 0.001)
					return color;

				float scale = max(_BlobNoiseScale, 0.001);
				float drift = _BlobNoiseTime * _BlobNoiseDrift;
				float2 p = input.worldXZ * scale;
				float cluster = ClusteredNoise(p + float2(drift, -drift * 0.73));
				cluster = lerp(cluster, ClusteredNoise(p * 0.47 + float2(37.1, 11.8) - drift * 0.31), 0.18);

				float minOpacity = saturate(min(_BlobOpacityMin, _BlobOpacityMax));
				float maxOpacity = saturate(max(_BlobOpacityMin, _BlobOpacityMax));
				color.a *= lerp(minOpacity, maxOpacity, cluster);
				return color;
			}
			ENDCG
		}
	}
}
