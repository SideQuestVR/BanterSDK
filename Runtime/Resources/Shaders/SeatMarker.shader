Shader "Banter/Seat Marker"
{
    Properties
    {
        [HDR] _SeatColor ("Seat", Color) = (0.08, 1.7, 2.3, 1)
        [HDR] _BackColor ("Backrest", Color) = (0.18, 1.35, 2, 1)
        [HDR] _ArrowColor ("Arrow", Color) = (0.6, 2, 2.8, 1)
        [HideInInspector] _Hover ("Hover press", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "SeatMarker"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SeatColor;
                half4 _BackColor;
                half4 _ArrowColor;
                float _Hover;
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
                half3 color : COLOR0;
                half3 effects : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionOS = input.positionOS.xyz;
                positionOS.y += input.color.r * (0.0175 + 0.0075 * sin(_Time.y * 1.5));
                positionOS.y += input.color.b * lerp(0.10, 0.015, smoothstep(0, 1, _Hover));
                output.positionCS = TransformObjectToHClip(positionOS);
                // Use the authored backrest height before the ring/arrow displacement.
                output.effects = half3(input.uv.x, saturate((input.positionOS.y - 0.13) / 0.75), input.color.g);

                // Authored V extends beyond 0..1; leave its mirrored repeat spacing intact.
                half wave = sin(input.uv.y * 1.4 - _Time.y * 1.7) * 0.5h + 0.5h;
                half brightness = lerp(0.72h + 0.28h * wave * wave * wave,
                    0.94h + 0.06h * sin(_Time.y * 2.2), input.color.b);
                output.color = (_SeatColor.rgb * input.color.r + _BackColor.rgb * input.color.g
                    + _ArrowColor.rgb * input.color.b) * brightness;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half edge = abs(input.effects.x * 2 - 1);
                half stripAlpha = 1 - smoothstep(0.35h, 1.0h, edge);
                half backAlpha = lerp(0.12h, 1.0h, smoothstep(0, 1, input.effects.y));
                return half4(input.color, lerp(stripAlpha, backAlpha, input.effects.z));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
