// Ported to URP (was built-in CGPROGRAM unlit pass)
Shader "Unlit/TextureVertexColor"
{
	Properties
	{
		[MainTexture] _MainTex("Texture", 2D) = "white" {}
		[MainColor] _Color("Color", Color) = (1, 1, 1, 1)
	}

	SubShader
	{
		Tags
		{
			"RenderType"="Opaque"
			"Queue"="Geometry"
			"IgnoreProjector"="True"
			"RenderPipeline"="UniversalPipeline"
		}
		LOD 100

		Pass
		{
			// No LightMode tag: URP renders this via SRPDefaultUnlit, the same
			// way its own Universal Render Pipeline/Unlit main pass is drawn.
			Name "Unlit"

			Cull Back
			ZWrite On

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex);	SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				half4 _Color;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
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
				o.color = v.color;
				return o;
			}

			half4 frag(Varyings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				// Sample texture (loaded as linear to preserve alpha channel on ASTC)
				half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

				// Manually convert RGB from sRGB to linear (alpha stays linear to prevent corruption)
				texColor.rgb = pow(texColor.rgb, 2.2);

				// Check if vertex colors are nearly black (default/missing)
				// Unity provides black (0,0,0,1) for meshes without vertex colors
				half3 vertexColor = i.color.rgb;
				if (dot(vertexColor, vertexColor) < 0.01)
				{
					// No vertex colors or black - treat as white (no effect on texture)
					vertexColor = half3(1, 1, 1);
				}

				// Convert vertex color from sRGB to linear space
				// (matches Three.js behavior - GLTF vertex colors are in sRGB)
				half3 vertexColorLinear = pow(vertexColor, 2.2);

				// Multiply in linear space: texture x material color x vertex color
				half4 col = texColor * _Color * half4(vertexColorLinear, i.color.a);

				return col;
			}

			ENDHLSL
		}
	}
	Fallback "Universal Render Pipeline/Unlit"
}
