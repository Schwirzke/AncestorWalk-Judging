using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace AncestorWalk
{
    public sealed class TreeImportDraft
    {public TreeDocument Document;public string Directory;public List<string> GedcomChoices=new();public string SourcePath;}
    public static class TreeArchiveImport
    {
        sealed class ArchiveLimitException:Exception{public ArchiveLimitException():base("Expanded archive exceeds 512 MB."){} }
        public const long MaximumArchive=128L*1024*1024,MaximumExpanded=512L*1024*1024;
        public static string SafeRelative(string path)
        {
            path=(path??"").Replace('\\','/');
            if(path.Length==0||path.Length>500||path.StartsWith("/")||path.Contains(':')||path.IndexOf('\0')>=0||path.Split('/').Any(p=>p==".."))throw new InvalidDataException("Unsafe media path.");
            return string.Join("/",path.Split('/').Where(p=>p!=""&&p!="."));
        }
        static byte[] ReadBounded(Stream stream,long maximum,CancellationToken token)
        {using var output=new MemoryStream();var buffer=new byte[65536];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(output.Length+n>maximum)throw new InvalidDataException("File exceeds the import size limit.");output.Write(buffer,0,n);}return output.ToArray();}
        static string Decode(byte[] bytes)
        {
            string text;
            if(bytes.Length>=2&&bytes[0]==255&&bytes[1]==254)text=new UnicodeEncoding(false,true,true).GetString(bytes,2,bytes.Length-2);
            else if(bytes.Length>=2&&bytes[0]==254&&bytes[1]==255)text=new UnicodeEncoding(true,true,true).GetString(bytes,2,bytes.Length-2);
            else text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
            var charset=System.Text.RegularExpressions.Regex.Match(text,@"(?im)^1 CHAR (.+)$");
            if(charset.Success&&!new[]{"UTF-8","UNICODE","ASCII"}.Contains(charset.Groups[1].Value.Trim().ToUpperInvariant()))throw new InvalidDataException("Export this GEDCOM as UTF-8 before importing.");
            return text;
        }
        static string Resolve(string path,Dictionary<string,Func<byte[]>> files,string gedDirectory,List<string> warnings)
        {
            if(Uri.TryCreate(path,UriKind.Absolute,out _)){warnings.Add("External media was not downloaded: "+path);return null;}
            string clean;try{clean=SafeRelative(path);}catch{warnings.Add("Unsafe/missing media reference: "+path);return null;}
            string relative=string.IsNullOrEmpty(gedDirectory)?clean:gedDirectory+"/"+clean;
            var candidates=new List<string>{relative,clean};
            try{string decoded=SafeRelative(Uri.UnescapeDataString(clean));candidates.Add(string.IsNullOrEmpty(gedDirectory)?decoded:gedDirectory+"/"+decoded);candidates.Add(decoded);}catch{}
            foreach(var key in candidates)if(files.ContainsKey(key))return key;
            var matches=files.Keys.Where(k=>candidates.Any(c=>string.Equals(k.Normalize(NormalizationForm.FormC),c.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase))).ToArray();
            if(matches.Length==1){warnings.Add("Media path normalized: "+path);return matches[0];}
            warnings.Add("Missing or ambiguous media: "+path);return null;
        }
        public static TreeImportDraft Stage(TreeStore store,string input,string gedcomChoice=null,CancellationToken token=default,Action<string> progress=null)
        {
            var draft=new TreeImportDraft{SourcePath=input};Directory.CreateDirectory(store.Root);
            string staging=Path.Combine(store.Root,".import-"+Guid.NewGuid().ToString("N"));draft.Directory=staging;Directory.CreateDirectory(staging);
            try
            {
                if(new FileInfo(input).Length>MaximumArchive)throw new InvalidDataException("Import file exceeds 128 MB.");
                string ext=Path.GetExtension(input).ToLowerInvariant();var files=new Dictionary<string,Func<byte[]>>(StringComparer.Ordinal);long expanded=0;
                byte[] ReadEntry(Stream stream){var bytes=ReadBounded(stream,32*1024*1024,token);expanded=checked(expanded+bytes.Length);if(expanded>MaximumExpanded)throw new ArchiveLimitException();return bytes;}
                using var archiveStream=File.OpenRead(input);using var zip=ext==".zip"||ext==".gdz"?new ZipArchive(archiveStream,ZipArchiveMode.Read,true):null;
                string looseRoot=Path.GetDirectoryName(Path.GetFullPath(input));
                if(zip!=null)
                {
                    if(zip.Entries.Count>5000)throw new InvalidDataException("Archive contains more than 5,000 entries.");long total=0;
                    foreach(var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();if(entry.FullName.EndsWith("/"))continue;
                        string name=SafeRelative(entry.FullName);if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Archive symbolic links are not supported.");
                        total=checked(total+entry.Length);if(total>MaximumExpanded||entry.Length>32*1024*1024)throw new InvalidDataException("Expanded archive exceeds the import limits.");
                        if(files.ContainsKey(name))throw new InvalidDataException("Archive has duplicate paths: "+name);
                        files.Add(name,()=>{using var stream=entry.Open();return ReadEntry(stream);});
                    }
                    draft.GedcomChoices=files.Keys.Where(k=>Path.GetExtension(k).Equals(".ged",StringComparison.OrdinalIgnoreCase)).OrderBy(k=>k,StringComparer.Ordinal).ToList();
                    if(gedcomChoice==null&&files.ContainsKey("gedcom.ged"))gedcomChoice="gedcom.ged";
                    if(gedcomChoice==null&&draft.GedcomChoices.Count==1)gedcomChoice=draft.GedcomChoices[0];
                    if(gedcomChoice==null&&draft.GedcomChoices.Count>1){Cleanup(store,draft);draft.Directory=null;return draft;}
                    if(gedcomChoice==null||!draft.GedcomChoices.Contains(gedcomChoice))throw new InvalidDataException("No selected GEDCOM file in this archive.");
                }
                else
                {
                    if(ext!=".ged")throw new InvalidDataException("Choose a .ged, .zip or .gdz file.");gedcomChoice=Path.GetFileName(input);
                    files.Add(gedcomChoice,()=>File.ReadAllBytes(input));
                }
                progress?.Invoke("Reading family records…");var ged=files[gedcomChoice]();if(ged.Length>4_000_000)throw new InvalidDataException("GEDCOM exceeds 4 MB.");
                string text=Decode(ged);var tree=GedcomImporter.Parse(text);draft.Document=TreeDocument.FromTree(tree,TreeStore.NewId(),Path.GetFileNameWithoutExtension(input));draft.Document.SourceGedcom=text;
                var links=GedcomMedia.Parse(text);string gedDirectory=Path.GetDirectoryName(gedcomChoice)?.Replace('\\','/');
                if(zip==null)
                {
                    foreach(var link in links)
                    {
                        string relative;try{relative=SafeRelative(link.Path);}catch{continue;}
                        string path=Path.Combine(looseRoot,relative.Replace('/',Path.DirectorySeparatorChar));
                        bool symbolic=false;var cursor=new FileInfo(path).Directory;
                        while(cursor!=null&&cursor.FullName.StartsWith(looseRoot,StringComparison.Ordinal)){if(cursor.Exists&&(cursor.Attributes&FileAttributes.ReparsePoint)!=0){symbolic=true;break;}if(cursor.FullName==looseRoot)break;cursor=cursor.Parent;}
                        if(File.Exists(path)&&!symbolic&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0&&!files.ContainsKey(relative))files.Add(relative,()=>{using var stream=File.OpenRead(path);return ReadBounded(stream,32*1024*1024,token);});
                    }
                }
                var paths=new Dictionary<string,PhotoRecord>();var byHash=new Dictionary<string,PhotoRecord>();int index=0;
                foreach(var pair in files.Where(f=>new[]{".jpg",".jpeg",".png"}.Contains(Path.GetExtension(f.Key).ToLowerInvariant())))
                {
                    token.ThrowIfCancellationRequested();progress?.Invoke("Caching photo "+(++index)+"…");
                    try
                    {
                        var bytes=pair.Value();PhotoHeader.Read(bytes,out int width,out int height,out int orientation);string hash=PanoramaCache.Hash(bytes);
                        if(!byHash.TryGetValue(hash,out var record))
                        {
                            if(byHash.Count>=2000)throw new InvalidDataException("More than 2,000 photos.");
                            string extension=bytes[0]==137?".png":".jpg";record=new PhotoRecord{Id=hash,File=hash+extension,Title=Path.GetFileNameWithoutExtension(pair.Key),OriginalPath=pair.Key,Width=width,Height=height,Orientation=orientation};
                            byHash.Add(hash,record);draft.Document.Photos.Add(record);File.WriteAllBytes(Path.Combine(staging,record.File),bytes);
                        }
                        paths[pair.Key]=record;
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception e)when(e is InvalidDataException||e is IOException){draft.Document.Warnings.Add("Photo skipped: "+pair.Key+" — "+e.Message);}
                }
                var preferredPeople=new HashSet<string>();
                foreach(var link in links)
                {
                    string path=Resolve(link.Path,files,gedDirectory,draft.Document.Warnings);
                    if(path==null)continue;
                    if(!paths.TryGetValue(path,out var photo)){draft.Document.Warnings.Add("Unsupported or invalid photo: "+path);continue;}
                    if(!tree.People.TryGetValue(link.PersonId,out var p))continue;
                    if(!p.PhotoIds.Contains(photo.Id))p.PhotoIds.Add(photo.Id);p.PrimaryPhotoId??=photo.Id;
                    if(link.IsPrimary&&preferredPeople.Add(p.Id))p.PrimaryPhotoId=photo.Id;
                    string Clip(string value,int max)=>value.Length>max?value.Substring(0,max):value;
                    if(!string.IsNullOrWhiteSpace(link.Title))photo.Title=Clip(link.Title,200);if(!string.IsNullOrWhiteSpace(link.Date))photo.Date=Clip(link.Date,200);if(!string.IsNullOrWhiteSpace(link.Description))photo.Description=Clip(link.Description,2000);
                }
                draft.Document.SourceFingerprint=PanoramaCache.Hash(Encoding.UTF8.GetBytes(text+string.Join("",byHash.Keys.OrderBy(x=>x,StringComparer.Ordinal))));
                draft.Document.Warnings=draft.Document.Warnings.Select(w=>w.Length>1024?w.Substring(0,1024)+"…":w).ToList();draft.Document.Validate();TreeStore.Serialize(draft.Document);return draft;
            }
            catch{Cleanup(store,draft);throw;}
        }
        public static TreeDocument Commit(TreeStore store,TreeImportDraft draft)
        {
            var doc=draft.Document;doc.Validate();if(Directory.Exists(store.DocumentDirectory(doc.Id)))throw new IOException("This tree already exists.");
            Directory.CreateDirectory(store.MediaRoot);
            foreach(var photo in doc.Photos)
            {
                string from=Path.Combine(draft.Directory,photo.File),to=Path.Combine(store.MediaRoot,photo.File);
                if(!File.Exists(to)||PanoramaCache.Hash(File.ReadAllBytes(to))!=photo.Id){string temp=to+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.Copy(from,temp);if(File.Exists(to))File.Replace(temp,to,null);else File.Move(temp,to);}finally{if(File.Exists(temp))File.Delete(temp);}}
            }
            doc.UpdatedUtc=DateTime.UtcNow.ToString("O");TreeStore.AtomicText(Path.Combine(draft.Directory,"source.ged"),doc.SourceGedcom);TreeStore.AtomicText(Path.Combine(draft.Directory,"tree.json"),TreeStore.Serialize(doc));
            // Directory rename publishes the complete tree in one step; an interrupted import is never listed.
            foreach(var photo in doc.Photos)File.Delete(Path.Combine(draft.Directory,photo.File));
            Directory.Move(draft.Directory,store.DocumentDirectory(doc.Id));draft.Directory=null;return doc;
        }
        public static void Cleanup(TreeStore store,TreeImportDraft draft)
        {
            if(draft?.Directory==null)return;string path=Path.GetFullPath(draft.Directory),root=Path.GetFullPath(store.Root)+Path.DirectorySeparatorChar;
            if(path.StartsWith(root,StringComparison.Ordinal)&&Path.GetFileName(path).StartsWith(".import-",StringComparison.Ordinal)&&Directory.Exists(path))Directory.Delete(path,true);
        }
        public static PhotoRecord AddPhoto(TreeStore store,string file)
        {
            if(new FileInfo(file).Length>32*1024*1024)throw new InvalidDataException("Photo exceeds 32 MB.");var bytes=File.ReadAllBytes(file);PhotoHeader.Read(bytes,out int width,out int height,out int orientation);
            string id=PanoramaCache.Hash(bytes),name=id+(bytes[0]==137?".png":".jpg");Directory.CreateDirectory(store.MediaRoot);string target=Path.Combine(store.MediaRoot,name);
            if(!File.Exists(target)||PanoramaCache.Hash(File.ReadAllBytes(target))!=id){string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}}
            return new PhotoRecord{Id=id,File=name,Title=Path.GetFileNameWithoutExtension(file),OriginalPath=Path.GetFileName(file),Width=width,Height=height,Orientation=orientation};
        }
    }
}
