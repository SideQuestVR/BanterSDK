// Alpha-blended twin of Unlit/Diffuse. Body lives in Runtime/Shaders/SQDiffuseCore.hlsl.
Shader "Unlit/DiffuseTransparent"
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
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"RenderPipeline"="UniversalPipeline"
		}
		LOD 100

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode"="UniversalForward" }

			Cull [_Cull]
			Blend SrcAlpha OneMinusSrcAlpha
			// ZWrite intentionally left On to match the original built-in pass.

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing
			// Runtime-toggled global keywords set by SideQuest.Lighting (LightingSystem).
			#pragma multi_compile _ _SQ_FAKE_SHADOWS
			#pragma multi_compile _ _SQ_AO_MAPS
			// Per-material optional maps, set by BSMaterial (see NewDiffuse.shader).
			#pragma multi_compile_local _ _BS_NORMALMAP
			#pragma multi_compile_local _ _BS_MASKMAPS

			#define SQ_TRIPLANAR 0
			#define SQ_TRANSPARENT 1
			#include "Packages/com.sidequest.creator-sdk/Runtime/Shaders/SQDiffuseCore.hlsl"
			ENDHLSL
		}
	}
	Fallback "Universal Render Pipeline/Unlit"
}
