Shader "CounterMine/MuzzleFlash"
{
    Properties
    {
        _Intensity ("Brightness", Float) = 1
        _Age ("Age", Float) = 0
        _Duration ("Flash Duration", Float) = 0.055
        _Seed ("Variation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _Age, _Duration, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 kind : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 color : COLOR; float alive : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                bool spark = input.kind.x > 1.5;
                float life = spark ? .14 : max(.01, _Duration);
                float remaining = saturate(1 - _Age / life);
                float3 position = input.positionOS.xyz * lerp(.25, 1.0, remaining);
                if (spark)
                {
                    float angle = input.kind.y * 2.39996 + _Seed * 6.28318;
                    float speed = 9 + frac(input.kind.y * .731 + _Seed) * 14;
                    position += float3(cos(angle) * speed * _Age, sin(angle) * speed * _Age - 18 * _Age * _Age, _Age * 8);
                }
                output.positionCS = TransformObjectToHClip(position);
                output.color = input.color.rgb * _Intensity;
                output.alive = remaining;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(min(input.alive, _Intensity) - .001);
                return half4(input.color, 1);
            }
            ENDHLSL
        }
    }
}
