// Made with Amplify Shader Editor v1.9.4
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Banter/BanterTeleportIndikator"
{
	Properties
	{
		_MainTex("MainTex", 2D) = "white" {}
		_Color("Color", Color) = (0.6591351,0.1367925,1,0)
		_Speed("Speed", Float) = 1
		_EmittPower("EmittPower", Range( 0 , 1)) = 0.42
		_AltMode("AltMode", Range( 0 , 1)) = 0
		_Speed2("Speed2", Float) = 0.5
		_Offset("Offset", Float) = 0
		_XTiling("XTiling", Float) = 1
		_YTiling("YTiling", Float) = 1
		_BandOnly("BandOnly", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#pragma exclude_renderers xboxone xboxseries playstation ps4 ps5 switch 
		#pragma surface surf Unlit alpha:fade keepalpha noshadow noambient novertexlights nolightmap  nodynlightmap nodirlightmap 
		struct Input
		{
			float2 uv2_texcoord2;
			float4 vertexColor : COLOR;
		};

		uniform float4 _Color;
		uniform sampler2D _MainTex;
		uniform float _Speed;
		uniform float _XTiling;
		uniform float _YTiling;
		uniform float _Offset;
		uniform float _Speed2;
		uniform float _AltMode;
		uniform float _BandOnly;
		uniform float _EmittPower;

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float mulTime195 = _Time.y * _Speed;
			float2 appendResult308 = (float2(_XTiling , _YTiling));
			float2 appendResult306 = (float2(0.0 , _Offset));
			float2 uv2_TexCoord2 = i.uv2_texcoord2 * appendResult308 + appendResult306;
			float2 panner119 = ( mulTime195 * float2( 1,0 ) + uv2_TexCoord2);
			float4 tex2DNode114 = tex2D( _MainTex, panner119 );
			float mulTime271 = _Time.y * _Speed;
			float2 panner270 = ( ( mulTime271 * _Speed2 ) * float2( 1,0 ) + uv2_TexCoord2);
			float2 appendResult298 = (float2(uv2_TexCoord2.x , ( ( 1.0 - uv2_TexCoord2.y ) + -0.5 )));
			float2 panner275 = ( ( mulTime271 * ( _Speed2 + 0.5 ) ) * float2( 1,0 ) + appendResult298);
			float temp_output_278_0 = ( tex2D( _MainTex, panner270 ).g + tex2D( _MainTex, panner275 ).g );
			float temp_output_273_0 = ( tex2DNode114.r + temp_output_278_0 );
			float lerpResult285 = lerp( temp_output_273_0 , ( tex2DNode114.r * temp_output_278_0 ) , _AltMode);
			float lerpResult310 = lerp( lerpResult285 , tex2DNode114.r , _BandOnly);
			o.Emission = ( saturate( ( _Color + ( lerpResult310 * ( _Color + _EmittPower ) ) ) ) * i.vertexColor ).rgb;
			float smoothstepResult279 = smoothstep( 0.05 , 1.0 , temp_output_273_0);
			float lerpResult312 = lerp( smoothstepResult279 , tex2DNode114.r , _BandOnly);
			o.Alpha = lerpResult312;
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19400
Node;AmplifyShaderEditor.RangedFloatNode;307;-1212.825,2873.126;Inherit;False;Property;_Offset;Offset;6;0;Create;True;0;0;0;False;0;False;0;0.13;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;309;-1516.368,2653.592;Inherit;False;Property;_XTiling;XTiling;7;0;Create;True;0;0;0;False;0;False;1;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;314;-1486.713,2754.431;Inherit;False;Property;_YTiling;YTiling;8;0;Create;True;0;0;0;False;0;False;1;0.25;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;306;-1046.825,2834.126;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;308;-1210.368,2702.592;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;1;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;2;-810.753,2647.979;Inherit;True;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;2,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;256;-709.4011,3014.9;Inherit;False;Property;_Speed;Speed;2;0;Create;True;0;0;0;False;0;False;1;-1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;301;-929.47,3266.006;Inherit;False;Property;_Speed2;Speed2;5;0;Create;True;0;0;0;False;0;False;0.5;0.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;296;-527.5258,3285.343;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;271;-678.4026,3121.095;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;299;-561.47,3484.006;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;-0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;302;-805.47,3392.006;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;298;-438.1115,3485.172;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;272;-469.4025,3126.095;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;276;-662.51,3363.946;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;195;-548.913,3020.91;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;270;-289.4024,3108.095;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;275;-298.5098,3302.946;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;119;-328.7599,2903.359;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;269;-110.4024,3087.095;Inherit;True;Property;_MainTex1;MainTex;0;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;114;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;274;-101.5099,3276.946;Inherit;True;Property;_MainTex2;MainTex;0;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;114;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;278;247.5962,3239.692;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;114;-111.0347,2884.424;Inherit;True;Property;_MainTex;MainTex;0;0;Create;True;0;0;0;False;0;False;-1;None;7ec1122cfc202644fbdf472d0e82e91b;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;273;556.6534,3241.978;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;280;342.3531,2831.554;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;286;251.2777,3058.985;Inherit;False;Property;_AltMode;AltMode;4;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;265;218.498,2700.717;Inherit;False;Property;_EmittPower;EmittPower;3;0;Create;True;0;0;0;False;0;False;0.42;0.439;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;285;671.3217,3028.223;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;311;709.5328,3261.362;Inherit;False;Property;_BandOnly;BandOnly;9;0;Create;True;0;0;0;False;0;False;0;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;133;259.2271,2434.405;Inherit;False;Property;_Color;Color;1;0;Create;True;0;0;0;False;0;False;0.6591351,0.1367925,1,0;1,1,1,1;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;264;501.4979,2682.717;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0.4339623;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;310;935.2423,2809.602;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;261;1191.006,2736.932;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;230;1462.474,2527.651;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SmoothstepOpNode;279;993.5485,3054.016;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.05;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;199;1825,2539.14;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.VertexColorNode;287;1552,2688;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode;313;-890.535,3024.561;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;312;1344,2960;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;315;1936,2672;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;252;2241.419,2535.846;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Banter/BanterTeleportIndikator;False;False;False;False;True;True;True;True;True;False;False;False;False;False;True;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;False;0;False;Transparent;;Transparent;All;6;d3d11;glcore;gles;gles3;metal;vulkan;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;306;1;307;0
WireConnection;308;0;309;0
WireConnection;308;1;314;0
WireConnection;2;0;308;0
WireConnection;2;1;306;0
WireConnection;296;0;2;2
WireConnection;271;0;256;0
WireConnection;299;0;296;0
WireConnection;302;0;301;0
WireConnection;298;0;2;1
WireConnection;298;1;299;0
WireConnection;272;0;271;0
WireConnection;272;1;301;0
WireConnection;276;0;271;0
WireConnection;276;1;302;0
WireConnection;195;0;256;0
WireConnection;270;0;2;0
WireConnection;270;1;272;0
WireConnection;275;0;298;0
WireConnection;275;1;276;0
WireConnection;119;0;2;0
WireConnection;119;1;195;0
WireConnection;269;1;270;0
WireConnection;274;1;275;0
WireConnection;278;0;269;2
WireConnection;278;1;274;2
WireConnection;114;1;119;0
WireConnection;273;0;114;1
WireConnection;273;1;278;0
WireConnection;280;0;114;1
WireConnection;280;1;278;0
WireConnection;285;0;273;0
WireConnection;285;1;280;0
WireConnection;285;2;286;0
WireConnection;264;0;133;0
WireConnection;264;1;265;0
WireConnection;310;0;285;0
WireConnection;310;1;114;1
WireConnection;310;2;311;0
WireConnection;261;0;310;0
WireConnection;261;1;264;0
WireConnection;230;0;133;0
WireConnection;230;1;261;0
WireConnection;279;0;273;0
WireConnection;199;0;230;0
WireConnection;312;0;279;0
WireConnection;312;1;114;1
WireConnection;312;2;311;0
WireConnection;315;0;199;0
WireConnection;315;1;287;0
WireConnection;252;2;315;0
WireConnection;252;9;312;0
ASEEND*/
//CHKSM=9646F8AACD1FDAAB898C9CB2032B0E7A73ABEBF7