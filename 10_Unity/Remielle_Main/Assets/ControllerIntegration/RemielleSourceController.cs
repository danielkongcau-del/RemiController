using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remielle.Controller
{
    [DefaultExecutionOrder(100)]
    public sealed class RemielleSourceController : MonoBehaviour
    {
        public RemielleNativeAnimation driver;
        public Transform visual;
        public RemielleAuthoredMotor motor;
        public Camera viewCamera;
        public CinemachineBrain brain;
        public CinemachineCamera layoutCamera;
        public TextAsset actionCameraPack;
        public SourceLayoutCamera ActionCamera { get; private set; }
        SourceCameraHeightAnchor cameraHeightAnchor;
        public TextAsset actionShakePack;
        public SourceCameraShake ActionShake { get; private set; }
        public TextAsset specialResourcePack;
        public SourceSpecialResource SpecialResource { get; private set; }
        [Min(1)] public int normalSpecialsPerCharge=2;
        public TextAsset hitQueryPack;
        public SourceHitQuery HitQuery { get; private set; }
        public SourceTrainingDamage TrainingDamage { get; private set; }
        public TextAsset hitFeedbackPack;
        public SourceHitFeedback HitFeedback { get; private set; }
        public TextAsset hitStopPack;
        public SourceHitStop HitStop { get; private set; }
        public TextAsset hitParticlePack;
        public GameObject[] hitParticlePrefabs;
        [Min(.01f)] public float hitParticleScale=.25f;
        public SourceHitParticles HitParticles { get; private set; }
        public TextAsset specialEffectEventPack,specialParticlePack;
        public GameObject[] specialParticlePrefabs;
        public SourceFrameEvents SkillEvents { get; private set; }
        public SourceSkillParticles SkillParticles { get; private set; }
        public TextAsset sourcePack,timeSettings,actionEventPack;
        public TextAsset visibilityEventPack;
        public SourceRendererVisibility.Binding[] visibilityBindings;
        public SourceRendererVisibility Visibility { get; private set; }
        public TextAsset weaponTrailPack;
        public Material weaponTrailMaterial;
        public SourceWeaponTrail WeaponTrail { get; private set; }
        public TextAsset weaponMaterialPack;
        public Texture2D weaponEmissionTexture;
        public SourceWeaponEmission WeaponEmission { get; private set; }
        public Shader weaponSurfaceShader;
        public Texture2D weaponOutlineTexture;
        public SourceWeaponSurface WeaponSurface { get; private set; }
        public TextAsset leftWeaponMaterialPack,leftWeaponTrailPack,largeWeaponMaterialPack,largeWeaponTrailPack;
        public SourceWeaponEmission LeftWeaponEmission { get; private set; }
        public SourceWeaponSurface LeftWeaponSurface { get; private set; }
        public SourceWeaponTrail LeftWeaponTrail { get; private set; }
        public SourceModifierWindow LargeWeaponWindow { get; private set; }
        public SourceWeaponSurface LargeWeaponSurface { get; private set; }
        public SourceWeaponTrail LargeWeaponTrail { get; private set; }
        public bool readDevices=true;
        [Min(0)] public float inputBufferSeconds=.2f;
        public SourceInputBuffer InputBuffer { get; private set; }
        InputActionMap actions;
        InputAction moveAction,evadeAction,attackAction,specialAction;
        bool pendingEvade,pendingAttack,pendingSpecial;
        public SourceActionSession Session { get; private set; }
        public string Error { get; private set; }
        public bool Ready=>Session!=null&&Error==null;
        public double WarmupMilliseconds { get; private set; }
        public SourceFrameEvents ActionEvents { get; private set; }
        public System.Collections.Generic.List<JObject> EventTrace { get; }=new System.Collections.Generic.List<JObject>();
        public int DroppedEventTraceEntries { get; private set; }
        void RecordEvent(JObject entry)
        {
            if(EventTrace.Count>=4096){EventTrace.RemoveAt(0);DroppedEventTraceEntries++;}
            EventTrace.Add(entry);
        }
        internal void RestrictInputDevices(params InputDevice[] devices)=>actions.devices=devices;

        void Awake()
        {
            try
            {
                if(actionCameraPack)cameraHeightAnchor=new SourceCameraHeightAnchor(layoutCamera.Follow,motor.transform,driver);
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                Session=new SourceActionSession(new NativeControllerSource(sourcePack.text),"Avatar_Female_Size02_RemielleOrigin_Controller",directory,
                    new NativeControllerTimeSettings(timeSettings.text),avatars,driver,visual,motor);
                var warmup=System.Diagnostics.Stopwatch.StartNew();SourcePreviewMotions.Prewarm(Session);
                WarmupMilliseconds=warmup.Elapsed.TotalMilliseconds;
                if(visibilityEventPack)Visibility=new SourceRendererVisibility(visibilityBindings,visibilityEventPack.text);
                // Acquire the profile's runtime materials before the surface
                // owns derived copies. Start() then updates the same originals.
                if(weaponSurfaceShader){var profile=visual.GetComponentInChildren<RemielleRuntimeProfile>();if(profile)profile.Apply(profile.battle);}
                if(weaponTrailPack)WeaponTrail=new SourceWeaponTrail(weaponTrailPack.text,new NativeControllerSource(sourcePack.text),driver,weaponTrailMaterial);
                if(weaponMaterialPack)WeaponEmission=new SourceWeaponEmission(weaponMaterialPack.text,new NativeControllerSource(sourcePack.text),visibilityBindings,weaponEmissionTexture);
                if(weaponSurfaceShader)
                {
                    WeaponSurface=new SourceWeaponSurface(weaponMaterialPack.text,visibilityBindings,weaponSurfaceShader,weaponOutlineTexture);
                    WeaponEmission.ModifierChanged+=WeaponSurface.SetActive;
                }
                if(leftWeaponMaterialPack)
                {
                    var source=new NativeControllerSource(sourcePack.text);
                    LeftWeaponEmission=new SourceWeaponEmission(leftWeaponMaterialPack.text,source,visibilityBindings,weaponEmissionTexture);
                    LeftWeaponSurface=new SourceWeaponSurface(leftWeaponMaterialPack.text,visibilityBindings,weaponSurfaceShader,weaponOutlineTexture);
                    LeftWeaponEmission.ModifierChanged+=LeftWeaponSurface.SetActive;
                    LeftWeaponTrail=new SourceWeaponTrail(leftWeaponTrailPack.text,source,driver,weaponTrailMaterial);
                }
                if(largeWeaponMaterialPack)
                {
                    var source=new NativeControllerSource(sourcePack.text);
                    LargeWeaponWindow=new SourceModifierWindow(largeWeaponMaterialPack.text,source);
                    LargeWeaponSurface=new SourceWeaponSurface(largeWeaponMaterialPack.text,visibilityBindings,weaponSurfaceShader,weaponOutlineTexture);
                    LargeWeaponWindow.Changed+=LargeWeaponSurface.SetActive;
                    LargeWeaponTrail=new SourceWeaponTrail(largeWeaponTrailPack.text,source,driver,weaponTrailMaterial);
                }
                if(actionEventPack)
                {
                    ActionEvents=new SourceFrameEvents(actionEventPack.text,new NativeControllerSource(sourcePack.text),"Avatar_Female_Size02_RemielleOrigin_Controller");
                    if(actionCameraPack)ActionCamera=new SourceLayoutCamera(actionCameraPack.text,ActionEvents,layoutCamera);
                    if(actionShakePack)ActionShake=new SourceCameraShake(actionShakePack.text,ActionEvents,motor.transform);
                    if(specialResourcePack)SpecialResource=new SourceSpecialResource(specialResourcePack.text,Session.Parameters,ActionEvents,normalSpecialsPerCharge);
                    if(hitQueryPack)HitQuery=new SourceHitQuery(hitQueryPack.text,ActionEvents,motor.transform);
                    if(HitQuery!=null)TrainingDamage=new SourceTrainingDamage(HitQuery);
                    if(hitFeedbackPack)HitFeedback=new SourceHitFeedback(hitFeedbackPack.text,HitQuery,motor.transform);
                    if(hitStopPack)HitStop=new SourceHitStop(hitStopPack.text,HitQuery);
                    if(hitParticlePack)HitParticles=new SourceHitParticles(hitParticlePack.text,hitParticlePrefabs,HitQuery,hitParticleScale);
                    if(specialEffectEventPack)
                    {
                        SkillEvents=new SourceFrameEvents(specialEffectEventPack.text,new NativeControllerSource(sourcePack.text),"Avatar_Female_Size02_RemielleOrigin_Controller");
                        SkillParticles=new SourceSkillParticles(specialParticlePack.text,specialParticlePrefabs,SkillEvents,driver,motor.transform);
                    }
                    ActionEvents.Emitted+=e=>RecordEvent(new JObject{["generation"]=e.Generation,["state"]=e.State,["entry"]=e.Index,
                        ["cycle"]=e.Cycle,["frame"]=e.AuthoredFrame,["type"]=e.Type,["reason"]=e.Reason,["fields"]=e.Fields});
                    ActionEvents.Released+=(id,reason)=>RecordEvent(new JObject{["generation"]=id,["released"]=true,["reason"]=reason});
                    Session.ObserveActions(n=>{ActionEvents.Observe(n);SkillEvents?.Observe(n);WeaponTrail?.Observe(n);WeaponEmission?.Observe(n);
                        LeftWeaponTrail?.Observe(n);LeftWeaponEmission?.Observe(n);LargeWeaponTrail?.Observe(n);LargeWeaponWindow?.Observe(n);});
                }
                actions=new InputActionMap("Remielle");
                InputBuffer=new SourceInputBuffer(Session.Parameters,inputBufferSeconds);
                moveAction=actions.AddAction("Move",InputActionType.Value);
                moveAction.expectedControlType="Vector2";
                moveAction.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
                evadeAction=actions.AddAction("Evade",InputActionType.Button,"<Keyboard>/space");
                attackAction=actions.AddAction("Attack",InputActionType.Button,"<Mouse>/leftButton");
                specialAction=actions.AddAction("Special",InputActionType.Button,"<Keyboard>/e");
                evadeAction.performed+=_=>pendingEvade=true;
                attackAction.performed+=_=>pendingAttack=true;
                specialAction.performed+=_=>pendingSpecial=true;
            }
            catch(Exception ex){Fail(ex);}
        }
        void Update()
        {
            if(!Ready||!readDevices)return;
            // Performed callbacks survive the input-update/controller-update
            // boundary, including several input events before the next tick.
            if(Time.deltaTime<=0)return;
            TickInput(Time.deltaTime,moveAction.ReadValue<Vector2>(),pendingEvade,evadeAction.IsPressed(),pendingAttack,attackAction.IsPressed(),false,pendingSpecial,specialAction.IsPressed());
            pendingEvade=pendingAttack=pendingSpecial=false;
        }

        // The buffer owns input lifetime; recovered conditions own transitions.
        public void TickInput(float delta,Vector2 movement,bool evadePressed,bool evadeHeld,bool attackPressed,bool attackHeld,bool runHeld,bool specialPressed=false,bool specialHeld=false)
        {
            if(!Ready)return;
            try
            {
                // Existing effects advance before this tick emits new hits.
                // This is world delta, before the actor-local hitstop consumes it.
                HitParticles?.Advance(delta);
                delta=HitStop?.Consume(delta)??delta;
                InputBuffer.Advance(delta);
                var p=Session.Parameters;bool moving=movement.sqrMagnitude>.0001f;
                p.SetBool(p.Hash("Bool_IsMoving"),moving);
                p.SetBool(p.Hash("Bool_HoldEvade"),evadeHeld&&!evadePressed);
                // Selector 8 checks HoldAttackA before PressAttackA: publishing
                // both on the initial edge incorrectly enters Attack_Burst_01.
                // A held action begins on a subsequent input tick; the source
                // state still owns its authored 13-frame follow-up threshold.
                p.SetBool(p.Hash("Bool_HoldAttackA"),attackHeld&&!attackPressed);
                p.SetBool(p.Hash("Bool_HoldAttackB"),specialHeld&&!specialPressed);
                p.SetBool(p.Hash("Bool_WalkToRun"),runHeld);
                if(evadePressed)InputBuffer.Press(p.Hash("Trigger_PressEvade"));
                if(attackPressed)InputBuffer.Press(p.Hash("Trigger_PressAttackA"));
                if(specialPressed)InputBuffer.Press(p.Hash("Trigger_PressAttackB"));
                if(moving&&delta>0)
                {
                    Vector3 forward=viewCamera?viewCamera.transform.forward:Vector3.forward;forward.y=0;
                    if(forward.sqrMagnitude<.001f)forward=Vector3.forward;forward.Normalize();
                    Vector3 right=Vector3.Cross(Vector3.up,forward);
                    Vector3 direction=forward*movement.y+right*movement.x;
                    motor.transform.rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
                }
                Session.Tick(delta);
                InputBuffer.AfterCommit();
                SkillParticles?.Advance(delta);
                WeaponTrail?.Advance(delta);
                WeaponEmission?.Advance(delta);
                WeaponSurface?.Advance(delta);
                LeftWeaponTrail?.Advance(delta);LeftWeaponEmission?.Advance(delta);LeftWeaponSurface?.Advance(delta);
                LargeWeaponTrail?.Advance(delta);LargeWeaponSurface?.Advance(delta);
                cameraHeightAnchor?.Advance(delta);ActionCamera?.Advance(delta);
                ActionShake?.Advance();
            }
            catch(Exception ex){Fail(ex);}
        }
        void LateUpdate(){if(Ready){HitFeedback?.Advance();if(brain)brain.ManualUpdate();}}
        void Fail(Exception ex){Error=ex.ToString();Debug.LogException(ex);}
        void OnGUI()
        {
            GUI.Label(new Rect(16,12,1200,28),"Remielle preview — WASD move / Space evade (hold while moving to dash) / Left mouse attack / E special");
            if(Session!=null)GUI.Label(new Rect(16,38,1050,28),"State "+Session.CurrentState+(Session.NextState.HasValue?" -> "+Session.NextState:"")+"   Action frame "+Session.ActionFrames.ToString("F1"));
            GUI.Label(new Rect(16,64,1100,40),Error!=null?"Stopped: "+Error.Split('\n')[0]:"Input buffer "+inputBufferSeconds.ToString("F2")+" s — skill effects are being integrated.");
            if(SpecialResource!=null)GUI.Label(new Rect(16,100,1100,28),
                SpecialResource.BranchIndex==1?"EX special ready — press E":
                "EX charge: "+SpecialResource.NormalSpecialsTowardCharge+" / "+SpecialResource.NormalSpecialsPerCharge+" normal specials (E)");
            if(HitQuery!=null)GUI.Label(new Rect(16,128,1100,28),"Training hits "+HitQuery.Hits+" / misses "+HitQuery.Misses+" — blue targets flash orange when struck");
            if(TrainingDamage!=null)
            {
                var target=TrainingDamage.LastTarget;
                GUI.Label(new Rect(16,156,1100,28),"Training damage "+TrainingDamage.TotalApplied.ToString("F1")+
                    (target?" / "+target.name+" HP "+target.Health.ToString("F0")+" / "+target.MaximumHealth.ToString("F0"):" / target HP 100")+
                    " (damage / stagger = 1 per hit)");
            }
        }
        void OnEnable(){if(Ready)actions?.Enable();}
        void OnDisable(){actions?.Disable();pendingEvade=pendingAttack=pendingSpecial=false;InputBuffer?.Clear();HitParticles?.Clear();SkillParticles?.Clear();}
        void OnDestroy()
        {
            actions?.Disable();actions?.Dispose();
            try{Session?.Dispose();}finally{Session=null;try{Visibility?.Dispose();}finally{DisposeWeaponEffects();}}
        }
        void DisposeWeaponEffects()
        {
            // Restore emission blocks before the surface material owners.
            foreach(var effect in new IDisposable[]{InputBuffer,SkillParticles,HitParticles,HitStop,HitFeedback,TrainingDamage,HitQuery,SpecialResource,ActionShake,ActionCamera,cameraHeightAnchor,WeaponEmission,LeftWeaponEmission,LargeWeaponWindow,WeaponSurface,LeftWeaponSurface,LargeWeaponSurface,WeaponTrail,LeftWeaponTrail,LargeWeaponTrail})
                try{effect?.Dispose();}catch(Exception ex){Debug.LogException(ex);}
        }
    }
}
