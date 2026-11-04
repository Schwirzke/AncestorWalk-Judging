using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AncestorWalk
{
    // App editors use a physical keyboard or ray-operated keys, with the same selection policy.
    public static class TextEntry
    {
        public static bool Modified(Keyboard keys)=>keys!=null&&(keys.leftMetaKey.isPressed||keys.rightMetaKey.isPressed||keys.leftCtrlKey.isPressed||keys.rightCtrlKey.isPressed);
        public static bool NavigationCharacter(char c)=>c>='\uF700'&&c<='\uF703';
        public static bool FunctionCharacter(char c)=>c>='\uF700'&&c<='\uF8FF';
        public static void Insert(ref string value,ref bool selected,string text,int limit,Func<char,bool> allowed=null)
        {
            if(text?.Length==1&&NavigationCharacter(text[0])){selected=false;return;}
            string clean=new string((text??"").Where(c=>!char.IsControl(c)&&!FunctionCharacter(c)&&(allowed==null||allowed(c))).ToArray());
            if(clean.Length==0)return;
            if(selected)value="";selected=false;int room=Math.Max(0,limit-value.Length);
            value+=clean.Length>room?clean.Substring(0,room):clean;
        }
        public static void Delete(ref string value,ref bool selected)
        {value=selected||value.Length==0?"":value.Substring(0,value.Length-1);selected=false;}
        public static bool Shortcuts(Keyboard keys,ref string value,ref bool selected,int limit,Func<char,bool> allowed=null,bool secret=false)
        {
            if(keys==null)return false;
            if(keys.leftArrowKey.wasPressedThisFrame||keys.rightArrowKey.wasPressedThisFrame||keys.upArrowKey.wasPressedThisFrame||keys.downArrowKey.wasPressedThisFrame||keys.endKey.wasPressedThisFrame){selected=false;return true;}
            if(Modified(keys))
            {
                if(keys.aKey.wasPressedThisFrame){selected=value.Length>0;return true;}
                if(keys.vKey.wasPressedThisFrame){Insert(ref value,ref selected,GUIUtility.systemCopyBuffer,limit,allowed);return true;}
                if(!secret&&selected&&(keys.cKey.wasPressedThisFrame||keys.xKey.wasPressedThisFrame))
                {GUIUtility.systemCopyBuffer=value;if(keys.xKey.wasPressedThisFrame){value="";selected=false;}return true;}
                return false;
            }
            if(keys.backspaceKey.wasPressedThisFrame||keys.deleteKey.wasPressedThisFrame){Delete(ref value,ref selected);return true;}
            return false;
        }
        public static void Caret(Text label,bool selected,bool empty)
        {
            var caret=label.GetComponent<TextEntryCaret>();if(caret==null){caret=label.gameObject.AddComponent<TextEntryCaret>();caret.Bind(label);}caret.ResetBlink(selected,empty);
        }
        public static void Caret(TextMesh label,bool selected,bool empty,Material material,Transform frame)
        {
            var caret=label.GetComponent<TextEntryCaret>();if(caret==null){caret=label.gameObject.AddComponent<TextEntryCaret>();caret.Bind(label,material,frame);}caret.ResetBlink(selected,empty);
        }
        public static Image Highlight(Text label)
        {
            var rect=new GameObject("Selected text",typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(label.transform.parent,false);rect.SetSiblingIndex(label.transform.GetSiblingIndex());
            var source=label.rectTransform;rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;rect.anchoredPosition=source.anchoredPosition;rect.sizeDelta=source.sizeDelta;
            var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.15f,.43f,.55f,.75f);image.raycastTarget=false;rect.gameObject.SetActive(false);return image;
        }
    }
}
