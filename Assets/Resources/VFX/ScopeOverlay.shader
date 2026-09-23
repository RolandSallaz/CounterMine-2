Shader "CounterMine/ScopeOverlay"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - .5) * float2(_ScreenParams.x / _ScreenParams.y, 1);
                float pixel = 1 / _ScreenParams.y;
                float radius = length(p);
                float rim = smoothstep(.445, .457, radius);
                float hair = 1 - smoothstep(pixel * .45, pixel * 1.15, min(abs(p.x), abs(p.y)));
                float post = (1 - smoothstep(pixel * 1.8, pixel * 2.8, min(abs(p.x), abs(p.y)))) * smoothstep(.10, .12, max(abs(p.x), abs(p.y)));
                float tickX = (1 - smoothstep(pixel, pixel * 1.6, abs(frac(p.x / .045 + .5) - .5) * .045)) * (1 - smoothstep(.006, .008, abs(p.y)));
                float tickY = (1 - smoothstep(pixel, pixel * 1.6, abs(frac(p.y / .045 + .5) - .5) * .045)) * (1 - smoothstep(.006, .008, abs(p.x)));
                float ticks = max(tickX, tickY) * (1 - smoothstep(.19, .20, radius));
                float alpha = max(rim, max(hair, max(post, ticks)));
                alpha = max(alpha, smoothstep(.34, .445, radius) * .18);
                return fixed4(.008,.012,.009,alpha);
            }
            ENDCG
        }
    }
}
