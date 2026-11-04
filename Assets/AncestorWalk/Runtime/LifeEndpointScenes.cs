using System;
using System.Collections.Generic;
using System.Linq;

namespace AncestorWalk
{
    public static class LifeEndpointScenes
    {
        public static IEnumerable<(int Year,string Place)> Targets(Person person,ResidenceTimeline locations)
        {
            bool born=ValidYear(person.BirthDate,out int birth),died=!LifeTimeline.IsLiving(person)&&ValidYear(person.DeathDate,out _);
            ValidYear(person.DeathDate,out int death);
            if(born&&died&&birth>death)yield break;
            var events=new SortedDictionary<int,string>();
            if(born)events[birth]=person.BirthPlace;
            if(died&&(!born||death!=birth||!string.IsNullOrWhiteSpace(person.DeathPlace)))events[death]=person.DeathPlace;
            foreach(var entry in events)
            {
                string place=locations.entries.FirstOrDefault(e=>e.year==entry.Key)?.place??entry.Value;
                place=ResidenceTimeline.Normalize(place);
                if(place.Length>0&&place.Length<=ResidenceTimeline.MaximumLocationLength&&!place.Any(char.IsControl))yield return (entry.Key,place);
            }
        }
        public static bool ValidYear(string date,out int year)=>LifeTimeline.Year(date,out year)&&year>0&&year<=DateTime.Now.Year;
        // Playback can use a known endpoint without claiming a complete lifespan.
        public static bool TrySceneRange(Person person,out int start,out int end)
        {
            if(LifeTimeline.TryRange(person,out start,out end))return true;
            bool birth=ValidYear(person.BirthDate,out int b),death=!LifeTimeline.IsLiving(person)&&ValidYear(person.DeathDate,out _);
            ValidYear(person.DeathDate,out int d);start=end=0;
            if(birth&&death&&b>d)return false;
            if(!birth&&!death)return false;
            start=birth?b:d;end=death?d:b;return true;
        }
    }
}
