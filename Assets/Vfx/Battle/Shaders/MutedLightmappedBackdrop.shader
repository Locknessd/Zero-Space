Shader "Battle/Muted Lightmapped Backdrop"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _LightMap ("LightMap", 2D) = "white" {}
        _LightStrength ("Light Strength", Range(0,5)) = 1
        _Emission ("Emission", Range(0,1)) = 1
        _Saturation ("Backdrop Saturation", Range(0,1)) = .58
        _Contrast ("Backdrop Contrast", Range(0,1)) = .86
        _Exposure ("Backdrop Exposure", Range(0,1.5)) = .88
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_FOG_COORDS(2) };
            sampler2D _MainTex, _LightMap;
            float4 _MainTex_ST, _LightMap_ST;
            half4 _Color;
            half _LightStrength, _Emission, _Saturation, _Contrast, _Exposure;
            Output vert(Input v)
            {
                Output o; o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.uv2 = TRANSFORM_TEX(v.uv2, _LightMap);
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            half4 frag(Output i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv) * (tex2D(_LightMap, i.uv2) * _LightStrength + _Emission) * _Color;
                half luminance = dot(col.rgb, half3(.2126,.7152,.0722));
                col.rgb = lerp(luminance.xxx, col.rgb, _Saturation);
                col.rgb = max(0, (col.rgb - .35) * _Contrast + .35) * _Exposure;
                UNITY_APPLY_FOG(i.fogCoord,col); return col;
            }
            ENDCG
        }
    }
}
