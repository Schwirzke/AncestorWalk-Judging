using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
namespace AncestorWalk
{
    // Bounded full-resolution leases; reads/decode/validation never block the frame loop.
    public sealed class ScenePanoramaCache : MonoBehaviour
    {
        public sealed class Entry
        {
            public string Key,Tree,Person,Directory;public int Year,References;public bool Ready,Failed;
            public Texture Texture;public PanoramaRecord Record;public long Used;
        }
        public const int MaximumUnused=2;
        public const long MaximumUnusedBytes=64L*1024*1024;
        PanoramaUpscaler upscaler;
        public long ResidentBytes{get{long total=0;foreach(var e in entries.Values)if(e.Texture!=null)total+=TextureBytes(e.Texture);return total;}}
        public long UnusedBytes{get{long total=0;foreach(var e in entries.Values)if(e.References==0&&e.Texture!=null)total+=TextureBytes(e.Texture);return total;}}
        public static long TextureBytes(Texture t)
        {if(t==null)return 0;bool mips=t is RenderTexture rt?rt.useMipMap:t is Texture2D image&&image.mipmapCount>1;long pixels=0;int w=t.width,h=t.height;while(true){pixels+=(long)w*h;if(!mips||w==1&&h==1)break;w=Math.Max(1,w/2);h=Math.Max(1,h/2);}return pixels*4;}
        readonly Dictionary<string,Entry> entries=new();readonly List<Entry> queue=new();
        string root;long clock;bool working;UnityWebRequest request;
        public int Reads {get;private set;}public int Commits {get;private set;}public int WorkerThread {get;private set;}
        public int LastCommitFrame {get;private set;}=-1;
        void Awake(){root=PanoramaCache.DirectoryPath;if(PanoramaUpscaler.Enabled)upscaler=gameObject.AddComponent<PanoramaUpscaler>();}
        public Entry Acquire(string tree,string person,int year,string revision)
        {
            string key=tree+"/"+person+"/"+year+"/"+revision;
            if(entries.TryGetValue(key,out var old)){old.References++;old.Used=++clock;return old;}
            var e=new Entry{Key=key,Tree=tree,Person=PanoramaCache.PersonKey(person),Year=year,References=1,Used=++clock};
            e.Directory=Path.Combine(root,"people",tree,e.Person,"years",year.ToString());entries.Add(key,e);queue.Add(e);return e;
        }
        void Remove(Entry e){if(entries.TryGetValue(e.Key,out var stored)&&ReferenceEquals(e,stored))entries.Remove(e.Key);}
        public void Release(Entry e){if(e==null)return;e.References=Math.Max(0,e.References-1);e.Used=++clock;if(e.References==0&&(queue.Remove(e)||e.Failed))Remove(e);Trim();}
        void Update(){if(working||queue.Count==0)return;var e=queue[0];queue.RemoveAt(0);working=true;StartCoroutine(Load(e));}
        IEnumerator Load(Entry e)
        {
            Texture2D texture=null;RenderTexture scaled=null;
            try
            {
                var metadata=Task.Run(()=>{WorkerThread=Thread.CurrentThread.ManagedThreadId;var r=PanoramaCache.ReadManifest(e.Directory,"scene.json");if(r.treeId!=e.Tree||r.personKey!=e.Person||r.year!=e.Year)throw new InvalidDataException("Scene identity mismatch");return r;});
                while(!metadata.IsCompleted)yield return null;
                if(metadata.IsFaulted||metadata.IsCanceled){_ = metadata.Exception;e.Failed=true;yield break;}
                e.Record=metadata.Result;if(e.References==0)yield break;
                using(var web=UnityWebRequestTexture.GetTexture(new Uri(Path.Combine(e.Directory,e.Record.file)).AbsoluteUri,true))
                {
                    request=web;web.timeout=30;web.redirectLimit=0;var operation=web.SendWebRequest();Reads++;
                    while(!operation.isDone){if(e.References==0){web.Abort();yield break;}yield return null;}
                    request=null;if(web.result!=UnityWebRequest.Result.Success){e.Failed=true;yield break;}
                    // DownloadHandlerTexture retains the encoded bytes and decodes on Unity's worker.
                    // Hash those same bytes, without rereading the PNG from disk.
                    var bytes=web.downloadHandler.data;
                    var validate=Task.Run(()=>{WorkerThread=Thread.CurrentThread.ManagedThreadId;PanoramaCache.ValidatePng(bytes);if(PanoramaCache.Hash(bytes)!=e.Record.sha256)throw new InvalidDataException("Scene checksum mismatch");});
                    while(!validate.IsCompleted)yield return null;
                    if(validate.IsFaulted||validate.IsCanceled){_ = validate.Exception;e.Failed=true;yield break;}
                    if(e.References==0)yield break;
                    bytes=null;
                    while(!PanoramaFrameBudget.TryTake())yield return null;
                    texture=DownloadHandlerTexture.GetContent(web);
                    if(texture==null||texture.width!=e.Record.width||texture.height!=e.Record.height){e.Failed=true;yield break;}
                    texture.name="Historical panorama "+e.Year;texture.wrapModeU=TextureWrapMode.Repeat;texture.wrapModeV=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Trilinear;
                }
                // Decode and upscale are separate budgeted stages. Never upload/read back full pixels on the CPU.
                yield return null;
                if(e.References==0)yield break;
                while(!PanoramaFrameBudget.TryTake())yield return null;
                scaled=PanoramaUpscaler.Enabled&&upscaler!=null?upscaler.Create(texture):null;
                if(scaled!=null)
                {
                    for(int row=0;row<scaled.height;row+=PanoramaUpscaler.RowsPerFrame)
                    {
                        if(e.References==0)yield break;
                        while(!PanoramaFrameBudget.TryTake())yield return null;
                        upscaler.RenderRows(texture,scaled,row,PanoramaUpscaler.RowsPerFrame);
                        yield return null;
                    }
                    while(!PanoramaFrameBudget.TryTake())yield return null;
                    scaled.GenerateMips();e.Texture=scaled;scaled=null;Destroy(texture);texture=null;
                }
                else {e.Texture=texture;texture=null;}
                Commits++;LastCommitFrame=Time.frameCount;
            }
            finally
            {
                request=null;if(texture!=null)Destroy(texture);if(scaled!=null)Destroy(scaled);e.Ready=true;if(e.Texture==null)e.Failed=true;if(e.Failed&&e.References==0)Remove(e);working=false;Trim();
            }
        }
        void Trim()
        {
            int unused=0;foreach(var e in entries.Values)if(e.References==0&&e.Ready)unused++;
            while(unused>MaximumUnused||UnusedBytes>MaximumUnusedBytes){Entry oldest=null;foreach(var e in entries.Values)if(e.References==0&&e.Ready&&(oldest==null||e.Used<oldest.Used))oldest=e;if(oldest==null)break;if(oldest.Texture!=null)Destroy(oldest.Texture);entries.Remove(oldest.Key);unused--;}
        }
        void OnDestroy(){try{request?.Abort();}catch(ObjectDisposedException){}foreach(var e in entries.Values)if(e.Texture!=null)Destroy(e.Texture);entries.Clear();queue.Clear();}
    }
}
