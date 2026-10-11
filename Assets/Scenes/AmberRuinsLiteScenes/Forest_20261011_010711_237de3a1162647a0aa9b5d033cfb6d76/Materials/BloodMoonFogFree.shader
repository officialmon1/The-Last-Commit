Shader "AmberRuinsLite/SkyURP" {
 Properties{ _Color("Color",Color)=(.7,.12,.2,1) _MainTex("Moon texture",2D)="white"{} }
 SubShader{ Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Pass{
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);CBUFFER_START(UnityPerMaterial) float4 _Color; CBUFFER_END
 V vert(A a){V v;v.p=TransformObjectToHClip(a.p.xyz);v.uv=a.uv;return v;}
 half4 frag(V v):SV_Target{return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*_Color;}
 ENDHLSL
 }} }