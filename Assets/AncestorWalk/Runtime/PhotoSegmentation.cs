using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AncestorWalk
{
    [Serializable] public sealed class CutoutItem {public string sha256,objectId;public int width,height;}
    [Serializable] public sealed class CutoutJob {public string requestId,photoId,mode,model,status,error;public CutoutItem[] items;}
    [Serializable] public sealed class CutoutResult {public string sourcePhotoId,mode,model;public List<PhotoRecord> photos=new();}
    [Serializable] sealed class CutoutRequest {public string requestId,photoId,mode,imageB64;public bool allowUpload=true;}
    public static class CutoutCache
    {
        public static void Mode(string mode){if(mode!="head"&&mode!="face"&&mode!="person")throw new ArgumentException("Choose head, face or person.");}
        public static string DirectoryFor(string storage,string photo,string mode){TreeStore.CheckId(photo);Mode(mode);return Path.Combine(storage,"Cutouts","v2",photo,mode);}
        public static void Validate(CutoutJob job,string request,string photo,string mode)
        {
            if(job==null||job.requestId!=request||job.photoId!=photo||job.mode!=mode||job.model!="sam-3.1"||job.status!="generating"&&job.status!="complete"&&job.status!="failed")throw new InvalidDataException("Cutout response identity mismatch.");
            if(job.status!="complete")return;
            if(job.items==null||job.items.Length>16)throw new InvalidDataException("Invalid cutout candidates.");
            foreach(var item in job.items){TreeStore.CheckId(item.sha256);if(item.width<1||item.height<1||item.width>1536||item.height>1536)throw new InvalidDataException("Invalid cutout dimensions.");}
        }
        public static CutoutResult Read(string storage,PhotoRecord source,string mode)
        {
            string file=Path.Combine(DirectoryFor(storage,source.Id,mode),"result.json");if(!File.Exists(file))return null;
            try
            {
                if(new FileInfo(file).Length>128*1024)return null;var result=JsonUtility.FromJson<CutoutResult>(File.ReadAllText(file));
                if(result.sourcePhotoId!=source.Id||result.mode!=mode||result.model!="sam-3.1"||result.photos==null||result.photos.Count>16)return null;
                var store=new TreeStore(storage);foreach(var p in result.photos)
                {TreeStore.CheckId(p.Id);if(p.File!=p.Id+".png"||p.SourcePhotoId!=source.Id||p.CutoutMode!=mode)return null;var bytes=File.ReadAllBytes(Path.Combine(store.MediaRoot,p.File));PhotoHeader.Read(bytes,out int w,out int h,out _);if(PanoramaCache.Hash(bytes)!=p.Id||w!=p.Width||h!=p.Height)return null;}
                return result;
            }
            catch{return null;}
        }
        public static PhotoRecord Save(string storage,PhotoRecord source,string mode,CutoutItem item,byte[] png)
        {
            PhotoHeader.Read(png,out int w,out int h,out _);if(png[0]!=137||w!=item.width||h!=item.height||PanoramaCache.Hash(png)!=item.sha256)throw new InvalidDataException("Cutout image checksum or dimensions mismatch.");
            var store=new TreeStore(storage);Directory.CreateDirectory(store.MediaRoot);string file=item.sha256+".png",path=Path.Combine(store.MediaRoot,file);
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,png);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
            string title=mode+" "+item.objectId+" · "+source.Title;if(title.Length>200)title=title.Substring(0,200);
            return new PhotoRecord{Id=item.sha256,File=file,Width=w,Height=h,Orientation=1,Title=title,Date=source.Date,Description="Cut out from an attached photo using Meta SAM 3.1.",OriginalPath=source.OriginalPath,SourcePhotoId=source.Id,CutoutMode=mode,CutoutModel="sam-3.1"};
        }
    }
    public sealed class PhotoSegmentation : MonoBehaviour
    {
        public bool Busy{get;private set;}
        public string Message{get;private set;}="";
        public CutoutResult Result{get;private set;}
        public event Action Changed;
        string storage;UnityWebRequest current;IEnumerator operation;
#if UNITY_EDITOR
        internal string TestStorage;internal SceneServiceConnection TestConnection;
#endif
        void Awake(){storage=PanoramaCache.StorageRoot;}
        public void Extract(PhotoRecord photo,string mode)
        {
            if(Busy)return;CutoutCache.Mode(mode);Busy=true;Result=null;Message="Checking saved cutouts…";Changed?.Invoke();StartCoroutine(Safe(photo,mode));
        }
        void Status(string message){Message=message;Changed?.Invoke();}
        IEnumerator Safe(PhotoRecord photo,string mode)
        {
            var run=MetaApi.Flatten(Run(photo,mode));operation=run;try{while(true){bool more;Exception error=null;try{more=run.MoveNext();}catch(Exception e){more=false;error=e;}if(error!=null){Status(error is InvalidOperationException||error is InvalidDataException?error.Message:"Cutout interrupted. Click extraction again to check the same request.");yield break;}if(!more)break;yield return run.Current;}}
            finally{(run as IDisposable)?.Dispose();current=null;Busy=false;Changed?.Invoke();}
        }
        IEnumerator Run(PhotoRecord photo,string mode)
        {
            string root=storage;
#if UNITY_EDITOR
            root=TestStorage??root;
#endif
            string directory=CutoutCache.DirectoryFor(root,photo.Id,mode),pending=Path.Combine(directory,"pending.json"),manifest=Path.Combine(directory,"result.json");
            var cached=Task.Run(()=>CutoutCache.Read(root,photo,mode));while(!cached.IsCompleted)yield return null;
            if(cached.IsFaulted)throw cached.Exception.GetBaseException();if(cached.Result!=null){Result=cached.Result;Status("Saved cutouts ready.");yield break;}
            bool legacyPending=File.Exists(pending);DevelopmentAI.TryConnection(out var development);
#if UNITY_EDITOR
            bool direct=TestConnection==null&&development==null&&!legacyPending;
#else
            bool direct=development==null&&!legacyPending;
#endif
            if(direct)
            {
                yield return MetaApi.Guard(DirectMetaCutout.Run(root,photo,mode,result=>{Result=result;Status(result.photos.Count==0?"No matches found.":result.photos.Count+" cutout(s) ready. Choose the right person.");},Status,request=>current=request),()=>Status("Direct SAM request interrupted. No automatic replay; original photo kept."));yield break;
            }
            SceneServiceConnection connection;
#if UNITY_EDITOR
            connection=TestConnection??development??SceneGeneration.ReadConnection();
#else
            connection=development??SceneGeneration.ReadConnection();
#endif
            var prepare=Task.Run(()=>
            {
                Directory.CreateDirectory(directory);if(File.Exists(pending))
                {
                    if(new FileInfo(pending).Length>4096)throw new InvalidDataException("Invalid pending cutout request.");var saved=JsonUtility.FromJson<CutoutRequest>(File.ReadAllText(pending));
                    if(saved.photoId!=photo.Id||saved.mode!=mode||!System.Text.RegularExpressions.Regex.IsMatch(saved.requestId??"",@"\A[a-f0-9]{32}\z"))throw new InvalidDataException("Pending photo identity mismatch.");return (saved,(byte[])null);
                }
                if(photo.File!=photo.Id+".png"&&photo.File!=photo.Id+".jpg")throw new InvalidDataException("Invalid source photo.");
                var bytes=File.ReadAllBytes(Path.Combine(new TreeStore(root).MediaRoot,photo.File));PhotoHeader.Read(bytes,out _,out _,out _);if(PanoramaCache.Hash(bytes)!=photo.Id)throw new InvalidDataException("Source photo checksum mismatch.");
                var body=new CutoutRequest{requestId=Guid.NewGuid().ToString("N"),photoId=photo.Id,mode=mode};TreeStore.AtomicText(pending,JsonUtility.ToJson(body));body.imageB64=Convert.ToBase64String(bytes);var payload=Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));body.imageB64=null;return (body,payload);
            });
            while(!prepare.IsCompleted)yield return null;if(prepare.IsFaulted)throw prepare.Exception.GetBaseException();var (request,payload)=prepare.Result;
            UnityWebRequest Request(string path,byte[] body=null)
            {
                var web=new UnityWebRequest(connection.url+path,body==null?"GET":"POST"){downloadHandler=new DownloadHandlerBuffer(),timeout=30,redirectLimit=0};web.SetRequestHeader("Authorization","Bearer "+connection.token);
                if(body!=null){web.uploadHandler=new UploadHandlerRaw(body);web.SetRequestHeader("Content-Type","application/json");}current=web;return web;
            }
            if(payload!=null)
            {
                Status("Extracting "+mode+" with Meta SAM 3.1…");while(!PanoramaFrameBudget.TryTake())yield return null;
                using(var web=Request("/cutouts",payload))
                {
                    yield return web.SendWebRequest();if(web.result!=UnityWebRequest.Result.Success)
                    {
                        if(web.responseCode==400||web.responseCode==401||web.responseCode==404||web.responseCode==409){var clear=Task.Run(()=>File.Delete(pending));while(!clear.IsCompleted)yield return null;Status(web.responseCode==409?"Another photo is extracting. Try again when it finishes.":web.responseCode==404?"Restart the Mac helper to enable SAM extraction.":"Photo helper rejected the request. Check setup before retrying.");}
                        else Status(web.responseCode==404?"Restart the Mac helper to enable SAM extraction.":"Connection interrupted. Click again to check this request without re-uploading.");yield break;
                    }
                }
            }
            float deadline=Time.realtimeSinceStartup+360;CutoutJob job=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                using(var web=Request("/cutouts/"+request.requestId))
                {
                    yield return web.SendWebRequest();if(web.result!=UnityWebRequest.Result.Success){Status("Cannot check extraction. Click again to check; no new upload.");yield break;}
                    job=JsonUtility.FromJson<CutoutJob>(web.downloadHandler.text);CutoutCache.Validate(job,request.requestId,photo.Id,mode);
                }
                if(job.status=="failed"){var clear=Task.Run(()=>File.Delete(pending));while(!clear.IsCompleted)yield return null;Status(job.error);yield break;}
                if(job.status=="complete")break;yield return new WaitForSecondsRealtime(1);
            }
            if(job?.status!="complete"){Status("Extraction is still pending. Click again to check.");yield break;}
            var result=new CutoutResult{sourcePhotoId=photo.Id,mode=mode,model=job.model};
            for(int i=0;i<job.items.Length;i++)
            {
                Status("Caching cutout "+(i+1)+" / "+job.items.Length+"…");byte[] bytes;
                using(var web=Request("/cutouts/"+request.requestId+"/images/"+i)){yield return web.SendWebRequest();if(web.result!=UnityWebRequest.Result.Success){Status("Download interrupted. Click again to resume this result.");yield break;}bytes=web.downloadHandler.data;}
                var item=job.items[i];var save=Task.Run(()=>CutoutCache.Save(root,photo,mode,item,bytes));while(!save.IsCompleted)yield return null;if(save.IsFaulted)throw save.Exception.GetBaseException();result.photos.Add(save.Result);yield return null;
            }
            string json=JsonUtility.ToJson(result);var finish=Task.Run(()=>{TreeStore.AtomicText(manifest,json);File.Delete(pending);});while(!finish.IsCompleted)yield return null;if(finish.IsFaulted)throw finish.Exception.GetBaseException();
            Result=result;Status(result.photos.Count==0?"No "+mode+" found. Try another mode or photo.":result.photos.Count+" cutout(s) ready. Choose the right person.");
        }
        void OnDestroy(){try{current?.Abort();}catch(ObjectDisposedException){}catch(NullReferenceException){}(operation as IDisposable)?.Dispose();operation=null;}
    }
}
