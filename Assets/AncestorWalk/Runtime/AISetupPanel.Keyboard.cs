using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk
{
    public sealed partial class AISetupPanel
    {
        // The system overlay has no placement API. This keypad shares the setup panel's pose.
        // API keys use printable ASCII without spaces; Shift exposes every supported symbol.
        static readonly string[] LowerRows={"1234567890","!@#$%^&*()-=+_","qwertyuiop[]\\","asdfghjkl;'","zxcvbnm,./`"};
        static readonly string[] UpperRows={"1234567890","!@#$%^&*()-=+_","QWERTYUIOP{}|","ASDFGHJKL:\"","ZXCVBNM<>?~"};
        readonly List<(Text Label,char Lower,char Upper)> keyCaps=new();
        sealed class KeyVisual {public RectTransform Rect;public Image Image;public bool Hover;public float FlashUntil;}
        readonly List<KeyVisual> keyVisuals=new();
        RectTransform attachedKeyboard;Text shiftLabel;Image shiftBackground;bool shifted;

        void BuildAttachedKeyboard()
        {
            attachedKeyboard=Rect("Attached API keyboard",card,new Vector2(0,-170),new Vector2(714,236));
            Background(attachedKeyboard,new Color(.025f,.065f,.085f,1));
            for(int row=0;row<LowerRows.Length;row++)
            for(int column=0;column<LowerRows[row].Length;column++)
            {
                char lower=LowerRows[row][column],upper=UpperRows[row][column];
                float spacing=row==0?68:row==1?50:54;
                var cap=KeyButton(lower.ToString(),new Vector2((column-(LowerRows[row].Length-1)*.5f)*spacing,98-row*39),new Vector2(spacing-4,34),()=>TypeAttached(shifted?upper:lower));
                keyCaps.Add((cap,lower,upper));
            }
            shiftLabel=KeyButton("Shift",new Vector2(-276,-97),new Vector2(138,38),()=>{if(Busy)return;shifted=!shifted;RefreshKeyCaps();});
            shiftBackground=shiftLabel.transform.parent.GetComponent<Image>();
            KeyButton("Left",new Vector2(-153,-97),new Vector2(92,38),()=>MoveAttached(-1));
            KeyButton("Right",new Vector2(-53,-97),new Vector2(92,38),()=>MoveAttached(1));
            KeyButton("Backspace",new Vector2(100,-97),new Vector2(198,38),()=>ReplaceAttached("",true));
            KeyButton("Clear",new Vector2(276,-97),new Vector2(138,38),()=>{if(!Busy){SetValue("");AttachedCaret(0);Focus();}});
        }
        Text KeyButton(string title,Vector2 at,Vector2 size,Action action)
        {
            var rect=Rect("API key · "+title,attachedKeyboard,at,size);var image=Background(rect,new Color(.09f,.24f,.30f,1));keyVisuals.Add(new KeyVisual{Rect=rect,Image=image});
            var label=Label(rect,title,Vector2.zero,size,22);hits.Add(new Hit{Rect=rect,Action=action});return label;
        }
        void TypeAttached(char value)
        {
            ReplaceAttached(value.ToString(),false);
        }
        void ReplaceAttached(string value,bool backspace)
        {
            if(!IsOpen||Busy)return;Focus();string text=keyInput.text;
            int a=Mathf.Clamp(keyInput.selectionStringAnchorPosition,0,text.Length),b=Mathf.Clamp(keyInput.selectionStringFocusPosition,0,text.Length);
            int start=Math.Min(a,b),end=Math.Max(a,b);if(backspace&&start==end&&start>0)start--;
            int room=keyInput.characterLimit-(text.Length-(end-start));if(value.Length>room)value=value.Substring(0,Math.Max(0,room));
            keyInput.text=text.Remove(start,end-start).Insert(start,value);AttachedCaret(start+value.Length);
        }
        void MoveAttached(int direction)
        {
            if(Busy)return;Focus();int a=keyInput.selectionStringAnchorPosition,b=keyInput.selectionStringFocusPosition;
            AttachedCaret(a==b?a+direction:direction<0?Math.Min(a,b):Math.Max(a,b));
        }
        void AttachedCaret(int position){position=Mathf.Clamp(position,0,keyInput.text.Length);keyInput.selectionStringAnchorPosition=keyInput.selectionStringFocusPosition=position;keyInput.ForceLabelUpdate();}
        void RefreshKeyCaps()
        {
            foreach(var cap in keyCaps)cap.Label.text=(shifted?cap.Upper:cap.Lower).ToString();
            if(shiftLabel!=null)shiftLabel.text=shifted?"SHIFT ON":"Shift";
            RefreshKeyFeedback();
        }
        void PointKeyboard(Ray ray,bool vr,bool pressed)
        {
            foreach(var visual in keyVisuals){visual.Hover=!Busy&&Contains(visual.Rect,ray,vr);if(visual.Hover&&pressed)visual.FlashUntil=Time.unscaledTime+.18f;}
            RefreshKeyFeedback();
        }
        void RefreshKeyFeedback()
        {
            foreach(var visual in keyVisuals)
            {
                if(!IsOpen){visual.Hover=false;visual.FlashUntil=0;}
                visual.Image.color=visual.FlashUntil>Time.unscaledTime?new Color(.90f,.60f,.18f,1):visual.Hover&&!Busy?new Color(.22f,.50f,.63f,1):visual.Image==shiftBackground&&shifted?new Color(.18f,.43f,.60f,1):new Color(.09f,.24f,.30f,1);
            }
        }
        void BuildVisibilityToggle()
        {
            var button=Rect("Show or hide API key",form,new Vector2(265,29),new Vector2(60,36));Background(button);
            // Draw the eye from UI shapes so it does not depend on an emoji/font glyph.
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI*2/24,b=(i+1)*Mathf.PI*2/24;
                var start=new Vector2(Mathf.Cos(a)*14,4+Mathf.Sin(a)*6);var end=new Vector2(Mathf.Cos(b)*14,4+Mathf.Sin(b)*6);
                var line=Rect("Eye outline",button,(start+end)*.5f,new Vector2(Vector2.Distance(start,end)+.4f,1.6f));line.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(end.y-start.y,end.x-start.x)*Mathf.Rad2Deg);Background(line,Color.white);
            }
            var pupil=Rect("Eye pupil",button,new Vector2(0,4),new Vector2(5,5));Background(pupil,Color.white);
            var slash=Rect("Eye masked",button,new Vector2(0,4),new Vector2(32,2));slash.localRotation=Quaternion.Euler(0,0,30);Background(slash,Color.white);eyeSlash=slash.gameObject;
            eyeLabel=Label(button,"Show",new Vector2(0,-12),new Vector2(54,10),10);
            hits.Add(new Hit{Rect=button,Action=()=>{if(!Busy)SetKeyVisible(!keyVisible);}});
        }
        void SetKeyVisible(bool visible)
        {
            keyVisible=visible;if(keyInput==null)return;
            int a=keyInput.selectionStringAnchorPosition,b=keyInput.selectionStringFocusPosition;
            keyInput.contentType=visible?TMPro.TMP_InputField.ContentType.Standard:TMPro.TMP_InputField.ContentType.Password;
            keyInput.shouldHideSoftKeyboard=wasVr;keyInput.selectionStringAnchorPosition=a;keyInput.selectionStringFocusPosition=b;keyInput.ForceLabelUpdate();
            if(eyeSlash!=null)eyeSlash.SetActive(!visible);if(eyeLabel!=null)eyeLabel.text=visible?"Hide":"Show";
        }
    }
}
