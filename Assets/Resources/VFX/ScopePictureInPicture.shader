Shader "CounterMine/ScopePictureInPicture"
{
    Properties { _ScopeTex ("Scope View", 2D) = "black" {} }
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - .5;
                float radius = length(p);
                float aa = max(max(fwidth(p.x), fwidth(p.y)), .0001);
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
