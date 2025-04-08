// Made with Amplify Shader Editor v1.9.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Sky"
{
	Properties
	{
		_Sky("Sky", 2D) = "white" {}
		_Ground("Ground", 2D) = "white" {}
		_GroundMask("GroundMask", 2D) = "white" {}
		_GroundTiling("GroundTiling", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] _texcoord4( "", 2D ) = "white" {}
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.5
		#define ASE_VERSION 19800
		#pragma surface surf Unlit keepalpha addshadow fullforwardshadows vertex:vertexDataFunc 
		struct Input
		{
			float2 uv_texcoord;
			float2 uv4_texcoord4;
			float2 uv2_texcoord2;
			float eyeDepth;
		};

		uniform sampler2D _Sky;
		uniform sampler2D _Ground;
		uniform float _GroundTiling;
		uniform sampler2D _GroundMask;


		float2 voronoihash20( float2 p )
		{
			
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi20( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
		{
			float2 n = floor( v );
			float2 f = frac( v );
			float F1 = 8.0;
			float F2 = 8.0; float2 mg = 0;
			for ( int j = -1; j <= 1; j++ )
			{
				for ( int i = -1; i <= 1; i++ )
			 	{
			 		float2 g = float2( i, j );
			 		float2 o = voronoihash20( n + g );
					o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
					float d = 0.5 * dot( r, r );
			 		if( d<F1 ) {
			 			F2 = F1;
			 			F1 = d; mg = g; mr = r; id = o;
			 		} else if( d<F2 ) {
			 			F2 = d;
			
			 		}
			 	}
			}
			
F1 = 8.0;
for ( int j = -2; j <= 2; j++ )
{
for ( int i = -2; i <= 2; i++ )
{
float2 g = mg + float2( i, j );
float2 o = voronoihash20( n + g );
		o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
float d = dot( 0.5 * ( r + mr ), normalize( r - mr ) );
F1 = min( F1, d );
}
}
return F1;
		}


		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			o.eyeDepth = -UnityObjectToViewPos( v.vertex.xyz ).z;
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float smoothstepResult11 = smoothstep( 0.975 , 1.0 , i.uv_texcoord.x);
			float smoothstepResult12 = smoothstep( 0.0 , 0.025 , i.uv_texcoord.x);
			float4 lerpResult10 = lerp( tex2D( _Sky, i.uv_texcoord ) , tex2D( _Sky, i.uv4_texcoord4 ) , ( smoothstepResult11 + ( 1.0 - smoothstepResult12 ) ));
			float2 temp_cast_0 = (_GroundTiling).xx;
			float2 uv2_TexCoord5 = i.uv2_texcoord2 * temp_cast_0;
			float time20 = 0.0;
			float2 voronoiSmoothId20 = 0;
			float2 coords20 = i.uv2_texcoord2 * 150.0;
			float2 id20 = 0;
			float2 uv20 = 0;
			float voroi20 = voronoi20( coords20, time20, id20, uv20, 0, voronoiSmoothId20 );
			float cameraDepthFade22 = (( i.eyeDepth -_ProjectionParams.y - 10.0 ) / 10.0);
			float4 lerpResult23 = lerp( tex2D( _Ground, uv2_TexCoord5 ) , tex2D( _Ground, uv20 ) , saturate( cameraDepthFade22 ));
			float4 lerpResult4 = lerp( lerpResult10 , lerpResult23 , tex2D( _GroundMask, i.uv2_texcoord2 ).r);
			o.Emission = lerpResult4.rgb;
			o.Alpha = 1;
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19800
Node;AmplifyShaderEditor.TextureCoordinatesNode;13;-1328,-288;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;17;-1296,48;Inherit;False;Property;_GroundTiling;GroundTiling;3;0;Create;True;0;0;0;False;0;False;0;333;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;12;-976,-112;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.025;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;21;-1424,160;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode;14;-752,-96;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;11;-976,-240;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.975;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;8;-1152,-640;Inherit;False;3;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CameraDepthFade;22;-1248,352;Inherit;False;3;2;FLOAT3;0,0,0;False;0;FLOAT;10;False;1;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;5;-1056,16;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VoronoiNode;20;-1120,176;Inherit;False;0;0;1;4;1;False;1;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;150;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.SamplerNode;2;-656,-16;Inherit;True;Property;_Ground;Ground;1;0;Create;True;0;0;0;False;0;False;-1;None;f56a3b5a00c24004789f1f793f2639b1;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode;24;-656,192;Inherit;True;Property;_Ground1;Ground;1;0;Create;True;0;0;0;False;0;False;-1;None;bc704515fd201c44aacb49475e7d360d;True;0;False;white;Auto;False;Instance;2;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleAddOpNode;16;-560,-144;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;9;-832,-656;Inherit;True;Property;_Sky1;Sky;0;0;Create;True;0;0;0;False;0;False;-1;None;c22e159d4ba069740b3361cf69f5ec7b;True;0;False;white;Auto;False;Instance;1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode;1;-816,-448;Inherit;True;Property;_Sky;Sky;0;0;Create;True;0;0;0;False;0;False;-1;None;c22e159d4ba069740b3361cf69f5ec7b;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TextureCoordinatesNode;19;-928,448;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;25;-832,304;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;23;-336,64;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;10;-352,-384;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;3;-512,544;Inherit;True;Property;_GroundMask;GroundMask;2;0;Create;True;0;0;0;False;0;False;-1;None;a230b9b4c8306c64fb53b9f754f328a1;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp;4;64,-80;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;80,-624;Float;False;True;-1;3;ASEMaterialInspector;0;0;Unlit;Sky;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Opaque;0.5;True;True;0;False;Opaque;;Geometry;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;12;0;13;1
WireConnection;14;0;12;0
WireConnection;11;0;13;1
WireConnection;5;0;17;0
WireConnection;20;0;21;0
WireConnection;2;1;5;0
WireConnection;24;1;20;2
WireConnection;16;0;11;0
WireConnection;16;1;14;0
WireConnection;9;1;8;0
WireConnection;1;1;13;0
WireConnection;25;0;22;0
WireConnection;23;0;2;0
WireConnection;23;1;24;0
WireConnection;23;2;25;0
WireConnection;10;0;1;0
WireConnection;10;1;9;0
WireConnection;10;2;16;0
WireConnection;3;1;19;0
WireConnection;4;0;10;0
WireConnection;4;1;23;0
WireConnection;4;2;3;1
WireConnection;0;2;4;0
ASEEND*/
//CHKSM=6426B7629F936D3564EC8AB8FE64B1EF4495C178