Shader "CounterMine/ScopePictureInPicture"
{
    Properties
    {
        _ScopeTex ("Scope View", 2D) = "black" {}
        _ScopeActive ("Magnified View", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ScopeTex); SAMPLER(sampler_ScopeTex);
            float _ScopeActive;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - .5;
                float radius = length(p);
                float aa = max(max(fwidth(p.x), fwidth(p.y)), .0001);
                // At the hip the eyepiece is coated glass, with a subdued sky reflection.
                // It is deliberately independent of the PiP texture, which is rendered only in ADS.
                float highlight = 1 - smoothstep(.045, .34, length((p - float2(-.19,.21)) * float2(1,1.6)));
                float arc = 1 - smoothstep(.012,.045,abs(radius - .41));
                half3 glass = lerp(half3(.012,.027,.036), half3(.065,.105,.12), saturate(input.uv.y * .65 + highlight * .5));
                glass += highlight * half3(.06,.105,.12) + arc * .012;
                glass *= 1 - smoothstep(.3,.51,radius) * .62;
                if (_ScopeActive < .5) return half4(glass, 1);
                float hair = 1 - smoothstep(aa * .3, aa * 1.05, min(abs(p.x), abs(p.y)));
                float post = (1 - smoothstep(aa * 1.25, aa * 2.1, min(abs(p.x), abs(p.y)))) * smoothstep(.21,.23,max(abs(p.x),abs(p.y)));
                float tickX = (1 - smoothstep(aa * .4, aa * 1.1, abs(frac(p.x / .07 + .5) - .5) * .07)) * (1 - smoothstep(.012,.018,abs(p.y)));
                float tickY = (1 - smoothstep(aa * .4, aa * 1.1, abs(frac(p.y / .07 + .5) - .5) * .07)) * (1 - smoothstep(.012,.018,abs(p.x)));
                float ticks = max(tickX,tickY) * (1 - smoothstep(.28,.30,radius));
                half3 scene = SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, input.uv).rgb;
                scene *= 1 - smoothstep(.37,.5,radius) * .55;
                return half4(lerp(scene, half3(.007,.009,.008), saturate(max(hair,max(post,ticks)))), 1);
            }
            ENDHLSL
        }
    }
}
