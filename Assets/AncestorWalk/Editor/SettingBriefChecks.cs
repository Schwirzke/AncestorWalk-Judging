using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SettingBriefChecks
    {
        public static string Run()
        {
            int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
            var brief=new SceneSettingBrief{version=1,place="Fixture city",date="1972",model="muse-spark-1.3",text="A dense urban street. [1]",createdUtc="2026-10-31T00:00:00Z",sources=new[]{new GuideSource{title="Synthetic archive",url="https://example.org/archive"}}};
            var job=new SceneJob{year=1972,date="1972",place=brief.place,status="generating",stage="researching",settingBrief=brief,prompt="Fixture researched image prompt",model="muse-image-1.0"};
            var round=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(job));
            Check(round.stage=="researching"&&round.settingBrief.text==brief.text,"Research stage and brief survive response serialization");
            Check(round.settingBrief.sources.Single().url==brief.sources[0].url,"Source links survive response serialization");
            var legacy=JsonUtility.FromJson<SceneJob>("{\"year\":1972,\"status\":\"complete\"}");Check((legacy.settingBrief==null||string.IsNullOrEmpty(legacy.settingBrief.text))&&string.IsNullOrEmpty(legacy.stage),"Legacy job responses remain readable");
            var copy=SceneSettingBrief.Copy(brief);copy.sources[0].title="Changed";Check(brief.sources[0].title=="Synthetic archive","Brief snapshots do not share mutable source entries");
            string root=Path.GetFullPath("Library/SettingBriefChecks/"+Guid.NewGuid().ToString("N"));string tree=new string('a',64),person="brief-fixture";
            try
            {
                job.details="Private subject directions";DirectMetaScene.NoteImageRefusal(root,job,brief);
                string feedback=File.ReadAllText(Path.Combine(root,"SettingBriefs",DirectMetaScene.BriefCacheKey(job.place,job.date)+".refusal.json"));
                Check(feedback.Contains("content_policy_violation")&&feedback.Contains("previous_visual_brief")&&!feedback.Contains(job.details),"Direct retry feedback carries only sanitized policy error and visual research");
                var retryInput=DirectMetaScene.ResearchInput(job.place,job.date,brief.text);
                Check((string)retryInput["previous_image_failure"]?["code"]=="content_policy_violation"&&retryInput.Count==3,"Research API receives refusal context internally");
                Check(DirectMetaScene.ResearchInput(job.place,job.date).Count==2,"Ordinary research remains place and year only");
                var png=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");job.sha256=PanoramaCache.Hash(png);
                int width=(png[16]<<24)|(png[17]<<16)|(png[18]<<8)|png[19],height=(png[20]<<24)|(png[21]<<16)|(png[22]<<8)|png[23];
                var saved=PanoramaCache.SaveDecodedAt(root,tree,person,1972,png,job,width,height);
                var directory=Path.Combine(root,"people",tree,PanoramaCache.PersonKey(person),"years","1972");
                var restored=PanoramaCache.ReadManifest(directory,"scene.json");
                Check(restored.settingBrief.model==brief.model&&restored.settingBrief.text==brief.text,"App cache saves researched brief with image provenance");
                Check(restored.settingBrief.sources.Single().title=="Synthetic archive","App cache preserves research sources");
                saved.settingBrief.text="Changed";Check(job.settingBrief.text==brief.text,"Saved record does not alias job brief");
                var indexed=PanoramaCache.YearRecordsAt(root,tree,person).Single();indexed.settingBrief.sources[0].title="Changed";
                Check(PanoramaCache.YearRecordsAt(root,tree,person).Single().settingBrief.sources[0].title=="Synthetic archive","Validated cache index returns independent provenance copies");
                var before=File.ReadAllBytes(Path.Combine(directory,"scene.json"));
                Check(!PanoramaCache.SaveDecodedIfMissingAt(root,tree,person,1972,png,job,width,height),"Automatic work preserves an existing researched image");
                Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"scene.json"))),"Existing image metadata stays byte-for-byte unchanged");
                job.settingBrief=null;PanoramaCache.SaveDecodedAt(root,tree,person,1973,png,job,width,height);
                var old=PanoramaCache.YearRecordsAt(root,tree,person).Single(r=>r.year==1973).settingBrief;Check(old==null||string.IsNullOrEmpty(old.text),"Legacy scenes without research remain valid");
                job.settingBrief=SceneSettingBrief.Copy(brief);job.settingBrief.text=new string('a',5800);job.prompt=new string('b',7000);
                job.settingBrief.sources=Enumerable.Range(0,15).Select(i=>new GuideSource{title="Synthetic source "+i,url="https://example.org/"+new string('c',1900)+i}).ToArray();
                PanoramaCache.SaveDecodedAt(root,tree,person,1974,png,job,width,height);
                string large=Path.Combine(root,"people",tree,PanoramaCache.PersonKey(person),"years","1974");
                Check(File.ReadAllText(Path.Combine(large,"scene.json")).Length>20000&&PanoramaCache.ReadManifest(large,"scene.json").settingBrief.sources.Length==15,"Bounded research provenance can exceed the old 20 KB manifest limit");
                Check(SceneSettingBrief.Copy(null)==null,"Missing optional provenance remains missing");
                string result=checks+" setting-brief provenance checks passed; isolated cache only, no Meta calls.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/setting-brief-checks.txt",result);return result;
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
