using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class ThumbnailCacheChecks
    {
        public static string Run(){var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();g.StartCoroutine(Checks());return "Checking persistent thumbnails, integrity and LRU";}
        static IEnumerator Checks()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            string tree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"))),person="thumbnail-fixture";
            var hosts=new List<GameObject>();CubeThumbnailCache New(){var go=new GameObject("Thumbnail cache fixture");hosts.Add(go);return go.AddComponent<CubeThumbnailCache>();}
            var pixels=new Texture2D(512,256,TextureFormat.RGBA32,false);var data=new Color32[512*256];
            var random=new System.Random();var color=new Color32((byte)random.Next(1,255),(byte)random.Next(1,255),(byte)random.Next(1,255),255);
            for(int i=0;i<data.Length;i++)data[i]=color;pixels.SetPixels32(data);pixels.Apply();byte[] png=pixels.EncodeToPNG();UnityEngine.Object.Destroy(pixels);
            string hash=PanoramaCache.Hash(png),thumb=Path.Combine(PanoramaCache.StorageRoot,"CubeThumbnails","v1",hash+".rgba");
            string scenes=Path.Combine(PanoramaCache.DirectoryPath,"people",tree);
            IEnumerator Ready(CubeThumbnailCache.Entry e){float end=Time.realtimeSinceStartup+20;while(!e.Ready&&Time.realtimeSinceStartup<end)yield return null;Check(e.Ready,"Thumbnail request finishes");}
            try
            {
                PanoramaCache.SaveYear(tree,person,1900,png,"fixture","fixture",place:"York");var first=New();var a=first.Acquire(tree,person,1900,"v1");yield return Ready(a);
                Check(!a.Failed&&a.Texture.width==512&&a.Texture.height==256,"Cold thumbnail dimensions");
                Check(a.Texture.anisoLevel==CubeThumbnailCache.Anisotropy&&a.Texture.filterMode==FilterMode.Trilinear,"Fresh GPU thumbnails keep anisotropic trilinear sampling");
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    float end=Time.realtimeSinceStartup+10;while(!File.Exists(thumb)&&Time.realtimeSinceStartup<end)yield return null;
                    Check(File.Exists(thumb),"Thumbnail persisted asynchronously");
                    var second=New();var b=second.Acquire(tree,person,1900,"v1");yield return Ready(b);
                    Check(!b.Failed&&second.Decodes==0&&b.Texture is Texture2D t&&!t.isReadable,"Next session uses raw thumbnail without PNG decode or CPU copy");
                    Check(b.Texture.anisoLevel==CubeThumbnailCache.Anisotropy&&b.Texture.filterMode==FilterMode.Trilinear,"Persisted raw thumbnails use the same sampling settings");
                    Check(ReferenceEquals(b,second.Acquire(tree,person,1900,"v1")),"Repeated request shares handle");second.Release(b);second.Release(b);
                    // Valid source repairs a corrupt disposable thumbnail.
                    File.WriteAllBytes(thumb,new byte[512*256*4+64]);var repair=New();var c=repair.Acquire(tree,person,1900,"v2");yield return Ready(c);
                    Check(!c.Failed&&repair.Decodes==1,"Corrupt thumbnail rebuilt from validated source");
                    while(c.Readback)yield return null;
                }
                // Exercise real entry eviction; retained source/derivative files remain untouched.
                var lru=New();
                for(int year=1900;year<1935;year++)
                {
                    if(year!=1900)PanoramaCache.SaveYear(tree,person,year,png,"fixture","fixture",place:"York");
                    var e=lru.Acquire(tree,person,year,"lru");yield return Ready(e);lru.Release(e);
                }
                float until=Time.realtimeSinceStartup+5;while(lru.TextureCount>CubeThumbnailCache.MaximumUnusedTextures&&Time.realtimeSinceStartup<until)yield return null;
                Check(lru.TextureCount<=CubeThumbnailCache.MaximumUnusedTextures,"LRU retains at most 32 unpinned thumbnails");
                int reads=lru.DiskReads;var held=lru.Acquire(tree,person,1934,"lru");yield return Ready(held);Check(lru.DiskReads==reads,"Recent thumbnail is reused without disk access");lru.Release(held);
                var record=PanoramaCache.ReadYear(tree,person,1900,out var original);
                File.WriteAllBytes(Path.Combine(scenes,PanoramaCache.PersonKey(person),"years","1900",original.file),new byte[40]);
                var invalid=New();var bad=invalid.Acquire(tree,person,1900,"invalid-source");yield return Ready(bad);
                Check(bad.Failed&&bad.Texture==null,"Cached derivative cannot bypass source checksum/identity validation");
                Directory.CreateDirectory("Artifacts/Performance");File.WriteAllText("Artifacts/Performance/thumbnail-checks.txt",n+" thumbnail cache checks passed; isolated fixtures, no API requests.");
            }
            finally
            {
                foreach(var go in hosts)if(go!=null)UnityEngine.Object.Destroy(go);
                if(Directory.Exists(scenes))Directory.Delete(scenes,true);if(File.Exists(thumb))File.Delete(thumb);
            }
        }
    }
}
