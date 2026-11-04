using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class GedcomCompatibilityChecks
    {
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            const string events="0 HEAD\n1 GEDC\n2 VERS 5.5.1\n1 CHAR UTF-8\n0 @I1@ INDI\n1 NAME Zoë /Example/\n1 BIRT\n1 BIRT\n2 DATE Abt. 1800\n2 PLAC First place\n1 BIRT\n2 DATE 1805\n2 PLAC Other place\n1 DEAT\n2 PLAC Final place\n1 DEAT\n2 DATE 1880\n2 PLAC Alternate final place\n1 RESI\n2 DATE 1830\n2 PLAC Elsewhere\n1 OCCU Weaver\n0 @I2@ INDI\n1 NAME Partial /Example/\n1 BIRT\n2 DATE 1900\n1 BIRT\n2 PLAC Later place\n1 DEAT\n1 DEAT\n2 DATE 1980\n0 @I3@ INDI\n1 NAME No dates\n1 BIRT\n2 DATE 10 AUG 1810 or 27 may 1814\n0 TRLR";
            var tree=GedcomImporter.Parse(events.Replace("\n","\r\n"));var first=tree.People["@I1@"];var second=tree.People["@I2@"];
            Check(tree.People.Count==3&&first.Name=="Zoë Example","CRLF 5.5.1 and Unicode names");
            Check(first.BirthDate=="Abt. 1800"&&first.BirthPlace=="First place","Empty event skipped; first nonempty birth kept together");
            Check(first.DeathDate==""&&first.DeathPlace=="Final place","Place-only death does not borrow a later date");
            Check(second.BirthDate=="1900"&&second.BirthPlace=="","Date-only birth does not borrow a later place");
            Check(second.DeathDate=="1980","Empty death falls back to next nonempty event");
            Check(tree.Warnings.Count(x=>x.Contains("alternate BIRT"))==2&&tree.Warnings.Count(x=>x.Contains("alternate DEAT"))==2,"One alternate-event note per person/event kind");
            Check(tree.Warnings.Any(x=>x.Contains("RESI"))&&tree.Warnings.Any(x=>x.Contains("OCCU")),"Unsupported facts reported");
            Check(!LifeTimeline.Year(tree.People["@I3@"].BirthDate,out _),"Ambiguous dates remain verbatim and unselectable");
            const string phrases="0 HEAD\n1 GEDC\n2 VERS 7.0\n0 @P1@ INDI\n1 NAME Phrase /Example/\n1 BIRT\n2 DATE\n3 PHRASE ABT 1820 (or 1823)\n2 PLAC First place\n1 BIRT\n2 DATE 1825\n2 PLAC Later place\n1 DEAT\n2 DATE\n3 PHRASE 10 02 1861\n0 @P2@ INDI\n1 NAME Exact /Example/\n1 BIRT\n2 DATE 1 MAY 1901\n3 PHRASE Early spring\n2 PLAC Known place\n3 PHRASE Local place description\n1 DEAT\n2 PLAC Unknown place\n3 PHRASE Not a date\n0 TRLR";
            var phraseTree=GedcomImporter.Parse(phrases);var phrase=phraseTree.People["@P1@"];var exact=phraseTree.People["@P2@"];
            Check(phrase.BirthDate=="ABT 1820 (or 1823)"&&phrase.BirthPlace=="First place","GEDCOM 7 date phrase forms the first nonempty event without borrowing an alternate date");
            Check(!LifeTimeline.Year(phrase.BirthDate,out _),"Conflicting phrase years do not invent a timeline year");
            Check(phrase.DeathDate=="10 02 1861"&&LifeTimeline.Year(phrase.DeathDate,out int phraseYear)&&phraseYear==1861,"Numeric ambiguous date wording retained with its unambiguous year");
            Check(exact.BirthDate=="1 MAY 1901","Structured date takes precedence over explanatory phrase");
            Check(exact.DeathDate==""&&exact.DeathPlace=="Unknown place","Place phrase cannot become an event date");
            Check(phraseTree.Warnings.Count(w=>w.Contains("date supplied as PHRASE"))==2,"Only displayed phrase fallbacks receive import notes");
            Check(!exact.KnownLiving,"Absent death date does not invent living status");
            const string media="0 HEAD\n1 CHAR UTF-8\n0 @I1@ INDI\n1 NAME Photo /Example/\n1 OBJE @M1@\n1 OBJE @M2@\n2 _PRIM Y\n1 OBJE @M3@\n2 _PRIM Y\n0 @I2@ INDI\n1 NAME Shared /Example/\n1 OBJE @M2@\n1 OBJE\n2 _PRIM Y\n2 FILE images/c.png\n3 TITL Inline portrait\n0 @I3@ INDI\n1 NAME Missing /Example/\n1 OBJE @M1@\n1 OBJE @Missing@\n2 _PRIM Y\n1 OBJE @M3@\n2 _PRIM Y\n0 @I4@ INDI\n1 NAME Fallback /Example/\n1 OBJE @M1@\n1 OBJE @Missing@\n2 _PRIM Y\n0 @M1@ OBJE\n1 FILE images/a.png\n0 @M2@ OBJE\n1 FILE images/b.png\n0 @M3@ OBJE\n1 FILE images/c.png\n0 TRLR";
            var links=GedcomMedia.Parse(media);Check(links.Count==10,"Shared, inline and unresolved associations retained");
            Check(!links[0].IsPrimary&&links[1].IsPrimary&&links[2].IsPrimary&&!links[3].IsPrimary&&links[4].IsPrimary,"Primary preference belongs to person association, not shared media");
            string root=Path.GetFullPath("Library/GedcomCompatibility/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var store=new TreeStore(root);var texture=new Texture2D(8,8);texture.SetPixel(0,0,Color.red);texture.Apply();var red=texture.EncodeToPNG();texture.SetPixel(0,0,Color.blue);texture.Apply();var blue=texture.EncodeToPNG();UnityEngine.Object.DestroyImmediate(texture);
            string archive=Path.Combine(root,"fixture.zip");
            try
            {
                using(var zip=new ZipArchive(File.Create(archive),ZipArchiveMode.Create))
                    foreach(var pair in new[]{("family.ged",Encoding.UTF8.GetBytes(media.Replace("\n","\r\n"))),("images/a.png",red),("images/b.png",blue),("images/c.png",red)})
                    {using var output=zip.CreateEntry(pair.Item1).Open();output.Write(pair.Item2,0,pair.Item2.Length);}
                var staged=TreeArchiveImport.Stage(store,archive);var doc=staged.Document;string a=PanoramaCache.Hash(red),b=PanoramaCache.Hash(blue);
                Check(doc.Photos.Count==2,"Duplicate file bytes share storage");
                Check(doc.People[0].PhotoIds.Count==2&&doc.People[1].PhotoIds.Count==2,"Shared/deduplicated files retain both people's associations");
                Check(doc.People[0].PrimaryPhotoId==b,"First resolved preferred image wins over earlier fallback and later preferred image");
                Check(doc.People[1].PrimaryPhotoId==a,"Inline preferred image honored independently");
                Check(doc.People[2].PrimaryPhotoId==a,"Missing preferred image does not prevent later resolved preference");
                Check(doc.People[3].PrimaryPhotoId==a,"Missing-only preference falls back to first valid attachment");
                Check(doc.Warnings.Count(x=>x.Contains("Missing or ambiguous"))==2,"Missing preferred files remain diagnostic");
                var imported=TreeArchiveImport.Commit(store,staged);File.Delete(archive);var reopened=store.Load(imported.Id);
                Check(reopened.SourceGedcom==media.Replace("\n","\r\n"),"Original source text retained exactly");
                Check(reopened.People[0].PrimaryPhotoId==b&&reopened.Photos.All(p=>File.Exists(Path.Combine(store.MediaRoot,p.File))),"Portrait choice and originals survive reopen without ZIP");
                string result=n+" GEDCOM compatibility checks passed: coherent events, alternatives, unsupported facts, CRLF, Unicode, portrait preference, missing media, shared/deduplicated photos and offline reopen.";
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/gedcom-compatibility-checks.txt",result);return result;
            }
            finally{Directory.Delete(root,true);}
        }
    }
}
