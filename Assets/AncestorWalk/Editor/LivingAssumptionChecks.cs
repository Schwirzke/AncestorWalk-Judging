using System;
using System.Linq;

namespace AncestorWalk.Editor
{
    public static class LivingAssumptionChecks
    {
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var today=new DateTime(2026,10,31);
            var p=new Person{BirthDate="1917",DeathDate="",BirthPlace="Example city"};
            Check(LifeTimeline.IsLiving(p,today),"Age 109 is assumed living");
            Check(!p.KnownLiving&&p.DeathDate=="","Computed assumption does not change stored facts");
            p.BirthDate="1916";Check(!LifeTimeline.IsLiving(p,today),"Year-only age 110 is not assumed living");
            p.BirthDate="1 NOV 1916";Check(LifeTimeline.IsLiving(p,today),"Still living before the exact 110th birthday");
            p.BirthDate="31 OCT 1916";Check(!LifeTimeline.IsLiving(p,today),"Exact 110th birthday is excluded");
            p.BirthDate="1916-11-01";Check(LifeTimeline.IsLiving(p,today),"ISO full dates use the birthday cutoff");
            p.BirthDate="2000";p.DeathDate="2020";Check(!LifeTimeline.IsLiving(p,today),"Recorded death prevents assumption");
            p.DeathDate="Unknown";Check(!LifeTimeline.IsLiving(p,today),"Nonempty death text is retained without assuming living");
            p.DeathDate="";p.BirthDate="";Check(!LifeTimeline.IsLiving(p,today),"Missing birth is not guessed");
            p.BirthDate="BET 1950 AND 1955";Check(!LifeTimeline.IsLiving(p,today),"Ambiguous birth is not guessed");
            p.BirthDate="2027";Check(!LifeTimeline.IsLiving(p,today),"Future birth year is excluded");
            p.BirthDate="1 NOV 2026";Check(!LifeTimeline.IsLiving(p,today),"Future full birth date is excluded");
            p.BirthDate="";p.KnownLiving=true;Check(LifeTimeline.IsLiving(p,today),"Explicit living remains supported");
            p.KnownLiving=false;p.BirthDate=(DateTime.Today.Year-40).ToString();
            Check(LifeTimeline.TryRange(p,out int start,out int end)&&start==DateTime.Today.Year-40&&end==DateTime.Today.Year,"Assumed-living timeline ends at current year");
            Check(LifeEndpointScenes.TrySceneRange(p,out _,out end)&&end==DateTime.Today.Year,"Scene timeline shares the living range");
            Check(LifeEndpointScenes.Targets(p,new ResidenceTimeline()).Select(t=>t.Year).SequenceEqual(new[]{start}),"Automatic generation schedules recorded birth only");
            Check(!TimelineMarker.Build(p,new ResidenceTimeline(),Array.Empty<int>()).Any(m=>m.MissingLocation),"No invented death-location warning");
            Check(!p.KnownLiving&&p.DeathDate=="","Timeline and generation checks leave source flags unchanged");
            return n+" living-assumption checks passed.";
        }
    }
}
