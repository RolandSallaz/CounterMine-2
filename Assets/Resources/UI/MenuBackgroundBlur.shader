Shader "CounterMine/MenuBackgroundBlur"
{
    Properties { [PerRendererData] _MainTex ("Map", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                // Low-resolution map plus a weighted 5x5 kernel. The character is
                // composited afterwards from a separate full-resolution camera.
                half3 color = 0;
                float weight = 0;
                [unroll] for (int x = -2; x <= 2; x++)
                [unroll] for (int y = -2; y <= 2; y++)
                {
                    float w = (3 - abs(x)) * (3 - abs(y));
                    color += tex2D(_MainTex, i.uv + float2(x,y) * _MainTex_TexelSize.xy * 1.5).rgb * w;
                    weight += w;
                }
                return half4(color / weight, 1) * i.color;
            }
            ENDHLSL
        }
    }
}
