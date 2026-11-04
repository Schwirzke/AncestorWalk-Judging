using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
namespace AncestorWalk
{
    public static class LifeTimeline
    {
        static readonly string[] BirthDateFormats={"d MMM yyyy","dd MMM yyyy","yyyy-MM-dd"};
        public static bool IsLiving(Person person)=>IsLiving(person,DateTime.Today);
        public static bool IsLiving(Person person,DateTime today)
        {
            if(person==null)return false;
            if(person.KnownLiving)return true;
            if(!string.IsNullOrWhiteSpace(person.DeathDate)||!Year(person.BirthDate,out int born)||born<=0||born>today.Year)return false;
            today=today.Date;
            if(DateTime.TryParseExact(person.BirthDate.Trim(),BirthDateFormats,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var birth))
                return birth<=today&&birth.AddYears(110)>today;
            return today.Year-born<110;
        }
        public static bool Year(string date,out int year)
        {
            year=0;var matches=Regex.Matches(date??"",@"\b\d{3,4}\b");
            return matches.Count==1 && !Regex.IsMatch(date??"",@"\b(BEF|AFT|BET|FROM|TO)\b",RegexOptions.IgnoreCase) && int.TryParse(matches[0].Value,out year);
        }
        public static bool TryRange(Person p,out int birth,out int end)
        {
            var today=DateTime.Today;end=0;if(!Year(p.BirthDate,out birth)||birth<=0)return false;
            if(IsLiving(p,today))end=today.Year;
            else if(!Year(p.DeathDate,out end))return false;
            return birth<=end && end<=today.Year;
        }
        public static void Pair(int[] years,int selected,out int a,out int b,out float blend)
        {
            a=b=0;blend=0;if(years.Length==0)return;
            a=years.Where(y=>y<=selected).DefaultIfEmpty(years[0]).Max();
            b=years.Where(y=>y>=selected).DefaultIfEmpty(years[years.Length-1]).Min();
            // Between generated-year markers neither image is fully in place.
            // Preserve the middle dissolve, reserving the final 15% for the exact marker.
            float dissolve=Mathf.InverseLerp(.35f,.65f,Mathf.InverseLerp(a,b,selected));
            blend=a==b?0:Mathf.Lerp(.15f,.85f,Mathf.SmoothStep(0,1,dissolve));
        }
    }
    public sealed class YearSlider : MonoBehaviour
    {
        public Action<float> Changed;
        public BoxCollider RailCollider{get;private set;}
        public BoxCollider ThumbCollider{get;private set;}
        Transform thumb;float grabOffset,lastWidth=-1,lastHeight=-1;
        public static YearSlider Resolve(Collider hit)=>hit==null?null:hit.GetComponent<YearSlider>()??hit.GetComponent<YearSliderThumb>()?.Slider;
        public void SetThumb(Transform handle)
        {
            thumb=handle;RailCollider=GetComponent<BoxCollider>();ThumbCollider=handle.GetComponent<BoxCollider>();
            handle.gameObject.AddComponent<YearSliderThumb>().Slider=this;
            UpdateHitArea(Camera.main!=null?Camera.main.transform.position:transform.position);
        }
        public void UpdateHitArea(Vector3 eye)
        {
            if(thumb==null||RailCollider==null||ThumbCollider==null)return;
            var rail=transform.lossyScale;var knob=thumb.lossyScale;float distance=Vector3.Distance(eye,transform.position);
            float width=Mathf.Clamp(distance*.012f,Mathf.Abs(knob.x)*3,Mathf.Abs(rail.x)*.16f);
            float height=Mathf.Clamp(distance*.018f,Mathf.Max(Mathf.Abs(knob.y)*1.8f,Mathf.Abs(rail.y)*2.2f),Mathf.Abs(rail.y)*3.2f);
            if(Mathf.Abs(width-lastWidth)<.001f&&Mathf.Abs(height-lastHeight)<.001f)return;
            lastWidth=width;lastHeight=height;
            // Invisible padding grows modestly with range, bounded within the panel's timeline row.
            RailCollider.size=new Vector3(1+width/Mathf.Abs(rail.x),height/Mathf.Abs(rail.y),1);
            RailCollider.center=Vector3.zero;
            ThumbCollider.size=new Vector3(width/Mathf.Abs(knob.x),height/Mathf.Abs(knob.y),1);
        }
        public void BeginDrag(Ray ray,bool onThumb)
        {
            grabOffset=0;
            if(onThumb&&thumb!=null&&Point(ray,out float value))grabOffset=value-(transform.InverseTransformPoint(thumb.position).x+.5f);
            Drag(ray);
        }
        public void EndDrag(){grabOffset=0;}
        bool Point(Ray ray,out float value)
        {
            var plane=new Plane(transform.forward,transform.position);
            if(plane.Raycast(ray,out float distance)){value=transform.InverseTransformPoint(ray.GetPoint(distance)).x+.5f;return true;}value=0;return false;
        }
        public void Drag(Ray ray)
        {
            if(Point(ray,out float value))Changed?.Invoke(Mathf.Clamp01(value-grabOffset));
        }
    }
    public sealed class YearSliderThumb : MonoBehaviour {public YearSlider Slider;}
}
