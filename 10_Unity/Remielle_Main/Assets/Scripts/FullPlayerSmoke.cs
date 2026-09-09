#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using UnityEngine;

// Runs only in the locally built development player with -remielleAuditSmoke.
[DefaultExecutionOrder(10000)]
public class FullPlayerSmoke : MonoBehaviour
{
    static string Out { get { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,"-remielleAuditOutput"); return i>=0&&i+1<args.Length?args[i+1]:"E:/ZZZ/local-only/RemielleModelReadiness/20260904"; } }
    [Serializable] class Result
    {
        public bool pass;
        public string utc,unity,platform,graphicsDevice,graphicsApi,error;
        public int frames,renderers,materialSlots,clips,cloneCycles;
        public float maxBridgeError,maxPauseError,maxSourceMotion;
        public bool postShaderRetained,menuCapture,battleCapture,materialsReleased;
    }
    readonly Result result=new();
    RemielleNativeAnimation driver;
    RemielleReviewControls controls;
    Camera captureCamera;
    GameObject clone;
    Material[] owned;
    Matrix4x4[] pausePose,initialPose;
    int frames;
    bool finished;
    double started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Environment.GetCommandLineArgs().Contains("-remielleAuditSmoke"))
            new GameObject("OptInPlayerSmoke").AddComponent<FullPlayerSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=OnLog;}
    void OnDestroy(){Application.logMessageReceived-=OnLog;}
    void OnLog(string message,string stack,LogType type)
    {
        if(!finished&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))Finish(false,message+"\n"+stack);
    }
    void Start()
    {
        try
        {
            started=Time.realtimeSinceStartupAsDouble;
            Time.captureDeltaTime=1f/60;Application.runInBackground=true;
            driver=FindFirstObjectByType<RemielleNativeAnimation>();controls=FindFirstObjectByType<RemielleReviewControls>();captureCamera=Camera.main;
            if(driver==null||controls==null||captureCamera==null)throw new Exception("Player scene bindings missing");
            var renderers=driver.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled).ToArray();
            result.renderers=renderers.Length;result.materialSlots=renderers.Sum(r=>r.sharedMaterials.Length);result.clips=driver.nativeAnimation.Cast<AnimationState>().Count();
            if(result.renderers!=27||result.materialSlots!=31||result.clips!=15)throw new Exception("Player asset counts changed");
            foreach(var r in renderers)
            {
                if(r.bones.Length!=r.sharedMesh.bindposes.Length||r.bones.Any(b=>b==null))throw new Exception("Invalid player skin: "+r.name);
                foreach(var m in r.sharedMaterials)
                {
                    if(m==null||m.shader==null||!m.shader.isSupported)throw new Exception("Unsupported player material: "+r.name);
                    if(m.GetFloat("_DoubleSided")>0&&m.GetFloat("_DoubleUV")!=3)throw new Exception("Wrong player backface UV route");
                }
            }
            result.postShaderRetained=controls.post.lutShader!=null&&controls.post.lutShader.isSupported;
            if(!result.postShaderRetained)throw new Exception("Post LUT shader stripped from player");
            result.utc=DateTime.UtcNow.ToString("O");result.unity=Application.unityVersion;result.platform=Application.platform.ToString();
            result.graphicsDevice=SystemInfo.graphicsDeviceName;result.graphicsApi=SystemInfo.graphicsDeviceType.ToString();
            initialPose=driver.bones.Select(b=>b.source.localToWorldMatrix).ToArray();
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    void LateUpdate()
    {
        if(finished||driver==null)return;
        try
        {
            if(Time.realtimeSinceStartupAsDouble-started>180)throw new Exception("Player smoke timed out");
            frames++;
            for(int i=0;i<driver.bones.Length;i++)
            {
                var b=driver.bones[i];var source=b.source.localToWorldMatrix;
                result.maxBridgeError=Mathf.Max(result.maxBridgeError,Error(source*b.basis,b.target.localToWorldMatrix));
                result.maxSourceMotion=Mathf.Max(result.maxSourceMotion,Error(source,initialPose[i]));
            }
            if(!(result.maxBridgeError<.0001f))throw new Exception("Player bridge mismatch: "+result.maxBridgeError);
            if(frames==30){Capture("player-menu.png");result.menuCapture=true;}
            if(frames==40){pausePose=driver.bones.Select(b=>b.source.localToWorldMatrix).ToArray();Time.timeScale=0;}
            if(frames>40&&frames<48)
                for(int i=0;i<pausePose.Length;i++)result.maxPauseError=Mathf.Max(result.maxPauseError,Error(pausePose[i],driver.bones[i].source.localToWorldMatrix));
            if(frames==48){Time.timeScale=1;if(result.maxPauseError>1e-5)throw new Exception("Paused pose changed");}
            if(frames==60){controls.profile.Apply(true);controls.post.lut=controls.battlePost;}
            if(frames==90){Capture("player-battle.png");result.battleCapture=true;}
            if(frames==110)driver.Play("Run_Transform_01");
            if(frames==150)Capture("player-motion.png");
            if(frames>=180&&frames<240)
            {
                int phase=(frames-180)%20;
                if(phase==0)
                {
                    clone=Instantiate(driver.gameObject);clone.transform.position+=Vector3.right*1000;
                    var bridge=clone.GetComponent<RemielleNativeAnimation>();bridge.autoplay=false;bridge.nativeAnimation.Stop();bridge.enabled=false;
                }
                if(phase==3)
                {
                    var original=driver.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).ToHashSet();
                    owned=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&!original.Contains(m)).Distinct().ToArray();
                    if(owned.Length==0||owned.Length>13)throw new Exception("Clone profile ownership invalid");
                    Destroy(clone);
                }
                if(phase==8)
                {
                    if(owned.Any(m=>m!=null))throw new Exception("Player runtime materials leaked after clone destruction");
                    result.cloneCycles++;
                }
            }
            if(frames>=240)
            {
                result.materialsReleased=result.cloneCycles==3;
                if(!result.materialsReleased||result.maxSourceMotion<.001f)throw new Exception("Player motion/lifetime test incomplete");
                Finish(true,null);
            }
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    static float Error(Matrix4x4 a,Matrix4x4 b)
    {
        float result=0;
        for(int i=0;i<16;i++){float e=Mathf.Abs(a[i]-b[i]);if(float.IsNaN(e)||float.IsInfinity(e))return float.PositiveInfinity;result=Mathf.Max(result,e);}
        return result;
    }
    void Capture(string name)
    {
        var rt=new RenderTexture(720,1024,24);captureCamera.targetTexture=rt;captureCamera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(720,1024,TextureFormat.RGBA32,false);
        tex.ReadPixels(new Rect(0,0,720,1024),0,0);tex.Apply();File.WriteAllBytes(Out+"/"+name,tex.EncodeToPNG());
        RenderTexture.active=previous;captureCamera.targetTexture=null;rt.Release();Destroy(rt);Destroy(tex);
    }
    void Finish(bool pass,string error)
    {
        if(finished)return;finished=true;Time.timeScale=1;
        result.pass=pass;result.error=error;result.frames=frames;
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/player-smoke.json",JsonUtility.ToJson(result,true));
        if(pass)Debug.Log("REMielle_PLAYER_SMOKE_VERIFIED");else Debug.LogError(error);
        Application.Quit(pass?0:1);
    }
}
#endif
