Shader "CounterMine/SafeZoneBarrier"
{
 Properties { _Tint ("Team color", Color) = (.15,.65,1,1) _RevealDistance ("Reveal distance", Float) = 5 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Tint;
   float _RevealDistance;
   CBUFFER_END
   struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
   struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
   Varyings Vert(Attributes v)
   {
    Varyings o;
    o.world = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(o.world);
    o.uv = v.uv;
    return o;
   }
   half4 Frag(Varyings i) : SV_Target
   {
    float proximity = 1 - smoothstep(.4, max(.5, _RevealDistance), distance(i.world, _WorldSpaceCameraPos));
    float2 edge = min(i.uv, 1-i.uv);
    float rim = 1-smoothstep(.004, .018, min(edge.x,edge.y));
    float2 gridUV = i.uv * 20;
    float2 grid = abs(frac(gridUV-.5)-.5) / max(fwidth(gridUV), .001);
    float lines = 1-saturate(min(grid.x,grid.y));
    float scan = pow(saturate(.5+.5*sin(i.world.y*5-_Time.y*2)), 16);
    float alpha = proximity * (.07 + lines*.17 + rim*.5 + scan*.045);
    return half4(_Tint.rgb * (1+rim*.5), alpha * _Tint.a);
   }
   ENDHLSL
  }
 }
}
