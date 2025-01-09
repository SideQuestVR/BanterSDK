// Made with Amplify Shader Editor v1.9.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "BanterSkybox"
{
	Properties
	{
		_Pano("Pano", 2D) = "black" {}
		_HexTile("HexTile", 2D) = "white" {}
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#define ASE_VERSION 19800
		#pragma surface surf Unlit keepalpha addshadow fullforwardshadows vertex:vertexDataFunc 
		struct Input
		{
			float3 worldPos;
			float2 uv2_texcoord2;
			float eyeDepth;
		};

		uniform sampler2D _Pano;
		uniform sampler2D _HexTile;
		uniform float4 _HexTile_ST;

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
			float3 ase_positionWS = i.worldPos;
			float3 ase_viewVectorWS = ( _WorldSpaceCameraPos.xyz - ase_positionWS );
			float3 ase_viewDirWS = normalize( ase_viewVectorWS );
			float2 uv1_HexTile = i.uv2_texcoord2 * _HexTile_ST.xy + _HexTile_ST.zw;
			float4 tex2DNode28 = tex2D( _HexTile, uv1_HexTile );
			float cameraDepthFade31 = (( i.eyeDepth -_ProjectionParams.y - 5.0 ) / 10.0);
			float temp_output_32_0 = saturate( ( 1.0 - cameraDepthFade31 ) );
			float2 appendResult13 = (float2(( ( ( UNITY_PI + atan2( ase_viewDirWS.z , ase_viewDirWS.x ) ) / ( UNITY_PI * 2.0 ) ) + ( tex2DNode28.r * temp_output_32_0 ) ) , ( acos( ( ase_viewDirWS.y / length( ase_viewDirWS ) ) ) / UNITY_PI )));
			o.Emission = tex2D( _Pano, appendResult13 ).rgb;
			o.Alpha = 1;
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19800
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;19;-1584,-464;Inherit;False;World;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.CameraDepthFade;31;-944,256;Inherit;False;3;2;FLOAT3;0,0,0;False;0;FLOAT;10;False;1;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.LengthOpNode;14;-1296,-416;Inherit;False;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ATan2OpNode;17;-1136,-384;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;21;-1248,-256;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;33;-652.0493,183.0372;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;15;-1248,-512;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;20;-976,-128;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;18;-960,-352;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;3.141593;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;32;-587.3175,78.53973;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;28;-992,0;Inherit;True;Property;_HexTile;HexTile;1;0;Create;True;0;0;0;False;0;False;-1;None;1c5a8e93efcf5a14a9f2862dd7a01db0;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ACosOpNode;16;-1104,-464;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;23;-704,-352;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;34;-396.5193,-122.9112;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;22;-720,-464;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;24;-448,-368;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.25;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;13;-240,-432;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;30;-592,-192;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.7;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;25;-48,-448;Inherit;True;Property;_Pano;Pano;0;0;Create;True;0;0;0;False;0;False;-1;None;6cb63a850aeec8f45879321ae652a55c;True;0;False;black;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;288,-496;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;BanterSkybox;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Opaque;0.5;True;True;0;False;Opaque;;Geometry;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;14;0;19;0
WireConnection;17;0;19;3
WireConnection;17;1;19;1
WireConnection;33;0;31;0
WireConnection;15;0;19;2
WireConnection;15;1;14;0
WireConnection;20;0;21;0
WireConnection;18;0;21;0
WireConnection;18;1;17;0
WireConnection;32;0;33;0
WireConnection;16;0;15;0
WireConnection;23;0;18;0
WireConnection;23;1;20;0
WireConnection;34;0;28;1
WireConnection;34;1;32;0
WireConnection;22;0;16;0
WireConnection;22;1;21;0
WireConnection;24;0;23;0
WireConnection;24;1;34;0
WireConnection;13;0;24;0
WireConnection;13;1;22;0
WireConnection;30;0;28;1
WireConnection;30;2;32;0
WireConnection;25;1;13;0
WireConnection;0;2;25;0
ASEEND*/
//CHKSM=3C0FFBF50477348810E959CE1C88F00E6D6D1870