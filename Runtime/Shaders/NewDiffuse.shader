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

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

				// Calculate lighting from main directional light if it exists
				Light mainLight = GetMainLight();
				half NdotL = max(0.0, dot(normalWS, mainLight.direction));

				// Add ambient/environment lighting (works without lights)
				half3 ambient = SampleSH(normalWS);

				// Combine directional and ambient light, ensuring minimum brightness
				half3 lighting = max(0.6, max(ambient * 0.5 + 0.5, NdotL * mainLight.color + ambient));

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
