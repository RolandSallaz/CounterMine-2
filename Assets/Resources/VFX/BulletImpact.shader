Shader "CounterMine/BulletImpact"
{
 Properties { _Color ("Color", Color) = (1,1,1,1) }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass {
 Tags { "LightMode"="SRPDefaultUnlit" }
 Cull Off
 ZWrite Off
 Blend SrcAlpha OneMinusSrcAlpha
 Offset -1, -1
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _Color;
 CBUFFER_END
 struct Attributes { float4 positionOS : POSITION; };
 struct Varyings { float4 positionCS : SV_POSITION; };
 Varyings Vert(Attributes input) { Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); return output; }
 half4 Frag(Varyings input) : SV_Target { return _Color; }
 ENDHLSL
 }
 }
}
