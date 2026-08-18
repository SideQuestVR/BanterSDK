// Ported to URP (was built-in CGPROGRAM pass using _WorldSpaceLightPos0 / _LightColor0 / ShadeSH9)
Shader "Unlit/Diffuse"
{
	Properties
	{
		[MainTexture] _MainTex("Texture", 2D) = "white" {}
		[MainColor] _Color("Color", Color) = (1, 1, 1, 1)
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 0.0
	}

	SubShader
	{
		Tags
		{
			"RenderType"="Opaque"
			"Queue"="Geometry"
			"RenderPipeline"="UniversalPipeline"
		}
		LOD 100

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode"="UniversalForward" }

			Cull [_Cull]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing
			// Runtime-toggled global keywords set by SideQuest.Lighting (LightingSystem).
			#pragma multi_compile _ _SQ_FAKE_SHADOWS
			#pragma multi_compile _ _SQ_AO_MAPS

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#if defined(_SQ_FAKE_SHADOWS) || defined(_SQ_AO_MAPS)
			#include "SQFakeShadow.hlsl"
			#endif

			TEXTURE2D(_MainTex);	SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				half4 _Color;
				float _Cull;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
			#if defined(_SQ_FAKE_SHADOWS) || defined(_SQ_AO_MAPS)
				float3 positionWS : TEXCOORD2;
			#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes v)
			{
				Varyings o = (Varyings)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.normalWS = TransformObjectToWorldNormal(v.normalOS);
			#if defined(_SQ_FAKE_SHADOWS) || defined(_SQ_AO_MAPS)
				o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
			#endif
				return o;
			}

			half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				// Sample texture
				half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

				// Combine with color
				half4 col = texColor * _Color;

				// Flip normal for back faces
				half3 normalWS = normalize(i.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);

				half3 lighting;
			#if defined(_SQ_FAKE_SHADOWS)
				// Fake directional light: direction/colour come from the _SQ_ globals, never
				// GetMainLight() — materials are shared scene-wide by BSMaterial's cache, so
				// all lighting state must be global. The 0.6 floor below is replaced by
				// tunable floors here so shadowed faces can actually darken.
				half  ndl    = saturate(dot(normalWS, _SQ_FakeLightDir.xyz));
				half  shadow = SQSampleFakeShadow(i.positionWS, normalWS);
				#if defined(_SQ_AO_MAPS)
					// AO is ambient attenuation, not a colour multiply: ambient is sampled
					// along the bent normal (light arrives from the open side of a corner)
					// and scaled by visibility; the sun gets only a mild micro-occlusion —
					// it already has a real shadow term.
					half aoVis; half3 bentN;
					SQSampleAOVolume(i.positionWS, normalWS, aoVis, bentN);
					half ao = lerp(1.0h, aoVis, _SQ_LightingParams.z);
					half3 amb = max(SampleSH(bentN), _SQ_LightingParams.yyy) * ao;
					half directAO = lerp(1.0h, aoVis, 0.5h * _SQ_LightingParams.z);
					lighting = max(_SQ_LightingParams.xxx, amb + ndl * shadow * directAO * _SQ_FakeLightColor.rgb);
				#else
					half3 amb = max(SampleSH(normalWS), _SQ_LightingParams.yyy);
					lighting = max(_SQ_LightingParams.xxx, amb + ndl * shadow * _SQ_FakeLightColor.rgb);
				#endif
			#else
				// Calculate lighting from main directional light if it exists
				Light mainLight = GetMainLight();
				half NdotL = max(0.0, dot(normalWS, mainLight.direction));

				// Add ambient/environment lighting (works without lights)
				half3 ambient = SampleSH(normalWS);

				// Combine directional and ambient light, ensuring minimum brightness
				lighting = max(0.6, max(ambient * 0.5 + 0.5, NdotL * mainLight.color + ambient));
				#if defined(_SQ_AO_MAPS)
					half aoVis; half3 bentN;
					SQSampleAOVolume(i.positionWS, normalWS, aoVis, bentN);
					lighting *= lerp(1.0h, aoVis, _SQ_LightingParams.z);
				#endif
			#endif

				// Apply lighting to color
				col.rgb *= lighting;
				col.a = 1;

				return col;
			}

			ENDHLSL
		}
	}
	Fallback "Universal Render Pipeline/Unlit"
}
