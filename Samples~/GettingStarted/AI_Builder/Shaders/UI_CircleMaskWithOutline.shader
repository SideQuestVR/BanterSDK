Shader "Custom/UI_CircleMaskWithOverlayRing"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _RingColor ("Ring Color", Color) = (1,1,1,1)
        [PerRendererData] _RingThickness ("Ring Thickness", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float4 _RingColor;
            float _RingThickness;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv - 0.5; // Center UVs
                float dist = length(uv); // Distance from the center

                // Circle mask - defines the inner circle
                float circleMask = step(0.5, dist); // Everything outside the circle is masked

                // Ring overlay - calculated independently of the circle mask
                float outerRing = step(0.5, dist); // Outer edge of the ring
                float innerRing = step(0.5 - _RingThickness, dist); // Inner edge of the ring
                float ring = innerRing - outerRing; // Isolate the ring

                // Texture sampling
                fixed4 mainTexColor = tex2D(_MainTex, i.uv);

                // Blend between main texture and ring color
                fixed4 ringColor = _RingColor;
                fixed4 finalColor = lerp(mainTexColor, ringColor, ring);

                // Apply final alpha - ensure the mask only hides outside the circle
                finalColor.a *= 1.0 - circleMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
