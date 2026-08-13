// Made with Amplify Shader Editor v1.9.1.5 (original Built-in surface shader kept as the
// second SubShader below). A hand-written URP Unlit port was added as the first SubShader:
// the original is an Unlit surface shader (lighting returns (0,0,0,alpha)), so the whole
// look is o.Emission + o.Alpha, which ports cleanly to a URP transparent unlit pass.
// Under URP the first SubShader is used; under the Built-in pipeline the second is used.
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

	// ──────────────────────────────────────────────────────────────────────────────
	// URP (Universal Render Pipeline) — hand-ported from the Built-in surface shader.
	// ──────────────────────────────────────────────────────────────────────────────
	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "ForwardUnlit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Back

			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile_fog

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
			TEXTURE2D(_FXTex);   SAMPLER(sampler_FXTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float4 _FXTex_ST;
				float4 _Color;
				float4 _Offset;
				float4 _Tiling;
				float _Darken;
				float _Scale;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float4 color      : COLOR;
				float2 uv0        : TEXCOORD0;
				float2 uv1        : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float4 color       : COLOR;
				float2 uv0         : TEXCOORD0;
				float2 uv1         : TEXCOORD1;
				float3 viewDirTS   : TEXCOORD2; // tangent-space view dir (for parallax)
				float  camDist     : TEXCOORD3; // temp_output_52_0 (constant per object)
				float  fogFactor   : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// --- Simplex noise (ported verbatim from the Amplify surface shader) ---
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

			// Camera position expressed in object space -> the shared distance metric
			// (Amplify temp_output_52_0). Constant across the whole object.
			float PortalCamDist()
			{
				float3 camOS = mul( GetWorldToObjectMatrix(), float4( _WorldSpaceCameraPos, 1.0 ) ).xyz;
				float lerpResult245 = lerp( length( camOS ), ( ( 1.0 - _Scale ) * 4.0 ), _Scale );
				return sqrt( lerpResult245 ) * 0.8;
			}

			Varyings vert( Attributes IN )
			{
				Varyings OUT = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_TRANSFER_INSTANCE_ID( IN, OUT );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( OUT );

				float camDist = PortalCamDist(); // temp_output_52_0

				// --- vertex displacement (vertexDataFunc) ---
				float4 vcol = saturate( IN.color );
				float3 n = IN.normalOS;
				float3 appendResult37 = float3( 0.0, n.y, n.z );
				float temp57 = -3.0 + camDist * 2.0;
				float clamp59 = clamp( temp57, -1.0, -0.1 );
				float clamp54 = clamp( 1.0 - camDist, -0.6, 0.0 );
				float3 displaced = IN.positionOS.xyz
					+ ( vcol.xyz * ( appendResult37 * clamp59 ) )
					+ ( IN.positionOS.xyz * clamp54 );

				VertexPositionInputs posInputs = GetVertexPositionInputs( displaced );
				OUT.positionHCS = posInputs.positionCS;

				// --- tangent-space view direction (for the parallax _MainTex offset) ---
				VertexNormalInputs normInputs = GetVertexNormalInputs( IN.normalOS, IN.tangentOS );
				float3 viewDirWS = normalize( GetWorldSpaceViewDir( posInputs.positionWS ) );
				OUT.viewDirTS = float3(
					dot( normInputs.tangentWS,   viewDirWS ),
					dot( normInputs.bitangentWS, viewDirWS ),
					dot( normInputs.normalWS,    viewDirWS ) );

				OUT.color = IN.color;
				OUT.uv0 = IN.uv0;
				OUT.uv1 = IN.uv1;
				OUT.camDist = camDist;
				OUT.fogFactor = ComputeFogFactor( posInputs.positionCS.z );
				return OUT;
			}

			half4 frag( Varyings IN ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

				float camDist = IN.camDist; // temp_output_52_0

				float2 uv_TexCoord122 = IN.uv0 * _Tiling.xy + _Offset.xy;
				float temp57 = -3.0 + camDist * 2.0;
				float clampResult189 = clamp( ( 1.0 - temp57 ) - 0.5, 0.35, 1.0 );
				float temp147 = clampResult189;
				float2 _Vector1 = float2( 0.5, 0.5 );
				float2 uv2 = IN.uv1 * float2( 2.0, 1.0 );

				float t = _Time.y;
				float2 panner119 = t * float2( 1.0, 0.0 ) + uv2;
				float fx114r = SAMPLE_TEXTURE2D( _FXTex, sampler_FXTex, panner119 ).r;
				float2 panner257 = ( t * 0.5 ) * float2( 1.0, 0.0 ) + uv2;
				float2 panner258 = ( t * 0.25 ) * float2( 1.0, 0.0 ) + uv2;
				float temp262 = fx114r + ( SAMPLE_TEXTURE2D( _FXTex, sampler_FXTex, panner257 ).g
										 + SAMPLE_TEXTURE2D( _FXTex, sampler_FXTex, panner258 ).g );

				float2 panner123 = 0.5 * t * float2( 0.0, 1.0 ) + uv_TexCoord122;
				float noise = snoise( panner123 * 5.0 ) * 0.5 + 0.5;

				float4 vcol = saturate( IN.color );

				// Parallax-offset UV for _MainTex (Amplify Offset239)
				float2 parallax = ( -1.5 ) * IN.viewDirTS.xy * 0.1;
				float2 uvBase = ( uv_TexCoord122 * temp147 ) - ( ( temp147 * _Vector1 ) - _Vector1 );
				float2 distortion = ( temp262 + noise * 0.015 )
					* ( ( 1.0 - vcol.rg ) + float2( 0.2169811, 0.2169811 ) )
					* clampResult189;
				float2 Offset239 = parallax + ( uvBase + distortion );

				float4 mainTex = SAMPLE_TEXTURE2D( _MainTex, sampler_MainTex, Offset239 );

				float clampResult113 = clamp( 2.0 - camDist, 0.0, 0.2 );
				float4 darkened = lerp( mainTex, mainTex * clampResult113, _Darken );

				float smooth229 = smoothstep( 0.1, 0.5, uv2.y );
				float2 uv1_FX = IN.uv1 * _FXTex_ST.xy + _FXTex_ST.zw;
				float4 fx16 = SAMPLE_TEXTURE2D( _FXTex, sampler_FXTex, uv1_FX );
				float temp263 = fx16.r + fx16.g;

				float3 rgb135 = saturate( ( ( 1.0 - smooth229 ) * _Color.rgb ) + temp262 );
				float3 emission = darkened.rgb + rgb135;

				float smooth22 = smoothstep( 0.0, 0.45, uv2.y );
				float alpha = temp263 + saturate( smooth22 );

				emission = MixFog( emission, IN.fogFactor );
				return half4( emission, alpha );
			}
			ENDHLSL
		}
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Built-in Render Pipeline — original Amplify surface shader (kept for the Banter
	// project / non-URP builds; ignored under URP because the SubShader above matches).
	// ──────────────────────────────────────────────────────────────────────────────
	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#pragma multi_compile_instancing
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

		UNITY_INSTANCING_BUFFER_START(BanterPortalSurf)
			UNITY_DEFINE_INSTANCED_PROP(float4, _FXTex_ST)
#define _FXTex_ST_arr BanterPortalSurf
		UNITY_INSTANCING_BUFFER_END(BanterPortalSurf)


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
			float4 _FXTex_ST_Instance = UNITY_ACCESS_INSTANCED_PROP(_FXTex_ST_arr, _FXTex_ST);
			float2 uv1_FXTex = i.uv2_texcoord2 * _FXTex_ST_Instance.xy + _FXTex_ST_Instance.zw;
			float4 tex2DNode16 = tex2D( _FXTex, uv1_FXTex );
			float temp_output_263_0 = ( tex2DNode16.r + tex2DNode16.g );
			float4 appendResult135 = (float4(saturate( ( ( ( 1.0 - smoothstepResult229 ) * _Color ) + ( temp_output_262_0 - 0.0 ) ) ).rgb , temp_output_263_0));
			o.Emission = ( lerpResult237 + appendResult135 ).rgb;
			float smoothstepResult22 = smoothstep( 0.0 , 0.45 , uv2_TexCoord2.y);
			o.Alpha = ( temp_output_263_0 + saturate( smoothstepResult22 ) );
		}

		ENDCG
	}
}
