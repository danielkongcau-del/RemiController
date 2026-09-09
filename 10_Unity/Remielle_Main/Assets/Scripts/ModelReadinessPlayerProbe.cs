#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Opt-in render inspection in the built player; no scene or source asset edits.
[DefaultExecutionOrder(10000)]
public class ModelReadinessPlayerProbe : MonoBehaviour
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ModelReadiness/20260904";
    [Serializable] class Shot { public string name; public int frame; public float yaw,lightYaw; public bool battle; }
    [Serializable] class Axis { public int rotation,axis,matchingPixels; public Vector3 expected,actual; public float error; }
    [Serializable] class PoseBounds { public string clip,worstRenderer; public float fraction,maxDistanceOutsideBounds; public int vertices; }
    [Serializable] class Report { public bool pass;public string utc,error,device,api;public int frames;public List<Shot> shots=new();public List<Axis> axes=new();public List<PoseBounds> bounds=new(); }
    readonly Report report=new();
    RemielleNativeAnimation driver;RemielleFaceLighting face;RemielleReviewControls controls;
    Camera cam;Light sun;SkinnedMeshRenderer[] skins;Material[] faceMaterials;
    Quaternion initialRotation;int frames;bool finished;double started;AnimationState[] clips;
    static readonly string[] Names={"front","back","left","right","face-front","face-side-light","face-back-light","ribbon-front","ribbon-back","wings-front","wings-back","battle-front","battle-face","afk-face"};
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install(){if(Environment.GetCommandLineArgs().Contains("-remielleModelReadiness"))new GameObject("OptInModelReadiness").AddComponent<ModelReadinessPlayerProbe>();}
    void OnEnable(){Application.logMessageReceived+=OnLog;}
    void OnDestroy(){Application.logMessageReceived-=OnLog;}
    void OnLog(string message,string stack,LogType type){if(!finished&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))Finish(false,message+"\n"+stack);}
    void Start()
    {
        try
        {
            Directory.CreateDirectory(Out);started=Time.realtimeSinceStartupAsDouble;
            Application.runInBackground=true;Time.captureDeltaTime=1f/60;
            driver=FindFirstObjectByType<RemielleNativeAnimation>();face=driver.GetComponent<RemielleFaceLighting>();
            controls=FindFirstObjectByType<RemielleReviewControls>();controls.enabled=false;cam=Camera.main;
            sun=FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l=>l.type==LightType.Directional);
            skins=driver.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled).ToArray();
            faceMaterials=face.renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.GetFloat("_MaterialType")==1).Distinct().ToArray();
            initialRotation=driver.transform.rotation;Freeze("Idle_Loop",0);
            clips=driver.nativeAnimation.Cast<AnimationState>().ToArray();
            report.utc=DateTime.UtcNow.ToString("O");report.device=SystemInfo.graphicsDeviceName;report.api=SystemInfo.graphicsDeviceType.ToString();
            Setup(0);
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void Freeze(string clip,float fraction){driver.Play(clip,0);var state=driver.nativeAnimation[clip];state.time=state.length*fraction;state.speed=0;}
    void LateUpdate()
    {
        if(finished||driver==null)return;
        try
        {
            if(Time.realtimeSinceStartupAsDouble-started>240)throw new Exception("Render probe timeout");
            frames++;
            if(frames<=Names.Length*8)
            {
                int n=(frames-1)/8;if((frames-1)%8==0)Setup(n);
                if(n>=4&&n<=8||n==12||n==13)FrameFace(n==8?180:0);
                if(frames%8==0){Capture(Names[n]);report.shots.Add(new Shot{name=Names[n],frame=frames,yaw=n==1||n==8||n==10?180:n==2?-90:n==3?90:0,lightYaw=n==5?90:n==6?0:160,battle=n>=11&&n<=12});}
                return;
            }
            int f=frames-Names.Length*8-1;
            if(f<9*8)
            {
                int rotation=f/24,axis=f/8%3;
                if(f%8==0)
                {
                    foreach(var r in skins)r.enabled=face.renderers.Contains(r);
                    controls.post.enabled=false;controls.profile.Apply(false);Freeze("Idle_Loop",0);
                    driver.transform.rotation=Quaternion.Euler(0,rotation*90,0)*initialRotation;
                    foreach(var m in faceMaterials){m.SetFloat("_DebugMode",1);m.SetFloat("_DebugFaceVector",axis+1);}
                }
                FrameFace(rotation*90);
                if(f%8==7)VerifyAxis(rotation,axis);
                return;
            }
            f-=9*8;
            if(f<clips.Length*5*5)
            {
                int pose=f/5,index=pose/5;float fraction=(pose%5)*.25f;
                if(f%5==0)
                {
                    foreach(var r in skins)r.enabled=true;
                    foreach(var m in faceMaterials){m.SetFloat("_DebugMode",0);m.SetFloat("_DebugFaceVector",0);}
                    driver.transform.rotation=initialRotation;Freeze(clips[index].name,fraction);
                }
                if(f%5>=1)FrameAll(0);
                if(f%5==4)VerifyBounds(clips[index].name,fraction);
                return;
            }
            if(report.shots.Count!=14||report.axes.Count!=9||report.bounds.Count!=75)throw new Exception("Incomplete render coverage");
            Finish(true,null);
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void Setup(int n)
    {
        driver.transform.rotation=initialRotation;
        foreach(var r in skins)r.enabled=true;
        foreach(var m in faceMaterials){m.SetFloat("_DebugMode",0);m.SetFloat("_DebugFaceVector",0);}
        bool battle=n==11||n==12;controls.profile.Apply(battle);controls.post.enabled=true;controls.post.lut=battle?controls.battlePost:null;
        sun.transform.rotation=Quaternion.Euler(25,n==5?90:n==6?0:160,0);
        if(n==13)Freeze("Idle_AFK",.25f);else Freeze("Idle_Loop",0);
        if(n==7||n==8)foreach(var r in skins)r.enabled=r.name.EndsWith("Body_2");
        if(n==9||n==10)foreach(var r in skins)r.enabled=r.name.Contains("Wing");
        if(n>=4&&n<=8||n==12||n==13)FrameFace(n==8?180:0);
        else FrameAll(n==1||n==10?180:n==2?-90:n==3?90:0);
    }
    void FrameFace(float yaw)
    {
        cam.orthographic=true;cam.orthographicSize=.34f;cam.aspect=720f/1024;cam.nearClipPlane=.001f;cam.farClipPlane=100;
        var center=face.head.position+Vector3.down*.12f;
        cam.transform.position=center+Quaternion.Euler(0,yaw,0)*Vector3.forward*1.4f;cam.transform.LookAt(center);
    }
    void FrameAll(float yaw)
    {
        var points=new List<Vector3>();
        foreach(var r in skins.Where(r=>r.enabled))
        {
            var mesh=new Mesh();r.BakeMesh(mesh);points.AddRange(mesh.vertices.Select(v=>r.transform.TransformPoint(v)));Destroy(mesh);
        }
        if(points.Count==0)throw new Exception("No geometry for render case");
        var rotation=Quaternion.Euler(0,yaw,0);var inv=Quaternion.Inverse(rotation);
        var bounds=new Bounds(inv*points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(inv*p);
        var center=rotation*bounds.center;cam.orthographic=true;cam.aspect=720f/1024;
        cam.orthographicSize=Mathf.Max(bounds.size.y/2,bounds.size.x/(2*cam.aspect))*1.08f;
        cam.transform.position=center+rotation*Vector3.forward*5;cam.transform.LookAt(center);
    }
    void Capture(string name)
    {
        var rt=new RenderTexture(720,1024,24);var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(720,1024,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,720,1024),0,0);tex.Apply();
        File.WriteAllBytes(Out+"/render-"+name+".png",tex.EncodeToPNG());
        RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Destroy(rt);Destroy(tex);
    }
    void VerifyAxis(int rotation,int axis)
    {
        // Float render targets preserve negative directions, unlike PNG. A
        // constant face region must reproduce all three signed world axes.
        var local=axis==0?face.headLocalForward:axis==1?face.headLocalRight:face.headLocalUp;
        var expected=face.head.TransformDirection(local).normalized;
        foreach(var r in face.renderers){var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);if(block.GetFloat("_HeadDirectionsWorldSpace")!=1)throw new Exception("World direction mode missing in player");}
        var rt=new RenderTexture(720,1024,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);rt.antiAliasing=1;
        var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(720,1024,TextureFormat.RGBAFloat,false,true);tex.ReadPixels(new Rect(0,0,720,1024),0,0);tex.Apply();
        int count=0;double x=0,y=0,z=0;
        foreach(var c in tex.GetPixels())
        {
            if(!float.IsFinite(c.r)||!float.IsFinite(c.g)||!float.IsFinite(c.b))throw new Exception("Nonfinite GPU output");
            var v=new Vector3(c.r,c.g,c.b);if((v-expected).magnitude<.002f){count++;x+=v.x;y+=v.y;z+=v.z;}
        }
        var actual=count>0?new Vector3((float)(x/count),(float)(y/count),(float)(z/count)):Vector3.zero;float error=(actual-expected).magnitude;
        report.axes.Add(new Axis{rotation=rotation*90,axis=axis,matchingPixels=count,expected=expected,actual=actual,error=error});
        RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Destroy(rt);Destroy(tex);
        if(count<10000||error>.002f)throw new Exception($"GPU world axis failed: rotation={rotation*90} axis={axis} pixels={count} error={error}");
    }
    void VerifyBounds(string clip,float fraction)
    {
        var rt=new RenderTexture(32,32,24);cam.targetTexture=rt;cam.Render();cam.targetTexture=null;rt.Release();Destroy(rt);
        var row=new PoseBounds{clip=clip,fraction=fraction};
        foreach(var r in skins)
        {
            var mesh=new Mesh();r.BakeMesh(mesh);var bounds=r.bounds;
            foreach(var v in mesh.vertices)
            {
                var p=r.transform.TransformPoint(v);row.vertices++;
                if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new Exception("Nonfinite live skin: "+r.name);
                float error=Vector3.Distance(p,bounds.ClosestPoint(p));
                if(error>row.maxDistanceOutsideBounds){row.maxDistanceOutsideBounds=error;row.worstRenderer=r.name;}
            }
            Destroy(mesh);
        }
        report.bounds.Add(row);
        if(row.maxDistanceOutsideBounds>.0001f)throw new Exception($"Live renderer bounds miss geometry: {clip} {fraction} {row.worstRenderer} {row.maxDistanceOutsideBounds}");
    }
    void Finish(bool pass,string error)
    {
        if(finished)return;finished=true;report.pass=pass;report.error=error;report.frames=frames;
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/player-render-verification.json",JsonUtility.ToJson(report,true));
        if(pass)Debug.Log("MODEL_READINESS_PLAYER_VERIFIED");else Debug.LogError(error);Application.Quit(pass?0:1);
    }
}
#endif
