using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class PanelSideLockChecks
    {
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||!g.IsTree||!g.CacheIndexReady)throw new Exception("Wait for the indexed tree in Play mode");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var eye=Camera.main.transform;var position=eye.position;var eyeRotation=eye.rotation;var angles=g.TreeRotation;float scale=g.TreeScale;
            var node=g.GetComponentsInChildren<CubeFacingControls>().First();float fade=node.CloseFadeDistance;
            var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();var layout=TreeLayoutSettings.Load();float interaction=NavigationPreferences.InteractionGenerations;
            bool had=PlayerPrefs.HasKey(PanelSideLock.PreferenceKey);float saved=PlayerPrefs.GetFloat(PanelSideLock.PreferenceKey);
            bool hadDistant=PlayerPrefs.HasKey(DistantPanelLock.PreferenceKey);float savedDistant=PlayerPrefs.GetFloat(DistantPanelLock.PreferenceKey);
            bool hadFade=PlayerPrefs.HasKey(NodeNearFade.PreferenceKey);float savedFade=PlayerPrefs.GetFloat(NodeNearFade.PreferenceKey);
            var sphere=node.GetComponentsInChildren<SphereCollider>().Single();
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var capture=typeof(GalleryNavigation).GetField("dragging",BindingFlags.Instance|BindingFlags.NonPublic);var oldCapture=capture.GetValue(nav);
            var step=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),node,typeof(CubeFacingControls).GetMethod("AdvanceFacing",BindingFlags.Instance|BindingFlags.NonPublic));
            bool Readable(){var box=node.Panel.Find("Panel backing").GetComponent<BoxCollider>();return Vector3.Dot(-node.Panel.forward,eye.position-box.transform.TransformPoint(box.center))>0;}
            try
            {
                Check(CubeFacingControls.ChooseSide(Vector3.up,0)==4&&CubeFacingControls.ChooseSide(Vector3.down,0)==5,"Overhead and underneath views select vertical faces");
                Check(CubeFacingControls.ChooseSide(new Vector3(1,1.03f,0),1)==1,"Vertical corner buffer prevents face chatter");
                Check(CubeFacingControls.ChooseSide(new Vector3(6,5,0),4)==4&&CubeFacingControls.ChooseSide(new Vector3(8,5,0),4)==1,"Top panel stays flat until the viewer is clearly beside it");
                // Per-node override proves the lock does not depend on the user's near-fade preference.
                Check(DistantPanelLock.Frozen(9,8,false)&&DistantPanelLock.Frozen(7.9f,8,true)&&!DistantPanelLock.Frozen(7.7f,8,true)&&!DistantPanelLock.Frozen(100,0,false),"Distant freeze has an approach buffer and Off mode");
                node.SetDistantLockDistance(0);
                node.SetCloseFadeDistance(0);
                node.SetSideLockDistance(1);
                Check(PanelSideLock.ValidDistance(float.NaN)==3&&PanelSideLock.ValidDistance(float.PositiveInfinity)==3,"Invalid lock distances use the farther default");
                Check(PanelSideLock.ValidDistance(-1)==0&&PanelSideLock.ValidDistance(50)==30,"Lock setting supports Off through 30 metres");
                Check(g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.Panel.localPosition.x==0&&c.Panel.localPosition.z==0),"All information panels are centered above their spheres");
                foreach(float size in new[]{.25f,1f,2f})foreach(var rotation in new[]{Vector2.zero,new Vector2(52,35)})
                {
                    node.SetSideLockDistance(1);
                    g.SetTreeScale(size);g.SetTreeRotation(rotation);
                    Check(node.Panel.localPosition.x==0&&node.Panel.localPosition.z==0,"Scale and tilt preserve the centered panel anchor");
                    var center=sphere.transform.TransformPoint(sphere.center);float radius=sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x);
                    void Eye(Vector3 direction,float gap){eye.position=center+node.transform.rotation*direction*(radius+gap);step(.3f);}
                    Eye(Vector3.back,5+size*2);Eye(Vector3.back,5+size*2);
                    var anchor=node.Panel.localRotation;var facing=node.Panel.localRotation;int transitions=node.SideTransitions,pools=node.PoolBuilds,reads=g.Thumbnails.DiskReads;
                    Eye(Vector3.back,.6f);
                    for(int i=0;i<20;i++)
                    {
                        float angle=(45+(i%2==0?.5f:-.5f))*Mathf.Deg2Rad;
                        Eye(new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle)),.6f);
                        Check(node.Panel.localRotation==anchor&&node.Panel.localRotation==facing,"Readable nearby panel stays fixed through corner jitter");
                    }
                    for(int i=0;i<8;i++)
                    {
                        float angle=i*Mathf.PI/4;Eye(new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle)),.6f);
                        Check(Readable(),"Close orbit corrects wrong-facing panels throughout the range");
                    }
                    Eye(Vector3.back,5+size*2);Eye(Vector3.back,.6f);anchor=node.Panel.localRotation;transitions=node.SideTransitions;
                    Eye(Vector3.back,1.1f);
                    Check(node.Panel.localRotation==anchor,"Hysteresis holds panel through the release buffer");
                    Check((bool)typeof(CubeFacingControls).GetField("sideLocked",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(node),"Distance lock persists in the release buffer");
                    Eye(Vector3.back,1.35f);
                    Check(!(bool)typeof(CubeFacingControls).GetField("sideLocked",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(node),"Distance lock releases beyond its buffer");
                    Eye(Vector3.right,1.35f);
                    Check(node.Panel.localRotation!=anchor&&node.SideTransitions==transitions+1,"Moving away resumes the normal side crossfade");
                    float earlyGap=Mathf.Min(3,node.InteractionRange-radius-.2f);
                    node.SetSideLockDistance(4);Eye(Vector3.back,7+size*2);Eye(Vector3.back,earlyGap);
                    anchor=node.Panel.localRotation;transitions=node.SideTransitions;
                    var beforePanel=node.Panel.Find("Panel backing").GetComponent<BoxCollider>();
                    var earlyEye=center+node.transform.rotation*new Vector3(Mathf.Sin(55*Mathf.Deg2Rad),0,-Mathf.Cos(55*Mathf.Deg2Rad))*(radius+earlyGap);
                    Check(Vector3.Dot(-node.Panel.forward,earlyEye-beforePanel.transform.TransformPoint(beforePanel.center))>0,"Early-correction path is still on the old panel's front side");
                    eye.position=earlyEye;step(.3f);
                    Check(node.Panel.localRotation!=anchor&&node.SideTransitions==transitions+1&&Readable(),"A 55-degree oblique panel corrects inside the lock range");
                    anchor=node.Panel.localRotation;
                    for(int i=0;i<10;i++)
                    {
                        float angle=(55+(i%2==0?.5f:-.5f))*Mathf.Deg2Rad;
                        Eye(new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle)),earlyGap);
                        Check(node.Panel.localRotation==anchor,"Early correction remains stable through small reverse movements");
                    }
                    node.SetSideLockDistance(1);
                    Eye(Vector3.back,5+size*2);anchor=node.Panel.localRotation;
                    Eye(Vector3.right,.6f);
                    Check(node.Panel.localRotation!=anchor&&Readable(),"Entering the range corrects a side chosen farther away");
                    Eye(Vector3.back,5+size*2);Eye(Vector3.back,.6f);anchor=node.Panel.localRotation;
                    capture.SetValue(nav,node.Panel.GetComponentInChildren<YearSlider>(true));
                    Eye(Vector3.forward,.6f);
                    Check(node.Panel.localRotation==anchor,"Year drag keeps its anchor even when the viewer moves behind it");
                    capture.SetValue(nav,null);step(.3f);
                    Check(node.Panel.localRotation!=anchor&&Readable(),"Release corrects facing without another camera movement");
                    Eye(Vector3.back,5+size*2);anchor=node.Panel.localRotation;
                    eye.position=center+node.transform.rotation*Vector3.right*(radius+.6f);node.SetSideLockDistance(2);
                    Check(node.Panel.localRotation!=anchor&&Readable(),"Adjusting distance immediately corrects a wrong-facing panel");
                    Eye(Vector3.back,5+size*2);Eye(Vector3.back,.6f);anchor=node.Panel.localRotation;transitions=node.SideTransitions;
                    eye.position=center+node.transform.rotation*Vector3.forward*(radius+.6f);step(.05f);
                    Check(node.SideTransitions==transitions+1&&node.Panel.localRotation!=anchor&&Readable(),"Wrong-facing correction selects the facing side immediately");
                    Check(node.transform.Cast<Transform>().Any(t=>t.name.StartsWith("Pooled panel face ")&&t.gameObject.activeSelf),"Correction retains outgoing stationary face during crossfade");
                    Check(node.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Pooled panel face ")).All(t=>t.localPosition==node.Panel.localPosition),"Live and fading panels share the same centered anchor");
                    step(.3f);Check(!node.transform.Cast<Transform>().Any(t=>t.name.StartsWith("Pooled panel face ")&&t.gameObject.activeSelf),"Correction completes the existing crossfade");
                    Eye(Vector3.forward,5+size*2);Eye(Vector3.forward,5+size*2);
                    var backing=node.Panel.Find("Panel backing").GetComponent<BoxCollider>();
                    var panelCenter=backing.transform.TransformPoint(backing.center);
                    eye.position=panelCenter-backing.transform.forward*.1f;step(.3f);
                    anchor=node.Panel.localRotation;facing=node.Panel.localRotation;transitions=node.SideTransitions;
                    eye.position=panelCenter+backing.transform.right*.03f-backing.transform.forward*.1f;step(.3f);
                    Check(node.DistanceToSurface(eye.position)<1&&node.Panel.localRotation==anchor&&node.Panel.localRotation==facing&&node.SideTransitions==transitions,"Panel-surface approach locks its side");
                    eye.position=panelCenter+backing.transform.right*(radius+1);step(.3f);
                    Check(node.Panel.localRotation!=anchor&&Readable(),"Edge-on panel corrects to a readable fixed side");
                    Check(node.PoolBuilds==pools&&g.Thumbnails.DiskReads==reads,"Lock/release does not rebuild pools or read panoramas");
                }
                node.SetCloseFadeDistance(0);node.SetSideLockDistance(30);
                g.SetTreeScale(1);g.SetTreeRotation(new Vector2(.81f,-85));
                eye.position=node.Panel.position+node.transform.up*5;step(.3f);step(.3f);
                Check(Vector3.Dot(-node.Panel.forward,node.transform.up)>.99f&&Readable(),"Tilted tree corrects to the top-facing panel within the nearby lock");
                int overheadPool=node.PoolBuilds;
                eye.position=node.Panel.position+node.transform.TransformDirection(new Vector3(0,5,-4));step(.3f);step(.3f);
                var topRotation=node.Panel.localRotation;var topNormal=-node.Panel.forward;
                eye.position=node.Panel.position+node.transform.TransformDirection(new Vector3(6,5,0));step(.05f);
                Check(Vector3.Dot(-node.Panel.forward,topNormal)>.99f&&Quaternion.Angle(topRotation,node.Panel.localRotation)<.1f,"Lateral overhead movement keeps text upright rather than rolling it");
                Check(Vector3.Dot(node.Panel.up,eye.up)>.9f,"Tilted-tree top text aligns with camera up");
                eye.rotation=Quaternion.AngleAxis(90,topNormal)*eye.rotation;step(.05f);
                Check(Quaternion.Angle(topRotation,node.Panel.localRotation)>80&&Vector3.Dot(node.Panel.up,eye.up)>.9f,"Camera-only roll updates the fixed reading heading");
                var topGhost=node.transform.Find("Pooled panel face 4");
                Check(topGhost.gameObject.activeSelf&&Quaternion.Angle(topGhost.localRotation,topRotation)<.1f,"Reading-heading crossfade keeps the outgoing orientation stationary");
                eye.rotation=eyeRotation;step(.3f);var overheadRotation=node.Panel.localRotation;
                capture.SetValue(nav,node.Panel.GetComponentInChildren<YearSlider>(true));
                eye.position=node.Panel.position-node.transform.up*5;step(.3f);
                Check(node.Panel.localRotation==overheadRotation,"Vertical facing stays locked during slider drag");
                capture.SetValue(nav,null);step(.3f);step(.3f);
                Check(Vector3.Dot(-node.Panel.forward,-node.transform.up)>.99f&&Readable(),"Underneath view switches to the bottom-facing panel after release");
                Check(node.PoolBuilds==overheadPool,"Vertical transitions reuse their prebuilt panel pools");
                node.SetDistantLockDistance(0);node.SetInteractionRange(20);
                eye.position=node.Panel.position+node.transform.up;step(.3f);step(.3f);node.SetDistantLockDistance(2);
                eye.position=node.Panel.position+node.transform.up*5;step(.3f);var farAnchor=node.Panel.localRotation;int farTransitions=node.SideTransitions;
                eye.position=node.Panel.position-node.transform.up*5;step(.3f);
                eye.rotation=Quaternion.AngleAxis(90,node.transform.up)*eye.rotation;step(.3f);
                Check(node.Panel.localRotation==farAnchor&&node.SideTransitions==farTransitions,"Distant panels keep side and reading heading even when the viewer moves behind them");
                eye.rotation=eyeRotation;eye.position=node.Panel.position-node.transform.up*1.5f;step(.3f);step(.3f);
                Check(Vector3.Dot(-node.Panel.forward,-node.transform.up)>.99f,"Approaching resumes normal panel correction");
                node.SetDistantLockDistance(0);
                var farSlider=prefs.GetComponentsInChildren<UnityEngine.UI.Slider>(true).Single(s=>s.name=="Distant panel freeze distance");
                Check(farSlider.minValue==0&&farSlider.maxValue==30,"Freeze distance slider supports Off through 30 metres");
                farSlider.value=12;
                Check(PlayerPrefs.GetFloat(DistantPanelLock.PreferenceKey)==12&&g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.DistantLockDistance==12),"Freeze setting saves and applies immediately to all panels");
                prefs.SetDistantPanelLockDistance(0);
                var slider=prefs.GetComponentsInChildren<UnityEngine.UI.Slider>(true).Single(s=>s.name=="Panel side lock distance");
                Check(slider.minValue==0&&slider.maxValue==30,"Preferences slider exposes the complete distance range");
                slider.value=30f;
                Check(PlayerPrefs.GetFloat(PanelSideLock.PreferenceKey)==30f&&g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.SideLockDistance==30f),"Slider applies immediately to all panels and saves the selected distance");
                g.SetTreeScale(1);g.SetTreeRotation(Vector2.zero);
                var worldCenter=sphere.transform.TransformPoint(sphere.center);float worldRadius=sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x);
                eye.position=worldCenter+Vector3.back*(worldRadius+7);step(.3f);step(.3f);
                var lockedAnchor=node.Panel.localRotation;
                eye.position=worldCenter+Vector3.back*(worldRadius+3);step(.3f);
                eye.position=worldCenter+new Vector3(.5f,0,-.8660254f)*(worldRadius+3);step(.3f);
                Check(node.Panel.localRotation==lockedAnchor,"Configured farther distance holds a comfortably readable side");
                eye.position=worldCenter+Vector3.forward*(worldRadius+3);step(.3f);
                Check(node.Panel.localRotation!=lockedAnchor&&Readable(),"Configured farther distance still corrects wrong-facing panels");
                slider.value=0;
                lockedAnchor=node.Panel.localRotation;
                eye.position=worldCenter+Vector3.back*(worldRadius+.6f);step(.3f);
                Check(node.Panel.localRotation!=lockedAnchor,"Off resumes normal side selection even nearby");
                prefs.SetPanelSideLockDistance(30f);g.ShowTree();
                Check(g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.SideLockDistance==30f),"Rebuilt panels load the persisted setting");
                prefs.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="Reset layout").onClick.Invoke();
                Check(PanelSideLock.Distance==3&&slider.value==3&&g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.SideLockDistance==3),"Reset layout restores the 3 metre baseline");
                Check(DistantPanelLock.Distance==PreferenceDefaults.DistantPanelLockDistance&&farSlider.value==PreferenceDefaults.DistantPanelLockDistance,"Reset restores the distant freeze default");
                string result=n+" panel-side lock checks passed: earlier oblique-angle corrections, readable-side stability, wrong-facing/edge/entry corrections, stationary crossfades, drag/release, corner jitter, panel surfaces, hysteresis, scaled/tilted trees, unchanged pools/preview reads, live saved settings, Off and Reset.";
                Directory.CreateDirectory("Artifacts/PanelSideLock");File.WriteAllText("Artifacts/PanelSideLock/checks.txt",result);return result;
            }
            finally
            {
                capture.SetValue(nav,oldCapture);
                prefs.SetTreeLayout(layout.Gap,layout.Stagger,layout.Rise);prefs.SetInteractionDistance(interaction);
                if(had)PlayerPrefs.SetFloat(PanelSideLock.PreferenceKey,saved);else PlayerPrefs.DeleteKey(PanelSideLock.PreferenceKey);
                prefs.SetPanelSideLockDistance(PanelSideLock.Distance);
                if(!had)PlayerPrefs.DeleteKey(PanelSideLock.PreferenceKey);PlayerPrefs.Save();
                if(hadFade)PlayerPrefs.SetFloat(NodeNearFade.PreferenceKey,savedFade);else PlayerPrefs.DeleteKey(NodeNearFade.PreferenceKey);
                prefs.SetCloseFadeDistance(NodeNearFade.Distance);if(!hadFade)PlayerPrefs.DeleteKey(NodeNearFade.PreferenceKey);PlayerPrefs.Save();
                if(hadDistant)PlayerPrefs.SetFloat(DistantPanelLock.PreferenceKey,savedDistant);else PlayerPrefs.DeleteKey(DistantPanelLock.PreferenceKey);
                prefs.SetDistantPanelLockDistance(DistantPanelLock.Distance);if(!hadDistant)PlayerPrefs.DeleteKey(DistantPanelLock.PreferenceKey);PlayerPrefs.Save();
                g.SetTreeScale(scale);g.SetTreeRotation(angles);eye.SetPositionAndRotation(position,eyeRotation);
                foreach(var controls in g.GetComponentsInChildren<CubeFacingControls>())controls.SetCloseFadeDistance(fade);
            }
        }
    }
}
