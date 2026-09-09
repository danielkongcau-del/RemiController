#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Opt-in regression in real Play Mode. No callbacks run during ordinary use.
[InitializeOnLoad, DefaultExecutionOrder(10000)]
public class NativeMotionPlayProbe : MonoBehaviour
{
    const string Key="Remielle.MotionPlayProbe";
    const string FullKey="Remielle.MotionPlayProbe.Full";
    const string Out="E:/ZZZ/local-only/RemielleModelReadiness/20260904";
    static double deadline;
    static NativeMotionPlayProbe(){if(SessionState.GetBool(Key,false))Install();}
    public static void StartBatch()
    {
        EditorSceneManager.OpenScene("Assets/V3/Remielle_AnimationReview.unity");
        SessionState.SetBool(Key,true);Install();EditorApplication.EnterPlaymode();
    }
    public static void StartFullAudit(){SessionState.SetBool(FullKey,true);StartBatch();}
    static void Install()
    {
        deadline=EditorApplication.timeSinceStartup+900;
        EditorApplication.update-=InstallInPlay;EditorApplication.update+=InstallInPlay;
    }
    static void InstallInPlay()
    {
        if(EditorApplication.timeSinceStartup>deadline){Finish(false,"Timed out entering Play Mode");return;}
        if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        EditorApplication.update-=InstallInPlay;
        new GameObject("OptInNativeMotionProbe").AddComponent<NativeMotionPlayProbe>();
    }
    class Track {public Transform node,reference;public AnimationCurve[] curves;}
    string[] clips={"Idle_Loop","Run_Transform_01","Run_Transform_02"};
    readonly List<Track> tracks=new();
    readonly JArray results=new();
    RemielleNativeAnimation driver;
    AnimationState state;
    int clipIndex,frames,skipFrame,comparisons,loops;
    float maxError,maxEditorError,lastTime,elapsed;
    string worst="";
    Camera captureCamera;
    GameObject referenceRig;
    Transform[] referenceNodes;
    bool full;
    float maxSourcePositionError,maxSourceScaleError,maxSourceRotationError;
    int boundaryEndReferences;
    // Sampling through the public float clock adds float32 timing error. These
    // bounds are 0.012 degrees and 0.001 source units (0.00001 world units);
    // original-key interpolation is audited separately at much tighter bounds.
    const float RotationTolerance=.0001f,PositionTolerance=.001f,ScaleTolerance=.0001f;
    void Start()
    {
        try
        {
            Time.captureDeltaTime=1f/60;
            driver=UnityEngine.Object.FindFirstObjectByType<RemielleNativeAnimation>();
            if(driver==null)throw new Exception("Missing native driver");
            full=SessionState.GetBool(FullKey,false);
            if(full)clips=driver.nativeAnimation.Cast<AnimationState>().Select(s=>s.name).ToArray();
            captureCamera=Camera.main;
            referenceRig=Instantiate(driver.nativeAnimation.gameObject);
            referenceRig.name="IndependentClipSampleReference";
            referenceRig.GetComponent<Animation>().enabled=false;
            foreach(var renderer in referenceRig.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            referenceNodes=driver.bones.Select(b=>
            {
                string path=AnimationUtility.CalculateTransformPath(b.source,driver.nativeAnimation.transform);
                return string.IsNullOrEmpty(path)?referenceRig.transform:referenceRig.transform.Find(path);
            }).ToArray();
            BeginClip();
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void BeginClip()
    {
        tracks.Clear();frames=comparisons=loops=0;maxError=maxEditorError=elapsed=lastTime=0;worst="";
        maxSourcePositionError=maxSourceScaleError=maxSourceRotationError=0;
        boundaryEndReferences=0;
        string name=clips[clipIndex];state=driver.nativeAnimation[name];
        foreach(var group in AnimationUtility.GetCurveBindings(state.clip)
                    .Where(b=>b.type==typeof(Transform)&&b.propertyName.StartsWith("m_LocalRotation."))
                    .GroupBy(b=>b.path))
        {
            var node=driver.nativeAnimation.transform.Find(group.Key);
            if(node==null)throw new Exception("Missing animated node: "+group.Key);
            tracks.Add(new Track{node=node,reference=referenceRig.transform.Find(group.Key),curves=Enumerable.Range(0,4).Select(i=>
                AnimationUtility.GetEditorCurve(state.clip,EditorCurveBinding.FloatCurve(group.Key,typeof(Transform),"m_LocalRotation."+"xyzw"[i]))).ToArray()});
        }
        driver.Play(name,full&&clipIndex>0?.18f:0);state.time=0;skipFrame=Time.frameCount;
    }
    void LateUpdate()
    {
        if(driver==null||Time.frameCount==skipFrame)return;
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Live probe timed out");
            float time=state.time;
            bool loop=state.wrapMode==WrapMode.Loop||state.clip.wrapMode==WrapMode.Loop;
            float sample=loop?Mathf.Repeat(time,state.length):Mathf.Clamp(time,0,state.length);
            if(sample<lastTime)loops++;lastTime=sample;
            void Reference(float at)
            {
                for(int i=0;i<referenceNodes.Length;i++)
                {
                    var b=driver.bones[i];var t=referenceNodes[i];
                    t.localPosition=b.sourceRestPosition;t.localRotation=b.sourceRestRotation;t.localScale=b.sourceRestScale;
                }
                state.clip.SampleAnimation(referenceRig,at);
            }
            // Runtime Animation uses its quaternion evaluator. The independent
            // clone detects timing/update discrepancies without modifying the
            // live driver; editor component interpolation remains a diagnostic.
            Reference(sample);
            bool blending=full&&clipIndex>0&&elapsed<.23f;
            // Public AnimationState.time is float; the engine's playback clock
            // can still be on the inclusive final sample when Repeat(float)
            // rounds across the loop boundary. Test the two adjacent endpoint
            // poses only inside four float ULPs, with the same pose tolerances.
            float clockEpsilon=Mathf.Max(1e-7f,Mathf.Abs(time)*4f/8388608f);
            if(loop&&!blending&&time>state.length*.5f&&sample<clockEpsilon)
            {
                float Gap()
                {
                    float result=0;
                    for(int i=0;i<referenceNodes.Length;i++)
                    {
                        var a=driver.bones[i].source;var b=referenceNodes[i];
                        result=Mathf.Max(result,(a.localPosition-b.localPosition).magnitude,(a.localScale-b.localScale).magnitude);
                        var q=a.localRotation.normalized;var r=b.localRotation.normalized;
                        var qv=new Vector4(q.x,q.y,q.z,q.w);var rv=new Vector4(r.x,r.y,r.z,r.w);
                        result=Mathf.Max(result,Mathf.Min((qv-rv).magnitude,(qv+rv).magnitude));
                    }
                    return result;
                }
                // SampleAnimation also wraps an exact length to zero. Use the
                // immediately preceding representable float for the final pose.
                float endTime=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(state.length)-1);
                float startGap=Gap();Reference(endTime);float endGap=Gap();
                if(endGap<startGap){sample=endTime;boundaryEndReferences++;}else Reference(sample);
            }
            if(!blending)
            {
            foreach(var track in tracks)
            {
                var q=new Quaternion(track.curves[0].Evaluate(sample),track.curves[1].Evaluate(sample),track.curves[2].Evaluate(sample),track.curves[3].Evaluate(sample)).normalized;
                var actual=track.node.localRotation.normalized;
                var a=new Vector4(q.x,q.y,q.z,q.w);var b=new Vector4(actual.x,actual.y,actual.z,actual.w);
                maxEditorError=Mathf.Max(maxEditorError,Mathf.Min((a-b).magnitude,(a+b).magnitude));
                var reference=track.reference.localRotation.normalized;
                a=new Vector4(reference.x,reference.y,reference.z,reference.w);
                float error=Mathf.Min((a-b).magnitude,(a+b).magnitude);
                if(float.IsNaN(error))throw new Exception("Non-finite live rotation");
                if(error>maxError){maxError=error;worst=track.node.name+" at "+sample;}
                comparisons++;
            }
            for(int i=0;i<referenceNodes.Length;i++)
            {
                var actual=driver.bones[i].source;var expected=referenceNodes[i];
                maxSourcePositionError=Mathf.Max(maxSourcePositionError,(actual.localPosition-expected.localPosition).magnitude);
                maxSourceScaleError=Mathf.Max(maxSourceScaleError,(actual.localScale-expected.localScale).magnitude);
                var a=actual.localRotation.normalized;var b=expected.localRotation.normalized;
                var av=new Vector4(a.x,a.y,a.z,a.w);var bv=new Vector4(b.x,b.y,b.z,b.w);
                maxSourceRotationError=Mathf.Max(maxSourceRotationError,Mathf.Min((av-bv).magnitude,(av+bv).magnitude));
            }
            }
            frames++;elapsed+=Time.deltaTime;
            // Capture actual GPU-skinned output after the driver's LateUpdate.
            if(frames==60)Capture(Out+"/live-"+clips[clipIndex]+".png");
            float duration=full?state.length+.2f:clipIndex==0?state.length+.2f:Mathf.Min(state.length-.05f,2f);
            if(elapsed<duration)return;
            bool pass=maxError<RotationTolerance&&frames>30&&maxSourceRotationError<RotationTolerance&&maxSourcePositionError<PositionTolerance&&maxSourceScaleError<ScaleTolerance;
            results.Add(new JObject{["clip"]=clips[clipIndex],["pass"]=pass,["frames"]=frames,["boneComparisons"]=comparisons,["loopsCrossed"]=loops,["boundaryEndReferences"]=boundaryEndReferences,["maxRuntimeSampleChordError"]=maxError,["maxEditorComponentChordDifference"]=maxEditorError,["maxSourcePositionError"]=maxSourcePositionError,["maxSourceScaleError"]=maxSourceScaleError,["maxSourceRotationError"]=maxSourceRotationError,["includesUnkeyedSourceNodes"]=true,["transitionFadeSeconds"]=full&&clipIndex>0?.18f:0,["worst"]=worst});
            if(!pass){Write(false);Finish(false,"Live animation differs from verified curves");return;}
            clipIndex++;
            if(clipIndex<clips.Length){BeginClip();return;}
            Write(true);Finish(true,"NATIVE_MOTION_PLAY_VERIFIED");
        }
        catch(Exception e){Write(false);Finish(false,e.ToString());}
    }
    void Write(bool pass)=>File.WriteAllText(Out+"/live-motion-verification.json",new JObject
    {
        ["pass"]=pass,["utc"]=DateTime.UtcNow.ToString("O"),["clips"]=results,
        ["captureMethod"]="Real Play Mode, native Animation updates and GPU skinning; live driver not manually sampled. Independent invisible clone evaluated with AnimationClip.SampleAnimation for same-time runtime comparison.",
        ["legacyClampBlendShapeWeights"]=PlayerSettings.legacyClampBlendShapeWeights,
        ["comparisonToleranceChord"]=RotationTolerance,["comparisonToleranceSourcePosition"]=PositionTolerance,["comparisonToleranceScale"]=ScaleTolerance,
        ["timingNote"]="Nominal public float AnimationState.time; inclusive end pose also tested only within four float ULPs of a loop boundary. No arbitrary search for a matching pose. Float32 clock/evaluator discrepancies are reported, not set to zero."
    }.ToString());
    void Capture(string path)
    {
        var rt=new RenderTexture(720,1024,24);captureCamera.targetTexture=rt;captureCamera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var tex=new Texture2D(720,1024,TextureFormat.RGBA32,false);
        tex.ReadPixels(new Rect(0,0,720,1024),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
        RenderTexture.active=previous;captureCamera.targetTexture=null;rt.Release();Destroy(rt);Destroy(tex);
    }
    static void Finish(bool pass,string message)
    {
        SessionState.SetBool(Key,false);SessionState.SetBool(FullKey,false);EditorApplication.update-=InstallInPlay;
        if(pass)Debug.Log(message);else Debug.LogError(message);
        EditorApplication.Exit(pass?0:1);
    }
}
#endif
