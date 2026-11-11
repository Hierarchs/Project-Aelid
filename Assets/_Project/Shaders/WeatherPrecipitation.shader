Shader "Aelid/Weather Precipitation"
{
    Properties
    {
        [HDR] _WeatherTint("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WeatherTint;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 edge = abs(input.uv * 2.0 - 1.0);
                half alpha = (1.0 - smoothstep(0.3, 1.0, edge.x)) * (1.0 - smoothstep(0.45, 1.0, edge.y));
                return half4(_WeatherTint.rgb, _WeatherTint.a * alpha);
            }
            ENDHLSL
        }
    }
}
