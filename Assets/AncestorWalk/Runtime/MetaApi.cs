using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AncestorWalk
{
    public sealed class MetaReply
    {
        public JObject Json;public string Error;public long Http;public float RetryAfter=2;
        public bool Success=>Error==null&&Json!=null;
    }
    // BYOK: no serialization, disk/keychain access, configurable hosts or helper fallback.
    public static class MetaApi
    {
        public const string BaseUrl="https://api.meta.ai/v1/",Image="muse-image-1.0",Spark="muse-spark-1.3",Voice="muse-voice-transcribe-1.0";
        static string sessionKey;public static bool Configured=>!string.IsNullOrEmpty(sessionKey);
        public static int ActiveRequests{get;private set;}
#if UNITY_EDITOR
        public static Func<string,byte[],MetaReply> TestReply;
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){sessionKey=null;ActiveRequests=0;}
        public static string ValidateKey(string key)
        {
            key=(key??"").Trim();if(key.Length<8||key.Length>=4096||key.Any(c=>c<33||c>126))throw new InvalidOperationException("Enter a valid Meta Model API key without spaces.");return key;
        }
        public static void UseKey(string key){sessionKey=ValidateKey(key);}
        public static void ForgetKey(){sessionKey=null;}
        public static void RequireKey(){if(!Configured)throw new InvalidOperationException("Open AI Setup and enter your Meta API key.");}
        public static byte[] JsonBytes(JObject json)=>Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        static readonly uint[] CrcTable=BuildCrc();
        static uint[] BuildCrc(){var table=new uint[256];for(uint n=0;n<256;n++){uint c=n;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;table[n]=c;}return table;}
        static uint Big32(byte[] data,int at)=>((uint)data[at]<<24)|((uint)data[at+1]<<16)|((uint)data[at+2]<<8)|data[at+3];
        public static void ValidatePngComplete(byte[] bytes)
        {
            if(bytes==null||bytes.Length<33||bytes.Length>32*1024*1024||!bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Invalid PNG.");
            int offset=8;bool image=false;
            while(offset<=bytes.Length-12)
            {
                uint size=Big32(bytes,offset);if(size>(uint)(bytes.Length-offset-12))throw new InvalidDataException("Truncated PNG chunk.");
                int length=(int)size;uint crc=uint.MaxValue;for(int i=offset+4;i<offset+8+length;i++)crc=CrcTable[(crc^bytes[i])&255]^(crc>>8);
                if((crc^uint.MaxValue)!=Big32(bytes,offset+8+length))throw new InvalidDataException("PNG checksum mismatch.");
                uint kind=Big32(bytes,offset+4);if(offset==8&&(kind!=0x49484452||length!=13))throw new InvalidDataException("Missing PNG header.");if(kind==0x49444154)image=true;
                offset+=length+12;if(kind==0x49454e44){if(length!=0||!image||offset!=bytes.Length)throw new InvalidDataException("Invalid PNG ending.");return;}
            }
            throw new InvalidDataException("Incomplete PNG.");
        }
        public static string Error(long code)=>code==401||code==403?"Meta rejected the key or model access. Check AI Setup.":code==429?"Meta rate limit reached. Wait before retrying.":code==0?"Meta connection interrupted. A submitted request may still finish; no automatic replay.":"Meta returned HTTP "+code+". Check model access and credits.";
        public static string DetailedError(long http,byte[] raw)
        {
            string summary=Error(http);if(raw==null||raw.Length>16384)return summary;
            try{var error=JObject.Parse(Encoding.UTF8.GetString(raw))["error"];foreach(string field in new[]{"code","type","param"}){string value=(string)error?[field];if(System.Text.RegularExpressions.Regex.IsMatch(value??"",@"\A[A-Za-z0-9_.\[\]-]{1,80}\z"))summary+=" "+field+": "+value+".";}}
            catch{}return summary;
        }
        public static IEnumerator Send(string path,byte[] body,MetaReply reply,Action<UnityWebRequest> track=null,string candidate=null,string contentType="application/json",int timeout=100,int maxBytes=1_000_000)
        {
            if(path!="models"&&path!="responses"&&path!="images/generations"&&path!="images/edits"&&path!="asr/transcribe")throw new InvalidOperationException("Unsupported Meta endpoint.");
            string key=candidate??sessionKey;
            if(string.IsNullOrEmpty(key)){reply.Error="Open AI Setup and enter your Meta API key.";yield break;}
#if UNITY_EDITOR
            if(TestReply!=null){var fake=TestReply(path,body);yield return null;reply.Json=fake.Json;reply.Http=fake.Http;reply.Error=fake.Error;reply.RetryAfter=fake.RetryAfter;yield break;}
#endif
            using var request=new UnityWebRequest(BaseUrl+path,body==null?"GET":"POST"){downloadHandler=new DownloadHandlerBuffer(),timeout=timeout,redirectLimit=0};
            request.SetRequestHeader("Authorization","Bearer "+key);key=null;candidate=null;
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(body);request.SetRequestHeader("Content-Type",contentType);}body=null;
            ActiveRequests++;track?.Invoke(request);
            try
            {
                var operation=request.SendWebRequest();
                while(!operation.isDone){if(request.downloadedBytes>(ulong)maxBytes){request.Abort();reply.Error="Meta response exceeded the supported size.";yield break;}yield return null;}
                reply.Http=request.responseCode;
                if(float.TryParse(request.GetResponseHeader("Retry-After"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float delay))reply.RetryAfter=Mathf.Clamp(delay,1,300);
                if(request.result!=UnityWebRequest.Result.Success){reply.Error=DetailedError(reply.Http,request.downloadHandler.data);yield break;}
                byte[] raw=request.downloadHandler.data;if(raw==null||raw.Length>maxBytes){reply.Error="Meta returned an oversized response.";yield break;}
                var parse=Task.Run(()=>JObject.Parse(Encoding.UTF8.GetString(raw),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error}));
                while(!parse.IsCompleted)yield return null;
                if(parse.IsFaulted){_=parse.Exception;reply.Error="Meta returned unreadable JSON.";yield break;}
                reply.Json=parse.Result;
            }
            finally{track?.Invoke(null);ActiveRequests--;}
        }
        public static IEnumerator Guard(IEnumerator routine,Action failure)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            try
            {
                while(stack.Count>0)
                {
                    var top=stack.Peek();bool more=false,failed=false;try{more=top.MoveNext();}catch{failed=true;}
                    if(failed){failure();yield break;}if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(top.Current is IEnumerator nested)stack.Push(nested);else yield return top.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        public static IEnumerator Flatten(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            try{while(stack.Count>0){var top=stack.Peek();if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator nested)stack.Push(nested);else yield return top.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        public static JObject ResearchPayload(string instructions,JObject context)=>new JObject{
            ["model"]=Spark,["instructions"]=instructions,["input"]=context.ToString(Formatting.None),["tools"]=new JArray(new JObject{["type"]="web_search"}),
            ["include"]=new JArray("web_search_call.results"),["max_output_tokens"]=4096,["reasoning"]=new JObject{["effort"]="low"},["store"]=false};
        public static (string Text,GuideSource[] Sources) SourcedText(JObject result,bool allowRetrievedUrls)
        {
            if((string)result["status"]!="completed"||result["output"] is not JArray outputs)throw new InvalidOperationException("Historical research did not finish.");
            var searches=outputs.OfType<JObject>().Where(o=>(string)o["type"]=="web_search_call"&&(string)o["status"]=="completed").ToArray();
            if(searches.Length==0)throw new InvalidOperationException("No completed web research was returned.");
            var sources=new List<GuideSource>();var paragraphs=new List<string>();
            foreach(var item in outputs.OfType<JObject>().Where(o=>(string)o["type"]=="message"&&(string)o["role"]=="assistant"&&(string)o["phase"]!="commentary"))
            foreach(var part in (item["content"] as JArray??new JArray()).OfType<JObject>().Where(o=>(string)o["type"]=="output_text"))
            {
                string text=(string)part["text"]??"";var inserts=new List<(int At,int Source)>();
                foreach(var annotation in (part["annotations"] as JArray??new JArray()).OfType<JObject>())
                {
                    string url=(string)annotation["url"];int start=(int?)annotation["start_index"]??-1,end=(int?)annotation["end_index"]??-1;
                    if((string)annotation["type"]!="url_citation"||!HistoricalGuide.SafeUrl(url)||start<0||end<=start)continue;
                    // Provider indices count Unicode code points; C# strings count UTF-16 units.
                    int offset=Utf16Offset(text,end);if(offset<0)continue;
                    int index=sources.FindIndex(s=>s.url==url);if(index<0){sources.Add(Source(url,(string)annotation["title"]));index=sources.Count-1;}inserts.Add((offset,index+1));
                }
                foreach(var insert in inserts.Distinct().OrderByDescending(i=>i.At))text=text.Insert(insert.At," ["+insert.Source+"]");paragraphs.Add(text);
            }
            string answer=string.Join("\n\n",paragraphs).Trim();
            if(allowRetrievedUrls&&sources.Count==0)
            foreach(var search in searches)foreach(var hit in (search["results"] as JArray??new JArray()).OfType<JObject>())
            {
                string url=(string)hit["url"];if(!HistoricalGuide.SafeUrl(url)||sources.Any(s=>s.url==url)||!System.Text.RegularExpressions.Regex.IsMatch(answer,System.Text.RegularExpressions.Regex.Escape(url)+@"(?=$|[\s<>)\]}.,;])"))continue;
                sources.Add(Source(url,(string)hit["title"]));
            }
            if(answer.Length==0||answer.Length>12000||sources.Count==0||sources.Count>30)throw new InvalidOperationException("No usable sourced answer was returned.");
            return(answer,sources.ToArray());
        }
        static GuideSource Source(string url,string title){title=string.IsNullOrWhiteSpace(title)?new Uri(url).Host:title;return new GuideSource{url=url,title=title.Length>180?title.Substring(0,180):title};}
        static int Utf16Offset(string text,int points){int i=0;for(int p=0;p<points;p++){if(i>=text.Length)return -1;i+=char.IsHighSurrogate(text[i])&&i+1<text.Length&&char.IsLowSurrogate(text[i+1])?2:1;}return i;}
        public static byte[] SpeechBody(byte[] wav,out string contentType)
        {
            if(wav==null||wav.Length<44||wav.Length>1_500_044)throw new InvalidOperationException("Record up to 30 seconds of speech.");
            string boundary="ancestorwalk-"+Guid.NewGuid().ToString("N");contentType="multipart/form-data; boundary="+boundary;
            string settings=new JObject{["model"]=Voice,["mode"]="PUSH_TO_TALK",["audioEncoding"]="WAV"}.ToString(Formatting.None);
            byte[] head=Encoding.UTF8.GetBytes("--"+boundary+"\r\nContent-Disposition: form-data; name=\"request\"\r\nContent-Type: application/json\r\n\r\n"+settings+"\r\n--"+boundary+"\r\nContent-Disposition: form-data; name=\"audio\"; filename=\"question.wav\"\r\nContent-Type: audio/wav\r\n\r\n"),tail=Encoding.ASCII.GetBytes("\r\n--"+boundary+"--\r\n");
            byte[] body=new byte[head.Length+wav.Length+tail.Length];Buffer.BlockCopy(head,0,body,0,head.Length);Buffer.BlockCopy(wav,0,body,head.Length,wav.Length);Buffer.BlockCopy(tail,0,body,head.Length+wav.Length,tail.Length);return body;
        }
        public static IEnumerator Transcribe(byte[] wav,Action<string,string> done,Action<UnityWebRequest> track)
        {
            byte[] body=SpeechBody(wav,out string contentType);var reply=new MetaReply();yield return Send("asr/transcribe",body,reply,track,contentType:contentType);
            if(!reply.Success){done(null,reply.Error);yield break;}
            string text=((string)reply.Json["transcript"]??"").Trim();done(text.Length>0&&text.Length<=2000?text:null,text.Length==0||text.Length>2000?"No usable speech recognized. Please record again.":null);
        }
    }
}
