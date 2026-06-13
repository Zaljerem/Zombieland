Shader "Custom/ZombieBlob"
{
	Properties
	{
		_MainTex ("Blob Mask", 2D) = "white" {}
		_Color ("Tint", Color) = (1, 1, 1, 1)
		_BlobOpacityMin ("Blob Opacity Min", Range(0, 1)) = 0.42
		_BlobOpacityMax ("Blob Opacity Max", Range(0, 1)) = 0.76
		_BlobNoiseScale ("Blob Wave Scale", Float) = 2.00
		_BlobFlowSpeed ("Blob Wave Phase Speed", Float) = 0.45
		_BlobWaveShadeStrength ("Blob Wave Shade Strength", Range(0, 1)) = 0.68
		_BlobEdgeContrast ("Blob Edge Contrast", Range(0, 1)) = 0.95
		_BlobNoiseTime ("Blob Noise Time Seconds", Float) = 0
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
			float _BlobFlowSpeed;
			float _BlobWaveShadeStrength;
			float _BlobEdgeContrast;
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

			float GooWave(float2 worldXZ)
			{
				float scale = max(_BlobNoiseScale, 0.001);
				float phase = _BlobNoiseTime * _BlobFlowSpeed;
				float2 dirA = normalize(float2(1.0, 1.0));
				float2 dirB = normalize(float2(1.0, -1.0));
				float2 q = worldXZ;
				float2 warp = float2(
					sin(dot(q, float2(0.31, 0.73)) * scale * 0.42 + 1.10),
					cos(dot(q, float2(-0.79, 0.26)) * scale * 0.39 - 0.60)
				) * ((0.18 + 0.04 * sin(phase * 0.29 + 0.70)) / scale);
				float2 p = q + warp;
				float x = p.x * scale;
				float z = p.y * scale;
				float waveX = sin(x * 1.00 + 0.35) * sin(phase * 0.61 + 0.20);
				float waveZ = sin(z * 1.04 + 1.85) * sin(phase * 0.55 + 1.60);
				float waveA = sin(dot(p, dirA) * scale * 1.02 + 2.55) * sin(phase * 0.47 + 2.80);
				float waveB = sin(dot(p, dirB) * scale * 0.98 - 1.20) * sin(phase * 0.43 + 4.10);
				float waveCross = sin(x * 0.92 + 0.80) * sin(z * 0.96 - 1.30) * sin(phase * 0.37 + 2.20);
				float field = waveX * 0.25 + waveZ * 0.25 + waveA * 0.22 + waveB * 0.22 + waveCross * 0.18;
				return smoothstep(-0.50, 0.50, field);
			}

			fixed4 frag(v2f input) : SV_Target
			{
				fixed4 color = tex2D(_MainTex, input.uv) * _Color;
				if (color.a <= 0.001)
					return color;

				float maskAlpha = color.a;
				float cluster = GooWave(input.worldXZ);
				float shadeBand = saturate(1.0 - abs(cluster - 0.50) / 0.42);
				shadeBand = shadeBand * shadeBand * (3.0 - 2.0 * shadeBand);
				float3 waveShade = lerp(float3(1.0, 1.0, 1.0), float3(0.03, 0.20, 0.04), saturate(shadeBand * _BlobWaveShadeStrength));
				float edgeCore = smoothstep(0.012, 0.055, maskAlpha) * (1.0 - smoothstep(0.115, 0.235, maskAlpha));
				float edgeFeather = smoothstep(0.004, 0.025, maskAlpha) * (1.0 - smoothstep(0.210, 0.360, maskAlpha));
				float edgeBand = saturate(edgeCore * 1.55 + edgeFeather * 0.35);
				float edgeImpact = saturate(edgeBand * _BlobEdgeContrast);
				float3 edgeShade = lerp(float3(1.0, 1.0, 1.0), float3(0.00, 0.04, 0.00), edgeImpact);
				color.rgb *= waveShade * edgeShade;

				float minOpacity = saturate(min(_BlobOpacityMin, _BlobOpacityMax));
				float maxOpacity = saturate(max(_BlobOpacityMin, _BlobOpacityMax));
				float opacity = lerp(minOpacity, maxOpacity, cluster);
				color.a = saturate(color.a * opacity + edgeImpact * (1.0 - saturate(maskAlpha * 1.65)) * 0.12);
				return color;
			}
			ENDCG
		}
	}
}
