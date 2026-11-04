using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ProductDemoChecks
    {
        public static string BundleIO()
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}
            string scratch=Path.GetFullPath(Path.Combine("Artifacts","bundle-check-"+Guid.NewGuid().ToString("N")));
            string streaming=Path.Combine(scratch,"StreamingAssets"),source=Path.Combine(streaming,BundledDemoScenes.Folder),cache=Path.Combine(scratch,"cache","Panoramas");
            Directory.CreateDirectory(Path.Combine(source,"images"));Directory.CreateDirectory(Path.Combine(source,"previews"));
            try
            {
                var texture=new Texture2D(256,128,TextureFormat.RGBA32,false);byte[] png;
                try{texture.SetPixels32(Enumerable.Repeat(new Color32(15,32,64,255),256*128).ToArray());texture.Apply();png=texture.EncodeToPNG();}finally{UnityEngine.Object.DestroyImmediate(texture);}
                string sha=PanoramaCache.Hash(png),tree=new string('a',64);var person=new Person{Id="@FIXTURE@",Name="Fictional test",BirthDate="1900",DeathDate="1980"};
                var raw=new byte[512*256*4];var preview=System.Text.Encoding.ASCII.GetBytes(PanoramaCache.Hash(raw)).Concat(raw).ToArray();
                var record=new PanoramaRecord{treeId=tree,personKey=PanoramaCache.PersonKey(person.Id),year=1900,sha256=sha,file=sha+".png",width=256,height=128,model="muse-image",place="York, England",prompt="Offline fixture"};
                var manifest=new BundledDemoScenes.Manifest{version=1,sceneYear=2026,treeId=tree,scenes=new[]{new BundledDemoScenes.Entry{personId=person.Id,record=record,thumbnailSha256=PanoramaCache.Hash(preview)}},omittedScenes=new[]{new BundledDemoScenes.Omission{personId=person.Id,year=1980,reason="Explicit empty fixture endpoint"}}};
                string manifestPath=Path.Combine(source,"manifest.json");File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));File.WriteAllBytes(Path.Combine(source,"images",record.file),png);File.WriteAllBytes(Path.Combine(source,"previews",sha+".rgba"),preview);
                var seeded=System.Threading.Tasks.Task.Run(()=>BundledDemoScenes.SeedAt(streaming,cache,tree,new[]{person})).GetAwaiter().GetResult();
                Check(seeded.Available&&seeded.Installed==1,"Desktop local seed succeeds on a worker");
                Check(seeded.OmittedScenes.Length==1&&seeded.OmittedScenes[0].year==1980&&!PanoramaCache.HasImageAt(cache,tree,person.Id,1980),"Documented empty endpoint stays separate from generated scenes");
                Check(PanoramaCache.HasImageAt(cache,tree,person.Id,1900),"Seed primes the valid cache");
                string saved=Path.Combine(cache,"people",tree,record.personKey,"years","1900","scene.json");var original=File.ReadAllBytes(saved);
                Check(BundledDemoScenes.SeedAt(streaming,cache,tree,new[]{person}).Preserved==1&&original.SequenceEqual(File.ReadAllBytes(saved)),"Repeated opens preserve existing records byte for byte");
                Check(File.ReadAllBytes(Path.Combine(scratch,"cache","CubeThumbnails","v1",sha+".rgba")).SequenceEqual(preview),"Ready-to-upload preview seeded");
                var portraitTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);byte[] portraitPng;
                try{portraitTexture.SetPixels32(Enumerable.Repeat(new Color32(90,65,50,255),64*64).ToArray());portraitTexture.Apply();portraitPng=portraitTexture.EncodeToPNG();}finally{UnityEngine.Object.DestroyImmediate(portraitTexture);}
                string portraitHash=PanoramaCache.Hash(portraitPng);var photo=new PhotoRecord{Id=portraitHash,File=portraitHash+".png",Title="Fictional portrait",Width=64,Height=64,Orientation=1};
                Directory.CreateDirectory(Path.Combine(source,"portraits"));File.WriteAllBytes(Path.Combine(source,"portraits",photo.File),portraitPng);
                manifest.version=2;manifest.portraits=new[]{new BundledDemoScenes.Portrait{personId=person.Id,photo=photo,model="muse-image-1.0",prompt="Fictional adult fixture"}};File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));
                var withPhoto=System.Threading.Tasks.Task.Run(()=>BundledDemoScenes.SeedAt(streaming,cache,tree,new[]{person})).GetAwaiter().GetResult();
                Check(withPhoto.PortraitsInstalled==1&&File.ReadAllBytes(Path.Combine(scratch,"cache","Photos",photo.File)).SequenceEqual(portraitPng),"Version two seeds content-addressed portraits before texture loading");
                Check(BundledDemoScenes.SeedAt(streaming,cache,tree,new[]{person}).PortraitsPreserved==1,"Existing valid portraits remain byte-for-byte unchanged");
                string apk=Path.Combine(scratch,"fixture.apk");using(var zip=System.IO.Compression.ZipFile.Open(apk,System.IO.Compression.ZipArchiveMode.Create))
                    foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip,file,"assets/"+BundledDemoScenes.Folder+"/"+Path.GetRelativePath(source,file).Replace('\\','/'));
                var android=BundledDemoScenes.SeedAt("jar:"+new Uri(apk).AbsoluteUri+"!/assets",Path.Combine(scratch,"android","Panoramas"),tree,new[]{person});
                Check(android.Installed==1&&android.PortraitsInstalled==1,"Android APK scenes and portraits seed without HTTP");
                bool refused=false;record.personKey=new string('b',64);File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));try{BundledDemoScenes.SeedAt(streaming,Path.Combine(scratch,"foreign","Panoramas"),tree,new[]{person});}catch(InvalidDataException){refused=true;}
                Check(refused,"Foreign person identities rejected");record.personKey=PanoramaCache.PersonKey(person.Id);File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));
                File.WriteAllBytes(Path.Combine(source,"images",record.file),png.Concat(new byte[]{1}).ToArray());refused=false;try{BundledDemoScenes.SeedAt(streaming,Path.Combine(scratch,"bad","Panoramas"),tree,new[]{person});}catch(InvalidDataException){refused=true;}
                Check(refused,"Corrupt image rejected before cache commit");
                manifest.omittedScenes[0].year=1900;File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));refused=false;try{BundledDemoScenes.SeedAt(streaming,Path.Combine(scratch,"duplicate","Panoramas"),tree,new[]{person});}catch(InvalidDataException){refused=true;}
                Check(refused,"An endpoint cannot be both generated and omitted");
                manifest.omittedScenes[0].year=1980;File.WriteAllBytes(Path.Combine(source,"images",record.file),png);File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest));
                File.WriteAllBytes(Path.Combine(source,"portraits",photo.File),portraitPng.Concat(new byte[]{1}).ToArray());refused=false;
                try{BundledDemoScenes.SeedAt(streaming,Path.Combine(scratch,"badportrait","Panoramas"),tree,new[]{person});}catch(InvalidDataException){refused=true;}
                Check(refused,"Corrupt portrait rejected before media publication");
                var photoOwner=new GameObject("Bundled media gate fixture");
                try
                {
                    var photos=photoOwner.AddComponent<PhotoTextureCache>();photos.BeginBundledMedia();photos.BeginBundledMedia();
                    Check(photos.PreparingBundledMedia,"Overlapping media preparations hold photo reads");photos.EndBundledMedia();
                    Check(photos.PreparingBundledMedia,"Finishing a stale tree seed cannot release a newer seed");photos.EndBundledMedia();
                    Check(!photos.PreparingBundledMedia,"Completing both seeds releases photo reads");photos.EndBundledMedia();
                    Check(!photos.PreparingBundledMedia,"Media gate cannot underflow after cleanup");
                }
                finally{UnityEngine.Object.DestroyImmediate(photoOwner);}
                string result=n+" local bundle IO checks passed; desktop/APK, preservation and corruption; no API calls.";File.WriteAllText("Artifacts/product-demo-bundle-checks.txt",result);return result;
            }
            finally{if(Directory.Exists(scratch))Directory.Delete(scratch,true);}
        }
        public static string Data()
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}
            var data=Resources.Load<TextAsset>("ProductDemoFamily");Check(data!=null,"Product demo is included in Resources");
            var tree=GedcomImporter.Parse(data.text);var id=PanoramaCache.TreeId(data.text);
            var document=TreeDocument.FromTree(tree,id,"Fictional product demo",true);ProductDemoData.Attach(document);document.Validate(true);
            Check(tree.People.Count==37&&tree.Families.Count==18,"37 people and 18 family groups");
            Check(tree.Families.Sum(f=>f.Parents.Count*f.Children.Count)==36&&tree.Warnings.All(w=>w=="5 RESI fact(s) retained in source.ged; not imported into editable fields or location timelines."),"36 valid edges; only the expected preserved-residence import note");
            Check(id!=PanoramaCache.DemoTreeId,"Product and classic demo caches have separate identities");
            Check(id==PanoramaCache.ProductDemoTreeId,"Product cache identity is frozen independently of future dataset expansion");
            Check(id!=PanoramaCache.PreviousProductDemoTreeId,"European and previous York demo caches remain separate");
            var parents=tree.Families.SelectMany(f=>f.Children.Select(c=>(Child:c,Parents:f.Parents))).ToDictionary(x=>x.Child,x=>x.Parents);
            var seen=new HashSet<string>();var generation=new[]{"@EURO001@"};int depth=0;
            while(generation.Length>0)
            {
                Check(generation.Length==(depth<=3?1<<depth:2),"Three complete ancestor generations, then paired parents: "+depth);
                foreach(var person in generation)Check(seen.Add(person),"No duplicate ancestors or cycles");
                if(depth<3)foreach(var person in generation)Check(parents[person].Count==2,"Every recent ancestor has two recorded parents");
                generation=generation.SelectMany(p=>parents.TryGetValue(p,out var list)?list:new List<string>()).ToArray();depth++;
            }
            Check(depth==15&&seen.Count==37,"All people belong to one connected 15-generation tree");
            Check(tree.People["@EURO001@"].Name=="Maya Hartwell"&&LifeTimeline.IsLiving(tree.People["@EURO001@"]),"Modern starting person is living");
            Check(tree.People.Values.Select(p=>{LifeTimeline.Year(p.BirthDate,out int year);return year;}).Min()==1600,"Single branch ends around 1600");
            Check(ProductDemoData.Current.endpoints.Length==74&&ProductDemoData.Current.endpoints.Select(e=>e.place).Distinct().Count()>=25,"Varied European cities and villages across 74 endpoints");
            Check(document.Photos.Count==8&&document.People.Count(p=>p.PhotoIds.Count==1&&!string.IsNullOrEmpty(p.PrimaryPhotoId))==8,"Eight primary portrait associations");
            foreach(var family in tree.Families)Check(family.Parents.Count==2&&family.Parents.Select(p=>tree.People[p].Sex).OrderBy(s=>s).SequenceEqual(new[]{"F","M"}),"Every recorded family has male and female parents");
            foreach(var person in tree.People.Values)
            {
                Check(LifeTimeline.TryRange(person,out int birth,out int end)&&birth<=end,"Usable lifespan for every person");
                Check(person.Sex=="M"||person.Sex=="F","Recorded sex supports connectors");
                Check(LifeEndpointScenes.Targets(person,new ResidenceTimeline()).Count()==(LifeTimeline.IsLiving(person)?1:2),"Every recorded endpoint has a valid European setting");
                var moves=new ResidenceTimeline{entries=ProductDemoData.DefaultMoves(id,person.Id)};
                foreach(var endpoint in ProductDemoData.Current.endpoints.Where(e=>e.personId==person.Id))Check(ResidenceTimeline.Same(endpoint.place,moves.At(person,endpoint.year)),"Catalog settings match playback location compatibility");
            }
            foreach(var family in tree.Families)foreach(string child in family.Children)foreach(string parent in family.Parents)
            {
                LifeTimeline.Year(tree.People[child].BirthDate,out int childBirth);LifeTimeline.Year(tree.People[parent].BirthDate,out int parentBirth);
                Check(childBirth-parentBirth>=20&&childBirth-parentBirth<=45,"Plausible parent ages throughout the lineage");
            }
            var layout=TreeLayout.Build(tree,TreeLayoutSettings.Load());
            Check(layout.Positions.Count==37&&layout.Positions.Values.Select(p=>p.z).Distinct().Count()==15&&layout.Warnings.Count==0,"Every generation lays out without warnings");
            foreach(var position in layout.Positions.Values)Check(layout.Bounds.Contains(position),"Navigation bounds include every person");
            foreach(var route in layout.Connectors)for(int i=1;i<route.Points.Length;i++)
            {var d=route.Points[i]-route.Points[i-1];Check((Mathf.Abs(d.x)>.001f?1:0)+(Mathf.Abs(d.y)>.001f?1:0)+(Mathf.Abs(d.z)>.001f?1:0)<=1,"Connectors remain orthogonal");}
            var legacy=GedcomImporter.Parse(Resources.Load<TextAsset>("DemoFamily").text);
            Check(legacy.People.Count==31&&legacy.People["@I3@"].Name=="Rose Hartwell","Classic demo remains intact");
            string result=n+" product demo data/layout checks passed; no model requests or private family writes.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/product-demo-checks.txt",result);return result;
        }
    }
}
