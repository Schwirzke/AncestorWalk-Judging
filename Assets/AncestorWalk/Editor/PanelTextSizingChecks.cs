using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class PanelTextSizingChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var prefs=nav.GetComponent<NavigationPreferences>();var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            bool had=PlayerPrefs.HasKey(PanelTextSizing.PreferenceKey);float boost=PanelTextSizing.Boost,speed=prefs.TreePanSpeed,sensitivity=prefs.LookSensitivity,brightness=TreeEnvironment.Brightness;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",BindingFlags.Instance|BindingFlags.NonPublic);
            var drag=typeof(GalleryNavigation).GetField("dragging",BindingFlags.Instance|BindingFlags.NonPublic);
            long bytes=0;
            try
            {
                g.Tutorial.Dismiss();g.ShowTree();prefs.SetOpen(false);
                prefs.SetTextBoost(.25f);Check(PanelTextSizing.DistanceScale(0)==1&&PanelTextSizing.DistanceScale(2)==1,"Near text retains original size");
                Check(Mathf.Approximately(PanelTextSizing.DistanceScale(5),1.125f),"Smooth midpoint");
                Check(PanelTextSizing.DistanceScale(8)==1.25f&&PanelTextSizing.DistanceScale(80)==1.25f,"Far boost capped");
                prefs.SetTextBoost(2);PlayerPrefs.Save();PanelTextSizing.ReloadPreference();Check(PanelTextSizing.Boost==2,"Two hundred percent preference reload");
                Check(PanelTextSizing.DistanceScale(8)==3,"Two hundred percent adds twice the original size");
                prefs.SetTextBoost(float.NaN);Check(PanelTextSizing.Boost==.25f,"Invalid boost defaults safely");
                prefs.SetTextBoost(3);Check(PanelTextSizing.Boost==2,"Boost clamps at two hundred percent");
                var reset=prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Reset");reset.onClick.Invoke();Check(PanelTextSizing.Boost==.25f,"Reset restores boost");
                prefs.SetTextBoost(2);
                Check(prefs.GetComponentsInChildren<Slider>(true).Single(s=>s.name=="Distant panel text boost").maxValue==2,"Slider reaches two hundred percent");
                var all=g.GetComponentsInChildren<CubeFacingControls>();Check(all.Length==31,"Sample tree included");
                foreach(var c in all)
                {
                    c.SetInteractionRange(6);var step=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),c,advance);
                    foreach(float distance in new[]{1.5f,5,8})
                    {
                        nav.transform.position+=c.transform.position+Vector3.back*distance-nav.view.transform.position;step(.3f);
                        var panelSizes=c.Panel.GetComponentsInChildren<PanelTextSizing>(true);
                        float common=panelSizes[0].AppliedScale/panelSizes[0].FittedBaseline;
                        foreach(var sizing in panelSizes)
                        {
                            var text=sizing.GetComponent<TMP_Text>();text.ForceMeshUpdate(true);var b=text.textBounds;var scale=text.transform.localScale;
                            Check(Mathf.Max(Mathf.Abs(b.min.x),Mathf.Abs(b.max.x))*Mathf.Abs(scale.x)<=sizing.Area.x*.5f+.001f,"Text stays in row width: "+text.text);
                            Check(Mathf.Max(Mathf.Abs(b.min.y),Mathf.Abs(b.max.y))*Mathf.Abs(scale.y)<=sizing.Area.y*.5f+.001f,"Text stays in row height: "+text.text);
                            Check(Mathf.Abs(sizing.AppliedScale/sizing.FittedBaseline-common)<.0001f,"Every label shares panel growth");
                        }
                    }
                    var rail=c.Panel.GetComponentInChildren<YearSlider>(true).transform;var timeline=rail.parent;
                    Check(Mathf.Approximately(timeline.Find("Birth year").localPosition.x,-1.15f)&&Mathf.Approximately(timeline.Find("End year").localPosition.x,1.15f),"Endpoint anchors unchanged");
                    Check(Mathf.Approximately(timeline.Find("Current year").localPosition.x,timeline.Find("Selected year").localPosition.x),"Current year follows thumb");
                }
                var control=all[0];var tick=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),control,advance);
                var name=control.Panel.Find("Label").GetComponent<TMP_Text>();var fit=name.GetComponent<PanelTextSizing>();string savedName=name.text;
                foreach(string value in new[]{"Rose","Alexandria Theodosia Montgomery-Winterbourne the Third","OneVeryLongUnbrokenFamilyNameThatMustRemainInsideThePanel"})
                {
                    name.text=value;control.InvalidateContent();nav.transform.position+=control.transform.position+Vector3.back*8-nav.view.transform.position;tick(.3f);
                    Check(name.textBounds.size.x*name.transform.localScale.x<fit.Area.x,"Long name fits without overflow");
                    var labels=control.Panel.GetComponentsInChildren<PanelTextSizing>(true);
                    Check(labels.All(s=>Mathf.Abs(s.AppliedScale/s.FittedBaseline-fit.AppliedScale/fit.FittedBaseline)<.0001f),"Fitted long label retains shared growth");
                }
                var year=control.Panel.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Current year").GetComponent<PanelTextSizing>();
                float cappedYear=year.AppliedScale,cappedName=fit.AppliedScale;
                nav.transform.position+=Vector3.forward*2;tick(.3f);
                Check(Mathf.Abs(year.AppliedScale-cappedYear)<.0001f&&Mathf.Abs(fit.AppliedScale-cappedName)<.0001f,"Year and long name stop together at the first row limit");
                name.text="Rose";control.InvalidateContent();nav.transform.position+=control.transform.position+Vector3.back*1.5f-nav.view.transform.position;tick(.3f);float near=fit.AppliedScale;
                nav.transform.position+=Vector3.back*6.5f;tick(.3f);Check(fit.AppliedScale>near*1.2f,"Short name actually grows with distance");
                float frozen=fit.AppliedScale;drag.SetValue(nav,control.Panel.GetComponentInChildren<YearSlider>(true));nav.transform.position+=Vector3.forward*6;tick(.3f);Check(fit.AppliedScale==frozen,"Drag freezes size");
                drag.SetValue(nav,null);tick(.3f);Check(fit.AppliedScale<frozen,"Size updates after drag release");
                nav.transform.position+=control.transform.position+Vector3.back*5-nav.view.transform.position;tick(.3f);
                nav.transform.position+=control.transform.position+Vector3.right*5-nav.view.transform.position;tick(.05f);
                var ghost=control.transform.Find("Pooled panel face 0");Check(ghost.gameObject.activeSelf,"Outgoing face fades");
                var ghostText=ghost.GetComponentsInChildren<TMP_Text>(true).First(t=>t.text==name.text);
                nav.transform.position+=control.transform.position+Vector3.right*7-nav.view.transform.position;tick(.02f);
                Check(Vector3.Distance(ghostText.transform.localScale,name.transform.localScale)<.001f,"Fading face tracks text size");
                nav.transform.position+=control.transform.position+Vector3.back*30-nav.view.transform.position;tick(.3f);int count=fit.FitCount;
                name.text="Changed while hidden";control.InvalidateContent();tick(.3f);Check(fit.FitCount==count,"Hidden text skips fitting");
                nav.transform.position+=control.transform.position+Vector3.back*5-nav.view.transform.position;tick(.3f);Check(fit.FitCount==count+1,"Visible changed text refits once");
                name.text=savedName;control.InvalidateContent();tick(.3f);
                var place=control.Panel.GetComponentsInChildren<PanelTextSizing>(true).Single(s=>s.name=="Timeline label");var placeText=place.GetComponent<TMP_Text>();string savedPlace=placeText.text;
                placeText.text="Alexandria, Mediterranean coast\nCape Town, South Africa · 1905";place.Apply(3);
                Check(placeText.textBounds.size.y*placeText.transform.localScale.y<place.Area.y&&placeText.textBounds.size.x*placeText.transform.localScale.x<place.Area.x,"Two-line location fits its row");
                placeText.text=savedPlace;place.Apply(1.25f);
                var dates=control.Panel.GetComponentsInChildren<TMP_Text>(true).First(t=>t.name=="Label"&&t!=name);string savedDates=dates.text;
                foreach(string value in new[]{"","Unknown — Unknown","ABT 12 DEC 1800 — BEF 24 DEC 1899"})
                {dates.text=value;var s=dates.GetComponent<PanelTextSizing>();s.Apply(3);Check(dates.textBounds.size.x*dates.transform.localScale.x<=s.Area.x,"Missing/long dates fit");}
                dates.text=savedDates;dates.GetComponent<PanelTextSizing>().Apply(1.25f);
                var current=control.Panel.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Current year");var currentPosition=current.transform.localPosition;
                foreach(float x in new[]{-1.15f,1.15f})
                {
                    current.transform.localPosition=new Vector3(x,currentPosition.y,currentPosition.z);current.GetComponent<PanelTextSizing>().Apply(3);
                    var b=current.textBounds;float width=Mathf.Max(Mathf.Abs(b.min.x),Mathf.Abs(b.max.x))*current.transform.localScale.x;
                    Check(Mathf.Abs(x)+width<1.70f,"Thumb year remains inside panel at endpoint");
                }
                current.transform.localPosition=currentPosition;control.InvalidateContent();tick(.3f);
                // Warm face pools and fit limits, then vary distance and facing without changing content.
                for(int i=0;i<16;i++){float a=i*Mathf.PI/4;nav.transform.position+=control.transform.position+new Vector3(Mathf.Sin(a)*5,0,Mathf.Cos(a)*5)-nav.view.transform.position;tick(.3f);}
                var sizingAll=control.Panel.GetComponentsInChildren<PanelTextSizing>(true);int fits=sizingAll.Sum(s=>s.FitCount),pool=control.PoolBuilds,objects=control.GetComponentsInChildren<Transform>(true).Length;
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<1000;i++){float a=i*.03f,d=5+2*Mathf.Sin(i*.017f);nav.transform.position+=control.transform.position+new Vector3(Mathf.Sin(a)*d,0,Mathf.Cos(a)*d)-nav.view.transform.position;tick(.016f);}
                bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(sizingAll.Sum(s=>s.FitCount)==fits,"Movement never repeats fitting");Check(control.PoolBuilds==pool&&control.GetComponentsInChildren<Transform>(true).Length==objects,"Movement creates no panel objects");Check(bytes==0,"Warmed scaling/facing loop allocates zero managed bytes: "+bytes);
                string result=n+" panel text checks passed; "+bytes+" managed bytes / 1000 varying-distance face steps; no API calls or Quest deployment.";System.IO.File.WriteAllText("Artifacts/panel-text-sizing-checks.txt",result);return result;
            }
            finally
            {
                drag.SetValue(nav,null);prefs.SetTextBoost(boost);prefs.SetTreePanSpeed(speed);prefs.SetSensitivity(sensitivity);prefs.SetTreeBrightness(brightness);
                if(!had)PlayerPrefs.DeleteKey(PanelTextSizing.PreferenceKey);PanelTextSizing.ReloadPreference();
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
                g.ShowTree();nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
            }
        }
    }
}
