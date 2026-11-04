using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class NodeNavigationChecks
    {
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            if(!Application.isPlaying||g==null)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
            var localPosition=nav.view.transform.localPosition;
            var drag=typeof(GalleryNavigation).GetField("dragging",BindingFlags.Instance|BindingFlags.NonPublic);
            try
            {
                g.ShowTree();nav.ResetTreeView();
                var ids=g.Layout.Positions.Keys.OrderBy(id=>g.Layout.Positions[id].z).ThenBy(id=>g.Layout.Positions[id].x).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
                foreach(string id in ids)
                {
                    nav.transform.position+=g.TreeNodePosition(id)-Vector3.forward*.7f-nav.view.transform.position;
                    Check(nav.NearestNode()==id,"Nearest uses camera distance: "+id);
                    nav.FocusNearestNode();
                    Check(Vector3.Distance(nav.view.transform.position,g.TreeNodePosition(id)+new Vector3(0,.35f,-2.2f))<.001f,"Frames cube front: "+id);
                    Check(Vector3.Angle(nav.view.transform.forward,Vector3.forward)<.01f,"Faces cube: "+id);
                }
                nav.FrameNode(ids[0]);var visited=new HashSet<string>();
                for(int i=0;i<ids.Length;i++){Check(nav.FocusedPersonId==ids[i],"Stable generation/left-right order");visited.Add(nav.FocusedPersonId);nav.StepNode(1);}
                Check(visited.Count==g.Tree.People.Count&&nav.FocusedPersonId==ids[0],"Visits every person and wraps");
                nav.StepNode(-1);Check(nav.FocusedPersonId==ids[ids.Length-1],"Previous wraps");nav.StepNode(1);Check(nav.FocusedPersonId==ids[0],"Next reverses previous");
                nav.view.transform.localPosition+=new Vector3(.3f,.1f,.2f);nav.FrameNode(ids[0]);
                Check(Vector3.Distance(nav.view.transform.position,g.TreeNodePosition(ids[0])+new Vector3(0,.35f,-2.2f))<.001f,"Camera offset accounted for");
                g.PanTree(new Vector2(.3f,-.2f));nav.FrameNode(ids[0]);
                Check(Vector3.Distance(nav.view.transform.position,g.TreeNodePosition(ids[0])+new Vector3(0,.35f,-2.2f))<.001f,"Tree translation accounted for");
                var before=nav.transform.position;nav.FrameNode("missing-id");Check(nav.transform.position==before,"Missing node safe");
                drag.SetValue(nav,g.GetComponentsInChildren<YearSlider>(true).First());nav.StepNode(1);Check(nav.transform.position==before,"Year drag blocks node movement");drag.SetValue(nav,null);
                g.Select(ids[0]);nav.StepNode(1);Check(nav.transform.position==Vector3.zero,"No node jump in historical scene");
                nav.Return();Check(Vector3.Distance(nav.transform.position,before)<.001f&&nav.FocusedPersonId==ids[0],"Focused pose survives scene return");
                nav.FlyTreeSticks(Vector2.right,Vector2.zero,.1f);Check(nav.FocusedPersonId==null,"Free flight resets browsing anchor");
                nav.ResetTreeView();Check(nav.FocusedPersonId==null&&nav.transform.position==Vector3.zero,"Reset clears focus");
                string result=n+" node-navigation checks passed; no generation requests.";System.IO.File.WriteAllText("Artifacts/node-navigation-checks.txt",result);return result;
            }
            finally{drag.SetValue(nav,null);nav.view.transform.localPosition=localPosition;g.ShowTree();g.ResetTreePan();nav.ResetTreeView();}
        }
    }
}
