using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ProductDemoPersistenceChecks
    {
        public static string Run()
        {
            int count=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
            string root=Path.GetFullPath("Library/DemoAuthoringChecks/"+Guid.NewGuid().ToString("N"));string resources=Path.Combine(root,"Assets/AncestorWalk/Resources"),streaming=Path.Combine(root,"Assets/StreamingAssets"),bundle=Path.Combine(streaming,BundledDemoScenes.Folder),cache=Path.Combine(root,"cache");
            Directory.CreateDirectory(resources);Directory.CreateDirectory(bundle);Directory.CreateDirectory(cache);
            string seed="isolated fictional authoring fixture",tree=PanoramaCache.Hash(Encoding.UTF8.GetBytes(seed));File.WriteAllText(Path.Combine(resources,"ProductDemoCacheIdentityV2.txt"),seed);
            var p=new Person{Id="child",Name="Fictional edited child",BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"};
            var parent=new Person{Id="parent",Name="Fictional parent",BirthDate="1870",DeathDate="1940",BirthPlace="York",DeathPlace="York"};
            var document=new TreeDocument{Id=tree,Name="Edited fictional demo",IsDemo=true,SourceGedcom="LOCAL SOURCE MUST NOT SHIP"};document.People.AddRange(new[]{p,parent});document.Families.Add(new FamilyRecord{Id="family",Parents={parent.Id},Children={p.Id}});
            var old=new BundledDemoScenes.Manifest{version=2,treeId=tree,sceneYear=2026,scenes=Array.Empty<BundledDemoScenes.Entry>(),portraits=Array.Empty<BundledDemoScenes.Portrait>(),omittedScenes=Array.Empty<BundledDemoScenes.Omission>()};File.WriteAllText(Path.Combine(bundle,"manifest.json"),JsonUtility.ToJson(old));
            var catalog=new ProductDemoData.Catalog{sceneYear=2026,moves=Array.Empty<ProductDemoData.Move>()};
            var tex=new Texture2D(256,128,TextureFormat.RGBA32,false);byte[] png;
            try{tex.SetPixels32(Enumerable.Repeat(new Color32(17,45,73,255),256*128).ToArray());tex.Apply();png=tex.EncodeToPNG();}finally{UnityEngine.Object.DestroyImmediate(tex);}
            var photoBytes=WithText(png,"Private metadata fixture");string photoHash=PanoramaCache.Hash(photoBytes);
            var photo=new PhotoRecord{Id=photoHash,File=photoHash+".png",Title="Fictional subject",OriginalPath="local-only-photo.png",Width=256,Height=128};document.Photos.Add(photo);p.PhotoIds.Add(photo.Id);p.PrimaryPhotoId=photo.Id;
            Directory.CreateDirectory(Path.Combine(cache,"Photos"));File.WriteAllBytes(Path.Combine(cache,"Photos",photo.File),photoBytes);
            string personKey=PanoramaCache.PersonKey(p.Id),locationFolder=Path.Combine(cache,"Locations",tree);Directory.CreateDirectory(locationFolder);
            var locations=new ResidenceTimeline{treeId=tree,personKey=personKey,entries={new Residence{year=1900,place="York"},new Residence{year=1920,place="Bath"}}};File.WriteAllText(Path.Combine(locationFolder,personKey+".json"),JsonUtility.ToJson(locations));
            SceneYearDetails.Save(cache,tree,p.Id,1900,"A red bicycle near the door.");ScenePhotoChoices.Save(cache,tree,p.Id,1900,SceneReference.FromPhoto(photo,1900,"York"));
            var sceneBytes=WithText(png,"Scene metadata fixture");string sha=PanoramaCache.Hash(sceneBytes);
            var job=new SceneJob{sha256=sha,place="York",prompt="Synthetic fixture",model="muse-image-1.0",reference=SceneReference.FromPhoto(photo,1900,"York"),horizonLift=12};
            PanoramaCache.SaveDecodedAt(Path.Combine(cache,"Panoramas"),tree,p.Id,1900,sceneBytes,job,256,128);
            string previews=Path.Combine(cache,"CubeThumbnails/v1");Directory.CreateDirectory(previews);byte[] raw=new byte[512*256*4];File.WriteAllBytes(Path.Combine(previews,sha+".rgba"),Encoding.ASCII.GetBytes(PanoramaCache.Hash(raw)).Concat(raw).ToArray());
            // Unrelated app data must never be visited or copied.
            Directory.CreateDirectory(Path.Combine(cache,"Trees",new string('b',64)));File.WriteAllText(Path.Combine(cache,"Trees",new string('b',64),"private.txt"),"PRIVATE TREE FIXTURE");File.WriteAllText(Path.Combine(cache,"Photos","unattached.png"),"PRIVATE PHOTO FIXTURE");
            try
            {
                ProductDemoPersistence.ExportAt(root,cache,document,catalog);
                string snapshotPath=Path.Combine(resources,ProductDemoAuthoring.ResourceName+".json"),manifestPath=Path.Combine(bundle,"manifest.json");
                var snapshot=JsonUtility.FromJson<ProductDemoAuthoring.Snapshot>(File.ReadAllText(snapshotPath));var manifest=JsonUtility.FromJson<BundledDemoScenes.Manifest>(File.ReadAllText(manifestPath));
                Check(snapshot.document.People[0].Name==p.Name&&snapshot.document.Families[0].Parents[0]==parent.Id,"People and relationships persist");
                Check(snapshot.locations.Single(l=>l.personKey==personKey).entries.Last().place=="Bath","Location moves persist");
                Check(snapshot.settings.Single().details.Contains("red bicycle"),"Unsubmitted creative details persist");
                Check(snapshot.document.SourceGedcom==""&&snapshot.document.Photos[0].OriginalPath=="","Local paths and source archive omitted");
                string cleanHash=PanoramaCache.Hash(png);Check(snapshot.document.Photos[0].Id==cleanHash&&snapshot.document.People[0].PrimaryPhotoId==cleanHash,"Photo metadata stripped and associations remapped");
                Check(snapshot.settings.Single().photo.sha256==cleanHash&&manifest.scenes.Single().record.reference.sha256==cleanHash,"Photo choices and scene references remapped");
                Check(manifest.scenes.Single().record.sha256==cleanHash,"Generated scene metadata stripped");
                Check(manifest.documentSha256==PanoramaCache.Hash(File.ReadAllBytes(snapshotPath)),"Manifest pins document revision");
                Check(!Directory.GetFiles(bundle,"*",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f).Contains("PRIVATE TREE FIXTURE")||File.ReadAllText(f).Contains("PRIVATE PHOTO FIXTURE")),"Unattached/private data excluded");
                var fresh=BundledDemoScenes.SeedAt(streaming,Path.Combine(root,"fresh/Panoramas"),tree,snapshot.document.People.ToArray());
                Check(fresh.Installed==1&&fresh.PortraitsInstalled==1,"Fresh machine receives authored scene and photo");
                Check(BundledDemoScenes.SeedAt(streaming,Path.Combine(root,"fresh/Panoramas"),tree,snapshot.document.People.ToArray()).Preserved==1,"Repeated seeding preserves cache");
                byte[] before=File.ReadAllBytes(snapshotPath),beforeManifest=File.ReadAllBytes(manifestPath);
                ProductDemoPersistence.ExportAt(root,cache,document,catalog);Check(before.SequenceEqual(File.ReadAllBytes(snapshotPath))&&beforeManifest.SequenceEqual(File.ReadAllBytes(manifestPath)),"Repeated export is stable");
                var privateDoc=document.Copy();privateDoc.Id=new string('c',64);privateDoc.IsDemo=false;bool rejected=false;
                try{ProductDemoPersistence.ExportAt(root,cache,privateDoc,catalog);}catch(InvalidDataException){rejected=true;}Check(rejected,"Private trees rejected");
                document.People[0].Name="ghp_"+new string('x',36);rejected=false;try{ProductDemoPersistence.ExportAt(root,cache,document,catalog);}catch(InvalidDataException){rejected=true;}Check(rejected&&before.SequenceEqual(File.ReadAllBytes(snapshotPath))&&beforeManifest.SequenceEqual(File.ReadAllBytes(manifestPath)),"Secret rejection preserves published metadata");document.People[0].Name=p.Name="Fictional edited child";
                File.WriteAllBytes(Path.Combine(cache,"Photos",photo.File),new byte[]{1,2,3});rejected=false;try{ProductDemoPersistence.ExportAt(root,cache,document,catalog);}catch(InvalidDataException){rejected=true;}Check(rejected&&before.SequenceEqual(File.ReadAllBytes(snapshotPath)),"Corrupt photo preserves prior snapshot");File.WriteAllBytes(Path.Combine(cache,"Photos",photo.File),photoBytes);
                string previousId=ProductDemoAuthoring.EditorTreeId;try{ProductDemoAuthoring.EditorTreeId=tree;Check(ProductDemoAuthoring.CanEdit(document)&&!ProductDemoAuthoring.CanEdit(privateDoc),"Only configured built-in demo editable");}finally{ProductDemoAuthoring.EditorTreeId=previousId;}
                return "PASS: "+count+" demo persistence checks; document, media, locations, settings, clean seeding, privacy and failure preservation.";
            }
            finally{Directory.Delete(root,true);}
        }
        static byte[] WithText(byte[] png,string text)
        {
            byte[] payload=Encoding.ASCII.GetBytes("Comment\0"+text),type=Encoding.ASCII.GetBytes("tEXt");using var stream=new MemoryStream();stream.Write(png,0,png.Length-12);
            int length=payload.Length;stream.Write(new[]{(byte)(length>>24),(byte)(length>>16),(byte)(length>>8),(byte)length},0,4);stream.Write(type,0,4);stream.Write(payload,0,payload.Length);
            uint crc=0xffffffff;foreach(byte b in type.Concat(payload)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}crc^=0xffffffff;
            stream.Write(new[]{(byte)(crc>>24),(byte)(crc>>16),(byte)(crc>>8),(byte)crc},0,4);stream.Write(png,png.Length-12,12);return stream.ToArray();
        }
    }
}
