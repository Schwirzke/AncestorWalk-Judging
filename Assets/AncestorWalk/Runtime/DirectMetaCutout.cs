using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace AncestorWalk
{
    public static class DirectMetaCutout
    {
        static readonly HashSet<string> reviewed=new();
        public static IEnumerator Run(string storage,PhotoRecord photo,string mode,Action<CutoutResult> done,Action<string> status,Action<UnityWebRequest> track)
        {
            MetaApi.RequireKey();CutoutCache.Mode(mode);string directory=CutoutCache.DirectoryFor(storage,photo.Id,mode),pending=Path.Combine(directory,"direct-pending.json");
            if(File.Exists(pending)&&reviewed.Add(pending)){status("Earlier extraction was interrupted. Check Meta usage, then click Extract again to submit a new request.");yield break;}
            string file=Path.Combine(new TreeStore(storage).MediaRoot,photo.File);
            var validate=Task.Run(()=>{if(photo.File!=photo.Id+".png"&&photo.File!=photo.Id+".jpg")throw new InvalidDataException("Invalid photo path.");var bytes=File.ReadAllBytes(file);if(bytes.Length>32*1024*1024||PanoramaCache.Hash(bytes)!=photo.Id)throw new InvalidDataException("Photo checksum mismatch.");PhotoHeader.Read(bytes,out int w,out int h,out _);if(w>8192||h>8192||(long)w*h>16777216)throw new InvalidDataException("Choose a photo of at most 16 megapixels.");});
            while(!validate.IsCompleted)yield return null;if(validate.IsFaulted){_=validate.Exception;status("Source photo could not be validated.");yield break;}
            Texture2D full=null;RenderTexture small=null;Material orientation=null;byte[] pixels=null;int width=0,height=0;
            try
            {
                status("Preparing photo…");
                using(var web=UnityWebRequestTexture.GetTexture(new Uri(file).AbsoluteUri,true))
                {track(web);yield return web.SendWebRequest();track(null);if(web.result!=UnityWebRequest.Result.Success){status("Could not decode the source photo.");yield break;}while(!PanoramaFrameBudget.TryTake())yield return null;full=DownloadHandlerTexture.GetContent(web);}
                int orientedWidth=photo.Orientation>=5?full.height:full.width,orientedHeight=photo.Orientation>=5?full.width:full.height;float scale=Mathf.Min(1,1536f/Mathf.Max(orientedWidth,orientedHeight));width=Mathf.Max(1,Mathf.RoundToInt(orientedWidth*scale));height=Mathf.Max(1,Mathf.RoundToInt(orientedHeight*scale));
                while(!PanoramaFrameBudget.TryTake())yield return null;
                small=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);small.Create();orientation=new Material(Resources.Load<Shader>("Shaders/PhotoThumbnail"));orientation.SetFloat("_Orientation",photo.Orientation);orientation.SetFloat("_Aspect",1);
                bool old=GL.sRGBWrite;try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(full,small,orientation);}finally{GL.sRGBWrite=old;}
                if(SystemInfo.supportsAsyncGPUReadback){var read=AsyncGPUReadback.Request(small,0,TextureFormat.RGBA32);while(!read.done)yield return null;if(read.hasError){status("Could not read prepared photo.");yield break;}pixels=read.GetData<byte>().ToArray();}
                else
                {
                    while(!PanoramaFrameBudget.TryTake())yield return null;var copy=new Texture2D(width,height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
                    try{RenderTexture.active=small;copy.ReadPixels(new Rect(0,0,width,height),0,0);copy.Apply();pixels=copy.GetRawTextureData<byte>().ToArray();}finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(copy);}
                }
            }
            finally{track(null);if(full!=null)UnityEngine.Object.Destroy(full);if(small!=null)UnityEngine.Object.Destroy(small);if(orientation!=null)UnityEngine.Object.Destroy(orientation);}
            // Fresh pixel encoding strips EXIF/GPS and is thread-safe in Unity.
            var encode=Task.Run(()=>ImageConversion.EncodeArrayToPNG(pixels,UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,(uint)width,(uint)height));while(!encode.IsCompleted)yield return null;
            if(encode.IsFaulted){_=encode.Exception;status("Could not prepare photo upload.");yield break;}string image="data:image/png;base64,"+Convert.ToBase64String(encode.Result);
            string requestId=Guid.NewGuid().ToString("N");Directory.CreateDirectory(directory);TreeStore.AtomicText(pending,new JObject{["requestId"]=requestId,["photoId"]=photo.Id,["mode"]=mode,["phase"]="submitted"}.ToString());
            status("Extracting "+mode+" with Meta SAM 3.1…");var reply=new MetaReply();yield return Query(image,mode,reply,track);
            if(!reply.Success){status(reply.Error);yield break;}
            var parse=Task.Run(()=>SamMask.Parse(reply.Json,width,height));while(!parse.IsCompleted)yield return null;if(parse.IsFaulted){_=parse.Exception;status("SAM returned an incomplete or invalid mask. Original photo kept.");yield break;}
            List<SamMask> masks=parse.Result,faces=null;
            if(mode=="head"&&masks.Count>0)
            {
                status("Including facial detail…");var faceReply=new MetaReply();yield return Query(image,"face",faceReply,track);if(!faceReply.Success){status(faceReply.Error);yield break;}
                var faceParse=Task.Run(()=>SamMask.Parse(faceReply.Json,width,height));while(!faceParse.IsCompleted)yield return null;if(faceParse.IsFaulted){_=faceParse.Exception;status("SAM returned an invalid face mask. Original photo kept.");yield break;}faces=faceParse.Result;
            }
            image=null;var result=new CutoutResult{sourcePhotoId=photo.Id,mode=mode,model="sam-3.1"};
            for(int i=0;i<masks.Count;i++)
            {
                var mask=masks[i];status("Caching cutout "+(i+1)+" / "+masks.Count+"…");
                var save=Task.Run(()=>
                {
                    var cropped=Crop(pixels,width,height,mask,faces);if(cropped==null)return null;
                    var item=new CutoutItem{sha256=PanoramaCache.Hash(cropped),objectId=mask.Id};PhotoHeader.Read(cropped,out item.width,out item.height,out _);return CutoutCache.Save(storage,photo,mode,item,cropped);
                });
                while(!save.IsCompleted)yield return null;if(save.IsFaulted){_=save.Exception;status("Could not cache cutout. Original photo kept.");yield break;}if(save.Result!=null)result.photos.Add(save.Result);yield return null;
            }
            string json=JsonUtility.ToJson(result);var finish=Task.Run(()=>{TreeStore.AtomicText(Path.Combine(directory,"result.json"),json);File.Delete(pending);});while(!finish.IsCompleted)yield return null;if(finish.IsFaulted){_=finish.Exception;status("Could not save the extraction result.");yield break;}
            reviewed.Remove(pending);done(result);
        }
        static IEnumerator Query(string image,string concept,MetaReply reply,Action<UnityWebRequest> track)
        {
            var payload=new JObject{["model"]="sam-3.1",["stream"]=false,["store"]=false,["metadata"]=new JObject{["mask_encoding"]="one_bit"},["input"]=new JArray(new JObject{["type"]="message",["role"]="user",["content"]=new JArray(new JObject{["type"]="input_text",["text"]=concept},new JObject{["type"]="input_image",["image_url"]=image})})};
            yield return MetaApi.Send("responses",MetaApi.JsonBytes(payload),reply,track,timeout:300,maxBytes:24*1024*1024);
        }
        public static byte[] Crop(byte[] rgba,int width,int height,SamMask mask,List<SamMask> faces)
        {
            if(faces!=null)foreach(var face in faces)
            {
                float x=face.Left+face.Width*.5f,y=face.Top+face.Height*.5f;if(x<mask.Left||x>=mask.Left+mask.Width||y<mask.Top||y>=mask.Top+mask.Height)continue;
                for(int fy=0;fy<face.Height;fy++)for(int fx=0;fx<face.Width;fx++){int mx=face.Left+fx-mask.Left,my=face.Top+fy-mask.Top;if(mx>=0&&mx<mask.Width&&my>=0&&my<mask.Height)mask.Alpha[my*mask.Width+mx]=Math.Max(mask.Alpha[my*mask.Width+mx],face.Alpha[fy*face.Width+fx]);}
            }
            int minX=mask.Width,minY=mask.Height,maxX=-1,maxY=-1;
            for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++)if(mask.Alpha[y*mask.Width+x]>0&&rgba[((height-1-mask.Top-y)*width+mask.Left+x)*4+3]>0){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
            if(maxX<0)return null;int cw=maxX-minX+1,ch=maxY-minY+1,margin=Math.Max(2,(int)Math.Round(Math.Max(cw,ch)*.04));float scale=Math.Min(1,(1536-2*margin)/(float)Math.Max(cw,ch));int outW=Math.Max(1,(int)(cw*scale)),outH=Math.Max(1,(int)(ch*scale)),paddedW=outW+margin*2,paddedH=outH+margin*2;var output=new byte[paddedW*paddedH*4];
            for(int y=0;y<outH;y++)for(int x=0;x<outW;x++)
            {int mx=minX+Math.Min(cw-1,(int)(x/scale)),my=minY+Math.Min(ch-1,(int)(y/scale)),at=((height-1-mask.Top-my)*width+mask.Left+mx)*4,to=((paddedH-1-margin-y)*paddedW+margin+x)*4;Buffer.BlockCopy(rgba,at,output,to,3);output[to+3]=(byte)(rgba[at+3]*mask.Alpha[my*mask.Width+mx]/255);}
            return ImageConversion.EncodeArrayToPNG(output,UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,(uint)paddedW,(uint)paddedH);
        }
    }
}
