using System;
using System.IO;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class FoundationChecks
    {
        public static string Run()
        {
            int passed = 0;
            void Check(bool ok, string name) { if (!ok) throw new Exception("Check failed: " + name); passed++; }
            var demo = GedcomImporter.Parse(Resources.Load<TextAsset>("DemoFamily").text);
            Check(demo.People.Count>=3 && demo.Families.Count>=1,"sample graph includes original family");
            Check(demo.People["@I1@"].BirthDate=="ABT 1870","preserve uncertain date");
            Check(demo.Families[0].Parents.Count==2 && demo.Families[0].Children[0]=="@I3@","family links");
            Check(demo.Warnings.Count==0,"sample integrity");
            var missing = GedcomImporter.Parse("0 @I1@ INDI\n1 NAME Alice /Example/\n0 @F1@ FAM\n1 CHIL @MISSING@");
            Check(missing.Warnings.Count==1,"dangling reference warning");
            var unicode = GedcomImporter.Parse("\uFEFF0 @I1@ INDI\r\n1 NAME Zoë /Müller/\r\n1 BIRT\r\n2 DATE BET 1900 AND 1902\r\n1 DEAT\r\n2 DATE 1980");
            Check(unicode.People["@I1@"].Name=="Zoë Müller" && unicode.People["@I1@"].BirthDate=="BET 1900 AND 1902","UTF8 BOM CRLF and dates");
            foreach (var invalid in new[]{"not gedcom","0 HEAD\n0 TRLR","0 @I1@ INDI\n0 @I1@ INDI","0 @I1@ INDI\n0 @F1@ FAM\n0 @F1@ FAM"})
            {
                bool rejected=false; try{GedcomImporter.Parse(invalid);}catch(FormatException){rejected=true;}
                Check(rejected,"reject invalid or duplicate record");
            }
            Directory.CreateDirectory("Artifacts");
            string result=$"{passed} importer checks passed at {DateTime.UtcNow:O}";
            File.WriteAllText("Artifacts/importer-checks.txt",result); return result;
        }
    }
}
