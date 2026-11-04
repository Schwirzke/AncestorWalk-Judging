using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class PortraitSizingChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/portrait-sizing-checks.txt";
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy)throw new Exception("Ready, idle Play mode required");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Checking enlarged portraits, bounds, distance scaling, pool fades and warm movement; no model calls.";
        }
        static IEnumerator Runner(IEnumerator first)
        {
            var stack=new Stack<IEnumerator>();stack.Push(first);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception error=null;try{more=current.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=nav.GetComponent<NavigationPreferences>();
            var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;var treeRotation=g.TreeRotation;
            var pitch=typeof(GalleryNavigation).GetField("pitch",Hidden);var oldPitch=pitch.GetValue(nav);
            var dragging=typeof(GalleryNavigation).GetField("dragging",Hidden);bool wasPrefs=prefs.IsOpen,guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);
            float boost=PanelTextSizing.Boost;bool hadBoost=PlayerPrefs.HasKey(PanelTextSizing.PreferenceKey);
            string selected=g.SelectedId;GameObject controlObject=null;TMP_Text changedName=null,changedPlace=null;string savedName=null,savedPlace=null;
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden);
            SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            try
            {
                g.Tutorial.Dismiss();prefs.SetOpen(false);g.Library.Close();g.ShowTree();g.SetTreeRotation(Vector2.zero);
                var portraits=g.GetComponentsInChildren<PhotoPortrait>();Check(portraits.Length>0,"Open tree has portrait coverage");
                var photo=portraits[0];var controls=photo.GetComponent<CubeFacingControls>();controlObject=controls.gameObject;
                var step=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),controls,advance);
                void Eye(Vector3 direction,float distance){nav.transform.position+=controls.transform.position+direction*distance-nav.view.transform.position;step(.3f);}
                var surface=photo.Surface;var sizing=surface.GetComponent<PanelPortraitSizing>();Check(sizing!=null,"Portrait uses cached sizing component");
                controls.SetInteractionRange(12);prefs.SetTextBoost(0);Eye(Vector3.back,1.5f);
                float deadline=Time.realtimeSinceStartup+20;while(surface.sharedMaterial.mainTexture==Texture2D.grayTexture&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(surface.sharedMaterial.mainTexture!=Texture2D.grayTexture,"Enlarged photo uses existing async texture cache");
                var texture=surface.sharedMaterial.mainTexture;Check(texture.width==256&&texture.height==256,"Photo resolution and storage remain bounded");
                Check(Mathf.Approximately(surface.transform.localScale.x,.8f)&&PanelPortraitSizing.NearSize/.55f>1.45f,"Near portrait is about 45 percent larger");
                prefs.SetTextBoost(.25f);
                foreach(float distance in new[]{1.5f,2,5,8,20})
                {
                    Eye(Vector3.back,distance);float wanted=Mathf.Min(PanelTextSizing.DistanceScale(distance),PanelPortraitSizing.MaximumSize/PanelPortraitSizing.NearSize);
                    Check(Mathf.Abs(sizing.AppliedScale-wanted)<.001f,"Portrait follows shared smooth distance curve at "+distance+"m");
                }
                prefs.SetTextBoost(2);Eye(Vector3.back,8);Check(Mathf.Abs(surface.transform.localScale.x-1.1f)<.001f,"Large boost caps within portrait column");
                changedName=controls.Panel.Find("Label").GetComponent<TMP_Text>();savedName=changedName.text;
                changedPlace=controls.Panel.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Timeline label");savedPlace=changedPlace.text;
                changedName.text="Alexandria Theodosia Montgomery-Winterbourne the Third";changedPlace.text="Alexandria, Mediterranean coast\nCape Town, South Africa · 1905";controls.InvalidateContent();step(.3f);
                foreach(var size in controls.Panel.GetComponentsInChildren<PanelTextSizing>(true))
                {
                    var text=size.GetComponent<TMP_Text>();text.ForceMeshUpdate(true);var b=text.textBounds;var scale=text.transform.localScale;
                    var center=controls.Panel.InverseTransformPoint(text.transform.position);
                    float x=Mathf.Max(Mathf.Abs(b.min.x),Mathf.Abs(b.max.x))*Mathf.Abs(scale.x),y=Mathf.Max(Mathf.Abs(b.min.y),Mathf.Abs(b.max.y))*Mathf.Abs(scale.y);
                    Check(x<=size.Area.x*.5f+.001f&&y<=size.Area.y*.5f+.001f,"Photo-layout labels fit their padded bounds");
                    if(center.y>-.2f)Check(center.x-size.Area.x*.5f>PanelPortraitSizing.Position.x+PanelPortraitSizing.MaximumSize*.5f+.1f,"Header rows reserve a gap beside maximum portrait");
                }
                Check(PanelPortraitSizing.Position.y-PanelPortraitSizing.MaximumSize*.5f>-.21f&&PanelPortraitSizing.Position.y+PanelPortraitSizing.MaximumSize*.5f<.98f,"Photo clears the thumb year and top rule");
                Check(surface.GetComponent<Collider>()==null,"Bigger visual creates no pointer target");
                var noPhoto=g.GetComponentsInChildren<CubeFacingControls>().FirstOrDefault(c=>c.GetComponent<PhotoPortrait>()==null);
                if(noPhoto!=null){var label=noPhoto.Panel.Find("Label").GetComponent<PanelTextSizing>();Check(label.Area.x==3.4f&&Mathf.Approximately(label.transform.localPosition.x,0),"Panels without photos retain full-width centered names");}
                Eye(Vector3.back,5);float before=sizing.AppliedScale;dragging.SetValue(nav,controls.Panel.GetComponentInChildren<YearSlider>(true));Eye(Vector3.back,1.5f);
                Check(sizing.AppliedScale==before,"Dragging freezes portrait size");dragging.SetValue(nav,null);step(.3f);Check(sizing.AppliedScale==1,"Release updates photo size immediately");
                Eye(Vector3.back,5);nav.transform.position+=controls.transform.position+Vector3.right*5-nav.view.transform.position;step(.05f);
                var ghost=controls.transform.Find("Pooled panel face 0");Check(ghost.gameObject.activeSelf,"Outgoing photo face remains pooled and fading");
                var copy=ghost.GetComponentsInChildren<MeshRenderer>().Single(r=>r.sharedMaterial==surface.sharedMaterial);
                nav.transform.position+=controls.transform.position+Vector3.right*8-nav.view.transform.position;step(.02f);
                Check(Vector3.Distance(copy.transform.localScale,surface.transform.localScale)<.001f,"Fading and live portraits scale together");
                Check(ReferenceEquals(copy.sharedMaterial.mainTexture,texture),"Fading faces share the same cached image");
                Eye(Vector3.right,100);float hidden=sizing.AppliedScale;prefs.SetTextBoost(0);step(.3f);Check(sizing.AppliedScale==hidden,"Hidden portraits skip scaling");
                Eye(Vector3.back,1.5f);Check(sizing.AppliedScale==1,"Reappearing photo uses current boost");prefs.SetTextBoost(2);
                for(int i=0;i<16;i++){float a=i*Mathf.PI/4;Eye(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)),5);}
                int objects=controls.GetComponentsInChildren<Transform>(true).Length,pool=controls.PoolBuilds,reads=g.Photos.Reads,commits=g.Photos.Commits;
                long bytes=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<1000;i++){float a=i*.03f,d=5+2*Mathf.Sin(i*.017f);nav.transform.position+=controls.transform.position+new Vector3(Mathf.Sin(a)*d,0,Mathf.Cos(a)*d)-nav.view.transform.position;step(.016f);}
                bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                Check(bytes==0,"Warmed photo/text/facing loop allocates zero managed bytes: "+bytes);
                Check(controls.PoolBuilds==pool&&controls.GetComponentsInChildren<Transform>(true).Length==objects,"Distance changes create no panel or portrait objects");
                Check(g.Photos.Reads==reads&&g.Photos.Commits==commits&&surface.sharedMaterial.mainTexture==texture,"Scaling performs no photo reads, decodes or texture commits");
                File.WriteAllText(Report,n+" portrait checks passed: larger base, near/middle/far bounds, shared preference, long labels, slider freeze, pool fades, hidden skip and zero managed bytes over 1000 movement steps. No API calls or family writes.");
            }
            finally
            {
                dragging.SetValue(nav,null);if(changedName!=null)changedName.text=savedName;if(changedPlace!=null)changedPlace.text=savedPlace;
                prefs.SetTextBoost(boost);if(!hadBoost)PlayerPrefs.DeleteKey(PanelTextSizing.PreferenceKey);PanelTextSizing.ReloadPreference();
                g.SetTreeRotation(treeRotation);g.ShowTree();if(selected!=null)g.Select(selected);prefs.SetOpen(wasPrefs);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;pitch.SetValue(nav,oldPitch);SessionState.SetBool("AncestorWalk.EndpointChecks",guard);
            }
        }
    }
}
