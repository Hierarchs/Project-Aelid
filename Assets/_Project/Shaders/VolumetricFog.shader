// Volumetric height fog with light shafts. Driven by VolumetricFogFeature.
// Pass 0 ray-marches at half resolution; pass 1 upsamples (depth-aware) and
// blends onto the camera colour as colour * transmittance + in-scatter.
// Pixel rows: _FogOrientation.x says whether depth row 0 is the screen top,
// queried from RenderGraph (never UNITY_UV_STARTS_AT_TOP).
Shader "Hidden/Aelid/VolumetricFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_FLOAT(_FogDepthTexture);
        TEXTURE2D(_FogTexture);

        float4x4 _FogInvViewProj;
        float4 _FogCamera;          // xyz position, w orthographic
        float4 _FogCameraForward;   // xyz
        float4 _FogSize;            // full width, full height, half width, half height
        float4 _FogOrientation;     // x depth row 0 is top, y colour rows are flipped relative to depth
        float4 _FogMedium;          // haze density, ground mist density, base height, 1 / falloff
        float4 _FogMedium2;         // top, morning-mist density, noise amount, 1 / noise scale
        float4 _FogMarch;           // wind drift x, wind drift z, steps, max opacity
        float4 _FogLight;           // anisotropy, sun scattering, ambient scattering, local-light scattering
        float4 _FogTint;
        float4 _FogAmbient;
        float4 _FogLights[8];       // xyz position, w 1 / range^2
        float4 _FogLightColors[8];  // rgb colour, w how lit (0..1)
        int _FogLightCount;
        float4 _FogClear;           // x clearing strength, y clearing radius as a fraction of range
        float _AelidMorningMist;    // 0..1, published by DayNightCycle

        struct FogAttributes { uint vertexID : SV_VertexID; };
        struct FogVaryings { float4 positionCS : SV_POSITION; };

        FogVaryings FogVert(FogAttributes input)
        {
            FogVaryings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            return output;
        }

        float RawDepth(int2 pixel)
        {
            pixel = clamp(pixel, int2(0, 0), int2(_FogSize.xy) - 1);
            return LOAD_TEXTURE2D(_FogDepthTexture, pixel).r;
        }

        // World position of a depth-texture pixel at a given device depth.
        float3 WorldAt(int2 pixel, float deviceDepth)
        {
            float2 uv = (pixel + 0.5) / _FogSize.xy;
            float ndcY = _FogOrientation.x > 0.5 ? 1.0 - 2.0 * uv.y : 2.0 * uv.y - 1.0;
            float4 h = mul(_FogInvViewProj, float4(uv.x * 2.0 - 1.0, ndcY, deviceDepth, 1.0));
            return h.xyz / h.w;
        }

        float EyeDepth(int2 pixel)
        {
            return dot(WorldAt(pixel, RawDepth(pixel)) - _FogCamera.xyz, _FogCameraForward.xyz);
        }
        ENDHLSL

        Pass
        {
            Name "FogMarch"
            Blend Off

            HLSLPROGRAM
            #pragma vertex FogVert
            #pragma fragment FragMarch
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            float Density(float3 p)
            {
                float top = _FogMedium2.x;
                float haze = _FogMedium.x * (1.0 - smoothstep(top - 4.0, top, p.y));
                float mist = (_FogMedium.y + _FogMedium2.y * _AelidMorningMist) * exp(-max(p.y - _FogMedium.z, 0.0) * _FogMedium.w);
                float d = (haze + mist) * step(p.y, top);
                if (_FogMedium2.z > 0.0)
                {
                    float3 q = (p - float3(_FogMarch.x, 0.0, _FogMarch.y)) * _FogMedium2.w * float3(1.0, 2.5, 1.0);
                    float n = ValueNoise(q) * 0.65 + ValueNoise(q * 2.03 + 17.0) * 0.35;
                    d *= lerp(1.0, saturate(n * 2.0 - 0.15), _FogMedium2.z);
                }

                // Lit lanterns, windows and fires burn a clear pocket in the fog
                // (same curve as VolumetricFogFeature.Clearing).
                if (_FogClear.x > 0.0)
                {
                    float clear = 0.0;
                    float invRadius2 = 1.0 / max(_FogClear.y * _FogClear.y, 1e-4);
                    for (int k = 0; k < _FogLightCount; k++)
                    {
                        float3 v = _FogLights[k].xyz - p;
                        float x = saturate(1.0 - dot(v, v) * _FogLights[k].w * invRadius2);
                        clear = max(clear, x * x * (3.0 - 2.0 * x) * _FogLightColors[k].w);
                    }
                    d *= 1.0 - _FogClear.x * clear;
                }
                return d;
            }

            // Henyey-Greenstein, scaled so isotropic scattering is 1.
            float Phase(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
                return (1.0 - g2) / (denom * sqrt(denom));
            }

            float3 LocalLight(float3 p)
            {
                float3 sum = 0;
                for (int k = 0; k < _FogLightCount; k++)
                {
                    float3 toLight = _FogLights[k].xyz - p;
                    float d2 = max(dot(toLight, toLight), 0.6); // soft core, no hot spot
                    float f = d2 * _FogLights[k].w;
                    float window = saturate(1.0 - f * f);
                    sum += _FogLightColors[k].rgb * (window * window / d2);
                }
                return sum;
            }

            float4 FragMarch(FogVaryings input) : SV_Target
            {
                int2 texel = int2(input.positionCS.xy);
                int2 pixel = min(texel * 2, int2(_FogSize.xy) - 1);
                float3 surface = WorldAt(pixel, RawDepth(pixel));
                float3 start = WorldAt(pixel, UNITY_NEAR_CLIP_VALUE);

                float3 ray = surface - start;
                float rayLength = length(ray);
                float3 dir = ray / max(rayLength, 1e-4);
                rayLength = min(rayLength, 400.0);

                // Clip the ray to the fog layer below Top.
                float top = _FogMedium2.x;
                float tStart = 0.0;
                if (start.y > top)
                {
                    if (dir.y >= -1e-4)
                        return float4(0, 0, 0, 1);
                    tStart = (start.y - top) / -dir.y;
                }
                float tEnd = rayLength;
                if (dir.y > 1e-4)
                    tEnd = min(tEnd, (top - start.y) / dir.y);
                if (tStart >= tEnd)
                    return float4(0, 0, 0, 1);

                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * Phase(dot(dir, mainLight.direction), _FogLight.x) * _FogLight.y;
                float3 ambient = _FogAmbient.rgb * _FogLight.z;
                float local = _FogLight.w;

                int steps = (int)_FogMarch.z;
                float ds = (tEnd - tStart) / steps;
                // Fixed mid-step samples: no per-pixel or per-frame noise to turn into glyph flicker.
                const float jitter = 0.5;

                float3 scattered = 0;
                float transmittance = 1.0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 p = start + dir * (tStart + (i + jitter) * ds);
                    float sigma = Density(p);
                    if (sigma <= 1e-6)
                        continue;

                    float3 lit = sun * MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    #if defined(_LIGHT_COOKIES)
                        lit *= SampleMainLightCookie(p);
                    #endif
                    lit += ambient;
                    if (_FogLightCount > 0)
                        lit += LocalLight(p) * local;

                    float stepT = exp(-sigma * ds);
                    scattered += transmittance * lit * (1.0 - stepT);
                    transmittance *= stepT;
                    if (transmittance < 0.01)
                        break;
                }

                scattered *= _FogTint.rgb;
                float opacity = 1.0 - transmittance;
                float maxOpacity = _FogMarch.w;
                if (opacity > maxOpacity)
                {
                    scattered *= maxOpacity / opacity;
                    transmittance = 1.0 - maxOpacity;
                }
                return float4(scattered, transmittance);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FogComposite"
            // colour = colour * transmittance + in-scatter
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma vertex FogVert
            #pragma fragment FragComposite

            float4 FragComposite(FogVaryings input) : SV_Target
            {
                int2 pixel = int2(input.positionCS.xy);
                if (_FogOrientation.y > 0.5)
                    pixel.y = int(_FogSize.y) - 1 - pixel.y;
                float eye = EyeDepth(pixel);

                // Bilinear footprint in the half-resolution fog grid, weighted by depth
                // similarity so fog does not bleed across tree and building edges.
                float2 t = (pixel + 0.5) * 0.5 - 0.5;
                int2 base = int2(floor(t));
                float2 f = t - base;
                int2 maxTexel = int2(_FogSize.zw) - 1;

                float4 sum = 0;
                float weightSum = 0;
                [unroll]
                for (int j = 0; j < 2; j++)
                {
                    [unroll]
                    for (int i = 0; i < 2; i++)
                    {
                        int2 q = clamp(base + int2(i, j), int2(0, 0), maxTexel);
                        float bilinear = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
                        float w = bilinear / (0.05 + abs(eye - EyeDepth(q * 2)));
                        sum += LOAD_TEXTURE2D(_FogTexture, q) * w;
                        weightSum += w;
                    }
                }
                return weightSum > 1e-6 ? sum / weightSum : float4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
