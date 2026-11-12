// Ported to URP (was built-in CGPROGRAM pass using _WorldSpaceLightPos0 / _LightColor0 / ShadeSH9).
// Body lives in Runtime/Shaders/SQDiffuseCore.hlsl, shared with the transparent and triplanar twins.
// Under Resources so Shader.Find sees it in a player build: every material is created at runtime,
// so nothing else in a build references it.
Shader "Unlit/Diffuse"
{
	Properties
	{
		[MainTexture] _MainTex("Texture", 2D) = "white" {}
		[MainColor] _Color("Color", Color) = (1, 1, 1, 1)
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 0.0
		[Header(Optional Maps)]
		[NoScaleOffset] _NormalMap("Normal Map (RGB, linear)", 2D) = "bump" {}
		_NormalStrength("Normal Strength", Range(0, 2)) = 1
		[NoScaleOffset] _RoughnessMap("Roughness (G, white = rough)", 2D) = "white" {}
		_SpecStrength("Specular Strength", Range(0, 2)) = 0.5
		[NoScaleOffset] _AOMap("Ambient Occlusion (R)", 2D) = "white" {}
		_AOStrength("AO Strength", Range(0, 1)) = 1
		[Header(Triplanar)]
		_TriplanarScale("Tiles Per Metre", Float) = 1
		_TriplanarSharpness("Blend Sharpness", Range(1, 16)) = 4
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
			// Per-material optional maps, set by BSMaterial. multi_compile (not shader_feature):
			// every material is created at runtime, so nothing in the build references a
			// variant and shader_feature ones would be stripped.
			#pragma multi_compile_local _ _BS_NORMALMAP
			#pragma multi_compile_local _ _BS_MASKMAPS

			#define SQ_TRIPLANAR 0
			#define SQ_TRANSPARENT 0
			#include "Packages/com.sidequest.creator-sdk/Runtime/Shaders/SQDiffuseCore.hlsl"
			ENDHLSL
		}
	}
	Fallback "Universal Render Pipeline/Unlit"
}
