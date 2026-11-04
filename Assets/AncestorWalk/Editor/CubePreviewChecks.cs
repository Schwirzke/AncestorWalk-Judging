using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class CubePreviewChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Refresh(AncestorGallery g)=>typeof(AncestorGallery).GetMethod("RefreshCards",Private).Invoke(g,null);
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g==null||!Application.isPlaying)throw new Exception("Play mode required");
            Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/cube-preview-checks.txt","RUNNING");g.StartCoroutine(Checks(g));return "Cube panorama checks running";
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
            string id="cube-check-"+Guid.NewGuid().ToString("N");
            var p=new Person{Id=id,Name="Cube preview fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",Private).GetValue(g);
            g.Tree.People.Add(id,p);locations[id]=ResidenceTimeline.Load(g.TreeId,p);Refresh(g);g.ShowTree();
            var manager=g.GetComponentInChildren<CubePreviewManager>();manager.enabled=false;
            CubePanoramaPreview Find()=>g.GetComponentsInChildren<CubePanoramaPreview>().Single(x=>x.PersonId==id);
            var preview=Find();var fallback=Resources.Load<Texture2D>("AllTimePanorama");
            string cache=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(id));
            string locationFile=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(id)+".json");
            byte[] first=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png"),second=File.ReadAllBytes("Assets/AncestorWalk/Resources/AllTimePanorama.png");
            IEnumerator Load(){float until=Time.realtimeSinceStartup+20;while(preview.IsLoading&&Time.realtimeSinceStartup<until){preview.LoadNext();yield return null;}Check(!preview.IsLoading,"Asynchronous preview completes within deadline");}
            try
            {
                Check(preview.PreviewMaterial.shader.isSupported,"Cube shader supported");preview.SetResident(true);
                Check(preview.FirstYear==0&&preview.PreviewMaterial.mainTexture==fallback,"Uncached All Time fallback");
                PanoramaCache.SaveYear(g.TreeId,id,1930,first,"fixture","fixture",12,null,"York");
                PanoramaCache.SaveYear(g.TreeId,id,1950,second,"fixture","fixture",5,null,"York");
                g.SetYear(id,1940);Refresh(g);yield return Load();
                Check(preview.FirstYear==1930&&preview.SecondYear==1950,"Per-person year pair");
                Check(Mathf.Abs(preview.PreviewMaterial.GetFloat("_Blend")-.5f)<.001f,"Midpoint uses scene blend curve");
                Check(preview.PreviewMaterial.GetFloat("_HorizonLift")==12&&preview.PreviewMaterial.GetFloat("_SecondLift")==5,"Per-image horizon lifts");
                var texture=preview.PreviewMaterial.mainTexture;
                Check(texture.width==512&&texture.height==256,"Bounded preview dimensions");Check(texture is RenderTexture || texture is Texture2D small&&!small.isReadable,"No CPU pixel copy retained");
                Check(texture.filterMode==FilterMode.Trilinear&&texture.anisoLevel==CubeThumbnailCache.Anisotropy,"Sharper oblique filtering retains trilinear mipmaps");
                Check(preview.PreviewMaterial.GetFloat("_MipBias")==CubePanoramaPreview.DetailMipBias,"Preview uses the bounded detail bias");
                g.SetYear(id,1934);Check(preview.PreviewMaterial.GetFloat("_Blend")==0,"Early hold matches scene");
                g.SetYear(id,1946);Check(preview.PreviewMaterial.GetFloat("_Blend")==1,"Late hold matches scene");
                Check(preview.PreviewMaterial.mainTexture==texture&&!preview.IsLoading,"Same-pair scrub avoids decode");
                g.SetYear(id,1940);float mix=preview.PreviewMaterial.GetFloat("_Blend");g.Select(id);
                float deadline=Time.realtimeSinceStartup+20;while(g.IsSceneLoading&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(Mathf.Abs(RenderSettings.skybox.GetFloat("_Blend")-mix)<.001f&&g.Year(id)==1940,"Entering scene keeps year and blend");
                g.ShowTree();manager=g.GetComponentInChildren<CubePreviewManager>();manager.enabled=false;preview=Find();preview.SetResident(true);yield return Load();
                Check(preview.FirstYear==1930&&preview.SecondYear==1950,"Return reloads correct cube pair");
                var old=preview.PreviewMaterial.mainTexture;
                PanoramaCache.SaveYear(g.TreeId,id,1930,second,"regenerated fixture","fixture",3,null,"York");Refresh(g);yield return Load();
                Check(preview.PreviewMaterial.mainTexture!=old&&preview.PreviewMaterial.GetFloat("_HorizonLift")==3,"Regeneration revision refreshes preview");
                Check(preview.PreviewMaterial.GetFloat("_Transition")==0,"Replacement starts crossfade");
                yield return new WaitForSecondsRealtime(.4f);preview.Tick();Check(preview.PreviewMaterial.GetFloat("_Transition")==1,"Replacement finishes");
                g.SaveLocation(id,1900,"Monterey");Check(preview.FirstYear==0&&preview.PreviewMaterial.mainTexture==fallback,"Invalidated caches use All Time");Check(Directory.Exists(cache),"Invalidated files preserved");
                g.SaveLocation(id,1900,"York");yield return Load();Check(preview.FirstYear==1930,"Compatible location reuses preview");
                p.BirthDate=p.DeathDate="";preview.Refresh();Check(preview.FirstYear==0,"Missing lifespan fallback");p.BirthDate="1900";p.DeathDate="1980";preview.Refresh();yield return Load();
                preview.SetResident(false);yield return null;
                Check(!preview.IsLoading&&preview.PreviewMaterial.mainTexture==Texture2D.grayTexture,"Suspended preview releases texture references");
                Check(g.Thumbnails.UnusedTextureCount<=CubeThumbnailCache.MaximumUnusedTextures,"Shared thumbnail memory is bounded");
                int decodes=g.Thumbnails.Decodes;
                preview.SetResident(true);g.SetYear(id,1930);g.SetYear(id,1950);yield return Load();Check(preview.FirstYear==1950&&preview.SecondYear==1950,"Rapid requests coalesce to latest year");
                Check(g.Thumbnails.Decodes==decodes,"Revisiting a resident-cache thumbnail avoids redecoding");
                preview.SetResident(false);p.BirthDate=p.DeathDate="";preview.SetResident(true);Check(!preview.IsLoading&&preview.FirstYear==0,"Unknown lifespan queues no image");
                Check(g.GetComponentInChildren<CubePreviewManager>().RegisteredCount==g.Tree.People.Count,"Every cube is registered without a residency cap");
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/cube-preview-checks.txt",n+" cube panorama checks passed; no API requests.");
            }
            finally
            {
                g.Tree.People.Remove(id);locations.Remove(id);g.ShowTree();Refresh(g);
                if(Directory.Exists(cache))Directory.Delete(cache,true);if(File.Exists(locationFile))File.Delete(locationFile);
                PlayerPrefs.DeleteKey("year."+g.TreeId+"."+id);
            }
        }
    }
}
