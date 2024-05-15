// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Unlit/DiffuseTransparent" {
    Properties {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Cull ("Culling", Float) = 0.0
        [Toggle] _FlipBackface("Flip Backface", Float) = 0.0
    }
    SubShader {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        Cull [_Cull]
        
        Pass 
        {
            CGPROGRAM

            #pragma vertex vert alpha
            #pragma fragment frag alpha

            #include "UnityCG.cginc"
            #pragma multi_compile_fog
            #define USING_FOG (defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2))

            struct appdata_t 
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
            };

            struct v2f 
            {
                float4 vertex  : SV_POSITION;
                half2 texcoord : TEXCOORD0;
                fixed fog : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO //Insert
            };

            sampler2D _MainTex;
            float _FlipBackface;
            float4 _MainTex_ST;
            float4 _Color;

            v2f vert (appdata_t v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v); //Insert
                UNITY_INITIALIZE_OUTPUT(v2f, o); //Insert
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert

                o.vertex     = UnityObjectToClipPos(v.vertex);
                // v.texcoord.x = 1 - v.texcoord.x;
                o.texcoord   = TRANSFORM_TEX(v.texcoord, _MainTex);


                float3 eyePos = UnityObjectToViewPos(v.vertex);
                float fogCoord = length(eyePos.xyz);  // radial fog distance
                UNITY_CALC_FOG_FACTOR_RAW(fogCoord);
                o.fog = saturate(unityFogFactor);

                return o;
            }

            fixed4 frag (v2f i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                if (_FlipBackface > 0 && !isFrontFace) {
                    i.texcoord.x = 1.0 - i.texcoord.x;
                }
                fixed4 col = tex2D(_MainTex, i.texcoord) * _Color; // multiply by _Color
                

               // col.rgb = lerp(unity_FogColor.rgb, col.rgb, i.fog);

                return col;
            }

            ENDCG
        }
    }
}