using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
namespace AncestorWalk
{
    // Shared across tree rebuilds. Only this component owns preview textures.
    public sealed class CubeThumbnailCache : MonoBehaviour
    {
        public const int Width=512,Height=256,MaximumUnusedTextures=32,Anisotropy=4;
        public sealed class Entry
        {
            public string Key,Directory,Tree,Person;public int Year,References;public Texture Texture;
            public float Lift;public bool Ready,Failed,Readback,Warm;public long Used;
        }
        sealed class Source {public PanoramaRecord Record;public string Image,Thumbnail;public byte[] Raw;}
        readonly Dictionary<string,Entry> entries=new();readonly List<Entry> queue=new();
        string root;long clock;bool working;UnityWebRequest activeRequest;
        public int DiskReads {get;private set;}public int Decodes {get;private set;}public int Hits {get;private set;}
        public int TextureCount {get{int n=0;foreach(var e in entries.Values)if(e.Texture!=null)n++;return n;}}
        public int UnusedTextureCount {get{int n=0;foreach(var e in entries.Values)if(e.Texture!=null&&e.References==0)n++;return n;}}
        public int PinnedTextureCount=>TextureCount-UnusedTextureCount;
        public int PendingCount=>queue.Count+(working?1:0);
        void Awake(){root=PanoramaCache.StorageRoot;}
        public Entry Acquire(string tree,string person,int year,string revision)
        {
            string key=tree+"/"+person+"/"+year+"/"+revision;
            if(entries.TryGetValue(key,out var e)){Hits++;e.References++;e.Used=++clock;return e;}
            e=new Entry{Key=key,Tree=tree,Person=PanoramaCache.PersonKey(person),Year=year,References=1,Used=++clock};
            e.Directory=Path.Combine(root,"Panoramas","people",tree,e.Person,"years",year.ToString());
            entries.Add(key,e);queue.Add(e);return e;
        }
        public void Release(Entry e)
        {
            if(e==null)return;e.References=Math.Max(0,e.References-1);e.Used=++clock;
            if(e.References==0&&!e.Warm&&((!e.Ready&&queue.Remove(e))||e.Failed))entries.Remove(e.Key);
            Trim();
        }
        public void Prewarm(string tree,string person,int year,string revision)
        {
            if(entries.Count>=MaximumUnusedTextures)return;var e=Acquire(tree,person,year,revision);e.Warm=true;Release(e);
        }
        void Update()
        {
            if(working||queue.Count==0)return;
            int at=0;for(int i=0;i<queue.Count;i++)if(queue[i].References>0){at=i;break;}
            var e=queue[at];queue.RemoveAt(at);working=true;StartCoroutine(SafeLoad(e));
        }
        IEnumerator SafeLoad(Entry e)
        {
            var routine=Load(e);
            try
            {
                while(true)
                {
                    bool more;try{more=routine.MoveNext();}catch(Exception error){Fail(e);Debug.LogWarning("Cube thumbnail unavailable: "+error.GetType().Name);yield break;}
                    if(!more)break;yield return routine.Current;
                }
            }
            finally{(routine as IDisposable)?.Dispose();working=false;}
        }
        IEnumerator Load(Entry e)
        {
            Source source=null;Texture2D full=null;RenderTexture small=null;
            var task=Task.Run(()=>ReadSource(e));DiskReads++;
            while(!task.IsCompleted)yield return null;
            if(task.IsFaulted||task.IsCanceled){_ = task.Exception;Fail(e);yield break;}
            source=task.Result;e.Lift=source.Record.horizonLift;
            if(source.Raw!=null)
            {
                while(!PanoramaFrameBudget.TryTake())yield return null;
                var texture=new Texture2D(Width,Height,TextureFormat.RGBA32,true,false){name="Shared cube thumbnail "+e.Year};
                texture.SetPixelData(source.Raw,0);texture.Apply(true,true);Configure(texture);e.Texture=texture;
            }
            else
            {
                using(var request=UnityWebRequestTexture.GetTexture(new Uri(source.Image).AbsoluteUri,true))
                {
                    activeRequest=request;request.timeout=30;request.redirectLimit=0;
                    yield return request.SendWebRequest();activeRequest=null;
                    if(request.result!=UnityWebRequest.Result.Success){Fail(e);yield break;}
                    while(!PanoramaFrameBudget.TryTake())yield return null;
                    full=DownloadHandlerTexture.GetContent(request);Decodes++;
                }
                yield return null;
                while(!PanoramaFrameBudget.TryTake())yield return null;
                // Keep the downsample on the GPU. No synchronous ReadPixels or CPU full-image decode.
                small=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                    {name="Shared cube thumbnail "+e.Year,useMipMap=true,autoGenerateMips=false};
                small.Create();bool old=GL.sRGBWrite;
                try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(full,small);small.GenerateMips();}
                finally{GL.sRGBWrite=old;Destroy(full);}
                Configure(small);e.Texture=small;
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    e.Readback=true;
                    AsyncGPUReadback.Request(small,0,TextureFormat.RGBA32,request=>
                    {
                        e.Readback=false;if(this==null)return;
                        if(!request.hasError){var bytes=request.GetData<byte>().ToArray();_ = Task.Run(()=>WriteThumbnail(source.Thumbnail,bytes));}
                        Trim();
                    });
                }
            }
            e.Ready=true;working=false;Trim();
        }
        static void Configure(Texture t){t.wrapModeU=TextureWrapMode.Repeat;t.wrapModeV=TextureWrapMode.Clamp;t.filterMode=FilterMode.Trilinear;t.anisoLevel=Anisotropy;}
        Source ReadSource(Entry e)
        {
            // Explicit directory avoids Unity/Android API calls on the worker thread.
            var png=PanoramaCache.Read(out var record,e.Directory,"scene.json");
            if(record.treeId!=e.Tree||record.personKey!=e.Person||record.year!=e.Year)throw new InvalidDataException("Thumbnail identity mismatch");
            var result=new Source{Record=record,Image=Path.Combine(e.Directory,record.file),Thumbnail=Path.Combine(root,"CubeThumbnails","v1",record.sha256+".rgba")};
            try
            {
                var file=new FileInfo(result.Thumbnail);
                if(file.Exists&&file.Length==Width*Height*4+64)
                {
                    var bytes=File.ReadAllBytes(result.Thumbnail);var raw=new byte[Width*Height*4];Buffer.BlockCopy(bytes,64,raw,0,raw.Length);
                    if(System.Text.Encoding.ASCII.GetString(bytes,0,64)==PanoramaCache.Hash(raw))result.Raw=raw;
                }
            }
            catch(IOException){ /* Rebuild a disposable thumbnail from the validated source. */ }
            return result;
        }
        static void WriteThumbnail(string path,byte[] raw)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));var hash=System.Text.Encoding.ASCII.GetBytes(PanoramaCache.Hash(raw));
                using(var stream=File.Create(temp)){stream.Write(hash,0,hash.Length);stream.Write(raw,0,raw.Length);}
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
            catch(IOException){if(File.Exists(temp))File.Delete(temp);}
            catch(UnauthorizedAccessException){if(File.Exists(temp))File.Delete(temp);}
        }
        void Fail(Entry e){e.Ready=e.Failed=true;working=false;}
        void Trim()
        {
            while(UnusedTextureCount>MaximumUnusedTextures)
            {
                Entry oldest=null;foreach(var e in entries.Values)if(e.Texture!=null&&e.References==0&&!e.Readback&&(oldest==null||e.Used<oldest.Used))oldest=e;
                if(oldest==null)break;Destroy(oldest.Texture);entries.Remove(oldest.Key);
            }
            // Discard failed/unloaded metadata once it is unreferenced; retain only bounded queued warmups.
        }
        void OnDestroy()
        {
            try{activeRequest?.Abort();}catch(ObjectDisposedException){}
            foreach(var e in entries.Values)if(e.Texture!=null)Destroy(e.Texture);
            entries.Clear();queue.Clear();
        }
    }
}
