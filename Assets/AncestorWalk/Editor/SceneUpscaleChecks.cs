using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class SceneUpscaleChecks
    {
        const string Report="Artifacts/scene-upscale-checks.txt";
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Guarded Play mode required");
            File.WriteAllText(Report,"RUNNING");UnityEngine.Object.FindAnyObjectByType<AncestorGallery>().StartCoroutine(Safe(Checks()));return "Scene upscale checks running";
        }
        static IEnumerator Safe(IEnumerator test)
        {
            while(true){bool more=false;object next=null;try{more=test.MoveNext();if(more)next=test.Current;}catch(Exception e){(test as IDisposable)?.Dispose();File.WriteAllText(Report,"FAIL: "+e);yield break;}if(!more)yield break;yield return next;}
        }
        static IEnumerator Checks()
        {
            int count=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
            var owned=new List<UnityEngine.Object>();T Own<T>(T obj)where T:UnityEngine.Object{owned.Add(obj);return obj;}
            string tree=TreeStore.NewId(),person="upscale-fixture",path=Path.Combine(PanoramaCache.DirectoryPath,"people",tree);
            var previous=RenderTexture.active;
            try
            {
                Check(PanoramaUpscaler.Size(2240,1120,16384)==new Vector2Int(4480,2240),"Typical source doubles");
                Check(PanoramaUpscaler.Size(4096,2048,16384)==new Vector2Int(8192,4096),"Output dimension capped");
                Check(PanoramaUpscaler.Size(2240,1120,4096)==new Vector2Int(4096,2048),"Device maximum preserves ratio");
                var host=Own(new GameObject("Isolated scene upscaler"));var upscale=host.AddComponent<PanoramaUpscaler>();Check(upscale.Available,"Upscale shader supported");
                var source=Own(new Texture2D(256,128,TextureFormat.RGBA32,false,false));source.wrapModeU=TextureWrapMode.Repeat;source.wrapModeV=TextureWrapMode.Clamp;
                var pixels=new Color[256*128];for(int y=0;y<128;y++)for(int x=0;x<256;x++)pixels[y*256+x]=new Color(x/255f,y/127f,.25f,1);source.SetPixels(pixels);source.Apply();
                var target=Own(upscale.Create(source));Check(target!=null&&target.width==512&&target.height==256,"GPU target doubles dimensions");
                int bands=0;for(int row=0;row<target.height;row+=PanoramaUpscaler.RowsPerFrame){upscale.RenderRows(source,target,row,PanoramaUpscaler.RowsPerFrame);bands++;yield return null;}
                target.GenerateMips();Check(bands==4,"Upscale is divided across bounded row batches");
                var read=Own(new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,false));RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();RenderTexture.active=previous; // Test-only readback.
                Color bottom=read.GetPixel(64,32),top=read.GetPixel(64,224),right=read.GetPixel(448,32);
                Check(top.g>bottom.g+.65f&&right.r>bottom.r+.65f,"GPU upscale preserves vertical/horizontal orientation");
                Check(Math.Abs(bottom.r-.125f)<.04f&&Math.Abs(bottom.g-.125f)<.04f&&Math.Abs(bottom.b-.25f)<.04f,"Filtering preserves colors and gamma");
                Check(Math.Abs(read.GetPixel(100,63).g-read.GetPixel(100,64).g)<.03f,"No discontinuity at row-batch boundary");
                Check(target.useMipMap&&target.wrapModeU==TextureWrapMode.Repeat&&target.wrapModeV==TextureWrapMode.Clamp,"Mipmaps and panorama wrap retained");
                var cacheHost=Own(new GameObject("Isolated full-scene cache"));var cache=cacheHost.AddComponent<ScenePanoramaCache>();
                byte[] png=source.EncodeToPNG();PanoramaCache.SaveYear(tree,person,1900,png,"fixture","fixture",place:"York");string digest=PanoramaCache.Hash(png);
                var entry=cache.Acquire(tree,person,1900,"fixture");float end=Time.realtimeSinceStartup+20;while(!entry.Ready&&Time.realtimeSinceStartup<end)yield return null;
                Check(entry.Ready&&!entry.Failed&&entry.Texture.width==(PanoramaUpscaler.Enabled?512:256)&&((PanoramaUpscaler.Enabled&&entry.Texture is RenderTexture)||(!PanoramaUpscaler.Enabled&&entry.Texture is Texture2D)),"Scene loading follows the enabled/disabled upscale policy");
                Check(entry.Record.width==256&&entry.Record.height==128&&entry.Record.sha256==digest,"Original cache metadata unchanged");
                var reused=cache.Acquire(tree,person,1900,"fixture");Check(ReferenceEquals(entry,reused)&&cache.Reads==1&&cache.Commits==1,"Repeated lease reuses upscale without reload");cache.Release(reused);cache.Release(entry);
                Check(PanoramaCache.Hash(File.ReadAllBytes(Path.Combine(entry.Directory,entry.Record.file)))==digest,"Original PNG remains byte-for-byte unchanged");
                var entries=(Dictionary<string,ScenePanoramaCache.Entry>)typeof(ScenePanoramaCache).GetField("entries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache);
                foreach(var old in entries.Values)if(old.Texture!=null)UnityEngine.Object.Destroy(old.Texture);entries.Clear();
                for(int i=0;i<3;i++){var descriptor=new RenderTexture(4480,2240,0){useMipMap=true};entries.Add("memory-"+i,new ScenePanoramaCache.Entry{Key="memory-"+i,Texture=descriptor,Ready=true,Used=i+1,References=i==2?1:0});} // Descriptors test budget without allocating large GPU buffers.
                long one=ScenePanoramaCache.TextureBytes(entries["memory-0"].Texture);Check(one>50L*1024*1024&&one<52L*1024*1024,"Typical upscaled RGBA/mip estimate is about 51 MiB");
                typeof(ScenePanoramaCache).GetMethod("Trim",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cache,null);
                Check(cache.UnusedBytes<=ScenePanoramaCache.MaximumUnusedBytes&&entries.ContainsKey("memory-2"),"64 MiB unused cap preserves displayed textures");
                Check(CubeThumbnailCache.Width==512&&CubeThumbnailCache.Height==256,"Sphere preview resolution unchanged");
                File.WriteAllText(Report,"PASS: "+count+" scene upscale, rendering, cache and memory checks. No model calls.");
            }
            finally{RenderTexture.active=previous;foreach(var o in owned)if(o!=null)UnityEngine.Object.Destroy(o);if(Directory.Exists(path))Directory.Delete(path,true);}
        }
    }
}
