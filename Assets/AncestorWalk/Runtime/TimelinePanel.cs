using System.Linq;
using UnityEngine;
using TMPro;
namespace AncestorWalk
{
    public sealed class TimelinePanel : MonoBehaviour
    {
        TimelineMarker[] tickData;
        public string PersonId=>person?.Id;
        AncestorGallery gallery; Person person; int birth,end; bool inside;
        bool fullTimeline;TMP_Text yearLabel,placeLabel; Transform knob,controlsRoot;
        Renderer knobRenderer,generateRenderer;TMP_Text generateLabel;GalleryTarget generateTarget;int actionRevision=-1;bool actionReady;
        MaterialPropertyBlock knobBlock;
        readonly System.Collections.Generic.List<Renderer> markerRenderers=new();
        CubeFacingControls facing;readonly System.Collections.Generic.List<GameObject> markers=new();
        public void SetFacingOwner(CubeFacingControls owner){facing=owner;}
        public void SetControlsVisible(bool visible){if(controlsRoot!=null)controlsRoot.gameObject.SetActive(visible);}
        public void Build(AncestorGallery owner,Person p,Vector3 position,bool inScene)
        {
            gallery=owner;person=p;inside=inScene;transform.position=position;
            knobBlock=new MaterialPropertyBlock();
            if(!inside)
            {
                controlsRoot=new GameObject("Year controls").transform;controlsRoot.SetParent(transform,false);
                var action=Box("Regenerate selected year",new Vector3(0,-1.075f,-.055f),new Vector3(2.8f,.26f,.04f),gallery.teal);
                generateRenderer=action.GetComponent<Renderer>();generateTarget=action.AddComponent<GalleryTarget>();generateTarget.personId=p.Id;generateTarget.action=GalleryAction.GenerateScene;
                generateLabel=Text("",new Vector3(0,-1.075f,-.08f),.034f);generateLabel.name="Regenerate year label";
                PanelTextSizing.Attach(generateLabel,new Vector2(2.65f,.22f));RefreshAction();
            }
            bool portrait=!inside&&owner.Portrait(p.Id)!=null;
            float placeX=portrait?PanelPortraitSizing.LabelX:0,placeWidth=portrait?PanelPortraitSizing.LabelWidth:3.4f;
            if(!LifeTimeline.TryRange(p,out birth,out end))
            {
                string known=LifeEndpointScenes.TrySceneRange(p,out birth,out end)?owner.Place(p.Id)+" · "+birth:"Dates unavailable";
                var unavailable=Text(known,new Vector3(placeX,.08f,0),.04f);if(!inside)PanelTextSizing.Attach(unavailable,new Vector2(placeWidth,.32f));RefreshMarkers();return;
            }
            fullTimeline=true;if(controlsRoot==null){controlsRoot=new GameObject("Year controls").transform;controlsRoot.SetParent(transform,false);}
            yearLabel=Text("",new Vector3(0,-.29f,0),.048f);
            yearLabel.name="Current year";
            var rail=Box("Year slider",new Vector3(0,-.6f,0),new Vector3(2.3f,.16f,.025f),gallery.stone);
            var slider=rail.AddComponent<YearSlider>();slider.Changed=t=>gallery.SetYear(p.Id,Mathf.RoundToInt(Mathf.Lerp(birth,end,t)));
            knob=Box("Selected year",new Vector3(0,-.6f,0),new Vector3(.06f,.20f,.025f),gallery.teal).transform;
            knobRenderer=knob.GetComponent<Renderer>();
            slider.SetThumb(knob);
            var birthLabel=Text(birth.ToString(),new Vector3(-1.15f,-.83f,0),.032f);
            birthLabel.name="Birth year";
            birthLabel.alignment=TextAlignmentOptions.Center;
            var deathLabel=Text(end.ToString(),new Vector3(1.15f,-.83f,0),.032f);
            deathLabel.name="End year";
            deathLabel.alignment=TextAlignmentOptions.Center;
            placeLabel=Text("",new Vector3(placeX,.08f,-.06f),.042f,transform);
            if(!inside)
            {
                PanelTextSizing.Attach(yearLabel,new Vector2(.8f,.26f));
                PanelTextSizing.Attach(birthLabel,new Vector2(.7f,.20f));
                PanelTextSizing.Attach(deathLabel,new Vector2(.7f,.20f));
                PanelTextSizing.Attach(placeLabel,new Vector2(placeWidth,.32f));
            }
            Refresh();
        }
        void Update(){bool ready=gallery.IsTree&&gallery.CacheIndexReady;if(generateLabel!=null&&(actionRevision!=gallery.Generation.Revision||ready!=actionReady)){actionReady=ready;RefreshAction();}}
        void RefreshAction()
        {
            if(generateLabel==null)return;actionRevision=gallery.Generation.Revision;
            string label=gallery.SceneActionLabel(person.Id);bool enabled=gallery.CanGenerateScene(person.Id);generateTarget.enabled=enabled;
            if(generateLabel.text!=label){generateLabel.text=label;facing?.InvalidateContent();}
            Color color=enabled?gallery.teal.color:new Color(.14f,.20f,.21f);
            if(facing!=null)facing.SetElementColor(generateRenderer,color);
            else {var block=new MaterialPropertyBlock();block.SetColor("_Color",color);block.SetColor("_BaseColor",color);generateRenderer.SetPropertyBlock(block);}
        }
        public void Refresh()
        {
            RefreshAction();
            if(yearLabel==null){RefreshMarkers();return;}
            int year=gallery.Year(person.Id);var years=gallery.AvailableYears(person.Id);
            var thumbColor=TimelineMarker.ThumbColor(year,gallery.Markers(person.Id),gallery.gold.color,gallery.teal.color);
            if(facing!=null)facing.SetElementColor(knobRenderer,thumbColor);
            else {knobBlock.SetColor("_Color",thumbColor);knobBlock.SetColor("_BaseColor",thumbColor);knobRenderer.SetPropertyBlock(knobBlock);}
            var markerData=gallery.Markers(person.Id);int covered=TimelineMarker.CoveredMarker(year,markerData,birth,end,2.3f,knob.localScale.x,.035f);
            gallery.Hud?.RegisterMarker(knob,covered<0?null:markerData[covered].Tooltip(markerData[covered].MissingLocation&&!markerData[covered].Generated?null:gallery.Locations(person.Id).At(person,markerData[covered].Year)),()=>facing==null||facing.InteractionsVisible&&facing.CloseOpacity>=.5f);
            var entry=gallery.Locations(person.Id).entries.Where(e=>e.year<=year).OrderBy(e=>e.year).LastOrDefault();
            placeLabel.text=Wrap(gallery.Place(person.Id)+" · "+(entry==null?birth:entry.year));
            yearLabel.text=year.ToString();
            knob.localPosition=new Vector3(Mathf.InverseLerp(birth,end,year)*2.3f-1.15f,-.6f,-.003f);
            yearLabel.transform.localPosition=new Vector3(knob.localPosition.x,-.29f,-.12f);
            facing?.InvalidateContent();
            RefreshMarkers();
        }
        void RefreshMarkers()
        {
            // Cache ticks stay separate from the draggable hit target.
            var data=gallery.Markers(person.Id);if(ReferenceEquals(tickData,data))return;tickData=data;
            bool added=markers.Count<data.Length;
            while(markers.Count<data.Length){var tick=Box("Cached year",Vector3.zero,new Vector3(.025f,.09f,.015f),gallery.gold);tick.GetComponent<Collider>().enabled=false;Destroy(tick.GetComponent<Collider>());markers.Add(tick);markerRenderers.Add(tick.GetComponent<Renderer>());}
            for(int i=0;i<markers.Count;i++)
            {
                bool show=i<data.Length;markers[i].SetActive(show);if(!show)continue;
                markers[i].transform.localPosition=new Vector3(fullTimeline?Mathf.InverseLerp(birth,end,data[i].Year)*2.3f-1.15f:0,data[i].LocationMove?-.575f:-.63f,-.045f);
                markers[i].transform.localScale=data[i].LocationMove||data[i].Blinking?new Vector3(.035f,.14f,.015f):new Vector3(.025f,.07f,.015f);
                gallery.Hud?.RegisterMarker(markers[i].transform,data[i].Tooltip(data[i].MissingLocation&&!data[i].Generated?null:gallery.Locations(person.Id).At(person,data[i].Year)),()=>facing==null||facing.InteractionsVisible&&facing.CloseOpacity>=.5f);
                var color=data[i].DisplayColor(gallery.gold.color);
                if(facing!=null)facing.SetElementColor(markerRenderers[i],color,data[i].Blinking);
                else {var block=new MaterialPropertyBlock();block.SetColor("_Color",color);block.SetColor("_BaseColor",color);block.SetFloat("_Blink",data[i].Blinking?1:0);markerRenderers[i].SetPropertyBlock(block);}
            }
            if(added)facing?.RefreshPanelRendering();
        }
        static string Wrap(string text)
        {var lines=new System.Collections.Generic.List<string>();string line="";foreach(var word in text.Split(' ')){if(line.Length+word.Length>38){lines.Add(line);line="";}line+=(line.Length==0?"":" ")+word;}lines.Add(line);return string.Join("\n",lines.Take(2));}
        GameObject Box(string name,Vector3 at,Vector3 size,Material material)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(controlsRoot??transform,false);o.transform.localPosition=at;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=material;return o;}
        TMP_Text Text(string text,Vector3 at,float size,Transform parent=null)
        {var o=new GameObject("Timeline label");o.transform.SetParent(parent??controlsRoot??transform,false);o.transform.localPosition=at+new Vector3(0,0,-.12f);return WorldText.Create(o,text,size*.4f,gallery.paper.color);}
    }
}
