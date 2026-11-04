using InputField = TMPro.TMP_InputField;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AncestorWalk.Editor
{
    public static class TreeEditingPolishChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/tree-editing-polish-play-checks.txt";
        const string Ged="0 HEAD\n0 @M@ INDI\n1 NAME Father\n1 SEX M\n0 @F@ INDI\n1 NAME Mother\n1 SEX F\n0 @C@ INDI\n1 NAME Child\n1 SEX M\n0 @S@ INDI\n1 NAME Sibling\n1 SEX F\n0 @U@ INDI\n1 NAME Unspecified\n0 @X@ INDI\n1 NAME Other\n1 SEX X\n0 @F1@ FAM\n1 HUSB @M@\n1 WIFE @F@\n1 CHIL @C@\n1 CHIL @S@\n0 TRLR";
        static TreeDocument Fixture()=>TreeDocument.FromTree(GedcomImporter.Parse(Ged),TreeStore.NewId(),"Synthetic editable family");
        static bool On(Vector3 p,Vector3 a,Vector3 b)=>Vector3.Distance(p,a)+Vector3.Distance(p,b)<=Vector3.Distance(a,b)+.0001f;
        public static string TextInput()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            foreach(char arrow in new[]{'\uF700','\uF701','\uF702','\uF703'})
            {
                string value="München 1972";bool selected=true;TextEntry.Insert(ref value,ref selected,arrow.ToString(),200);
                Check(value=="München 1972"&&!selected,"Mac arrow collapses selection without deleting text");
                TextEntry.Insert(ref value,ref selected,"!",200);Check(value=="München 1972!","Typing after navigation appends at the end");
            }
            string text="Family";bool all=true;TextEntry.Insert(ref text,ref all,"\uF704",200);Check(text=="Family"&&all,"Other function-key events cannot replace selected text");
            Check(TextEntryCaret.Blink(.1f)&&!TextEntryCaret.Blink(.7f)&&TextEntryCaret.Blink(1.1f),"Caret has a repeating visible/hidden phase");
            var old=UnityEngine.InputSystem.Keyboard.current;var keys=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            try{foreach(var key in new[]{UnityEngine.InputSystem.Key.LeftArrow,UnityEngine.InputSystem.Key.RightArrow,UnityEngine.InputSystem.Key.UpArrow,UnityEngine.InputSystem.Key.DownArrow})
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keys,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keys,new UnityEngine.InputSystem.LowLevel.KeyboardState(key));UnityEngine.InputSystem.InputSystem.Update();
                text="Family";all=true;Check(TextEntry.Shortcuts(keys,ref text,ref all,200)&&text=="Family"&&!all,"Input System arrow preserves the selected value");
            }}finally{UnityEngine.InputSystem.InputSystem.RemoveDevice(keys);old?.MakeCurrent();}
            return n+" text navigation/caret data checks passed.";
        }
        public static string TextInputPlay()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||g.Generation.Busy)throw new Exception("Idle guarded Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            object Get(object o,string name)=>o.GetType().GetField(name,Hidden).GetValue(o);
            void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Hidden).Invoke(o,args);
            void Editing(InputField input)
            {
                Check(input!=null&&input.textComponent.GetComponent<TextEntryCaret>()==null,"Uses Unity input and caret");
                void Select(int start,int end){input.ForceLabelUpdate();input.textComponent.ForceMeshUpdate();input.stringPosition=start;input.selectionFocusPosition=end;input.selectionStringFocusPosition=end;}
                input.text="York";Select(4,4);
                input.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.LeftArrow});NativeTextInput.Insert(input,"!");
                Check(input.text=="Yor!k","Arrow moves caret for middle insertion");
                Select(1,4);NativeTextInput.Insert(input,"a");Check(input.text=="Yak","Partial selection replaces only selected text");
                NativeTextInput.Delete(input);Check(input.text=="Yk","Backspace deletes before caret");
                input.text="München";Select(7,7);GUIUtility.systemCopyBuffer=" York";input.ProcessEvent(Event.KeyboardEvent(Application.platform==RuntimePlatform.OSXEditor?"%v":"^v"));
                Check(input.text=="München York","Native paste preserves Unicode at caret");
                Select(0,7);
                string modifier=Application.platform==RuntimePlatform.OSXEditor?"%":"^";
                input.ProcessEvent(Event.KeyboardEvent(modifier+"c"));Check(GUIUtility.systemCopyBuffer=="München","Native copy uses partial selection");
                input.ProcessEvent(Event.KeyboardEvent(modifier+"x"));Check(input.text==" York","Native cut removes only selected text");
                input.ProcessEvent(Event.KeyboardEvent(modifier+"v"));Check(input.text=="München York","Native paste restores copied selection");
                Check(input.caretBlinkRate>0&&!input.readOnly,"Built-in blinking caret and editing enabled");
            }
            string clipboard=GUIUtility.systemCopyBuffer;var lib=g.Library;var ai=g.AISetup;var location=g.LocationEditor;g.Tutorial.Dismiss();g.Batch.Close();lib.Close();ai.Close();location.Close();
            try
            {
                lib.Open();Call(lib,"EditText","Name","Example family",new Action<string>(_=>{}));Editing((InputField)Get(lib,"nativeInput"));lib.Close();
                ai.Open();var keyInput=(InputField)Get(ai,"keyInput");Check(keyInput.contentType==InputField.ContentType.Password,"API key uses native password masking");keyInput.text="fixture-not-a-key";keyInput.selectionStringAnchorPosition=0;keyInput.selectionStringFocusPosition=keyInput.text.Length;keyInput.ProcessEvent(Event.KeyboardEvent(Application.platform==RuntimePlatform.OSXEditor?"%c":"^c"));Check(!GUIUtility.systemCopyBuffer.Contains("fixture"),"Password copy does not expose session key");ai.Close();
                var person=g.Tree.People.Values.First(p=>LifeEndpointScenes.TrySceneRange(p,out _,out _));LifeEndpointScenes.TrySceneRange(person,out int year,out _);location.Open(g,person,year);Editing((InputField)Get(location,"nativeInput"));
                return n+" native text editor checks passed; no model calls or saved edits.";
            }
            finally{GUIUtility.systemCopyBuffer=clipboard;lib.Close();ai.Close();location.Close();}
        }
        public static string WordSelectionPlay()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            var runner=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();runner.StartCoroutine(CheckedWordSelection());return "Native word-selection checks started";
        }
        static IEnumerator CheckedWordSelection()
        {
            var run=WordSelection();try{while(true){bool next;try{next=run.MoveNext();}catch(Exception e){File.WriteAllText("Artifacts/native-word-selection-checks.txt","FAILED: "+e);yield break;}if(!next)break;yield return run.Current;}}
            finally{(run as IDisposable)?.Dispose();}
        }
        static IEnumerator WordSelection()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            const string report="Artifacts/native-word-selection-checks.txt";File.WriteAllText(report,"RUNNING");
            var root=new GameObject("Isolated native selection fixture",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1000;
            var label=new GameObject("Input viewport",typeof(RectTransform)).AddComponent<Text>();label.transform.SetParent(root.transform,false);label.rectTransform.sizeDelta=new Vector2(500,80);label.fontSize=24;label.color=Color.white;
            var input=NativeTextInput.Bind(label,null,1000);input.text="München York";NativeTextInput.Focus(input,true);
            try
            {
                yield return null;yield return null;input.ForceLabelUpdate();Canvas.ForceUpdateCanvases();
                Check(input.selectionStringAnchorPosition==0&&input.selectionStringFocusPosition==input.text.Length,"Select all on initial entry");
                input.selectionStringAnchorPosition=input.selectionStringFocusPosition=4;NativeTextInput.Focus(input);yield return null;
                Check(input.selectionStringAnchorPosition==4&&input.selectionStringFocusPosition==4,"Repeated focus preserves caret");
                input.textComponent.ForceMeshUpdate();
                Vector2 Position(int index){var ch=input.textComponent.textInfo.characterInfo[index];return RectTransformUtility.WorldToScreenPoint(null,input.textComponent.transform.TransformPoint((ch.bottomLeft+ch.topRight)*.5f));}
                var hit=new RaycastResult{gameObject=input.gameObject,module=root.GetComponent<GraphicRaycaster>()};
                var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=Position(2),pointerPressRaycast=hit,pointerCurrentRaycast=hit};
                typeof(InputField).GetField("m_PointerDownClickStartTime",Hidden).SetValue(input,-10f);input.OnPointerDown(data);input.OnPointerUp(data);input.OnPointerDown(data);input.OnPointerUp(data);
                int start=Math.Min(input.selectionStringAnchorPosition,input.selectionStringFocusPosition),end=Math.Max(input.selectionStringAnchorPosition,input.selectionStringFocusPosition);
                Check(input.text.Substring(start,end-start)=="München","Native double-click selects Unicode word");
                typeof(InputField).GetField("m_PointerDownClickStartTime",Hidden).SetValue(input,-10f);data.position=Position(0);input.OnPointerDown(data);input.OnBeginDrag(data);data.position=Position(9);input.OnDrag(data);input.OnEndDrag(data);input.OnPointerUp(data);
                Check(Math.Abs(input.selectionStringAnchorPosition-input.selectionStringFocusPosition)>4,"Native pointer drag selects text");
                input.text="First line.\nSecond line!";input.lineType=InputField.LineType.MultiLineNewline;input.textComponent.textWrappingMode=TMPro.TextWrappingModes.Normal;input.ForceLabelUpdate();input.textComponent.ForceMeshUpdate();
                Check(input.textComponent.textInfo.lineCount>=2,"Multiline text retains line breaks");
                input.text=new string('w',1000);input.ForceLabelUpdate();input.textComponent.ForceMeshUpdate();input.stringPosition=input.text.Length;NativeTextInput.Insert(input,"x");Check(input.text.Length==1000,"Character limit preserved");
                var camera=Camera.main;canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
                var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(640,160);rect.SetPositionAndRotation(camera.transform.position+camera.transform.forward*2,camera.transform.rotation);rect.localScale=Vector3.one*.002f;
                input.text="München York";input.lineType=InputField.LineType.SingleLine;input.ForceLabelUpdate();Canvas.ForceUpdateCanvases();input.textComponent.ForceMeshUpdate();
                Ray RayTo(int index){var ch=input.textComponent.textInfo.characterInfo[index];var point=input.textComponent.transform.TransformPoint((ch.bottomLeft+ch.topRight)*.5f);return new Ray(camera.transform.position,(point-camera.transform.position).normalized);}
                typeof(InputField).GetField("m_PointerDownClickStartTime",Hidden).SetValue(input,-10f);NativeTextInput.Point(input,RayTo(2));
                var pointer=(PointerEventData)typeof(NativeTextInput).GetField("rayPointer",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);Check(pointer.pressEventCamera==camera,"Quest pointer uses the world-space canvas camera");
                NativeTextInput.UpdatePointer(RayTo(2),true,false);NativeTextInput.Point(input,RayTo(2));NativeTextInput.UpdatePointer(RayTo(2),true,false);
                start=Math.Min(input.selectionStringAnchorPosition,input.selectionStringFocusPosition);end=Math.Max(input.selectionStringAnchorPosition,input.selectionStringFocusPosition);Check(input.text.Substring(start,end-start)=="München","Quest double-trigger selects the pointed word");
                typeof(InputField).GetField("m_PointerDownClickStartTime",Hidden).SetValue(input,-10f);NativeTextInput.Point(input,RayTo(0));NativeTextInput.UpdatePointer(RayTo(9),true,true);NativeTextInput.UpdatePointer(RayTo(9),true,false);
                Check(Math.Abs(input.selectionStringAnchorPosition-input.selectionStringFocusPosition)>4,"Quest trigger-held pointer drag selects text");
                File.WriteAllText(report,n+" native word-selection/focus/drag/multiline checks passed.");
            }
            finally{UnityEngine.Object.Destroy(root);}
        }
        public static string Data()
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}void Reject(Action a,string why){bool rejected=false;try{a();}catch{rejected=true;}Check(rejected,why);}
            string text="My family";bool selected=true;TextEntry.Insert(ref text,ref selected,"K",200);Check(text=="K"&&!selected,"First key replaces selection");TextEntry.Insert(ref text,ref selected,"elly",200);Check(text=="Kelly","Following keys append");
            selected=true;TextEntry.Delete(ref text,ref selected);Check(text==""&&!selected,"Backspace clears selected contents");text="ab";TextEntry.Delete(ref text,ref selected);Check(text=="a","Unselected backspace removes one character");
            text=new string('a',200);selected=true;TextEntry.Insert(ref text,ref selected,"new",200);Check(text=="new","Full length selections can be replaced");
            selected=true;TextEntry.Insert(ref text,ref selected,"1934, 1936\r\n",500,c=>char.IsDigit(c)||c==','||c==' ');Check(text=="1934, 1936","Paste strips unsupported/control characters");TextEntry.Insert(ref text,ref selected,new string('1',500),500);Check(text.Length==500,"Paste respects field limit");
            var doc=Fixture();doc.SourceGedcom=Ged;Check(doc.People.Single(p=>p.Id=="@M@").Sex=="M"&&doc.People.Single(p=>p.Id=="@F@").Sex=="F","Recorded GEDCOM sex retained");
            Check(doc.People.Single(p=>p.Id=="@U@").Sex==""&&doc.People.Single(p=>p.Id=="@X@").Sex=="X","Unknown/other values remain distinct");
            Check(RelationshipColors.For(doc.People[0],Color.gray)==RelationshipColors.Male&&RelationshipColors.For(doc.People[1],Color.gray)==RelationshipColors.Female,"Blue/pink colors follow recorded sex");
            Check(RelationshipColors.For(doc.People.Single(p=>p.Id=="@U@"),Color.gray)==Color.gray&&RelationshipColors.For(doc.People.Single(p=>p.Id=="@X@"),Color.gray)==Color.gray,"No sex inferred for unknown/other");
            var layout=TreeLayout.Build(doc.ToTree(),new TreeLayoutSettings());Check(layout.Connectors.Count(r=>r.PersonId!=null)==4&&layout.Connectors.Count(r=>r.PersonId==null)==3,"One branch per person and single neutral family trunks");
            var oldSegments=layout.Routes.SelectMany(r=>r.Zip(r.Skip(1),(a,b)=>(a,b))).ToArray();var drawn=layout.Connectors.SelectMany(r=>r.Points.Zip(r.Points.Skip(1),(a,b)=>(a,b))).ToArray();
            foreach(var group in new[]{(oldSegments,drawn),(drawn,oldSegments)})foreach(var segment in group.Item1)for(int i=0;i<=8;i++)
                Check(group.Item2.Any(s=>On(Vector3.Lerp(segment.a,segment.b,i/8f),s.a,s.b)),"Colored routes exactly cover existing orthogonal geometry");
            foreach(var segment in drawn){var d=segment.b-segment.a;Check((Mathf.Abs(d.x)>.001f?1:0)+(Mathf.Abs(d.y)>.001f?1:0)+(Mathf.Abs(d.z)>.001f?1:0)<=1,"Colored routes remain orthogonal");}
            var branches=layout.Connectors.Where(r=>r.PersonId!=null).ToArray();for(int i=0;i<branches.Length;i++)for(int j=i+1;j<branches.Length;j++)
                foreach(var a in branches[i].Points.Zip(branches[i].Points.Skip(1),(a,b)=>(a,b)))foreach(var b in branches[j].Points.Zip(branches[j].Points.Skip(1),(a,b)=>(a,b)))
                    Check((a.a-a.b).sqrMagnitude<.000001f||!On((a.a+a.b)*.5f,b.a,b.b),"Distinct colored branches do not double-draw shared trunks");
            string root=Path.GetFullPath("Artifacts/TreeEditingData/"+Guid.NewGuid().ToString("N"));var store=new TreeStore(root);store.Save(doc);
            string json=Path.Combine(store.DocumentDirectory(doc.Id),"tree.json");var legacy=JObject.Parse(File.ReadAllText(json));foreach(var p in (JArray)legacy["People"])((JObject)p).Remove("Sex");string legacyText=legacy.ToString();File.WriteAllText(json,legacyText);
            var loaded=store.Load(doc.Id);Check(loaded.People.Single(p=>p.Id=="@M@").Sex=="M"&&loaded.People.Single(p=>p.Id=="@F@").Sex=="F","Old documents recover recorded sex from retained GEDCOM");Check(File.ReadAllText(json)==legacyText,"Recovery never rewrites existing private documents");
            loaded.People[0].Sex="";store.Save(loaded);Check(store.Load(doc.Id).People[0].Sex=="","Explicit unknown edit is not overwritten from original source");
            var other=Fixture();store.Save(other);Directory.CreateDirectory(store.MediaRoot);string shared=Path.Combine(store.MediaRoot,new string('f',64)+".png");File.WriteAllText(shared,"shared-photo-fixture");
            string Owned(string kind,string id)=>kind=="Locations"?Path.Combine(root,kind,id):Path.Combine(root,"Panoramas","people",id);
            foreach(var id in new[]{doc.Id,other.Id})foreach(var kind in new[]{"Locations","Panoramas"}){string path=Owned(kind,id);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"fixture"),id);}
            string queue=Path.Combine(root,"scene-generation-queue.json");File.WriteAllText(queue,"retained-request-history");
            Check(store.Delete(doc.Id)=="Tree deleted."&&store.List().Single().Id==other.Id,"Deleting removes only selected saved document");
            Check(!Directory.Exists(Owned("Locations",doc.Id))&&!Directory.Exists(Owned("Panoramas",doc.Id)),"Selected location/panorama caches removed");
            Check(File.ReadAllText(shared)=="shared-photo-fixture"&&File.ReadAllText(queue)=="retained-request-history","Shared photos and request history preserved");
            Check(Directory.Exists(Owned("Locations",other.Id))&&Directory.Exists(Owned("Panoramas",other.Id))&&store.Load(other.Id).People.Count==6,"Other tree and caches unchanged");
            Reject(()=>store.Delete("../outside"),"Deletion rejects escaped paths");Reject(()=>store.Delete(doc.Id),"Stale deletion rejected");Check(store.List().Single().Id==other.Id,"Failed deletion preserves remaining trees");
            var demo=Fixture();demo.IsDemo=true;Directory.CreateDirectory(store.DocumentDirectory(demo.Id));File.WriteAllText(Path.Combine(store.DocumentDirectory(demo.Id),"tree.json"),JsonUtility.ToJson(demo));Reject(()=>store.Delete(demo.Id),"Demo is protected even through storage API");
            string result=n+" text selection, sex/connector and isolated deletion data checks passed.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/tree-editing-polish-data-checks.txt",result);return result;
        }
        static object Get(object o,string name)=>o.GetType().GetField(name,Hidden).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,Hidden).SetValue(o,value);
        static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Hidden).Invoke(o,args);
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy||g.Library.Busy)throw new Exception("Ready, idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Running isolated tree editing UI checks.";
        }
        static IEnumerator Safe(IEnumerator test)
        {
            var stack=new Stack<IEnumerator>();stack.Push(test);while(stack.Count>0)
            {
                var run=stack.Peek();bool more=false;Exception failure=null;try{more=run.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(run.Current is IEnumerator nested){stack.Push(nested);continue;}yield return run.Current;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}
            IEnumerator Until(Func<bool> done,string why){float end=Time.realtimeSinceStartup+20;while(!done()&&Time.realtimeSinceStartup<end)yield return null;Check(done(),why);}
            var lib=g.Library;var original=g.Document.Copy();var originalGen=g.Generation;var originalStore=lib.Store;var originalList=Get(lib,"library");
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();bool navEnabled=nav.enabled,hud=g.HudVisible;Vector3 pose=nav.transform.position;Quaternion rotation=nav.transform.rotation,head=nav.view.transform.localRotation;Vector2 pan=g.TreePan,angles=g.TreeRotation;float scale=g.TreeScale;
            string last=PlayerPrefs.GetString("AncestorWalk.LastTree","");var box=new GameObject("Inactive isolated editing queue");box.SetActive(false);var gen=box.AddComponent<SceneGeneration>();
            string dir=Path.GetFullPath("Artifacts/TreeEditingPlay/"+Guid.NewGuid().ToString("N"));var store=new TreeStore(dir);Directory.CreateDirectory(dir);Set(gen,"storage",dir);typeof(SceneGeneration).GetProperty("Ready").SetValue(gen,true);
            var queue=(SceneQueueState)Get(gen,"queue");var doc=Fixture();doc.People.Single(p=>p.Id=="@C@").BirthDate="1930";doc.People.Single(p=>p.Id=="@C@").DeathDate="1980";store.Save(doc);var second=Fixture();second.Name="Second disposable tree";store.Save(second);
            void Click(object overlay,string title)
            {
                RectTransform target=null;foreach(var hit in (IEnumerable)Get(overlay,"hits")){var r=(RectTransform)hit.GetType().GetField("Rect").GetValue(hit);if(r.name==title||r.name.StartsWith(title,StringComparison.Ordinal)){target=r;break;}}
                Check(target!=null,"Control exists: "+title);Canvas.ForceUpdateCanvases();var ray=new Ray(target.position-target.forward,target.forward);
                if(overlay is TreeLibraryOverlay library)library.HandleInput(ray,true,true,false);else if(overlay is SceneBatchOverlay batch)batch.HandleInput(ray,true,true,false);
            }
            try
            {
                g.Tutorial.Dismiss();g.AISetup.Close();g.Batch.Close();g.LocationEditor.Close();lib.Close();nav.enabled=false;g.Hud.SetVisible(true);
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);Set(lib,"store",store);Set(lib,"library",store.List());g.OpenDocument(doc);yield return Until(()=>g.CacheIndexReady,"Fixture index ready");
                lib.Open();Click(lib,"New Tree");Click(lib,"Name:");Check((bool)Get(lib,"selectAll")&&((Image)Get(lib,"textSelection")).gameObject.activeSelf,"Tree name opens visibly selected");Call(lib,"Type",'N');Call(lib,"Type",'e');Call(lib,"Type",'w');Check((string)Get(lib,"textValue")=="New","Physical typing replaces the whole initial tree name");Click(lib,"Cancel");Check(((TreeDocument)Get(lib,"draft")).Name=="My family","Cancelled text edit leaves name unchanged");
                Click(lib,"Name:");Click(lib,"Q");Check((string)Get(lib,"textValue")=="Q","Quest virtual key replaces selected tree name");Click(lib,"Done");Check(((TreeDocument)Get(lib,"draft")).Name=="Q","Done applies replacement");Click(lib,"‹ Back");
                lib.OpenPersonEditor("@C@");Click(lib,"Name:");Click(lib,"Backspace");Check((string)Get(lib,"textValue")=="","Virtual backspace clears complete selection");Click(lib,"Cancel");Click(lib,"Sex:");Check(((TreeDocument)Get(lib,"draft")).People.Single(p=>p.Id=="@C@").Sex=="F","Person editor exposes recorded-sex control");lib.Close();
                var properties=new MaterialPropertyBlock();var colors=new List<Color>();foreach(var line in g.GetComponentsInChildren<LineRenderer>().Where(l=>l.name=="Family relationship")){line.GetPropertyBlock(properties);colors.Add(properties.GetColor("_BaseColor"));Check(line.sharedMaterial==g.paper,"Connector colors share one material");}
                Check(colors.Any(c=>c==RelationshipColors.Male)&&colors.Any(c=>c==RelationshipColors.Female)&&colors.Any(c=>c==g.gold.color),"Live family draws blue, pink and neutral segments");
                g.Batch.Open(g.Tree.People["@C@"]);
                if(SceneBatchOverlay.MultiYearPanelEnabled){Check((bool)Get(g.Batch,"selectAll"),"Year-list field opens selected");Click(g.Batch,"1");Click(g.Batch,"9");Check(g.Batch.Years=="19","Batch virtual keyboard replaces once, then appends");g.Batch.Close();}
                else Check(!g.Batch.IsOpen,"Temporarily hidden multi-year panel cannot open");
                g.LocationEditor.Open(g,g.Tree.People["@C@"],1930);g.LocationEditor.Activate("focus");g.LocationEditor.Activate("A");Check((string)Get(g.LocationEditor,"value")=="A","Location editor retains replacement behavior");g.LocationEditor.Close();
                g.AISetup.Open();Set(g.AISetup,"key","fixture-key-never-persisted");Call(g.AISetup,"Focus");Call(g.AISetup,"Type",'x');Check((string)Get(g.AISetup,"key")=="x"&&!((Text)Get(g.AISetup,"keyText")).text.Contains("x"),"Masked key replacement stays masked");g.AISetup.Close();
                lib.Open();Call(lib,"RequestDelete",second);Click(lib,"Keep tree");Check(store.List().Count==2,"Keep tree preserves documents");Call(lib,"RequestDelete",second);lib.HandleInput(new Ray(),true,false,true);Check(store.List().Count==2&&(string)Get(lib,"page")=="home","Quest Back cancels deletion");
                Call(lib,"RequestDelete",second);Click(lib,"Delete tree");yield return Until(()=>!lib.Busy,"Noncurrent deletion finishes asynchronously");Check(store.List().Single().Id==doc.Id&&g.TreeId==doc.Id,"Deleting another tree preserves the current family");
                var active=new PendingScene{treeId=doc.Id,personId="@C@",phase=2,job=new SceneJob{requestId=new string('b',32),treeId=doc.Id,personKey=PanoramaCache.PersonKey("@C@"),year=1930,place="York"}};
                var worker=Activator.CreateInstance(typeof(SceneGeneration).GetNestedType("Worker",BindingFlags.NonPublic),true);worker.GetType().GetField("Pending").SetValue(worker,active);((IDictionary)Get(gen,"workers")).Add(active,worker);queue.active.Add(active);
                queue.waiting.Add(new PendingScene{treeId=doc.Id,personId="@C@",phase=1,job=new SceneJob{requestId=new string('c',32),year=1931}});
                Call(lib,"RequestDelete",doc);Check(((Text)Get(lib,"deleteSummary")).text.Contains("1 active and 1 waiting"),"Delete warning names affected jobs");Click(lib,"Keep tree");Check(gen.Busy&&queue.waiting.Count==1,"Cancel keeps active and waiting jobs intact");
                Call(lib,"RequestDelete",doc);Click(lib,"Delete tree");yield return Until(()=>!lib.Busy,"Current tree cancellation and deletion finish");
                Check(!gen.Busy&&queue.waiting.Count==0&&queue.cancelled.Any(p=>p.job.requestId==active.job.requestId),"Submitted request identity retained after deletion");
                Check(store.List().Count==0&&g.Document.IsDemo&&lib.IsOpen,"Deleting current tree returns safely to demo and library");Check(JsonUtility.FromJson<SceneQueueState>(File.ReadAllText(Path.Combine(dir,"scene-generation-queue.json"))).cancelled.Any(p=>p.job.requestId==active.job.requestId),"Cancellation checkpoint is durable before deletion");
                File.WriteAllText(Report,n+" tree editing Play checks passed: desktop/Quest selection, masked entry, sex controls/colors, confirmation/Back, current/other-tree deletion and durable job cancellation. Synthetic trees only.");
            }
            finally
            {
                lib.Close();g.Batch.Close();g.AISetup.Close();g.LocationEditor.Close();Set(lib,"store",originalStore);Set(lib,"library",originalList);typeof(AncestorGallery).GetProperty("Generation").SetValue(g,originalGen);g.OpenDocument(original);g.SetTreeRotation(angles);g.SetTreeScale(scale);g.PanTree(pan-g.TreePan);nav.enabled=navEnabled;nav.transform.SetPositionAndRotation(pose,rotation);nav.view.transform.localRotation=head;g.Hud.SetVisible(hud);
                if(last.Length==0)PlayerPrefs.DeleteKey("AncestorWalk.LastTree");else PlayerPrefs.SetString("AncestorWalk.LastTree",last);PlayerPrefs.Save();UnityEngine.Object.Destroy(box);
            }
        }
    }
}
