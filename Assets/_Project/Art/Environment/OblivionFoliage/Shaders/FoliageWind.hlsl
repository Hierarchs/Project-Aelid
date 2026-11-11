// Shared wind for the Oblivion foliage pack.
// Vertex color: R = bend weight, G = per-branch phase, B = leaf flutter.
#ifndef OBLIVION_FOLIAGE_WIND_INCLUDED
#define OBLIVION_FOLIAGE_WIND_INCLUDED

// Set by the OblivionFoliageWind component (all zero when no controller is active).
float4 _OF_GlobalWind;        // xyz = direction * strength multiplier, w = 1 when active
float  _OF_GlobalWindSpeed;   // speed multiplier

// objectOriginWS: pivot of the object, gives every placed copy its own timing.
float3 OF_ApplyWind(float3 positionWS, float3 normalWS, float4 color, float3 objectOriginWS,
                    float3 windDirection, float windStrength, float windSpeed,
                    float flutterStrength, float flutterSpeed, float time)
{
    float3 dir = float3(windDirection.x, 0, windDirection.z);
    float strength = windStrength;
    float speed = windSpeed;
    if (_OF_GlobalWind.w > 0.5)
    {
        float3 g = float3(_OF_GlobalWind.x, 0, _OF_GlobalWind.z);
        float l = length(g);
        dir = l > 1e-4 ? g / l : dir;
        strength *= l;
        speed *= max(_OF_GlobalWindSpeed, 0.01);
    }
    dir = normalize(dir + float3(1e-5, 0, 0));

    float phase = dot(objectOriginWS, float3(0.37, 0.0, 0.23)) + color.g * 6.2831;
    float t = time * speed;

    // Main bend: leans with the wind and rocks back and forth, with a slower gust layer.
    float gust = 0.75 + 0.25 * sin(t * 0.31 + phase * 0.5);
    float sway = (0.55 + 0.35 * sin(t + phase) + 0.15 * sin(t * 2.7 + phase * 1.3)) * gust;
    float bendAmount = sway * strength * color.r;
    float3 bend = dir * bendAmount;
    bend.y -= 0.35 * abs(bendAmount) * color.r;   // tips dip slightly as they bend

    // Leaf flutter: small fast wobble along the leaf normal, fades out when there is no wind.
    float fl = sin(time * flutterSpeed * (0.8 + 0.4 * color.g) + dot(positionWS, float3(2.1, 1.7, 2.9)) + color.g * 31.0);
    float windOn = saturate(strength / max(windStrength, 1e-4));
    float3 flutter = (normalWS * 0.7 + float3(0, 0.3, 0)) * fl * flutterStrength * color.b * windOn;

    return positionWS + bend + flutter;
}
#endif
