Shader "Banter/Grip Hologram"
{
    Properties
    {
        [HDR] _Color ("Glow", Color) = (0.08, 1.7, 2.3, 1)
        [HideInInspector] _Opacity ("Opacity", Range(0,1)) = 1
        [HideInInspector] _DepthOnly ("Depth Only", Float) = 0
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
        [HideInInspector] _ZWrite ("Depth Write", Float) = 0
        [HideInInspector] _ZTest ("Depth Test", Float) = 3
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+101" }
        Pass
        {
            Name "GripHologram"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            ColorMask [_ColorMask]
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                half _DepthOnly;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 gradientUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half gradient : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.gradient = input.gradientUV.y;
                return output;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_DepthOnly > .5h) return 0;
                half rim = 1 - abs(dot(normalize(input.normalWS), normalize(GetWorldSpaceViewDir(input.positionWS))));
                half wave = sin(input.gradient * 8 - _Time.y * 1.7) * .5h + .5h;
                half gradient = smoothstep(0, 1, input.gradient);
                half rimSquared = rim * rim;
                half3 glow = _Color.rgb * (.065h + .14h * gradient + .8h * rimSquared * rimSquared)
                    * (.9h + .1h * wave * wave * wave);
                return half4(glow, _Opacity * (.28h + .45h * rimSquared));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
