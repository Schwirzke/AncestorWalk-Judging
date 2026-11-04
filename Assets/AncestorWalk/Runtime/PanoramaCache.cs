using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using UnityEngine;

namespace AncestorWalk
{
    [Serializable]
    public sealed class PanoramaRecord
    {
        public int version = 1;
        public string model, prompt, createdUtc, sha256, file;
        public int width, height, year,policyRetryLevel;
        public string treeId, personKey, place, details;
        public SceneSettingBrief settingBrief;
        public SceneReference reference,basePanorama;public string generationMode;
        public float horizonLift = 12;
    }

    public static class PanoramaCache
    {
        sealed class ValidatedImage
        {
            public PanoramaRecord Record;
            public string Image,Tree,Person;public int Year;public long ManifestTime,ManifestSize,ImageTime,ImageSize;
            public bool Unchanged(string manifest)
            {var m=new FileInfo(manifest);var i=new FileInfo(Image);return m.Exists&&i.Exists&&m.LastWriteTimeUtc.Ticks==ManifestTime&&m.Length==ManifestSize&&i.LastWriteTimeUtc.Ticks==ImageTime&&i.Length==ImageSize;}
        }
        static readonly ConcurrentDictionary<string,object> commitLocks=new();
        static readonly ConcurrentDictionary<string,ValidatedImage> validatedImages=new();
        static object CommitLock(string root)=>commitLocks.GetOrAdd(Path.GetFullPath(Path.Combine(root,"scene.json")),_=>new object());
        static void RememberImage(string root,string manifestName,PanoramaRecord record)
        {
            string path=Path.GetFullPath(Path.Combine(root,manifestName)),image=Path.Combine(root,record.file);
            var m=new FileInfo(path);var i=new FileInfo(image);
            validatedImages[path]=new ValidatedImage{Record=Copy(record),Image=image,Tree=record.treeId,Person=record.personKey,Year=record.year,ManifestTime=m.LastWriteTimeUtc.Ticks,ManifestSize=m.Length,ImageTime=i.LastWriteTimeUtc.Ticks,ImageSize=i.Length};
        }
        static PanoramaRecord Copy(PanoramaRecord r)=>new PanoramaRecord{version=r.version,model=r.model,prompt=r.prompt,createdUtc=r.createdUtc,sha256=r.sha256,file=r.file,width=r.width,height=r.height,year=r.year,policyRetryLevel=r.policyRetryLevel,treeId=r.treeId,personKey=r.personKey,place=r.place,details=r.details,horizonLift=r.horizonLift,settingBrief=SceneSettingBrief.Copy(r.settingBrief),reference=SceneReference.Copy(r.reference),basePanorama=SceneReference.Copy(r.basePanorama),generationMode=r.generationMode};
        static PanoramaRecord IndexedRecord(string root,string tree,string person,int year)
        {
            lock(CommitLock(root))
            {
                string path=Path.GetFullPath(Path.Combine(root,"scene.json"));
                if(validatedImages.TryGetValue(path,out var stamp)&&stamp.Tree==tree&&stamp.Person==PersonKey(person)&&stamp.Year==year&&stamp.Unchanged(path))return Copy(stamp.Record);
                Read(out var record,root,"scene.json");return record;
            }
        }
        // Indexing primes this stamp. Unchanged images need metadata checks, not another PNG read/hash.
        public static bool HasImageAt(string directory,string tree,string person,int year)
        {
            string root=Path.Combine(directory,"people",tree,PersonKey(person),"years",year.ToString());
            lock(CommitLock(root))return HasImageInRoot(root,tree,person,year);
        }
        static bool HasImageInRoot(string root,string tree,string person,int year)
        {
            try
            {
                string path=Path.GetFullPath(Path.Combine(root,"scene.json"));
                if(validatedImages.TryGetValue(path,out var stamp)&&stamp.Tree==tree&&stamp.Person==PersonKey(person)&&stamp.Year==year&&stamp.Unchanged(path))return true;
                Read(out var record,root,"scene.json");return record.treeId==tree&&record.personKey==PersonKey(person)&&record.year==year;
            }
            catch(InvalidDataException){return false;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}
        }
        // Per-thread counters isolate synchronous save work from thumbnail workers.
        [ThreadStatic] static long imageReads,imageValidations;
        public static long ImageReadCount=>imageReads;
        public static long ImageValidationCount=>imageValidations;
        public static string StorageRoot
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");
                using var files=activity.Call<AndroidJavaObject>("getFilesDir");
                return files.Call<string>("getAbsolutePath");
#else
                return Application.persistentDataPath;
#endif
            }
        }
        public static string DirectoryPath => Path.Combine(StorageRoot,"Panoramas");
        static string NormalizeGedcom(string gedcom)=>gedcom.Replace("\r\n","\n").Trim();
        public static string TreeId(string gedcom)
        {
            // The bundled fictional demo can grow without orphaning saved scenes/moves.
            // Other imports retain their normal content-derived identity.
            string normalized=NormalizeGedcom(gedcom);
            var demo=Resources.Load<TextAsset>("DemoFamily");
            if(demo!=null&&normalized==NormalizeGedcom(demo.text))return DemoTreeId;
            var product=Resources.Load<TextAsset>("ProductDemoFamily");
            if(product!=null&&normalized==NormalizeGedcom(product.text))return ProductDemoTreeId;
            return Hash(Encoding.UTF8.GetBytes(normalized));
        }
        public static string DemoTreeId => Hash(Encoding.UTF8.GetBytes(NormalizeGedcom(Resources.Load<TextAsset>("DemoCacheIdentity").text)));
        public static string PreviousProductDemoTreeId=>Hash(Encoding.UTF8.GetBytes(NormalizeGedcom(Resources.Load<TextAsset>("ProductDemoCacheIdentity").text)));
        public static string ProductDemoTreeId=>Hash(Encoding.UTF8.GetBytes(NormalizeGedcom(Resources.Load<TextAsset>("ProductDemoCacheIdentityV2").text)));
        public static string PersonKey(string personId) => Hash(Encoding.UTF8.GetBytes(personId));
        static string PersonRoot(string treeId,string personId)
        {
            if(treeId==null || treeId.Length!=64) throw new ArgumentException("Invalid tree ID");
            foreach(char c in treeId) if(!Uri.IsHexDigit(c)) throw new ArgumentException("Invalid tree ID");
            return Path.Combine(DirectoryPath,"people",treeId,PersonKey(personId));
        }
        public static bool HasFor(string treeId,string personId)
        { try { ReadFor(treeId,personId,out _);return true; } catch {return false;} }
        public static byte[] ReadFor(string treeId,string personId,out PanoramaRecord record)
        {
            var bytes=Read(out record,PersonRoot(treeId,personId),"scene.json");
            if(record.treeId!=treeId || record.personKey!=PersonKey(personId))throw new InvalidDataException("Scene belongs to another person");
            return bytes;
        }
        public static PanoramaRecord SaveFor(string treeId,string personId,byte[] png,string prompt,string model,float horizonLift=12,string createdUtc=null)
        { return Save(png,prompt,model,PersonRoot(treeId,personId),"scene.json",treeId,PersonKey(personId),horizonLift,createdUtc); }
        public static byte[] ReadYear(string tree,string person,int year,out PanoramaRecord record)
        {
            var bytes=Read(out record,Path.Combine(PersonRoot(tree,person),"years",year.ToString()),"scene.json");
            if(record.treeId!=tree || record.personKey!=PersonKey(person) || record.year!=year)throw new InvalidDataException("Year scene identity mismatch");
            return bytes;
        }
        public static PanoramaRecord SaveYear(string tree,string person,int year,byte[] png,string prompt,string model,float lift=12,string created=null,string place=null)
        {
            if(year<1 || year>9999)throw new ArgumentException("Invalid year");
            return Save(png,prompt,model,Path.Combine(PersonRoot(tree,person),"years",year.ToString()),"scene.json",tree,PersonKey(person),lift,created,year,place);
        }
        public static int[] Years(string tree,string person)
            =>System.Array.ConvertAll(YearRecords(tree,person),record=>record.year);
        public static PanoramaRecord[] YearRecords(string tree,string person)
            =>YearRecordsAt(DirectoryPath,tree,person);
        public static PanoramaRecord[] YearRecordsAt(string directory,string tree,string person)
        {
            var records=new System.Collections.Generic.List<PanoramaRecord>();
            var root=Path.Combine(directory,"people",tree,PersonKey(person),"years");
            if(Directory.Exists(root))foreach(var dir in Directory.GetDirectories(root))
                if(int.TryParse(Path.GetFileName(dir),out int year))try {var record=IndexedRecord(dir,tree,person,year);if(record.treeId==tree&&record.personKey==PersonKey(person)&&record.year==year)records.Add(record);}catch{}
            records.Sort((a,b)=>a.year.CompareTo(b.year));return records.ToArray();
        }
        public static void MigrateYears(string tree,Person person)
        {
            if(!LifeEndpointScenes.ValidYear(person.BirthDate,out int birth))return;
            if(File.Exists(Path.Combine(PersonRoot(tree,person.Id),"years",birth.ToString(),"scene.json")))return;
            try {var bytes=ReadFor(tree,person.Id,out var record);SaveYear(tree,person.Id,birth,bytes,record.prompt,record.model,record.horizonLift,record.createdUtc,person.BirthPlace);}catch{}
        }
        public static void MigrateClara(string treeId)
        {
            if(treeId!=DemoTreeId || !Exists || File.Exists(Path.Combine(PersonRoot(treeId,"@I1@"),"scene.json"))) return;
            try { var bytes=Read(out var record);SaveFor(treeId,"@I1@",bytes,record.prompt,record.model,record.horizonLift,record.createdUtc); }
            catch(Exception) { Debug.LogWarning("Original Clara cache could not be migrated; it has been preserved."); }
        }
        // Legacy import runs before indexing on a worker; no Texture2D creation or Unity paths here.
        public static void MigrateAt(string directory,string tree,Person[] people,bool demo)
        {
            void Copy(byte[] png,PanoramaRecord record,string person,int year,string place)
            {var job=new SceneJob{sha256=record.sha256,place=place,details=record.details,prompt=record.prompt,model=record.model,createdUtc=record.createdUtc,horizonLift=record.horizonLift,settingBrief=record.settingBrief,reference=SceneReference.Copy(record.reference),basePanorama=SceneReference.Copy(record.basePanorama),basePanoramaResolved=true,generationMode=record.generationMode,policyRetryLevel=record.policyRetryLevel};if(year>0)SaveDecodedIfMissingAt(directory,tree,person,year,png,job,record.width,record.height);else SaveDecodedAt(directory,tree,person,year,png,job,record.width,record.height);}
            string PersonDirectory(string id)=>Path.Combine(directory,"people",tree,PersonKey(id));
            if(demo&&!File.Exists(Path.Combine(PersonDirectory("@I1@"),"scene.json")))
                try{var png=Read(out var record,directory,"clara.json");Copy(png,record,"@I1@",0,record.place);}catch{}
            foreach(var person in people)
            {
                if(!LifeEndpointScenes.ValidYear(person.BirthDate,out int birth)||HasImageAt(directory,tree,person.Id,birth))continue;
                try{var png=Read(out var record,PersonDirectory(person.Id),"scene.json");if(record.treeId==tree&&record.personKey==PersonKey(person.Id))Copy(png,record,person.Id,birth,person.BirthPlace);}catch{}
            }
        }
        public static bool Exists => File.Exists(Path.Combine(DirectoryPath,"clara.json"));
        public static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        public static void ValidatePng(byte[] bytes)
        {
            if(bytes == null || bytes.Length < 33 || bytes.Length > 32*1024*1024)
                throw new InvalidDataException("Image is empty or exceeds the 32 MB limit.");
            byte[] signature = {137,80,78,71,13,10,26,10};
            for(int i=0;i<8;i++) if(bytes[i]!=signature[i]) throw new InvalidDataException("Expected a PNG image.");
            uint w = Read32(bytes,16), h = Read32(bytes,20);
            if(w<256 || h<128 || w>4096 || h>2048 || Math.Abs((double)w/h-2)> .02)
                throw new InvalidDataException($"Received {w}×{h}; panorama needs a 2:1 image, at most 4096×2048. Try generating again.");
        }
        static uint Read32(byte[] bytes,int offset) => ((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3];
        public static Texture2D Decode(byte[] bytes)
        {
            ValidatePng(bytes);
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,true,false);
            if(!ImageConversion.LoadImage(texture,bytes)) { Release(texture); throw new InvalidDataException("PNG could not be decoded."); }
            texture.name="Meta panorama"; texture.wrapModeU=TextureWrapMode.Repeat; texture.wrapModeV=TextureWrapMode.Clamp;
            texture.filterMode=FilterMode.Trilinear; return texture;
        }
        public static PanoramaRecord Save(byte[] png,string prompt,string model,string root = null,string manifestName="clara.json",string treeId=null,string personKey=null,float horizonLift=12,string createdUtc=null,int year=0,string place=null)
        {
            // Validate before touching the last good cache; image and manifest never contain credentials.
            var texture=Decode(png); int width=texture.width,height=texture.height; Release(texture);
            root ??= DirectoryPath;
            lock(CommitLock(root))
            {
            Directory.CreateDirectory(root);
            var hash=Hash(png);
            var record=new PanoramaRecord {place=place,year=year,model=model,prompt=prompt,createdUtc=createdUtc??DateTime.UtcNow.ToString("O"),sha256=hash,file=hash+".png",width=width,height=height,treeId=treeId,personKey=personKey,horizonLift=horizonLift};
            string imagePath=Path.Combine(root,record.file);
            // Repair a damaged file even when its content-addressed name already exists.
            if(!File.Exists(imagePath) || Hash(File.ReadAllBytes(imagePath))!=hash) File.WriteAllBytes(imagePath,png);
            string manifest=Path.Combine(root,manifestName), temporary=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,JsonUtility.ToJson(record,true));
            if(File.Exists(manifest)) File.Replace(temporary,manifest,null); else File.Move(temporary,manifest);
            RememberImage(root,manifestName,record);return record;
            }
        }
        public static byte[] Read(out PanoramaRecord record,string root = null,string manifestName="clara.json")
        {
            root ??= DirectoryPath;
            record=ReadManifest(root,manifestName);
            string file=Path.Combine(root,record.file);
            var bytes=File.ReadAllBytes(file);imageReads++;ValidatePng(bytes);imageValidations++;
            if(Hash(bytes)!=record.sha256) throw new InvalidDataException("Cached image checksum mismatch.");
            RememberImage(root,manifestName,record);
            return bytes;
        }
        public static PanoramaRecord ReadManifest(string root,string manifestName)
        {
            string json=File.ReadAllText(Path.Combine(root,manifestName));
            if(json.Length>128000) throw new InvalidDataException("Cache manifest is too large.");
            var record=JsonUtility.FromJson<PanoramaRecord>(json);
            if(record==null || record.version!=1 || record.sha256==null || record.sha256.Length!=64 || record.file!=record.sha256+".png")
                throw new InvalidDataException("Invalid panorama cache manifest.");
            foreach(char c in record.sha256) if(!Uri.IsHexDigit(c)) throw new InvalidDataException("Invalid cache filename.");
            string file=Path.Combine(root,record.file);
            if(new FileInfo(file).Length>32*1024*1024) throw new InvalidDataException("Cached image is too large.");
            return record;
        }
        // Caller has decoded the image through DownloadHandlerTexture; all remaining work is pure IO/CPU.
        public static PanoramaRecord SaveDecodedAt(string directory,string tree,string person,int year,byte[] png,SceneJob job,int width,int height)
        {
            string root=Path.Combine(directory,"people",tree,PersonKey(person));if(year>0)root=Path.Combine(root,"years",year.ToString());
            lock(CommitLock(root))return SaveDecodedCore(directory,tree,person,year,png,job,width,height);
        }
        // Install a shipped panorama with bounded worker memory; never replace a valid user image.
        public static bool InstallBundledAt(string directory,string tree,string person,PanoramaRecord record,Func<Stream> open)
        {
            TreeStore.CheckId(tree);TreeStore.CheckId(record.sha256);
            if(record.version!=1||record.treeId!=tree||record.personKey!=PersonKey(person)||record.year<1||record.file!=record.sha256+".png")throw new InvalidDataException("Invalid bundled scene identity.");
            string root=Path.Combine(directory,"people",tree,record.personKey,"years",record.year.ToString());
            lock(CommitLock(root))
            {
                if(HasImageInRoot(root,tree,person,record.year))return false;
                Directory.CreateDirectory(root);string temp=Path.Combine(root,Guid.NewGuid().ToString("N")+".tmp");
                try
                {
                    using(var input=open())using(var sha=SHA256.Create())using(var output=File.Create(temp))
                    {
                        var header=new byte[33];int offset=0;while(offset<header.Length){int n=input.Read(header,offset,header.Length-offset);if(n==0)throw new InvalidDataException("Incomplete bundled PNG.");offset+=n;}
                        ValidatePng(header);if(Read32(header,16)!=record.width||Read32(header,20)!=record.height)throw new InvalidDataException("Bundled dimensions mismatch.");
                        using(var hashed=new CryptoStream(output,sha,CryptoStreamMode.Write,true))
                        {
                            hashed.Write(header,0,header.Length);long total=header.Length;var buffer=new byte[65536];int n;
                            while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>32L*1024*1024)throw new InvalidDataException("Bundled PNG exceeds size limit.");hashed.Write(buffer,0,n);}hashed.FlushFinalBlock();
                            if(BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()!=record.sha256)throw new InvalidDataException("Bundled PNG checksum mismatch.");
                        }
                    }
                    string image=Path.Combine(root,record.file);if(File.Exists(image))File.Replace(temp,image,null);else File.Move(temp,image);
                    string manifest=Path.Combine(root,"scene.json");TreeStore.AtomicText(manifest,JsonUtility.ToJson(record));RememberImage(root,"scene.json",record);return true;
                }
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
        }
        public static bool SaveDecodedIfMissingAt(string directory,string tree,string person,int year,byte[] png,SceneJob job,int width,int height)
        {
            string root=Path.Combine(directory,"people",tree,PersonKey(person),"years",year.ToString());
            lock(CommitLock(root))
            {
                if(HasImageInRoot(root,tree,person,year))return false;
                SaveDecodedCore(directory,tree,person,year,png,job,width,height);return true;
            }
        }
        public static string ImageShaAt(string directory,string tree,string person,int year)
        {
            string root=Path.Combine(directory,"people",tree,PersonKey(person),"years",year.ToString());
            lock(CommitLock(root))return HasImageInRoot(root,tree,person,year)?ReadManifest(root,"scene.json").sha256:"";
        }
        // An explicit regeneration may replace its original image, but never a newer completion.
        public static bool SaveDecodedIfUnchangedAt(string directory,string tree,string person,int year,string expectedSha,byte[] png,SceneJob job,int width,int height)
        {
            string root=Path.Combine(directory,"people",tree,PersonKey(person),"years",year.ToString());
            lock(CommitLock(root))
            {
                if(HasImageInRoot(root,tree,person,year)&&ReadManifest(root,"scene.json").sha256!=(expectedSha??""))return false;
                SaveDecodedCore(directory,tree,person,year,png,job,width,height);return true;
            }
        }
        static PanoramaRecord SaveDecodedCore(string directory,string tree,string person,int year,byte[] png,SceneJob job,int width,int height)
        {
            ValidatePng(png);var hash=Hash(png);if(hash!=job.sha256)throw new InvalidDataException("Image checksum failed");
            if(width!=(int)Read32(png,16)||height!=(int)Read32(png,20))throw new InvalidDataException("Decoded dimensions differ");
            var root=Path.Combine(directory,"people",tree,PersonKey(person));if(year>0)root=Path.Combine(root,"years",year.ToString());Directory.CreateDirectory(root);
            var record=new PanoramaRecord{treeId=tree,personKey=PersonKey(person),year=year,policyRetryLevel=job.policyRetryLevel,place=job.place,details=job.details,prompt=job.prompt,model=job.model,createdUtc=job.createdUtc,sha256=hash,file=hash+".png",width=width,height=height,horizonLift=job.horizonLift,settingBrief=SceneSettingBrief.Copy(job.settingBrief),reference=SceneReference.Copy(job.reference),basePanorama=SceneReference.Copy(job.basePanorama),generationMode=job.generationMode};
            var image=Path.Combine(root,record.file);if(!File.Exists(image)||Hash(File.ReadAllBytes(image))!=hash)File.WriteAllBytes(image,png);
            var manifest=Path.Combine(root,"scene.json");var temp=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(record,true));if(File.Exists(manifest))File.Replace(temp,manifest,null);else File.Move(temp,manifest);RememberImage(root,"scene.json",record);ProductDemoAuthoring.Changed(Path.GetDirectoryName(directory),tree);return record;
        }
        public static Texture2D TryLoad()
        {
            if(!Exists) return null;
            try { return Decode(Read(out _)); }
            catch(Exception e) { Debug.LogWarning("Clara panorama cache unavailable; using bundled preview. " + e.Message); return null; }
        }
        public static void Release(UnityEngine.Object item)
        { if(Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item); }
    }
}
