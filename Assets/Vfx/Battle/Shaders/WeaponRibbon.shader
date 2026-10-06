Shader "Battle/Weapon Ribbon"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o; o.position = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = v.uv; return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float width = saturate(i.uv.x);
                float edge = smoothstep(.8, .94, width);
                float alpha = smoothstep(0, .22, width) * (1 - smoothstep(.975, 1, width));
                return fixed4(lerp(i.color.rgb, fixed3(1, .98, .9), edge), i.color.a * alpha);
            }
            ENDCG
        }
    }
}
