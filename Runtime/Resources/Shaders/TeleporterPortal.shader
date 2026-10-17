Shader "Banter/Teleporter Portal"
{
    Properties
    {
        _MainTex ("Destination", 2D) = "black" {}
        [HDR] _Color ("Rim", Color) = (0.08, 1.7, 2.3, 1)
        [HideInInspector] _HasImage ("Has destination image", Float) = 0
        [HideInInspector] _StereoImage ("Stereo destination image", Float) = 0
        [HideInInspector] _PerspectiveImage ("Perspective destination image", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "TeleporterPortal"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_TexelSize;
                float _HasImage;
                float _StereoImage;
                float _PerspectiveImage;
                float4x4 _PortalViewProjectionL;
                float4x4 _PortalViewProjectionR;
            CBUFFER_END
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
                nointerpolation float reversePhoto : TEXCOORD1;
                float4 portalPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.reversePhoto = TransformWorldToObject(GetCameraPositionWS()).z > input.positionOS.z ? 1 : 0;
                float4 worldPosition = float4(TransformObjectToWorld(input.positionOS.xyz), 1);
                output.portalPosition = mul(unity_StereoEyeIndex == 0 ? _PortalViewProjectionL : _PortalViewProjectionR, worldPosition);
                return output;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float radius = length(input.uv * 2 - 1);
                half edge = smoothstep(.76, .91, radius);
                half alpha = 1 - smoothstep(.91, 1, radius);
                clip(alpha - .02h);
                half pulse = .9h + .1h * sin(_Time.y * 1.7 + input.uv.y * 5);
                // The closed portal mesh's photo mapping reverses between its two viewing sides.
                float2 imageUV = float2(lerp(1 - input.uv.x, input.uv.x, input.reversePhoto), input.uv.y);
                half hasImage = _HasImage;
                if (_PerspectiveImage > .5)
                {
                    float4 projected = input.portalPosition;
                    #if UNITY_UV_STARTS_AT_TOP
                        projected.y = -projected.y;
                    #endif
                    imageUV = projected.xy / max(projected.w, .0001) * .5 + .5;
                    hasImage *= projected.w > .0001 && all(imageUV >= 0) && all(imageUV <= 1) ? 1 : 0;
                    imageUV = saturate(imageUV);
                }
                if (_StereoImage > .5)
                {
                    float eyeOffset = unity_StereoEyeIndex * .5;
                    float halfTexel = _MainTex_TexelSize.x * .5;
                    imageUV.x = clamp(imageUV.x * .5 + eyeOffset, eyeOffset + halfTexel, eyeOffset + .5 - halfTexel);
                }
                half3 image = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, imageUV).rgb;
                half3 fallback = _Color.rgb * lerp(.025h, .12h, input.uv.y);
                return half4(lerp(lerp(fallback, image, hasImage), _Color.rgb * pulse, edge), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
