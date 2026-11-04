using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    [InitializeOnLoad]
    public static class ProductDemoPersistence
    {
        [Serializable] sealed class DetailsRecord {public string details;}
        [Serializable] sealed class ChoiceRecord {public SceneReference photo;}
        [Serializable] sealed class Configuration {public bool enabled;public string judgingRoot;}
        [Serializable] sealed class MirrorState {public List<Stamp> files=new();}
        [Serializable] sealed class Stamp {public string path,sha;}
        static readonly string Root=Path.GetFullPath(".");
        static readonly string State=Path.Combine(Root,"Library","DemoAuthoring");
        static readonly string ResourcesPath=Path.Combine(Root,"Assets","AncestorWalk","Resources");
        static readonly string SnapshotPath=Path.Combine(ResourcesPath,ProductDemoAuthoring.ResourceName+".json");
        static readonly string Bundle=Path.Combine(Root,"Assets","StreamingAssets",BundledDemoScenes.Folder);
        static string storage,tree;static Configuration configuration;static int dirty;static double nextSave;static bool saving;
        public static string Status {get;private set;}="Demo authoring disabled";
        static ProductDemoPersistence(){EditorApplication.delayCall+=Initialize;EditorApplication.update+=Update;EditorApplication.playModeStateChanged+=OnPlayMode;AssemblyReloadEvents.beforeAssemblyReload+=BeforeReload;}
        static void Initialize()
        {
            storage=PanoramaCache.StorageRoot;tree=PanoramaCache.ProductDemoTreeId;
            string path=Path.Combine(State,"config.json");configuration=File.Exists(path)?JsonUtility.FromJson<Configuration>(File.ReadAllText(path)):new Configuration();
            ProductDemoAuthoring.EditorTreeId=configuration.enabled?tree:null;
            ProductDemoAuthoring.EditorLoad=LoadLocal;
            ProductDemoAuthoring.EditorChanged=(root,id)=>
            {
                if(!configuration.enabled||id!=tree||Path.GetFullPath(root)!=Path.GetFullPath(storage))return;
                Directory.CreateDirectory(State);File.WriteAllText(Path.Combine(State,"pending"),"Demo changes awaiting export");Interlocked.Exchange(ref dirty,1);
            };
            if(configuration.enabled){Status="Demo changes save to project assets";if(File.Exists(Path.Combine(State,"pending")))dirty=1;}
        }
        static TreeDocument LoadLocal(TreeDocument fallback)
        {
            if(!configuration.enabled)return fallback;
            string path=Path.Combine(storage,"Trees",tree,"tree.json");if(!File.Exists(path))return fallback;
            var document=new TreeStore(storage).Load(tree);if(!document.IsDemo)throw new InvalidDataException("Saved demo identity is not built-in.");return document;
        }
        [MenuItem("Tools/Ancestor Walk/Demo Authoring/Enable persistent demo editing")]
        public static void Enable()
        {
            Directory.CreateDirectory(State);configuration??=new Configuration();configuration.enabled=true;
            TreeStore.AtomicText(Path.Combine(State,"config.json"),JsonUtility.ToJson(configuration,true));Initialize();
        }
        public static void SetJudgingMirror(string path)
        {
            path=Path.GetFullPath(path);string expected=Path.GetFullPath(Path.Combine(Root,"..","AncestorWalk-Judging"));
            if(path!=expected||Path.GetFileName(Root)!="AncestorWalk"||!Directory.Exists(Path.Combine(path,".git"))||!File.Exists(Path.Combine(path,"Docs","PrivacyReview.md")))throw new InvalidOperationException("Expected the prepared local judging repository.");
            Enable();configuration.judgingRoot=path;TreeStore.AtomicText(Path.Combine(State,"config.json"),JsonUtility.ToJson(configuration,true));
        }
        static void Update()
        {
            if(dirty==0||saving||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<nextSave)return;
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(EditorApplication.isPlaying&&(gallery==null||!gallery.CacheIndexReady||gallery.Library?.Busy==true))return;
            try{ExportNow();}catch(Exception e){Status="Demo export needs attention: "+e.Message;Debug.LogError(Status);nextSave=EditorApplication.timeSinceStartup+15;}
        }
        static void OnPlayMode(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)BeforeReload();}
        static void BeforeReload(){if(dirty!=0&&!saving)try{ExportNow();}catch(Exception e){Debug.LogError("Demo edits remain saved locally; export pending: "+e.Message);}}
        [MenuItem("Tools/Ancestor Walk/Demo Authoring/Save demo for judging now")]
        public static void SaveMenu(){Debug.Log(ExportNow());}
        public static string ExportNow()
        {
            if(configuration==null)Initialize();if(!configuration.enabled)throw new InvalidOperationException("Enable persistent demo editing first.");
            if(saving)throw new InvalidOperationException("Demo export already running.");saving=true;Interlocked.Exchange(ref dirty,0);
            Directory.CreateDirectory(State);File.WriteAllText(Path.Combine(State,"pending"),"Demo export in progress");
            try
            {
                var family=Resources.Load<TextAsset>("ProductDemoFamily");
                var fallback=TreeDocument.FromTree(GedcomImporter.Parse(family.text),tree,"Hartwell Through Time · fictional",true);ProductDemoData.Attach(fallback);
                var document=LoadLocal(File.Exists(SnapshotPath)?JsonUtility.FromJson<ProductDemoAuthoring.Snapshot>(File.ReadAllText(SnapshotPath)).document:fallback).Copy();
                var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
                if(EditorApplication.isPlaying&&gallery?.Document?.Id==tree&&gallery.Document.IsDemo&&gallery.Library?.Busy!=true)document=gallery.Document.Copy();
                string result=ExportAt(Root,storage,document,ProductDemoData.Current);
                if(!string.IsNullOrEmpty(configuration.judgingRoot))Mirror(configuration.judgingRoot);
                if(dirty==0&&File.Exists(Path.Combine(State,"pending")))File.Delete(Path.Combine(State,"pending"));
                Status=result+(string.IsNullOrEmpty(configuration.judgingRoot)?"":"; local judging repo updated");nextSave=EditorApplication.timeSinceStartup+1;
                // Import only the small snapshot; full AssetDatabase.Refresh during Play would reload scripts.
                AssetDatabase.ImportAsset("Assets/AncestorWalk/Resources/"+ProductDemoAuthoring.ResourceName+".json");return Status;
            }
            catch{Interlocked.Exchange(ref dirty,1);throw;}finally{saving=false;}
        }
        public static string ExportAt(string project,string cache,TreeDocument document,ProductDemoData.Catalog catalog)
        {
            // Testable offline exporter: never enumerates other trees, global photos or credentials.
            string resources=Path.Combine(project,"Assets","AncestorWalk","Resources"),bundle=Path.Combine(project,"Assets","StreamingAssets",BundledDemoScenes.Folder);
            string identity=PanoramaCache.Hash(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(resources,"ProductDemoCacheIdentityV2.txt")).Trim()));
            if(document.Id!=identity||!document.IsDemo)throw new InvalidDataException("Only the built-in fictional demo may be exported.");document=document.Copy();document.Validate(true);
            var old=JsonUtility.FromJson<BundledDemoScenes.Manifest>(File.ReadAllText(Path.Combine(bundle,"manifest.json")));
            if(old.treeId!=identity)throw new InvalidDataException("Bundle identity mismatch.");
            string stage=Path.Combine(project,"Library","DemoAuthoring","stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            var writes=new Dictionary<string,string>();var photoMap=new Dictionary<string,PhotoRecord>();var imageMap=new Dictionary<string,string>();
            byte[] Read(string path){RejectLinks(path);var info=new FileInfo(path);if(info.Length>32L*1024*1024)throw new InvalidDataException("Demo media exceeds 32 MB.");return File.ReadAllBytes(path);}
            void Stage(string relative,byte[] bytes)
            {
                Scan(bytes,relative);string current=Path.Combine(bundle,relative);
                if(relative!="manifest.json"&&File.Exists(current)&&PanoramaCache.Hash(File.ReadAllBytes(current))==PanoramaCache.Hash(bytes)){writes[relative]=current;return;}
                string path=Path.Combine(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);writes[relative]=path;
            }
            string PhotoFile(PhotoRecord photo)
            {
                string local=Path.Combine(cache,"Photos",photo.File);return File.Exists(local)?local:Path.Combine(bundle,"portraits",photo.File);
            }
            // Older scene-photo picks were saved only as exact-year references. Adopt only
            // files explicitly selected for this demo, never browse the shared photo folder.
            foreach(var person in document.People)
            {
                var references=PanoramaCache.YearRecordsAt(Path.Combine(cache,"Panoramas"),identity,person.Id).Select(r=>SceneReference.Optional(r.reference)).Where(r=>r?.IsPhoto==true).ToList();
                string choices=Path.Combine(cache,"ScenePhotoChoices",identity,PanoramaCache.PersonKey(person.Id));
                if(Directory.Exists(choices))foreach(string path in Directory.GetFiles(choices,"*.json"))
                {RejectLinks(path);if(new FileInfo(path).Length>4096)throw new InvalidDataException("Invalid demo photo choice.");var choice=SceneReference.Optional(JsonUtility.FromJson<ChoiceRecord>(File.ReadAllText(path))?.photo);if(choice!=null)references.Add(choice);}
                foreach(var reference in references)
                {
                    SceneReference.Validate(reference,reference.place);if(!reference.IsPhoto)throw new InvalidDataException("Invalid demo photo choice.");
                    if(document.Photos.Any(p=>p.Id==reference.sha256))continue;
                    byte[] bytes=Read(Path.Combine(cache,"Photos",reference.file));if(PanoramaCache.Hash(bytes)!=reference.sha256)throw new InvalidDataException("Selected demo photo checksum mismatch.");
                    PhotoHeader.Read(bytes,out int width,out int height,out int orientation);
                    document.Photos.Add(new PhotoRecord{Id=reference.sha256,File=reference.file,Title="Demo scene photo",Width=width,Height=height,Orientation=orientation});
                }
            }
            foreach(var photo in document.Photos)
            {
                var bytes=Read(PhotoFile(photo));if(PanoramaCache.Hash(bytes)!=photo.Id)throw new InvalidDataException("Demo photo checksum mismatch.");
                PhotoHeader.Read(bytes,out int width,out int height,out _);if(width!=photo.Width||height!=photo.Height)throw new InvalidDataException("Demo photo dimensions mismatch.");
                if(!old.portraits.Any(p=>p.photo.Id==photo.Id))bytes=StripMetadata(bytes);string hash=PanoramaCache.Hash(bytes);var copy=JsonUtility.FromJson<PhotoRecord>(JsonUtility.ToJson(photo));copy.Id=hash;copy.File=hash+(bytes[0]==137?".png":".jpg");copy.OriginalPath="";photoMap.Add(photo.Id,copy);
                Stage("portraits/"+copy.File,bytes);
            }
            foreach(var photo in photoMap.Values)if(!string.IsNullOrEmpty(photo.SourcePhotoId))
            {if(!photoMap.TryGetValue(photo.SourcePhotoId,out var source))throw new InvalidDataException("Cutout source must be attached to the demo.");photo.SourcePhotoId=source.Id;}
            foreach(var person in document.People)
            {person.PhotoIds=person.PhotoIds.Select(id=>photoMap[id].Id).Distinct().ToList();if(!string.IsNullOrEmpty(person.PrimaryPhotoId))person.PrimaryPhotoId=photoMap[person.PrimaryPhotoId].Id;}
            document.Photos=photoMap.Values.GroupBy(p=>p.Id).Select(g=>g.First()).ToList();document.SourceGedcom="";document.Warnings.Clear();document.SourceFingerprint=identity;
            var prior=File.Exists(Path.Combine(resources,ProductDemoAuthoring.ResourceName+".json"))?JsonUtility.FromJson<ProductDemoAuthoring.Snapshot>(File.ReadAllText(Path.Combine(resources,ProductDemoAuthoring.ResourceName+".json"))):null;
            var locations=new List<ResidenceTimeline>();
            foreach(var person in document.People)
            {
                string key=PanoramaCache.PersonKey(person.Id),path=Path.Combine(cache,"Locations",identity,key+".json");
                var location=File.Exists(path)?JsonUtility.FromJson<ResidenceTimeline>(File.ReadAllText(path)):prior?.locations.FirstOrDefault(l=>l.personKey==key)??new ResidenceTimeline{treeId=identity,personKey=key,entries=(catalog.moves??Array.Empty<ProductDemoData.Move>()).Where(m=>m.personId==person.Id).Select(m=>new Residence{year=m.year,place=m.place}).ToList()};locations.Add(location);
            }
            var settings=(prior?.settings??Array.Empty<ProductDemoAuthoring.YearSetting>()).Where(e=>document.People.Any(p=>PanoramaCache.PersonKey(p.Id)==e.personKey)).ToDictionary(e=>e.personKey+"/"+e.year,e=>e);
            foreach(var person in document.People)
            {
                string key=PanoramaCache.PersonKey(person.Id);
                foreach(string folder in new[]{"SceneDetails","ScenePhotoChoices"})
                {
                    string directory=Path.Combine(cache,folder,identity,key);if(!Directory.Exists(directory))continue;
                    RejectLinks(directory);
                    foreach(string path in Directory.GetFiles(directory,"*.json"))
                    {
                        if(!int.TryParse(Path.GetFileNameWithoutExtension(path),out int year)||year<1||year>9999)throw new InvalidDataException("Invalid saved demo setting year.");
                        if(new FileInfo(path).Length>8192)throw new InvalidDataException("Demo year setting too large.");
                        string id=key+"/"+year;if(!settings.TryGetValue(id,out var setting))settings[id]=setting=new ProductDemoAuthoring.YearSetting{personKey=key,year=year};
                        string text=File.ReadAllText(path);
                        if(folder=="SceneDetails")setting.details=SceneYearDetails.Normalize(JsonUtility.FromJson<DetailsRecord>(text)?.details);
                        else
                        {
                            setting.photo=SceneReference.Copy(JsonUtility.FromJson<ChoiceRecord>(text)?.photo);
                            if(setting.photo!=null){if(!photoMap.TryGetValue(setting.photo.sha256,out var photo))throw new InvalidDataException("Chosen scene photo must be attached to the demo.");setting.photo.sha256=photo.Id;setting.photo.file=photo.File;}
                        }
                    }
                }
            }
            var snapshot=new ProductDemoAuthoring.Snapshot{document=document,locations=locations.ToArray(),settings=settings.Values.OrderBy(e=>e.personKey).ThenBy(e=>e.year).ToArray()};ProductDemoAuthoring.Validate(snapshot,identity);
            string json=JsonUtility.ToJson(snapshot,true);Scan(Encoding.UTF8.GetBytes(json),"demo document");
            var entries=new List<BundledDemoScenes.Entry>();
            foreach(var person in document.People)
            {
                var records=old.scenes.Where(e=>e.personId==person.Id).ToDictionary(e=>e.record.year,e=>e.record);
                foreach(var record in PanoramaCache.YearRecordsAt(Path.Combine(cache,"Panoramas"),identity,person.Id))records[record.year]=record;
                if(!LifeEndpointScenes.TrySceneRange(person,out int start,out int end))continue;
                foreach(var original in records.Values.OrderBy(r=>r.year))
                {
                    if(original.year<start||original.year>end)continue;
                    var location=locations.Single(l=>l.personKey==PanoramaCache.PersonKey(person.Id));if(!ResidenceTimeline.Same(original.place,location.At(person,original.year)))continue;
                    var record=JsonUtility.FromJson<PanoramaRecord>(JsonUtility.ToJson(original));
                    string ImagePath(string sha,int year)
                    {
                        TreeStore.CheckId(sha);string local=Path.Combine(cache,"Panoramas","people",identity,PanoramaCache.PersonKey(person.Id),"years",year.ToString(),sha+".png");
                        if(File.Exists(local))return local;
                        if(!old.scenes.Any(e=>e.personId==person.Id&&e.record.year==year&&e.record.sha256==sha))throw new InvalidDataException("Reference is not from this demo person.");
                        return Path.Combine(bundle,"images",sha+".png");
                    }
                    string AddImage(string sha,int year)
                    {
                        if(imageMap.TryGetValue(sha,out string existing))return existing;
                        byte[] bytes=Read(ImagePath(sha,year));if(PanoramaCache.Hash(bytes)!=sha)throw new InvalidDataException("Demo scene checksum mismatch.");PanoramaCache.ValidatePng(bytes);
                        if(!old.scenes.Any(e=>e.record.sha256==sha))bytes=StripMetadata(bytes);string hash=PanoramaCache.Hash(bytes);Stage("images/"+hash+".png",bytes);imageMap[sha]=hash;return hash;
                    }
                    foreach(var reference in new[]{SceneReference.Optional(record.reference),SceneReference.Optional(record.basePanorama)})
                    {
                        if(reference==null)continue;SceneReference.Validate(reference,record.place);
                        if(reference.IsPhoto){if(!photoMap.TryGetValue(reference.sha256,out var photo))throw new InvalidDataException("Scene photo must be attached to the demo before export.");reference.sha256=photo.Id;reference.file=photo.File;}
                        else reference.sha256=AddImage(reference.sha256,reference.year);
                    }
                    record.sha256=AddImage(original.sha256,record.year);record.file=record.sha256+".png";
                    string preview=Path.Combine(cache,"CubeThumbnails","v1",original.sha256+".rgba");if(!File.Exists(preview))preview=Path.Combine(bundle,"previews",original.sha256+".rgba");
                    byte[] thumbnail=File.Exists(preview)?Read(preview):MakePreview(File.ReadAllBytes(writes["images/"+record.file]));
                    if(thumbnail.Length!=524352||Encoding.ASCII.GetString(thumbnail,0,64)!=PanoramaCache.Hash(thumbnail.Skip(64).ToArray()))throw new InvalidDataException("Invalid demo preview.");
                    Stage("previews/"+record.sha256+".rgba",thumbnail);entries.Add(new BundledDemoScenes.Entry{personId=person.Id,record=record,thumbnailSha256=PanoramaCache.Hash(thumbnail)});
                }
            }
            var photos=document.Photos.Select(photo=>new BundledDemoScenes.Portrait{personId=document.People.FirstOrDefault(p=>p.PhotoIds.Contains(photo.Id))?.Id??document.People.FirstOrDefault()?.Id,photo=photo,model=photo.CutoutModel??"demo-author-selected",prompt="Explicitly attached to the fictional demo."}).ToArray();
            var omissions=new List<BundledDemoScenes.Omission>();
            foreach(var p in document.People)
                if(LifeEndpointScenes.TrySceneRange(p,out int start,out int end))foreach(int year in new[]{start,end}.Distinct())
                    if(!entries.Any(e=>e.personId==p.Id&&e.record.year==year))omissions.Add(new BundledDemoScenes.Omission{personId=p.Id,year=year,reason=old.omittedScenes?.FirstOrDefault(o=>o.personId==p.Id&&o.year==year)?.reason??"No authored image saved for this endpoint."});
            var manifest=new BundledDemoScenes.Manifest{version=3,treeId=identity,sceneYear=catalog.sceneYear,documentSha256=PanoramaCache.Hash(Encoding.UTF8.GetBytes(json)),scenes=entries.ToArray(),portraits=photos,omittedScenes=omissions.ToArray()};
            BundledDemoScenes.ValidateManifest(manifest,identity,document.People.ToArray());Stage("manifest.json",Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest,true)));
            // Publish media first, then the paired small metadata files; backups allow recovery.
            foreach(var entry in writes.Where(e=>e.Key!="manifest.json"))CopyChanged(entry.Value,Path.Combine(bundle,entry.Key));
            string snapshotPath=Path.Combine(resources,ProductDemoAuthoring.ResourceName+".json"),manifestPath=Path.Combine(bundle,"manifest.json");
            string oldSnapshot=File.Exists(snapshotPath)?File.ReadAllText(snapshotPath):null,oldManifest=File.ReadAllText(manifestPath);
            try{TreeStore.AtomicText(snapshotPath,json);TreeStore.AtomicText(manifestPath,File.ReadAllText(writes["manifest.json"]));}
            catch{if(oldSnapshot!=null)TreeStore.AtomicText(snapshotPath,oldSnapshot);else if(File.Exists(snapshotPath))File.Delete(snapshotPath);TreeStore.AtomicText(manifestPath,oldManifest);throw;}
            EnsureMeta(snapshotPath);foreach(var entry in writes)EnsureMeta(Path.Combine(bundle,entry.Key));
            Directory.Delete(stage,true);
            return "Saved fictional demo: "+document.People.Count+" people, "+entries.Count+" scenes, "+photos.Length+" photos";
        }
        static byte[] MakePreview(byte[] png)
        {
            var full=new Texture2D(2,2,TextureFormat.RGBA32,false);var small=new Texture2D(512,256,TextureFormat.RGBA32,false);RenderTexture rt=null;var previous=RenderTexture.active;
            try
            {
                if(!ImageConversion.LoadImage(full,png,false))throw new InvalidDataException("Invalid demo panorama.");
                rt=RenderTexture.GetTemporary(512,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);Graphics.Blit(full,rt);RenderTexture.active=rt;small.ReadPixels(new Rect(0,0,512,256),0,0);small.Apply();
                byte[] raw=small.GetRawTextureData<byte>().ToArray();return Encoding.ASCII.GetBytes(PanoramaCache.Hash(raw)).Concat(raw).ToArray();
            }
            finally{RenderTexture.active=previous;if(rt!=null)RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(full);UnityEngine.Object.DestroyImmediate(small);}
        }
        public static byte[] StripMetadata(byte[] data)
        {
            using var output=new MemoryStream();
            if(data.Length>=8&&data[0]==137)
            {
                output.Write(data,0,8);int at=8;bool ended=false;
                while(at+12<=data.Length)
                {
                    long n=((long)data[at]<<24)|((long)data[at+1]<<16)|((long)data[at+2]<<8)|data[at+3];if(n>data.Length-at-12)throw new InvalidDataException("Truncated PNG.");
                    string kind=Encoding.ASCII.GetString(data,at+4,4);int count=(int)n+12;
                    if(kind!="eXIf"&&kind!="tEXt"&&kind!="iTXt"&&kind!="zTXt")output.Write(data,at,count);
                    at+=count;if(kind=="IEND"){ended=true;break;}
                }
                if(!ended||at!=data.Length)throw new InvalidDataException("Invalid PNG end.");return output.ToArray();
            }
            if(data.Length<4||data[0]!=255||data[1]!=216)throw new InvalidDataException("Demo photos must be PNG or JPEG.");
            output.Write(data,0,2);int offset=2;
            while(offset<data.Length)
            {
                int start=offset;if(data[offset++]!=255)throw new InvalidDataException("Invalid JPEG marker.");while(offset<data.Length&&data[offset]==255)offset++;if(offset>=data.Length)break;int marker=data[offset++];
                if(marker==0xDA){output.Write(data,start,data.Length-start);return output.ToArray();}
                if(marker==0xD9){output.Write(data,start,offset-start);return output.ToArray();}
                if(offset+2>data.Length)break;int length=(data[offset]<<8)|data[offset+1];if(length<2||offset+length>data.Length)break;
                if(!(marker>=0xE1&&marker<=0xEF)&&marker!=0xFE)output.Write(data,start,offset+length-start);offset+=length;
            }
            throw new InvalidDataException("Truncated JPEG.");
        }
        public static void Scan(byte[] bytes,string label)
        {
            // New image metadata was stripped before staging; baseline media was audited.
            // Scan text, not compressed pixels or raw preview data (which may contain arbitrary bytes).
            if(label.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||label.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)||label.EndsWith(".rgba",StringComparison.OrdinalIgnoreCase))return;
            string text=Encoding.UTF8.GetString(bytes);
            foreach(string pattern in new[]{@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",@"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",@"\b(?:gh[pousr]_[A-Za-z0-9]{30,100}|github_pat_[A-Za-z0-9_]{30,150})",@"\bsk-(?:proj-)?[A-Za-z0-9_-]{32,150}",@"\bEAA[A-Za-z0-9]{50,300}\b",@"/Us"+@"ers/[^/\s""']+",@"[A-Za-z]:\\Users\\",@"\b192\.168\.\d{1,3}\.\d{1,3}\b"})
                if(Regex.IsMatch(text,pattern))throw new InvalidDataException("Private path or credential pattern in "+label+". Export kept local for review.");
        }
        static void RejectLinks(string path)
        {for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked demo paths are not allowed.");}
        static void CopyChanged(string from,string to)
        {
            RejectLinks(from);RejectLinks(to);if(Path.GetFullPath(from)==Path.GetFullPath(to))return;byte[] bytes=File.ReadAllBytes(from);if(File.Exists(to)&&PanoramaCache.Hash(File.ReadAllBytes(to))==PanoramaCache.Hash(bytes))return;
            Directory.CreateDirectory(Path.GetDirectoryName(to));string temp=to+".tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(to))File.Replace(temp,to,null);else File.Move(temp,to);
        }
        static void EnsureMeta(string path)
        {
            if(File.Exists(path+".meta"))return;
            string content="fileFormatVersion: 2\nguid: "+Guid.NewGuid().ToString("N")+"\n";
            File.WriteAllText(path+".meta",content);
        }
        static void Mirror(string destination)
        {
            RejectLinks(destination);if(Path.GetFullPath(destination)!=Path.GetFullPath(Path.Combine(Root,"..","AncestorWalk-Judging"))||!Directory.Exists(Path.Combine(destination,".git")))throw new InvalidOperationException("Judging destination changed.");
            string statePath=Path.Combine(State,"mirror.json");var state=File.Exists(statePath)?JsonUtility.FromJson<MirrorState>(File.ReadAllText(statePath)):new MirrorState();
            var previous=state.files.ToDictionary(s=>s.path,s=>s.sha);var files=new List<string>();
            foreach(string path in Directory.GetFiles(Bundle,"*",SearchOption.AllDirectories))
            {
                string rel=Path.GetRelativePath(Bundle,path).Replace('\\','/');
                if(rel=="manifest.json"||rel=="manifest.json.meta"||Regex.IsMatch(rel,@"\A(?:images/[a-f0-9]{64}\.png|portraits/[a-f0-9]{64}\.(?:png|jpg)|previews/[a-f0-9]{64}\.rgba)(?:\.meta)?\z"))files.Add(path);
            }
            files.Add(SnapshotPath);files.Add(SnapshotPath+".meta");
            foreach(string path in files)
            {
                string relative=Path.GetRelativePath(Root,path);string target=Path.Combine(destination,relative);byte[] bytes=File.ReadAllBytes(path);Scan(bytes,relative);
                if(File.Exists(target))
                {
                    string hash=PanoramaCache.Hash(File.ReadAllBytes(target));
                    if(previous.TryGetValue(relative,out string expected)?hash!=expected&&hash!=PanoramaCache.Hash(bytes):hash!=PanoramaCache.Hash(bytes))throw new IOException("Judging file has independent edits: "+relative);
                }
            }
            foreach(string path in files){string relative=Path.GetRelativePath(Root,path);CopyChanged(path,Path.Combine(destination,relative));previous[relative]=PanoramaCache.Hash(File.ReadAllBytes(path));}
            state.files=previous.Select(p=>new Stamp{path=p.Key,sha=p.Value}).ToList();TreeStore.AtomicText(statePath,JsonUtility.ToJson(state,true));
            TreeStore.AtomicText(Path.Combine(State,"last-mirror.json"),JsonUtility.ToJson(new MirrorState{files=state.files},true));
        }
        // Seed the conflict guard only after the agent has reviewed the existing snapshot.
        public static void AcceptCurrentMirrorBaseline()
        {
            if(string.IsNullOrEmpty(configuration?.judgingRoot))throw new InvalidOperationException("Set the local judging mirror first.");
            var state=new MirrorState();string prefix="Assets/StreamingAssets/"+BundledDemoScenes.Folder;
            foreach(var path in Directory.GetFiles(Path.Combine(configuration.judgingRoot,prefix),"*",SearchOption.AllDirectories))
                state.files.Add(new Stamp{path=Path.GetRelativePath(configuration.judgingRoot,path),sha=PanoramaCache.Hash(File.ReadAllBytes(path))});
            string snapshot=Path.Combine(configuration.judgingRoot,"Assets/AncestorWalk/Resources/"+ProductDemoAuthoring.ResourceName+".json");
            foreach(string path in new[]{snapshot,snapshot+".meta"})if(File.Exists(path))state.files.Add(new Stamp{path=Path.GetRelativePath(configuration.judgingRoot,path),sha=PanoramaCache.Hash(File.ReadAllBytes(path))});
            TreeStore.AtomicText(Path.Combine(State,"mirror.json"),JsonUtility.ToJson(state,true));
        }
    }
}
