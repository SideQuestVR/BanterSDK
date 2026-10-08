// Unlit, see-through glow: MainTex scrolls along the strip, tinted by Color; alpha is the
// texture's alpha times Transparency. Casts a dithered shadow.

Shader "NeonGlow"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Color("Color", Color) = (0,0,0,0)
        _Transparency("Transparency", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Transparency;
        CBUFFER_END

        // The scroll ignores MainTex's tiling and offset, as the original did.
        float4 SurfaceUV(float2 uv0, float2 uv1) { return float4(uv0 * float2(0.05, 1.0), 0.0, 0.0); }

        half4 NeonGlowTexture(float2 uv) { return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(_Time.y, 0.0)); }

        // 4x4 ordered dither, standing in for the Built-in pipeline's _DitherMaskLOD: see-through
        // surfaces cast shadows whose density follows their alpha.
        static const float kBayer4x4[16] = { 0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0 };

        float DitherThreshold(float4 positionCS)
        {
            uint2 pixel = (uint2)positionCS.xy % 4u;
            return (kBayer4x4[pixel.y * 4u + pixel.x] + 0.5) / 16.0;
        }

        void SurfaceClip(float4 uv, half4 color, float4 positionCS)
        {
            clip(NeonGlowTexture(uv.xy).a * _Transparency - DitherThreshold(positionCS));
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS        : SV_POSITION;
                float4 uv                : TEXCOORD0;
                float3 positionWS        : TEXCOORD1;
                float3 normalWS          : TEXCOORD2;
                half4  color             : TEXCOORD3;
                float2 fogFactorAndDepth : TEXCOORD4; // x: fog factor, y: eye depth
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 SurfaceUnlit(float4 uv, half4 color, float3 positionWS, float3 normalWS, float eyeDepth)
            {
                half4 tex = NeonGlowTexture(uv.xy);
                return half4(tex.rgb * _Color.rgb, tex.a * _Transparency);
            }

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = SurfaceUV(input.uv0, input.uv1);
                output.color = input.color;
                output.fogFactorAndDepth = float2(ComputeFogFactor(vertexInput.positionCS.z), -vertexInput.positionVS.z);
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = SurfaceUnlit(input.uv, input.color, input.positionWS, input.normalWS, input.fogFactorAndDepth.y);
                color.rgb = MixFog(color.rgb, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndDepth.x));
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light whose shadow map is being drawn.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = SurfaceUV(input.uv0, input.uv1);
                output.color = input.color;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SurfaceClip(input.uv, input.color, input.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
