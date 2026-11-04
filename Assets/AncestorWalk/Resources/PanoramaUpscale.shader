Shader "AncestorWalk/Panorama Upscale"
{
    Properties { _MainTex("Original panorama",2D)="white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;float4 _SourceSize;
            float Weight(float x)
            {
                x=abs(x);
                return x<=1?(1.5*x-2.5)*x*x+1:x<2?((-.5*x+2.5)*x-4)*x+2:0;
            }
            half3 Sample(float2 pixel){return tex2Dlod(_MainTex,float4((pixel+.5)*_SourceSize.zw,0,0)).rgb;}
            half4 frag(v2f_img i):SV_Target
            {
                float2 pixel=i.uv*_SourceSize.xy-.5,base=floor(pixel),fraction=pixel-base;
                half3 color=0,low=1,high=0;
                [unroll] for(int y=-1;y<=2;y++)[unroll] for(int x=-1;x<=2;x++)
                {
                    half3 sample=Sample(base+float2(x,y));color+=sample*(Weight(x-fraction.x)*Weight(y-fraction.y));
                    if(x>=0&&x<=1&&y>=0&&y<=1){low=min(low,sample);high=max(high,sample);}
                }
                // Restrained detail enhancement, clamped to local colors to avoid bright halos.
                half3 blur=(Sample(pixel+float2(-1,0))+Sample(pixel+float2(1,0))+Sample(pixel+float2(0,-1))+Sample(pixel+float2(0,1)))*.25;
                color=clamp(color+.12*(color-blur),low,high);
                return half4(saturate(color),1);
            }
            ENDCG
        }
    }
}
