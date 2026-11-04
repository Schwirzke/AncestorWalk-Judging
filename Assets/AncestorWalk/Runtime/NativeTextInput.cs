using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
namespace AncestorWalk
{
    // Unity owns keyboard editing, selection, clipboard, scrolling and caret rendering.
    public sealed class NativeTextInput : MonoBehaviour
    {
        TMP_InputField field;
        static TMP_InputField rayInput;static PointerEventData rayPointer;
        public static bool Editing{get{var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;var input=selected!=null?selected.GetComponent<TMP_InputField>():null;return input!=null&&input.isFocused&&UIFade.Interactive(input.transform);}}
        // Keep the existing label rect as the clipped viewport so callers retain their layout anchors.
        public static TMP_InputField Bind(Text label,Action<string> changed,int limit,bool secret=false,RectTransform host=null)
        {
            var rect=label.rectTransform;
            if(host==null){host=new GameObject("Native text input",typeof(RectTransform)).GetComponent<RectTransform>();host.SetParent(rect.parent,false);
            host.anchorMin=rect.anchorMin;host.anchorMax=rect.anchorMax;host.pivot=rect.pivot;host.anchoredPosition=rect.anchoredPosition;host.sizeDelta=rect.sizeDelta;}
            rect.SetParent(host,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(8,2);rect.offsetMax=new Vector2(-8,-2);
            label.enabled=false;label.raycastTarget=false;rect.gameObject.AddComponent<RectMask2D>();
            var text=Text(rect,"Editable text",label.text,label.fontSize,label.color);text.alignment=TextAlignmentOptions.MidlineLeft;
            var image=host.GetComponent<Image>();if(image==null){image=host.gameObject.AddComponent<Image>();image.color=new Color(.07f,.14f,.18f,.98f);}image.raycastTarget=true;
            var input=host.gameObject.AddComponent<TMP_InputField>();input.textViewport=rect;input.textComponent=text;input.targetGraphic=image;input.characterLimit=limit;input.caretColor=Color.white;input.customCaretColor=true;input.selectionColor=new Color(.10f,.38f,.78f,1f);input.onFocusSelectAll=false;
            if(secret)input.contentType=TMP_InputField.ContentType.Password;
            input.SetTextWithoutNotify(label.text);input.onValueChanged.AddListener(v=>changed?.Invoke(v));
            var canvas=host.GetComponentInParent<Canvas>();if(canvas!=null&&canvas.GetComponent<GraphicRaycaster>()==null)canvas.gameObject.AddComponent<GraphicRaycaster>();
            host.gameObject.AddComponent<NativeTextInput>().field=input;return input;
        }
        static TextMeshProUGUI Text(RectTransform parent,string name,string value,float size,Color color)
        {
            var text=new GameObject(name,typeof(RectTransform)).AddComponent<TextMeshProUGUI>();text.transform.SetParent(parent,false);
            text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;text.rectTransform.offsetMin=text.rectTransform.offsetMax=Vector2.zero;
            text.font=WorldText.Font;text.fontSize=size;text.color=color;text.text=value;text.richText=false;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;return text;
        }
        public static void Placeholder(TMP_InputField input,string value)
        {var label=Text(input.textViewport,"Placeholder",value,input.textComponent.fontSize,new Color(.7f,.75f,.78f));label.alignment=input.textComponent.alignment;input.placeholder=label;}
        public static void Focus(TMP_InputField input,bool all=false)
        {if(input==null)return;if(!input.isFocused)input.ActivateInputField();if(all)input.GetComponent<NativeTextInput>().StartCoroutine(input.GetComponent<NativeTextInput>().SelectAll());}
        IEnumerator SelectAll(){yield return null;if(field!=null&&field.isActiveAndEnabled){field.selectionStringAnchorPosition=0;field.selectionStringFocusPosition=field.text.Length;field.ForceLabelUpdate();}}
        public static void Insert(TMP_InputField input,string text)
        {if(input==null)return;foreach(char c in text)input.ProcessEvent(new Event{type=EventType.KeyDown,character=c});input.ForceLabelUpdate();}
        public static void Delete(TMP_InputField input)
        {if(input==null)return;input.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Backspace});input.ForceLabelUpdate();}
        public static void Point(TMP_InputField input,Ray ray)
        {
            if(input==null||!input.interactable||!UIFade.Interactive(input.transform))return;var rect=(RectTransform)input.transform;var plane=new Plane(rect.forward,rect.position);
            if(!plane.Raycast(ray,out float d))return;
            var canvas=input.GetComponentInParent<Canvas>();var camera=canvas.worldCamera!=null?canvas.worldCamera:Camera.main;
            var result=new RaycastResult{gameObject=input.gameObject,module=canvas.GetComponent<GraphicRaycaster>(),worldPosition=ray.GetPoint(d),worldNormal=-rect.forward,distance=d};
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=camera.WorldToScreenPoint(result.worldPosition),pointerPressRaycast=result,pointerCurrentRaycast=result};
            EndPointer();input.OnPointerDown(data);rayInput=input;rayPointer=data;rayPointer.pressPosition=data.position;
        }
        public static void UpdatePointer(Ray ray,bool vr,bool held)
        {
            if(rayInput==null){rayPointer=null;return;}
            if(!vr||!held||!rayInput.isActiveAndEnabled||!UIFade.Interactive(rayInput.transform)){EndPointer();return;}
            var rect=(RectTransform)rayInput.transform;var plane=new Plane(rect.forward,rect.position);if(!plane.Raycast(ray,out float d))return;
            var camera=rayPointer.pressEventCamera;if(camera==null)return;rayPointer.position=camera.WorldToScreenPoint(ray.GetPoint(d));
            if(!rayPointer.dragging&&(rayPointer.position-rayPointer.pressPosition).sqrMagnitude>=EventSystem.current.pixelDragThreshold*EventSystem.current.pixelDragThreshold)
            {rayInput.OnBeginDrag(rayPointer);rayPointer.dragging=true;}
            if(rayPointer.dragging)rayInput.OnDrag(rayPointer);
        }
        static void EndPointer()
        {if(rayInput!=null&&rayPointer!=null){if(rayPointer.dragging)rayInput.OnEndDrag(rayPointer);rayInput.OnPointerUp(rayPointer);}rayInput=null;rayPointer=null;}
        void OnDisable(){if(rayInput==field)EndPointer();}
    }
}
