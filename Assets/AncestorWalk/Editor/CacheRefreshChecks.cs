using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class CacheRefreshChecks
    {
        public static string Run()
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g.Generation.Busy)throw new Exception("Generation must be idle");
            g.ShowTree();
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",flags).GetValue(g);
            string suffix=Guid.NewGuid().ToString("N");
            var p=new Person{Id="refresh-target-"+suffix,Name="Refresh fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            var other=new Person{Id="refresh-other-"+suffix,Name="Unchanged fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            double saveMs=0;
            try
            {
                var png=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");
                foreach(var person in new[]{p,other})
                {g.Tree.People.Add(person.Id,person);locations[person.Id]=ResidenceTimeline.Load(g.TreeId,person);}
                foreach(int year in new[]{1900,1940,1960})PanoramaCache.SaveYear(g.TreeId,p.Id,year,png,"fixture","fixture",place:year==1900?"York":"Paris");
                var bad=PanoramaCache.SaveYear(g.TreeId,p.Id,1970,png,"fixture","fixture",place:"York");
                string badFile=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(p.Id),"years","1970",bad.file);
                var corrupt=(byte[])png.Clone();corrupt[corrupt.Length-1]^=1;File.WriteAllBytes(badFile,corrupt);
                foreach(int year in new[]{1900,1910,1920,1930,1940})PanoramaCache.SaveYear(g.TreeId,other.Id,year,png,"fixture","fixture",place:"York");
                g.RefreshPerson(p.Id);g.RefreshPerson(other.Id);
                var untouched=g.CachedYears(other.Id);string revision=g.SceneRevision(other.Id,1900);
                var panels=g.GetComponentsInChildren<CubeFacingControls>();var version=typeof(CubeFacingControls).GetField("contentVersion",flags);var versions=panels.Select(c=>(int)version.GetValue(c)).ToArray();
                long reads=PanoramaCache.ImageReadCount,hashes=PanoramaCache.ImageValidationCount;var sw=Stopwatch.StartNew();
                g.SaveLocation(p.Id,1940,"Paris");sw.Stop();saveMs=sw.Elapsed.TotalMilliseconds;
                Check(PanoramaCache.ImageReadCount==reads,"Location edit reuses validated records without PNG reads");
                Check(PanoramaCache.ImageValidationCount==hashes,"Location edit reuses validated records without PNG hashing");
                Check(ReferenceEquals(untouched,g.CachedYears(other.Id))&&g.SceneRevision(other.Id,1900)==revision,"Unrelated person's index/revision untouched");
                Check(g.CachedYears(p.Id).SequenceEqual(new[]{1900,1940,1960}),"Location compatibility updated and corrupt PNG excluded");
                Check(ResidenceTimeline.Load(g.TreeId,p).At(p,1940)=="Paris","Move persisted atomically");
                Check(panels.Select((c,i)=>(int)version.GetValue(c)==versions[i]).All(ok=>ok),"Unrelated visible panel content untouched");
                reads=PanoramaCache.ImageReadCount;hashes=PanoramaCache.ImageValidationCount;g.SetYear(p.Id,1940);
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Year update does not revalidate images");
                Check(panels.Select((c,i)=>(int)version.GetValue(c)==versions[i]).All(ok=>ok),"Year update refreshes only target UI");
                typeof(SceneGeneration).GetMethod("Notify",flags).Invoke(g.Generation,new object[]{p.Id,"Fixture status"});
                Check(g.Generation.Message(p.Id)=="Fixture status","Person-specific status notification still available");
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Generation status performs zero image reads/hashes");
                // Simulate successful completion notification, with no network request.
                var saved=(Action<string>)typeof(SceneGeneration).GetField("SceneSaved",flags).GetValue(g.Generation);saved.Invoke(p.Id);
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Successful generation indexes the target without main-thread PNG reads");
                reads=PanoramaCache.ImageReadCount;hashes=PanoramaCache.ImageValidationCount;
                Check(locations[p.Id].CompatibleYears(g.TreeId,p).SequenceEqual(new[]{1900,1940,1960}),"Compatibility API retains sorted valid years");
                Check(PanoramaCache.ImageReadCount-reads==1&&PanoramaCache.ImageValidationCount-hashes==1,"Compatibility reuses validated images; only the corrupt file needs revalidation");
                string result=n+" targeted cache-refresh checks passed. Fixture SaveLocation: "+saveMs.ToString("F1")+" ms, zero image reads / hashes on main thread, no unrelated-person refresh or paid calls.";
                File.WriteAllText("Artifacts/Locations/cache-refresh-checks.txt",result);return result;
            }
            finally
            {
                foreach(var person in new[]{p,other})
                {
                    g.Tree.People.Remove(person.Id);locations.Remove(person.Id);PlayerPrefs.DeleteKey("year."+g.TreeId+"."+person.Id);
                    string root=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(person.Id));if(Directory.Exists(root))Directory.Delete(root,true);
                    string file=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(person.Id)+".json");if(File.Exists(file))File.Delete(file);
                }
                typeof(AncestorGallery).GetMethod("RefreshCards",flags).Invoke(g,null);g.ShowTree();
            }
        }
    }
}
