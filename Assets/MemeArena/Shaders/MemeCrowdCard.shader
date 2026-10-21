Shader "MemeArena/Crowd Card"
{
    Properties
    {
        _BaseMap("Crowd atlas", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _IdleSway("Idle motion", Float) = 0.035
        _ReactionType("Reaction", Float) = 0
        _ReactionStart("Reaction start", Float) = -100
        _ReactionDuration("Reaction duration", Float) = 0
        _ReactionStrength("Reaction strength", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _IdleSway;
                float _ReactionType;
                float _ReactionStart;
                float _ReactionDuration;
                float _ReactionStrength;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 center : TEXCOORD1;
                float2 seed : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float phase = input.seed.x * 6.283185;
                float age = _Time.y - _ReactionStart - frac(input.seed.x * 3.17) * 0.22;
                float active = step(0, age) * step(age, _ReactionDuration) * saturate((_ReactionDuration-age)*3);
                float sad = step(2.5, _ReactionType) * (1-step(3.5, _ReactionType)) + step(4.5, _ReactionType);
                float jump = (1-sad) * active * _ReactionStrength * 0.3;
                float t = _Time.y * (2.2 + frac(input.seed.x)*1.1) + phase;
                float3 centerWS = TransformObjectToWorld(input.center);
                centerWS.y += abs(sin(t)) * (_IdleSway + jump);
                float3 direction = GetCameraPositionWS() - centerWS;
                direction.y = 0;
                float3 right = normalize(cross(float3(0,1,0), direction) + float3(0.00001,0,0));
                float2 offset = input.positionOS.xy - input.center.xy;
                float2 scale = float2(length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21));
                offset *= scale;
                offset.y *= 1 - sad * active * 0.14;
                float roll = atan2(unity_ObjectToWorld._m10, length(unity_ObjectToWorld._m00_m20));
                roll += sad * active * sin(t) * 0.12;
                offset = mul(float2x2(cos(roll), -sin(roll), sin(roll), cos(roll)), offset);
                float3 world = centerWS + right * offset.x + float3(0,1,0) * offset.y;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(color.a - 0.4);
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
    }
}
