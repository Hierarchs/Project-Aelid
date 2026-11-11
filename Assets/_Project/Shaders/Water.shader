// Stylised water for the hybrid ASCII renderer. Output is the lit 3D water
// colour; the ASCII pass turns it into horizontal line glyphs afterwards.
//   - planar reflection (PlanarReflection.cs) distorted by waves and ripples
//   - refraction of the lake bed with depth-based absorption
//   - seamless, softly foamed shoreline from the camera depth texture
//   - a reflected moon disc, sun/moon glints, ripple rings, and fog
// Rendered in the transparent queue so it can read the opaque depth/colour.
Shader "Aelid/Water"
{
    Properties
    {
        [Header(Colour)]
        _DeepColor("Deep Colour", Color) = (0.008, 0.018, 0.036, 1)
        _ShallowTint("Shallow Tint (multiplies the bed)", Color) = (0.48, 0.64, 0.70, 1)
        _DepthFade("Depth Fade (m)", Float) = 0.24
        _ShoreSoftness("Shore Softness (m)", Float) = 0.08

        [Header(Reflection and Refraction)]
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.9
        _ReflectionDistortion("Reflection Distortion", Range(0, 0.1)) = 0.025
        _RefractionDistortion("Refraction Distortion", Range(0, 0.1)) = 0.02

        [Header(Waves)]
        _WaveScale("Wave Scale", Float) = 0.9
        _WaveSpeed("Wave Speed", Float) = 0.7
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.22
        _SpecularColor("Glint Colour", Color) = (1, 1, 1, 1)
        _Shininess("Glint Sharpness", Range(8, 1024)) = 280

        [Header(Foam and Ripples)]
        _FoamColor("Foam Colour (A = amount)", Color) = (0.63, 0.76, 0.83, 0.42)
        _FoamWidth("Shore Foam Width (m)", Float) = 0.1
        _RippleSpeed("Ripple Speed (m/s)", Float) = 1.15
        _RippleWidth("Ripple Ring Width (m)", Float) = 0.16
        _RippleLifetime("Ripple Lifetime (s)", Float) = 2.6
        _RippleStrength("Ripple Strength", Float) = 1.6

        [HideInInspector] _PlanarReflectionTexture("Reflection", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowTint;
                half4 _SpecularColor;
                half4 _FoamColor;
                float _DepthFade;
                float _ShoreSoftness;
                float _ReflectionStrength;
                float _ReflectionDistortion;
                float _RefractionDistortion;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveStrength;
                float _Shininess;
                float _FoamWidth;
                float _RippleSpeed;
                float _RippleWidth;
                float _RippleLifetime;
                float _RippleStrength;
            CBUFFER_END

            TEXTURE2D(_PlanarReflectionTexture);
            SAMPLER(sampler_PlanarReflectionTexture);

            // Globals from WaterRipples.cs
            float4 _WaterRipples[16]; // xy: position (xz), z: start time, w: strength
            float _WaterTime;
            float4 _WaterMoonDisc; // xy: world XZ, z: radius, w: visibility above horizon

            static const float2 kWaveDirs[4] = { float2(0.96, 0.28), float2(-0.51, 0.86), float2(0.20, -0.98), float2(-0.91, -0.41) };
            static const float kWaveFreq[4] = { 1.0, 1.73, 2.9, 4.6 };
            static const float kWaveAmp[4] = { 0.30, 0.22, 0.16, 0.10 };

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float EyeDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // Surface slope (d height / dx, d height / dz) of four drifting wave trains.
            float2 WaveSlope(float2 p, float t)
            {
                float2 slope = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float k = kWaveFreq[i] * _WaveScale;
                    float phase = dot(kWaveDirs[i], p) * k + t * _WaveSpeed * sqrt(k) * 2.0 + i * 1.7;
                    slope += kWaveDirs[i] * (cos(phase) * kWaveAmp[i]);
                }
                return slope * _WaveStrength;
            }

            // Expanding rings from WaterRipples; crest is a 0..1 highlight on the ring fronts.
            float2 RippleSlope(float2 p, out float crest)
            {
                float2 slope = 0.0;
                crest = 0.0;
                [loop] for (int i = 0; i < 16; i++)
                {
                    float4 r = _WaterRipples[i];
                    float age = _WaterTime - r.z;
                    if (r.w <= 0.0 || age < 0.0 || age > _RippleLifetime)
                        continue;
                    float2 d = p - r.xy;
                    float dist = length(d);
                    float x = (dist - age * _RippleSpeed) / _RippleWidth;
                    float envelope = exp(-x * x * 0.35);
                    float life = 1.0 - age / _RippleLifetime;
                    float amp = r.w * life * life;
                    slope += (d / max(dist, 1e-3)) * (-sin(x * 2.2) * envelope * amp);
                    crest += saturate(cos(x * 2.2)) * envelope * amp;
                }
                return slope;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = input.positionWS;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
                float t = _WaterTime;

                float crest;
                float2 slope = WaveSlope(positionWS.xz, t) + RippleSlope(positionWS.xz, crest) * _RippleStrength * 0.15;
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));

                // Water depth below this point, measured vertically.
                float surfaceEye = -TransformWorldToView(positionWS).z;
                float viewThickness = max(EyeDepth(SampleSceneDepth(screenUV)) - surfaceEye, 0.0);
                float depth = viewThickness * abs(viewDir.y);
                float shore = saturate(depth / max(_ShoreSoftness, 1e-3));

                // Refraction: the bed seen through the surface. Never pull in
                // things standing above the water (characters, banks).
                float2 refractUV = screenUV + normalWS.xz * _RefractionDistortion * shore;
                if (EyeDepth(SampleSceneDepth(refractUV)) < surfaceEye)
                    refractUV = screenUV;
                float3 bed = SampleSceneColor(refractUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float3 light = mainLight.color * mainLight.shadowAttenuation;
                float3 ambient = SampleSH(float3(0.0, 1.0, 0.0));
                float3 body = _DeepColor.rgb * (ambient + light * 0.35);
                float absorb = 1.0 - exp(-depth / max(_DepthFade, 1e-3));
                float3 color = lerp(bed * _ShallowTint.rgb, body, absorb);

                // Reflection. The mirror camera is a rotation, so its image is
                // mirrored horizontally relative to a true reflection.
                float2 reflectUV = float2(1.0 - screenUV.x, screenUV.y) - normalWS.xz * _ReflectionDistortion;
                float3 reflection = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, reflectUV).rgb;
                float ndv = saturate(dot(normalWS, viewDir));
                float fresnel = 0.5 + 0.5 * pow(1.0 - ndv, 4.0); // stylised: top-down water still mirrors
                color = lerp(color, reflection, _ReflectionStrength * fresnel * shore);

                // The orthographic camera needs a finite reflection footprint.
                // Anchor it to the pond and disturb it with the same waves and ripples.
                if (_WaterMoonDisc.w > 0.0)
                {
                    float2 moonOffset = positionWS.xz - _WaterMoonDisc.xy + slope * float2(3.5, 1.4);
                    float3 offset = float3(moonOffset.x, 0.0, moonOffset.y);
                    float axial = dot(offset, viewDir);
                    float radius = length(offset - viewDir * axial) / max(_WaterMoonDisc.z, 0.001);
                    float disc = 1.0 - smoothstep(0.78, 1.08, radius);
                    float halo = 1.0 - smoothstep(0.9, 1.65, radius);
                    color += mainLight.color * (disc * 1.15 + halo * 0.12) * _WaterMoonDisc.w * shore;
                }

                // Sun / moon glints.
                float3 halfDir = normalize(mainLight.direction + viewDir);
                color += _SpecularColor.rgb * light * pow(saturate(dot(normalWS, halfDir)), _Shininess) * shore;

                // Shore foam: one thin line that laps back and forth along the waterline.
                float lap = 0.5 + 0.5 * sin(t * 1.4 + positionWS.x * 0.7 + positionWS.z * 0.5);
                float edge = depth - _FoamWidth * 0.5 * lap;
                float foam = saturate(1.0 - abs(edge) / max(_FoamWidth * 0.5, 1e-3)) * shore;
                foam *= 0.6 + 0.4 * sin(positionWS.x * 1.3 - positionWS.z * 0.9 + t * 0.6); // break the line up a little
                foam = saturate(foam + crest * 0.9);
                color = lerp(color, _FoamColor.rgb * (ambient + light), foam * _FoamColor.a);

                // Fade into the untouched bed at the waterline: no hard edge.
                color = lerp(SampleSceneColor(screenUV), color, saturate(depth / 0.04));

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
