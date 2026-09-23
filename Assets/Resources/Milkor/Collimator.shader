Shader "CounterMine/Collimator"
{
    Properties
    {
        [HDR] _ReticleColor ("Reticle Color", Color) = (4, 0.08, 0.025, 1)
        _GlassColor ("Glass Tint", Color) = (0.08, 0.24, 0.26, 0.055)
        _DotRadius ("Dot Angular Radius (radians)", Range(0.0001, 0.01)) = 0.0009
        _RingRadius ("Ring Angular Radius (radians)", Range(0.001, 0.05)) = 0.012
        _RingThickness ("Ring Angular Thickness", Range(0.0001, 0.005)) = 0.00065
        _RingOpacity ("Ring Opacity", Range(0, 1)) = 0.65
        _Brightness ("Reticle Brightness", Range(0, 5)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Collimator"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ReticleColor, _GlassColor;
                float _DotRadius, _RingRadius, _RingThickness, _RingOpacity, _Brightness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Lens mesh uses metres, +Z down the optical axis, +Y up.
                // Angular projection places the reticle at optical infinity: translating
                // the eye moves it across the lens without gluing it to the glass.
                float3 ray = input.positionWS - GetCameraPositionWS();
                float3 forward = normalize(TransformObjectToWorldDir(float3(0,0,1)));
                float3 right = normalize(TransformObjectToWorldDir(float3(1,0,0)));
                float3 up = normalize(TransformObjectToWorldDir(float3(0,1,0)));
                float depth = dot(ray, forward);
                float2 angular = float2(dot(ray, right), dot(ray, up)) / max(abs(depth), 0.0001);
                float radius = length(angular);
                float aa = max(fwidth(radius), 0.00001);
                float dotMask = 1 - smoothstep(_DotRadius - aa, _DotRadius + aa, radius);
                float ring = 1 - smoothstep(_RingThickness * 0.5 - aa, _RingThickness * 0.5 + aa, abs(radius - _RingRadius));
                float visible = step(0.0001, depth) * smoothstep(0.15, 0.45, depth / max(length(ray), 0.0001));
                half reticle = saturate(max(dotMask, ring * _RingOpacity) * visible * _ReticleColor.a) * saturate(_Brightness);
                half glass = saturate(_GlassColor.a);
                half alpha = glass + reticle * (1 - glass);
                half3 color = _GlassColor.rgb * glass * (1 - reticle) + _ReticleColor.rgb * reticle * _Brightness;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
