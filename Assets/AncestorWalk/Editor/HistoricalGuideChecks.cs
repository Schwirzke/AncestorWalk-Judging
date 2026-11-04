using InputField = TMPro.TMP_InputField;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class HistoricalGuideChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool value,string message){if(!value)throw new Exception(message);n++;}
            var samples=new float[48000*2];for(int i=0;i<samples.Length;i++)samples[i]=.25f;
            byte[] wav=HistoricalGuide.EncodeWav(samples,2,48000);
            using(var reader=new BinaryReader(new MemoryStream(wav)))
            {
                Check(new string(reader.ReadChars(4))=="RIFF","WAV signature");Check(reader.ReadInt32()==wav.Length-8,"WAV length");
                reader.BaseStream.Position=22;Check(reader.ReadInt16()==1,"Stereo capture downmixed to mono");Check(reader.ReadInt32()==24000,"Output resampled to 24 kHz");
                reader.BaseStream.Position=34;Check(reader.ReadInt16()==16,"PCM16 output");reader.BaseStream.Position=40;Check(reader.ReadInt32()==48000,"Exactly one second of audio");
            }
            bool silent=false;try{HistoricalGuide.EncodeWav(new float[24000],1,24000);}catch(InvalidOperationException){silent=true;}Check(silent,"Silence rejected before upload");
            Check(!HistoricalGuide.SafeUrl("javascript:alert(1)")&&!HistoricalGuide.SafeUrl("file:///tmp/test")&&!HistoricalGuide.SafeUrl("https://user:pass@example.com"),"Unsafe citation schemes and credentials rejected");
            Check(HistoricalGuide.SafeUrl("https://example.com/archive"),"HTTPS citation accepted");
            string text=string.Join(" ",Enumerable.Repeat("Historical context with citations [1].",80));var pages=SceneOverlay.PaginateGuide(text);
            Check(pages.Count>1&&pages.All(p=>p.Length<=550),"Long answers paginate");Check(string.Join(" ",pages)==text,"Pagination preserves answer");
            var gallery=UnityEngine.Object.FindFirstObjectByType<AncestorGallery>();string old=gallery.SelectedId;
            bool tutorialOpen=gallery.Tutorial.IsOpen;gallery.Tutorial.Dismiss();
            try
            {
                string personId=gallery.Tree.People.Values.First(p=>LifeTimeline.TryRange(p,out _,out _)&&!string.IsNullOrWhiteSpace(gallery.Place(p.Id))).Id;
                gallery.Select(personId);var ui=gallery.Overlay;var guide=ui.Guide;var context=HistoricalGuide.Context(gallery,gallery.Tree.People[gallery.SelectedId]);
                Check(context.year==gallery.Year(gallery.SelectedId)&&context.place==gallery.Place(gallery.SelectedId),"Guide uses exact selected year and effective residence");
                Check(context.synthetic==(gallery.TreeId==PanoramaCache.DemoTreeId),"Guide preserves demo/imported provenance");
                Check(context.treeId==gallery.TreeId&&context.personKey==PanoramaCache.PersonKey(personId)&&context.name==gallery.Tree.People[personId].Name,"Guide context belongs to the selected tree and person");
                Check(!ui.GuideOpen&&!guide.Busy&&!guide.Recording,"Guide starts closed and microphone off");
                ui.OpenGuide();Check(ui.GuideOpen,"Guide opens");
                var mic=ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Guide microphone");
                var icon=mic.GetComponentInChildren<RecordingGraphic>();
                var wave=ui.GetComponentsInChildren<RecordingGraphic>(true).Single(r=>r.waveform);
                Check(mic.rect.width==44&&mic.rect.height==44&&icon!=null&&!icon.waveform,"Guide uses a padded mic-icon button");
                Check(!icon.raycastTarget&&!wave.raycastTarget&&!wave.gameObject.activeSelf,"Decorations do not intercept input and idle wave is hidden");
                Check(!ui.GetComponentsInChildren<Text>(true).Any(t=>t.text=="RECORD QUESTION"||t.text=="SEND QUESTION"),"Old record/send text button is replaced");
                typeof(HistoricalGuide).GetProperty("Recording").SetValue(guide,true);typeof(HistoricalGuide).GetProperty("Busy").SetValue(guide,true);
                typeof(SceneOverlay).GetMethod("RefreshGuide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
                Check(wave.gameObject.activeSelf&&mic.GetComponent<Image>().color.r>.2f,"Recording shows the listening wave and recording tint without microphone capture");
                Canvas.ForceUpdateCanvases();var mesh=wave.canvasRenderer.GetMesh();
                Check(mesh!=null&&mesh.vertexCount>0,"Listening wave creates visible UI geometry");
                typeof(HistoricalGuide).GetProperty("Recording").SetValue(guide,false);
                typeof(SceneOverlay).GetMethod("RefreshGuide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
                Check(!wave.gameObject.activeSelf&&icon.color==Color.gray,"Transcribing/researching hides the wave and dims the mic");
                var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                int token=(int)typeof(HistoricalGuide).GetField("version",hidden).GetValue(guide);
                guide.ToggleRecording();guide.ToggleRecording();
                Check(!guide.CanRecord&&guide.Busy&&(int)typeof(HistoricalGuide).GetField("version",hidden).GetValue(guide)==token,"Processing ignores repeated guide-record clicks");
                var hits=(System.Collections.IEnumerable)typeof(SceneOverlay).GetField("hits",hidden).GetValue(ui);
                var micHit=hits.Cast<object>().Single(h=>(RectTransform)h.GetType().GetField("rect").GetValue(h)==mic);
                Check(!((Func<bool>)micHit.GetType().GetField("enabled").GetValue(micHit))(),"Guide microphone pointer activation disabled during processing");
                typeof(SceneOverlay).GetMethod("ConfigureForMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{true});
                Check(mic.rect.width>=44&&mic.rect.height>=44,"Quest retains the full mic hit area");
                typeof(SceneOverlay).GetMethod("ConfigureForMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{false});
                Check(mic.rect.width==44&&mic.rect.height==44,"Desktop mic sizing restores after VR layout");
                typeof(HistoricalGuide).GetProperty("Busy").SetValue(guide,false);
                typeof(SceneOverlay).GetMethod("RefreshGuide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
                Check(icon.color==Color.white&&!wave.gameObject.activeSelf,"Ready state restores the white mic with no wave");
                Check(ui.HandleInput(new Ray(Vector3.one*1000,Vector3.up),true,true,true),"Modal blocks outside scene input");
                var answer=new GuideAnswer{requestId=context.requestId,treeId=context.treeId,personKey=context.personKey,year=context.year,place=context.place,question="What was daily life like?",answer=text,
                    sources=new[]{new GuideSource{title="Museum archive",url="https://example.org/archive"}}};
                Check(HistoricalGuide.Matches(answer,context),"Matching sourced response accepted");
                answer.year++;Check(!HistoricalGuide.Matches(answer,context),"Wrong timeline response rejected");answer.year--;
                answer.personKey="wrong";Check(!HistoricalGuide.Matches(answer,context),"Wrong ancestor rejected");answer.personKey=context.personKey;
                answer.sources[0].url="file:///tmp/test";Check(!HistoricalGuide.Matches(answer,context),"Unsafe server citation rejected");answer.sources[0].url="https://example.org/archive";
                Preview(ui,answer);Canvas.ForceUpdateCanvases();
                Check(ui.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("Historical context with citations")),"Answer visible");
                var mode=ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="SOURCES");
                var ray=new Ray(mode.position-mode.forward*10,mode.forward);ui.HandleInput(ray,true,true,false);
                Check(!mode.gameObject.activeInHierarchy,"Sources button remains hidden");
                Check(!ui.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("Museum archive")),"Hidden source control cannot open sources");
                var input=ui.GetComponentInChildren<InputField>(true);
                Check(input!=null&&input.text==answer.question,"Native question input retains the question");
                Check(input.GetComponent<RectTransform>().rect.width>500&&input.GetComponent<RectTransform>().anchoredPosition.y==mic.anchoredPosition.y,"Wide question field and microphone share the top row");
                Check(!ui.InlineCitationsVisible,"Citation numbers start hidden");
                Check(ui.transform.Cast<Transform>().All(t=>t.name=="Historical guide"||!t.gameObject.activeSelf),"Only Ask panel remains visible");
                ui.CloseGuide();Check(!ui.GuideOpen&&!guide.Recording&&!guide.Busy,"Closing releases microphone and work");
                Check(gallery.SelectedId==personId,"Close stays inside ancestor scene");
                Check(ui.transform.Cast<Transform>().Any(t=>t.name=="Edit Location"&&t.gameObject.activeSelf),"Closing Ask restores scene controls");
                ui.ToggleTimeline();Check(ui.TimelineOpen&&!ui.PanelOpen&&ui.transform.Cast<Transform>().Any(t=>t.name=="Edit Location"&&t.gameObject.activeSelf),"Timeline preserves HUD and scene controls");
                ui.ClosePanels();Check(!ui.PanelOpen,"Closing panels restores the scene toolbar");
                ui.OpenGuide();Check(guide.Answer==answer,"Answer retained on reopen within same visit");ui.CloseGuide();
                var result=n+" historical guide checks passed; no microphone capture or paid calls.";
                File.WriteAllText("Artifacts/guide-checks.txt",result);return result;
            }
            finally{if(old==null)gallery.ShowTree();else gallery.Select(old);if(tutorialOpen)gallery.Tutorial.Open();}
        }
        static void Preview(SceneOverlay ui,GuideAnswer answer)
        {
            typeof(HistoricalGuide).GetProperty("Answer").SetValue(ui.Guide,answer);
            typeof(SceneOverlay).GetMethod("RefreshGuide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
        }
        public static string ShowLiveAnswer()
        {
            var gallery=UnityEngine.Object.FindFirstObjectByType<AncestorGallery>();gallery.Tutorial.Dismiss();gallery.Select("@I1@");
            var answer=JsonUtility.FromJson<GuideAnswer>(File.ReadAllText("Artifacts/guide-live-answer.json"));
            gallery.SetYear("@I1@",answer.year);gallery.Overlay.OpenGuide();Preview(gallery.Overlay,answer);return "Live synthetic answer preview shown";
        }
    }
}
