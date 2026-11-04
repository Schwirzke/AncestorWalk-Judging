using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
namespace AncestorWalk
{
    // End-of-text caret for the app's select-all/append editors; it never consumes input.
    public sealed class TextEntryCaret : MonoBehaviour
    {
        Text ui;TextMesh world;Transform frame;Image image;LineRenderer line;
        bool selected,empty,dirty=true;float restarted;string lastText;Vector2 lastSize;int lastFont;
        public bool IsVisible=>gameObject.activeInHierarchy&&(image!=null?image.enabled:line!=null&&line.enabled);
        public static bool Blink(float elapsed)=>Mathf.Repeat(elapsed,1)<.55f;
        public void Bind(Text label)
        {
            ui=label;var child=new GameObject("Text caret",typeof(RectTransform));child.transform.SetParent(transform,false);
            image=child.AddComponent<Image>();image.raycastTarget=false;image.color=label.color;image.enabled=false;
        }
        public void Bind(TextMesh label,Material material,Transform inputFrame)
        {
            world=label;frame=inputFrame;var child=new GameObject("Text caret");child.transform.SetParent(transform,false);
            line=child.AddComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=material;line.positionCount=2;
            line.startWidth=line.endWidth=.008f;line.startColor=line.endColor=label.color;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
        }
        public void ResetBlink(bool allSelected,bool isEmpty){selected=allSelected;empty=isEmpty;dirty=true;restarted=Time.unscaledTime;RefreshNow();}
        void LateUpdate()=>RefreshNow();
        public void RefreshNow()
        {
            if(ui==null&&world==null)return;
            string text=ui!=null?ui.text:world.text;var size=ui!=null?ui.rectTransform.rect.size:Vector2.zero;int font=ui!=null?ui.fontSize:world.fontSize;
            if(dirty||lastText!=text||lastSize!=size||lastFont!=font)
            {
                if(ui!=null)PositionUI();else PositionWorld();lastText=text;lastSize=size;lastFont=font;dirty=false;
            }
            bool visible=!selected&&Blink(Time.unscaledTime-restarted);
            if(image!=null){if(image.enabled!=visible)image.enabled=visible;if(image.color!=ui.color)image.color=ui.color;}
            else if(line.enabled!=visible)line.enabled=visible;
        }
        void PositionUI()
        {
            var rect=ui.rectTransform.rect;float height=Mathf.Min(ui.fontSize+2,rect.height-4),x=0,y=rect.center.y;
            int horizontal=(int)ui.alignment%3;
            x=horizontal==0?rect.xMin+2:horizontal==2?rect.xMax-2:rect.center.x;
            if(!empty&&ui.font!=null)
            {
                // Use the same glyph cursor/line metrics as Unity's built-in InputField.
                var gen=ui.cachedTextGenerator;gen.Populate(ui.text,ui.GetGenerationSettings(rect.size));
                float unit=Mathf.Max(.001f,ui.pixelsPerUnit);int index=ui.text.Length;
                if(gen.characters.Count>0)
                {
                    int last=Mathf.Min(index,gen.characters.Count-1);var ch=gen.characters[last];
                    x=(ch.cursorPos.x+(index>=gen.characters.Count?ch.charWidth:0))/unit;
                    int row=0;for(int i=1;i<gen.lines.Count;i++)if(gen.lines[i].startCharIdx<=index)row=i;
                    if(gen.lines.Count>0){height=gen.lines[row].height/unit;y=gen.lines[row].topY/unit-height*.5f;}
                }
            }
            height=Mathf.Clamp(height,2,Mathf.Max(2,rect.height-4));x=Mathf.Clamp(x,rect.xMin+2,rect.xMax-2);y=Mathf.Clamp(y,rect.yMin+height*.5f,rect.yMax-height*.5f);
            image.rectTransform.sizeDelta=new Vector2(2,height);image.rectTransform.localPosition=new Vector3(x,y,0);
        }
        void PositionWorld()
        {
            var bounds=world.GetComponent<Renderer>().localBounds;float x=empty?0:bounds.max.x+.018f;
            if(!empty)
            {
                int spaces=0;for(int i=world.text.Length-1;i>=0&&world.text[i]==' ';i--)spaces++;
                if(spaces>0&&world.font.GetCharacterInfo(' ',out var glyph,world.fontSize,world.fontStyle))x+=spaces*glyph.advance*world.characterSize*.1f;
            }
            float height=Mathf.Max(.10f,bounds.size.y),y=empty?0:bounds.center.y;
            if(frame!=null)
            {
                var left=transform.InverseTransformPoint(frame.TransformPoint(Vector3.left*.5f));var right=transform.InverseTransformPoint(frame.TransformPoint(Vector3.right*.5f));
                x=Mathf.Clamp(x,left.x+.025f,right.x-.025f);
            }
            line.SetPosition(0,new Vector3(x,y-height*.5f,-.008f));line.SetPosition(1,new Vector3(x,y+height*.5f,-.008f));
        }
    }
}
