using System;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ControlTemplateChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);n++;}
            bool had=PlayerPrefs.HasKey(TreeControlTemplates.PreferenceKey);int saved=PlayerPrefs.GetInt(TreeControlTemplates.PreferenceKey);
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var g=nav.gallery;
            var prefs=nav.GetComponent<NavigationPreferences>();var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;float speed=prefs.TreePanSpeed;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);bool tutorialOpen=g.Tutorial.IsOpen;
            try
            {
                PlayerPrefs.DeleteKey(TreeControlTemplates.PreferenceKey);Check(TreeControlTemplates.Current==TreeControlMode.Fly,"Fresh install uses Fly");
                foreach(var mode in new[]{TreeControlMode.Slide,TreeControlMode.TwoStickFlight,TreeControlMode.Fly})
                {TreeControlTemplates.Select(mode);Check(TreeControlTemplates.Current==mode,"Preset persists");}
                foreach(float pitch in new[]{-60f,0f,60f})
                {
                    var heading=Quaternion.Euler(pitch,35,0);
                    var movement=TreeControlTemplates.Movement(TreeControlMode.Fly,Vector2.up,Vector2.one,heading);
                    Check(Vector3.Distance(movement,heading*Vector3.forward)<.001f,"Fly follows full look direction");
                    Check(Vector3.Distance(TreeControlTemplates.Movement(TreeControlMode.Fly,Vector2.right,Vector2.zero,heading),heading*Vector3.right)<.001f,"Fly strafe follows view right");
                }
                foreach(float yaw in new[]{0,90,180,270})
                {
                    var forward=TreeControlTemplates.Movement(TreeControlMode.Slide,Vector2.up,Vector2.one,yaw);
                    var side=TreeControlTemplates.Movement(TreeControlMode.Slide,Vector2.right,Vector2.zero,yaw);
                    Check(Vector3.Distance(forward,Quaternion.Euler(0,yaw,0)*Vector3.forward)<.001f,"Slide forward follows yaw");
                    Check(Mathf.Abs(forward.y)<.001f&&Mathf.Abs(side.y)<.001f,"Slide stays level");
                    Check(Mathf.Abs(Vector3.Dot(forward,side))<.001f,"Slide strafe perpendicular");
                    Check(TreeControlTemplates.Movement(TreeControlMode.TwoStickFlight,Vector2.up,Vector2.up,yaw)==new Vector3(0,1,1),"Saved flight independent of heading");
                }
                Check(TreeControlTemplates.Movement(TreeControlMode.Slide,Vector2.one*.1f,Vector2.one,0)==Vector3.zero,"Slide dead zone; right stick cannot translate");
                g.ShowTree();g.Tutorial.Dismiss();prefs.SetOpen(false);prefs.SetTreePanSpeed(2);TreeControlTemplates.Select(TreeControlMode.Slide);
                nav.ResetTreeView();nav.view.transform.localRotation=Quaternion.Euler(-70,90,25);
                nav.FlyTreeSticks(Vector2.up,Vector2.zero,1);Check(Vector3.Distance(nav.transform.position,Vector3.right*2)<.001f,"Pitch/roll do not lift Slide");
                nav.ResetTreeView();nav.FlyTreeSticks(Vector2.one,Vector2.zero,1);Check(Mathf.Abs(nav.transform.position.magnitude-2)<.001f,"Slide diagonal capped");
                nav.ResetTreeView();for(int i=0;i<10;i++)nav.FlyTreeSticks(Vector2.up,Vector2.zero,.1f);Check(Vector3.Distance(nav.transform.position,Vector3.forward*2)<.001f,"Slide time independent");
                g.Tutorial.Open();var blockedPosition=nav.transform.position;nav.FlyTreeSticks(Vector2.up,Vector2.zero,1);Check(nav.transform.position==blockedPosition,"Tutorial gates Slide");g.Tutorial.Dismiss();
                return n+" control-template checks passed; no API calls";
            }
            finally
            {
                if(had)PlayerPrefs.SetInt(TreeControlTemplates.PreferenceKey,saved);else PlayerPrefs.DeleteKey(TreeControlTemplates.PreferenceKey);
                PlayerPrefs.Save();prefs.SetTreePanSpeed(speed);nav.ResetTreeView();nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                if(tutorialOpen)g.Tutorial.Open();else g.Tutorial.Dismiss();
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
