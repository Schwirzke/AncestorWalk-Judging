using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ResidentPreviewChecks
    {
        public static string Run()
        {var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();g.StartCoroutine(Checks(g));return "Testing 18 cached cubes, 36 active textures and unused-cache pressure";}
        static byte[] Png(int seed)
        {
            var t=new Texture2D(256,128,TextureFormat.RGBA32,false);var pixels=new Color32[256*128];
            var c=new Color32((byte)(seed*17),(byte)(seed*31),(byte)(seed*47),255);for(int i=0;i<pixels.Length;i++)pixels[i]=c;
            t.SetPixels32(pixels);t.Apply();var bytes=t.EncodeToPNG();UnityEngine.Object.Destroy(t);return bytes;
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var position=nav.transform.position;var rotation=nav.transform.rotation;
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(g);
            var people=new List<Person>();var bodies=new List<CubePanoramaPreview>();string suffix=Guid.NewGuid().ToString("N");
            var cache=g.Thumbnails;CubePreviewManager manager=null;
            IEnumerator Loaded()
            {
                float end=Time.realtimeSinceStartup+60;
                while((cache.PendingCount>0||bodies.Any(p=>p.IsLoading))&&Time.realtimeSinceStartup<end)
                {if(manager!=null&&manager.LastFrameCommits>1)throw new Exception("More than one pair committed per frame");yield return null;}
                Check(cache.PendingCount==0&&bodies.All(p=>!p.IsLoading),"Asynchronous queues drain");yield return new WaitForSecondsRealtime(.4f);
            }
            try
            {
                g.Tutorial.Dismiss();g.LocationEditor.Close();g.ShowTree();
                for(int i=0;i<18;i++)
                {
                    var p=new Person{Id="resident-"+suffix+"-"+i,Name="Resident fixture "+i,BirthDate="1900",DeathDate="1980",BirthPlace="York"};people.Add(p);g.Tree.People.Add(p.Id,p);locations[p.Id]=ResidenceTimeline.Load(g.TreeId,p);
                    foreach(int year in new[]{1930,1950})PanoramaCache.SaveYear(g.TreeId,p.Id,year,Png(i*2+(year==1930?1:2)),"fixture","fixture",place:"York");
                    g.RefreshPerson(p.Id);g.SetYear(p.Id,1940);
                }
                g.ShowTree();manager=g.GetComponentInChildren<CubePreviewManager>();
                foreach(var p in people)bodies.Add(g.GetComponentsInChildren<CubePanoramaPreview>().Single(v=>v.PersonId==p.Id));
                Check(manager.RegisteredCount==g.Tree.People.Count,"Every cube is registered");
                Check(bodies.All(p=>p.IsResident&&p.IsLoading&&p.PreviewMaterial.mainTexture==Texture2D.grayTexture),"Cached scenes start neutral while loading without approach");
                yield return Loaded();
                foreach(var p in bodies)
                {
                    Check(p.IsResident&&p.FirstYear==1930&&p.SecondYear==1950,"All eighteen scene pairs remain resident");
                    Check(p.PreviewMaterial.mainTexture.width==512&&p.PreviewMaterial.mainTexture.height==256,"Full cube resolution retained");
                    Check(Mathf.Abs(p.PreviewMaterial.GetFloat("_Blend")-.5f)<.001f,"Selected-year blending retained");
                }
                Check(cache.PinnedTextureCount>=36,"More than 32 active textures protected");
                var textures=bodies.Select(p=>p.PreviewMaterial.mainTexture).ToArray();int reads=cache.DiskReads,decodes=cache.Decodes;
                nav.transform.position+=new Vector3(150,90,-150);nav.transform.rotation=Quaternion.Euler(0,180,0);yield return new WaitForSecondsRealtime(1.3f);
                Check(bodies.All(p=>p.IsResident&&p.FirstYear==1930),"Distant offscreen cubes do not revert");
                Check(bodies.Select((p,i)=>p.PreviewMaterial.mainTexture==textures[i]).All(v=>v)&&cache.DiskReads==reads&&cache.Decodes==decodes,"Camera movement causes no texture reset or reload");
                g.SetYear(people[0].Id,1950);yield return Loaded();Check(bodies[0].FirstYear==1950&&bodies[0].SecondYear==1950,"Offscreen slider selection updates immediately");
                Check(cache.DiskReads==reads&&cache.Decodes==decodes,"Warm year scrub avoids reads/decodes");
                var old=bodies[0].PreviewMaterial.mainTexture;PanoramaCache.SaveYear(g.TreeId,people[0].Id,1950,Png(77),"replacement","fixture",place:"York");g.RefreshPerson(people[0].Id);yield return Loaded();
                Check(bodies[0].PreviewMaterial.mainTexture!=old,"Offscreen regeneration applies new revision");
                Check(bodies.Skip(1).Select((p,i)=>p.PreviewMaterial.mainTexture==textures[i+1]).All(v=>v),"Other cubes retain their texture handles");
                g.SaveLocation(people[0].Id,1900,"Monterey");Check(bodies[0].FirstYear==0&&bodies[0].PreviewMaterial.mainTexture==Resources.Load<Texture2D>("AllTimePanorama"),"Uncached residence still uses All Time");
                g.SaveLocation(people[0].Id,1900,"York");yield return Loaded();Check(bodies[0].FirstYear==1950,"Restored location reuses its scene");
                // Pressure unused cache entries while all displayed textures remain pinned.
                for(int i=0;i<35;i++)
                {
                    var e=cache.Acquire(g.TreeId,people[1].Id,1930,"pressure-"+i);float end=Time.realtimeSinceStartup+10;
                    while(!e.Ready&&Time.realtimeSinceStartup<end)yield return null;
                    Check(e.Ready&&!e.Failed,"Pressure entry loads");cache.Release(e);
                }
                yield return new WaitForSecondsRealtime(.5f);
                Check(cache.UnusedTextureCount<=32&&cache.PinnedTextureCount>=35,"Only unused cache is capped at 32");
                Check(bodies.All(p=>p.PreviewMaterial.mainTexture!=null&&p.FirstYear>0),"Cache trimming preserves active cube scenes");
                var root=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(people[2].Id));
                var originals=new Dictionary<string,byte[]>();foreach(int year in new[]{1930,1950})
                {PanoramaCache.ReadYear(g.TreeId,people[2].Id,year,out var record);var file=Path.Combine(root,"years",year.ToString(),record.file);originals[file]=File.ReadAllBytes(file);File.WriteAllBytes(file,new byte[40]);}
                g.RefreshPerson(people[2].Id);Check(bodies[2].FirstYear==0,"Corrupt source scenes are unavailable");
                foreach(var item in originals)File.WriteAllBytes(item.Key,item.Value);g.RefreshPerson(people[2].Id);yield return Loaded();
                Check(bodies[2].FirstYear==1930,"Repaired sources restore preview");
                bodies[3].gameObject.SetActive(false);Check(!bodies[3].IsResident,"Disabled cube releases residency");bodies[3].gameObject.SetActive(true);yield return Loaded();Check(bodies[3].FirstYear==1930,"Re-enabled cube reacquires textures");
                g.Select(people[0].Id);yield return null;float deadline=Time.realtimeSinceStartup+10;while((cache.PendingCount>0||cache.UnusedTextureCount>32)&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(cache.PinnedTextureCount==0&&cache.UnusedTextureCount<=32,"Scene entry releases tree leases and trims unused textures");
                g.ShowTree();manager=g.GetComponentInChildren<CubePreviewManager>();bodies.Clear();foreach(var p in people)bodies.Add(g.GetComponentsInChildren<CubePanoramaPreview>().Single(v=>v.PersonId==p.Id));yield return Loaded();
                Check(bodies.All(p=>p.FirstYear>0)&&bodies[0].FirstYear==1950,"Returning restores every selected scene");
                Directory.CreateDirectory("Artifacts/Performance");File.WriteAllText("Artifacts/Performance/resident-preview-checks.txt",n+" resident preview checks passed; 18 cached cubes / 36 active textures, unused cache <=32, no paid API calls or Quest deployment.");
            }
            finally
            {
                g.ShowTree();foreach(var p in people)
                {
                    g.Tree.People.Remove(p.Id);locations.Remove(p.Id);PlayerPrefs.DeleteKey("year."+g.TreeId+"."+p.Id);
                    string dir=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(p.Id));if(Directory.Exists(dir))Directory.Delete(dir,true);
                    string file=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(p.Id)+".json");if(File.Exists(file))File.Delete(file);
                }
                g.ShowTree();nav.transform.SetPositionAndRotation(position,rotation);
            }
        }
    }
}
