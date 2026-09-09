#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Opt-in acceptance inside the built Player's normal auditCamera/animation loop.
// Headless Players do not schedule automatic camera rendering; their real
// Animation/LateUpdate still advance normally, followed by Camera.Render here.
// Interactive Players use the enabled camera. No per-frame Animation.Sample.
[DefaultExecutionOrder(10000)]
public sealed class RemielleNativeUIPlayerAudit : MonoBehaviour
{
    [Serializable] public class Segment
    {
        public string profile,clip,mode;
        public int frames,expectedFrames,width,height,minVisiblePixels=int.MaxValue,minX=int.MaxValue,minY=int.MaxValue,maxX,maxY,maxPresentationByteDifference;
        public float authoredLength,firstAnimationTime,lastAnimationTime;
        public long changedFrames,poseChanges,firstPoseHash,lastPoseHash;
        public string screenshot;
    }
    [Serializable] public class Report
    {
        public bool pass;public string utc,error,device,graphicsApi;
        public bool headlessScheduledCamera;
        public int frames,fullClipSegments,transitionSegments,profileSwitches,privateTargetsBefore,privateTargetsAfter;
        public double elapsedSeconds;
        public List<Segment> segments=new();
    }
    readonly Report report=new();
    RemielleNativeUIPresentation view;
    Camera auditCamera;
    RenderTexture target;
    string folder;
    string[] clips;
    AnimationState current;
    Segment segment;
    byte[] previousImage;
    int segmentIndex,phase,framesInSegment,transitionIndex,finishAfter;
    bool nextSegment,finishing,finished;
    double startTime;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
    {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-remielleNativeUIAudit");if(index<0)return;
        if(index+1>=args.Length)throw new ArgumentException("Native Player audit output directory is missing");
        var audit=new GameObject("Opt-in native UI Player audit").AddComponent<RemielleNativeUIPlayerAudit>();audit.folder=Path.GetFullPath(args[index+1]);
    }
    void OnEnable(){Application.logMessageReceived+=OnLog;}
    void OnDestroy(){Application.logMessageReceived-=OnLog;if(view)view.FramePresented-=Presented;ReleaseTarget();}
    void OnLog(string message,string stack,LogType type)
    {
        if(!finished&&!finishing&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))Finish(false,message+"\n"+stack);
    }
    // Every owned graph/audit target uses exactly HideAndDontSave (61).
    // Camera OnRenderImage also creates engine TempBuffers with internal flags (125).
    // These remain in Unity's cache after Camera.Render and are not graph allocations.
    static int PrivateTargets()=>Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.hideFlags==HideFlags.HideAndDontSave);
    void Start()
    {
        try
        {
            Directory.CreateDirectory(folder);startTime=Time.realtimeSinceStartupAsDouble;
            report.headlessScheduledCamera=Application.isBatchMode;report.utc=DateTime.UtcNow.ToString("O");report.device=SystemInfo.graphicsDeviceName;report.graphicsApi=SystemInfo.graphicsDeviceType.ToString();
            view=FindFirstObjectByType<RemielleNativeUIPresentation>();if(!view)throw new Exception("Native visible presentation missing from Player");
            auditCamera=view.GetComponent<Camera>();view.GetComponent<RemielleNativeUIReviewControls>().showUI=false;
            view.model.autoplay=false;view.model.nativeAnimation.cullingType=AnimationCullingType.AlwaysAnimate;
            clips=view.model.nativeAnimation.Cast<AnimationState>().Select(s=>s.name).OrderBy(n=>n).ToArray();
            if(clips.Length!=15)throw new Exception("Player does not contain all 15 native actions");
            view.ReleaseGraph();report.privateTargetsBefore=PrivateTargets();view.FramePresented+=Presented;
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Time.timeScale=1;Time.captureDeltaTime=1f/60;
            SetupSegment();
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void LateUpdate()
    {
        if(finished)return;
        if(finishing)
        {
            if(Time.frameCount>=finishAfter)
            {
                report.privateTargetsAfter=PrivateTargets();bool clean=report.privateTargetsAfter==report.privateTargetsBefore;
                Finish(clean,clean?"":"Native Player graph retained private render targets");
            }
            return;
        }
        try
        {
            if(Time.realtimeSinceStartupAsDouble-startTime>2400)throw new Exception("Native Player acceptance timed out");
            if(nextSegment){nextSegment=false;SetupSegment();}
            if(Application.isBatchMode&&!finishing&&!finished)auditCamera.Render();
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void SetupSegment()
    {
        if(phase==0&&segmentIndex>=30){phase=1;transitionIndex=0;}
        if(phase==1&&transitionIndex>=30)
        {
            finishing=true;view.FramePresented-=Presented;auditCamera.enabled=false;auditCamera.targetTexture=null;view.ReleaseGraph();ReleaseTarget();finishAfter=Time.frameCount+2;return;
        }
        int index=phase==0?segmentIndex:transitionIndex,profile=index/15,clipIndex=index%15;
        if(view.profileIndex!=profile){view.SetProfile(profile);report.profileSwitches++;}
        int w=phase==0?1920:1377,h=phase==0?1080:823;
        if(!target||target.width!=w||target.height!=h)
        {
            auditCamera.targetTexture=null;ReleaseTarget();target=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R8G8B8A8_SRGB,GraphicsFormat.D32_SFloat)){hideFlags=HideFlags.HideAndDontSave};
            if(!target.Create())throw new Exception("Native Player target creation failed");auditCamera.targetTexture=target;
        }
        view.viewYaw=new[]{0f,65f,180f}[clipIndex%3];view.lightEuler=new Vector3(0,phase==1?(clipIndex%3)*90:0,0);view.autoFrame=true;
        current=view.model.nativeAnimation[clips[clipIndex]];current.speed=1;
        view.model.Play(current.name,phase==0?0:.18f);current.time=0;
        // Only initial discontinuous selection is sampled. Subsequent frames
        // run through Unity's actual Animation evaluation and LateUpdate.
        if(phase==0){view.model.ResetSourcePose();view.model.nativeAnimation.Sample();view.model.ApplyPose();view.ResetHistory();}
        framesInSegment=0;previousImage=null;
        segment=new Segment{profile=view.profiles[profile].profileName,clip=current.name,mode=phase==0?"full-authored-clip":"continuous-crossfade",authoredLength=current.length,
            expectedFrames=phase==0?Mathf.CeilToInt(current.length*60)+2:20,width=w,height=h};report.segments.Add(segment);
    }
    void Presented(RemielleNativeUIPresentation sender,RenderTexture destination)
    {
        if(finishing||finished)return;
        try
        {
            if(destination!=target||sender.Graph==null||sender.LastError!=null)throw new Exception("Player auditCamera did not write the expected visible target");
            if(sender.PresentedWidth!=target.width||sender.PresentedHeight!=target.height||current.speed!=1)throw new Exception("Player changed rendering resolution or authored playback speed");
            int pixels=target.width*target.height;
            var image=Read(target,pixels*4);var native=Read(sender.Graph.FinalColor,pixels*4);var depth=Read(sender.Graph.Geometry.DepthRead,pixels*16);
            int visible=0,minX=target.width,minY=target.height,maxX=0,maxY=0,maxDifference=0;
            for(int p=0;p<pixels;p++)
            {
                float z=BitConverter.ToSingle(depth,p*16);if(!float.IsFinite(z)||z<0||z>1)throw new Exception("Native Player produced invalid depth");
                if(z>0){int x=p%target.width,y=p/target.width;visible++;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
            }
            for(int i=0;i<image.Length;i++)maxDifference=Math.Max(maxDifference,Math.Abs(image[i]-native[i]));
            if(maxDifference>1||visible<2000||minX==0||minY==0||maxX>=target.width-1||maxY>=target.height-1)throw new Exception("Player color/framing failed: "+segment.clip+" visible="+visible+" diff="+maxDifference+" bounds="+minX+","+minY+","+maxX+","+maxY);
            if(!sender.Graph.DepthHierarchyUpdated||!sender.Graph.Temporal.HasHistory)throw new Exception("Player render stage was skipped");
            long pose=PoseHash(sender.model);
            if(framesInSegment==0){segment.firstAnimationTime=current.time;segment.firstPoseHash=pose;}
            else if(previousImage!=null&&!previousImage.SequenceEqual(image))segment.changedFrames++;
            if(framesInSegment>0&&pose!=segment.lastPoseHash)segment.poseChanges++;segment.lastPoseHash=pose;
            segment.lastAnimationTime=current.time;segment.minVisiblePixels=Math.Min(segment.minVisiblePixels,visible);
            segment.minX=Math.Min(segment.minX,minX);segment.minY=Math.Min(segment.minY,minY);segment.maxX=Math.Max(segment.maxX,maxX);segment.maxY=Math.Max(segment.maxY,maxY);
            segment.maxPresentationByteDifference=Math.Max(segment.maxPresentationByteDifference,maxDifference);previousImage=image;framesInSegment++;segment.frames++;report.frames++;
            if(report.frames==1||report.frames%300==0)Debug.Log("REMIELLE_NATIVE_PLAYER_PROGRESS frames="+report.frames+" elapsed="+(Time.realtimeSinceStartupAsDouble-startTime));
            if(framesInSegment==Math.Max(1,segment.expectedFrames/2))
            {
                string path=Path.Combine(folder,segment.profile+"-"+(phase==0?"clip":"transition")+"-"+Array.IndexOf(clips,current.name).ToString("D2")+".png");
                var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,false);
                try{texture.LoadRawTextureData(image);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}finally{Destroy(texture);}segment.screenshot=path;
            }
            if(framesInSegment>=segment.expectedFrames)
            {
                if(segment.changedFrames<1||segment.poseChanges<1)throw new Exception("Continuous Player segment did not animate its actual source pose");
                if(phase==0){report.fullClipSegments++;segmentIndex++;}else{report.transitionSegments++;transitionIndex++;}
                Debug.Log("REMIELLE_NATIVE_PLAYER_SEGMENT "+segment.profile+" "+segment.clip+" "+segment.mode+" frames="+segment.frames);nextSegment=true;
            }
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    static byte[] Read(Texture texture,int bytes)
    {
        var data=new NativeArray<byte>(bytes,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
        try{var request=AsyncGPUReadback.RequestIntoNativeArray(ref data,texture,0);request.WaitForCompletion();if(request.hasError)throw new Exception("Native Player GPU readback failed");return data.ToArray();}finally{data.Dispose();}
    }
    static long PoseHash(RemielleNativeAnimation driver)
    {
        ulong hash=14695981039346656037UL;
        void Add(float value)
        {
            if(!float.IsFinite(value))throw new Exception("Native animation source pose is not finite");
            unchecked{hash^=(uint)BitConverter.SingleToInt32Bits(value);hash*=1099511628211UL;}
        }
        foreach(var link in driver.bones)
        {
            var p=link.source.localPosition;var q=link.source.localRotation;var s=link.source.localScale;
            Add(p.x);Add(p.y);Add(p.z);Add(q.x);Add(q.y);Add(q.z);Add(q.w);Add(s.x);Add(s.y);Add(s.z);
        }
        foreach(var link in driver.morphs)for(int i=0;i<link.source.sharedMesh.blendShapeCount;i++)Add(link.source.GetBlendShapeWeight(i));
        return unchecked((long)hash);
    }
    void ReleaseTarget(){if(!target)return;if(RenderTexture.active==target)RenderTexture.active=null;target.Release();Destroy(target);target=null;}
    void Finish(bool success,string error)
    {
        if(finished)return;finished=true;report.pass=success;report.error=error;report.elapsedSeconds=Time.realtimeSinceStartupAsDouble-startTime;
        if(view){view.FramePresented-=Presented;view.ReleaseGraph();}if(auditCamera){auditCamera.enabled=false;auditCamera.targetTexture=null;}ReleaseTarget();
        if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        Debug.Log("REMIELLE_NATIVE_PLAYER_AUDIT "+success+" frames="+report.frames+" "+error);Application.Quit(success?0:1);
    }
}
#endif
