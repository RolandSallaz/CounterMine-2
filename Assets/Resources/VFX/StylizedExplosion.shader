Shader "CounterMine/StylizedExplosion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Screen-door dissolve keeps overlapping smoke volumes correctly depth-sorted.
                float2 p = floor(i.positionCS.xy);
                float threshold = frac(52.9829189 * frac(dot(p, float2(.06711056, .00583715))));
                clip(i.color.a - max(.001, threshold));
                return half4(i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
