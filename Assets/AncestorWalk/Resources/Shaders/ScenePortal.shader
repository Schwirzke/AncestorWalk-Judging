Shader "AncestorWalk/Scene Portal"
{
    Properties { _Opacity("Opacity",Range(0,1))=1 _Seed("Phase",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            float _Opacity,_Seed;
            V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=(i.uv-.5)*2;float r=length(p),a=atan2(p.y,p.x),t=_Time.y+_Seed;
                float ring=exp(-abs(r-.79)*105),halo=exp(-abs(r-.79)*18)*.35;
                float outer=exp(-abs(r-.89)*140)*(.45+.35*sin(a*18-t));
                float swirl=pow(saturate(sin(a*4+r*22-t*1.7)),5)*.14*smoothstep(.12,.7,r);
                float arcs=pow(saturate(sin(a*3+t*.8)),12)*exp(-abs(r-.75)*60);
                float inside=1-smoothstep(.73,.79,r);
                half3 teal=half3(.08,.88,.76),gold=half3(1,.68,.2);
                half3 color=half3(.015,.035,.065)*inside+teal*(swirl+halo+ring*.8)+gold*(arcs+outer);
                float alpha=saturate(inside*.93+ring+halo+outer+arcs)*_Opacity;
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
