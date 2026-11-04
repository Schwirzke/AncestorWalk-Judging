using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace AncestorWalk
{
    public enum TimelineSelectionState { Unmarked, PendingMove, Generated }
    public readonly struct TimelineMarker
    {
        public readonly int Year;
        public readonly bool Generated;
        public readonly bool LocationMove;
        public readonly bool Generating;
        public readonly bool MissingLocation;
        public readonly bool Queued;
        public bool Blinking=>Generating||Queued||MissingLocation&&!Generated;
        public static readonly Color PendingColor=new Color(.74f,.5f,.95f);
        public static readonly Color QueuedColor=new Color(.25f,.6f,1f);
        public static readonly Color GeneratingColor=new Color(.15f,1f,.3f);
        public static readonly Color MissingLocationColor=new Color(1f,.15f,.12f);
        public static float BlinkAlpha(float time)=>Mathf.Repeat(time,1)<.5f?1:.2f;
        public TimelineMarker(int year,bool generated,bool locationMove=false,bool generating=false,bool missingLocation=false,bool queued=false){Queued=queued;Year=year;Generated=generated;LocationMove=locationMove;Generating=generating;MissingLocation=missingLocation;}
        public string Tooltip(string location)
        {
            string state=Generating?(Generated?"Regenerating":"Generating"):Queued?(Generated?"Queued for regeneration":"Queued"):Generated?"":MissingLocation?"Missing location":LocationMove?"Saved location · not generated":"Not generated";
            return Year+(state.Length>0?" · "+state:"")+"\n"+(string.IsNullOrWhiteSpace(location)?"Location not set":location);
        }
        public static int CoveredMarker(int year,TimelineMarker[] markers,int birth,int end,float railWidth,float thumbWidth,float markerWidth)
        {
            int found=-1;float closest=float.PositiveInfinity;
            for(int i=0;i<markers.Length;i++)
            {
                float distance=Mathf.Abs((float)markers[i].Year-year)*railWidth/Mathf.Max(1,end-birth);
                if(distance<closest&&distance<=(thumbWidth+markerWidth)*.5f){closest=distance;found=i;}
            }
            return found;
        }
        public Color DisplayColor(Color generated)=>Generating?GeneratingColor:Queued?QueuedColor:Generated?generated:MissingLocation?MissingLocationColor:PendingColor;
        public static TimelineSelectionState SelectionState(int year,TimelineMarker[] markers)
        {
            foreach(var marker in markers)if(marker.Year==year)
                return marker.Generated?TimelineSelectionState.Generated:(marker.Generating||marker.Queued||marker.MissingLocation)&&!marker.LocationMove?TimelineSelectionState.Unmarked:TimelineSelectionState.PendingMove;
            return TimelineSelectionState.Unmarked;
        }
        public static Color ThumbColor(int year,TimelineMarker[] markers,Color generated,Color unmarked)
            =>SelectionState(year,markers) switch {TimelineSelectionState.Generated=>generated,TimelineSelectionState.PendingMove=>PendingColor,_=>unmarked};
        // Built only when this person's compatible scenes or saved moves change.
        public static TimelineMarker[] Build(Person person,ResidenceTimeline locations,int[] years,IEnumerable<int> generating=null,IEnumerable<int> queued=null)
        {
            if(!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end))return Array.Empty<TimelineMarker>();
            var markers=new SortedDictionary<int,bool>();
            foreach(int year in years)if(year>=birth&&year<=end)markers[year]=true;
            var moves=locations.entries.Where(e=>e.year>=birth&&e.year<=end).OrderBy(e=>e.year).ToArray();
            foreach(var move in moves)if(!markers.ContainsKey(move.year))markers[move.year]=false;
            var active=new HashSet<int>(generating??Array.Empty<int>());foreach(int year in active)if(year>=birth&&year<=end&&!markers.ContainsKey(year))markers[year]=false;
            var waiting=new HashSet<int>(queued??Array.Empty<int>());waiting.ExceptWith(active);foreach(int year in waiting)if(year>=birth&&year<=end&&!markers.ContainsKey(year))markers[year]=false;
            var missing=new HashSet<int>();
            void Endpoint(string date,string recordedPlace)
            {
                if(!LifeEndpointScenes.ValidYear(date,out int year)||year<birth||year>end)return;
                string place=locations.entries.FirstOrDefault(e=>e.year==year)?.place??recordedPlace;
                if(!string.IsNullOrWhiteSpace(place))return;
                missing.Add(year);if(!markers.ContainsKey(year))markers[year]=false;
            }
            Endpoint(person.BirthDate,person.BirthPlace);
            if(!LifeTimeline.IsLiving(person))Endpoint(person.DeathDate,person.DeathPlace);
            return markers.Select(m=>new TimelineMarker(m.Key,m.Value,moves.Any(move=>move.year==m.Key),active.Contains(m.Key),missing.Contains(m.Key),waiting.Contains(m.Key))).ToArray();
        }
        public static bool Same(TimelineMarker[] a,TimelineMarker[] b)
        {if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i].Year!=b[i].Year||a[i].Generated!=b[i].Generated||a[i].LocationMove!=b[i].LocationMove||a[i].Generating!=b[i].Generating||a[i].Queued!=b[i].Queued||a[i].MissingLocation!=b[i].MissingLocation)return false;return true;}
    }
}
