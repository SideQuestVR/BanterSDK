Shader "VRMirror/VRPortalShaderAlpha"
{
    Properties
    {
        _Tint("Tint Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 posL : TEXCOORD1;
                float4 posR : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Tint;

            sampler2D EyeTexL;
            sampler2D EyeTexR;
            float4x4 EyeViewMatrixL;
            float4x4 EyeViewMatrixR;
            float4x4 EyeProjMatrixL;
            float4x4 EyeProjMatrixR;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.posL = mul(EyeProjMatrixL, mul(EyeViewMatrixL, mul(unity_ObjectToWorld, v.vertex)));
                o.posR = mul(EyeProjMatrixR, mul(EyeViewMatrixR, mul(unity_ObjectToWorld, v.vertex)));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float eyeBlend = clamp(unity_StereoEyeIndex, 0.0, 1.0);
                float4 viewPosL = i.posL / i.posL.w;
                float4 viewPosR = i.posR / i.posR.w;

                fixed4 colL = tex2D(EyeTexL, viewPosL.xy * 0.5 + 0.5);
                fixed4 colR = tex2D(EyeTexR, viewPosR.xy * 0.5 + 0.5);
                fixed4 col = lerp(colL, colR, eyeBlend);
                col.xyz *= _Tint.xyz;

                // Use alpha blending
                return col;
            }
            ENDCG
        }
    }
}
