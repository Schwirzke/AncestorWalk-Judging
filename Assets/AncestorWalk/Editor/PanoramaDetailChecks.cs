using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AncestorWalk.Editor
{
    // Isolated GPU probes, not a runtime readback or a change to the saved scene.
    public static class PanoramaDetailChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required for isolated rendering checks");
            int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
            var owned=new List<UnityEngine.Object>();T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
            var oldActive=RenderTexture.active;var observations=new List<string>();
            try
            {
                var shader=Resources.Load<Shader>("Shaders/CubePanorama");Check(shader!=null&&shader.isSupported,"Panorama detail shader is supported");
                var material=Own(new Material(shader));material.SetFloat("_SpinSpeed",0);material.SetFloat("_HorizonLift",0);material.SetFloat("_SecondLift",0);material.SetFloat("_PreviousLiftA",0);material.SetFloat("_PreviousLiftB",0);
                material.SetVector("_ControlPositive",Vector4.zero);material.SetVector("_ControlNegative",Vector4.zero);
                Texture2D Mips(Color tint)
                {
                    var texture=Own(new Texture2D(512,256,TextureFormat.RGBA32,true,true));
                    for(int mip=0;mip<texture.mipmapCount;mip++)
                    {
                        var color=tint*(1-.09f*mip);color.a=1;
                        texture.SetPixels(Enumerable.Repeat(color,Math.Max(1,512>>mip)*Math.Max(1,256>>mip)).ToArray(),mip);
                    }
                    texture.Apply(false,false);texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=CubeThumbnailCache.Anisotropy;texture.wrapModeU=TextureWrapMode.Repeat;texture.wrapModeV=TextureWrapMode.Clamp;return texture;
                }
                var white=Mips(Color.white);var red=Mips(Color.red);var blue=Mips(Color.blue);
                material.SetTexture("_MainTex",white);material.SetTexture("_SecondTex",white);material.SetTexture("_PreviousA",white);material.SetTexture("_PreviousB",white);
                var sphere=Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));sphere.name="Transient panorama detail probe";sphere.layer=31;sphere.GetComponent<Renderer>().sharedMaterial=material;
                var host=Own(new GameObject("Transient panorama probe camera"));var camera=host.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.fieldOfView=60;camera.nearClipPlane=.05f;camera.farClipPlane=50;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.None;
                var target=Own(new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));target.Create();
                var pixels=Own(new Texture2D(256,256,TextureFormat.RGBA32,false,true));
                Color Render(float distance,float bias,bool seam=false)
                {
                    material.SetFloat("_MipBias",bias);camera.transform.position=seam?new Vector3(-distance,0,0):new Vector3(0,0,-distance);camera.transform.LookAt(Vector3.zero);
                    Physics.SyncTransforms();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply(false,false);
                    Color value=Color.clear;for(int y=127;y<=129;y++)for(int x=127;x<=129;x++)value+=pixels.GetPixel(x,y);return value/9;
                }
                foreach(float distance in new[]{4f,8f,12f})
                {
                    var ordinary=Render(distance,0);var detailed=Render(distance,CubePanoramaPreview.DetailMipBias);
                    observations.Add(distance+"m mip probe: standard "+ordinary.r.ToString("F4")+", detailed "+detailed.r.ToString("F4"));
                    Check(detailed.r>ordinary.r+.015f,"Detail bias selects a sharper mip at "+distance+"m");
                }
                var near=Render(1.5f,CubePanoramaPreview.DetailMipBias);var far=Render(12,CubePanoramaPreview.DetailMipBias);Check(near.r>far.r+.04f,"Distant probes still use mipmaps instead of forcing full detail");
                Render(8,0,true);var middle=pixels.GetPixel(128,128).r;var adjacent=(pixels.GetPixel(124,128).r+pixels.GetPixel(132,128).r)*.5f;
                observations.Add("Longitude seam center "+middle.ToString("F4")+", adjacent "+adjacent.ToString("F4"));
                Check(middle>adjacent-.06f,"Longitude wrap does not select the blurriest mip at the seam");
                material.SetTexture("_MainTex",red);material.SetTexture("_SecondTex",blue);material.SetFloat("_Blend",.5f);
                var blend=Render(8,CubePanoramaPreview.DetailMipBias);Check(Mathf.Abs(blend.r-blend.b)<.015f&&blend.r>.05f&&blend.g<.01f,"Year blend keeps matching detail on both textures");
                material.SetTexture("_MainTex",blue);material.SetTexture("_SecondTex",blue);material.SetTexture("_PreviousA",red);material.SetTexture("_PreviousB",red);material.SetFloat("_Transition",.5f);
                var fade=Render(8,CubePanoramaPreview.DetailMipBias);Check(Mathf.Abs(fade.r-fade.b)<.015f&&fade.r>.05f&&fade.g<.01f,"Replacement fade uses the same detail sampling");
                Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"No shader compilation errors");
                Check(CubeThumbnailCache.Width==512&&CubeThumbnailCache.Height==256,"Preview pixel storage is unchanged");
                string output=checks+" panorama detail GPU checks passed.\n"+string.Join("\n",observations);Directory.CreateDirectory("Artifacts/PanoramaDetail");File.WriteAllText("Artifacts/PanoramaDetail/render-checks.txt",output);return output;
            }
            finally{RenderTexture.active=oldActive;foreach(var item in owned.AsEnumerable().Reverse())if(item!=null)UnityEngine.Object.DestroyImmediate(item);}
        }
    }
}
