Shader "Mobile/StylizedFakeLit"
{
    Properties
    {
        // Color Tint (per-material)
        [Header(Base)]
        _Color ("Color Tint", Color) = (1,1,1,1)

        // Hemisphere Lighting
        [Header(Hemisphere Lighting)]
        _SkyColor ("Sky Color", Color) = (0.8, 0.9, 1.0, 1)
        _GroundColor ("Ground Color", Color) = (0.2, 0.15, 0.1, 1)
        _HemisphereStrength ("Hemisphere Strength", Range(0,1)) = 0.7

        // Height AO (gradient-based ambient occlusion)
        [Header(Ambient Occlusion)]
        _AOStrength ("AO Strength", Range(0,1)) = 0.6
        [Toggle(_USE_OBJECT_BOUNDS)] _UseObjectBounds ("Use Object Bounds", Float) = 0
        _AOGroundLevel ("Ground Level (World Y)", Float) = 0
        _AOHeight ("AO Height (World)", Float) = 2
        _ObjectMinY ("Object Min Y (Local)", Float) = 0
        _ObjectMaxY ("Object Max Y (Local)", Float) = 1
        _AOPower ("AO Softness", Range(0.5,3)) = 1.5

        // Rim Lighting
        [Header(Rim Lighting)]
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.8, 1)
        _RimPower ("Rim Sharpness", Range(1,8)) = 3
        _RimStrength ("Rim Strength", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_OBJECT_BOUNDS

            // Mobile targets
            #pragma target 3.0

            #include "UnityCG.cginc"

            // Properties
            half4 _Color;

            half4 _SkyColor;
            half4 _GroundColor;
            half _HemisphereStrength;

            half _AOStrength;
            half _AOGroundLevel;
            half _AOHeight;
            half _ObjectMinY;
            half _ObjectMaxY;
            half _AOPower;

            half4 _RimColor;
            half _RimPower;
            half _RimStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;  // Vertex color for baked AO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 worldNormal : TEXCOORD0;
                half3 viewDir : TEXCOORD1;
                half heightAO : TEXCOORD2;
                half vertexAO : TEXCOORD3;  // Baked vertex color AO
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);

                // World position for AO and view direction
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // World normal for hemisphere lighting
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                // View direction for fresnel (computed in vertex for mobile)
                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // Height AO calculation
                #if defined(_USE_OBJECT_BOUNDS)
                    // Object-space: use local vertex Y position with object min/max bounds
                    half objectHeight = _ObjectMaxY - _ObjectMinY;
                    half normalizedHeight = (v.vertex.y - _ObjectMinY) / max(objectHeight, 0.001);
                #else
                    // World-space: always darkens at bottom regardless of object rotation
                    half normalizedHeight = (worldPos.y - _AOGroundLevel) / _AOHeight;
                #endif
                o.heightAO = saturate(normalizedHeight);
                o.heightAO = pow(o.heightAO, _AOPower);

                // Pass baked vertex color AO (use red channel as grayscale)
                o.vertexAO = v.color.r;

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // Base color
                half3 baseColor = _Color.rgb;

                // === HEMISPHERE LIGHTING ===
                // Remap normal.y from [-1,1] to [0,1]
                half hemisphere = i.worldNormal.y * 0.5 + 0.5;
                half3 hemisphereLight = lerp(_GroundColor.rgb, _SkyColor.rgb, hemisphere);

                // Blend hemisphere with white based on strength
                half3 lighting = lerp(half3(1,1,1), hemisphereLight, _HemisphereStrength);

                // === HEIGHT-BASED AO ===
                half ao = lerp(1.0, i.heightAO, _AOStrength);

                // === BAKED VERTEX COLOR AO ===
                // Multiply with baked AO (1.0 = no occlusion, 0.0 = full occlusion)
                ao *= i.vertexAO;

                // === RIM/FRESNEL LIGHTING ===
                half NdotV = saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                half fresnel = pow(1.0 - NdotV, _RimPower);
                half3 rim = _RimColor.rgb * fresnel * _RimStrength;

                // === FINAL COMPOSITE ===
                half3 finalColor = (baseColor * lighting * ao) + rim;

                // Apply fog
                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                return half4(finalColor, 1.0);
            }
            ENDCG
        }

        // Shadow caster pass for casting shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata_shadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shadow
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_shadow vertShadow(appdata_shadow v)
            {
                v2f_shadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            half4 fragShadow(v2f_shadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    Fallback "Mobile/Diffuse"
}
