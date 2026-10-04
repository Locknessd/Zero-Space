Shader "Battle/Soft Smoke"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p=(i.uv-.5)*2;
                float ripple=sin(p.x*12+p.y*6)*sin(p.y*11-p.x*4)*.075;
                float density=1-smoothstep(.25,.96,length(p)+ripple);
                return fixed4(i.color.rgb, i.color.a*density*density);
            }
            ENDCG
        }
    }
}
