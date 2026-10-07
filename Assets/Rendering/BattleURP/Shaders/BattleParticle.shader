Shader "Battle/URP/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,3)) = 1
        [Toggle] _UseFontColor ("Comic font channel colors", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _Intensity;
                half _UseFontColor;
            CBUFFER_END
            struct Attributes
            {
                float4 position : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                half4 faceColor : TEXCOORD1;
                half4 edgeColor : TEXCOORD2;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half3 faceColor : TEXCOORD1;
                half3 edgeColor : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.color = input.color * _TintColor;
                output.uv = TRANSFORM_TEX(input.uv.xy, _MainTex);
                output.faceColor = input.faceColor.rgb * _TintColor.rgb;
                output.edgeColor = input.edgeColor.rgb * _TintColor.rgb;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = texel * input.color;
                if (_UseFontColor > .5h)
                    color.rgb = texel.b * input.color.rgb +
                        texel.g * input.faceColor + texel.r * input.edgeColor;
                color.rgb *= _Intensity;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
