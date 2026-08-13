// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X
// Ported to URP by hand (was a built-in "#pragma surface surf Standard" surface
// shader, which URP cannot compile). Re-saving this shader from Amplify will
// regenerate surface-shader code and undo the port - edit the HLSL below instead.
Shader "Banter/FakeReflectiveA"
{
	Properties
	{
		[MainTexture] _MainTex("MainTex", 2D) = "black" {}
		_Gloss("Gloss", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags
		{
			"RenderType"="Opaque"
			"Queue"="Geometry+0"
			"RenderPipeline"="UniversalPipeline"
		}
		LOD 300

		// Shared material data, kept outside the passes so every pass sees the
		// same UnityPerMaterial layout (required for the SRP Batcher).
		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
			float4 _MainTex_ST;
			half _Gloss;
		CBUFFER_END
		ENDHLSL

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode"="UniversalForward" }

			Cull Back
			ZWrite On

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan

			// Universal Pipeline keywords (equivalent of "fullforwardshadows")
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_MainTex);	SAMPLER(sampler_MainTex);

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
				float2 staticLightmapUV : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				half3 normalWS : TEXCOORD2;
				DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 3);
				half fogFactor : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes v)
			{
				Varyings o = (Varyings)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
				o.positionCS = TransformWorldToHClip(o.positionWS);
				o.normalWS = TransformObjectToWorldNormal(v.normalOS);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);

				OUTPUT_LIGHTMAP_UV(v.staticLightmapUV, unity_LightmapST, o.staticLightmapUV);
				OUTPUT_SH(o.normalWS.xyz, o.vertexSH);

				o.fogFactor = ComputeFogFactor(o.positionCS.z);
				return o;
			}

			half4 frag(Varyings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				half4 tex2DNode3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = tex2DNode3.rgb;
				surfaceData.metallic = 0.0;
				surfaceData.specular = 0.0;
				// Original surface shader: ((1 - _MainTex) - _Gloss).r
				// Saturated because URP feeds smoothness straight into roughness;
				// out-of-range values produce speckle on mobile.
				surfaceData.smoothness = saturate((1.0 - tex2DNode3.r) - _Gloss);
				surfaceData.normalTS = half3(0, 0, 1);
				surfaceData.emission = half3(0, 0, 0);
				surfaceData.occlusion = 1.0;
				surfaceData.alpha = 1.0;
				surfaceData.clearCoatMask = 0.0;
				surfaceData.clearCoatSmoothness = 0.0;

				InputData inputData = (InputData)0;
				inputData.positionWS = i.positionWS;
				inputData.normalWS = NormalizeNormalPerPixel(i.normalWS);
				inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
				#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
					inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
				#else
					inputData.shadowCoord = float4(0, 0, 0, 0);
				#endif
				inputData.fogCoord = i.fogFactor;
				inputData.vertexLighting = half3(0, 0, 0);
				inputData.bakedGI = SAMPLE_GI(i.staticLightmapUV, i.vertexSH, inputData.normalWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
				inputData.shadowMask = half4(1, 1, 1, 1);

				half4 color = UniversalFragmentPBR(inputData, surfaceData);
				color.rgb = MixFog(color.rgb, inputData.fogCoord);
				color.a = 1.0;
				return color;
			}
			ENDHLSL
		}

		// Equivalent of the surface shader's "addshadow"
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull Back

			HLSLPROGRAM
			#pragma vertex vertShadow
			#pragma fragment fragShadow
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			// Set by URP when rendering the shadow map - must stay outside UnityPerMaterial.
			float3 _LightDirection;
			float3 _LightPosition;

			struct AttributesShadow
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VaryingsShadow
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			VaryingsShadow vertShadow(AttributesShadow v)
			{
				VaryingsShadow o = (VaryingsShadow)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

				#if _CASTING_PUNCTUAL_LIGHT_SHADOW
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif

				float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

				#if UNITY_REVERSED_Z
					positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
					positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif

				o.positionCS = positionCS;
				return o;
			}

			half4 fragShadow(VaryingsShadow i) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		// Depth prepass / depth texture support
		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask R
			Cull Back

			HLSLPROGRAM
			#pragma vertex vertDepth
			#pragma fragment fragDepth
			#pragma target 3.0
			#pragma only_renderers d3d11 glcore gles3 vulkan
			#pragma multi_compile_instancing

			struct AttributesDepth
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VaryingsDepth
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			VaryingsDepth vertDepth(AttributesDepth v)
			{
				VaryingsDepth o = (VaryingsDepth)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
				return o;
			}

			half4 fragDepth(VaryingsDepth i) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}
	Fallback "Universal Render Pipeline/Lit"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.OneMinusNode;18;-229,33.70343;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;132,-91;Half;False;True;-1;2;ASEMaterialInspector;0;0;Standard;Banter/FakeReflectiveA;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Opaque;0.5;True;True;0;False;Opaque;;Geometry;All;4;d3d11;glcore;gles;gles3;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SamplerNode;3;-541,-162;Inherit;True;Property;_MainTex;MainTex;0;0;Create;True;0;0;0;False;0;False;-1;None;0366facf3caa79244a4f1ff4f5457572;True;0;False;black;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleSubtractOpNode;20;-30.5,126.2034;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;14;-461.0117,135.5932;Inherit;False;Property;_Gloss;Gloss;1;0;Create;True;0;0;0;False;0;False;0;0.694;0;1;0;1;FLOAT;0
WireConnection;18;0;3;0
WireConnection;0;0;3;0
WireConnection;0;4;20;0
WireConnection;20;0;18;0
WireConnection;20;1;14;0
ASEEND*/
//CHKSM=FB97DF64F16CD463AEB2B886AA1792DA755B3A82
