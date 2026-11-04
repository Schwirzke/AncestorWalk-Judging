using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneCompletionChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/scene-completion-checks.txt";
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy)throw new Exception("Ready, idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Completion feedback checks running against read-only cached scenes; no generation requests.";
        }
        static IEnumerator Safe(IEnumerator routine)
        {
            while(true)
            {
                bool next=false;Exception error=null;try{next=routine.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){(routine as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}
                if(!next)yield break;yield return routine.Current;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var p=g.Tree.People.Values.FirstOrDefault(p=>g.AvailableYears(p.Id).Length>=2);
            if(p==null)throw new Exception("This read-only UI check needs a person with two compatible cached years.");
            var other=g.Tree.People.Values.FirstOrDefault(x=>x.Id!=p.Id&&LifeEndpointScenes.TrySceneRange(x,out _,out _));
            var years=g.AvailableYears(p.Id);int first=years.First(),last=years.Last(),originalYear=g.Year(p.Id),otherYear=other==null?0:g.Year(other.Id);
            string selected=g.SelectedId;var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var position=nav.transform.position;var rotation=nav.transform.rotation;var head=nav.view.transform.localRotation;
            var oldAngles=g.TreeRotation;float oldScale=g.TreeScale;bool tutorial=g.Tutorial.IsOpen,hud=g.HudVisible;
            var completed=(Action<string,string,int>)typeof(SceneGeneration).GetField("SceneCompleted",Hidden).GetValue(g.Generation);
            var saved=(Action<string>)typeof(SceneGeneration).GetField("SceneSaved",Hidden).GetValue(g.Generation);
            void Success(string tree,int year){completed.Invoke(tree,p.Id,year);saved.Invoke(p.Id);}
            try
            {
                g.Tutorial.Dismiss();g.Library.Close();g.Batch.Close();g.AISetup.Close();g.LocationEditor.Close();g.Hud.SetVisible(true);g.ShowTree();yield return null;
                g.SetYear(p.Id,first);var preview=g.GetComponentsInChildren<CubePanoramaPreview>(true).Single(x=>x.PersonId==p.Id);
                var controls=preview.GetComponent<CubeFacingControls>();var collider=preview.GetComponentInChildren<SphereCollider>();
                var body=collider.transform;var bodyScale=body.localScale;var bodyBounds=collider.bounds;var panelPosition=controls.Panel.localPosition;var nodeScale=preview.transform.localScale;
                float initial=preview.PreviewMaterial.GetFloat("_CompletionStart");
                Check(!UnityEditor.ShaderUtil.ShaderHasError(preview.PreviewMaterial.shader),"Pulse shader compiles without errors");
                Check(preview.PreviewMaterial.GetFloat("_CompletionAmount")==.06f&&preview.PreviewMaterial.GetFloat("_CompletionDuration")==.65f,"Completion effect has a restrained six-percent, 0.65-second envelope");
                Check(collider.GetComponent<Renderer>().localBounds.size.x>=collider.radius*2*1.059f,"Culling bounds include the visual bulge");
                var eye=nav.view.transform.position;long reads=PanoramaCache.ImageReadCount,hashes=PanoramaCache.ImageValidationCount;
                Success(g.TreeId,last);float until=Time.realtimeSinceStartup+10;while(g.PendingCacheRefreshes>0&&Time.realtimeSinceStartup<until)yield return null;
                Check(g.PendingCacheRefreshes==0&&g.Year(p.Id)==last,"Saved completion selects its exact year after the person index refresh");
                Check(preview.PreviewMaterial.GetFloat("_CompletionStart")>initial,"Successful completion starts the sphere bulge");
                Check(g.IsTree&&nav.view.transform.position==eye,"Completion leaves the camera and tree view in place");
                Check(body.localScale==bodyScale&&preview.transform.localScale==nodeScale&&collider.bounds==bodyBounds&&controls.Panel.localPosition==panelPosition,"Pulse leaves body transforms, collider, node scale and panel position unchanged");
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Completion refresh does not reread or rehash validated PNGs");
                Success(g.TreeId,last);Success(g.TreeId,first);until=Time.realtimeSinceStartup+10;while(g.PendingCacheRefreshes>0&&Time.realtimeSinceStartup<until)yield return null;
                Check(g.Year(p.Id)==first,"Back-to-back completions settle on the last finished year");
                float pulse=preview.PreviewMaterial.GetFloat("_CompletionStart");Success(new string('f',64),last);
                until=Time.realtimeSinceStartup+10;while(g.PendingCacheRefreshes>0&&Time.realtimeSinceStartup<until)yield return null;
                Check(g.Year(p.Id)==first&&preview.PreviewMaterial.GetFloat("_CompletionStart")==pulse,"A completion from another tree cannot jump or pulse the current tree");
                int objects=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;int pool=controls.PoolBuilds;
                yield return new WaitForSecondsRealtime(.8f);
                Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objects&&controls.PoolBuilds==pool,"Pulse creates no scene objects or pooled panel faces");
                Check(Time.timeSinceLevelLoad-pulse>CubePanoramaPreview.CompletionPulseSeconds&&body.localScale==bodyScale,"Pulse expires without changing the baseline scale");
                if(other!=null)
                {
                    g.Select(other.Id);yield return null;eye=nav.view.transform.position;Success(g.TreeId,last);
                    until=Time.realtimeSinceStartup+10;while(g.PendingCacheRefreshes>0&&Time.realtimeSinceStartup<until)yield return null;
                    Check(g.SelectedId==other.Id&&g.Year(other.Id)==otherYear&&g.Year(p.Id)==last&&nav.view.transform.position==eye,"Another person's completion changes only that person's year, not the open scene");
                }
                g.Select(p.Id);yield return null;eye=nav.view.transform.position;Success(g.TreeId,first);
                until=Time.realtimeSinceStartup+10;while(g.PendingCacheRefreshes>0&&Time.realtimeSinceStartup<until)yield return null;yield return null;
                var yearText=(UnityEngine.UI.Text)typeof(SceneOverlay).GetField("yearText",Hidden).GetValue(g.Overlay);
                Check(g.SelectedId==p.Id&&g.Year(p.Id)==first&&yearText.text==first.ToString()&&nav.view.transform.position==eye,"Open scene timeline follows the completed exact year without moving the viewer");
                g.ShowTree();yield return null;Check(g.Year(p.Id)==first,"Returning to the tree retains the completion year");
                File.WriteAllText(Report,n+" completion feedback checks passed: exact-year jump, latest completion, tree identity, other-person isolation, scene timeline, stable geometry/panel/pointer bounds, culling, zero PNG revalidation and no animation objects. Existing cached images were read only; no generation requests.");
            }
            finally
            {
                g.SetYear(p.Id,originalYear);if(other!=null)g.SetYear(other.Id,otherYear);g.SetTreeRotation(oldAngles);g.SetTreeScale(oldScale);
                if(selected==null)g.ShowTree();else g.Select(selected);nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=head;
                g.Hud.SetVisible(hud);if(tutorial)g.Tutorial.Open();
            }
        }
    }
}
