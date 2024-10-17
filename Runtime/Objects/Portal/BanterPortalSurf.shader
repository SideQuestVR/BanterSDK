// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Banter/PortalSurf"
{
	Properties
	{
		[PerRendererData]_MainTex("MainTex", 2D) = "white" {}
		_FXTex("FXTex", 2D) = "white" {}
		[PerRendererData]_Color("Color", Color) = (0.6591351,0.1367925,1,0)
		[PerRendererData]_Offset("Offset", Vector) = (0.15,0,0,0)
		[PerRendererData]_Tiling("Tiling", Vector) = (0.65,1,0,0)
		_Darken("Darken", Range( 0 , 1)) = 0
		_Scale("Scale", Range( 0 , 1)) = 0
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
		#pragma exclude_renderers xboxone xboxseries playstation ps4 ps5 switch 
		#pragma surface surf Unlit alpha:fade keepalpha noshadow noambient novertexlights nolightmap  nodynlightmap nodirlightmap vertex:vertexDataFunc 
		struct Input
		{
			float2 uv_texcoord;
			float2 uv2_texcoord2;
			float4 vertexColor : COLOR;
			float3 viewDir;
			INTERNAL_DATA
		};

		uniform float _Scale;
		uniform sampler2D _MainTex;
		uniform float2 _Tiling;
		uniform float2 _Offset;
		uniform sampler2D _FXTex;
		uniform float _Darken;
		uniform float4 _Color;
		uniform float4 _FXTex_ST;


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
			float lerpResult245 = lerp( length( worldToObj50 ) , ( ( 1.0 - _Scale ) * 4.0 ) , _Scale);
			float temp_output_52_0 = ( sqrt( lerpResult245 ) * 0.8 );
			float temp_output_57_0 = (-3.0 + (temp_output_52_0 - 0.0) * (-1.0 - -3.0) / (1.0 - 0.0));
			float clampResult59 = clamp( temp_output_57_0 , -1.0 , -0.1 );
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
			o.Normal = float3(0,0,1);
			float2 uv_TexCoord122 = i.uv_texcoord * _Tiling + _Offset;
			float3 worldToObj50 = mul( unity_WorldToObject, float4( _WorldSpaceCameraPos, 1 ) ).xyz;
			float lerpResult245 = lerp( length( worldToObj50 ) , ( ( 1.0 - _Scale ) * 4.0 ) , _Scale);
			float temp_output_52_0 = ( sqrt( lerpResult245 ) * 0.8 );
			float temp_output_57_0 = (-3.0 + (temp_output_52_0 - 0.0) * (-1.0 - -3.0) / (1.0 - 0.0));
			float clampResult189 = clamp( ( ( 1.0 - temp_output_57_0 ) - 0.5 ) , 0.35 , 1.0 );
			float temp_output_147_0 = ( clampResult189 / 1.0 );
			float2 _Vector1 = float2(0.5,0.5);
			float2 uv2_TexCoord2 = i.uv2_texcoord2 * float2( 2,1 );
			float2 panner119 = ( _Time.y * float2( 1,0 ) + uv2_TexCoord2);
			float4 tex2DNode114 = tex2D( _FXTex, panner119 );
			float2 panner257 = ( ( _Time.y * 0.5 ) * float2( 1,0 ) + uv2_TexCoord2);
			float2 panner258 = ( ( _Time.y * 0.25 ) * float2( 1,0 ) + uv2_TexCoord2);
			float temp_output_262_0 = ( tex2DNode114.r + ( tex2D( _FXTex, panner257 ).g + tex2D( _FXTex, panner258 ).g ) );
			float2 panner123 = ( 0.5 * _Time.y * float2( 0,1 ) + uv_TexCoord122);
			float simplePerlin2D120 = snoise( panner123*5.0 );
			simplePerlin2D120 = simplePerlin2D120*0.5 + 0.5;
			float4 temp_output_40_0 = saturate( i.vertexColor );
			float2 Offset239 = ( ( -0.5 - 1 ) * i.viewDir.xy * 0.1 ) + ( float4( ( ( ( uv_TexCoord122 * temp_output_147_0 ) + float2( 0,0 ) ) - ( ( temp_output_147_0 * _Vector1 ) - _Vector1 ) ), 0.0 , 0.0 ) + ( ( ( temp_output_262_0 + ( simplePerlin2D120 * 0.015 ) ) * ( ( 1.0 - temp_output_40_0 ) + float4( 0.2169811,0.2169811,0.2169811,0 ) ) ) * clampResult189 ) ).rg;
			float4 tex2DNode1 = tex2D( _MainTex, Offset239 );
			float clampResult113 = clamp( ( 1.0 - (-1.0 + (temp_output_52_0 - 0.0) * (0.0 - -1.0) / (1.0 - 0.0)) ) , 0.0 , 0.2 );
			float4 lerpResult237 = lerp( tex2DNode1 , ( tex2DNode1 * clampResult113 ) , _Darken);
			float smoothstepResult229 = smoothstep( 0.1 , 0.5 , uv2_TexCoord2.y);
			float2 uv1_FXTex = i.uv2_texcoord2 * _FXTex_ST.xy + _FXTex_ST.zw;
			float4 tex2DNode16 = tex2D( _FXTex, uv1_FXTex );
			float temp_output_263_0 = ( tex2DNode16.r + tex2DNode16.g );
			float4 appendResult135 = (float4(saturate( ( ( ( 1.0 - smoothstepResult229 ) * _Color ) + ( temp_output_262_0 - 0.0 ) ) ).rgb , temp_output_263_0));
			o.Emission = ( lerpResult237 + appendResult135 ).rgb;
			float smoothstepResult22 = smoothstep( 0.0 , 0.45 , uv2_TexCoord2.y);
			o.Alpha = ( temp_output_263_0 + saturate( smoothstepResult22 ) );
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;127;-785.6219,-258.5735;Inherit;True;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;39;694.2155,920.4197;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
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
Node;AmplifyShaderEditor.OneMinusNode;188;-566.558,252.2075;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;155;-1056.013,-1193.892;Inherit;False;Constant;_Float0;Float 0;1;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;201;666.2747,82.24412;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;121;-1060.048,-286.2085;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;120;-1515.224,-344.8703;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;1,1;False;1;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;40;-1756.854,945.588;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;124;-1287.106,-333.5423;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0.015;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;108;351.4457,-445.8544;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;211;-2621.979,346.025;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;205;-1102.605,160.207;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0.2169811,0.2169811,0.2169811,0;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode;128;-1347.89,128.9534;Inherit;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;122;-2108.794,-300.7094;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;0.65,1;False;1;FLOAT2;0.15,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleTimeNode;195;-2431.691,-168.4014;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;123;-1747.136,-301.2703;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,1;False;1;FLOAT;0.5;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ClampOpNode;189;-277.2482,221.5774;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.35;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;134;305.8491,76.64368;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode;231;299.9648,480.0374;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;230;506.1089,-0.9657593;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;232;-65.03516,-17.96259;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node;209;-2445.52,-484.0081;Inherit;False;Property;_Tiling;Tiling;6;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.65,1;0.65,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector2Node;210;-2439.52,-343.0081;Inherit;False;Property;_Offset;Offset;4;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.15,0;0.15,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.LerpOp;237;1098.011,-582.7161;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;238;897.0109,-712.7161;Inherit;False;Property;_Darken;Darken;7;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;109;808.4794,-475.3119;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.TFHCRemapNode;86;91.4491,-438.82;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-1;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;113;511.5965,-450.0891;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.2;False;1;FLOAT;0
Node;AmplifyShaderEditor.LengthOpNode;51;-1113.569,1203.392;Inherit;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;52;-509.5844,1189.729;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0.8;False;1;FLOAT;0
Node;AmplifyShaderEditor.SqrtOpNode;144;-695.5057,1205.533;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;245;-859.5823,1288.087;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;242;-1146.582,1467.087;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;4;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;243;-1318.582,1510.087;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;241;-1627.835,1514.477;Inherit;False;Property;_Scale;Scale;9;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.TransformPositionNode;50;-1350.548,1204.33;Inherit;True;World;Object;False;Fast;True;1;0;FLOAT3;0,0,0;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldSpaceCameraPos;49;-1612.053,1206.169;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.PosVertexDataNode;42;419.502,1458.243;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode;53;317.1826,1607.615;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;43;612.743,1460.14;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;248;-1560.862,1628.789;Inherit;False;Property;_ManualScale;ManualScale;8;0;Create;True;0;0;0;False;1;Toggle(_);False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.NormalVertexDataNode;34;47.2802,1007.317;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TFHCRemapNode;57;32.50266,1183.294;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;-0.5;False;4;FLOAT;-1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;36;-173.072,1252.211;Inherit;False;Constant;_DistanceMin;DistanceMin;3;0;Create;True;0;0;0;False;0;False;-3;-3;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;58;-269.3092,1324.261;Inherit;False;Constant;_DistanceMax;DistanceMax;4;0;Create;True;0;0;0;False;0;False;-1;-1;-1;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;140;1724.125,-145.7068;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;44;1728.347,35.73242;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;199;660.6509,-5.919998;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SmoothstepOpNode;229;-24.72219,570.7626;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.1;False;2;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;22;-4.43628,343.1738;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.45;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;54;460.6921,1603.81;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;-0.6;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;59;248.8965,1183.158;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;-1;False;2;FLOAT;-0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;133;7.049171,145.1437;Inherit;False;Property;_Color;Color;2;1;[PerRendererData];Create;True;0;0;0;False;0;False;0.6591351,0.1367925,1,0;0,1,0.2220846,1;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;1;357.3315,-681.689;Inherit;True;Property;_MainTex;MainTex;0;1;[PerRendererData];Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SwizzleNode;99;-1293.05,-80.94476;Inherit;False;FLOAT3;0;1;2;3;1;0;COLOR;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;104;-374.6613,-401.5322;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.DynamicAppendNode;135;888.8676,-240.1718;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;2;-2398.227,123.5843;Inherit;True;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;2,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;212;-2968.979,384.025;Inherit;False;InstancedProperty;_Spawn;Spawn;10;0;Create;True;0;0;0;False;0;False;0.25;0.25;-1;0.25;0;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;119;-2070.487,32.8357;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;240;-491.4559,-858.1198;Inherit;False;Tangent;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.DynamicAppendNode;253;-8.434998,-934.2231;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.NegateNode;254;-251.435,-732.2231;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ParallaxMappingNode;239;137.7128,-796.5157;Inherit;False;Normal;4;0;FLOAT2;0,0;False;1;FLOAT;-0.5;False;2;FLOAT;0.1;False;3;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;257;-2201.91,481.0308;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;258;-2205.801,634.486;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;1,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;259;-2378.851,539.8827;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;260;-2362.801,680.4859;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.25;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;261;-1640.071,487.9382;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;262;-1527.283,338.6011;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;263;485.67,-205.9678;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;16;96.30486,-186.5008;Inherit;True;Property;_FXTex;FXTex;1;0;Create;True;0;0;0;False;0;False;-1;11de5eb05f45e4a48a776d6d46e99328;None;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;114;-1852.591,40.71153;Inherit;True;Property;_TextureSample0;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;16;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;255;-1984.535,380.1064;Inherit;True;Property;_TextureSample1;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;16;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;256;-1983.539,579.6248;Inherit;True;Property;_TextureSample2;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;1ad9b7888055c8740adc124c6aff185b;1ad9b7888055c8740adc124c6aff185b;True;1;False;white;Auto;False;Instance;16;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;235;1633.188,-49.10789;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;252;2158.609,-158.3362;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Banter/PortalSurf;False;False;False;False;True;True;True;True;True;False;False;False;False;False;True;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;False;0;False;Transparent;;Transparent;All;6;d3d11;glcore;gles;gles3;metal;vulkan;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
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
WireConnection;188;0;57;0
WireConnection;201;0;22;0
WireConnection;121;0;262;0
WireConnection;121;1;124;0
WireConnection;120;0;123;0
WireConnection;40;0;38;0
WireConnection;124;0;120;0
WireConnection;108;0;86;0
WireConnection;211;1;212;0
WireConnection;205;0;128;0
WireConnection;128;0;40;0
WireConnection;122;0;209;0
WireConnection;122;1;210;0
WireConnection;123;0;122;0
WireConnection;189;0;191;0
WireConnection;134;0;231;0
WireConnection;134;1;133;0
WireConnection;231;0;229;0
WireConnection;230;0;134;0
WireConnection;230;1;232;0
WireConnection;232;0;262;0
WireConnection;237;0;1;0
WireConnection;237;1;109;0
WireConnection;237;2;238;0
WireConnection;109;0;1;0
WireConnection;109;1;113;0
WireConnection;86;0;52;0
WireConnection;113;0;108;0
WireConnection;51;0;50;0
WireConnection;52;0;144;0
WireConnection;144;0;245;0
WireConnection;245;0;51;0
WireConnection;245;1;242;0
WireConnection;245;2;241;0
WireConnection;242;0;243;0
WireConnection;243;0;241;0
WireConnection;50;0;49;0
WireConnection;53;0;52;0
WireConnection;43;0;42;0
WireConnection;43;1;54;0
WireConnection;57;0;52;0
WireConnection;57;3;36;0
WireConnection;57;4;58;0
WireConnection;140;0;237;0
WireConnection;140;1;135;0
WireConnection;44;0;39;0
WireConnection;44;1;43;0
WireConnection;199;0;230;0
WireConnection;229;0;2;2
WireConnection;22;0;2;2
WireConnection;54;0;53;0
WireConnection;59;0;57;0
WireConnection;1;1;239;0
WireConnection;99;0;114;0
WireConnection;104;0;127;0
WireConnection;104;1;189;0
WireConnection;135;0;199;0
WireConnection;135;3;263;0
WireConnection;119;0;2;0
WireConnection;119;1;195;0
WireConnection;253;0;240;1
WireConnection;253;1;240;2
WireConnection;253;2;240;3
WireConnection;254;0;240;2
WireConnection;239;0;97;0
WireConnection;239;3;240;0
WireConnection;257;0;2;0
WireConnection;257;1;259;0
WireConnection;258;0;2;0
WireConnection;258;1;260;0
WireConnection;259;0;195;0
WireConnection;260;0;195;0
WireConnection;261;0;255;2
WireConnection;261;1;256;2
WireConnection;262;0;114;1
WireConnection;262;1;261;0
WireConnection;263;0;16;1
WireConnection;263;1;16;2
WireConnection;114;1;119;0
WireConnection;255;1;257;0
WireConnection;256;1;258;0
WireConnection;235;0;263;0
WireConnection;235;1;201;0
WireConnection;252;2;140;0
WireConnection;252;9;235;0
WireConnection;252;11;44;0
ASEEND*/
//CHKSM=8809037C090D3A76ACCF40780D5787BA40E72424