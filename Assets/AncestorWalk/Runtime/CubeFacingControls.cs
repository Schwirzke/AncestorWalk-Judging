using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using TMPro;
namespace AncestorWalk
{
    public sealed class CubeFacingControls : MonoBehaviour
    {
        sealed class Element
        {
            public Renderer Renderer;public TMP_Text Text;public MaterialPropertyBlock Block;public Color Color;
            public MeshFilter Filter;public Element Source;
            public PanelTextSizing Sizing;public PanelPortraitSizing PortraitSizing;public Vector3 SizeBaseScale;
            public bool Scalable=>Sizing!=null||PortraitSizing!=null;
            public float AppliedSize=>Sizing!=null?Sizing.AppliedScale:PortraitSizing!=null?PortraitSizing.AppliedScale:1;
            public bool Blinking;
            public float Alpha=-1;
            public void Fade(float alpha)
            {
                if(Mathf.Approximately(alpha,Alpha))return;Alpha=alpha;var color=Color;color.a=alpha;
                if(Text!=null)Text.color=color;
                else {Block.SetColor("_Color",color);Block.SetFloat("_Blink",Blinking?1:0);Renderer.SetPropertyBlock(Block);}
            }
        }
        sealed class Ghost {public Transform Root;public BoxCollider Blocker;public readonly List<Element> Elements=new();public readonly Dictionary<Renderer,Element> BySource=new();public float Alpha;public int Version=-1;}
        const float HalfDesignSize=1.9f;
        static readonly float FacingCorrectionCosSquared=Mathf.Pow(Mathf.Cos(50f*Mathf.Deg2Rad),2);
        static readonly Vector3[] Sides={Vector3.back,Vector3.right,Vector3.forward,Vector3.left,Vector3.up,Vector3.down};
        static readonly ProfilerMarker Marker=new("Tree.Controls.Update"),SwitchMarker=new("Tree.Controls.SideSwitch");
        Transform panel,portal;GalleryNavigation navigation;AncestorGallery gallery;Renderer cube,panelBacking;Camera view;
        TimelinePanel timeline;Material fadeMaterial;BoxCollider panelCollider;readonly List<Element> live=new();readonly Ghost[] ghosts=new Ghost[Sides.Length];
        YearSlider[] sliders;Collider[] sliderColliders;Collider portalCollider;Renderer[] portalRenderers;TMP_Text[] portalTexts;MaterialPropertyBlock[] portalBlocks;
        int side,verticalHeading,contentVersion;bool initialized,nearby=true,proximityDirty=true,lastInteractive,dirty=true,lastDragging,sideLocked;
        float opacity=1,interactionRange,proximityOpacity,panelY,portalOpacity;bool pointed,portalDirty=true;
        Vector3 lastCamera,lastCenter;Quaternion lastRotation,lastViewRotation;Vector3 lastScale;
        float lastTextBoost=-1;
        float sharedTextScale=-1;bool textSizingDirty=true;
        CubePanoramaPreview preview;SphereCollider sphereCollider;
        float closeDistance,closeOpacity=1;
        float sideLockDistance,distantLockDistance;bool distantLocked;
        public float DistantLockDistance=>distantLockDistance;
        public float SideLockDistance=>sideLockDistance;
        public float CloseOpacity=>closeOpacity;
        public float CloseFadeDistance=>closeDistance;
        float PanelOpacity=>proximityOpacity*closeOpacity;
        public int PoolBuilds {get;private set;}public int SideTransitions {get;private set;}
        public bool InteractionsVisible=>nearby;
        public float InteractionRange
        {
            get{float scale=gallery!=null?gallery.TreeScale:1;return Mathf.Max(interactionRange*scale,closeDistance+TreeLayout.Size*.5f*scale+.5f);}
        }
        public float InteractionOpacity=>proximityOpacity;
        public Transform Panel=>panel;public Transform Portal=>portal;
        public float PortalOpacity=>portalOpacity*closeOpacity;
        public bool PortalInteractive=>pointed&&PortalOpacity>=.5f&&closeOpacity>=.5f&&view!=null&&(view.transform.position-transform.position).sqrMagnitude<InteractionRange*InteractionRange;
        public void SetPointed(bool value)
        {
            if(pointed==value)return;pointed=value;portalDirty=true;dirty=true;
            if(!value&&portalCollider!=null)portalCollider.enabled=false;
        }
        public void Build(Renderer surface)
        {
            panel=transform.Find("Person information panel");portal=transform.Find("Scene portal");cube=surface;
            preview=GetComponent<CubePanoramaPreview>();sphereCollider=surface.GetComponent<SphereCollider>();closeDistance=NodeNearFade.Distance;sideLockDistance=PanelSideLock.Distance;distantLockDistance=DistantPanelLock.Distance;
            panelBacking=panel.Find("Panel backing").GetComponent<Renderer>();view=Camera.main;navigation=FindAnyObjectByType<GalleryNavigation>();
            panelCollider=panelBacking.GetComponent<BoxCollider>();
            timeline=panel.GetComponentInChildren<TimelinePanel>();timeline.SetFacingOwner(this);
            var owner=gallery=GetComponentInParent<AncestorGallery>();fadeMaterial=owner.PanelFadeMaterial;
            interactionRange=NavigationPreferences.InteractionGenerations*(TreeLayout.Size+TreeLayoutSettings.Load().Gap);
            portalCollider=portal.GetComponent<Collider>();portalRenderers=portal.GetComponentsInChildren<Renderer>(true);
            portal.GetComponent<ScenePortal>().Controls=this;
            portal.gameObject.SetActive(false);portalCollider.enabled=false;
            portalTexts=new TMP_Text[portalRenderers.Length];portalBlocks=new MaterialPropertyBlock[portalRenderers.Length];
            for(int i=0;i<portalRenderers.Length;i++){portalTexts[i]=portalRenderers[i].GetComponent<TMP_Text>();portalBlocks[i]=new MaterialPropertyBlock();portalRenderers[i].GetPropertyBlock(portalBlocks[i]);}
            panel.localRotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
            var sphereScale=sphereCollider.transform.lossyScale;
            float sphereTop=sphereCollider.transform.TransformPoint(sphereCollider.center).y+sphereCollider.radius*Mathf.Max(Mathf.Abs(sphereScale.x),Mathf.Abs(sphereScale.y),Mathf.Abs(sphereScale.z));
            var p=panel.position;p.y+=sphereTop+.02f-panelBacking.bounds.min.y;panel.position=p;panelY=panel.localPosition.y;
            for(int i=0;i<Sides.Length;i++)
            {
                var root=new GameObject("Pooled panel face "+i).transform;root.SetParent(transform,false);root.localScale=panel.localScale;
                root.localPosition=new Vector3(0,panelY,0);root.localRotation=PanelSideRotation(i);
                // Outgoing faces remain visible during their fade and must still shield
                // nodes behind them. These pooled surfaces never act as slider targets.
                var blocker=root.gameObject.AddComponent<BoxCollider>();blocker.center=panelBacking.transform.localPosition;
                blocker.size=Vector3.Scale(panelCollider.size,panelBacking.transform.localScale);blocker.enabled=false;
                root.gameObject.AddComponent<SortingGroup>();root.gameObject.SetActive(false);ghosts[i]=new Ghost{Root=root,Blocker=blocker};
            }
            RefreshPanelRendering();surface.sharedMaterial.SetVector("_ControlPositive",Vector4.zero);surface.sharedMaterial.SetVector("_ControlNegative",Vector4.zero);UpdateFacing();
        }
        Element Setup(Renderer renderer)
        {
            var text=renderer.GetComponent<TMP_Text>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            Color color=text!=null?text.color:renderer.sharedMaterial==fadeMaterial?block.GetColor("_Color"):renderer.sharedMaterial.color;
            if(text==null&&block.HasColor("_Color"))color=block.GetColor("_Color");
            if(text is TextMeshPro worldText)worldText.sortingOrder=40;
            else renderer.sortingOrder=renderer.name=="Panel backing"?0:renderer.name=="Year slider"?10:renderer.name=="Cached year"?(block.GetFloat("_Blink")>.5f?35:20):renderer.name=="Selected year"?30:5;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(text==null&&renderer.GetComponent<PhotoPortrait>()==null&&renderer.sharedMaterial.shader.name!="AncestorWalk/Person Photo")renderer.sharedMaterial=fadeMaterial;
            return new Element{Renderer=renderer,Text=text,Block=block,Color=color,Blinking=block.GetFloat("_Blink")>.5f,Filter=renderer.GetComponent<MeshFilter>(),Sizing=text!=null?text.GetComponent<PanelTextSizing>():null,PortraitSizing=renderer.GetComponent<PanelPortraitSizing>()};
        }
        public void InvalidateContent(){contentVersion++;dirty=true;textSizingDirty=true;}
        public void SetElementColor(Renderer renderer,Color color,bool blinking=false)
        {
            foreach(var element in live)if(element.Renderer==renderer)
            {
                if(element.Color==color&&element.Blinking==blinking)return;
                if(renderer.name=="Cached year")renderer.sortingOrder=blinking?35:20;
                element.Color=color;element.Blinking=blinking;element.Alpha=-1;element.Fade(opacity*PanelOpacity);
                foreach(var ghost in ghosts)if(ghost.BySource.TryGetValue(renderer,out var copy))
                {copy.Color=color;copy.Blinking=blinking;copy.Renderer.sortingOrder=renderer.sortingOrder;copy.Alpha=-1;copy.Fade(ghost.Alpha*PanelOpacity);}
                return;
            }
            // New ticks are registered with the pool after their first color is assigned.
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            block.SetColor("_Color",color);block.SetColor("_BaseColor",color);block.SetFloat("_Blink",blinking?1:0);renderer.SetPropertyBlock(block);
        }
        public void RefreshPanelRendering()
        {
            if(panel==null||fadeMaterial==null)return;
            if(panel.GetComponent<SortingGroup>()==null)panel.gameObject.AddComponent<SortingGroup>();
            // TMP owns fallback glyph meshes: its parent text updates their color, sorting and
            // visibility. Pool the text once, rather than treating atlas submeshes as geometry.
            live.Clear();foreach(var renderer in panel.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.GetComponent<TMP_SubMesh>()==null)live.Add(Setup(renderer));
            sliders=panel.GetComponentsInChildren<YearSlider>(true);var targets=new List<Collider>();
            foreach(var slider in sliders){targets.Add(slider.GetComponent<Collider>());if(slider.ThumbCollider!=null)targets.Add(slider.ThumbCollider);}foreach(var action in panel.GetComponentsInChildren<GalleryTarget>(true)){var hit=action.GetComponent<Collider>();if(hit!=null)targets.Add(hit);}sliderColliders=targets.ToArray();
            foreach(var ghost in ghosts)if(ghost!=null)
            {
                foreach(var old in ghost.Elements)old.Source=null;
                foreach(var source in live)
                {
                    if(ghost.BySource.TryGetValue(source.Renderer,out var existing)){existing.Source=source;continue;}
                    var item=new GameObject("Pooled panel element");item.transform.SetParent(ghost.Root,false);
                    Renderer renderer;
                    if(source.Text!=null){WorldText.Create(item,"",.01f,source.Color);renderer=item.GetComponent<MeshRenderer>();renderer.sharedMaterial=source.Renderer.sharedMaterial;}
                    else{item.AddComponent<MeshFilter>().sharedMesh=source.Filter.sharedMesh;renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=source.Renderer.sharedMaterial.shader.name=="AncestorWalk/Person Photo"?source.Renderer.sharedMaterial:fadeMaterial;}
                    var element=Setup(renderer);element.Source=source;ghost.Elements.Add(element);ghost.BySource.Add(source.Renderer,element);PoolBuilds++;
                }
                // Keep spare elements for future cache-marker changes, instead of destroying them.
                ghost.Version=-1;
            }
            contentVersion++;FadeLive();dirty=true;textSizingDirty=true;lastInteractive=!lastInteractive;UpdateInteraction();
        }
        void SyncGhost(Ghost ghost)
        {
            if(ghost.Version==contentVersion)return;
            for(int i=0;i<ghost.Elements.Count;i++)
            {
                var target=ghost.Elements[i];var source=target.Source;bool exists=source!=null&&source.Renderer.gameObject.activeInHierarchy;target.Renderer.gameObject.SetActive(exists);if(!exists)continue;
                var tr=target.Renderer.transform;var sr=source.Renderer.transform;
                tr.localPosition=panel.InverseTransformPoint(sr.position);tr.localRotation=Quaternion.Inverse(panel.rotation)*sr.rotation;
                var a=sr.lossyScale;var b=panel.lossyScale;tr.localScale=new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
                if(source.Scalable)target.SizeBaseScale=tr.localScale/source.AppliedSize;
                target.Renderer.enabled=true;target.Color=source.Color;target.Blinking=source.Blinking;target.Alpha=-1;target.Renderer.sortingOrder=source.Renderer.sortingOrder;
                if(source.Text!=null)
                {
                    var text=target.Text;var from=source.Text;text.font=from.font;text.fontSize=from.fontSize;
                    text.alignment=from.alignment;text.richText=from.richText;text.text=from.text;
                    text.rectTransform.sizeDelta=from.rectTransform.sizeDelta;text.margin=from.margin;
                    target.Renderer.sharedMaterial=source.Renderer.sharedMaterial;
                }
                else target.Filter.sharedMesh=source.Filter.sharedMesh;
            }
            ghost.Version=contentVersion;
        }
        void LateUpdate(){UpdateFacing();}
        void UpdateFacing(){using(Marker.Auto())AdvanceFacing(Time.unscaledDeltaTime);}
        public void SetInteractionRange(float generations){interactionRange=generations*(TreeLayout.Size+TreeLayoutSettings.Load().Gap);dirty=true;UpdateFacing();}
        public void SetCloseFadeDistance(float metres){closeDistance=NodeNearFade.ValidDistance(metres);dirty=true;UpdateFacing();}
        public void SetSideLockDistance(float metres){sideLockDistance=PanelSideLock.ValidDistance(metres);sideLocked=false;dirty=true;UpdateFacing();}
        public void SetDistantLockDistance(float metres){distantLockDistance=DistantPanelLock.ValidDistance(metres);distantLocked=false;dirty=true;UpdateFacing();}
        public float DistanceToSurface(Vector3 eye)
        {
            float distance=sphereCollider!=null?NodeNearFade.SphereDistance(eye,sphereCollider):Vector3.Distance(eye,transform.position);
            distance=Mathf.Min(distance,NodeNearFade.BoxDistance(eye,panelCollider.transform,panelCollider.center,panelCollider.size));
            foreach(var ghost in ghosts)if(ghost!=null&&ghost.Alpha>0)
                distance=Mathf.Min(distance,NodeNearFade.BoxDistance(eye,ghost.Root,ghost.Blocker.center,ghost.Blocker.size));
            return distance;
        }
        void AdvanceFacing(float deltaTime)
        {
            if(view==null)view=Camera.main;if(view==null)return;
            var eye=view.transform.position;var center=transform.position;bool dragging=navigation!=null&&navigation.IsDraggingYear;
            bool dragEnded=lastDragging&&!dragging;
            float surfaceDistance=DistanceToSurface(eye);
            bool wasDistantLocked=distantLocked;distantLocked=initialized&&DistantPanelLock.Frozen(surfaceDistance,distantLockDistance,distantLocked);
            // Keep a nearby reading/interaction surface still, including when near fading is off.
            // Physical-metre hysteresis also works when the tree is scaled or tilted.
            sideLocked=initialized&&sideLockDistance>0&&surfaceDistance<=sideLockDistance+(sideLocked?PanelSideLock.ReleaseBuffer:0);
            float closeTarget=closeDistance<=0?1:NodeNearFade.Opacity(surfaceDistance,closeDistance);
            bool moving=dirty||wasDistantLocked!=distantLocked||lastTextBoost!=PanelTextSizing.Boost||eye!=lastCamera||view.transform.rotation!=lastViewRotation||center!=lastCenter||transform.rotation!=lastRotation||transform.lossyScale!=lastScale;
            bool fading=closeOpacity!=closeTarget||opacity<1||proximityOpacity!=(nearby?1:0)||portalOpacity!=(nearby&&pointed?1:0);foreach(var ghost in ghosts)if(ghost.Alpha>0)fading=true;
            if(!moving&&!fading&&!proximityDirty&&!portalDirty&&dragging==lastDragging)return;
            lastCamera=eye;lastViewRotation=view.transform.rotation;lastCenter=center;lastRotation=transform.rotation;lastScale=transform.lossyScale;lastDragging=dragging;dirty=false;
            float beforeClose=closeOpacity;
            closeOpacity=initialized?Mathf.MoveTowards(closeOpacity,closeTarget,deltaTime/NodeNearFade.FadeSeconds):closeTarget;
            if(beforeClose!=closeOpacity)portalDirty=true;
            preview?.SetVisibility(closeOpacity);
            if(sphereCollider!=null&&sphereCollider.enabled!=(closeOpacity>=.5f))sphereCollider.enabled=closeOpacity>=.5f;
            bool value=(eye-center).sqrMagnitude<InteractionRange*InteractionRange;
            if(nearby!=value){nearby=value;proximityDirty=true;}
            // Fully hidden controls do only the distance/pose check; no hierarchy scans or transform writes.
            if(!nearby&&proximityOpacity<=0&&!proximityDirty){AdvancePortal(deltaTime);UpdateInteraction();return;}
            foreach(var slider in sliders)slider.UpdateHitArea(eye);
            if(!dragging)
            {
                float requested=PanelTextSizing.DistanceScale(Vector3.Distance(eye,center));
                bool portraitResized=false;foreach(var element in live)if(element.PortraitSizing!=null)portraitResized|=element.PortraitSizing.Apply(requested);
                bool refitted=false;foreach(var element in live)if(element.Sizing!=null)
                {refitted|=element.Sizing.Prepare();requested=Mathf.Min(requested,element.Sizing.GrowthLimit);}
                bool resized=refitted||textSizingDirty||Mathf.Abs(requested-sharedTextScale)>=.005f;
                if(resized)
                {
                    foreach(var element in live)if(element.Sizing!=null)element.Sizing.ApplyShared(requested);
                    sharedTextScale=requested;textSizingDirty=false;
                }
                foreach(var ghost in ghosts)if(ghost.Alpha>0)
                {
                    if(ghost.Version!=contentVersion)SyncGhost(ghost);
                    else if(resized||portraitResized)foreach(var element in ghost.Elements)
                        if(element.Source!=null&&element.Source.Scalable)element.Renderer.transform.localScale=element.SizeBaseScale*element.Source.AppliedSize;
                }
                lastTextBoost=PanelTextSizing.Boost;
            }
            if(!dragging&&!distantLocked&&(!initialized||moving||dragEnded))
            {
                var localEye=transform.InverseTransformPoint(eye);
                int next=!initialized||!sideLocked||!PanelComfortablyFacesViewer(eye)?ChooseSide(localEye,side):side;
                int heading=verticalHeading;
                // Keep text upright on screen. Lateral viewer position must not roll
                // top/bottom text sideways, especially when the whole tree is tilted.
                if(next>=4)heading=ChooseHeading(transform.InverseTransformDirection(view.transform.up),verticalHeading);
                var orientation=PanelSideRotation(next,heading);
                if(!initialized||next!=side||Quaternion.Angle(panel.localRotation,orientation)>.1f)
                {
                    if(initialized&&opacity>0&&proximityOpacity>0)
                    {
                        using(SwitchMarker.Auto()){var old=ghosts[side];SyncGhost(old);old.Root.localRotation=panel.localRotation;old.Alpha=opacity;old.Root.gameObject.SetActive(true);SideTransitions++;}
                    }
                    if(initialized&&next==side)opacity=0;
                    else
                    {
                        var incoming=ghosts[next];opacity=initialized&&Quaternion.Angle(incoming.Root.localRotation,orientation)<.1f?incoming.Alpha:initialized?0:1;
                        incoming.Alpha=0;incoming.Root.gameObject.SetActive(false);
                    }
                    side=next;verticalHeading=heading;initialized=true;panel.localPosition=new Vector3(0,panelY,0);panel.localRotation=orientation;
                }
            }
            if(!dragging)
            {
                opacity=Mathf.MoveTowards(opacity,1,deltaTime/.25f);
                foreach(var ghost in ghosts)if(ghost.Alpha>0){ghost.Alpha=Mathf.MoveTowards(ghost.Alpha,0,deltaTime/.25f);if(ghost.Alpha==0)ghost.Root.gameObject.SetActive(false);}
            }
            float before=proximityOpacity;proximityOpacity=Mathf.MoveTowards(proximityOpacity,nearby?1:0,deltaTime/.25f);
            if(before!=proximityOpacity||beforeClose!=closeOpacity||proximityDirty)
            {
                bool draw=(nearby||proximityOpacity>0)&&closeOpacity>0;bool showing=draw&&!panel.gameObject.activeSelf;
                if(panel.gameObject.activeSelf!=draw)panel.gameObject.SetActive(draw);
                if(showing)timeline.Refresh();
                foreach(var ghost in ghosts)if(PanelOpacity==0)ghost.Root.gameObject.SetActive(false);
                proximityDirty=false;
            }
            FadeLive();foreach(var ghost in ghosts)
            {
                bool draw=ghost.Alpha*PanelOpacity>0;if(ghost.Root.gameObject.activeSelf!=draw)ghost.Root.gameObject.SetActive(draw);
                if(ghost.Alpha>0)foreach(var e in ghost.Elements)e.Fade(ghost.Alpha*PanelOpacity);
            }
            AdvancePortal(deltaTime);
            UpdateInteraction();
            if(!portal.gameObject.activeSelf||!moving)return;
            var direction=transform.InverseTransformPoint(eye);if(direction.sqrMagnitude<.0001f)return;direction.Normalize();
            // Sphere surface has the same radius in every direction.
            portal.localPosition=direction*(HalfDesignSize+.18f);Face(portal,eye);
        }
        void FadeLive(){foreach(var e in live)e.Fade(opacity*PanelOpacity);}
        bool PanelComfortablyFacesViewer(Vector3 eye)
        {
            // Correct beyond 50 degrees: keep a small buffer past the 45-degree corner.
            var toward=eye-panelCollider.transform.TransformPoint(panelCollider.center);
            float facing=Vector3.Dot(-panel.forward,toward);
            return facing>.0001f&&facing*facing>toward.sqrMagnitude*FacingCorrectionCosSquared;
        }
        void AdvancePortal(float deltaTime)
        {
            float old=portalOpacity;portalOpacity=Mathf.MoveTowards(portalOpacity,nearby&&pointed?1:0,deltaTime/.25f);
            if(old==portalOpacity&&!portalDirty)return;
            float visibleOpacity=PortalOpacity;bool draw=visibleOpacity>0;if(portal.gameObject.activeSelf!=draw)portal.gameObject.SetActive(draw);
            for(int i=0;i<portalRenderers.Length;i++)
            {if(portalTexts[i]!=null){var color=portalTexts[i].color;color.a=visibleOpacity;portalTexts[i].color=color;}else{portalBlocks[i].SetFloat("_Opacity",visibleOpacity);portalRenderers[i].SetPropertyBlock(portalBlocks[i]);}}
            portalDirty=false;if(portalCollider.enabled!=PortalInteractive)portalCollider.enabled=PortalInteractive;
        }
        void UpdateInteraction()
        {
            bool interactive=nearby&&PanelOpacity>=.5f;bool slider=interactive&&opacity>=.5f;
            bool block=opacity*PanelOpacity>0;
            if(panelCollider.enabled!=block)panelCollider.enabled=block;
            foreach(var ghost in ghosts)if(ghost!=null)
            {bool visible=ghost.Alpha*PanelOpacity>0;if(ghost.Blocker.enabled!=visible)ghost.Blocker.enabled=visible;}
            if(portalCollider!=null&&portalCollider.enabled!=PortalInteractive)portalCollider.enabled=PortalInteractive;
            if(sliderColliders!=null)foreach(var collider in sliderColliders)if(collider!=null&&collider.enabled!=slider)collider.enabled=slider;
            lastInteractive=interactive;
        }
        static Quaternion PanelSideRotation(int index,int heading=0)=>Quaternion.LookRotation(-Sides[index],index>=4?-Sides[heading]:Vector3.up);
        static int ChooseHeading(Vector3 up,int current)
        {
            up.y=0;if(up.sqrMagnitude<.01f)return current;up.Normalize();
            int best=current;float score=Vector3.Dot(up,-Sides[current]);
            for(int i=0;i<4;i++)if(Vector3.Dot(up,-Sides[i])>score+.08f){best=i;score=Vector3.Dot(up,-Sides[i]);}
            return best;
        }
        public static int ChooseSide(Vector3 toward,int current)
        {
            // Remain flat across oblique overhead movement; leave only once the viewer
            // is clearly beside this plane. Opposite-hemisphere views still correct.
            if(current>=4&&toward.y*Sides[current].y>0&&Mathf.Abs(toward.y)>=Mathf.Max(Mathf.Abs(toward.x),Mathf.Abs(toward.z))*.75f)return current;
            if(toward.sqrMagnitude<.0001f)return current;toward.Normalize();int best=current;float score=Vector3.Dot(toward,Sides[current]);
            for(int i=0;i<Sides.Length;i++)if(Vector3.Dot(toward,Sides[i])>score+.08f){best=i;score=Vector3.Dot(toward,Sides[i]);}return best;
        }
        static void Face(Transform item,Vector3 viewer)
        {
            var direction=viewer-item.position;if(direction.sqrMagnitude<.0001f)return;direction.Normalize();
            item.rotation=Quaternion.LookRotation(-direction,Mathf.Abs(Vector3.Dot(direction,Vector3.up))>.98f?Vector3.forward:Vector3.up);
        }
    }
}
