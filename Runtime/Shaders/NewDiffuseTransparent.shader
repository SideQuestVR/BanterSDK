Shader "Unlit/DiffuseTransparent"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Color("Color", Color) = (1, 1, 1, 1)
		_Cull ("Culling", Float) = 0.0
	}

	SubShader
	{
		Tags
		{
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
		}

		Pass
		{
			Blend SrcAlpha OneMinusSrcAlpha
			Cull [_Cull]

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _Color;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float3 viewDir : TEXCOORD2;

				 UNITY_VERTEX_OUTPUT_STEREO //Insert
			};

			v2f vert(appdata v)
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID(v); //Insert
				UNITY_INITIALIZE_OUTPUT(v2f, o); //Insert
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert

				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
				float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
				return o;
			}

			fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
			{
				
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); //Insert
				// Sample texture
				fixed4 texColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);
				// Sample texture
				// fixed4 texColor = tex2D(_MainTex, i.uv);

				// Combine with color
				fixed4 col = texColor * _Color;

				// Flip normal for back faces
				float3 worldNormal = normalize(i.worldNormal) * sign(facing);

				// Calculate lighting from main directional light if it exists
				float3 lightDir = _WorldSpaceLightPos0.xyz;
				float NdotL = max(0, dot(worldNormal, lightDir));

				// Add ambient/environment lighting (works without lights)
				float3 ambient = ShadeSH9(half4(worldNormal, 1.0));

				// Combine directional and ambient light, ensuring minimum brightness
				float3 lighting = max(0.6, max(ambient * 0.5 + 0.5, NdotL * _LightColor0.rgb + ambient));

				// Apply lighting to color
				col.rgb *= lighting;
                //col.a = 1;

				return col;
			}

			ENDCG
		}
	}
	Fallback "Unlit/Diffuse"
}
