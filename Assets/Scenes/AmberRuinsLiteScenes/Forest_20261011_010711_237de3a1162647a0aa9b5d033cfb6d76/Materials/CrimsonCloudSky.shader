Shader "AmberRuinsLite/CrimsonCloudSkyURP" {
 Properties {
  _Horizon("Fog horizon",Color)=(.24,.15,.19,1)
  _Crimson("Crimson sky",Color)=(.47,.16,.23,1)
  _Zenith("Plum zenith",Color)=(.094,.055,.133,1)
  _Cloud("Cloud color",Color)=(.15,.098,.137,1)
 }
 SubShader {
  Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Horizon, _Crimson, _Zenith, _Cloud;
   CBUFFER_END
   struct A { float4 p:POSITION; };
   struct V { float4 p:SV_POSITION; float3 direction:TEXCOORD0; };
   V vert(A a) { V v; v.p=TransformObjectToHClip(a.p.xyz); v.direction=a.p.xyz; return v; }
   float hash(float2 p) {
    float3 q=frac(float3(p.x,p.y,p.x)*.1031);
    q+=dot(q,q.yzx+33.33);
    return frac((q.x+q.y)*q.z);
   }
   float valueNoise(float2 p) {
    float2 i=floor(p), f=frac(p), u=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),u.x),
                lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),u.x),u.y);
   }
   half4 frag(V v):SV_Target {
    float3 d=normalize(v.direction);
    float altitude=saturate(d.y);
    float3 color=lerp(_Horizon.rgb,_Crimson.rgb,smoothstep(0,.22,altitude));
    color=lerp(color,_Zenith.rgb,smoothstep(.18,.92,altitude));
    float2 p=d.xz/max(.35,d.y+.55)*3.4;
    p+=float2(_Time.y*.002,_Time.y*.0007);
    float clouds=valueNoise(p)*.55+valueNoise(p*2.03+7.1)*.28+
                 valueNoise(p*4.07+19.3)*.14;
    float cover=smoothstep(.33,.73,clouds)*smoothstep(.025,.14,altitude);
    color=lerp(color,_Cloud.rgb,cover*.78);
    // A soft wine-colored glow surrounds the existing moon mesh.
    float glow=pow(saturate(dot(d,normalize(float3(8,105,178)))),12);
    color+=_Crimson.rgb*glow*.12*smoothstep(0,.12,altitude);
    return half4(color,1);
   }
   ENDHLSL
  }
 }
}