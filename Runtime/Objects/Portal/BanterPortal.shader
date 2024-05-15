// Upgrade NOTE: upgraded instancing buffer 'BanterPortalC' to new syntax.

// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Banter/PortalC"
{
	Properties
	{
		[PerRendererData]_MainTex("MainTex", 2D) = "white" {}
		_FXTex("FXTex", 2D) = "white" {}
		[PerRendererData]_Color("Color", Color) = (0.6591351,0.1367925,1,0)
		[PerRendererData]_Offset("Offset", Vector) = (0.15,0,0,0)
		[PerRendererData]_Tiling("Tiling", Vector) = (0.65,1,0,0)
		_Spawn("Spawn", Range( -1 , 0.25)) = 0.25
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
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
		#pragma multi_compile_instancing
		#pragma only_renderers d3d11 glcore gles gles3 
		#pragma surface surf Unlit alpha:fade keepalpha noshadow vertex:vertexDataFunc 
		struct Input
		{
			float2 uv_texcoord;
			float2 uv2_texcoord2;
			float4 vertexColor : COLOR;
		};

		uniform sampler2D _MainTex;
		uniform float2 _Tiling;
		uniform float2 _Offset;
		uniform sampler2D _FXTex;
		uniform float4 _Color;

		UNITY_INSTANCING_BUFFER_START(BanterPortalC)
			UNITY_DEFINE_INSTANCED_PROP(float, _Spawn)
#define _Spawn_arr BanterPortalC
		UNITY_INSTANCING_BUFFER_END(BanterPortalC)


		float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }

		float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }

		float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }

		float snoise( float2 v )
		{
			const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
			float2 i = floor( v + dot( v, C.yy ) );
			float2 x0 = v - i + dot( i, C.xx );
			float2 i1;
			i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
			float4 x12 = x0.xyxy + C.xxzz;
			x12.xy -= i1;
			i = mod2D289( i );
			float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
			float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
			m = m * m;
			m = m * m;
			float3 x = 2.0 * frac( p * C.www ) - 1.0;
			float3 h = abs( x ) - 0.5;
			float3 ox = floor( x + 0.5 );
			float3 a0 = x - ox;
			m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
			float3 g;
			g.x = a0.x * x0.x + h.x * x0.y;
			g.yz = a0.yz * x12.xz + h.yz * x12.yw;
			return 130.0 * dot( m, g );
		}


		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float4 temp_output_40_0 = saturate( v.color );
			float3 ase_vertexNormal = v.normal.xyz;
			float4 appendResult37 = (float4(0.0 , ase_vertexNormal.y , ase_vertexNormal.z , 0.0));
			float3 worldToObj50 = mul( unity_WorldToObject, float4( _WorldSpaceCameraPos, 1 ) ).xyz;
			float temp_output_52_0 = ( sqrt( length( worldToObj50 ) ) * 0.8 );
			float temp_output_57_0 = (-3.0 + (temp_output_52_0 - 0.0) * (-1.0 - -3.0) / (1.0 - 0.0));
			float clampResult59 = clamp( temp_output_57_0 , -1.0 , -0.35 );
			float3 ase_vertex3Pos = v.vertex.xyz;
			float clampResult54 = clamp( ( 1.0 - temp_output_52_0 ) , -0.6 , 0.0 );
			v.vertex.xyz += ( ( temp_output_40_0 * ( appendResult37 * clampResult59 ) ) + float4( ( ase_vertex3Pos * clampResult54 ) , 0.0 ) ).rgb;
			v.vertex.w = 1;
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float2 uv_TexCoord122 = i.uv_texcoord * _Tiling + _Offset;
			float3 worldToObj50 = mul( unity_WorldToObject, float4( _WorldSpaceCameraPos, 1 ) ).xyz;
			float temp_output_52_0 = ( sqrt( length( worldToObj50 ) ) * 0.8 );
			float temp_output_57_0 = (-3.0 + (temp_output_52_0 - 0.0) * (-1.0 - -3.0) / (1.0 - 0.0));
			float clampResult189 = clamp( ( ( 1.0 - temp_output_57_0 ) - 0.5 ) , 0.35 , 1.0 );
			float temp_output_147_0 = ( clampResult189 / 1.0 );
			float2 _Vector1 = float2(0.5,0.5);
			float _Spawn_Instance = UNITY_ACCESS_INSTANCED_PROP(_Spawn_arr, _Spawn);
			float2 appendResult211 = (float2(0.0 , _Spawn_Instance));
			float2 uv2_TexCoord2 = i.uv2_texcoord2 * float2( 2,1 ) + appendResult211;
			float2 panner119 = ( _Time.y * float2( 1,0 ) + uv2_TexCoord2);
			float4 tex2DNode114 = tex2D( _FXTex, panner119 );
			float2 panner123 = ( 0.5 * _Time.y * float2( 0,1 ) + uv_TexCoord122);
			float simplePerlin2D120 = snoise( panner123*5.0 );
			simplePerlin2D120 = simplePerlin2D120*0.5 + 0.5;
			float4 temp_output_40_0 = saturate( i.vertexColor );
			float4 tex2DNode1 = tex2D( _MainTex, ( float4( ( ( ( uv_TexCoord122 * temp_output_147_0 ) + float2( 0,0 ) ) - ( ( temp_output_147_0 * _Vector1 ) - _Vector1 ) ), 0.0 , 0.0 ) + ( ( float4( ( (tex2DNode114).rgb + ( simplePerlin2D120 * 0.015 ) ) , 0.0 ) * ( ( 1.0 - temp_output_40_0 ) + float4( 0.2169811,0.2169811,0.2169811,0 ) ) ) * clampResult189 ) ).rg );
			float smoothstepResult229 = smoothstep( 0.25 , 0.7 , uv2_TexCoord2.y);
			float4 temp_output_230_0 = ( ( ( 1.0 - smoothstepResult229 ) * _Color ) + ( tex2DNode114 - float4( 0,0,0,0 ) ) );
			float4 tex2DNode16 = tex2D( _FXTex, panner119 );
			float4 appendResult135 = (float4(saturate( temp_output_230_0 ).rgb , tex2DNode16.r));
			o.Emission = ( tex2DNode1 + appendResult135 ).rgb;
			float smoothstepResult22 = smoothstep( 0.4 , 0.45 , uv2_TexCoord2.y);
			o.Alpha = ( tex2DNode16 + saturate( smoothstepResult22 ) ).r;
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;127;-785.6219,-258.5735;Inherit;True;2;2;0;FLOAT3;0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;39;694.2155,920.4197;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.NormalVertexDataNode;34;30.98381,1031.762;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;35;427.1208,1060.36;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.DynamicAppendNode;37;243.4664,1034.075;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleAddOpNode;97;-25.8638,-654.3907;Inherit;False;2;2;0;FLOAT2;0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.VertexColorNode;38;-1961.778,964.7357;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;148;-609.9736,-1228.521;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;149;-664.0986,-1428.26;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;150;-459.8645,-1429.25;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;153;-285.8214,-1427.777;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;156;-447.1516,-1132.337;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;154;-786.3544,-1112.481;Inherit;False;Constant;_Vector1;Vector 0;0;0;Create;True;0;0;0;False;0;False;0.5,0.5;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleDivideOpNode;147;-790.0654,-1227.907;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;191;-413.2767,235.492;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldSpaceCameraPos;49;-1326.841,1299.947;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.TransformPositionNode;50;-1057.336,1201.108;Inherit;True;World;Object;False;Fast;True;1;0;FLOAT3;0,0,0;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.LengthOpNode;51;-805.3572,1180.17;Inherit;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;188;-566.558,252.2075;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;155;-1056.013,-1193.892;Inherit;False;Constant;_Float0;Float 0;1;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;53;179.7106,1630.527;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PosVertexDataNode;42;472.9633,1469.154;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SqrtOpNode;144;-630.2936,1170.311;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;43;754.5793,1498.327;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode;16;192.057,-165.7555;Inherit;True;Property;_FXTex;FXTex;1;0;Create;True;0;0;0;False;0;False;-1;None;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;199;538.6509,-157.92;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.DynamicAppendNode;135;712.0458,-88.30357;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SaturateNode;201;666.2747,82.24412;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;121;-1060.048,-286.2085;Inherit;True;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;120;-1515.224,-344.8703;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;1,1;False;1;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;40;-1756.854,945.588;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;104;-374.6613,-401.5322;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;114;-1684.682,-79.33846;Inherit;True;Property;_TextureSample0;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;16;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PannerNode;119;-2085.315,120.663;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SwizzleNode;99;-1302.15,-64.04473;Inherit;False;FLOAT3;0;1;2;3;1;0;COLOR;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;124;-1287.106,-333.5423;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0.015;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;108;351.4457,-445.8544;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;52;-439.3723,1168.507;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0.8;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;211;-2621.979,346.025;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TFHCRemapNode;57;-12.23032,1184.385;Inherit;True;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-0.5;False;4;FLOAT;-1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;113;511.5965,-450.0891;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.1;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;139;-629.2728,514.8831;Inherit;False;Property;_uv1VMin;uv1VMin;4;0;Create;True;0;0;0;False;0;False;0.35;0.4;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;138;-619.5305,598.6475;Inherit;False;Property;_uv1VMax;uv1VMax;3;0;Create;True;0;0;0;False;0;False;0.35;0.45;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TFHCRemapNode;86;91.4491,-438.82;Inherit;True;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-1;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;205;-1102.605,160.207;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0.2169811,0.2169811,0.2169811,0;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode;128;-1347.89,128.9534;Inherit;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;36;-201.2942,1268.577;Inherit;False;Constant;_DistanceMin;DistanceMin;2;0;Create;True;0;0;0;False;0;False;-3;-3;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;58;-297.6036,1406.09;Inherit;False;Constant;_DistanceMax;DistanceMax;2;0;Create;True;0;0;0;False;0;False;-1;-1;-1;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;122;-2108.794,-300.7094;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;0.65,1;False;1;FLOAT2;0.15,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleTimeNode;195;-2431.691,-168.4014;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;123;-1747.136,-301.2703;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,1;False;1;FLOAT;0.5;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ClampOpNode;54;436.6888,1649.635;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;-0.6;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;59;258.0632,1196.399;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;-1;False;2;FLOAT;-0.35;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;189;-277.2482,221.5774;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0.35;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;109;687.5801,-551.4118;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;212;-2968.979,384.025;Inherit;False;InstancedProperty;_Spawn;Spawn;7;0;Create;True;0;0;0;False;0;False;0.25;0.25;-1;0.25;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;140;1211.125,-300.7068;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;44;1633.081,167.3993;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;213;2051.782,-168.834;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Banter/PortalC;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;False;0;False;Transparent;;Transparent;All;4;d3d11;glcore;gles;gles3;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Spherical;False;False;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.GetLocalVarNode;193;1770.597,-228.4594;Inherit;False;192;TEST;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;134;305.8491,76.64368;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode;231;299.9648,480.0374;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;22;-4.43628,343.1738;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0.4;False;2;FLOAT;0.45;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;230;506.1089,-0.9657593;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;2;-2398.227,123.5843;Inherit;True;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;2,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleSubtractOpNode;232;-65.03516,-17.96259;Inherit;False;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SmoothstepOpNode;229;-24.72219,570.7626;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0.25;False;2;FLOAT;0.7;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;192;1094.433,124.6145;Inherit;False;TEST;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;235;1471.223,16.41945;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;1;98.33147,-677.689;Inherit;True;Property;_MainTex;MainTex;0;1;[PerRendererData];Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;133;7.049171,145.1437;Inherit;False;Property;_Color;Color;2;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.6591351,0.1367925,1,0;0.4762058,1,0.4470588,1;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;209;-2445.52,-484.0081;Inherit;False;Property;_Tiling;Tiling;6;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.65,1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector2Node;210;-2439.52,-343.0081;Inherit;False;Property;_Offset;Offset;5;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.15,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
WireConnection;127;0;121;0
WireConnection;127;1;205;0
WireConnection;39;0;40;0
WireConnection;39;1;35;0
WireConnection;35;0;37;0
WireConnection;35;1;59;0
WireConnection;37;1;34;2
WireConnection;37;2;34;3
WireConnection;97;0;153;0
WireConnection;97;1;104;0
WireConnection;148;0;147;0
WireConnection;148;1;154;0
WireConnection;149;0;122;0
WireConnection;149;1;147;0
WireConnection;150;0;149;0
WireConnection;153;0;150;0
WireConnection;153;1;156;0
WireConnection;156;0;148;0
WireConnection;156;1;154;0
WireConnection;147;0;189;0
WireConnection;147;1;155;0
WireConnection;191;0;188;0
WireConnection;50;0;49;0
WireConnection;51;0;50;0
WireConnection;188;0;57;0
WireConnection;53;0;52;0
WireConnection;144;0;51;0
WireConnection;43;0;42;0
WireConnection;43;1;54;0
WireConnection;16;1;119;0
WireConnection;199;0;230;0
WireConnection;135;0;199;0
WireConnection;135;3;16;0
WireConnection;201;0;22;0
WireConnection;121;0;99;0
WireConnection;121;1;124;0
WireConnection;120;0;123;0
WireConnection;40;0;38;0
WireConnection;104;0;127;0
WireConnection;104;1;189;0
WireConnection;114;1;119;0
WireConnection;119;0;2;0
WireConnection;119;1;195;0
WireConnection;99;0;114;0
WireConnection;124;0;120;0
WireConnection;108;0;86;0
WireConnection;52;0;144;0
WireConnection;211;1;212;0
WireConnection;57;0;52;0
WireConnection;57;3;36;0
WireConnection;57;4;58;0
WireConnection;113;0;108;0
WireConnection;86;0;52;0
WireConnection;205;0;128;0
WireConnection;128;0;40;0
WireConnection;122;0;209;0
WireConnection;122;1;210;0
WireConnection;123;0;122;0
WireConnection;54;0;53;0
WireConnection;59;0;57;0
WireConnection;189;0;191;0
WireConnection;109;0;1;0
WireConnection;109;1;113;0
WireConnection;140;0;1;0
WireConnection;140;1;135;0
WireConnection;44;0;39;0
WireConnection;44;1;43;0
WireConnection;213;2;140;0
WireConnection;213;9;235;0
WireConnection;213;11;44;0
WireConnection;134;0;231;0
WireConnection;134;1;133;0
WireConnection;231;0;229;0
WireConnection;22;0;2;2
WireConnection;230;0;134;0
WireConnection;230;1;232;0
WireConnection;2;1;211;0
WireConnection;232;0;114;0
WireConnection;229;0;2;2
WireConnection;192;0;230;0
WireConnection;235;0;16;0
WireConnection;235;1;201;0
WireConnection;1;1;97;0
ASEEND*/
//CHKSM=B6F5D00BC28BC3FE6888E1C687C80B4C90177C37