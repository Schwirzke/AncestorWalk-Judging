Shader "AncestorWalk/Person Photo"
{
 Properties{_MainTex("Photo",2D)="white"{} _Color("Tint",Color)=(1,1,1,1)}
 SubShader {Tags{"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"} Pass{
 Tags{"LightMode"="SRPDefaultUnlit"} Cull Back ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);half4 _Color;
 V vert(A a){V v;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);v.pos=TransformObjectToHClip(a.vertex.xyz);v.uv=a.uv;return v;}
 half4 frag(V v):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*_Color;}
 ENDHLSL
 } }
}
