Shader "Unlit/Transparent"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Color("Color", Color) = (1, 1, 1, 1)
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
			Cull Off
			ZWrite Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _Color;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// Sample texture (loaded as linear to preserve alpha channel on ASTC)
				fixed4 texColor = tex2D(_MainTex, i.uv);

				// Manually convert RGB from sRGB to linear (alpha stays linear to prevent corruption)
				texColor.rgb = pow(texColor.rgb, 2.2);

				// Check if vertex colors are nearly black (default/missing)
				// Unity provides black (0,0,0,1) for meshes without vertex colors
				fixed3 vertexColor = i.color.rgb;
				if (dot(vertexColor, vertexColor) < 0.01) {
					// No vertex colors or black - treat as white (no effect on texture)
					vertexColor = fixed3(1, 1, 1);
				}

				// Convert vertex color from sRGB to linear space
				// (matches Three.js behavior - GLTF vertex colors are in sRGB)
				fixed3 vertexColorLinear = pow(vertexColor, 2.2);

				// Multiply in linear space: texture × material color × vertex color
				fixed4 col = texColor * _Color * fixed4(vertexColorLinear, i.color.a);

				return col;
			}

			ENDCG
		}
	}
	Fallback "Unlit/Transparent"
}
