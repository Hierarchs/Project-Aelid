Shader "Aelid/Stone"
{
    Properties
    {
        _BaseColor("Stone Colour", Color) = (0.46, 0.45, 0.43, 1)
        _MineralTint("Warm Mineral Tint", Color) = (1.12, 1.02, 0.86, 1)
        _MossColor("Lichen Colour", Color) = (0.20, 0.27, 0.12, 1)
        _FractureScale("Fracture Frequency (per m)", Range(0.2, 5)) = 1.1
        _CrackWidth("Joint Width", Range(0.005, 0.1)) = 0.025
        _JointStrength("Joint Contrast", Range(0, 1)) = 0.65
        _LayerScale("Rock Layers (per m)", Range(0.2, 5)) = 1.5
        _LayerStrength("Cliff Strata", Range(0, 1)) = 0.72
        _GrainStrength("Mineral Grain", Range(0, 0.5)) = 0.16
        _ReliefStrength("Shallow Relief (m)", Range(0, 0.03)) = 0.012
        _MossAmount("Lichen Amount", Range(0, 1)) = 0.18
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _MineralTint, _MossColor;
            float _FractureScale, _CrackWidth, _JointStrength, _LayerScale, _LayerStrength;
            float _GrainStrength, _ReliefStrength, _MossAmount, _Smoothness;
        CBUFFER_END
        #include "StoneSurface.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float4 shadowCoord : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings StoneVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.shadowCoord = GetShadowCoord(position);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "StoneForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoneVert
            #pragma fragment StoneFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            half4 StoneFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 baseNormal = NormalizeNormalPerPixel(input.normalWS);
                StoneSample stone = EvaluateStone(input.positionWS, baseNormal);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = stone.albedo;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.positionCS = input.positionCS;
                data.normalWS = StoneReliefNormal(input.positionWS, baseNormal, stone.relief);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    data.shadowCoord = input.shadowCoord;
                #else
                    data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                data.bakedGI = SampleSH(data.normalWS);
                data.shadowMask = half4(1, 1, 1, 1);
                data.vertexLighting = VertexLighting(input.positionWS, data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half4 color = UniversalFragmentPBR(data, surface);
                return half4(MixFog(color.rgb, InitializeInputDataFog(float4(input.positionWS, 1), 0)), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoneVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoneVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings input) : SV_Target
            {
                half3 baseNormal = NormalizeNormalPerPixel(input.normalWS);
                StoneSample stone = EvaluateStone(input.positionWS, baseNormal);
                half3 normal = StoneReliefNormal(input.positionWS, baseNormal, stone.relief);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = StoneVert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - output.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(
                    ApplyShadowBias(output.positionWS, output.normalWS, lightDirection)));
                return output;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
