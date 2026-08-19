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
TEXTURE3D(_SQ_AOVolume);              SAMPLER(sampler_SQ_AOVolume);

float4x4 _SQ_FakeLightVP;          // world -> sun map uv (scale-bias baked in)
float4   _SQ_FakeLightDir;         // xyz = surface->light, normalized
float4   _SQ_FakeLightColor;       // rgb * intensity, linear
float4   _SQ_FakeShadowRayOrigin;  // xyz = light plane origin (virtual cam pos)
// x = depth bias (normalized-depth units), y = normal bias (world units, scaled to texel by C#),
// z = 1 / sunMapSize (uv per texel), w = sunMapSize
float4   _SQ_FakeShadowParams;
// x = near, y = 1 / (far - near), z = EVSM warp exponent (0 = none)
float4   _SQ_FakeShadowDepthParams;
// x = blur passes (informational), y = penumbra radius in texels (informational),
// z = variance floor relative to mean² (the moments live in the warped domain,
// where an absolute floor is meaningless), w = light-bleed reduction floor
float4   _SQ_FakeShadowPcfParams;
// x = minimum lighting floor, y = ambient floor, z = AO strength, w = sun test mode (0 = coverage, 1 = depth compare)
float4   _SQ_LightingParams;

// AO: a world-space directional-visibility volume baked by SideQuest.Lighting's
// VoxelAOSystem (GPU voxelize or CPU raycast producer — same packed format).
// RGBA8: rgb = visibility moment * 2 + 0.5, a = mean visibility.
float4   _SQ_AOVolumeOrigin;       // xyz = grid min corner (world), w = normal offset (m)
float4   _SQ_AOVolumeParams;       // xyz = 1 / grid world size, w = contrast power (>= 1)

// 1 = fully lit, 0 = fully shadowed.
//
// The map is an EXPONENTIAL variance shadow map (R = min exp(c·depth), G = its
// square, both blurred at bake time by FakeLightRenderer), so the penumbra is
// evaluated with ONE bilinear tap through Chebyshev's inequality — perfectly
// smooth at any softness. The stochastic Poisson PCF this replaced sampled a
// wide disk with a per-pixel random rotation, and at 8 taps over a 300%-softness
// radius every pixel got its own noisy estimate: the grain the penumbra used to
// show WAS that sampling. The exp warp (c in _SQ_FakeShadowDepthParams.z) is
// what stops plain VSM's light bleed: around an object's base the blur mixes the
// object's depth into the floor texels, the floor sits barely behind that mean,
// and the unwarped bound brightens a ring exactly where AO contact-darkens.
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
    float rawDepth = (rayDistance - _SQ_FakeShadowDepthParams.x) * _SQ_FakeShadowDepthParams.y;
    // Beyond the far plane = untracked geometry outside the fitted volume; clamping
    // it onto the far plane would fabricate proximity to far casters, so treat as lit.
    if (rawDepth > 1.0) return 1.0;
    float refDepth = max(rawDepth, 0.0);
    refDepth -= _SQ_FakeShadowParams.x * (1.0 + slope)
              + _SQ_FakeShadowParams.y * _SQ_FakeShadowDepthParams.y;

    float2 moments = SAMPLE_TEXTURE2D_LOD(_SQ_FakeShadowMap, sampler_SQ_FakeShadowMap, uv, 0).rg;

    float warp = _SQ_FakeShadowDepthParams.z;
    float wEmpty = warp > 0.0 ? exp(warp) : 1.0;
    if (_SQ_LightingParams.w < 0.5)
    {
        // Coverage mode: any caster texel shadows. Only usable when receivers
        // are excluded from the caster set — every tracked primitive is both,
        // so depth compare is the default here (unlike the Airbus original).
        return moments.x < 0.999 * wEmpty ? 0.0 : 1.0;
    }

    // Into the warped domain the moments live in.
    float wRef = warp > 0.0 ? exp(warp * refDepth) : refDepth;

    // In front of (or at) the mean occluder depth = lit. This also keeps every
    // empty-map texel (cleared to the warped far value) fully lit for free.
    if (wRef <= moments.x) return 1.0;

    // Variance floor is RELATIVE to the mean squared — warped values span many
    // orders of magnitude, so an absolute floor would be wrong at both ends.
    float varianceFloor = moments.x * moments.x * _SQ_FakeShadowPcfParams.z;
    float variance = max(moments.y - moments.x * moments.x, varianceFloor);
    float d = wRef - moments.x;
    float pMax = variance / (variance + d * d);

    // Light-bleed reduction: Chebyshev is an UPPER bound and overlapping casters
    // push it off zero deep inside real shadow — remap the low end to black.
    float floorP = _SQ_FakeShadowPcfParams.w;
    return (half)saturate((pMax - floorP) / (1.0 - floorP));
}

// Directional ambient visibility from the baked voxel volume, one trilinear tap.
// Evaluates saturate(mean + 2 * dot(moment, N)) so a crease darkens each face by
// ITS exposure — a wall and the floor beside it read different values from the
// same voxel. bentNormalWS points toward the open sky and should steer ambient
// (SampleSH); it must never be used for direct light.
void SQSampleAOVolume(float3 positionWS, half3 normalWS, out half visibility, out half3 bentNormalWS)
{
    visibility = 1.0h;
    bentNormalWS = normalWS;

    // Offset along the normal so the tap reads the air just off the surface,
    // not the surface's own voxel.
    float3 uvw = (positionWS + normalWS * _SQ_AOVolumeOrigin.w - _SQ_AOVolumeOrigin.xyz)
               * _SQ_AOVolumeParams.xyz;
    if (any(uvw < 0.0) || any(uvw > 1.0)) return; // outside the volume = open

    float4 packed = SAMPLE_TEXTURE3D_LOD(_SQ_AOVolume, sampler_SQ_AOVolume, uvw, 0);
    float3 moment = (packed.rgb - 0.5) * 0.5;
    // Alpha stores contact visibility as a SPHERE mean with near hits excluded by
    // distance — but distance exclusion cannot remove the surface's own grazing
    // rays (they skim along the plane and land beyond any threshold), so the raw
    // mean still carries a uniform baseline everywhere (measured: open floor and
    // cube contact within 1 luminance point of each other). The receiver knows its
    // normal: projecting the openness moment onto it reconstructs THIS surface's
    // hemisphere — an open floor saturates to 1 (its moment points straight up),
    // while creases and contacts stay down because their moment is small or
    // points across the surface.
    float contact = saturate(packed.a + 2.0 * dot(moment, (float3)normalWS));

    // Orientation term from the full-hit moment: 1 where the surface faces the
    // open direction, dark where it faces INTO the blockage (undersides, the
    // inside faces of tight corners). Zero moment (open air) means no opinion.
    float orientation = 1.0 - saturate(-4.0 * dot(moment, (float3)normalWS));

    float vis = min(orientation, contact);

    // Contrast power deepens the midtones. The max guard matters: a stale or
    // uninitialized w of 0 would make pow() return 1 and silently disable AO.
    visibility = (half)pow(abs(vis), max(1.0, _SQ_AOVolumeParams.w));

    float m2 = dot(moment, moment);
    if (m2 > 1e-5)
    {
        float3 bent = moment * rsqrt(m2);
        // Guard: a bent normal pointing behind the surface is noise, not signal.
        if (dot(bent, (float3)normalWS) > 0.0) bentNormalWS = (half3)bent;
    }
}

#endif
