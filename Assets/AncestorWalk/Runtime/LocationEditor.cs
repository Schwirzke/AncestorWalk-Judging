using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace AncestorWalk
{
    public sealed class LocationKey : MonoBehaviour {public string value;}
    public sealed class LocationEditor : MonoBehaviour
    {
        GameObject panel,selection;AncestorGallery gallery;Person person;int year,page;string value="";bool selectAll;TextMesh field,message;Keyboard keyboard;
        TMPro.TMP_InputField nativeInput;LocationVoiceInput voice;Renderer microphoneButton,detailsMicrophoneButton;
        TMPro.TMP_InputField detailsInput,activeInput;string details="";bool voiceForDetails;
        Material keyMaterial,actionMaterial,removeMaterial;
        Transform entryFrame;LineRenderer listeningWave;readonly Vector3[] wavePoints=new Vector3[33];
        bool includePhoto;PhotoRecord scenePhoto;GameObject photoCheck;TextMesh photoLabel;
        Collider[] panelColliders=Array.Empty<Collider>();
        public bool IsOpen=>panel!=null;
        internal void SetSetupSuspended(bool suspended)
        {
            if(panel==null)return;
            if(suspended){nativeInput?.DeactivateInputField();detailsInput?.DeactivateInputField();}
            panel.SetActive(!suspended);
        }
        public void Open(AncestorGallery owner,Person p,int selected)
        {
            Close();includePhoto=false;scenePhoto=null;gallery=owner;person=p;year=selected;page=0;value=gallery.Locations(p.Id).At(p,year);selectAll=value.Length>0;gallery.Overlay?.CloseGuide();
            details=SceneYearDetails.Load(PanoramaCache.StorageRoot,owner.TreeId,p.Id,year);
            var remembered=ScenePhotoChoices.Load(PanoramaCache.StorageRoot,owner.TreeId,p.Id,year,value);
            if(remembered!=null){includePhoto=true;scenePhoto=owner.Document.Photos.FirstOrDefault(photo=>photo.Id==remembered.sha256)??new PhotoRecord{Id=remembered.sha256,File=remembered.file,Title="Selected photo"};}
            if(keyMaterial==null)
            {
                keyMaterial=new Material(gallery.teal){name="Location charcoal buttons",color=new Color(.12f,.16f,.20f)};
                actionMaterial=new Material(gallery.teal){name="Location dark teal actions",color=new Color(.08f,.25f,.28f)};
                removeMaterial=new Material(gallery.teal){name="Location muted remove action",color=new Color(.30f,.13f,.16f)};
            }
            if(voice==null){voice=gameObject.AddComponent<LocationVoiceInput>();voice.Changed+=VoiceChanged;voice.Recognized+=Recognized;}
            gallery.SetLocationModal(true);Build();panel.AddComponent<WorldUIFade>();
            keyboard=Keyboard.current;if(keyboard!=null)keyboard.onTextInput-=Type;
        }
        void Type(char c){}
        TMPro.TMP_InputField EditingField()=>detailsInput!=null&&detailsInput.isFocused?detailsInput:nativeInput!=null&&nativeInput.isFocused?nativeInput:activeInput;
        void Append(string text){StopVoice();NativeTextInput.Insert(EditingField(),text);}
        void Refresh(){if(nativeInput!=null&&nativeInput.text!=value)nativeInput.SetTextWithoutNotify(value);if(detailsInput!=null&&detailsInput.text!=details)detailsInput.SetTextWithoutNotify(details);}
        public void Close(){StopVoice();if(gallery!=null)gallery.SetLocationModal(false);if(keyboard!=null)keyboard.onTextInput-=Type;keyboard=null;if(panel!=null){if(nativeInput!=null)nativeInput.DeactivateInputField();if(detailsInput!=null)detailsInput.DeactivateInputField();var fade=panel.GetComponent<WorldUIFade>();if(fade!=null)fade.Close();else Destroy(panel);panel=null;nativeInput=null;detailsInput=null;activeInput=null;}}
        void OnDisable()=>Close();
        void OnDestroy(){if(keyMaterial!=null)Destroy(keyMaterial);if(actionMaterial!=null)Destroy(actionMaterial);if(removeMaterial!=null)Destroy(removeMaterial);}
        void OnApplicationPause(bool paused){if(paused)StopVoice();}
        void Update()
        {
            if(detailsInput!=null&&detailsInput.isFocused)activeInput=detailsInput;else if(nativeInput!=null&&nativeInput.isFocused)activeInput=nativeInput;
            if(listeningWave==null)return;bool recording=IsOpen&&(voice?.Recording??false);
            listeningWave.gameObject.SetActive(recording);if(!recording)return;
            float time=Time.unscaledTime;
            for(int i=0;i<wavePoints.Length;i++)
            {
                float u=(float)i/(wavePoints.Length-1),envelope=Mathf.Sin(u*Mathf.PI);
                wavePoints[i]=new Vector3((u-.5f)*1.35f,Mathf.Sin(u*Mathf.PI*10-time*8)*(.014f+.012f*Mathf.Sin(time*2.3f))*envelope,0);
            }
            listeningWave.SetPositions(wavePoints);
        }
        void StopVoice(){bool pending=voice?.Busy??false;voice?.Cancel();if(IsOpen){RefreshVoiceButtons();if(pending&&message!=null)message.text="Voice stopped. Type or record again.";}}
        void RefreshVoiceButtons()
        {
            bool busy=voice?.Busy==true,recording=voice?.Recording==true;
            void Refresh(Renderer button,bool forDetails)
            {
                if(button==null)return;bool active=forDetails==voiceForDetails;
                button.sharedMaterial=recording&&active?removeMaterial:busy?keyMaterial:actionMaterial;
                button.GetComponent<Collider>().enabled=!busy||recording&&active;
            }
            Refresh(microphoneButton,false);Refresh(detailsMicrophoneButton,true);
        }
        void VoiceChanged(){if(!IsOpen)return;RefreshVoiceButtons();if(message!=null)message.text=(voiceForDetails?"Scene details: ":"Location: ")+voice.Status;}
        void Listen(bool forDetails)
        {
            if(voice.Recording){if(voiceForDetails==forDetails)voice.FinishRecording();return;}
            if(voice.Busy)return;
            voiceForDetails=forDetails;
            listeningWave.transform.localPosition=new Vector3(.24f,forDetails ? .35f : 1.08f,-.11f);
            StopVoice();voice.StartListening("place");
        }
        void Recognized(string purpose,string text)
        {
            if(!IsOpen||purpose!="place")return;
            try{if(voiceForDetails)details=LocationVoiceInput.Dictation(text);else value=LocationVoiceInput.Place(text);}catch(Exception e){message.text=e.Message;return;}
            selectAll=true;Refresh();activeInput=voiceForDetails?detailsInput:nativeInput;NativeTextInput.Focus(activeInput,true);message.text="Review the "+(voiceForDetails?"scene details":"location")+", then click Generate.";
        }
        void Build()
        {
            if(panel!=null){panel.SetActive(false);Destroy(panel);}
            panel=new GameObject("Location editor");panel.transform.SetParent(transform,false);
            var camera=Camera.main.transform;panel.transform.position=camera.position+camera.forward*2.5f;panel.transform.rotation=Quaternion.LookRotation(panel.transform.position-camera.position,Vector3.up);panel.transform.localScale=Vector3.one*.7f;
            Box("Location editor backdrop",new Vector3(0,0,.12f),new Vector3(3.8f,4.25f,.08f),gallery.ink);
            Text(person.Name+" · LOCATION FROM "+year,new Vector3(0,1.82f,0),.048f);
            var mic=Box("Location microphone",new Vector3(-1.49f,1.30f,0),new Vector3(.34f,.36f,.06f),actionMaterial);mic.AddComponent<LocationKey>().value="voice-place";microphoneButton=mic.GetComponent<Renderer>();
            MicrophoneIcon(new Vector3(-1.49f,1.30f,-.075f));
            var detailsMic=Box("Scene details microphone",new Vector3(-1.49f,.66f,0),new Vector3(.34f,.36f,.06f),actionMaterial);detailsMic.AddComponent<LocationKey>().value="voice-details";detailsMicrophoneButton=detailsMic.GetComponent<Renderer>();
            MicrophoneIcon(new Vector3(-1.49f,.66f,-.075f));
            var entry=Box("Location text field",new Vector3(.24f,1.30f,.03f),new Vector3(2.95f,.34f,.04f),keyMaterial);entry.AddComponent<LocationKey>().value="focus";entryFrame=entry.transform;
            var wave=new GameObject("Listening waveform");wave.transform.SetParent(panel.transform,false);wave.transform.localPosition=new Vector3(.24f,.35f,-.11f);
            listeningWave=wave.AddComponent<LineRenderer>();listeningWave.useWorldSpace=false;listeningWave.sharedMaterial=gallery.paper;listeningWave.startWidth=listeningWave.endWidth=.009f;listeningWave.positionCount=wavePoints.Length;wave.SetActive(false);
            Text("Location",new Vector3(0,1.59f,0),.028f);
            Text("Scene details (optional)",new Vector3(0,.99f,0),.028f);
            Text("Example: Place the man from the photo in the parking lot\nbeside a parked car. Dress him in casual golf clothing.",new Vector3(0,.27f,0),.021f);
            nativeInput=MakeInput("Location",new Vector2(.24f,1.30f),new Vector2(590,68),false,v=>{value=v;StopVoice();});
            detailsInput=MakeInput("Scene details",new Vector2(.24f,.66f),new Vector2(590,100),true,v=>{details=v;StopVoice();});
            nativeInput.SetTextWithoutNotify(value);detailsInput.SetTextWithoutNotify(details);activeInput=nativeInput;NativeTextInput.Focus(nativeInput,true);
            Box("Scene details text field",new Vector3(.24f,.66f,.03f),new Vector3(2.95f,.50f,.04f),keyMaterial).AddComponent<LocationKey>().value="focus-details";
            string[] rows={"QWERTYUIOP","ASDFGHJKL","ZXCVBNM,.-","0123456789"};
            for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++)Button(rows[row][col].ToString(),rows[row][col].ToString(),new Vector3((col-(rows[row].Length-1)*.5f)*.32f,.10f-row*.26f,0),.28f);
            Button("SPACE","space",new Vector3(-1.26f,-.96f,0),.85f);Button("BACKSPACE","back",new Vector3(-.24f,-.96f,0),1.05f);Button("CLEAR","clear",new Vector3(.71f,-.96f,0),.65f);Button("NEW LINE","newline",new Vector3(1.43f,-.96f,0),.65f);
            Box("Include photo checkbox",new Vector3(-1.55f,-1.25f,0),new Vector3(.20f,.20f,.06f),keyMaterial).AddComponent<LocationKey>().value="photo-toggle";
            photoCheck=Box("Photo checkbox checked",new Vector3(-1.55f,-1.25f,-.045f),new Vector3(.12f,.12f,.012f),actionMaterial);Destroy(photoCheck.GetComponent<Collider>());
            photoLabel=Text("Add photo to scene",new Vector3(-.58f,-1.25f,0),.029f);
            Button("CHOOSE PHOTO","photo-pick",new Vector3(1.02f,-1.25f,0),1.32f);
            Button("GENERATE","save",new Vector3(-1.35f,-1.55f,0),.8f);Button("REMOVE ENTRY","remove",new Vector3(-.25f,-1.55f,0),1.25f);Button("CANCEL","cancel",new Vector3(1.05f,-1.55f,0),1f);
            message=Text("Mic: speak, then pause 1.5s to send · Tap again to finish sooner\n1,000 characters per field · Ctrl/Cmd+Enter: Generate · Esc: Cancel",new Vector3(0,-1.88f,0),.027f);RefreshVoiceButtons();RefreshPhoto();
            panelColliders=panel.GetComponentsInChildren<Collider>();Physics.SyncTransforms();
            // Saved-entry browsing is temporarily hidden; entries remain available on the timeline.
        }
        TMPro.TMP_InputField MakeInput(string name,Vector2 at,Vector2 size,bool multiline,Action<string> changed)
        {
            var go=new GameObject(name+" native input",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(panel.transform,false);go.transform.localPosition=new Vector3(at.x,at.y,-.06f);go.transform.localScale=Vector3.one*.005f;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Camera.main;canvas.sortingOrder=200;
            var rect=(RectTransform)go.transform;rect.sizeDelta=size;
            var label=new GameObject(name+" text",typeof(RectTransform),typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();label.transform.SetParent(rect,false);label.rectTransform.sizeDelta=size;label.font=gallery.font;label.fontSize=22;label.color=Color.white;
            var input=NativeTextInput.Bind(label,changed,multiline?SceneYearDetails.MaximumLength:ResidenceTimeline.MaximumLocationLength);
            input.lineType=multiline?TMPro.TMP_InputField.LineType.MultiLineNewline:TMPro.TMP_InputField.LineType.SingleLine;input.textComponent.alignment=multiline?TMPro.TextAlignmentOptions.TopLeft:TMPro.TextAlignmentOptions.MidlineLeft;input.textComponent.textWrappingMode=multiline?TMPro.TextWrappingModes.Normal:TMPro.TextWrappingModes.NoWrap;return input;
        }
        public void Activate(string key)
        {
            if(key=="photo-toggle"){includePhoto=!includePhoto;RefreshPhoto();if(includePhoto&&scenePhoto==null)PickPhoto();return;}
            if(key=="photo-pick"){PickPhoto();return;}
            if(key=="voice-place"||key=="voice-details"){Listen(key=="voice-details");return;}
            if(key=="focus"||key=="focus-details"){activeInput=key=="focus"?nativeInput:detailsInput;NativeTextInput.Focus(activeInput);return;}
            StopVoice();
            if(key=="cancel"){Close();return;}
            if(key=="save"||key=="remove")
            {
                try
                {
                    // Request setup before saving or closing the user's unfinished location draft.
                    if(key=="save")DevelopmentAI.Resolve();
                    if(key=="save"&&includePhoto&&scenePhoto==null){message.text="Choose a photo or uncheck Add photo to scene.";return;}
                    details=SceneYearDetails.Normalize(details);
                    var photo=key=="save"&&includePhoto?SceneReference.FromPhoto(scenePhoto,year,value):null;
                    SceneYearDetails.Save(PanoramaCache.StorageRoot,gallery.TreeId,person.Id,year,key=="remove"?"":details);
                    gallery.SaveLocation(person.Id,year,value,key=="remove");
                    gallery.SetYear(person.Id,year);
                    ScenePhotoChoices.Save(PanoramaCache.StorageRoot,gallery.TreeId,person.Id,year,photo);
                    gallery.Overlay?.LocationSaved(year,key=="remove",key=="save");
                    Close();
                    if(key=="save"){if(photo==null)gallery.GenerateScene(person.Id);else gallery.Generation.Generate(gallery.TreeId,person,gallery.TreeId==PanoramaCache.DemoTreeId,year,gallery.Place(person.Id),photo);}
                }
                catch(Exception e){message.text=e is System.IO.IOException?"Could not save location. Try again.":e.Message;}return;
            }
            var entries=gallery.Locations(person.Id).entries.OrderBy(e=>e.year).ToArray();
            if((key=="next"||key=="previous")&&entries.Length>0){page=(page+(key=="next"?1:entries.Length-1))%entries.Length;Build();return;}
            if(key=="entry"&&entries.Length>0){var e=entries[page%entries.Length];year=e.year;value=e.place;selectAll=value.Length>0;Build();return;}
            if(key=="clear"){var input=EditingField();if(input!=null)input.text="";}else if(key=="back")NativeTextInput.Delete(EditingField());else Append(key=="space"?" ":key=="newline"?"\n":key);
        }
        void RefreshPhoto()
        {
            if(photoCheck!=null)photoCheck.SetActive(includePhoto);
            if(photoLabel!=null){string title=scenePhoto?.Title;photoLabel.text=includePhoto&&scenePhoto!=null?"Photo: "+(title?.Length>24?title.Substring(0,23)+"…":title):"Add photo to scene";}
        }
        void PickPhoto()
        {
            StopVoice();nativeInput?.DeactivateInputField();detailsInput?.DeactivateInputField();panel.SetActive(false);
            void Restore(){if(panel==null)return;panel.SetActive(true);gallery.SetLocationModal(true);RefreshPhoto();}
            try{gallery.Library.PickScenePhoto(person.Id,photo=>{if(panel==null)return;scenePhoto=photo;includePhoto=true;Restore();},()=>{if(scenePhoto==null)includePhoto=false;Restore();});}
            catch(Exception e){Restore();message.text=e.Message;}
        }
        public void HandleInput(bool vr,Transform hand,Vector3 position,Quaternion rotation,bool select,bool back,LineRenderer pointer)
        {
            var keys=Keyboard.current;if(back||(keys?.escapeKey.wasPressedThisFrame??false)){Close();return;}
            if((keys?.enterKey.wasPressedThisFrame??false)&&(keys.leftCtrlKey.isPressed||keys.rightCtrlKey.isPressed||keys.leftMetaKey.isPressed||keys.rightMetaKey.isPressed)){Activate("save");return;}
            Ray ray;if(vr){hand.localPosition=position;hand.localRotation=rotation;ray=new Ray(hand.position,hand.forward);}else {var mouse=Mouse.current;ray=mouse!=null?Camera.main.ScreenPointToRay(mouse.position.ReadValue()):new Ray(Camera.main.transform.position,Camera.main.transform.forward);select=mouse?.leftButton.wasPressedThisFrame??false;}
            bool hit=RaycastPanel(ray,out var info);pointer.enabled=vr;pointer.SetPosition(0,ray.origin);pointer.SetPosition(1,hit?info.point:ray.GetPoint(5));
            if(select&&hit){var key=info.collider.GetComponent<LocationKey>();if(key!=null){if(key.value=="focus"||key.value=="focus-details"){activeInput=key.value=="focus"?nativeInput:detailsInput;if(vr)NativeTextInput.Point(activeInput,ray);}else Activate(key.value);}}
        }
        bool RaycastPanel(Ray ray,out RaycastHit hit)
        {
            hit=default;if(panel==null||!panel.activeInHierarchy)return false;float closest=15;bool found=false;
            foreach(var target in panelColliders)
                if(target!=null&&target.enabled&&target.gameObject.activeInHierarchy&&target.Raycast(ray,out var candidate,closest))
                {hit=candidate;closest=candidate.distance;found=true;}
            return found;
        }
        GameObject Box(string name,Vector3 pos,Vector3 size,Material mat)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(panel.transform,false);o.transform.localPosition=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=mat;return o;}
        void MicrophoneIcon(Vector3 at)
        {
            var icon=new GameObject("Microphone icon");icon.transform.SetParent(panel.transform,false);icon.transform.localPosition=at;
            void Stroke(string name,Vector3[] points,bool loop=false)
            {
                var go=new GameObject(name);go.transform.SetParent(icon.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=gallery.paper;line.useWorldSpace=false;line.startWidth=line.endWidth=.012f;line.loop=loop;line.positionCount=points.Length;line.SetPositions(points);
            }
            var capsule=new Vector3[24];for(int i=0;i<capsule.Length;i++){float angle=i*Mathf.PI*2/capsule.Length;capsule[i]=new Vector3(Mathf.Cos(angle)*.042f,.035f+Mathf.Sin(angle)*.075f,0);}Stroke("Microphone capsule",capsule,true);
            var cradle=new Vector3[17];for(int i=0;i<cradle.Length;i++){float angle=Mathf.PI+i*Mathf.PI/(cradle.Length-1);cradle[i]=new Vector3(Mathf.Cos(angle)*.079f,.025f+Mathf.Sin(angle)*.09f,0);}Stroke("Microphone cradle",cradle);
            Stroke("Microphone stem",new[]{new Vector3(0,-.065f,0),new Vector3(0,-.115f,0)});Stroke("Microphone base",new[]{new Vector3(-.045f,-.115f,0),new Vector3(.045f,-.115f,0)});
        }
        TextMesh Button(string label,string key,Vector3 pos,float width)
        {Box(label,pos,new Vector3(width,.24f,.06f),key=="remove"?removeMaterial:key=="save"||key=="voice-place"?actionMaterial:keyMaterial).AddComponent<LocationKey>().value=key;return Text(label,pos+new Vector3(0,0,-.05f),.029f);}
        TextMesh Text(string value,Vector3 pos,float size)
        {var o=new GameObject("Location label");o.transform.SetParent(panel.transform,false);o.transform.localPosition=pos+new Vector3(0,0,-.04f);var t=o.AddComponent<TextMesh>();t.font=gallery.font;t.fontSize=64;t.characterSize=size*.4f;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.color=gallery.paper.color;t.richText=false;t.text=value;o.GetComponent<MeshRenderer>().sharedMaterial=gallery.font.material;return t;}
    }
}
