Shader "CounterMine/RadarHighlight"
{
 Properties { _Color ("Color", Color) = (1,.22,.06,.5) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+200" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   ZTest Always
   ZWrite Off
   Cull Back
   Blend SrcAlpha OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Color;
   CBUFFER_END
   struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
   struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
   Varyings Vert(Attributes input)
   {
    Varyings o;
    o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return o;
   }
   half4 Frag(Varyings i) : SV_Target
   {
    half rim = pow(1 - saturate(abs(dot(normalize(i.normalWS), GetWorldSpaceNormalizeViewDir(i.positionWS)))), 2);
    return half4(lerp(_Color.rgb, half3(1,.8,.3), rim), lerp(_Color.a, .95, rim));
   }
   ENDHLSL
  }
 }
}
