// Shared body of the four SideQuest diffuse shaders:
//   Unlit/Diffuse, Unlit/DiffuseTransparent            (UV mapped)
//   Unlit/DiffuseTriplanar, Unlit/DiffuseTriplanarTransparent (world-space triplanar, no UVs)
//
// Each .shader under Runtime/Resources/Shaders is a thin wrapper that sets SQ_TRIPLANAR and
// SQ_TRANSPARENT and includes this file. They live under Resources so Shader.Find sees them in a
// player build (nothing else references them - every material is created at runtime).
//
// Optional maps are per-material local keywords, enabled by BSMaterial when the corresponding
// texture reference is non-empty:
//   _BS_NORMALMAP  - _NormalMap (tangent space, RGB, LINEAR). UV: TBN from the mesh tangent.
//                    Triplanar: whiteout blend in world space, no tangents needed.
//   _BS_MASKMAPS   - _RoughnessMap.g and _AOMap.r. The two may be the same texture: the cc0
//                    library ships one "mask" file with R = AO, G = roughness, B = metallic, and
//                    a plain grayscale map works in either slot because R == G there.
// The no-keyword variant must render exactly as the original shaders did.
//
// Lighting follows the original: a fake directional light + SH ambient driven by the _SQ_*
// globals when SideQuest.Lighting is active, GetMainLight() otherwise. Materials are shared
// scene-wide by BSMaterial's cache, so all lighting state must be global (never per-material).
// Roughness only matters through the Blinn-Phong highlight added under _BS_MASKMAPS; the
// diffuse term stays as it was.
#ifndef SQ_DIFFUSE_CORE_INCLUDED
#define SQ_DIFFUSE_CORE_INCLUDED

#ifndef SQ_TRIPLANAR
#define SQ_TRIPLANAR 0
#endif
#ifndef SQ_TRANSPARENT
#define SQ_TRANSPARENT 0
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(_SQ_FAKE_SHADOWS) || defined(_SQ_AO_MAPS)
#include "Packages/com.sidequest.creator-sdk/Runtime/Shaders/SQFakeShadow.hlsl"
#endif

#if defined(_SQ_FAKE_SHADOWS) || defined(_SQ_AO_MAPS) || defined(_BS_MASKMAPS) || SQ_TRIPLANAR
#define SQ_NEEDS_POSITION_WS 1
#else
#define SQ_NEEDS_POSITION_WS 0
#endif

#if defined(_BS_NORMALMAP) && !SQ_TRIPLANAR
#define SQ_NEEDS_TANGENT 1
#else
#define SQ_NEEDS_TANGENT 0
#endif

TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalMap);    SAMPLER(sampler_NormalMap);
TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_AOMap);        SAMPLER(sampler_AOMap);

// Identical in all four shaders (SRP batcher compatibility across the family).
CBUFFER_START(UnityPerMaterial)
	float4 _MainTex_ST;
	half4  _Color;
	float  _Cull;
	half   _NormalStrength;
	half   _SpecStrength;
	half   _AOStrength;
	float  _TriplanarScale;
	half   _TriplanarSharpness;
CBUFFER_END

struct Attributes
{
	float4 positionOS : POSITION;
	float3 normalOS   : NORMAL;
#if !SQ_TRIPLANAR
	float2 uv         : TEXCOORD0;
#endif
#if SQ_NEEDS_TANGENT
	float4 tangentOS  : TANGENT;
#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
	float4 positionCS : SV_POSITION;
#if !SQ_TRIPLANAR
	float2 uv         : TEXCOORD0;
#endif
	half3  normalWS   : TEXCOORD1;
#if SQ_NEEDS_POSITION_WS
	float3 positionWS : TEXCOORD2;
#endif
#if SQ_NEEDS_TANGENT
	half4  tangentWS  : TEXCOORD3;   // w = bitangent sign
#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings vert(Attributes v)
{
	Varyings o = (Varyings)0;

	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_TRANSFER_INSTANCE_ID(v, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
#if !SQ_TRIPLANAR
	o.uv = TRANSFORM_TEX(v.uv, _MainTex);
#endif
	o.normalWS = TransformObjectToWorldNormal(v.normalOS);
#if SQ_NEEDS_POSITION_WS
	o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
#endif
#if SQ_NEEDS_TANGENT
	o.tangentWS = half4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
#endif
	return o;
}

half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(i);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

	// Geometric normal: the interpolated one for mapping decisions (stable from either side),
	// and the back-face-flipped one for lighting.
	half3 vertexN = normalize(i.normalWS);
	half  faceSign = IS_FRONT_VFACE(facing, 1.0h, -1.0h);
	half3 geomN = vertexN * faceSign;

	// ---------------------------------------------------------------- albedo (+ triplanar setup)
#if SQ_TRIPLANAR
	float3 p = i.positionWS * _TriplanarScale;
	half3 bw = pow(abs(vertexN), _TriplanarSharpness);
	bw /= max(bw.x + bw.y + bw.z, 1e-4h);
	// Flip one uv axis per side so the texture is not mirrored on the negative faces.
	half3 axisSign = half3(vertexN.x < 0 ? -1.0h : 1.0h, vertexN.y < 0 ? -1.0h : 1.0h, vertexN.z < 0 ? -1.0h : 1.0h);
	float2 uvX = p.zy; uvX.x *= axisSign.x;
	float2 uvY = p.xz; uvY.x *= axisSign.y;
	float2 uvZ = p.xy; uvZ.x *= -axisSign.z;
	half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvX) * bw.x
	               + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvY) * bw.y
	               + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvZ) * bw.z;
#else
	half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
#endif

	half4 col = texColor * _Color;

	// ---------------------------------------------------------------- shading normal
	half3 N = geomN;
