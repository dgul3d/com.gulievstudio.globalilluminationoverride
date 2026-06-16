#ifndef GI_OVERRIDE_INCLUDED
#define GI_OVERRIDE_INCLUDED

// Define GIO_MAX_VOLUMES before including this file to override the limit (default: 8).
#ifndef GIO_MAX_VOLUMES
#define GIO_MAX_VOLUMES 8
#endif

#if !defined(SHADERGRAPH_PREVIEW)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#endif

// ─── Global shader data (uploaded by GIOverrideController) ───────────────────

// xyz = world-space center of each volume
float4 _GIOVolumes_Center[GIO_MAX_VOLUMES];

// xyz = world-space full size, w = blend smoothness (world units)
float4 _GIOVolumes_Size[GIO_MAX_VOLUMES];

// Rotation quaternion (x, y, z, w) for each volume — identity = (0, 0, 0, 1)
float4 _GIOVolumes_Rotation[GIO_MAX_VOLUMES];

// Active volume count (0 → pass-through to standard SH)
int _GIOVolumes_Count;

// Per-volume L2 SH coefficients packed in Unity's standard layout.
// Each element corresponds to the preset assigned to that volume slot.
float4 _GIOVolumes_SH_Ar[GIO_MAX_VOLUMES]; // L0+L1 red:   [w, x, y, dc]
float4 _GIOVolumes_SH_Ag[GIO_MAX_VOLUMES]; // L0+L1 green
float4 _GIOVolumes_SH_Ab[GIO_MAX_VOLUMES]; // L0+L1 blue
float4 _GIOVolumes_SH_Br[GIO_MAX_VOLUMES]; // L2 red:   [c4, c5, c6, c7]
float4 _GIOVolumes_SH_Bg[GIO_MAX_VOLUMES]; // L2 green
float4 _GIOVolumes_SH_Bb[GIO_MAX_VOLUMES]; // L2 blue
float4 _GIOVolumes_SH_C[GIO_MAX_VOLUMES];  // L2 final: [r, g, b, 1]

// ─── Helpers ─────────────────────────────────────────────────────────────────

// Build a rotation matrix from a unit quaternion (x, y, z, w).
// mul(m, localVec) → worldVec  |  mul(worldVec, m) → localVec  (R^T = R^-1)
float3x3 GIO_QuatToMatrix(float4 q)
{
    float x2 = q.x + q.x, y2 = q.y + q.y, z2 = q.z + q.z;
    float xx = q.x * x2, xy = q.x * y2, xz = q.x * z2;
    float yy = q.y * y2, yz = q.y * z2, zz = q.z * z2;
    float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;

    float3x3 m;
    m[0] = float3(1.0 - (yy + zz), xy - wz,         xz + wy);
    m[1] = float3(xy + wz,         1.0 - (xx + zz), yz - wx);
    m[2] = float3(xz - wy,         yz + wx,         1.0 - (xx + yy));
    return m;
}

// Signed distance to an oriented box (positive = outside, negative = inside).
// size is the full world-space extent along each local axis.
// rotation is a unit quaternion (x, y, z, w) transforming local → world.
float GIO_SDFOrientedBox(float3 worldPos, float3 center, float3 size, float4 rotation)
{
    float3x3 rotMatrix = GIO_QuatToMatrix(rotation);
    // mul(offset, rotMatrix) applies R^T → transforms world offset into local frame
    float3 localPos = mul(worldPos - center, rotMatrix);
    float3 q = abs(localPos) - size * 0.5;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Unity standard ambient SH (falls back to a constant in Shader Graph preview).
half3 GIO_SampleStandardSH(float3 normalWS)
{
#if defined(SHADERGRAPH_PREVIEW)
    return half3(0.05, 0.05, 0.1);
#else
    return SampleSH(normalWS);
#endif
}

// Evaluate the L2 SH stored at slot [i] for a given world-space normal.
half3 GIO_SampleVolumeSH(int i, float3 normalWS)
{
    half4 n = half4(normalWS, 1.0);

    half3 res;
    res.r = dot(_GIOVolumes_SH_Ar[i], n);
    res.g = dot(_GIOVolumes_SH_Ag[i], n);
    res.b = dot(_GIOVolumes_SH_Ab[i], n);

    half4 vB = n.xyzz * n.yzzx;
    res.r += dot(_GIOVolumes_SH_Br[i], vB);
    res.g += dot(_GIOVolumes_SH_Bg[i], vB);
    res.b += dot(_GIOVolumes_SH_Bb[i], vB);

    half vC = n.x * n.x - n.y * n.y;
    res += _GIOVolumes_SH_C[i].rgb * vC;

    return max(half3(0, 0, 0), res);
}

// ─── Main function ────────────────────────────────────────────────────────────

// Sample global illumination with GI override volumes.
// Overlapping volumes are blended proportionally by weight.
// Falls back to standard URP ambient SH outside all volumes.
half3 GIO_SampleGI(float3 worldPos, float3 normalWS)
{
    half3 standardSH = GIO_SampleStandardSH(normalWS);

    if (_GIOVolumes_Count == 0)
        return standardSH;

    half3 overrideSH = (half3)0;
    float totalWeight = 0.0;

    UNITY_LOOP
    for (int i = 0; i < _GIOVolumes_Count; i++)
    {
        float sdf = GIO_SDFOrientedBox(worldPos, _GIOVolumes_Center[i].xyz, _GIOVolumes_Size[i].xyz, _GIOVolumes_Rotation[i]);
        float smoothness = max(_GIOVolumes_Size[i].w, 0.0001);
        float weight = saturate(smoothstep(smoothness, -smoothness, sdf));

        overrideSH += GIO_SampleVolumeSH(i, normalWS) * weight;
        totalWeight += weight;
    }

    // Normalize blended SH so overlapping volumes don't overbrighten.
    if (totalWeight > 0.0)
        overrideSH /= totalWeight;

    // Blend with standard SH: fully inside at least one volume → pure override.
    float coverage = saturate(totalWeight);
    return lerp(standardSH, overrideSH, coverage);
}

// ─── Shader Graph wrappers ────────────────────────────────────────────────────
// Use these in a Custom Function node (File mode).
// Inputs:  WorldPos (Vector3), NormalWS (Vector3)
// Outputs: StandardSH (Vector3), BakedGI (Vector3)

void GIO_SampleGI_float(float3 worldPos, float3 normalWS,
    out float3 standardSH, out float3 bakedGI)
{
    standardSH = GIO_SampleStandardSH(normalWS);
    bakedGI    = GIO_SampleGI(worldPos, normalWS);
}

void GIO_SampleGI_half(half3 worldPos, half3 normalWS,
    out half3 standardSH, out half3 bakedGI)
{
    standardSH = GIO_SampleStandardSH(normalWS);
    bakedGI    = GIO_SampleGI(worldPos, normalWS);
}

#endif // GI_OVERRIDE_INCLUDED
