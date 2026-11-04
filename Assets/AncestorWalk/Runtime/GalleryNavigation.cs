using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using System.Linq;

namespace AncestorWalk
{
    public sealed class GalleryNavigation : MonoBehaviour
    {
        public Camera view;
        public Transform rightHand;
        public LineRenderer pointer;
        public AncestorGallery gallery;
        InputAction handPosition, handRotation, trigger, turn, back, pan,focusNode,nextNode,previousNode,hudMenu,rightGrip;
        public string FocusedPersonId {get;private set;}
        CubeFacingControls pointedControls;
        public string PointedPersonId {get;private set;}
        float pitch;
        bool startupHomeApplied,trackedHomeApplied;int trackedPoseFrames;
        public const float SmoothTurnDegreesPerSecond=90;
        Vector3 savedTreePosition;Quaternion savedTreeRotation,savedViewRotation;float savedPitch;bool treeViewSaved;
        YearSlider dragging;
        public bool IsDraggingYear => dragging != null;
        NavigationPreferences preferences;
        void Awake() { preferences = gameObject.AddComponent<NavigationPreferences>(); }
        InputAction Action(string name, string binding, InputActionType type = InputActionType.Value)
        { var action = new InputAction(name, type, binding); action.Enable(); return action; }
        void OnEnable()
        {
            handPosition = Action("Aim position", "<XRController>{RightHand}/pointerPosition");
            handRotation = Action("Aim rotation", "<XRController>{RightHand}/pointerRotation");
            trigger = Action("Select", "<XRController>{RightHand}/triggerPressed", InputActionType.Button);
            pan = Action("Tree movement", "<XRController>{LeftHand}/thumbstick");
            turn = Action("Turn", "<XRController>{RightHand}/thumbstick");
            rightGrip=Action("Scale tree grip","<XRController>{RightHand}/gripPressed",InputActionType.Button);
            back = Action("Back", "<XRController>{RightHand}/secondaryButton", InputActionType.Button);
            focusNode=Action("Nearest node","<XRController>{LeftHand}/primaryButton",InputActionType.Button);
            previousNode=Action("Previous node","<XRController>{LeftHand}/secondaryButton",InputActionType.Button);
            nextNode=Action("Next node","<XRController>{RightHand}/primaryButton",InputActionType.Button);
            hudMenu=Action("Toggle HUD","<XRController>{LeftHand}/menuButton",InputActionType.Button);
        }
        void OnDisable()
        { handPosition?.Dispose(); handRotation?.Dispose(); trigger?.Dispose(); turn?.Dispose(); back?.Dispose(); pan?.Dispose();focusNode?.Dispose();nextNode?.Dispose();previousNode?.Dispose();hudMenu?.Dispose();rightGrip?.Dispose(); }
        void LateUpdate()
        {
            if(gallery==null||view==null||!gallery.IsTree||gallery.IsBuildingTree||HomeNode()==null)return;
            bool vr=XRSettings.isDeviceActive;
            if(vr)
            {
                if(trackedHomeApplied)return;
                var head=InputSystem.GetDevice<XRHMD>();
                if(head==null||!head.isTracked.isPressed){trackedPoseFrames=0;return;}
                // Let the tracked pose driver apply the initial headset pose before framing.
                if(++trackedPoseFrames<2)return;
            }
            else if(startupHomeApplied)return;
            ResetTreeView();ApplyNodeFrame(HomeNode(),vr);
            startupHomeApplied=true;if(vr)trackedHomeApplied=true;
        }
        void Update()
        {
            if(dragging!=null&&(!dragging.isActiveAndEnabled||!dragging.GetComponent<Collider>().enabled))dragging=null;
            bool vr = XRSettings.isDeviceActive;
            if(vr){rightHand.localPosition=handPosition.ReadValue<Vector3>();rightHand.localRotation=handRotation.ReadValue<Quaternion>();}
            if(!vr&&Keyboard.current!=null)
            {
                var keys=Keyboard.current;
                TryDesktopPanoramaLook(new Vector2((keys.rightArrowKey.isPressed?1:0)-(keys.leftArrowKey.isPressed?1:0),(keys.upArrowKey.isPressed?1:0)-(keys.downArrowKey.isPressed?1:0)),Time.unscaledDeltaTime);
            }
            var libraryRay=vr?new Ray(rightHand.position,rightHand.forward):view.ScreenPointToRay(Mouse.current?.position.ReadValue()??Vector2.zero);
            NativeTextInput.UpdatePointer(libraryRay,vr,vr&&trigger.IsPressed());
            if(gallery.Hud!=null&&gallery.Hud.HandleError(libraryRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false)))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,libraryRay.origin);pointer.SetPosition(1,libraryRay.GetPoint(1.6f));return;}
            if(gallery.Overlay!=null&&gallery.Overlay.HandleReturn(libraryRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false)))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,libraryRay.origin);pointer.SetPosition(1,libraryRay.GetPoint(1.6f));return;}
            if(gallery.AISetup!=null&&gallery.AISetup.HandleInput(libraryRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false),vr&&back.WasPressedThisFrame()))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,libraryRay.origin);pointer.SetPosition(1,libraryRay.GetPoint(1.6f));return;}
            if(gallery.Batch!=null&&gallery.Batch.HandleInput(libraryRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false),vr&&back.WasPressedThisFrame()))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,libraryRay.origin);pointer.SetPosition(1,libraryRay.GetPoint(1.6f));return;}
            if(gallery.Library!=null&&gallery.Library.HandleInput(libraryRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false),vr&&back.WasPressedThisFrame()))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,libraryRay.origin);pointer.SetPosition(1,libraryRay.GetPoint(1.6f));return;}
            // Native text editing owns letter keys; do not fire P/H or movement shortcuts while typing Ask.
            if(!vr&&NativeTextInput.Editing&&(gallery.Overlay?.GuideOpen??false))
            {gallery.Overlay.HandleInput(libraryRay,false,Mouse.current?.leftButton.wasPressedThisFrame??false,Mouse.current?.leftButton.isPressed??false);pointer.enabled=false;return;}
            if(gallery.IsBuildingTree){ClearTreePointing();pointer.enabled=false;return;}
            bool scaleGrip=vr&&gallery.IsTree&&rightGrip.IsPressed();
            if(scaleGrip&&!trigger.IsPressed()&&!trigger.WasPressedThisFrame()&&!back.WasPressedThisFrame())ScaleTreeFromStick(turn.ReadValue<Vector2>().y,Time.unscaledDeltaTime);
            gallery.SyncTreeColliders();
            var hudRay=vr?new Ray(rightHand.position,rightHand.forward):view.ScreenPointToRay(Mouse.current?.position.ReadValue()??Vector2.zero);
            bool hudPress=vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false);
            bool hudHeld=vr?trigger.IsPressed():(Mouse.current?.leftButton.isPressed??false)||(Mouse.current?.rightButton.isPressed??false);
            bool hudShortcut=vr?hudMenu.WasPressedThisFrame():(Keyboard.current?.tabKey.wasPressedThisFrame??false);
            if(gallery.Hud!=null&&gallery.Hud.HandleInput(hudRay,vr,hudPress,hudHeld,hudShortcut))
            {ClearTreePointing();pointer.enabled=vr;pointer.SetPosition(0,hudRay.origin);pointer.SetPosition(1,hudRay.GetPoint(1.6f));return;}
            bool vrPreferencesConsumed=vr&&preferences.HandleControllerInput(hudRay,hudPress,trigger.IsPressed(),back.WasPressedThisFrame());
            bool desktopPreferencesConsumed=false;
            if(!vr)
            {
                var mouse=Mouse.current;var keys=Keyboard.current;
                bool wasCaptured=preferences.CapturingPointer;
                bool modal=(gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen);
                if(!modal)desktopPreferencesConsumed=preferences.HandleDesktopInput();
                bool shortcut=(keys?.pKey.wasPressedThisFrame??false)||(keys?.escapeKey.wasPressedThisFrame??false);
                // UI hover still captures clicks, but must not consume independent right-mouse look.
                if(!wasCaptured&&!shortcut)TryDesktopLook(mouse?.rightButton.isPressed??false,
                    mouse?.leftButton.wasPressedThisFrame??false,mouse?.leftButton.isPressed??false,mouse?.delta.ReadValue()??Vector2.zero);
                if(!wasCaptured&&!shortcut&&!(mouse?.leftButton.isPressed??false)&&keys!=null)
                {
                    RotateTree(new Vector2((keys.cKey.isPressed||keys.kKey.isPressed?1:0)-(keys.zKey.isPressed||keys.jKey.isPressed?1:0),(keys.vKey.isPressed||keys.mKey.isPressed?1:0)-(keys.rKey.isPressed||keys.iKey.isPressed?1:0)),Time.unscaledDeltaTime);
                    if(keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed)
                        ScaleTreeFromStick((keys.upArrowKey.isPressed?1:0)-(keys.downArrowKey.isPressed?1:0),Time.unscaledDeltaTime);
                }
            }
            // Pointer hover (including Help) must not consume independent stick turning.
            bool sceneTurned=vr&&!back.WasPressedThisFrame()&&TrySceneHoverTurn(trigger.WasPressedThisFrame(),trigger.IsPressed(),turn.ReadValue<Vector2>().x,Time.unscaledDeltaTime);
            if(vrPreferencesConsumed)
            {
                if(!preferences.CapturingPointer&&!trigger.IsPressed()&&!back.WasPressedThisFrame())
                {if(gallery.IsTree)FlyTreeSticks(pan.ReadValue<Vector2>(),scaleGrip?Vector2.zero:turn.ReadValue<Vector2>(),Time.unscaledDeltaTime);if(!sceneTurned&&!scaleGrip)SmoothTurn(turn.ReadValue<Vector2>().x,Time.unscaledDeltaTime);}
                ClearTreePointing();dragging=null;pointer.enabled=true;pointer.SetPosition(0,hudRay.origin);pointer.SetPosition(1,hudRay.GetPoint(1.6f));return;
            }
            var tutorialRay=vr?new Ray(rightHand.position,rightHand.forward):view.ScreenPointToRay(Mouse.current?.position.ReadValue()??Vector2.zero);
            if(gallery.Tutorial!=null&&gallery.Tutorial.HandleInput(tutorialRay,vr,vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false),back.WasPressedThisFrame()||(Keyboard.current?.escapeKey.wasPressedThisFrame??false)))
            {
                ClearTreePointing();dragging=null;if(gallery.Tutorial.IsOpen&&preferences.IsOpen)preferences.SetOpen(false);
                pointer.enabled=vr;pointer.SetPosition(0,tutorialRay.origin);pointer.SetPosition(1,tutorialRay.GetPoint(1.6f));return;
            }
            if(gallery.LocationEditor!=null && gallery.LocationEditor.IsOpen){ClearTreePointing();dragging=null;gallery.LocationEditor.HandleInput(vr,rightHand,handPosition.ReadValue<Vector3>(),handRotation.ReadValue<Quaternion>(),trigger.WasPressedThisFrame(),back.WasPressedThisFrame(),pointer);return;}
            if (!vr && (desktopPreferencesConsumed||(!gallery.IsTree&&gallery.HudVisible&&preferences.IsOpen))) {ClearTreePointing(); pointer.enabled = false; return; }
            if (back.WasPressedThisFrame() || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)){if(gallery.Overlay!=null&&gallery.Overlay.GuideOpen)gallery.Overlay.CloseGuide();else Return();return;}
            if(vr){rightHand.localPosition=handPosition.ReadValue<Vector3>();rightHand.localRotation=handRotation.ReadValue<Quaternion>();}
            var overlayRay=vr?new Ray(rightHand.position,rightHand.forward):view.ScreenPointToRay(Mouse.current?.position.ReadValue()??Vector2.zero);
            bool overlaySelect=vr?trigger.WasPressedThisFrame():(Mouse.current?.leftButton.wasPressedThisFrame??false);
            bool overlayHeld=vr?trigger.IsPressed():(Mouse.current?.leftButton.isPressed??false);
            if(gallery.Overlay!=null&&gallery.Overlay.HandleInput(overlayRay,vr,overlaySelect,overlayHeld))
            {ClearTreePointing();dragging=null;pointer.enabled=vr;pointer.SetPosition(0,overlayRay.origin);pointer.SetPosition(1,overlayRay.GetPoint(1.6f));return;}
            // Capture sliders before movement, including the initial press and release frame.
            if(dragging!=null){ClearTreePointing();if(overlayHeld)dragging.Drag(overlayRay);else {dragging.EndDrag();dragging=null;}return;}
            if(gallery.TreeControls!=null && gallery.TreeControls.HandleInput(overlayRay,vr,overlaySelect,overlayHeld,preferences.TreePanSpeed))
            {ClearTreePointing();pointer.enabled=vr;pointer.SetPosition(0,overlayRay.origin);pointer.SetPosition(1,overlayRay.GetPoint(1.6f));return;}
            if(overlaySelect&&TryBeginYearDrag(overlayRay))return;
            if(gallery.IsTree)
            {
                var keys=Keyboard.current;
                if((vr&&focusNode.WasPressedThisFrame())||(!vr&&(keys?.fKey.wasPressedThisFrame??false))){FocusNearestNode();return;}
                if((vr&&nextNode.WasPressedThisFrame())||(!vr&&(keys?.rightBracketKey.wasPressedThisFrame??false))){StepNode(1);return;}
                if((vr&&previousNode.WasPressedThisFrame())||(!vr&&(keys?.leftBracketKey.wasPressedThisFrame??false))){StepNode(-1);return;}
            }
            var driver = view.GetComponent<TrackedPoseDriver>();
            if (driver != null) driver.enabled = vr;
            Ray ray; bool select; bool held;
            if (vr)
            {
                rightHand.localPosition = handPosition.ReadValue<Vector3>(); rightHand.localRotation = handRotation.ReadValue<Quaternion>();
                ray = new Ray(rightHand.position, rightHand.forward); select = trigger.WasPressedThisFrame(); held=trigger.IsPressed();
                if(gallery.IsTree)
                {
                    FlyTreeSticks(pan.ReadValue<Vector2>(),scaleGrip?Vector2.zero:turn.ReadValue<Vector2>(),Time.unscaledDeltaTime);
                }
                if(!sceneTurned&&!scaleGrip)SmoothTurn(turn.ReadValue<Vector2>().x,Time.unscaledDeltaTime);
            }
            else
            {
                var keyboard = Keyboard.current; var mouse = Mouse.current;
                if (keyboard != null)
                {
                    var direction = new Vector3((keyboard.dKey.isPressed||gallery.IsTree&&keyboard.rightArrowKey.isPressed ? 1 : 0)-(keyboard.aKey.isPressed||gallery.IsTree&&keyboard.leftArrowKey.isPressed ? 1 : 0),0,(keyboard.wKey.isPressed ? 1 : 0)-(keyboard.sKey.isPressed ? 1 : 0));
                    if(gallery.IsTree){bool shift=keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed;direction.y=(!shift&&keyboard.upArrowKey.isPressed||keyboard.eKey.isPressed||keyboard.spaceKey.isPressed?1:0)-(!shift&&keyboard.downArrowKey.isPressed||keyboard.qKey.isPressed||keyboard.leftCtrlKey.isPressed?1:0);FlyDesktop(direction,Time.unscaledDeltaTime,shift);}
                    else {
                    direction=Vector3.ClampMagnitude(direction,1);
                    var forward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
                    var p = transform.position + Vector3.ClampMagnitude(transform.right*direction.x+forward*direction.z,1) * (2 * Time.deltaTime);
                    transform.position = new Vector3(Mathf.Clamp(p.x,-3.8f,3.8f),0,Mathf.Clamp(p.z,-2,8));
                    }
                }
                ray = mouse != null ? view.ScreenPointToRay(mouse.position.ReadValue()) : new Ray(view.transform.position, view.transform.forward);
                select = mouse != null && mouse.leftButton.wasPressedThisFrame; held=mouse!=null && mouse.leftButton.isPressed;
            }

            gallery.SyncTreeColliders();
            bool hit = Physics.Raycast(ray, out var info, RayDistance);
            Vector3 ground=default;bool groundHit=!gallery.IsTree&&!hit&&SceneGroundPoint(ray,out ground);
            pointer.enabled = vr; pointer.SetPosition(0, ray.origin); pointer.SetPosition(1, hit ? info.point : groundHit?ground:ray.GetPoint(8));
            if(gallery.IsTree){if(HandleTreeHit(hit?info.collider:null,select))return;}
            else ClearTreePointing();
            if(dragging!=null) {if(held)dragging.Drag(ray);else dragging=null;return;}
            if(!select)return;
            if(!hit){if(groundHit){var offset=view.transform.position-transform.position;transform.position=new Vector3(ground.x-offset.x,0,ground.z-offset.z);}return;}
            var slider=YearSlider.Resolve(info.collider);
            if(slider!=null){dragging=slider;slider.BeginDrag(ray,info.collider==slider.ThumbCollider);return;}
            var target = info.collider.GetComponent<GalleryTarget>();
            if (target != null)
            {
                if(!target.enabled)return;
                if (target.isReturn) Return();
                else if(target.action==GalleryAction.EditLocation)gallery.EditLocation(target.personId);
                else if(target.action==GalleryAction.GenerateScene) gallery.GenerateScene(target.personId);
                else { gallery.Select(target.personId); }
            }
        }
        bool SceneGroundPoint(Ray ray,out Vector3 point)
        {
            point=default;var plane=new Plane(Vector3.up,Vector3.zero);
            if(!plane.Raycast(ray,out float distance)||distance>RayDistance)return false;
            point=ray.GetPoint(distance);return Mathf.Abs(point.x)<3.8f&&point.z>=-2&&point.z<=8;
        }
        GhostSphereTarget pointedGhost;
        public void ClearTreePointing()
        {
            if(pointedGhost!=null)pointedGhost.SetPointed(false);pointedGhost=null;
            if(pointedControls!=null)pointedControls.SetPointed(false);
            pointedControls=null;PointedPersonId=null;
        }
        public bool TryBeginYearDrag(Ray ray)
        {
            gallery.SyncTreeColliders();
            if(!Physics.Raycast(ray,out var hit,RayDistance))return false;
            var slider=YearSlider.Resolve(hit.collider);if(slider==null)return false;
            ClearTreePointing();dragging=slider;slider.BeginDrag(ray,hit.collider==slider.ThumbCollider);return true;
        }
        public bool HandleTreeHit(Collider hit,bool select)
        {
            if(!CanNavigateTree){ClearTreePointing();return false;}
            var sibling=hit!=null?hit.GetComponent<GalleryTarget>():null;
            if(sibling!=null&&sibling.action==GalleryAction.ToggleSiblings){ClearTreePointing();if(select&&sibling.enabled&&hit.enabled)gallery.ToggleSiblings(sibling.personId);return true;}
            var ghost=hit!=null?hit.GetComponent<GhostSphereTarget>():null;
            if(ghost!=null)
            {
                if(pointedGhost!=ghost){ClearTreePointing();pointedGhost=ghost;ghost.SetPointed(true);}
                if(select)gallery.Library?.CreateFromGhost(ghost.Slot);return true;
            }
            if(pointedGhost!=null){pointedGhost.SetPointed(false);pointedGhost=null;}
            var cube=hit!=null?hit.GetComponent<TreeNodeTarget>():null;
            var portal=hit!=null?hit.GetComponent<ScenePortal>():null;
            CubeFacingControls next=cube!=null?cube.Controls:portal!=null?portal.Controls:null;
            if(pointedControls!=next){ClearTreePointing();pointedControls=next;if(next!=null)next.SetPointed(true);}
            PointedPersonId=cube!=null?cube.PersonId:portal!=null?portal.PersonId:null;
            if(!select)return false;
            // Resolve the actual foremost collider: a visible portal enters, a cube frames.
            if(portal!=null&&portal.Controls!=null&&portal.Controls.PortalInteractive){gallery.Select(portal.PersonId);ClearTreePointing();return true;}
            if(portal!=null)return true;
            if(cube!=null){FrameNode(cube.PersonId);return true;}
            return false;
        }
        float RayDistance => gallery.IsTree ? Mathf.Max(15,Vector3.Distance(view.transform.position,gallery.WorldTreeBounds.center)+gallery.WorldTreeBounds.extents.magnitude+20) : 15;
        void Home() { transform.position = Vector3.zero; transform.rotation = Quaternion.identity; pitch=0;if(!XRSettings.isDeviceActive)view.transform.localRotation=Quaternion.identity; }
        public void Fly(Vector3 direction,float seconds,bool boost=false)
        {
            direction=Vector3.ClampMagnitude(direction,1);
            var movement=view.transform.right*direction.x+Vector3.up*direction.y+view.transform.forward*direction.z;
            MoveTree(movement,seconds,boost);
        }
        public void FlyDesktop(Vector3 direction,float seconds,bool boost=false)
            =>Fly(direction,seconds,boost);
        bool CanNavigateTree=>gallery.IsTree&&!(gallery.AISetup?.IsOpen??false)&&!(gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)&&!preferences.CapturingPointer&&dragging==null&&!(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen);
        public bool RotateTree(Vector2 input,float seconds)
        {
            if(!CanNavigateTree||gallery.Tree.People.Count==0||(gallery.Library?.IsOpen??false)||(gallery.Batch?.IsOpen??false)||seconds<=0||input==Vector2.zero)return false;
            gallery.RotateTree(Vector2.ClampMagnitude(input,1)*(TreeRotationPose.DegreesPerSecond*seconds));return true;
        }
        public void ResetTreeRotation(){if(CanNavigateTree&&!(gallery.Library?.IsOpen??false)&&!(gallery.Batch?.IsOpen??false))gallery.ResetTreeRotation();}
        public bool ScaleTreeFromStick(float axis,float seconds)
        {
            if(!CanNavigateTree||gallery.Tree.People.Count==0||(gallery.Library?.IsOpen??false)||(gallery.Batch?.IsOpen??false))return false;
            float next=TreeScaling.Step(gallery.TreeScale,axis,seconds);if(next==gallery.TreeScale)return false;
            gallery.SetTreeScale(next);return true;
        }
        public string NearestNode()=>!gallery.IsTree?null:gallery.Layout.Positions.Keys
            .OrderBy(id=>(gallery.TreeNodePosition(id)-view.transform.position).sqrMagnitude)
            .ThenBy(id=>id,System.StringComparer.Ordinal).FirstOrDefault();
        public void FocusNearestNode(){if(CanNavigateTree)FrameNode(NearestNode());}
        public void StepNode(int direction)
        {
            if(!CanNavigateTree)return;
            var ids=gallery.Layout.Positions.Keys.OrderBy(id=>gallery.Layout.Positions[id].z)
                .ThenBy(id=>gallery.Layout.Positions[id].x).ThenBy(id=>id,System.StringComparer.Ordinal).ToArray();
            if(ids.Length==0)return;
            int index=System.Array.IndexOf(ids,FocusedPersonId??NearestNode());if(index<0)index=0;
            FrameNode(ids[(index+(direction<0?-1:1)+ids.Length)%ids.Length]);
        }
        public void FrameNode(string id)
            =>FrameNodeForMode(id,XRSettings.isDeviceActive);
        void FrameNodeForMode(string id,bool vr)
        {
            if(!CanNavigateTree||id==null||!gallery.Layout.Positions.ContainsKey(id))return;
            ApplyNodeFrame(id,vr);
        }
        void ApplyNodeFrame(string id,bool vr)
        {
            // Reposition the tracked camera, accounting for room-scale offsets; never write its XR pose.
            var center=gallery.TreeNodePosition(id);
            var orientation=gallery.TreeOrientation;var forward=orientation*Vector3.forward;
            float yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            if(vr)transform.RotateAround(view.transform.position,Vector3.up,yaw-view.transform.eulerAngles.y);
            else {transform.rotation=Quaternion.Euler(0,yaw,0);pitch=-Mathf.Asin(Mathf.Clamp(forward.y,-1,1))*Mathf.Rad2Deg;view.transform.localRotation=Quaternion.Euler(pitch,0,0);}
            float distance=Mathf.Max(2.2f*gallery.TreeScale,TreeLayout.Size*.5f*gallery.TreeScale+NodeNearFade.Distance+.25f);
            transform.position+=center+orientation*new Vector3(0,.35f*gallery.TreeScale,-distance)-view.transform.position;
            FocusedPersonId=id;UpdateTreeClip();
        }
        public void FlyTreeSticks(Vector2 left,Vector2 right,float seconds)
        {
            MoveTree(TreeControlTemplates.Movement(TreeControlTemplates.Current,left,right,view.transform.rotation),seconds);
        }
        public bool TrySceneHoverTurn(bool pressed,bool held,float axis,float seconds)
        {
            if(gallery.IsTree||pressed||held||dragging!=null||preferences.CapturingPointer||
                (gallery.Overlay!=null&&gallery.Overlay.IsDraggingYear)||
                (gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||
                (gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen))return false;
            SmoothTurn(axis,seconds);return true;
        }
        public bool TryDesktopPanoramaLook(Vector2 axis,float seconds)
        {
            if(gallery.IsTree||XRSettings.isDeviceActive||axis.sqrMagnitude==0||NativeTextInput.Editing||dragging!=null||preferences.CapturingPointer||gallery.IsTimelineDragging||
                (Mouse.current?.leftButton.isPressed??false)||(gallery.Tutorial?.IsOpen??false)||(gallery.LocationEditor?.IsOpen??false)||
                (gallery.Library?.IsOpen??false)||(gallery.AISetup?.IsOpen??false)||(gallery.Batch?.IsOpen??false))return false;
            var delta=Vector2.ClampMagnitude(axis,1)*(60*Mathf.Max(0,seconds));transform.Rotate(0,delta.x,0);
            pitch=Mathf.Clamp(pitch-delta.y,-65,65);view.transform.localRotation=Quaternion.Euler(pitch,0,0);return true;
        }
        public bool TryDesktopLook(bool rightHeld,bool pressed,bool held,Vector2 movement)
        {
            if(!rightHeld||pressed||held||dragging!=null||preferences.CapturingPointer||
                (gallery.Overlay!=null&&gallery.Overlay.IsDraggingYear)||
                (gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||
                (gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen))return false;
            var delta=movement*preferences.LookSensitivity;transform.Rotate(0,delta.x,0);
            pitch=Mathf.Clamp(pitch-delta.y,gallery.IsTree?-89:-65,gallery.IsTree?89:65);
            view.transform.localRotation=Quaternion.Euler(pitch,0,0);return true;
        }
        public void SmoothTurn(float axis,float seconds)
        {
            if((gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||preferences.CapturingPointer||dragging!=null||(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen))return;
            float amount=Mathf.Clamp01((Mathf.Abs(axis)-.2f)/.8f);
            if(amount==0||seconds<=0)return;
            transform.RotateAround(view.transform.position,Vector3.up,Mathf.Sign(axis)*amount*SmoothTurnDegreesPerSecond*seconds);
        }
        void MoveTree(Vector3 movement,float seconds,bool boost=false)
        {
            if(!CanNavigateTree)return;
            if(movement.sqrMagnitude>0)FocusedPersonId=null;
            transform.position+=Vector3.ClampMagnitude(movement,1)*preferences.TreePanSpeed*(boost?3:1)*seconds;
            UpdateTreeClip();
        }
        void UpdateTreeClip()
        {
            float distance=Mathf.Max(80,Vector3.Distance(view.transform.position,gallery.WorldTreeBounds.center)+gallery.WorldTreeBounds.extents.magnitude+20);
            if(Mathf.Abs(view.farClipPlane-distance)>.1f)view.farClipPlane=distance;
        }
        public void RefreshTreeClip(){if(gallery!=null&&gallery.IsTree&&view!=null)UpdateTreeClip();}
        public void EnterHistoricalScene()
        {
            ClearTreePointing();
            savedTreePosition=transform.position;savedTreeRotation=transform.rotation;savedViewRotation=view.transform.localRotation;savedPitch=pitch;treeViewSaved=true;Home();
        }
        public void RestoreTreeView()
        {
            if(!treeViewSaved)return;
            transform.SetPositionAndRotation(savedTreePosition,savedTreeRotation);pitch=savedPitch;
            if(!XRSettings.isDeviceActive)view.transform.localRotation=savedViewRotation;
            RefreshTreeClip();
        }
        public void ResetTreeView(){ClearTreePointing();gallery.SetTreeScale(1);gallery.ResetTreeRotation();gallery.ResetTreePan();FocusedPersonId=null;treeViewSaved=false;Home();RefreshTreeClip();}
        public void GoHome()
        {
            if(!gallery.IsTree||gallery.Tutorial?.IsOpen==true||gallery.LocationEditor?.IsOpen==true||gallery.Library?.IsOpen==true||gallery.AISetup?.IsOpen==true||gallery.Batch?.IsOpen==true||preferences.IsOpen)return;
            if(dragging!=null){dragging.EndDrag();dragging=null;}
            ResetTreeView();FrameNode(HomeNode());
        }
        public string HomeNode()
        {
            if(gallery.Layout==null||gallery.Layout.Positions.Count==0)return null;
            float front=gallery.Layout.Positions.Values.Min(p=>p.z);
            foreach(var person in gallery.Document.People)
                if(gallery.Layout.Positions.TryGetValue(person.Id,out var p)&&Mathf.Abs(p.z-front)<.001f)return person.Id;
            return null;
        }
        public void Return() { if(gallery.IsTree)ResetTreeView();else if(gallery.Tree!=null)gallery.ShowTree(); }
    }
}