#if defined(_BS_NORMALMAP)
	#if SQ_TRIPLANAR
		// Whiteout blend (Golus): swizzle world normals into each projection's tangent space,
		// blend, swizzle back. No mesh tangents required.
		half3 tnX = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvX), _NormalStrength);
		half3 tnY = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvY), _NormalStrength);
		half3 tnZ = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvZ), _NormalStrength);
		tnX.x *= axisSign.x;
		tnY.x *= axisSign.y;
		tnZ.x *= -axisSign.z;
		tnX = half3(tnX.xy + vertexN.zy, abs(tnX.z) * vertexN.x);
		tnY = half3(tnY.xy + vertexN.xz, abs(tnY.z) * vertexN.y);
		tnZ = half3(tnZ.xy + vertexN.xy, abs(tnZ.z) * vertexN.z);
		N = normalize(tnX.zyx * bw.x + tnY.xzy * bw.y + tnZ.xyz * bw.z) * faceSign;
	#else
		half3 tn = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalStrength);
		half3 T = normalize(i.tangentWS.xyz);
		half3 B = cross(vertexN, T) * i.tangentWS.w;
		N = normalize(tn.x * T + tn.y * B + tn.z * vertexN) * faceSign;
	#endif
#endif

	// ---------------------------------------------------------------- roughness / AO maps
	half rough = 1.0h;
	half aoTex = 1.0h;
#if defined(_BS_MASKMAPS)
	#if SQ_TRIPLANAR
		half2 mX = half2(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvX).g, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uvX).r);
		half2 mY = half2(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvY).g, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uvY).r);
		half2 mZ = half2(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvZ).g, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uvZ).r);
		half2 m = mX * bw.x + mY * bw.y + mZ * bw.z;
	#else
		half2 m = half2(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, i.uv).g, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, i.uv).r);
	#endif
	rough = m.x;
	aoTex = lerp(1.0h, m.y, _AOStrength);
#endif

	// ---------------------------------------------------------------- lighting
	half3 lighting;
	half3 L;
	half3 lightCol;   // direct light colour, already attenuated by shadow where one exists
	half  aoDirect = 1.0h;
#if defined(_SQ_FAKE_SHADOWS)
	// Fake directional light: direction/colour come from the _SQ_ globals, never
	// GetMainLight() - materials are shared scene-wide by BSMaterial's cache, so
	// all lighting state must be global. The 0.6 floor of the plain branch is replaced by
	// tunable floors here so shadowed faces can actually darken.
	L = _SQ_FakeLightDir.xyz;
	half  ndl    = saturate(dot(N, L));
	half  shadow = SQSampleFakeShadow(i.positionWS, geomN);
	lightCol = _SQ_FakeLightColor.rgb * shadow;
	#if defined(_SQ_AO_MAPS)
		// AO is ambient attenuation, not a colour multiply: ambient is sampled
		// along the bent normal (light arrives from the open side of a corner)
		// and scaled by visibility; the sun gets only a mild micro-occlusion -
		// it already has a real shadow term.
		half aoVis; half3 bentN;
		SQSampleAOVolume(i.positionWS, geomN, aoVis, bentN);
		half ao = lerp(1.0h, aoVis, _SQ_LightingParams.z);
		half3 amb = max(SampleSH(bentN), _SQ_LightingParams.yyy) * ao * aoTex;
		aoDirect = lerp(1.0h, aoVis, 0.5h * _SQ_LightingParams.z) * lerp(1.0h, aoTex, 0.5h);
		lighting = max(_SQ_LightingParams.xxx, amb + ndl * lightCol * aoDirect);
	#else
		half3 amb = max(SampleSH(N), _SQ_LightingParams.yyy) * aoTex;
		aoDirect = lerp(1.0h, aoTex, 0.5h);
		lighting = max(_SQ_LightingParams.xxx, amb + ndl * lightCol * aoDirect);
	#endif
#else
	// Calculate lighting from main directional light if it exists
	Light mainLight = GetMainLight();
	L = mainLight.direction;
	lightCol = mainLight.color;
	half NdotL = max(0.0h, dot(N, L));

	// Add ambient/environment lighting (works without lights)
	half3 ambient = SampleSH(N);

	// Combine directional and ambient light, ensuring minimum brightness
	lighting = max(0.6h, max(ambient * 0.5h + 0.5h, NdotL * lightCol + ambient));
	#if defined(_SQ_AO_MAPS)
		half aoVis; half3 bentN;
		SQSampleAOVolume(i.positionWS, geomN, aoVis, bentN);
		lighting *= lerp(1.0h, aoVis, _SQ_LightingParams.z);
	#endif
	// After the floor, or the map would be invisible on anything the floor clamps.
	lighting *= aoTex;
	aoDirect = lerp(1.0h, aoTex, 0.5h);
#endif

	// Apply lighting to color
	col.rgb *= lighting;

#if defined(_BS_MASKMAPS)
	// Cheap Blinn-Phong highlight so roughness is visible. Normalised so a sharp highlight
	// does not get brighter than a broad one; rough (1.0) surfaces get none at all.
	float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
	half3 H = normalize(L + half3(V));
	half gloss = saturate(1.0h - rough);
	half specPow = exp2(1.0h + gloss * 10.0h);
	half spec = pow(saturate(dot(N, H)), specPow) * (specPow + 8.0h) * 0.0398h * gloss * _SpecStrength;
	col.rgb += spec * lightCol * aoDirect * _Color.a;
#endif

#if SQ_TRANSPARENT
	// Keep the sampled alpha (times the tint's alpha, already applied above).
#else
	col.a = 1;
#endif

	return col;
}

#endif // SQ_DIFFUSE_CORE_INCLUDED
