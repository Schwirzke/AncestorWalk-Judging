using UnityEngine;
namespace AncestorWalk
{
    // One-off GPU filtering on scene load. Original PNGs and sphere previews stay native.
    public sealed class PanoramaUpscaler : MonoBehaviour
    {
        public static bool Enabled=>false; // Owner disabled scene upscaling; retain code for a future opt-in.
        public const int Scale=2,MaximumDimension=8192,RowsPerFrame=64;
        Material material;
        public bool Available{get{Ensure();return material!=null&&material.shader.isSupported;}}
        void Ensure(){if(material!=null)return;var shader=Resources.Load<Shader>("PanoramaUpscale");if(shader!=null&&shader.isSupported)material=new Material(shader){name="Scene panorama 2x filter"};}
        public static Vector2Int Size(int width,int height,int hardwareLimit)
        {
            int limit=Mathf.Min(MaximumDimension,hardwareLimit);int w=Mathf.Min(width*Scale,limit);
            if(w<=width)return new Vector2Int(width,height);
            return new Vector2Int(w,Mathf.Max(1,Mathf.RoundToInt((float)height*w/width)));
        }
        public RenderTexture Create(Texture source)
        {
            if(!Available)return null;var size=Size(source.width,source.height,SystemInfo.maxTextureSize);if(size.x<=source.width)return null;
            var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
            {name="Historical panorama 2x",useMipMap=true,autoGenerateMips=false,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
            if(!target.Create()){Destroy(target);return null;}return target;
        }
        public void RenderRows(Texture source,RenderTexture target,int first,int rows)
        {
            Ensure();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            GL.PushMatrix();
            try
            {
                Graphics.SetRenderTarget(target);GL.LoadOrtho();GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;
                material.SetTexture("_MainTex",source);material.SetVector("_SourceSize",new Vector4(source.width,source.height,1f/source.width,1f/source.height));
                if(!material.SetPass(0))throw new System.InvalidOperationException("Panorama upscale shader unavailable.");
                float bottom=(float)first/target.height,top=(float)Mathf.Min(first+rows,target.height)/target.height;
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0,bottom);GL.Vertex3(0,bottom,0);GL.TexCoord2(1,bottom);GL.Vertex3(1,bottom,0);
                GL.TexCoord2(1,top);GL.Vertex3(1,top,0);GL.TexCoord2(0,top);GL.Vertex3(0,top,0);GL.End();
            }
            finally{GL.PopMatrix();RenderTexture.active=previous;GL.sRGBWrite=srgb;}
        }
        void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
