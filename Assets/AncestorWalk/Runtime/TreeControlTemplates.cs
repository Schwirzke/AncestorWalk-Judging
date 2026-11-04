using UnityEngine;

namespace AncestorWalk
{
    public enum TreeControlMode { Slide, TwoStickFlight, Fly }

    // Versioned presets preserve the fixed-axis mapping while allowing level locomotion.
    public static class TreeControlTemplates
    {
        public const string PreferenceKey="AncestorWalk.TreeControlMode.v1";
        public static TreeControlMode Current {get{int value=PlayerPrefs.GetInt(PreferenceKey,2);return value>=0&&value<=2?(TreeControlMode)value:TreeControlMode.Fly;}}
        public static string Name => Current==TreeControlMode.Slide?"Slide":Current==TreeControlMode.Fly?"Fly":"Two-stick flight";
        public static void Select(TreeControlMode mode)
        {PlayerPrefs.SetInt(PreferenceKey,(int)mode);PlayerPrefs.Save();}
        public static Vector3 Movement(TreeControlMode mode,Vector2 left,Vector2 right,float viewYaw)
            =>Movement(mode,left,right,Quaternion.Euler(0,viewYaw,0));
        public static Vector3 Movement(TreeControlMode mode,Vector2 left,Vector2 right,Quaternion viewRotation)
        {
            if(left.magnitude<.2f)left=Vector2.zero;
            if(mode==TreeControlMode.TwoStickFlight)
                return new Vector3(left.x,left.y,Mathf.Abs(right.y)<.2f?0:right.y);
            if(mode==TreeControlMode.Fly)return viewRotation*new Vector3(left.x,0,left.y);
            return Quaternion.Euler(0,viewRotation.eulerAngles.y,0)*new Vector3(left.x,0,left.y);
        }
        public static string QuestHelp => Current==TreeControlMode.Fly?"Quest left: fly where you look · right ←→: turn":Current==TreeControlMode.Slide
            ?"Quest left: slide · right ←→: turn · buttons: height"
            :"Quest left: X/Y · right ↑↓: Z · ←→: turn";
    }
}
