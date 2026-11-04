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
    public sealed class PhotoTextureCache : MonoBehaviour
    {
        public sealed class Entry {public string Key;public PhotoRecord Photo;public int Size,References;public bool Ready,Failed;public Texture Texture;public long Used;}
        readonly Dictionary<string,Entry> entries=new();readonly List<Entry> queue=new();TreeStore store;bool working;long clock;UnityWebRequest request;
        public int Pending=>queue.Count+(working?1:0);public int Reads{get;private set;}public int Commits{get;private set;}
        int mediaPreparations;
        public bool PreparingBundledMedia=>mediaPreparations>0;
        public void BeginBundledMedia(){mediaPreparations++;}
        public void EndBundledMedia(){mediaPreparations=Math.Max(0,mediaPreparations-1);}
        public int UnusedCount{get{int n=0;foreach(var e in entries.Values)if(e.Ready&&e.References==0)n++;return n;}}
        void Awake(){store=new TreeStore(PanoramaCache.StorageRoot);}
        public Entry Acquire(PhotoRecord photo,int size=256)
        {
            size=size<=256?256:1024;string key=photo.Id+"-"+size;
            if(!entries.TryGetValue(key,out var e)){e=new Entry{Key=key,Photo=photo,Size=size};entries.Add(key,e);queue.Add(e);}e.References++;e.Used=++clock;return e;
        }
        void Remove(Entry e){if(entries.TryGetValue(e.Key,out var value)&&ReferenceEquals(value,e))entries.Remove(e.Key);}
        public void Release(Entry e){if(e==null)return;e.References=Math.Max(0,e.References-1);e.Used=++clock;if(e.References==0&&(queue.Remove(e)||e.Failed))Remove(e);Trim();}
        void Update(){if(PreparingBundledMedia||working||queue.Count==0)return;var e=queue[0];queue.RemoveAt(0);working=true;StartCoroutine(SafeLoad(e));}
        IEnumerator SafeLoad(Entry e)
        {
            var routine=Load(e);try{while(true){bool more;try{more=routine.MoveNext();}catch(Exception){e.Failed=true;yield break;}if(!more)break;yield return routine.Current;}}
            finally{(routine as IDisposable)?.Dispose();e.Ready=true;e.Failed=e.Texture==null;working=false;if(e.Failed&&e.References==0)Remove(e);Trim();}
        }
        IEnumerator Load(Entry e)
        {
            string path=Path.Combine(store.ThumbnailRoot,e.Key+".rgba");
            var rawTask=Task.Run(()=>{try{using var r=new BinaryReader(File.OpenRead(path));if(r.ReadString()!="AWP1"||r.ReadInt32()!=e.Size)return null;string hash=r.ReadString();var raw=r.ReadBytes(e.Size*e.Size*4);return raw.Length==e.Size*e.Size*4&&r.BaseStream.Position==r.BaseStream.Length&&PanoramaCache.Hash(raw)==hash?raw:null;}catch{return null;}});
            while(!rawTask.IsCompleted)yield return null;if(e.References==0)yield break;
            if(rawTask.Result!=null)
            {
                while(!PanoramaFrameBudget.TryTake())yield return null;var t=new Texture2D(e.Size,e.Size,TextureFormat.RGBA32,true,false);t.SetPixelData(rawTask.Result,0);t.Apply(true,true);Configure(t);e.Texture=t;Commits++;yield break;
            }
            // Integrity/header validation stays on a worker; Unity owns native JPEG/PNG decoding.
            var validate=Task.Run(()=>{var bytes=File.ReadAllBytes(Path.Combine(store.MediaRoot,e.Photo.File));PhotoHeader.Read(bytes,out _,out _,out _);if(PanoramaCache.Hash(bytes)!=e.Photo.Id)throw new InvalidDataException("Photo checksum mismatch.");});
            while(!validate.IsCompleted)yield return null;if(validate.IsFaulted){_=validate.Exception;yield break;}if(e.References==0)yield break;
            Texture2D full=null;RenderTexture small=null;Material material=null;
            try
            {
                using(var web=UnityWebRequestTexture.GetTexture(new Uri(Path.Combine(store.MediaRoot,e.Photo.File)).AbsoluteUri,true))
                {
                    request=web;web.timeout=30;var operation=web.SendWebRequest();Reads++;
                    while(!operation.isDone){if(e.References==0){web.Abort();yield break;}yield return null;}request=null;
                    if(web.result!=UnityWebRequest.Result.Success)yield break;while(!PanoramaFrameBudget.TryTake())yield return null;
                    full=DownloadHandlerTexture.GetContent(web);
                }
                yield return null;if(e.References==0)yield break;while(!PanoramaFrameBudget.TryTake())yield return null;
                small=new RenderTexture(e.Size,e.Size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){useMipMap=true,autoGenerateMips=false,name="Family photo "+e.Photo.Title};small.Create();
                material=new Material(Resources.Load<Shader>("Shaders/PhotoThumbnail"));material.SetFloat("_Orientation",e.Photo.Orientation);
                material.SetFloat("_TransparentPadding",string.IsNullOrEmpty(e.Photo.CutoutModel)?0:1);
                float aspect=(float)e.Photo.Width/e.Photo.Height;if(e.Photo.Orientation>=5)aspect=1/aspect;material.SetFloat("_Aspect",aspect);
                bool old=GL.sRGBWrite;try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(full,small,material);small.GenerateMips();}finally{GL.sRGBWrite=old;}
                Configure(small);e.Texture=small;small=null;Commits++;
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    var readback=AsyncGPUReadback.Request(e.Texture,0,TextureFormat.RGBA32);while(!readback.done)yield return null;
                    if(!readback.hasError){var raw=readback.GetData<byte>().ToArray();_ = Task.Run(()=>{try{Directory.CreateDirectory(store.ThumbnailRoot);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var writer=new BinaryWriter(File.Create(temp))){writer.Write("AWP1");writer.Write(e.Size);writer.Write(PanoramaCache.Hash(raw));writer.Write(raw);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}catch{}});}
                }
            }
            finally{request=null;if(full!=null)Destroy(full);if(small!=null)Destroy(small);if(material!=null)Destroy(material);}
        }
        static void Configure(Texture t){t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Trilinear;}
        void Trim(){while(UnusedCount>32){Entry oldest=null;foreach(var e in entries.Values)if(e.Ready&&e.References==0&&(oldest==null||e.Used<oldest.Used))oldest=e;if(oldest==null)break;if(oldest.Texture!=null)Destroy(oldest.Texture);Remove(oldest);}}
        void OnDestroy(){try{request?.Abort();}catch(ObjectDisposedException){}foreach(var e in entries.Values)if(e.Texture!=null)Destroy(e.Texture);entries.Clear();}
    }
    public sealed class PhotoPortrait : MonoBehaviour
    {
        PhotoTextureCache cache;PhotoTextureCache.Entry entry;PhotoRecord photo;Material material;bool applied;CubeFacingControls controls;
        public Renderer Surface {get;private set;}
        public void Build(AncestorGallery gallery,PhotoRecord record,Renderer surface)
        {cache=gallery.Photos;photo=record;Surface=surface;material=new Material(Resources.Load<Shader>("Shaders/PersonPhoto"));material.mainTexture=Texture2D.grayTexture;surface.sharedMaterial=material;}
        void Update()
        {
            if(controls==null)controls=GetComponent<CubeFacingControls>();bool visible=controls==null||controls.InteractionsVisible||controls.InteractionOpacity>0;
            if(!visible){if(entry!=null){cache.Release(entry);entry=null;applied=false;material.mainTexture=Texture2D.grayTexture;}return;}
            entry??=cache.Acquire(photo);if(applied||!entry.Ready||entry.Failed)return;if(!PanoramaFrameBudget.TryTake())return;material.mainTexture=entry.Texture;applied=true;
        }
        void OnDisable(){if(entry!=null){cache?.Release(entry);entry=null;}applied=false;}
        void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
