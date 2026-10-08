Shader "Battle/Assassin Ribbon"
{
    Properties
    {
        _OccludedAlpha ("Slash visibility over the fighter", Range(0, 1)) = .3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        // A short thrust can put the entire blade behind the receiver's body.
        // Keep a soft stroke there while the regular pass draws the bright edge
        // wherever the real swept blade is visible. Both use the same mesh.
        Pass
        {
            Name "Body overlap"
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _OccludedAlpha;
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o; o.position = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = v.uv; return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float width = saturate(i.uv.x);
                float alpha = smoothstep(0, .22, width) * (1 - smoothstep(.975, 1, width));
                return fixed4(i.color.rgb, i.color.a * alpha * _OccludedAlpha);
            }
            ENDCG
        }
        UsePass "Battle/Weapon Ribbon/Ribbon"
    }
}
