using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AncestorWalk
{
    [Serializable] public sealed class Person
    {
        public bool KnownLiving;
        public string Id, Name = "Unknown", BirthDate = "", BirthPlace = "", DeathDate = "", DeathPlace = "";
        // Null identifies older saved documents that predate retaining GEDCOM SEX.
        public string Sex;
        public List<string> PhotoIds=new();
        public string PrimaryPhotoId;
    }
    public sealed class Family
    {
        public string Id;
        public readonly List<string> Parents = new();
        public readonly List<string> Children = new();
    }
    public sealed class FamilyTree
    {
        public readonly Dictionary<string, Person> People = new();
        public readonly List<Family> Families = new();
        public readonly List<string> Warnings = new();
    }

    // Deliberately bounded GEDCOM subset. Dates remain verbatim: ABT/BET are evidence, not exact years.
    public static class GedcomImporter
    {
        static readonly Regex Line = new(@"^(\d+)\s+(?:(@[^@\s]+@)\s+)?(\S+)(?:\s+(.*))?$", RegexOptions.Compiled);
        static readonly HashSet<string> HandledPersonTags = new(){"NAME","BIRT","DEAT","FAMC","FAMS","OBJE","SEX"};
        public static FamilyTree Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 4_000_000) throw new FormatException("GEDCOM exceeds the 4 MB prototype limit.");
            var tree = new FamilyTree();
            Person person = null; Family family = null; string eventTag = null;
            string eventDate = "", eventDatePhrase = "", eventPlace = "";bool inEventDate=false;
            var selectedEvents = new HashSet<string>();
            var eventCounts = new Dictionary<string, int>();
            var unsupported = new Dictionary<string, int>();
            void FinishEvent()
            {
                if (person == null || eventTag != "BIRT" && eventTag != "DEAT") return;
                string key = person.Id + ":" + eventTag;
                eventCounts.TryGetValue(key, out int count); eventCounts[key] = count + 1;
                if (count == 1) tree.Warnings.Add($"{person.Id} has alternate {eventTag} records. The first nonempty event is displayed; all originals remain in source.ged.");
                string date=string.IsNullOrWhiteSpace(eventDate)?eventDatePhrase:eventDate;
                if (string.IsNullOrWhiteSpace(date) && string.IsNullOrWhiteSpace(eventPlace) || !selectedEvents.Add(key)) return;
                if(string.IsNullOrWhiteSpace(eventDate)&&!string.IsNullOrWhiteSpace(eventDatePhrase))
                    tree.Warnings.Add($"{person.Id} has a {eventTag} date supplied as PHRASE; original wording is displayed.");
                if (eventTag == "BIRT") { person.BirthDate = date; person.BirthPlace = eventPlace; }
                else { person.DeathDate = date; person.DeathPlace = eventPlace; }
            }
            using var reader = new StringReader(text.TrimStart('\uFEFF'));
            string raw; int number = 0;
            while ((raw = reader.ReadLine()) != null)
            {
                number++;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var m = Line.Match(raw);
                if (!m.Success) throw new FormatException($"Invalid GEDCOM line {number}.");
                if (!int.TryParse(m.Groups[1].Value, out int level)) throw new FormatException($"Invalid level at line {number}.");
                string id = m.Groups[2].Value, tag = m.Groups[3].Value, value = m.Groups[4].Value.Trim();
                if(level<=2)inEventDate=false;
                if (level <= 1) { FinishEvent(); eventTag = null; eventDate = eventDatePhrase = eventPlace = ""; }
                if (level == 0)
                {
                    person = null; family = null; eventTag = null;
                    if (tag == "INDI")
                    {
                        if (id.Length == 0 || tree.People.ContainsKey(id)) throw new FormatException($"Missing or duplicate person ID at line {number}.");
                        person = new Person { Id = id, Sex = "" }; tree.People.Add(id, person);
                    }
                    else if (tag == "FAM")
                    {
                        if (id.Length == 0 || tree.Families.Any(f => f.Id == id)) throw new FormatException($"Missing or duplicate family ID at line {number}.");
                        family = new Family { Id = id }; tree.Families.Add(family);
                    }
                    continue;
                }
                if (level == 1)
                {
                    eventTag = tag;
                    if (person != null && tag == "_LIVING") person.KnownLiving = value == "Y";
                    if (person != null && tag == "DEAT") person.KnownLiving = false;
                    if (person != null && tag == "NAME") person.Name = value.Replace("/", "").Trim();
                    if (person != null && tag == "SEX") person.Sex = value;
                    if (person != null && !tag.StartsWith("_", StringComparison.Ordinal) &&
                        !HandledPersonTags.Contains(tag))
                    { unsupported.TryGetValue(tag, out int count); unsupported[tag] = count + 1; }
                    if (family != null)
                    {
                        if (tag == "HUSB" || tag == "WIFE") family.Parents.Add(value);
                        if (tag == "CHIL") family.Children.Add(value);
                    }
                }
                if (person != null && level == 2)
                {
                    if (eventTag == "BIRT" || eventTag == "DEAT")
                    { if (tag == "DATE") {eventDate = value;eventDatePhrase="";inEventDate=true;} if (tag == "PLAC") eventPlace = value; }
                }
                if(person!=null&&level==3&&inEventDate&&tag=="PHRASE")eventDatePhrase=value;
            }
            FinishEvent();
            foreach (var pair in unsupported.OrderBy(p => p.Key, StringComparer.Ordinal))
                tree.Warnings.Add($"{pair.Value} {pair.Key} fact(s) retained in source.ged; not imported into editable fields or location timelines.");
            foreach (var f in tree.Families)
                foreach (var id in f.Parents.Concat(f.Children))
                    if (!tree.People.ContainsKey(id)) tree.Warnings.Add($"{f.Id} references missing person {id}.");
            if (tree.People.Count == 0) throw new FormatException("No individuals found in GEDCOM.");
            return tree;
        }
    }
}
