// Made with Amplify Shader Editor v1.9.2.2
// Ported to URP (was surface shader Unlit/TransparentCutout with parallax + panoramic UVs)
Shader "Banter/LoadingCageNew"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Cutoff( "Mask Clip Value", Float ) = 0.5
        _CageHight("CageHight", Range( 0 , 1)) = 0.3
        _Pano("Pano", 2D) = "black" {}
        _DisolveGuide("DisolveGuide", 2D) = "white" {}
        _Thumb("Thumb", 2D) = "white" {}
        _ThumbMask("ThumbMask", 2D) = "white" {}
        [Header(Change On Runtime)]_DissolveLoadAmount("Dissolve Load Amount", Range( 0 , 1)) = 0
        _DissolveAmount("Dissolve Amount", Range( 0 , 1)) = 0
        _Hue("Hue", Range( 0 , 1)) = 1
        _Brighness("Brighness", Range( 0 , 1)) = 0.5
        _BurnColor("BurnColor", Color) = (0,0,0,0)
        [HideInInspector] _texcoord2( "", 2D ) = "white" {}
        [HideInInspector] _texcoord3( "", 2D ) = "white" {}
        [HideInInspector] _texcoord( "", 2D ) = "white" {}
        [HideInInspector] __dirty( "", Int ) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Overlay+0" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZTest Always

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma only_renderers d3d11 glcore gles3 vulkan
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Pano);         SAMPLER(sampler_Pano);
            TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
            TEXTURE2D(_DisolveGuide); SAMPLER(sampler_DisolveGuide);
            TEXTURE2D(_Thumb);        SAMPLER(sampler_Thumb);
            TEXTURE2D(_ThumbMask);    SAMPLER(sampler_ThumbMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Cutoff;
                float  _CageHight;
                float  _DissolveLoadAmount;
                float  _DissolveAmount;
                float  _Hue;
                float  _Brighness;
                float4 _BurnColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float2 uv0            : TEXCOORD0;
                float2 uv1            : TEXCOORD1;
                float2 uv2            : TEXCOORD2;
                float3 worldPos       : TEXCOORD3;
                float3 tangentViewDir : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 HSVToRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float3 RGBToHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs  = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.worldPos   = posInputs.positionWS;
                output.uv0        = input.uv0;
                output.uv1        = input.uv1;
                output.uv2        = input.uv2;

                // Tangent-space view direction for parallax (equivalent to Surface Shader i.viewDir)
                float3 worldViewDir = normalize(_WorldSpaceCameraPos.xyz - posInputs.positionWS);
                output.tangentViewDir = float3(
                    dot(worldViewDir, normInputs.tangentWS),
                    dot(worldViewDir, normInputs.bitangentWS),
                    dot(worldViewDir, normInputs.normalWS)
                );

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // World-space view direction (equivalent to normalize(UnityWorldSpaceViewDir(worldPos)))
                float3 worldViewDir = normalize(GetCameraPositionWS() - input.worldPos);

                // Panoramic (equirectangular) UV from world view direction
                float2 panoUV = float2(
                    ((PI + atan2(worldViewDir.z, worldViewDir.x)) / (PI * 2.0)) + 0.25,
                    acos(worldViewDir.y) / PI
                );
                float3 panoHSV = RGBToHSV(SAMPLE_TEXTURE2D(_Pano, sampler_Pano, panoUV).rgb);
                float3 panoRGB = HSVToRGB(float3(panoHSV.x * _Hue, panoHSV.y, panoHSV.z * _Brighness));

                // Main cage texture (UV1 with tiling/offset)
                float2 uv_MainTex  = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
                float4 mainTex     = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv_MainTex);
                float  cageBlend   = smoothstep(1.0 - _CageHight, 1.0, 1.0 - input.uv1.y);
                float4 lerpResult9 = lerp(float4(panoRGB, 0.0), mainTex, mainTex.a * cageBlend);

                // Thumbnail (UV3) with parallax offset from tangent-space view direction
                float2 uv3          = input.uv2 * float2(1.0, 1.2) + float2(0.0, -0.125);
                float3 parallaxView = float3(-input.tangentViewDir.y, -input.tangentViewDir.x, input.tangentViewDir.z);
                float2 thumbUV      = (2.0 * parallaxView.xy * 0.1) + uv3;

                // Thumb dissolve — TFHCRemap(_DissolveLoadAmount, -0.5, 2.4, -1.0, 2.0)
                float thumbDissolve = -1.0 + (_DissolveLoadAmount + 0.5) * (3.0 / 2.9)
                                    + (1.0 - uv3.x) * (1.0 - SAMPLE_TEXTURE2D(_DisolveGuide, sampler_DisolveGuide, uv3).r);
                float thumbClamp    = clamp(-20.0 + thumbDissolve * 40.0, 0.0, 1.0);
                float thumbStep     = smoothstep(0.45, 0.5, thumbDissolve);

                float4 thumbSample  = SAMPLE_TEXTURE2D(_Thumb, sampler_Thumb, thumbUV);
                float  thumbMask    = SAMPLE_TEXTURE2D(_ThumbMask, sampler_ThumbMask, uv3).r;
                float4 lerpResult43 = lerp(lerpResult9,
                                           thumbSample + (1.0 - thumbClamp) * _BurnColor,
                                           saturate(thumbMask * thumbStep));

                // Main dissolve — TFHCRemap(1 - _DissolveAmount, 0, 1, -0.6, 0.6)
                float dissolveGuide = SAMPLE_TEXTURE2D(_DisolveGuide, sampler_DisolveGuide, input.uv1).r;
                float dissolveMapped = -0.6 + (1.0 - _DissolveAmount) * 1.2
                                     + (1.0 - dissolveGuide) * input.uv1.x;
                float burnEdge       = 1.0 - saturate(-20.0 + dissolveMapped * 40.0);

                float3 emission = (lerpResult43 + burnEdge * _BurnColor).rgb;

                clip(dissolveMapped - _Cutoff);
                return half4(emission, 1.0);
            }
            ENDHLSL
        }
    }
    CustomEditor "ASEMaterialInspector"
}
