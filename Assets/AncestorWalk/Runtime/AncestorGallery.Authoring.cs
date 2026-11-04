using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace AncestorWalk
{
    public sealed partial class AncestorGallery
    {
        Material ghostMaterial;
        public bool BuildMode=>Document!=null&&!ProductDemoAuthoring.ReadOnly(Document)&&PlayerPrefs.GetInt(TreeAuthoring.PreferenceKey(TreeId),0)!=0;
        public void SetBuildMode(bool enabled)
        {
            if(Document==null||ProductDemoAuthoring.ReadOnly(Document)||!IsTree||Library!=null&&Library.Busy)return;
            PlayerPrefs.SetInt(TreeAuthoring.PreferenceKey(TreeId),enabled?1:0);PlayerPrefs.Save();ShowTree();
        }
        void BuildGhostSpheres()
        {
            if(Layout.Ghosts.Count==0)return;
            foreach(var slot in Layout.Ghosts)BuildGhostSphere(slot);
            if(Library!=null&&Library.IsOpen)SetLocationModal(true);
        }
        void BuildGhostSphere(GhostSlot slot)
        {
            if(ghostMaterial==null)ghostMaterial=new Material(Resources.Load<Shader>("Shaders/GhostSphere"));
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=slot.IsRoot?"Ghost first person":"Ghost parent";sphere.transform.SetParent(treeRoot,false);sphere.transform.localPosition=Layout.GhostPositions[slot.Id];sphere.transform.localScale=Vector3.one*TreeLayout.Size;
            sphere.AddComponent<GhostSphereTarget>().Build(this,slot,ghostMaterial);
        }
        // A narrow additive/person-details path: preserve indexes, refresh epochs and active jobs.
        public void ValidateAuthoringDocument(TreeDocument document,string detailsId=null)
        {
            if(SelectedId!=null||IsBuildingTree)throw new InvalidOperationException("Return to the ready tree before adding relatives.");
            if(Generation.CancellationNeedsSaving)throw new InvalidOperationException("Wait for the stopped-job checkpoint to finish.");
            TreeAuthoring.ValidateChange(Document,document,detailsId);
            if(detailsId!=null&&(Generation.ActiveJobs.Any(p=>p.treeId==TreeId&&p.personId==detailsId)||Generation.Waiting.Any(p=>p.treeId==TreeId&&p.personId==detailsId)))throw new InvalidOperationException("Finish this person's queued scenes before changing their details.");
        }
        public void ApplyAuthoringDocument(TreeDocument document,string detailsId=null)
        {
            ValidateAuthoringDocument(document,detailsId);
            var previous=Tree.People.Keys.ToHashSet();Document=document;if(document.Id==DemoDocument?.Id)DemoDocument=document;Tree=document.ToTree();
            foreach(var person in Tree.People.Values)
            {
                if(!previous.Contains(person.Id)){locations[person.Id]=ResidenceTimeline.Load(TreeId,person);validatedRecords[person.Id]=Array.Empty<PanoramaRecord>();ApplyRecords(person.Id,Array.Empty<PanoramaRecord>());}
                else if(person.Id==detailsId)ApplyRecords(person.Id,validatedRecords.TryGetValue(person.Id,out var records)?records:Array.Empty<PanoramaRecord>());
            }
            ShowTree();
            if(detailsId!=null)StartCoroutine(QueueEndpointsWhenReady(treeEpoch,detailsId));
        }
    }
}
