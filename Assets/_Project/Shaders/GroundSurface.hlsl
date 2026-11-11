#ifndef AELID_GROUND_SURFACE_INCLUDED
#define AELID_GROUND_SURFACE_INCLUDED

#include "GroundLayout.hlsl"

float GroundHash(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float GroundNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GroundHash(i), GroundHash(i + float2(1, 0)), f.x),
                lerp(GroundHash(i + float2(0, 1)), GroundHash(i + 1.0), f.x), f.y);
}

float GroundPathDistance(float2 p)
{
    float distance = 10000.0;
    [unroll] for (int i = 0; i < AELID_GROUND_PATH_COUNT; i++)
    {
        float2 a = kGroundPaths[i].xy, ab = kGroundPaths[i].zw - a;
        float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-5));
        distance = min(distance, length(p - a - t * ab));
    }
    return distance;
}

// Shared by the lit surface and the ASCII ID pass, including both old grass/dirt buckets.
float GroundGrassCoverage(float2 p)
{
    if (_UseWorldLayout < 0.5) return saturate(_GrassAmount);
    float edgeNoise = GroundNoise(p * 1.35) - 0.5;
    float width = kGroundPathHalfWidth + (GroundNoise(p * 0.24 + 19.0) - 0.5) * 0.5;
    float grass = smoothstep(width - 0.25, width + _BlendWidth, GroundPathDistance(p) + edgeNoise * 0.65);
    float ruins = 1.0 - smoothstep(5.5, 8.0, distance(p, kGroundRuins));
    float ruinsGrass = smoothstep(0.30, 0.72, GroundNoise(p * 0.63 + 7.0));
    return grass * lerp(1.0, ruinsGrass, ruins);
}

half3 GroundAlbedo(float2 p, float grass)
{
    float broad = GroundNoise(p * 0.23);
    float clump = GroundNoise(p * 1.7 + 11.0);
    float grain = GroundNoise(p * 13.0);

    // Several metres of colour variation, smaller tufts, and sparse dry tips.
    half3 grassColor = lerp(_GrassDark.rgb, _GrassLight.rgb, saturate(broad * 0.75 + clump * 0.25));
    float dry = smoothstep(0.60, 0.83, GroundNoise(p * 0.51 - 23.0));
    grassColor = lerp(grassColor, _DryGrass.rgb, dry * 0.45);
    float2 bladeUV = p * float2(6.0, 10.0);
    float2 bladeCell = floor(bladeUV), blade = frac(bladeUV);
    float seed = GroundHash(bladeCell);
    float lean = (seed - 0.5) * 0.7;
    float bladeDistance = abs(blade.x - 0.5 - (blade.y - 0.5) * lean);
    float aa = max(fwidth(bladeUV.x), 0.01);
    float stroke = (1.0 - smoothstep(0.035, 0.035 + aa, bladeDistance))
                 * smoothstep(0.0, 0.18, blade.y) * (1.0 - smoothstep(0.65 + seed * 0.2, 1.0, blade.y));
    grassColor *= 0.90 + clump * 0.16 + stroke * _DetailStrength * 0.3;

    // Broad dry/damp soil patches, compact clods, pores and occasional small stones.
    half3 soil = lerp(_SoilDark.rgb, _SoilLight.rgb, broad * 0.6 + clump * 0.4);
    soil *= 1.0 + (grain - 0.5) * _DetailStrength * 0.45;
    float2 pebbleUV = p * 4.0, cell = floor(pebbleUV);
    float pebbleSeed = GroundHash(cell + 61.0);
    float2 pebbleCenter = float2(GroundHash(cell + 13.0), GroundHash(cell + 97.0)) * 0.5 + 0.25;
    float radius = lerp(0.08, 0.18, pebbleSeed);
    float pebble = (1.0 - smoothstep(radius, radius + max(length(fwidth(pebbleUV)), 0.01),
                                   length(frac(pebbleUV) - pebbleCenter))) * step(0.78, pebbleSeed);
    soil = lerp(soil, _PebbleColor.rgb, pebble * _DetailStrength);
    return lerp(soil, grassColor, grass) * _BaseColor.rgb;
}

#endif
