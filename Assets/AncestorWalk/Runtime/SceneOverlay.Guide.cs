using InputField = TMPro.TMP_InputField;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

namespace AncestorWalk
{
    public sealed partial class SceneOverlay
    {
        RectTransform guidePanel;InputField guideInput;readonly Dictionary<GameObject,bool> guideHidden=new();
        HistoricalGuide guide;
        Text guideHeading,guideQuestion,guideBody,guideStatus,guideRecord,guidePage,guideMode,guideProcessing;
        Image guideMicrophoneBackground;RecordingGraphic guideMicrophone,guideWave;
        readonly List<string> guidePages=new();
        readonly Text[] guideSources=new Text[3];
        int guidePageIndex;bool guideShowSources;
        public const string InlineCitationsKey="AncestorWalk.GuideInlineCitations";
        static readonly Regex CitationMarker=new(@"\s+\[([1-9][0-9]*)\]");
        bool guideInlineCitations;Text citationIcon;GameObject citationSlash;
        public bool InlineCitationsVisible=>guideInlineCitations;
        GuideAnswer displayedAnswer;
        public bool GuideOpen=>guidePanel!=null&&UIFade.Visible(guidePanel.gameObject);
        public HistoricalGuide Guide=>guide;
        void BuildGuide()
        {
            if(GetComponent<GraphicRaycaster>()==null)gameObject.AddComponent<GraphicRaycaster>();
            guide=gameObject.AddComponent<HistoricalGuide>();guide.Initialize(gallery,person);
            Button(root,"ASK",new Vector2(-317,-238),new Vector2(76,40),OpenGuide);
            guidePanel=Panel("Historical guide",new Vector2(0,15),new Vector2(820,410));
            guidePanel.GetComponent<Image>().color=new Color(.025f,.065f,.08f,.97f);
            guideHeading=Label(guidePanel,"",new Vector2(-50,174),new Vector2(620,28),21);
            guideInlineCitations=false;
            citationIcon=Button(guidePanel,"Toggle inline citations",new Vector2(320,174),new Vector2(44,36),ToggleGuideCitations);citationIcon.text="#";citationIcon.fontSize=24;
            var slash=Rect("Hidden citations slash",citationIcon.transform.parent,Vector2.zero,new Vector2(26,2));slash.localRotation=Quaternion.Euler(0,0,-45);Image(slash,Color.white);citationSlash=slash.gameObject;
            Button(guidePanel,"×",new Vector2(376,174),new Vector2(40,36),CloseGuide);
            var questionField=Rect("Ask question input",guidePanel,new Vector2(-30,120),new Vector2(566,44));Image(questionField,new Color(.07f,.14f,.18f,1));
            guideQuestion=Label(questionField,"",Vector2.zero,new Vector2(546,40),16);guideQuestion.alignment=TextAnchor.MiddleLeft;
            guideInput=NativeTextInput.Bind(guideQuestion,null,2000,host:questionField);NativeTextInput.Placeholder(guideInput,"Ask about this time and place…");
            hits.Add(new Hit{rect=questionField,action=()=>NativeTextInput.Focus(guideInput)});
            Button(guidePanel,"Send question",new Vector2(320,120),new Vector2(100,44),()=>guide.AskText(guideInput.text)).text="Ask";
            guideBody=Label(guidePanel,"",new Vector2(0,10),new Vector2(750,174),19);guideBody.alignment=TextAnchor.UpperLeft;
            guideBody.supportRichText=false;guideQuestion.supportRichText=false;guideHeading.supportRichText=false;
            for(int i=0;i<guideSources.Length;i++)
            {
                int slot=i;
                var label=Button(guidePanel,"",new Vector2(0,66-i*58),new Vector2(752,52),()=>OpenGuideSource(slot));
                label.fontSize=16;label.supportRichText=false;guideSources[i]=label;
            }
            guideStatus=Label(guidePanel,"",new Vector2(0,-102),new Vector2(752,42),14);guideStatus.supportRichText=false;
            guideProcessing=Label(guidePanel,"",new Vector2(368,-102),new Vector2(32,42),18);
            guideProcessing.alignment=TextAnchor.MiddleLeft;guideProcessing.raycastTarget=false;guideProcessing.gameObject.SetActive(false);
            Button(guidePanel,"‹",new Vector2(-350,-153),new Vector2(52,36),()=>{guidePageIndex=Math.Max(0,guidePageIndex-1);RefreshGuide();});
            guidePage=Label(guidePanel,"",new Vector2(-270,-153),new Vector2(100,36),14);
            Button(guidePanel,"›",new Vector2(-190,-153),new Vector2(52,36),()=>{guidePageIndex=Math.Min(GuidePageCount-1,guidePageIndex+1);RefreshGuide();});
            guideRecord=Button(guidePanel,"Guide microphone",new Vector2(-350,120),new Vector2(44,44),()=>guide.ToggleRecording());guideRecord.text="";hits[hits.Count-1].enabled=()=>guide.CanRecord;
            var mic=(RectTransform)guideRecord.transform.parent;guideMicrophoneBackground=mic.GetComponent<Image>();
            guideMicrophone=Rect("Microphone icon",mic,Vector2.zero,new Vector2(32,32)).gameObject.AddComponent<RecordingGraphic>();guideMicrophone.raycastTarget=false;guideMicrophone.color=Color.white;
            guideWave=Rect("Listening waveform",guidePanel,new Vector2(0,88),new Vector2(140,18)).gameObject.AddComponent<RecordingGraphic>();guideWave.waveform=true;guideWave.raycastTarget=false;guideWave.color=Color.white;guideWave.gameObject.SetActive(false);
            gallery.Hud?.Register(mic,"Record question · pause 1.5s to send · tap again to finish sooner");
            guideMode=Button(guidePanel,"SOURCES",new Vector2(264,-153),new Vector2(200,38),()=>{guideShowSources=!guideShowSources;guidePageIndex=0;RefreshGuide();});
            Label(guidePanel,"Tap mic, speak, then pause 1.5s to send · Tap again to finish sooner · 30s max · Close cancels",new Vector2(0,-187),new Vector2(790,22),12);
            guide.Changed+=RefreshGuide;guideMode.transform.parent.gameObject.SetActive(false);citationIcon.transform.parent.gameObject.SetActive(false);guidePanel.gameObject.SetActive(false);
        }
        public void OpenGuide(){RestorePanelUI();infoOpen=false;timelineOpen=false;UIFade.Set(guidePanel.gameObject,true);guidePanel.SetAsLastSibling();Apply(false);RefreshGuide();}
        public void CloseGuide(){guide.Cancel();guideInput.DeactivateInputField();UIFade.Set(guidePanel.gameObject,false);Apply(false);}
        public void ClosePanels(){RestorePanelUI();if(GuideOpen){guide.Cancel();guideInput.DeactivateInputField();UIFade.Set(guidePanel.gameObject,false);}infoOpen=false;timelineOpen=false;Apply(false);}
        public bool PanelOpen=>GuideOpen||infoOpen;
        void RestorePanelUI(){foreach(var saved in guideHidden)if(saved.Key!=null)UIFade.Set(saved.Key,saved.Value);guideHidden.Clear();}
        void IsolatePanel()
        {
            RestorePanelUI();
            if(!gallery.HudVisible||dragging)
            {
                foreach(Transform child in root)if(child!=timeline&&(dragging||child!=timelineToggle)){guideHidden[child.gameObject]=UIFade.Visible(child.gameObject);UIFade.Set(child.gameObject,false);}
                return;
            }
            var chosen=GuideOpen?guidePanel:infoOpen?info:null;if(chosen==null)return;
            foreach(Transform child in root)if(child!=chosen){guideHidden[child.gameObject]=UIFade.Visible(child.gameObject);UIFade.Set(child.gameObject,false);}
        }
        public static string GuideAnswerText(GuideAnswer answer,bool showNumbers)
        {
            if(answer==null)return "";if(showNumbers)return answer.answer;
            int count=answer.sources?.Length??0;
            return CitationMarker.Replace(answer.answer,m=>int.TryParse(m.Groups[1].Value,out int n)&&n<=count?"":m.Value);
        }
        void RebuildGuidePages(){guidePages.Clear();if(guide.Answer!=null)guidePages.AddRange(PaginateGuide(GuideAnswerText(guide.Answer,guideInlineCitations),wasVr?430:550));}
        public void ToggleGuideCitations()
        {
            guideInlineCitations=!guideInlineCitations;PlayerPrefs.SetInt(InlineCitationsKey,guideInlineCitations?1:0);PlayerPrefs.Save();RebuildGuidePages();RefreshGuide();
        }
        void ConfigureGuideComfort(bool vr)
        {
            comfort.Set(guidePanel,vr,size:new Vector2(740,410));
            comfort.Set(guideHeading.rectTransform,vr,new Vector2(-55,174),new Vector2(560,28));
            comfort.Set((RectTransform)citationIcon.transform.parent,vr,new Vector2(278,174),new Vector2(44,44));
            foreach(var hit in hits)if(hit.rect!=guidePanel&&hit.rect.IsChildOf(guidePanel))
            {
                comfort.Original(hit.rect);
                if(hit.rect.name=="×")comfort.Set(hit.rect,vr,new Vector2(334,174),new Vector2(44,44));
                else if(hit.rect.name=="‹")comfort.Set(hit.rect,vr,new Vector2(-306,-153));
                else if(hit.rect.name=="›")comfort.Set(hit.rect,vr,new Vector2(-166,-153));
                else if(hit.rect.name=="SOURCES")comfort.Set(hit.rect,vr,new Vector2(258,-153),new Vector2(190,38));
                else if(hit.rect.name=="Guide microphone")comfort.Set(hit.rect,vr,size:new Vector2(44,44));
                else if(comfort.OriginalSize(hit.rect).x>700)comfort.Set(hit.rect,vr,size:new Vector2(680,comfort.OriginalSize(hit.rect).y));
            }
            comfort.Set((RectTransform)guideInput.transform,vr,new Vector2(-20,120),new Vector2(486,44));
            comfort.Set(guideQuestion.rectTransform,vr,size:new Vector2(466,40));
            comfort.Set((RectTransform)guideInput.placeholder.transform,vr,size:new Vector2(466,40));
            foreach(var h in hits)if(h.rect.name=="Guide microphone")comfort.Set(h.rect,vr,new Vector2(-306,120));else if(h.rect.name=="Send question")comfort.Set(h.rect,vr,new Vector2(280,120),new Vector2(100,44));
            comfort.Set(guidePage.rectTransform,vr,new Vector2(-235,-153),new Vector2(70,36));
            foreach(var label in guidePanel.GetComponentsInChildren<Text>(true))if(comfort.OriginalSize(label.rectTransform).x>700)
                comfort.Set(label.rectTransform,vr,size:new Vector2(680,comfort.OriginalSize(label.rectTransform).y));
            RebuildGuidePages();RefreshGuide();
        }
        int GuidePageCount=>guideShowSources?Math.Max(1,((guide.Answer?.sources.Length??0)+2)/3):Math.Max(1,guidePages.Count);
        public static List<string> PaginateGuide(string text,int maximum=550)
        {
            var pages=new List<string>();
            while(!string.IsNullOrEmpty(text))
            {
                int size=Math.Min(maximum,text.Length);
                if(size<text.Length){int space=text.LastIndexOf(' ',size-1,size);if(space>maximum/2)size=space;}
                pages.Add(text.Substring(0,size).Trim());text=text.Substring(size).TrimStart();
            }
            return pages;
        }
        void RefreshGuide()
        {
            if(guideHeading==null)return;
            guideHeading.text=person.Name+" · "+gallery.Place(person.Id)+" · "+gallery.Year(person.Id);
            var answer=guide.Answer;bool answerChanged=!ReferenceEquals(displayedAnswer,answer);
            if(displayedAnswer!=answer){displayedAnswer=answer;guidePageIndex=0;guideShowSources=false;RebuildGuidePages();}
            citationIcon.color=guideInlineCitations?new Color(.95f,.72f,.3f):Color.white;citationSlash.SetActive(!guideInlineCitations);
            if(answerChanged&&answer!=null&&guideInput.text!=answer.question)guideInput.SetTextWithoutNotify(answer.question);
            guideInput.interactable=!guide.Busy;
            guidePageIndex=Mathf.Clamp(guidePageIndex,0,GuidePageCount-1);
            guideBody.gameObject.SetActive(!guideShowSources);
            guideBody.text=guidePages.Count>0?guidePages[Math.Min(guidePageIndex,guidePages.Count-1)]:
                "Try: What was daily life like here?\n\nWhat work was common? What was changing around this year?";
            guideBody.resizeTextForBestFit=true;guideBody.resizeTextMinSize=16;guideBody.resizeTextMaxSize=19;
            for(int i=0;i<guideSources.Length;i++)
            {
                int index=guidePageIndex*3+i;bool visible=guideShowSources&&answer!=null&&index<answer.sources.Length;
                guideSources[i].transform.parent.gameObject.SetActive(visible);
                if(visible){var source=answer.sources[index];guideSources[i].text="["+(index+1)+"] "+source.title+"\n"+new Uri(source.url).Host;}
            }
            guideStatus.text=guide.Status;
            RefreshGuideProcessing();
            guideMicrophone.color=guide.Busy&&!guide.Recording?Color.gray:Color.white;
            guideMicrophoneBackground.color=guide.Recording?new Color(.30f,.13f,.16f):guide.Busy?new Color(.12f,.15f,.16f):new Color(.08f,.25f,.28f);
            guideWave.gameObject.SetActive(GuideOpen&&guide.Recording);
            guidePage.text=(guidePageIndex+1)+" / "+GuidePageCount;guideMode.text=guideShowSources?"ANSWER":"SOURCES ("+(answer?.sources.Length??0)+")";
        }
        void RefreshGuideProcessing()
        {
            if(guideProcessing==null)return;
            bool processing=GuideOpen&&guide.Busy&&!guide.Recording;
            guideProcessing.gameObject.SetActive(processing);
            float width=wasVr?680:752;
            guideStatus.rectTransform.sizeDelta=new Vector2(processing?width-48:width,42);
            if(!processing)return;
            // Animate independently of network progress, including the final response wait.
            guideProcessing.text=new string('.',1+(int)(Time.unscaledTime*3)%3);
            float right=Mathf.Min(guideStatus.preferredWidth,guideStatus.rectTransform.sizeDelta.x)*.5f;
            guideProcessing.rectTransform.anchoredPosition=new Vector2(right+24,-102);
        }
        void OpenGuideSource(int slot)
        {
            int index=guidePageIndex*3+slot;var sources=guide.Answer?.sources;
            if(!guideShowSources||sources==null||index>=sources.Length)return;
            string url=sources[index].url;if(HistoricalGuide.SafeUrl(url))Application.OpenURL(url);
        }
    }
}
