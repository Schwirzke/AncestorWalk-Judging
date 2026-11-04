using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace AncestorWalk
{
    // Public fictional assets only. Seeding uses local files/APK entries, never HTTP or credentials.
    public static class BundledDemoScenes
    {
        public const string Folder="AncestorWalkDemo";
        [Serializable] public sealed class Manifest {public int version,sceneYear;public string treeId,documentSha256;public Entry[] scenes;public Omission[] omittedScenes;public Portrait[] portraits;}
        [Serializable] public sealed class Entry {public string personId,thumbnailSha256;public PanoramaRecord record;}
        [Serializable] public sealed class Portrait {public string personId,model,prompt,createdUtc;public PhotoRecord photo;}
        [Serializable] public sealed class Omission {public string personId,reason;public int year;}
        public sealed class Result {public bool Available;public int Installed,Preserved,PortraitsInstalled,PortraitsPreserved;public Omission[] OmittedScenes=Array.Empty<Omission>();}
        sealed class Source : IDisposable
        {
            readonly string root;readonly ZipArchive archive;
            public Source(string path)
            {
                if(path.StartsWith("jar:",StringComparison.Ordinal))
                {int end=path.IndexOf('!');if(end<0)throw new InvalidDataException("Invalid packaged asset path.");archive=new ZipArchive(File.OpenRead(new Uri(path.Substring(4,end-4)).LocalPath),ZipArchiveMode.Read);}
                else root=Path.Combine(path,Folder);
            }
            public Stream Open(string relative)
            {
                if(archive==null)return File.OpenRead(Path.Combine(root,relative));
                var entry=archive.GetEntry("assets/"+Folder+"/"+relative);if(entry==null)throw new FileNotFoundException("Bundled demo asset missing.");
                if(entry.Length>32L*1024*1024)throw new InvalidDataException("Bundled asset exceeds size limit.");return entry.Open();
            }
            public void Dispose(){archive?.Dispose();}
        }
        public static void ValidateManifest(Manifest manifest,string tree,Person[] people)
        {
            if(manifest==null||(manifest.version!=1&&manifest.version!=2&&manifest.version!=3)||manifest.treeId!=tree||manifest.scenes==null||manifest.scenes.Length>(manifest.version==3?2000:200))throw new InvalidDataException("Invalid demo bundle identity.");
            var allowed=new Dictionary<string,Person>();foreach(var p in people)allowed.Add(p.Id,p);var targets=new HashSet<string>();
            foreach(var entry in manifest.scenes)
            {
                var record=entry?.record;
                if(entry==null||record==null||!allowed.TryGetValue(entry.personId,out var person)||record.treeId!=tree||record.personKey!=PanoramaCache.PersonKey(entry.personId)||!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end)||record.year<birth||record.year>end||!targets.Add(entry.personId+"/"+record.year)||manifest.version<3&&(SceneReference.Optional(record.reference)!=null||SceneReference.Optional(record.basePanorama)!=null))
                    throw new InvalidDataException("Bundled scene is not an unreferenced fictional demo scene.");
                TreeStore.CheckId(record.sha256);TreeStore.CheckId(entry.thumbnailSha256);
                if(record.file!=record.sha256+".png")throw new InvalidDataException("Invalid bundled image name.");
            }
            foreach(var omission in manifest.omittedScenes??Array.Empty<Omission>())
                if(omission==null||!allowed.TryGetValue(omission.personId,out var person)||!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end)||omission.year<birth||omission.year>end||string.IsNullOrWhiteSpace(omission.reason)||!targets.Add(omission.personId+"/"+omission.year))throw new InvalidDataException("Invalid or duplicate omitted demo endpoint.");
            var portraits=new HashSet<string>();
            foreach(var entry in manifest.portraits??Array.Empty<Portrait>())
            {
                var photo=entry?.photo;
                if(photo==null||!allowed.ContainsKey(entry.personId)||!portraits.Add(manifest.version==3?photo.Id:entry.personId)||(photo.File!=photo.Id+".png"&&(manifest.version<3||photo.File!=photo.Id+".jpg"))||photo.Width<1||photo.Height<1||(manifest.version<3&&photo.Width!=photo.Height)||photo.Width>(manifest.version==3?16384:4096)||photo.Height>16384||photo.Orientation<1||photo.Orientation>8||(manifest.version<3&&photo.Orientation!=1))throw new InvalidDataException("Invalid bundled fictional portrait.");
                TreeStore.CheckId(photo.Id);
            }
        }
        public static Result SeedAt(string streamingRoot,string panoramas,string tree,Person[] people)
        {
            var result=new Result();using var source=new Source(streamingRoot);string json;
            try{using var file=source.Open("manifest.json");using var reader=new StreamReader(file);json=reader.ReadToEnd();if(json.Length>2*1024*1024)throw new InvalidDataException("Bundled manifest too large.");}
            catch(FileNotFoundException){return result;}catch(DirectoryNotFoundException){return result;}
            var manifest=JsonUtility.FromJson<Manifest>(json);
            ValidateManifest(manifest,tree,people);
            result.Available=true;result.OmittedScenes=manifest.omittedScenes??Array.Empty<Omission>();
            foreach(var entry in manifest.portraits??Array.Empty<Portrait>())
            {
                string media=Path.Combine(Path.GetDirectoryName(panoramas),"Photos",entry.photo.File);
                bool valid=false;
                if(File.Exists(media)){using var file=File.OpenRead(media);using var sha=SHA256.Create();valid=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()==entry.photo.Id;}
                if(valid){result.PortraitsPreserved++;continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(media));string temp=media+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using(var input=source.Open("portraits/"+entry.photo.File))using(var output=File.Create(temp))using(var sha=SHA256.Create())using(var hashed=new CryptoStream(output,sha,CryptoStreamMode.Write))
                    {var buffer=new byte[65536];long total=0;int count;while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>32L*1024*1024)throw new InvalidDataException("Portrait exceeds size limit.");hashed.Write(buffer,0,count);}hashed.FlushFinalBlock();if(BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()!=entry.photo.Id)throw new InvalidDataException("Portrait checksum mismatch.");}
                    if(File.Exists(media))File.Replace(temp,media,null);else File.Move(temp,media);result.PortraitsInstalled++;
                }
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            foreach(var entry in manifest.scenes)
            {
                var record=entry.record;
                if(PanoramaCache.InstallBundledAt(panoramas,tree,entry.personId,record,()=>source.Open("images/"+record.file)))result.Installed++;else result.Preserved++;
                string thumbnail=Path.Combine(Path.GetDirectoryName(panoramas),"CubeThumbnails","v1",record.sha256+".rgba");
                if(File.Exists(thumbnail))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(thumbnail));string temp=thumbnail+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using(var input=source.Open("previews/"+record.sha256+".rgba"))using(var output=File.Create(temp))using(var sha=SHA256.Create())
                    using(var hashed=new CryptoStream(output,sha,CryptoStreamMode.Write))
                    {
                        var buffer=new byte[65536];long total=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>524352)throw new InvalidDataException("Bundled thumbnail too large.");hashed.Write(buffer,0,n);}hashed.FlushFinalBlock();
                        if(total!=524352||BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()!=entry.thumbnailSha256)throw new InvalidDataException("Bundled thumbnail checksum mismatch.");
                    }
                    if(!File.Exists(thumbnail))File.Move(temp,thumbnail);
                }
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            return result;
        }
    }
}
