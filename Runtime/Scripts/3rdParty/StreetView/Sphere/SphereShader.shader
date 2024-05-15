//Shader "Unlit/PhotoSphereShader" {
//    Properties{
//        _MainTex("Texture", 2DArray) = "white" {}
//        [Enum(Off,0,On,1)] _ZWrite("ZWrite", Float) = 1.0
//        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4 //"LessEqual"
//        [Enum(UnityEngine.Rendering.CullMode)] _Culling("Culling", Float) = 0
//        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("BlendSource", Float) = 1
//        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("BlendDestination", Float) = 0
//        _Cols("Cols", Int) = 1
//        _Rows("Rows", Int) = 1
//        _Opacity("Opacity", Range(0.0, 1.0)) = 1.0
//    }
//    SubShader{
//               Tags {"Queue" = "Transparent" "RenderType" = "Transparent"} 
//             LOD 200
//              ZWrite[_ZWrite]
//              ZTest[_ZTest]
//              Cull[_Culling]
//            Blend [_SrcBlend] [_DstBlend]
//           //  ZWrite[_ZWrite]
//             // extra pass that renders to depth buffer only
//            // Alpha test depth only pass
//           // Pass {
//                // default ZTest, here to make sure it's not overriden by the later one
//              //  ZTest LEqual
//                // ZWrite On
//                // only render to depth buffer
//                 // ColorMask RGBA
//            //}
//            // ZTest Equal
//
//                    // shouldn't affect the result, but can be a minor perf win
//            CGPROGRAM
//            #pragma surface surf Lambert alpha:fade
//
//            // Use shader model 3.0 target, to get nicer looking lighting
//            #pragma target 3.0
//            struct Input {
//                float2 uv_MainTex;
//            //float4 screenPos;
//            };
//
//            UNITY_DECLARE_TEX2DARRAY(_MainTex);
//            half _Cols;
//            half _Rows;
//            float _Opacity;
//
//            void surf(Input IN, inout SurfaceOutput o) {
//
//                float uvx = IN.uv_MainTex.x * _Cols;
//                float uvy = IN.uv_MainTex.y * _Rows;
//                float uvz = floor(uvy) * _Cols + floor(uvx);
//                uvx = uvx - floor(uvx);
//                uvy = uvy - floor(uvy);
//                fixed4 c = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(uvx, uvy, uvz));
//                o.Albedo = c.rgb;
//                o.Alpha = _Opacity;
//                //// Screen-door transparency: Discard pixel if below threshold.
//                //float4x4 thresholdMatrix =
//                //{ 1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
//                //  13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
//                //   4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
//                //  16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
//                //};
//                //float4x4 _RowAccess = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
//                //float2 pos = IN.screenPos.xy / IN.screenPos.w;
//                //pos *= _ScreenParams.xy; // pixel position
//                //clip(_Opacity - thresholdMatrix[fmod(pos.x, 4)] * _RowAccess[fmod(pos.y, 4)]);
//            }
//            ENDCG
//    }
//    Fallback "Diffuse"
//}
//
Shader "Unlit/PhotoSphereShader"
{
    Properties
    {
        [Enum(Off, 0, On, 1)] _ZWrite("ZWrite", Float) = 1.0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4 //"LessEqual"
        [Enum(UnityEngine.Rendering.CullMode)] _Culling("Culling", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("BlendSource", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("BlendDestination", Float) = 0
        _MainTex("Texture", 2DArray) = "white" {}
        _Cols("Cols", Int) = 1
        _Rows("Rows", Int) = 1
        _Opacity("Opacity", Range(0.0, 1.0)) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        LOD 100
        ZWrite[_ZWrite]
        ZTest[_ZTest]
        Cull[_Culling]
        //Pass { ColorMask 0 }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            Fog { Mode Off }
            Lighting Off
            //ColorMask RGBA
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO //Insert
            };
            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            // sampler2D _MainTex;
            half _Cols;
            half _Rows;
            float _Opacity;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v); //Insert
                UNITY_INITIALIZE_OUTPUT(v2f, o); //Insert
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
               // UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            { 
                float uvx = i.uv.x * _Cols;
                float uvy = i.uv.y * _Rows;
                float uvz = floor(uvy) * _Cols + floor(uvx);
                uvx = uvx - floor(uvx);
                uvy = uvy - floor(uvy);


                //half4 col = UNITY_SAMPLE_TEX2DARRAY_GRAD(_MainTex, float3(frac(uvx), frac(uvy), uvz), ddx(uvx), ddy(uvy));

                fixed4 col = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(uvx, uvy, uvz));
                col.a = _Opacity;
                return col;
            }
            ENDCG
        }
    }
}
