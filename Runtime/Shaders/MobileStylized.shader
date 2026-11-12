// Ported to URP (was built-in ForwardBase + ShadowCaster using UnityCG.cginc)
Shader "Mobile/StylizedFakeLit"
{
    Properties
    {
        // Color Tint (per-material)
        [Header(Base)]
        [MainColor] _Color ("Color Tint", Color) = (1,1,1,1)

        // Hemisphere Lighting
        [Header(Hemisphere Lighting)]
        _SkyColor ("Sky Color", Color) = (0.8, 0.9, 1.0, 1)
        _GroundColor ("Ground Color", Color) = (0.2, 0.15, 0.1, 1)
        _HemisphereStrength ("Hemisphere Strength", Range(0,1)) = 0.7

        // Height AO (gradient-based ambient occlusion)
        [Header(Ambient Occlusion)]
        _AOStrength ("AO Strength", Range(0,1)) = 0.6
        [Toggle(_USE_OBJECT_BOUNDS)] _UseObjectBounds ("Use Object Bounds", Float) = 0
        _AOGroundLevel ("Ground Level (World Y)", Float) = 0
        _AOHeight ("AO Height (World)", Float) = 2
        _ObjectMinY ("Object Min Y (Local)", Float) = 0
        _ObjectMaxY ("Object Max Y (Local)", Float) = 1
        _AOPower ("AO Softness", Range(0.5,3)) = 1.5

        // Rim Lighting
        [Header(Rim Lighting)]
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.8, 1)
        _RimPower ("Rim Sharpness", Range(1,8)) = 3
        _RimStrength ("Rim Strength", Range(0,1)) = 0.4
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

        // Shared material data, kept outside the passes so every pass sees the
        // same UnityPerMaterial layout (required for the SRP Batcher).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;

            half4 _SkyColor;
            half4 _GroundColor;
            half _HemisphereStrength;

            half _AOStrength;
            half _UseObjectBounds;
            half _AOGroundLevel;
            half _AOHeight;
            half _ObjectMinY;
            half _ObjectMaxY;
            half _AOPower;

            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Mobile targets
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore gles3 vulkan

            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_OBJECT_BOUNDS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;  // Vertex color for baked AO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half3 viewDirWS : TEXCOORD1;
                // x = height AO, y = baked vertex color AO, z = fog factor
                half3 aoAndFog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // World position for AO and view direction
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);

                // World normal for hemisphere lighting
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);

                // View direction for fresnel (computed in vertex for mobile)
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                // Height AO calculation
                #if defined(_USE_OBJECT_BOUNDS)
                    // Object-space: use local vertex Y position with object min/max bounds
                    half objectHeight = _ObjectMaxY - _ObjectMinY;
                    half normalizedHeight = (v.positionOS.y - _ObjectMinY) / max(objectHeight, 0.001);
                #else
                    // World-space: always darkens at bottom regardless of object rotation
                    half normalizedHeight = (positionWS.y - _AOGroundLevel) / _AOHeight;
                #endif
                o.aoAndFog.x = pow(saturate(normalizedHeight), _AOPower);

                // Pass baked vertex color AO (use red channel as grayscale)
                o.aoAndFog.y = v.color.r;

                o.aoAndFog.z = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Base color
                half3 baseColor = _Color.rgb;

                // === HEMISPHERE LIGHTING ===
                // Remap normal.y from [-1,1] to [0,1]
                half hemisphere = i.normalWS.y * 0.5 + 0.5;
                half3 hemisphereLight = lerp(_GroundColor.rgb, _SkyColor.rgb, hemisphere);

                // Blend hemisphere with white based on strength
                half3 lighting = lerp(half3(1,1,1), hemisphereLight, _HemisphereStrength);

                // === HEIGHT-BASED AO ===
                half ao = lerp(1.0, i.aoAndFog.x, _AOStrength);

                // === BAKED VERTEX COLOR AO ===
                // Multiply with baked AO (1.0 = no occlusion, 0.0 = full occlusion)
                ao *= i.aoAndFog.y;

                // === RIM/FRESNEL LIGHTING ===
                half NdotV = saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS)));
                half fresnel = pow(1.0 - NdotV, _RimPower);
                half3 rim = _RimColor.rgb * fresnel * _RimStrength;

                // === FINAL COMPOSITE ===
                half3 finalColor = (baseColor * lighting * ao) + rim;

                // Apply fog
                finalColor = MixFog(finalColor, i.aoAndFog.z);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster pass for casting shadows
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

            // Differentiates directional from punctual light shadows, which use
            // different normal-bias formulas.
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

    Fallback "Universal Render Pipeline/Simple Lit"
}
