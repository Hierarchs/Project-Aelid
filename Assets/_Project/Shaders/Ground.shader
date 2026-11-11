Shader "Aelid/Ground"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _GrassDark("Grass Shadow Colour", Color) = (0.12, 0.27, 0.085, 1)
        _GrassLight("Grass Sunlit Colour", Color) = (0.34, 0.49, 0.17, 1)
        _DryGrass("Dry Grass Tips", Color) = (0.48, 0.43, 0.22, 1)
        _SoilDark("Damp Soil", Color) = (0.25, 0.18, 0.11, 1)
        _SoilLight("Dry Soil", Color) = (0.50, 0.38, 0.24, 1)
        _PebbleColor("Pebbles", Color) = (0.48, 0.45, 0.36, 1)
        _DetailStrength("Surface Detail", Range(0, 1)) = 0.65
        _BlendWidth("Path Shoulder Width (m)", Range(0.1, 2)) = 0.85
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        [Toggle] _UseWorldLayout("Follow Vertical Slice Paths", Float) = 1
        _GrassAmount("Grass Amount (World Layout Off)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _GrassDark, _GrassLight, _DryGrass, _SoilDark, _SoilLight, _PebbleColor;
            float _DetailStrength, _BlendWidth, _Smoothness, _UseWorldLayout, _GrassAmount;
        CBUFFER_END
        #include "GroundSurface.hlsl"

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
            half fogFactor : TEXCOORD2;
            float4 shadowCoord : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings GroundVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.fogFactor = ComputeFogFactor(position.positionCS.z);
            output.shadowCoord = GetShadowCoord(position);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "GroundForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GroundVert
            #pragma fragment GroundFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            half4 GroundFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = GetAbsolutePositionWS(input.positionWS).xz;
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = GroundAlbedo(p, GroundGrassCoverage(p));
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.positionCS = input.positionCS;
                data.normalWS = NormalizeNormalPerPixel(input.normalWS);
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
                return half4(MixFog(color.rgb, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "GroundSurfaceId"
            Tags { "LightMode" = "AelidGroundSurfaceId" }
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GroundVert
            #pragma fragment GroundId
            #pragma multi_compile_instancing
            half4 GroundId(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = GetAbsolutePositionWS(input.positionWS).xz;
                float coverage = GroundGrassCoverage(p);
                // World-locked 12.5 cm coverage dithering; the same blended edge as the albedo.
                float surfaceClass = coverage > GroundHash(floor(p * 8.0)) ? 6.0 : 0.0;
                return half4(surfaceClass / 255.0, 0, 0, 1);
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
            #pragma vertex GroundVert
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
            #pragma vertex GroundVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings input) : SV_Target
            {
                half3 normal = NormalizeNormalPerPixel(input.normalWS);
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
                Varyings output = GroundVert(input);
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
