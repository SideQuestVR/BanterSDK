// The teleport target ring and line: bands scrolling through MainTex (on the mesh's second UV
// set), unlit and see-through, multiplied by the vertex colour.

Shader "BS/TeleportIndicator"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Color("Color", Color) = (0.6591351,0.1367925,1,0)
        _Speed("Speed", Float) = 1
        _EmittPower("EmittPower", Range(0, 1)) = 0.42
        _AltMode("AltMode", Range(0, 1)) = 0
        _Speed2("Speed2", Float) = 0.5
        _Offset("Offset", Float) = 0
        _XTiling("XTiling", Float) = 1
        _YTiling("YTiling", Float) = 1
        _BandOnly("BandOnly", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Speed;
            float _EmittPower;
            float _AltMode;
            float _Speed2;
            float _Offset;
            float _XTiling;
            float _YTiling;
            float _BandOnly;
        CBUFFER_END

        float4 SurfaceUV(float2 uv0, float2 uv1) { return float4(uv1 * float2(_XTiling, _YTiling) + float2(0.0, _Offset), 0.0, 0.0); }

        void SurfaceClip(float4 uv, half4 color, float4 positionCS) {}
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
                float time = _Time.y * _Speed;
                float band = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv.xy + float2(time, 0.0)).r;
                float2 flippedUV = float2(uv.x, (1.0 - uv.y) - 0.5);
                float lines = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv.xy + float2(time * _Speed2, 0.0)).g
                            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, flippedUV + float2(time * (_Speed2 + 0.5), 0.0)).g;
                float sum = band + lines;
                float pattern = lerp(lerp(sum, band * lines, _AltMode), band, _BandOnly);
                half3 rgb = saturate(_Color + pattern * (_Color + _EmittPower)).rgb * color.rgb;
                half alpha = lerp(smoothstep(0.05, 1.0, sum), band, _BandOnly);
                return half4(rgb, alpha);
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
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
