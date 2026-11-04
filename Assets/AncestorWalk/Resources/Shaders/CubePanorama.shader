Shader "AncestorWalk/Cube Panorama"
{
    Properties
    {
        _MainTex ("Panorama (latitude-longitude)", 2D) = "white" {}
        _SecondTex ("Next year", 2D) = "white" {}
        _Blend ("Year blend", Range(0,1)) = 0
        _Opacity ("Node visibility", Range(0,1)) = 1
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
        _ControlPositive ("Positive face shading", Vector) = (0,0,0,0)
        _ControlNegative ("Negative face shading", Vector) = (0,0,1,0)
        _SecondLift ("Next horizon", Float) = 12
        _PreviousA ("Previous first", 2D) = "white" {}
        _PreviousB ("Previous second", 2D) = "white" {}
        _PreviousBlend ("Previous year blend", Float) = 0
        _PreviousLiftA ("Previous first horizon", Float) = 12
        _PreviousLiftB ("Previous second horizon", Float) = 12
        _Transition ("Replacement fade", Range(0,1)) = 1
        _Tint ("Tint", Color) = (.5,.5,.5,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Rotation", Range(0,360)) = 0
        _SpinSpeed ("Sphere spin (degrees/second)", Float) = 3
        _SpinStartTime ("Spin speed change time", Float) = 0
        [HideInInspector] _CompletionStart ("Generation completed time", Float) = -100
        [HideInInspector] _CompletionAmount ("Completion bulge", Float) = .06
        [HideInInspector] _CompletionDuration ("Completion seconds", Float) = .65
        _MipBias ("Panorama detail bias", Range(-1,0)) = -.75
        _HorizonLift ("Scenery lift (degrees)", Range(-25,25)) = 12
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite [_ZWrite] ZTest LEqual
        Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _SecondTex, _PreviousA, _PreviousB;
            float _PreviousBlend,_PreviousLiftA,_PreviousLiftB,_Transition;
            float _Blend, _SecondLift, _Opacity;
            float4 _ControlPositive,_ControlNegative;
            half4 _Tint;
            float _Exposure, _Rotation, _HorizonLift, _SpinSpeed, _SpinStartTime, _MipBias;
            float _CompletionStart, _CompletionAmount, _CompletionDuration;
            struct appdata { float3 normal : NORMAL; float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; float3 normalOS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Animate only the sphere surface; its UI, collider and connectors stay fixed.
                float angle=radians(_Rotation+(_Time.y-_SpinStartTime)*_SpinSpeed), s=sin(angle), c=cos(angle);
                float3 rotated=float3(c*v.vertex.x-s*v.vertex.z,v.vertex.y,s*v.vertex.x+c*v.vertex.z);
                float completion=saturate((_Time.y-_CompletionStart)/max(_CompletionDuration,.001));
                float bulge=1+_CompletionAmount*sin(UNITY_PI*completion);
                o.vertex=UnityObjectToClipPos(float4(rotated*bulge,1));
                o.direction=float3(v.vertex.x,v.vertex.y,-v.vertex.z);o.normalOS=v.normal;
                return o;
            }
            half3 Panorama(sampler2D image,float2 uv,float2 dx,float2 dy,float lift)
            {
                // Apply the horizon warp's derivative to the mip footprint as well.
                float stretch=1-(lift*UNITY_PI/180)*cos(UNITY_PI*uv.y);
                uv.y-=(lift/180)*sin(UNITY_PI*uv.y);
                dx.y*=stretch;dy.y*=stretch;
                half3 color=tex2Dgrad(image,uv,dx,dy).rgb;
                float weight=.5*(1-smoothstep(0,.035,min(uv.x,1-uv.x)))*smoothstep(.55,.72,uv.y);
                [branch] if(weight>0)color=lerp(color,tex2Dgrad(image,float2(1-uv.x,uv.y),float2(-dx.x,dx.y),float2(-dy.x,dy.y)).rgb,weight);
                return color;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 direction=normalize(i.direction);
                float2 uv=float2(.5-atan2(direction.z,direction.x)/(2*UNITY_PI),1-acos(clamp(direction.y,-1,1))/UNITY_PI);
                // Compute gradients before branches. Longitude wraps from 1 to 0;
                // unwrap that jump so it never selects the blurriest mip at the seam.
                float2 dx=ddx(uv),dy=ddy(uv);
                dx.x-=round(dx.x);dy.x-=round(dy.x);
                float detail=exp2(clamp(_MipBias,-1,0));dx*=detail;dy*=detail;
                half3 color;
                [branch] if(_Blend<=0)color=Panorama(_MainTex,uv,dx,dy,_HorizonLift);
                else if(_Blend>=1)color=Panorama(_SecondTex,uv,dx,dy,_SecondLift);
                else color=lerp(Panorama(_MainTex,uv,dx,dy,_HorizonLift),Panorama(_SecondTex,uv,dx,dy,_SecondLift),_Blend);
                if(_Transition<1)
                {
                    half3 previous;
                    [branch] if(_PreviousBlend<=0)previous=Panorama(_PreviousA,uv,dx,dy,_PreviousLiftA);
                    else if(_PreviousBlend>=1)previous=Panorama(_PreviousB,uv,dx,dy,_PreviousLiftB);
                    else previous=lerp(Panorama(_PreviousA,uv,dx,dy,_PreviousLiftA),Panorama(_PreviousB,uv,dx,dy,_PreviousLiftB),_PreviousBlend);
                    color=lerp(previous,color,_Transition);
                }
                // Darken the front for text; retain contrasting sides and top.
                float faceShade=lerp(.85,.16,saturate(dot(max(i.normalOS,0),_ControlPositive.xyz)+dot(max(-i.normalOS,0),_ControlNegative.xyz)));
                faceShade*=lerp(.85,1,saturate(i.normalOS.y*.5+.5));
                return half4(color * faceShade,_Opacity);
            }
            ENDCG
        }
    }
}
