// Writes each renderer's ASCII surface class (taken from its Rendering Layer
// bits) into an R8 target. Drawn as an override material over the opaque
// renderers that carry a class bit, depth-tested against the camera depth.
Shader "Hidden/Aelid/AsciiSurfaceId"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "AsciiSurfaceId"
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Bits 1..7 encode the class (AsciiSurfaceLayers.AllClassBits);
                // bit 0 is URP's Default layer, bits 8+ are left for light layers.
                uint bits = GetMeshRenderingLayer() & 0xFEu;
                uint surfaceClass = bits != 0u ? firstbitlow(bits) : 0u;
                return float4(surfaceClass / 255.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
