Shader "AncestorWalk/Photo Thumbnail"
{
 Properties { _MainTex("Photo",2D)="white"{} _Aspect("Aspect",Float)=1 _Orientation("EXIF",Float)=1 _TransparentPadding("Transparent padding",Float)=0 }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" } Pass {
 ZTest Always Cull Off ZWrite Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 vertex:POSITION;float2 uv:TEXCOORD0;};struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);float _Aspect,_Orientation,_TransparentPadding;
 V vert(A a){V v;v.pos=TransformObjectToHClip(a.vertex.xyz);v.uv=a.uv;return v;}
 half4 frag(V v):SV_Target{
 float2 uv=v.uv;if(_Aspect>1)uv.y=(uv.y-.5)*_Aspect+.5;else uv.x=(uv.x-.5)/_Aspect+.5;
 if(any(uv<0)||any(uv>1))return half4(.04,.06,.065,1-_TransparentPadding);
 int o=(int)_Orientation;
 if(o==2)uv.x=1-uv.x;else if(o==3)uv=1-uv;else if(o==4)uv.y=1-uv.y;
 else if(o==5)uv=float2(1-uv.y,1-uv.x);else if(o==6)uv=float2(1-uv.y,uv.x);else if(o==7)uv=uv.yx;else if(o==8)uv=float2(uv.y,1-uv.x);
 return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);}
 ENDHLSL
 } }
}
