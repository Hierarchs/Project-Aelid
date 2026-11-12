#ifndef AELID_STONE_SURFACE_INCLUDED
#define AELID_STONE_SURFACE_INCLUDED

float _AelidAsciiCleanBaseline;

float StoneHash(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float StoneNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(StoneHash(i), StoneHash(i + float2(1, 0)), f.x),
                lerp(StoneHash(i + float2(0, 1)), StoneHash(i + 1.0), f.x), f.y);
}

// Distance between the nearest two irregular cells: narrow seams, not a square grid.
float StoneJointDistance(float2 p)
{
    float2 cell = floor(p), local = frac(p);
    float nearest = 100.0, second = 100.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y);
            float2 seed = float2(StoneHash(cell + offset), StoneHash(cell + offset + 73.0));
            float2 delta = offset + 0.2 + seed * 0.6 - local;
            float d = dot(delta, delta);
            second = min(second, max(nearest, d));
            nearest = min(nearest, d);
        }
    }
    return sqrt(second) - sqrt(nearest);
}

float3 StonePlane(float2 p)
{
    float2 warp = float2(StoneNoise(p * 0.6), StoneNoise(p * 0.6 + 41.0)) - 0.5;
    float seam = StoneJointDistance(p * _FractureScale + warp * 0.35);
    float aa = max(fwidth(seam), 0.003);
    float joint = 1.0 - smoothstep(_CrackWidth, _CrackWidth + aa, seam);
    float mineral = StoneNoise(p * 0.48 + 13.0);
    float grit = StoneNoise(p * 9.0 + 5.0);
    // Stop sub-pixel mineral grain turning into high-frequency camera noise.
    float grainVisibility = 1.0 - saturate(length(fwidth(p * 9.0)));
    return float3(joint, mineral, lerp(0.5, grit, grainVisibility));
}

struct StoneSample
{
    half3 albedo;
    float relief;
};

StoneSample EvaluateStone(float3 positionWS, half3 normalWS)
{
    float3 p = GetAbsolutePositionWS(positionWS);
    float3 weights = pow(abs(normalWS), 4.0);
    weights /= max(weights.x + weights.y + weights.z, 1e-5);
    // All three projections use metres; cliffs do not stretch an XZ-only texture.
    // ('texture' is a reserved word in HLSL; DX12 refuses it as a variable name.)
    float3 planes = StonePlane(p.zy) * weights.x
                  + StonePlane(p.xz) * weights.y
                  + StonePlane(p.xy) * weights.z;
    float joint = planes.x;
    float mineral = planes.y;

    // Wavy bedding planes share world height, but mainly affect steep natural faces.
    float layerCoordinate = p.y * _LayerScale + (StoneNoise(p.xz * 0.34) - 0.5) * 0.65;
    float layerDistance = abs(frac(layerCoordinate) - 0.5);
    float layerAA = max(fwidth(layerCoordinate), 0.004);
    float bedding = 1.0 - smoothstep(0.035, 0.035 + layerAA, layerDistance);
    float cliff = 1.0 - smoothstep(0.35, 0.85, abs(normalWS.y));
    float layers = cliff * _LayerStrength;
    float sediment = 0.5 + 0.5 * sin(layerCoordinate * 6.2831853);

    half3 rock = _BaseColor.rgb * lerp(0.76, 1.23, mineral);
    rock = lerp(rock, rock * _MineralTint.rgb, smoothstep(0.45, 0.80, mineral) * 0.5);
    float grainStrength = _GrainStrength * lerp(1.0, 0.15, _AelidAsciiCleanBaseline);
    rock *= 1.0 + (planes.z - 0.5) * grainStrength;
    rock *= 1.0 + (sediment - 0.5) * layers * 0.22;
    rock *= 1.0 - joint * _JointStrength * 0.55 - bedding * layers * 0.28;

    // Restrained lichen on upward ledges and in joints; no vegetation geometry.
    float lichen = smoothstep(0.54, 0.78, StoneNoise(p.xz * 0.7 + p.y * 0.11));
    float ledge = saturate(normalWS.y);
    float moss = saturate(lichen * _MossAmount * (ledge * 0.65 + joint * 0.7));
    StoneSample result;
    result.albedo = lerp(rock, _MossColor.rgb, moss);
    result.relief = (mineral - 0.5) * 0.15 - joint * _JointStrength - bedding * layers * 0.45;
    return result;
}

// Surface-gradient relief changes lighting/normals, leaving silhouettes and collision intact.
half3 StoneReliefNormal(float3 positionWS, half3 normalWS, float relief)
{
    float3 n = normalize(normalWS);
    float3 dx = ddx(positionWS), dy = ddy(positionWS);
    float3 rx = cross(dy, n), ry = cross(n, dx);
    float determinant = dot(dx, rx);
    float inverse = abs(determinant) > 1e-8 ? 1.0 / determinant : 0.0;
    float reliefStrength = _ReliefStrength * (1.0 - _AelidAsciiCleanBaseline);
    float3 gradient = (ddx(relief) * rx + ddy(relief) * ry) * inverse * reliefStrength;
    gradient *= min(1.0, 0.45 / max(length(gradient), 1e-5));
    return normalize(n - gradient);
}

#endif
