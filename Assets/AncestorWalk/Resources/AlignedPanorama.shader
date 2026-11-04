Shader "AncestorWalk/Aligned Panorama"
{
    Properties
    {
        _MainTex ("Panorama (latitude-longitude)", 2D) = "white" {}
        _SecondTex ("Next year", 2D) = "white" {}
        _Blend ("Year blend", Range(0,1)) = 0
        _SecondLift ("Next horizon", Float) = 12
        _PreviousA ("Previous first", 2D) = "white" {}
        _PreviousB ("Previous second", 2D) = "white" {}
        _PreviousBlend ("Previous year blend", Float) = 0
        _PreviousLiftA ("Previous first horizon", Float) = 12
        _PreviousLiftB ("Previous second horizon", Float) = 12
        _Transition ("Replacement fade", Range(0,1)) = 1
        _Tint ("Tint", Color) = (.5,.5,.5,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Opacity ("Panorama opacity", Range(0,1)) = 1
        _Rotation ("Rotation", Range(0,360)) = 0
        _HorizonLift ("Scenery lift (degrees)", Range(-25,25)) = 12
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex, _SecondTex, _PreviousA, _PreviousB;
            float _PreviousBlend,_PreviousLiftA,_PreviousLiftB,_Transition;
            float _Blend, _SecondLift;
            half4 _Tint;
            float _Exposure, _Rotation, _HorizonLift, _Opacity;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float angle=radians(_Rotation), s=sin(angle), c=cos(angle);
                float3 rotated=float3(c*v.vertex.x-s*v.vertex.z,v.vertex.y,s*v.vertex.x+c*v.vertex.z);
                o.vertex=UnityObjectToClipPos(float4(rotated,1));
                o.direction=v.vertex.xyz;
                return o;
            }
            half3 SkySample(sampler2D image,float2 uv)
            {
                half3 color=tex2D(image,uv).rgb;
                float edge=min(uv.x,1-uv.x);
                float weight=.5*(1-smoothstep(0,.035,edge))*smoothstep(.55,.72,uv.y);
                [branch] if(weight>0)color=lerp(color,tex2D(image,float2(1-uv.x,uv.y)).rgb,weight);
                return color;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 direction=normalize(i.direction);
                float2 uv=float2(.5-atan2(direction.z,direction.x)/(2*UNITY_PI),1-acos(clamp(direction.y,-1,1))/UNITY_PI);
                // Raise the scenery uniformly around the horizon without tilting it or moving the tracked floor.
                // Taper to zero at the poles so zenith/nadir remain intact and the longitude seam stays continuous.
                float2 originalUV=uv;
                float2 uv2=uv;
                uv2.y-=(_SecondLift/180)*sin(UNITY_PI*uv.y);
                uv.y-=(_HorizonLift/180)*sin(UNITY_PI*uv.y);
                half3 color=lerp(SkySample(_MainTex,uv),SkySample(_SecondTex,uv2),_Blend);
                if(_Transition<1)
                {
                    float2 oldA=originalUV,oldB=originalUV;
                    oldA.y-=(_PreviousLiftA/180)*sin(UNITY_PI*originalUV.y);
                    oldB.y-=(_PreviousLiftB/180)*sin(UNITY_PI*originalUV.y);
                    color=lerp(lerp(SkySample(_PreviousA,oldA),SkySample(_PreviousB,oldB),_PreviousBlend),color,_Transition);
                }
                // The XR projection layer composites premultiplied color over the room.
                return half4(color * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure * _Opacity,_Opacity);
            }
            ENDCG
        }
    }
}
