Shader "Battle/URP/Lightmapped Backdrop"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _LightMap ("Authored lightmap", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _LightStrength ("Lightmap contribution", Range(0,3)) = 1
        _Emission ("Base contribution", Range(0,1)) = .35
        _Saturation ("Saturation", Range(0,1)) = .65
        _Contrast ("Contrast", Range(0,1)) = .82
        _Exposure ("Exposure", Range(0,2)) = .85
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_LightMap);
            SAMPLER(sampler_LightMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _LightMap_ST;
                half4 _Color;
                half _LightStrength;
                half _Emission;
                half _Saturation;
                half _Contrast;
                half _Exposure;
            CBUFFER_END
            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                half fog : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.uv2 = TRANSFORM_TEX(input.uv2, _LightMap);
                output.fog = ComputeFogFactor(output.position.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * _Color.rgb;
                color *= SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, input.uv2).rgb * _LightStrength + _Emission;
                half gray = dot(color, half3(.2126h,.7152h,.0722h));
                color = lerp(gray.xxx, color, _Saturation);
                color = max(0, (color - .35h) * _Contrast + .35h) * _Exposure;
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
