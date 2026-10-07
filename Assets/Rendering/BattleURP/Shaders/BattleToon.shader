Shader "Battle/URP/Toon Surface"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _Color ("Base tint", Color) = (1,1,1,1)
        _ShadowTint ("Colored shadow", Color) = (.42,.51,.64,1)
        _BandCenter ("Light band center", Range(-1,1)) = .15
        _BandSoftness ("Light band softness", Range(.01,.8)) = .18
        _RimColor ("Rim tint", Color) = (.65,.8,1,1)
        _RimStrength ("Rim strength", Range(0,.5)) = .08
        _Smoothness ("Highlight sharpness", Range(0,1)) = .35
        _SpecularStrength ("Highlight strength", Range(0,1)) = .06
        _Ambient ("Ambient contribution", Range(0,1)) = .4
        _HitFlash ("Contact highlight", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff", Range(0,1)) = .01
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _BaseMap ("Depth texture", 2D) = "white" {}
        [HideInInspector] _BaseColor ("Depth color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowTint;
                half4 _RimColor;
                half _BandCenter;
                half _BandSoftness;
                half _RimStrength;
                half _Smoothness;
                half _SpecularStrength;
                half _Ambient;
                half _HitFlash;
                half _Cutoff;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(base.a - _Cutoff);
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light key = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half band = smoothstep(_BandCenter - _BandSoftness, _BandCenter + _BandSoftness,
                    dot(normal, key.direction));
                band *= lerp(.3h, 1.h, key.shadowAttenuation);
                half3 lighting = lerp(_ShadowTint.rgb, half3(1,1,1), band);
                lighting *= lerp(half3(1,1,1), key.color, .5h);
                lighting += SampleSH(normal) * _Ambient;
                #ifdef _ADDITIONAL_LIGHTS
                uint count = min(GetAdditionalLightsCount(), 4u);
                for (uint i = 0u; i < count; ++i)
                {
                    Light fill = GetAdditionalLight(i, input.positionWS);
                    lighting += fill.color * saturate(dot(normal, fill.direction))
                        * fill.distanceAttenuation * .3h;
                }
                #endif
                half3 color = base.rgb * lighting;
                half specular = pow(saturate(dot(normal, normalize(key.direction + view))),
                    lerp(12.h, 96.h, _Smoothness));
                color += specular * _SpecularStrength * key.color * key.shadowAttenuation;
                half rim = pow(1.h - saturate(dot(normal, view)), 3.h);
                color += rim * _RimColor.rgb * _RimStrength * smoothstep(-.3h,.5h,dot(normal,key.direction));
                color = lerp(color, half3(1.25h,1.12h,.85h), _HitFlash * .65h);
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    Fallback Off
}
