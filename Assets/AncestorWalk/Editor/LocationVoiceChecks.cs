using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class LocationVoiceChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            Check(LocationVoiceInput.Place("  München,   Germany. ")=="München, Germany","Place normalization preserves Unicode");
            foreach(string text in new[]{"",new string('x',ResidenceTimeline.MaximumLocationLength+1),"Paris\0France"}){bool bad=false;try{LocationVoiceInput.Place(text);}catch(InvalidOperationException){bad=true;}Check(bad,"Invalid place rejected");}
            Check(LocationVoiceInput.Place(new string('x',1000)).Length==1000,"Long location descriptions accepted");
            Check(LocationVoiceInput.Dictation("  Add a blue car.\r\nDress them in 1970s clothing! ")=="Add a blue car.\nDress them in 1970s clothing!","Details dictation preserves punctuation and line breaks");
            foreach(string text in new[]{"",new string('x',1001),"Paris\0France"}){bool bad=false;try{LocationVoiceInput.Dictation(text);}catch(InvalidOperationException){bad=true;}Check(bad,"Invalid details dictation rejected");}
            var request=new LocationSpeechRequest{requestId=new string('a',32),purpose="place"};
            Check(LocationVoiceInput.Matches(request,new LocationSpeechResult{requestId=request.requestId,purpose="place",text="Paris"}),"Exact recording result accepted");
            Check(!LocationVoiceInput.Matches(request,new LocationSpeechResult{requestId=request.requestId,purpose="approval",text="Approve"}),"Approval cannot masquerade as place transcription");
            Check(!LocationVoiceInput.Matches(new LocationSpeechRequest{requestId=request.requestId,purpose="approval"},new LocationSpeechResult{requestId=request.requestId,purpose="approval",text="Approve"}),"The client accepts dictation only");
            Check(!LocationVoiceInput.Matches(request,new LocationSpeechResult{requestId=new string('b',32),purpose="place",text="Paris"}),"Stale request identity rejected");
            Check(!LocationVoiceInput.Matches(request,null)&&!LocationVoiceInput.Matches(null,new LocationSpeechResult()),"Missing recording/result rejected");
            return n+" voice-location policy checks passed.";
        }
        public static string Play()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g.Generation.Busy)throw new Exception("Wait for active generation before fixture checks");
            bool endpointGuard=UnityEditor.SessionState.GetBool("AncestorWalk.EndpointChecks",false);UnityEditor.SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            var originalGeneration=g.Generation;var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var pos=nav.transform.position;var rot=nav.transform.rotation;var look=nav.view.transform.localRotation;string selected=g.SelectedId;
            var pitchField=typeof(GalleryNavigation).GetField("pitch",Hidden);var pitch=pitchField.GetValue(nav);
            var holder=new GameObject("Inactive voice-generation fixture");holder.SetActive(false);var generation=holder.AddComponent<SceneGeneration>();
            typeof(SceneGeneration).GetField("storage",Hidden).SetValue(generation,Path.GetFullPath("Library/LocationVoiceChecks/"+Guid.NewGuid().ToString("N")));
            typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(generation,new SceneServiceConnection{url="http://127.0.0.1:1",token=new string('a',64)});typeof(SceneGeneration).GetProperty("Ready").SetValue(generation,true);
            typeof(AncestorGallery).GetProperty("Generation").SetValue(g,generation);
            string id="voice-fixture-"+Guid.NewGuid().ToString("N");var person=new Person{Id=id,Name="Voice location fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            var places=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",Hidden).GetValue(g);g.Tree.People.Add(id,person);places[id]=ResidenceTimeline.Load(g.TreeId,person);
            string path=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(id)+".json");
            var editor=g.LocationEditor;var heard=typeof(LocationEditor).GetMethod("Recognized",Hidden);string Value()=>(string)typeof(LocationEditor).GetField("value",Hidden).GetValue(editor);
            T[] Parts<T>() where T:Component=>((GameObject)typeof(LocationEditor).GetField("panel",Hidden).GetValue(editor)).GetComponentsInChildren<T>(true);
            void Hear(string purpose,string text)=>heard.Invoke(editor,new object[]{purpose,text});
            try
            {
                g.SetYear(id,1934);g.Select(id);g.EditLocation(id);
                Check(Parts<LocationKey>().Count(k=>k.value=="voice-place")==1&&Parts<LocationKey>().Count(k=>k.value=="voice-details")==1&&!Parts<LocationKey>().Any(k=>k.value=="voice-approval"),"Dedicated location and details microphones without approval");
                var mic=Parts<LocationKey>().Single(k=>k.value=="voice-place").transform;var entry=Parts<Transform>().Single(t=>t.name=="Location text field");
                var detailsMic=Parts<LocationKey>().Single(k=>k.value=="voice-details").transform;
                var detailsField=(TMPro.TMP_InputField)typeof(LocationEditor).GetField("detailsInput",Hidden).GetValue(editor);
                var target=typeof(LocationEditor).GetField("voiceForDetails",Hidden);
                Check(mic.localPosition.x+mic.localScale.x*.5f<entry.localPosition.x-entry.localScale.x*.5f&&Mathf.Abs(mic.localPosition.y-entry.localPosition.y)<.001f,"Microphone sits left of and level with the entry field");
                Check(mic.parent.Find("Microphone icon").GetComponentsInChildren<LineRenderer>().Length==4&&!Parts<TextMesh>().Any(t=>t.text=="SPEAK LOCATION"),"Microphone icon replaces the text button");
                Check(Parts<LocationKey>().All(k=>{var renderer=k.GetComponent<Renderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);return (block.HasColor("_Color")?block.GetColor("_Color"):renderer.sharedMaterial.color).maxColorComponent<.4f;}),"All location buttons use dark surfaces through their fade materials");
                foreach(var key in Parts<LocationKey>().Where(k=>k.value.StartsWith("voice-")))
                {var eye=Camera.main.transform.position;Physics.SyncTransforms();Check(Physics.Raycast(eye,(key.transform.position-eye).normalized,out var hit,15)&&hit.collider==key.GetComponent<Collider>(),"Voice controls are reachable");}
                var recording=editor.GetComponent<LocationVoiceInput>();var tick=typeof(LocationEditor).GetMethod("Update",Hidden);
                var wave=Parts<LineRenderer>().Single(w=>w.name=="Listening waveform");
                Check(!wave.gameObject.activeSelf,"Listening wave hidden while idle");
                try
                {
                    typeof(LocationVoiceInput).GetProperty("Recording").SetValue(recording,true);tick.Invoke(editor,null);
                    Check(wave.gameObject.activeSelf&&wave.positionCount==33&&Enumerable.Range(0,33).Any(i=>Mathf.Abs(wave.GetPosition(i).y)>.0001f),"Recording shows a waveform without capturing audio");
                    typeof(LocationVoiceInput).GetProperty("Busy").SetValue(recording,true);typeof(LocationEditor).GetMethod("VoiceChanged",Hidden).Invoke(editor,null);
                    Check(mic.GetComponent<Collider>().enabled&&!detailsMic.GetComponent<Collider>().enabled,"Only active microphone can finish its recording");
                    int token=(int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(recording);editor.Activate("voice-details");
                    Check(!(bool)target.GetValue(editor)&&recording.Recording&&(int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(recording)==token,"Other microphone cannot interrupt or retarget recording");
                }
                finally{typeof(LocationVoiceInput).GetProperty("Recording").SetValue(recording,false);typeof(LocationVoiceInput).GetProperty("Busy").SetValue(recording,false);tick.Invoke(editor,null);}
                Check(!wave.gameObject.activeSelf,"Listening wave hides when recording ends");
                typeof(LocationVoiceInput).GetProperty("Busy").SetValue(recording,true);
                try
                {
                    typeof(LocationEditor).GetMethod("VoiceChanged",Hidden).Invoke(editor,null);
                    Check(!mic.GetComponent<Collider>().enabled&&!detailsMic.GetComponent<Collider>().enabled,"Neither microphone can be clicked during processing");
                    int token=(int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(recording);
                    editor.Activate("voice-place");recording.StartListening("place");
                    Check(recording.Busy&&!recording.Recording&&(int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(recording)==token,"Repeated clicks preserve the in-flight transcription");
                }
                finally{typeof(LocationVoiceInput).GetProperty("Busy").SetValue(recording,false);typeof(LocationEditor).GetMethod("VoiceChanged",Hidden).Invoke(editor,null);}
                Check(mic.GetComponent<Collider>().enabled,"Microphone re-enables after processing");
                target.SetValue(editor,true);editor.Activate("focus");Hear("place","Add a blue car.\nDress them in 1970s clothing!");
                Check(detailsField.text=="Add a blue car.\nDress them in 1970s clothing!"&&Value()=="York","Focus changes do not redirect details transcription");
                target.SetValue(editor,false);editor.Activate("focus-details");
                Hear("place","Paris, France.");Check(Value()=="Paris, France"&&editor.IsOpen&&!File.Exists(path)&&generation.WaitingCount==0,"Dictation only fills review field");
                Check(detailsField.text=="Add a blue car.\nDress them in 1970s clothing!","Location transcription leaves details intact");
                Check((bool)typeof(LocationEditor).GetField("selectAll",Hidden).GetValue(editor),"Recognized text is selected for correction");
                Check(!recording.Busy&&!recording.Recording,"Dictation does not start another recording");
                Hear("approval","Approve.");Check(editor.IsOpen&&generation.WaitingCount==0&&!File.Exists(path),"Spoken approval has no save or generation effect");
                editor.Activate("save");Check(!editor.IsOpen&&generation.Scheduled(g.TreeId,id,1934,"Paris, France"),"Generate button saves and queues the exact year");
                Check(ResidenceTimeline.Load(g.TreeId,person).At(person,1934)=="Paris, France","Button-confirmed location persisted");
                Hear("approval","Generate");Check(generation.WaitingCount==1,"Repeated/late approval after close does nothing");
                g.SetYear(id,1935);g.EditLocation(id);Hear("place","Rome, Italy");editor.Activate("cancel");
                Check(!editor.IsOpen&&generation.WaitingCount==1&&!ResidenceTimeline.Load(g.TreeId,person).entries.Any(e=>e.year==1935),"Cancel button preserves saved entries");
                g.EditLocation(id);Check(!Parts<LocationKey>().Any(k=>k.value=="entry"||k.value=="previous"||k.value=="next"),"Saved-entry browsing controls stay hidden even when entries exist");
                Hear("place","Generate");Check(Value()=="Generate"&&editor.IsOpen&&generation.WaitingCount==1,"Dictated words are text, never commands");
                Hear("place","Tokyo, Japan");var input=(TMPro.TMP_InputField)typeof(LocationEditor).GetField("nativeInput",Hidden).GetValue(editor);input.ForceLabelUpdate();input.textComponent.ForceMeshUpdate();input.stringPosition=0;input.selectionFocusPosition=input.text.Length;input.selectionStringFocusPosition=input.text.Length;editor.Activate("X");Hear("approval","Generate");
                Check(Value()=="X"&&editor.IsOpen&&generation.WaitingCount==1,"Typing replaces selected transcript without generating");
                var voice=editor.GetComponent<LocationVoiceInput>();int before=(int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(voice);editor.Close();
                Check((int)typeof(LocationVoiceInput).GetField("version",Hidden).GetValue(voice)>before&&!voice.Busy&&!voice.Recording,"Close invalidates pending recording results");
                string report=n+" voice-location UI checks passed: dictation-only microphone, review, button-confirmed save/queue, cancellation, correction and stale-result guards. No microphone capture or Meta calls.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/location-voice-checks.txt",report);return report;
            }
            finally
            {
                editor.Close();typeof(AncestorGallery).GetProperty("Generation").SetValue(g,originalGeneration);UnityEngine.Object.DestroyImmediate(holder);g.Tree.People.Remove(id);places.Remove(id);g.ShowTree();
                if(selected!=null)g.Select(selected);nav.transform.SetPositionAndRotation(pos,rot);nav.view.transform.localRotation=look;pitchField.SetValue(nav,pitch);
                PlayerPrefs.DeleteKey("year."+g.TreeId+"."+id);if(File.Exists(path))File.Delete(path);
                UnityEditor.SessionState.SetBool("AncestorWalk.EndpointChecks",endpointGuard);
            }
        }
    }
}
