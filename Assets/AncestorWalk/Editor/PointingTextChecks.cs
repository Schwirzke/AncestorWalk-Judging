using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace AncestorWalk.Editor
{
    public static class PointingTextChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int count=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",BindingFlags.Instance|BindingFlags.NonPublic);
            void Step(CubeFacingControls c,float seconds){advance.Invoke(c,new object[]{seconds});Physics.SyncTransforms();}
            Collider RayTo(Vector3 p){Physics.SyncTransforms();var ray=new Ray(nav.view.transform.position,p-nav.view.transform.position);Check(Physics.Raycast(ray,out var hit,1000),"Ray reaches node");return hit.collider;}
            try
            {
                g.ShowTree();nav.ResetTreeView();
                foreach(var node in g.GetComponentsInChildren<TreeNodeTarget>())
                {
                    nav.ClearTreePointing();nav.FrameNode(node.PersonId);Step(node.Controls,.3f);
                    var c=node.Controls;
                    Check(!c.Portal.gameObject.activeSelf&&!c.Portal.GetComponent<Collider>().enabled,"Nearby untargeted portal stays hidden");
                    var hit=RayTo(node.transform.position);
                    Check(hit==node.GetComponent<Collider>(),"Foremost target is cube body");
                    nav.HandleTreeHit(hit,false);Step(c,.06f);
                    Check(c.PortalOpacity>0&&c.PortalOpacity<1&&!c.PortalInteractive,"Portal fades in before accepting input");
                    Step(c,.3f);Check(c.PortalInteractive&&c.Portal.GetComponent<Collider>().enabled,"Pointed nearby portal accepts input");
                    hit=RayTo(node.transform.position);
                    Check(hit.GetComponent<ScenePortal>()!=null,"Portal takes priority over cube body");
                    nav.HandleTreeHit(hit,false);Step(c,.3f);
                    Check(nav.PointedPersonId==node.PersonId&&c.PortalOpacity==1,"Moving pointer onto portal sustains reveal");
                    nav.HandleTreeHit(null,false);
                    Check(!c.Portal.GetComponent<Collider>().enabled,"Leaving disables portal hits immediately");
                    Step(c,.06f);Check(c.PortalOpacity>0&&c.PortalOpacity<1,"Leaving fades portal out");
                    Step(c,.3f);Check(!c.Portal.gameObject.activeSelf,"Fade-out deactivates portal");
                    nav.transform.position+=Vector3.back*(c.InteractionRange+3);Step(c,.3f);
                    Check(nav.HandleTreeHit(node.GetComponent<Collider>(),true)&&g.IsTree,"Distant cube click teleports without entering scene");
                    Check(Vector3.Distance(nav.view.transform.position,g.TreeNodePosition(node.PersonId)+new Vector3(0,.35f,-2.2f))<.001f,"Teleport frames correct cube front");
                }
                var target=g.GetComponentsInChildren<TreeNodeTarget>().First();nav.ClearTreePointing();nav.FrameNode(target.PersonId);
                nav.HandleTreeHit(target.GetComponent<Collider>(),false);Step(target.Controls,.3f);
                var other=g.GetComponentsInChildren<TreeNodeTarget>().Last();nav.HandleTreeHit(other.GetComponent<Collider>(),false);Step(target.Controls,.3f);
                Check(!target.Controls.Portal.gameObject.activeSelf,"Changing target hides old portal");
                nav.HandleTreeHit(target.GetComponent<Collider>(),false);Step(target.Controls,.3f);
                var portal=target.Controls.Portal.GetComponent<Collider>();var position=nav.transform.position;
                Check(nav.HandleTreeHit(portal,true)&&!g.IsTree&&g.SelectedId==target.PersonId,"Portal click enters correct historical scene");
                Check(nav.PointedPersonId==null,"Scene entry clears hover");nav.Return();
                Check(Vector3.Distance(nav.transform.position,position)<.001f,"Returning restores teleport pose");
                target=g.GetComponentsInChildren<TreeNodeTarget>().First();nav.FrameNode(target.PersonId);
                var drag=typeof(GalleryNavigation).GetField("dragging",BindingFlags.Instance|BindingFlags.NonPublic);
                drag.SetValue(nav,target.Controls.Panel.GetComponentInChildren<YearSlider>(true));
                try{Check(!nav.HandleTreeHit(target.GetComponent<Collider>(),true)&&nav.PointedPersonId==null,"Dragging year blocks cube selection and hover");}
                finally{drag.SetValue(nav,null);}
                nav.HandleTreeHit(target.GetComponent<Collider>(),false);Step(target.Controls,.3f);
                nav.transform.position+=Vector3.back*(target.Controls.InteractionRange+3);Step(target.Controls,.3f);
                Check(!target.Controls.Portal.gameObject.activeSelf&&!target.Controls.PortalInteractive,"Pointing out of range cannot reveal portal");
                Check(WorldText.Font.atlasPopulationMode==AtlasPopulationMode.Static,"Font atlas never regenerates during movement");
                Check(WorldText.Font.material.shader.name=="AncestorWalk/World SDF"&&WorldText.Font.material.shader.isSupported,"Depth-tested SDF shader supported");
                foreach(string name in new[]{"Mobile","PC"})Check(UnityEditor.AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/"+name+"_RPAsset.asset").msaaSampleCount==4,name+" pipeline has 4x MSAA");
                Check(nav.view.allowMSAA,"Camera allows MSAA");
                foreach(var c in g.GetComponentsInChildren<CubeFacingControls>())
                {
                    Check(c.Panel.GetComponentsInChildren<TextMesh>(true).Length==0,"No bitmap text remains on person panel");
                    foreach(var label in c.Panel.GetComponentsInChildren<TMP_Text>(true))
                    {label.ForceMeshUpdate(true);Check(label.font==WorldText.Font&&label.textInfo.characterCount>0,"Person label uses populated shared SDF atlas");}
                }
                string result=count+" pointing/text checks passed; no generation, Quest deployment or user data changes.";
                System.IO.File.WriteAllText("Artifacts/pointing-text-checks.txt",result);return result;
            }
            finally{nav.ClearTreePointing();g.ShowTree();nav.ResetTreeView();}
        }
    }
}
