#ifndef SQ_FAKE_SHADOW_INCLUDED
#define SQ_FAKE_SHADOW_INCLUDED

// Fake directional light shadow map + multi-direction AO atlas sampling.
// Rendered by SideQuest.Lighting (FakeLightRenderer) via CommandBuffer.DrawRenderer;
// everything here is global state because BSMaterial shares Material instances
// scene-wide by signature — per-material properties cannot carry lighting.
//
// Depth convention: the caster writes ray distance from the light plane,
// normalized to 0..1 over [near, far]. Clear colour is white (1 = nothing there).
// The VP matrices below include the texture scale-bias, so xy is uv directly.

TEXTURE2D(_SQ_FakeShadowMap);         SAMPLER(sampler_SQ_FakeShadowMap);
TEXTURE2D_ARRAY(_SQ_AOAtlas);         SAMPLER(sampler_SQ_AOAtlas);

float4x4 _SQ_FakeLightVP;          // world -> sun map uv (scale-bias baked in)
float4   _SQ_FakeLightDir;         // xyz = surface->light, normalized
float4   _SQ_FakeLightColor;       // rgb * intensity, linear
float4   _SQ_FakeShadowRayOrigin;  // xyz = light plane origin (virtual cam pos)
// x = depth bias (normalized-depth units), y = normal bias (world units, scaled to texel by C#),
// z = 1 / sunMapSize (uv per texel), w = sunMapSize
float4   _SQ_FakeShadowParams;
// x = near, y = 1 / (far - near)
float4   _SQ_FakeShadowDepthParams;
// x = PCF sample count (1..16), y = PCF radius in texels
float4   _SQ_FakeShadowPcfParams;
// x = minimum lighting floor, y = ambient floor, z = AO strength, w = sun test mode (0 = coverage, 1 = depth compare)
float4   _SQ_LightingParams;

// AO: 8 ortho depth slices in a Texture2DArray, one per fixed hemisphere direction.
float4x4 _SQ_AOMatrices[8];        // world -> slice uv [0,1] (scale-bias baked in)
float4   _SQ_AODirs[8];            // xyz = surface->light direction, w = 1 / (far - near)
float4   _SQ_AOOrigins[8];         // xyz = slice ray origin, w = near
// x = unused, y = normal bias (world units), z = depth bias (normalized), w = unused
float4   _SQ_AOParams;

static const float2 kSQPoisson16[16] = {
    float2(-0.94201624, -0.39906216), float2( 0.94558609, -0.76890725),
    float2(-0.09418410, -0.92938870), float2( 0.34495938,  0.29387760),
    float2(-0.91588581,  0.45771432), float2(-0.81544232, -0.87912464),
    float2(-0.38277543,  0.27676845), float2( 0.97484398,  0.75648379),
    float2( 0.44323325, -0.97511554), float2( 0.53742981, -0.47373420),
    float2(-0.26496911, -0.41893023), float2( 0.79197514,  0.19090188),
    float2(-0.24188840,  0.99706507), float2(-0.81409955,  0.91437590),
    float2( 0.19984126,  0.78641367), float2( 0.14383161, -0.14100790)
};

float SQFakeRand(float2 co)
{
    return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
}

// 1 = fully lit, 0 = fully shadowed.
half SQSampleFakeShadow(float3 positionWS, half3 normalWS)
{
    float NdotL = saturate(dot(normalWS, _SQ_FakeLightDir.xyz));

    // Per-texel depth variance grows with surface slope in light space; the depth
    // bias scales with it or every tilted surface self-shadows.
    float slope = sqrt(max(0.0, 1.0 - NdotL * NdotL)) / max(NdotL, 0.0001);
    slope = min(slope, 16.0);

    float4 sp = mul(_SQ_FakeLightVP, float4(positionWS, 1.0));
    float2 uv = sp.xy; // ortho projection: w is 1, scale-bias already applied
    if (any(uv < 0.0) || any(uv > 1.0)) return 1.0; // outside the volume = lit

    float rayDistance = dot(positionWS - _SQ_FakeShadowRayOrigin.xyz, -_SQ_FakeLightDir.xyz);
    float refDepth = saturate((rayDistance - _SQ_FakeShadowDepthParams.x) * _SQ_FakeShadowDepthParams.y);
    refDepth -= _SQ_FakeShadowParams.x * (1.0 + slope)
              + _SQ_FakeShadowParams.y * _SQ_FakeShadowDepthParams.y;

    int sampleCount = clamp((int)_SQ_FakeShadowPcfParams.x, 1, 16);
    float radius = _SQ_FakeShadowParams.z * _SQ_FakeShadowPcfParams.y;

    float angle = SQFakeRand(uv * _SQ_FakeShadowParams.w) * 6.2831853;
    float s, c;
    sincos(angle, s, c);
    float2x2 rot = float2x2(c, -s, s, c);

    float sum = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        if (i >= sampleCount) break;
        float2 o = mul(rot, kSQPoisson16[i]) * radius;
        float mapDepth = SAMPLE_TEXTURE2D_LOD(_SQ_FakeShadowMap, sampler_SQ_FakeShadowMap, uv + o, 0).r;
        if (_SQ_LightingParams.w < 0.5)
        {
            // Coverage mode: any caster texel shadows. Only usable when receivers
            // are excluded from the caster set — every tracked primitive is both,
            // so depth compare is the default here (unlike the Airbus original).
            sum += mapDepth < 0.999 ? 0.0 : 1.0;
        }
        else
        {
            sum += step(refDepth, mapDepth);
        }
    }
    return (half)(sum / sampleCount);
}

// Soft sky visibility from 8 fixed hemisphere directions, cosine weighted.
// 1 = fully open, 0 = fully occluded.
half SQSampleAO(float3 positionWS, half3 normalWS)
{
    // Push the sample point off the surface so a face never occludes itself.
    float3 p = positionWS + normalWS * _SQ_AOParams.y;

    float sumVis = 0.0;
    float sumW = 0.0;

    [unroll]
    for (int i = 0; i < 8; ++i)
    {
        float w = saturate(dot(normalWS, _SQ_AODirs[i].xyz));
        if (w <= 0.001) continue; // direction is behind the surface

        float vis = 1.0;
        float4 sp = mul(_SQ_AOMatrices[i], float4(p, 1.0));
        float2 uv = sp.xy;
        if (all(uv >= 0.0) && all(uv <= 1.0))
        {
            float mapDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_SQ_AOAtlas, sampler_SQ_AOAtlas, uv, i, 0).r;
            float refDepth = saturate((dot(p - _SQ_AOOrigins[i].xyz, -_SQ_AODirs[i].xyz) - _SQ_AOOrigins[i].w) * _SQ_AODirs[i].w);
            vis = step(refDepth - _SQ_AOParams.z, mapDepth);
        }

        sumVis += vis * w;
        sumW += w;
    }

    return (half)(sumW > 0.0 ? sumVis / sumW : 1.0);
}

#endif
