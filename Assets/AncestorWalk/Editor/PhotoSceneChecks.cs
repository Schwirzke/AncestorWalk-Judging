using InputField = TMPro.TMP_InputField;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class PhotoSceneChecks
    {
        const string Report="Artifacts/photo-scene-checks.txt";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
        public static string Run(bool includeUI=true)
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||g.Generation.ActiveCount>0)throw new Exception("Idle Play mode required");
            checks=0;File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g,includeUI)));return "Photo scene checks started; fixture API only";
        }
        static IEnumerator Safe(IEnumerator routine)
        {
            var run=MetaApi.Flatten(routine);
            try{while(true){bool more;try{more=run.MoveNext();}catch(Exception e){File.WriteAllText(Report,"FAILED: "+e);yield break;}if(!more)break;yield return run.Current;}}
            finally{(run as IDisposable)?.Dispose();}
        }
        static IEnumerator Checks(AncestorGallery g,bool includeUI)
        {
            string folder=Path.GetFullPath("Artifacts/PhotoSceneChecks/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"Photos"));
            string key=(string)typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var reply=MetaApi.TestReply;bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false),hud=g.HudVisible;string selected=g.SelectedId;
            int requests=0,researchRequests=0;JObject payload=null;string endpoint=null;GameObject queuedObject=null;
            var texture=new Texture2D(64,96,TextureFormat.RGB24,false);byte[] photoPng=texture.EncodeToPNG(),photoJpg=texture.EncodeToJPG();UnityEngine.Object.Destroy(texture);
            texture=new Texture2D(256,128,TextureFormat.RGB24,false);byte[] panorama=texture.EncodeToPNG();UnityEngine.Object.Destroy(texture);
            try
            {
                SessionState.SetBool("AncestorWalk.EndpointChecks",true);MetaApi.UseKey("fixture-photo-scene-key");
                MetaApi.TestReply=(path,body)=>{var sent=JObject.Parse(Encoding.UTF8.GetString(body));if(path=="responses"){researchRequests++;var context=JObject.Parse((string)sent["input"]);Check(context.Properties().Count()==2&&context["place"]!=null&&context["year"]!=null,"Research receives only place and year");return new MetaReply{Http=200,Json=(JObject)typeof(DirectMetaChecks).GetMethod("Research",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"An ordinary urban street in Munich in 1972."})};}requests++;endpoint=path;payload=sent;return new MetaReply{Http=200,Json=new JObject{["data"]=new JArray(new JObject{["b64_json"]=Convert.ToBase64String(panorama)})}};};
                foreach(bool jpeg in new[]{false,true})
                {
                    byte[] bytes=jpeg?photoJpg:photoPng;string sha=PanoramaCache.Hash(bytes),file=sha+(jpeg?".jpg":".png");File.WriteAllBytes(Path.Combine(folder,"Photos",file),bytes);
                    var photo=new PhotoRecord{Id=sha,File=file,Title="Fictional subject photo",Width=64,Height=96};var reference=SceneReference.FromPhoto(photo,1972,"Munich, Germany");
                    Check(reference.ContentType==(jpeg?"image/jpeg":"image/png"),"Correct photo MIME");Check(SceneReference.Same(reference,SceneReference.Copy(reference)),"Copy preserves photo identity");
                    ScenePhotoChoices.Save(folder,new string('a',64),"fixture",1972,reference);
                    Check(SceneReference.Same(ScenePhotoChoices.Load(folder,new string('a',64),"fixture",1972,reference.place),reference),"Photo choice persists independently of request success");
                    Check(ScenePhotoChoices.Load(folder,new string('a',64),"fixture",1973,reference.place)==null,"Photo selection stays scoped to exact year");
                    ScenePhotoChoices.Save(folder,new string('a',64),"fixture",1972,null);Check(ScenePhotoChoices.Load(folder,new string('a',64),"fixture",1972,reference.place)==null,"Unchecking clears remembered photo");
                    var job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=new string('a',64),personKey=PanoramaCache.PersonKey("fixture"),year=1972,date="1972",place="Munich, Germany",reference=reference,referenceResolved=true};
                    job.details="Place the photo subject beside a blue car.";
                    SceneYearDetails.Save(folder,job.treeId,"fixture",1972,job.details);Check(SceneYearDetails.Load(folder,job.treeId,"fixture",1972)==job.details,"Scene details persist");Check(SceneYearDetails.Load(folder,job.treeId,"fixture",1973)=="","Details stay scoped to exact year");
                    var pending=new PendingScene{treeId=job.treeId,personId="fixture",job=job,phase=1};SceneQueueState.Validate(pending);
                    var retry=SceneRetryPolicy.Next(pending,new SceneJob{status="failed",retryable=true},SceneRetryPolicy.UnixNow);Check(SceneReference.Same(retry.job.reference,reference),"Retry retains photo identity");
                    var brief=new SceneSettingBrief{version=1,place=job.place,date=job.date,model=MetaApi.Spark,text="An ordinary urban street in Munich in 1972.",sources=new[]{new GuideSource{title="Fixture archive",url="https://example.org/archive"}},createdUtc=DateTime.UtcNow.ToString("o")};
                    Directory.CreateDirectory(Path.Combine(folder,"SettingBriefs"));File.WriteAllText(Path.Combine(folder,"SettingBriefs",DirectMetaScene.BriefCacheKey(job.place,job.date)+".json"),JsonUtility.ToJson(brief));
                    if(!jpeg)File.Delete(Path.Combine(folder,"SettingBriefs",DirectMetaScene.BriefCacheKey(job.place,job.date)+".json"));
                    var result=new DirectSceneResult();yield return DirectMetaScene.GenerateFor("fixture",folder,Path.Combine(folder,"Panoramas"),job,result,s=>{},r=>{});
                    Check(result.Job.status=="complete","Photo generation completes");Check(endpoint=="images/edits","Uses Meta edit endpoint");
                    Check(((string)payload["images"][0]["image_url"]).StartsWith("data:"+reference.ContentType+";base64,"),"Original selected photo uploaded");
                    Check(((string)payload["prompt"]).Contains("Incorporate the people or objects")&&!((string)payload["prompt"]).Contains("EDIT THE ATTACHED PANORAMA"),"Photo is a subject reference");
                    Check(((string)payload["prompt"]).Contains(job.details)&&payload["details"]==null,"Details are image-prompt instructions only");Check(retry.job.details==job.details,"Retry preserves details snapshot");
                    var recovered=DirectMetaScene.Recover(folder,result.Job);Check(SceneReference.Same(recovered.Job.reference,reference),"Recovery preserves photo identity");
                }
                Check(requests==2,"Exactly two fixture image calls");
                string inputHash=PanoramaCache.Hash(photoJpg);var queuedPhoto=new PhotoRecord{Id=inputHash,File=inputHash+".jpg"};var queuedReference=SceneReference.FromPhoto(queuedPhoto,1972,"Munich, Germany");
                queuedObject=new GameObject("Isolated photo queue fixture");queuedObject.SetActive(false);var queue=queuedObject.AddComponent<SceneGeneration>();typeof(SceneGeneration).GetField("TestStorage",Private).SetValue(queue,folder);queuedObject.SetActive(true);
                float deadline=Time.realtimeSinceStartup+15;while(!queue.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
                var fictionalPerson=new Person{Id="fixture",BirthDate="1972",DeathDate="1972"};
                SceneYearDetails.Save(folder,new string('c',64),"fixture",1972,"A green car in the parking lot.");
                queue.Enqueue(new string('c',64),fictionalPerson,true,new[]{(1972,"Munich, Germany")},queuedReference);
                while(queue.CompletedCount==0&&queue.FailedCount==0&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(queue.CompletedCount==1&&queue.FailedCount==0,"Queued photo job completes with independent-year references disabled");
                var record=PanoramaCache.YearRecordsAt(Path.Combine(folder,"Panoramas"),new string('c',64),"fixture").Single();
                Check(record.reference.IsPhoto&&record.reference.sha256==inputHash,"Saved queued scene retains selected photo provenance");Check(requests==3,"Queue performs exactly one fixture edit request");
                Check(record.details=="A green car in the parking lot.","Queued scene stores exact-year details provenance");
                Check(PanoramaCache.Hash(File.ReadAllBytes(Path.Combine(folder,"Photos",queuedPhoto.File)))==inputHash,"Source photo unchanged");
                ScenePhotoChoices.Save(folder,new string('c',64),"fixture",1972,queuedReference);queue.Generate(new string('c',64),fictionalPerson,true,1972,"Munich, Germany");
                while(queue.CompletedCount<2&&queue.FailedCount==0&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(queue.CompletedCount==2&&requests==4,"Regenerate reuses photo after original request state is cleared");Check(researchRequests==1,"Unchanged location/year reuses research across details, photo formats and regeneration");
                var inserted=PanoramaCache.YearRecordsAt(Path.Combine(folder,"Panoramas"),new string('c',64),"fixture").Single();
                Check(inserted.generationMode=="photo-insert"&&inserted.basePanorama.sha256==record.sha256,"Cached exact-year scene automatically becomes the photo-edit base");
                Check(inserted.horizonLift==record.horizonLift&&inserted.settingBrief.text==record.settingBrief.text,"Insertion preserves horizon and original research provenance");
                Check(((JArray)payload["images"]).Count==2&&((string)payload["images"][0]["image_url"]).StartsWith("data:image/png;base64,"),"Panorama is first and portrait is second");
                Check(Convert.FromBase64String(((string)payload["images"][0]["image_url"]).Split(',')[1]).SequenceEqual(panorama)&&Convert.FromBase64String(((string)payload["images"][1]["image_url"]).Split(',')[1]).SequenceEqual(photoJpg),"Both edit inputs retain their exact bytes");
                Check(inserted.prompt.Length<1600&&!inserted.prompt.Contains("TARGET YEAR")&&inserted.prompt.Contains("image 2"),"Insertion uses a short prompt without historical research");
                Check(SceneReference.ExactSceneAt(Path.Combine(folder,"Panoramas"),new string('c',64),"fixture",1973,"Munich, Germany")==null&&SceneReference.ExactSceneAt(Path.Combine(folder,"Panoramas"),new string('c',64),"fixture",1972,"Paris")==null&&SceneReference.ExactSceneAt(Path.Combine(folder,"Panoramas"),new string('c',64),"other",1972,"Munich, Germany")==null,"Base lookup rejects other years, locations and people");
                var frozen=new PendingScene{treeId=new string('c',64),personId="fixture",phase=1,job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=new string('c',64),personKey=PanoramaCache.PersonKey("fixture"),year=1972,date="1972",place="Munich, Germany",reference=SceneReference.Copy(queuedReference),referenceResolved=true,basePanorama=SceneReference.Copy(inserted.basePanorama),basePanoramaResolved=true,settingBrief=SceneSettingBrief.Copy(inserted.settingBrief),details="Place him beside the path.",transport=DirectMetaScene.Transport}};
                SceneQueueState.Validate(frozen);var restored=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(frozen));SceneQueueState.Validate(restored);
                Check(SceneReference.Same(restored.job.basePanorama,frozen.job.basePanorama)&&SceneReference.Same(restored.job.reference,frozen.job.reference),"Checkpoint round-trip preserves both references");
                var insertRetry=SceneRetryPolicy.Next(frozen,new SceneJob{status="failed",retryable=true},SceneRetryPolicy.UnixNow);Check(insertRetry.job.basePanoramaResolved&&SceneReference.Same(insertRetry.job.basePanorama,frozen.job.basePanorama)&&insertRetry.job.settingBrief.text==frozen.job.settingBrief.text,"Retry preserves frozen base and provenance");
                insertRetry.job.basePanorama.sha256=new string('e',64);Check(frozen.job.basePanorama.sha256!=insertRetry.job.basePanorama.sha256,"Retry reference copies are independent");
                var legacy=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(frozen));legacy.phase=2;legacy.job.basePanorama=null;legacy.job.basePanoramaResolved=false;SceneQueueState.Validate(legacy,true);Check(legacy.job.basePanorama==null,"Submitted single-photo checkpoints remain unchanged");
                var heldReply=MetaApi.TestReply;int refused=0;MetaApi.TestReply=(path,body)=>{refused++;return new MetaReply{Http=400,Error="Meta HTTP 400. code: content_policy_violation."};};
                var refusedResult=new DirectSceneResult();yield return DirectMetaScene.GenerateFor("fixture",folder,Path.Combine(folder,"Panoramas"),frozen.job,refusedResult,_=>{},_=>{});MetaApi.TestReply=heldReply;
                Check(refused==1&&refusedResult.Job.status=="failed"&&!refusedResult.Job.retryable,"Transport returns one confirmed refusal; queue owns any bounded prompt retry");
                Check(PanoramaCache.ImageShaAt(Path.Combine(folder,"Panoramas"),new string('c',64),"fixture",1972)==record.sha256,"Failed insertion leaves cached scene unchanged");
                string staging=DirectMetaScene.Staging(folder,frozen.job.requestId);Directory.CreateDirectory(staging);var completed=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(frozen.job));completed.status="complete";completed.model=MetaApi.Image;completed.sha256=PanoramaCache.Hash(panorama);completed.prompt=DirectMetaScene.PhotoInsertPrompt(completed.details);
                File.WriteAllText(Path.Combine(staging,"result.json"),JsonUtility.ToJson(completed));File.WriteAllBytes(Path.Combine(staging,"image.png"),panorama);
                Check(SceneReference.Same(DirectMetaScene.Recover(folder,frozen.job).Job.basePanorama,frozen.job.basePanorama),"Direct recovery verifies the frozen base");
                var wrongBase=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(frozen.job));wrongBase.basePanorama.sha256=new string('e',64);bool rejected=false;try{DirectMetaScene.Recover(folder,wrongBase);}catch(InvalidDataException){rejected=true;}Check(rejected,"Recovery rejects a different base without submitting another request");
                string safeError=MetaApi.DetailedError(400,Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"content_policy_violation\",\"param\":\"prompt\",\"message\":\"private input\"}}"));
                Check(safeError.Contains("content_policy_violation")&&!safeError.Contains("private input"),"Direct errors retain safe code only");
                if(!includeUI){File.WriteAllText(Report,checks+" photo-edit transport, cache, retry and recovery checks passed; no external requests or family writes.");yield break;}
                string id=selected??g.Tree.People.Keys.First();g.Select(id);yield return null;var ui=g.Overlay;
                g.LocationEditor.Open(g,g.Tree.People[id],g.Year(id));
                var locationField=(TMPro.TMP_InputField)typeof(LocationEditor).GetField("nativeInput",Private).GetValue(g.LocationEditor);var detailsField=(TMPro.TMP_InputField)typeof(LocationEditor).GetField("detailsInput",Private).GetValue(g.LocationEditor);
                Check(locationField!=detailsField&&locationField.lineType==TMPro.TMP_InputField.LineType.SingleLine&&detailsField.lineType==TMPro.TMP_InputField.LineType.MultiLineNewline,"Separate native location and multiline details fields");
                string locationBefore=locationField.text;detailsField.text="Add a parked blue car.";Check(locationField.text==locationBefore,"Scene details do not alter location text");
                g.LocationEditor.Activate("focus-details");yield return null;g.LocationEditor.Activate("space");Check(detailsField.text!= "Add a parked blue car."&&locationField.text==locationBefore,"Virtual keyboard targets scene details independently");g.LocationEditor.Close();yield return null;
                g.Hud.SetVisible(false);g.LocationEditor.Open(g,g.Tree.People[id],g.Year(id));g.LocationEditor.Activate("photo-toggle");
                Check(g.Library.IsOpen,"Checkbox opens photo picker");Check(requests==4,"Choosing a photo issues no API request");g.Library.Close();
                Check(g.LocationEditor.IsOpen&&g.LocationEditor.transform.Find("Location editor").gameObject.activeSelf,"Picker cancellation restores editor");
                g.LocationEditor.Activate("photo-toggle");
                typeof(TreeLibraryOverlay).GetMethod("CompleteScenePhoto",Private).Invoke(g.Library,new object[]{new PhotoRecord{Id=new string('b',64),File=new string('b',64)+".png",Title="Fictional selected photo"}});
                Check((bool)typeof(LocationEditor).GetField("includePhoto",Private).GetValue(g.LocationEditor),"Selection checks include-photo option");
                var returnCanvas=(Canvas)typeof(SceneOverlay).GetField("returnCanvas",Private).GetValue(ui);Check(returnCanvas.enabled&&returnCanvas.gameObject.activeInHierarchy,"Return visible with HUD off and location editor open");
                var update=typeof(SceneOverlay).GetMethod("UpdateLocationLabels",Private);var weights=(System.Collections.Generic.Dictionary<string,float>)typeof(SceneOverlay).GetField("placeWeights",Private).GetValue(ui);
                foreach(bool vr in new[]{false,true})
                {
                    typeof(SceneOverlay).GetMethod("ConfigureForMode",Private).Invoke(ui,new object[]{vr});weights.Clear();string longPlace=string.Join(" ",Enumerable.Repeat("A long regional location with description",15));weights[longPlace]=.5f;weights["Another town"]=.5f;update.Invoke(ui,null);
                    var label=((Text[])typeof(SceneOverlay).GetField("placeLabels",Private).GetValue(ui))[0];Check(label.text.EndsWith("…")&&!label.text.Contains("\n"),"Long location ellipsized");
                    var measure=new TextGenerator();Check(measure.GetPreferredWidth(label.text,label.GetGenerationSettings(Vector2.zero))/label.pixelsPerUnit<=label.rectTransform.rect.width+.1f,"Ellipsis fits desktop/VR column");
                }
                g.LocationEditor.Close();typeof(SceneOverlay).GetMethod("ConfigureForMode",Private).Invoke(ui,new object[]{false});
                File.WriteAllText(Report,checks+" photo scene and UI checks passed; PNG/JPEG direct fixtures, subjects prompt, immutable retry/recovery, optional picker, independent Return, bounded ellipses. No external calls or family writes.");
            }
            finally
            {
                if(queuedObject!=null)UnityEngine.Object.Destroy(queuedObject);g.Library.Close();g.LocationEditor.Close();MetaApi.TestReply=reply;if(string.IsNullOrEmpty(key))MetaApi.ForgetKey();else MetaApi.UseKey(key);SessionState.SetBool("AncestorWalk.EndpointChecks",guard);g.Hud.SetVisible(hud);if(selected==null)g.ShowTree();else g.Select(selected);
            }
        }
    }
}
