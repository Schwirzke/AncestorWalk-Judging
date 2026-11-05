using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AncestorWalk
{
    // Shared by dictation and the guide so every microphone finishes the same way.
    public sealed class SpeechPauseDetector
    {
        public const float PauseSeconds=1.5f;
        public const string ListeningMessage="Listening… pause 1.5 seconds to send, or tap the mic again.";
        bool heardSpeech;float lastSpeech;
        public void Reset(){heardSpeech=false;lastSpeech=0;}
        public void Observe(float[] samples,float now)
        {
            if(samples==null||samples.Length==0)return;
            double power=0;foreach(float sample in samples)power+=sample*sample;
            if(Math.Sqrt(power/samples.Length)>.006){heardSpeech=true;lastSpeech=now;}
        }
        public bool Finished(float now)=>heardSpeech&&now-lastSpeech>=PauseSeconds;
    }
    [Serializable] public sealed class LocationSpeechRequest {public string requestId,purpose,audio;}
    [Serializable] public sealed class LocationSpeechResult {public string requestId,purpose,text,error,transcriptionModel;}
    public sealed class LocationVoiceInput : MonoBehaviour
    {
        public const int MaximumSeconds=12;
        public bool Recording{get;private set;}public bool Busy{get;private set;}
        public string Purpose{get;private set;}public string Status{get;private set;}="";
        public event Action Changed;public event Action<string,string> Recognized;
        AudioClip clip;string device;float started;float[] meter;int version;
        readonly SpeechPauseDetector speechPause=new SpeechPauseDetector();
        UnityWebRequest active;SceneServiceConnection connection;
        IEnumerator operation;
        void Notify(string text){Status=text;Changed?.Invoke();}
        public static string Place(string text)
        {
            string value=ResidenceTimeline.Normalize(text).TrimEnd('.','!','?').Trim();
            if(value.Length==0||value.Length>ResidenceTimeline.MaximumLocationLength||value.Any(char.IsControl))throw new InvalidOperationException("Speak a location description of up to 1,000 characters.");return value;
        }
        public static string Dictation(string text)
        {
            string value=(text??"").Replace("\r\n","\n").Replace('\r','\n').Trim();
            if(value.Length==0||value.Length>SceneYearDetails.MaximumLength||value.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\t'))throw new InvalidOperationException("Dictate up to 1,000 characters.");return value;
        }
        public static bool Matches(LocationSpeechRequest request,LocationSpeechResult result)=>request!=null&&request.purpose=="place"&&result!=null&&result.requestId==request.requestId&&result.purpose==request.purpose&&!string.IsNullOrWhiteSpace(result.text)&&result.text.Length<=ResidenceTimeline.MaximumLocationLength;
        public void StartListening(string purpose)
        {
            if(Busy)return;
            Cancel();Purpose=purpose;
            if(purpose!="place"){Notify("Use the microphone to dictate a location.");return;}
            try{connection=DevelopmentAI.Resolve();}catch(Exception e){Notify(e.Message);return;}
            Busy=true;StartCoroutine(Begin(version));
        }
        IEnumerator Begin(int token)
        {
            Notify("Checking microphone…");
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                bool resolved=false;var callbacks=new UnityEngine.Android.PermissionCallbacks();callbacks.PermissionGranted+=_=>resolved=true;callbacks.PermissionDenied+=_=>resolved=true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone,callbacks);float deadline=Time.realtimeSinceStartup+60;
                while(!resolved&&Time.realtimeSinceStartup<deadline&&token==version)yield return null;
            }
            bool allowed=UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            bool allowed=Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
            if(token!=version)yield break;
            if(!allowed){Busy=false;Notify("Microphone access is off. Enable it in system settings to speak.");yield break;}
            try
            {
                if(Microphone.devices.Length==0)throw new InvalidOperationException();
                device=Microphone.devices[0];clip=Microphone.Start(device,false,MaximumSeconds+1,24000);if(clip==null)throw new InvalidOperationException();
                meter=new float[1024*clip.channels];Recording=true;speechPause.Reset();started=Time.realtimeSinceStartup;
            }
            catch{ReleaseMicrophone();Busy=false;Notify("Could not start the microphone. You can still type the location.");yield break;}
            Notify(SpeechPauseDetector.ListeningMessage);
        }
        void Update()
        {
            if(!Recording)return;int frames=Microphone.GetPosition(device);
            if(frames>=1024&&clip.GetData(meter,frames-1024))
                speechPause.Observe(meter,Time.realtimeSinceStartup);
            if(Time.realtimeSinceStartup-started>=MaximumSeconds||speechPause.Finished(Time.realtimeSinceStartup))FinishRecording();
        }
        public void FinishRecording()
        {
            if(!Recording)return;byte[] wav=null;string error=null;
            try
            {
                int frames=Math.Min(Microphone.GetPosition(device),clip.frequency*MaximumSeconds);if(frames<clip.frequency/2)throw new InvalidOperationException("Recording was too short. Please speak again.");var samples=new float[frames*clip.channels];
                if(!clip.GetData(samples,0))throw new InvalidOperationException();wav=HistoricalGuide.EncodeWav(samples,clip.channels,clip.frequency);
            }
            catch(Exception e){error=e is InvalidOperationException?e.Message:"Could not read recording. Try speaking again.";}
            ReleaseMicrophone();if(error!=null){Busy=false;Notify(error);return;}
            var request=new LocationSpeechRequest{requestId=Guid.NewGuid().ToString("N"),purpose=Purpose,audio=Convert.ToBase64String(wav)};operation=MetaApi.Flatten(Transcribe(request,version));StartCoroutine(operation);
        }
        IEnumerator Transcribe(LocationSpeechRequest body,int token)
        {
            if(connection==null){yield return MetaApi.Guard(TranscribeDirect(body,token),()=>{Busy=false;active=null;Notify("Meta transcription interrupted. Please record again.");});yield break;}
            Notify("Transcribing…");
            using(var request=new UnityWebRequest(connection.url+"/location-speech","POST"))
            try
            {
                active=request;request.timeout=110;request.redirectLimit=0;request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));body.audio=null;
                request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");request.SetRequestHeader("Authorization","Bearer "+connection.token);
                yield return request.SendWebRequest();if(token!=version)yield break;Busy=false;active=null;
                LocationSpeechResult result=null;try{if(request.downloadHandler.data.Length<=10000)result=JsonUtility.FromJson<LocationSpeechResult>(request.downloadHandler.text);}catch{}
                if(request.result!=UnityWebRequest.Result.Success){Notify(request.responseCode==404?"Restart the Mac helper to enable voice locations.":result?.error??"Voice connection failed. Try speaking again.");yield break;}
                if(!Matches(body,result)){Notify("Voice result did not match this recording. Please try again.");yield break;}
                Notify("Speech recognized.");Recognized?.Invoke(body.purpose,result.text);
            }
            finally{if(ReferenceEquals(active,request))active=null;}
        }
        IEnumerator TranscribeDirect(LocationSpeechRequest body,int token)
        {
            Notify("Transcribing…");string text=null,error=null;byte[] wav=Convert.FromBase64String(body.audio);body.audio=null;
            yield return MetaApi.Transcribe(wav,(value,failure)=>{text=value;error=failure;},request=>active=request);wav=null;
            if(token!=version)yield break;Busy=false;
            if(error!=null){Notify(error);yield break;}
            string place=null;try{place=Dictation(text);}catch(InvalidOperationException e){Notify(e.Message);yield break;}
            Notify("Speech recognized.");Recognized?.Invoke(body.purpose,place);
        }
        void ReleaseMicrophone(){if(clip!=null){Microphone.End(device);Destroy(clip);clip=null;}meter=null;Recording=false;}
        public void Cancel()
        {
            ++version;var request=active;active=null;if(request!=null)try{request.Abort();}catch(ObjectDisposedException){}
            StopAllCoroutines();(operation as IDisposable)?.Dispose();operation=null;ReleaseMicrophone();Busy=false;
        }
        void OnApplicationPause(bool paused){if(paused)Cancel();}
        void OnDisable()=>Cancel();void OnDestroy()=>Cancel();
    }
}
