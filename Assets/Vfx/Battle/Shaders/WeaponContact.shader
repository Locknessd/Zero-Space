Shader "Battle/Weapon Contact"
{
    Properties { _TintColor ("Contact tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+40" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _TintColor;
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Output vert(Input v) { Output o; o.position = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }
            fixed4 frag(Output i) : SV_Target { return fixed4(_TintColor.rgb, _TintColor.a * i.color.a); }
            ENDCG
        }
    }
}
