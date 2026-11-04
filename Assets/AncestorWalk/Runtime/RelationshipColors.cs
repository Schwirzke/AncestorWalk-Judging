using UnityEngine;

namespace AncestorWalk
{
    public static class RelationshipColors
    {
        public static readonly Color Male=new(.18f,.52f,1f);
        public static readonly Color Female=new(1f,.38f,.67f);
        public static Color For(Person person,Color neutral)=>person?.Sex?.Trim().ToUpperInvariant() switch {"M"=>Male,"F"=>Female,_=>neutral};
        public static string Label(string sex)=>sex?.Trim().ToUpperInvariant() switch {"M"=>"Male","F"=>"Female","X"=>"Other",_=>"Unknown"};
        public static string Next(string sex)=>sex?.Trim().ToUpperInvariant() switch {"M"=>"F","F"=>"X","X"=>"",_=>"M"};
    }
}
