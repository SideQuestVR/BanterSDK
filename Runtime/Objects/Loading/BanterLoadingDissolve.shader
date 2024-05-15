// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Banter/LoadingDissolve"
{
	Properties
	{
		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_Albedo("Albedo", 2D) = "white" {}
		_DisolveGuide("Disolve Guide", 2D) = "white" {}
		_DisolveGuide2("Disolve Guide2", 2D) = "white" {}
		_BurnRamp("Burn Ramp", 2D) = "white" {}
		_DissolveAmount("Dissolve Amount", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "TransparentCutout"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		ZTest Always
		CGPROGRAM
		#pragma target 3.0
		#pragma only_renderers d3d11 glcore gles gles3 
		#pragma surface surf Unlit keepalpha addshadow fullforwardshadows 
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform sampler2D _Albedo;
		uniform half4 _Albedo_ST;
		uniform float _DissolveAmount;
		uniform sampler2D _DisolveGuide;
		uniform half4 _DisolveGuide_ST;
		uniform sampler2D _DisolveGuide2;
		uniform half4 _DisolveGuide2_ST;
		uniform sampler2D _BurnRamp;
		uniform float _Cutoff = 0.5;

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float2 uv_Albedo = i.uv_texcoord * _Albedo_ST.xy + _Albedo_ST.zw;
			float2 uv_DisolveGuide = i.uv_texcoord * _DisolveGuide_ST.xy + _DisolveGuide_ST.zw;
			float2 uv_DisolveGuide2 = i.uv_texcoord * _DisolveGuide2_ST.xy + _DisolveGuide2_ST.zw;
			half temp_output_73_0 = ( (-0.6 + (_DissolveAmount - 0.0) * (0.6 - -0.6) / (1.0 - 0.0)) + ( tex2D( _DisolveGuide, uv_DisolveGuide ).r * ( 1.0 - tex2D( _DisolveGuide2, uv_DisolveGuide2 ).r ) ) );
			half clampResult113 = clamp( (-4.0 + (temp_output_73_0 - 0.0) * (4.0 - -4.0) / (1.0 - 0.0)) , 0.0 , 1.0 );
			half temp_output_130_0 = ( 1.0 - clampResult113 );
			half2 appendResult115 = (half2(temp_output_130_0 , 0.0));
			o.Emission = ( tex2D( _Albedo, uv_Albedo ) + ( temp_output_130_0 * tex2D( _BurnRamp, appendResult115 ) ) ).rgb;
			o.Alpha = 1;
			clip( temp_output_73_0 - _Cutoff );
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.CommentaryNode;128;-991.3727,510.0833;Inherit;False;932.2314;729.3652;Dissolve - Opacity Mask;7;4;143;141;140;2;111;73;;1,1,1,1;0;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;138;435.9929,109.222;Half;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Banter/Dissolve;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;0.5;True;True;0;False;TransparentCutout;;Geometry;All;4;d3d11;glcore;gles;gles3;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;126;-162.5617,92.4654;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;73;-319.6845,566.4299;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;142;249.5412,19.85584;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.TFHCRemapNode;111;-647.2195,575.2112;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-0.6;False;4;FLOAT;0.6;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;114;-440.7648,199.8753;Inherit;True;Property;_BurnRamp;Burn Ramp;4;0;Create;True;0;0;0;False;0;False;-1;None;e2216e52e64b498b961c693f564d4d0c;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;1,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;115;-575.7913,222.4798;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;2;-829.2513,768.0635;Inherit;True;Property;_DisolveGuide;Disolve Guide;2;0;Create;True;0;0;0;False;0;False;-1;None;4bd6618be485426998392fc8a5e9bc18;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;141;-386.8438,773.1233;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;143;-537.3849,1089.601;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;78;-409.0742,-206.0451;Inherit;True;Property;_Albedo;Albedo;1;0;Create;True;0;0;0;False;0;False;-1;None;85e3723e62d44f758723754190c67911;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;4;-940.3392,583.5427;Float;False;Property;_DissolveAmount;Dissolve Amount;5;0;Create;True;0;0;0;False;0;False;0;0.229;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;140;-874.0182,990.6979;Inherit;True;Property;_DisolveGuide2;Disolve Guide2;3;0;Create;True;0;0;0;False;0;False;-1;None;4bd6618be485426998392fc8a5e9bc18;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TFHCRemapNode;112;-951.1525,262.8961;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-4;False;4;FLOAT;4;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;113;-708.634,84.31517;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;130;-580.6614,82.36071;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
WireConnection;138;2;142;0
WireConnection;138;10;73;0
WireConnection;126;0;130;0
WireConnection;126;1;114;0
WireConnection;73;0;111;0
WireConnection;73;1;141;0
WireConnection;142;0;78;0
WireConnection;142;1;126;0
WireConnection;111;0;4;0
WireConnection;114;1;115;0
WireConnection;115;0;130;0
WireConnection;141;0;2;1
WireConnection;141;1;143;0
WireConnection;143;0;140;1
WireConnection;112;0;73;0
WireConnection;113;0;112;0
WireConnection;130;0;113;0
ASEEND*/
//CHKSM=84BFC2D446D88246387827B137484820B75E066F