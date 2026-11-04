using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class TreePerformanceChecks
    {
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying)throw new Exception("Play mode required");
            g.StartCoroutine(Checks(g));return "Running pooled panels/shared thumbnail stress checks";
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            g.ShowTree();yield return null;
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var position=nav.transform.position;var rotation=nav.transform.rotation;
            var controls=g.GetComponentsInChildren<CubeFacingControls>();
            var method=typeof(CubeFacingControls).GetMethod("AdvanceFacing",BindingFlags.Instance|BindingFlags.NonPublic);
            var step=controls.Select(c=>(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),c,method)).ToArray();
            try
            {
                // Warm every reusable face once, then verify a long crossing creates no objects or pooled elements.
                for(int pass=0;pass<2;pass++)for(int face=0;face<8;face++)
                {float angle=face*Mathf.PI/4;nav.transform.position+=controls[0].transform.position+new Vector3(Mathf.Sin(angle)*2,0,Mathf.Cos(angle)*2)-Camera.main.transform.position;foreach(var advance in step)advance(.3f);}
                int objects=g.GetComponentsInChildren<Transform>(true).Length,pool=controls.Sum(c=>c.PoolBuilds);
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<1000;i++)
                {float angle=i*.03f;nav.transform.position+=controls[0].transform.position+new Vector3(Mathf.Sin(angle)*2,0,Mathf.Cos(angle)*2)-Camera.main.transform.position;foreach(var advance in step)advance(.016f);}
                long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(controls.Sum(c=>c.PoolBuilds)==pool,"No pooled element creation during 1000 movement steps");
                Check(g.GetComponentsInChildren<Transform>(true).Length==objects,"No panel object creation/destruction during movement");
                Check(controls.Sum(c=>c.SideTransitions)>10,"Stress path crosses panel sides");
                Check(controls.All(c=>c.Panel.GetComponentsInChildren<YearSlider>(true).Length==1),"Only live panels own sliders");
                Check(!g.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Fading information panel"),"No temporary panel snapshots");
                var cache=g.Thumbnails;float deadline=Time.realtimeSinceStartup+20;
                while(cache.PendingCount>0&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(cache.PendingCount==0,"Thumbnail queue drains");
                Check(cache.UnusedTextureCount<=CubeThumbnailCache.MaximumUnusedTextures,"Shared memory texture budget");
                int reads=cache.DiskReads,decodes=cache.Decodes;
                g.ShowTree();yield return null;
                deadline=Time.realtimeSinceStartup+2;while(Time.realtimeSinceStartup<deadline)yield return null;
                Check(cache.DiskReads==reads&&cache.Decodes==decodes,"Tree rebuild reuses prewarmed shared thumbnails");
                Directory.CreateDirectory("Artifacts/Performance");File.WriteAllText("Artifacts/Performance/stress.txt",n+" performance checks passed. Panel loop: "+bytes+" managed bytes / 1000 steps across "+controls.Length+" cubes (includes native text interop on first visible-side content sync). No paid requests.");
            }
            finally{nav.transform.SetPositionAndRotation(position,rotation);g.ShowTree();}
        }
    }
}
