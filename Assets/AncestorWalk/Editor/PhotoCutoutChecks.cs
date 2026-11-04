using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class PhotoCutoutChecks
    {
        sealed class Helper:IDisposable
        {
            readonly HttpListener listener=new();readonly Thread thread;readonly byte[] png;readonly string hash;CutoutJob job;
            public readonly string Url;public int Posts,Downloads;public bool InterruptOnce=true;
            public Helper(byte[] image)
            {png=image;hash=PanoramaCache.Hash(png);var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();int port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();Url="http://127.0.0.1:"+port;listener.Prefixes.Add(Url+"/");listener.Start();thread=new Thread(Serve){IsBackground=true};thread.Start();}
            [Serializable] sealed class Request{public string requestId,photoId,mode,imageB64;public bool allowUpload;}
            void Send(HttpListenerContext c,object value){var raw=Encoding.UTF8.GetBytes(JsonUtility.ToJson(value));c.Response.ContentType="application/json";c.Response.OutputStream.Write(raw,0,raw.Length);c.Response.Close();}
            void Serve()
            {
                while(listener.IsListening)try
                {
                    var c=listener.GetContext();if(c.Request.HttpMethod=="POST")
                    {
                        Posts++;var r=JsonUtility.FromJson<Request>(new StreamReader(c.Request.InputStream).ReadToEnd());
                        if(!r.allowUpload||PanoramaCache.Hash(Convert.FromBase64String(r.imageB64))!=r.photoId)throw new Exception("Fixture source identity");
                        job=new CutoutJob{requestId=r.requestId,photoId=r.photoId,mode=r.mode,model="sam-3.1",status="complete",items=new[]{new CutoutItem{sha256=hash,width=8,height=8,objectId="4"}}};Send(c,job);
                    }
                    else if(c.Request.Url.AbsolutePath.Contains("/images/"))
                    {Downloads++;if(InterruptOnce){InterruptOnce=false;c.Response.StatusCode=503;c.Response.Close();}else{c.Response.ContentType="image/png";c.Response.OutputStream.Write(png,0,png.Length);c.Response.Close();}}
                    else Send(c,job);
                }
                catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
            }
            public void Dispose(){listener.Close();thread.Join(1000);}
        }
        public static string Play()
        {var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null)throw new Exception("Play mode required");Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/photo-cutout-checks.txt","RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Checking photo cutouts with isolated fake SAM helper; no uploads";}
        static IEnumerator Runner(IEnumerator routine)
        {
            var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(routine);
            while(stack.Count>0){var r=stack.Peek();bool more=false;Exception error=null;object next=null;try{more=r.MoveNext();if(more)next=r.Current;}catch(Exception e){error=e;}if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText("Artifacts/photo-cutout-checks.txt","FAILED: "+error);yield break;}if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;}
        }
        static IEnumerator Checks(AncestorGallery gallery)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            int errors=0;void Log(string text,string stack,LogType type){if(type==LogType.Exception&&stack.Contains("PhotoSegmentation"))errors++;}Application.logMessageReceived+=Log;
            string root=Path.GetFullPath("Library/CutoutChecks/"+Guid.NewGuid().ToString("N"));var store=new TreeStore(root);Directory.CreateDirectory(store.MediaRoot);
            var image=new Texture2D(8,8,TextureFormat.RGBA32,false);image.SetPixels(Enumerable.Range(0,64).Select(i=>i%8==0||i<8?Color.clear:Color.yellow).ToArray());image.Apply();var png=image.EncodeToPNG();UnityEngine.Object.Destroy(image);
            string source=Path.GetFullPath("Library/CutoutChecks/synthetic-portrait.png");var photo=TreeArchiveImport.AddPhoto(store,source);var go=new GameObject("Isolated cutout client");var client=go.AddComponent<PhotoSegmentation>();
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(PhotoSegmentation).GetField("TestStorage",flags).SetValue(client,root);
            IEnumerator Done(){float end=Time.realtimeSinceStartup+20;while(client.Busy&&Time.realtimeSinceStartup<end)yield return null;Check(!client.Busy,"Async extraction completes while frames advance");}
            try
            {
                using(var helper=new Helper(png))
                {
                    typeof(PhotoSegmentation).GetField("TestConnection",flags).SetValue(client,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});
                    int frame=Time.frameCount;client.Extract(photo,"head");yield return Done();Check(client.Result==null&&client.Message.Contains("Download interrupted"),"Interrupted download keeps request for recovery");
                    client.Extract(photo,"head");yield return Done();Check(helper.Posts==1&&helper.Downloads==2,"Retry recovers same request without another upload/model call");Check(Time.frameCount>frame,"Network/file work does not stop frames");
                    Check(client.Result.photos.Count==1,"Candidate ready");var cut=client.Result.photos[0];Check(cut.SourcePhotoId==photo.Id&&cut.CutoutMode=="head"&&cut.CutoutModel=="sam-3.1","Cutout keeps source provenance");Check(cut.Width==8&&cut.Orientation==1,"Cutout records normalized dimensions");
                    Check(File.Exists(Path.Combine(store.MediaRoot,photo.File)),"Original photo retained");
                    var longSource=JsonUtility.FromJson<PhotoRecord>(JsonUtility.ToJson(photo));longSource.Title=new string('x',200);var bounded=CutoutCache.Save(root,longSource,"head",new CutoutItem{sha256=cut.Id,width=8,height=8,objectId="4"},png);Check(bounded.Title.Length==200,"Long source titles remain within saved document bounds");
                    var saved=CutoutCache.Read(root,photo,"head");Check(saved!=null&&saved.photos[0].Id==cut.Id,"Verified result survives cache reload");Check(CutoutCache.Read(root,photo,"face")==null,"Modes have independent caches");
                    client.Extract(photo,"head");yield return Done();Check(helper.Posts==1&&helper.Downloads==2&&client.Result!=null,"Warm results perform zero network requests");
                    File.WriteAllBytes(Path.Combine(store.MediaRoot,cut.File),new byte[24]);Check(CutoutCache.Read(root,photo,"head")==null,"Corrupt cached cutouts rejected");
                    var wrong=new CutoutJob{requestId=new string('a',32),photoId=photo.Id,mode="face",model="sam-3.1",status="complete",items=Array.Empty<CutoutItem>()};bool rejects=false;try{CutoutCache.Validate(wrong,new string('a',32),photo.Id,"head");}catch(InvalidDataException){rejects=true;}Check(rejects,"Response cannot attach another mode's result");
                    File.WriteAllBytes(Path.Combine(store.MediaRoot,cut.File),png);
                    var lease=gallery.Photos.Acquire(cut);gallery.Photos.Release(lease);Check(gallery.Photos.Pending==0,"Unused unstarted preview releases cleanly");
                }
                UnityEngine.Object.DestroyImmediate(go);go=null;Check(errors==0,"Client teardown does not abort a disposed web request");
                File.WriteAllText("Artifacts/photo-cutout-checks.txt",n+" photo cutout checks passed: async HTTP, download-only recovery, source integrity, per-mode persistent cache, provenance, original preservation and clean teardown. No Meta uploads.");
            }
            finally{Application.logMessageReceived-=Log;if(go!=null)UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
