// A window to the player's real surroundings (passthrough). Used with the BSPassthrough component.
//
// On a headset that offers core OpenXR alpha-blend passthrough, the client sets _BS_PassthroughActive and the
// "PassthroughHole" pass clears this surface to alpha 0 (the runtime shows its camera view there) and marks
// stencil bit 128, so the client's late pass can keep every other pixel opaque. Anywhere else - no
// passthrough on this runtime, desktop, the editor, and every mono camera (photos, recordings, mirrors,
// portals) - the "PassthroughFallback" pass draws _FallbackColor instead.
//
// Only one of the two passes draws: the other collapses its triangles before the rasteriser (no clip, so
// early depth and stencil stay on). No keywords of its own, so world bundles carry every variant it needs.
Shader "BS/Passthrough"
{
	Properties
	{
		_Opacity("Opacity", Range(0, 1)) = 1
		[NoScaleOffset] _MaskTex("Mask (alpha)", 2D) = "white" {}
		_FallbackColor("Fallback Colour", Color) = (0, 0, 0, 1)
		[Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
	}

	HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		TEXTURE2D(_MaskTex);	SAMPLER(sampler_MaskTex);

		CBUFFER_START(UnityPerMaterial)
			half _Opacity;
			half4 _FallbackColor;
			float _Cull;
		CBUFFER_END

		// Set by the client while passthrough is on. A global: not in the Properties block or UnityPerMaterial,
		// so no material value can override it.
		float _BS_PassthroughActive;

		struct Attributes
		{
			float4 positionOS : POSITION;
			float2 uv : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct Varyings
		{
			float4 positionCS : SV_POSITION;
			float2 uv : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
		};

		// Passthrough exists only in the headset's own two-eye view.
		bool PassthroughShowing()
		{
		#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
			return _BS_PassthroughActive > 0.5;
		#else
			return false;
		#endif
		}

		Varyings VertPass(Attributes v, bool draw)
		{
			Varyings o = (Varyings)0;
			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_TRANSFER_INSTANCE_ID(v, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

			// Every vertex of a pass that shouldn't draw lands on one point outside the clip volume.
			o.positionCS = draw ? TransformObjectToHClip(v.positionOS.xyz) : float4(2.0, 2.0, 2.0, 1.0);
			o.uv = v.uv;
			return o;
		}

		half Coverage(Varyings i)
		{
			return saturate(_Opacity * SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).a);
		}
	ENDHLSL

	SubShader
	{
		Tags
		{
			// After opaques and the skybox (so it can cut through them), before ordinary transparents (which
			// still draw over it).
			"Queue"="Transparent-1"
			"RenderType"="Transparent"
			"IgnoreProjector"="True"
			"RenderPipeline"="UniversalPipeline"
		}
		LOD 100

		ZWrite On
		ZTest LEqual
		Cull [_Cull]

		Pass
		{
			Name "PassthroughHole"
			Tags { "LightMode"="SRPDefaultUnlit" }

			// Colour fades to black and alpha is set to exactly 1 - coverage: with full coverage the pixel
			// becomes (0, 0, 0, 0), which the runtime shows as the room.
			Blend Zero SrcAlpha, One Zero
			Stencil
			{
				Ref 128
				WriteMask 128
				Comp Always
				Pass Replace
			}

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing

			Varyings vert(Attributes v)
			{
				return VertPass(v, PassthroughShowing());
			}

			half4 frag(Varyings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				return half4(0, 0, 0, 1 - Coverage(i));
			}
			ENDHLSL
		}

		Pass
		{
			Name "PassthroughFallback"
			Tags { "LightMode"="UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing

			Varyings vert(Attributes v)
			{
				return VertPass(v, !PassthroughShowing());
			}

			half4 frag(Varyings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				return half4(_FallbackColor.rgb, _FallbackColor.a * Coverage(i));
			}
			ENDHLSL
		}
	}
	Fallback Off
}
