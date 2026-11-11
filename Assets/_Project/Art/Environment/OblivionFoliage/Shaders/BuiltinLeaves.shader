// Only used by the Built-in render pipeline (URP uses "OblivionFoliage/URP Foliage Wind").
Shader "OblivionFoliage/Builtin Leaves Two-Sided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGBA)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        [Header(Wind)]
        _WindDirection ("Wind Direction (XZ)", Vector) = (1, 0, 0.35, 0)
        _WindStrength ("Bend Strength (m at tips)", Range(0, 1)) = 0.12
        _WindSpeed ("Bend Speed", Range(0, 5)) = 1.3
        _FlutterStrength ("Leaf Flutter (m)", Range(0, 0.1)) = 0.015
        _FlutterSpeed ("Leaf Flutter Speed", Range(0, 30)) = 9
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" "DisableBatching"="LODFading" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert alphatest:_Cutoff addshadow vertex:vert
        #include "FoliageWind.hlsl"

        sampler2D _MainTex;
        fixed4 _Color;
        float4 _WindDirection;
        float _WindStrength, _WindSpeed, _FlutterStrength, _FlutterSpeed;

        struct Input { float2 uv_MainTex; };

        void vert (inout appdata_full v)
        {
            float3 posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 nWS = UnityObjectToWorldNormal(v.normal);
            float3 originWS = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            posWS = OF_ApplyWind(posWS, nWS, v.color, originWS, _WindDirection.xyz, _WindStrength, _WindSpeed,
                                 _FlutterStrength, _FlutterSpeed, _Time.y);
            v.vertex.xyz = mul(unity_WorldToObject, float4(posWS, 1)).xyz;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
