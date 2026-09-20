Shader "CounterMine/Arena Sky"
{
    Properties
    {
        _Zenith ("Upper sky", Color) = (.24, .43, .64, 1)
        _Horizon ("Horizon haze", Color) = (.72, .80, .85, 1)
        _Ground ("Below horizon", Color) = (.38, .40, .40, 1)
        _SunColor ("Sun", Color) = (1, .87, .68, 1)
        _SunDirection ("Direction to sun", Vector) = (.3838, .7431, -.5481, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _Ground, _SunColor;
                float4 _SunDirection;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.direction = TransformObjectToWorldDir(input.positionOS, false);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                half height = saturate(direction.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, height * (2 - height));
                sky = lerp(sky, _Ground.rgb, saturate(-direction.y * 4));
                float sunAngle = dot(direction, normalize(_SunDirection.xyz));
                half glow = smoothstep(.94, 1, sunAngle) * .09h;
                half disc = smoothstep(.99980, .99995, sunAngle);
                return half4(lerp(sky + glow * _SunColor.rgb, _SunColor.rgb, disc), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
