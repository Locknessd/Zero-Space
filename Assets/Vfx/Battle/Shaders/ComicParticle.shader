Shader "Battle/Comic Particle"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Output vert(Input v) { Output o; o.position = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }
            fixed4 frag(Output i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
