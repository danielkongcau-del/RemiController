using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

public static class RemielleNativeUIPresentationAudit
{
    const string Out=RemielleNativeUIPresentationBuild.Out+"editor/";
    static JObject Ref(string path)=>RemielleUINativePostBuild.Ref(path);
    static RenderTexture Target(int w,int h)
    {
        var t=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R8G8B8A8_SRGB,GraphicsFormat.D32_SFloat)){hideFlags=HideFlags.HideAndDontSave};
        if(!t.Create())throw new Exception("Presentation audit target unavailable");return t;
    }
    static void Release(RenderTexture t){if(!t)return;t.Release();Object.DestroyImmediate(t);}
    static JArray TargetInventory()=>new JArray(Resources.FindObjectsOfTypeAll<RenderTexture>().Select(t=>new JObject{["id"]=t.GetInstanceID(),["name"]=t.name,["width"]=t.width,["height"]=t.height,["format"]=t.graphicsFormat.ToString(),["flags"]=t.hideFlags.ToString(),["created"]=t.IsCreated()}));
    // Every owned graph/audit target uses exactly HideAndDontSave (61).
    // Camera OnRenderImage also creates engine TempBuffers with internal flags (125).
    // These remain in Unity's cache after Camera.Render and are not graph allocations.
    static int PrivateTargets()=>Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.hideFlags==HideFlags.HideAndDontSave);
    public static JObject CheckImage(RemielleNativeUIPresentation view,RenderTexture target,string stem)
    {
        if(view.LastError!=null||view.Graph==null)throw new Exception("Visible native graph failed: "+view.LastError);
        var bytes=RemielleUINativePostAudit.Read(target,target.width*target.height*4);
        var native=RemielleUINativePostAudit.Read(view.Graph.FinalColor,bytes.Length);
        int max=0,changed=0;for(int i=0;i<bytes.Length;i++){int d=Math.Abs(bytes[i]-native[i]);max=Math.Max(max,d);if(d!=0)changed++;}
        if(max>1)throw new Exception("Visible sRGB presentation changed native final color: "+max);
        if(view.PresentedWidth!=target.width||view.PresentedHeight!=target.height)throw new Exception("Visible renderer reduced camera resolution");
        if(!view.Graph.DepthHierarchyUpdated||view.Graph.DepthHierarchy.Width!=target.width/2||view.Graph.DepthHierarchy.Height!=target.height/2)
            throw new Exception("Current half-resolution producer not connected");
        File.WriteAllBytes(stem+".rgba8",bytes);File.WriteAllBytes(stem+"-native.rgba8",native);
        var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,false);
        try{texture.LoadRawTextureData(bytes);texture.Apply();File.WriteAllBytes(stem+".png",texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
        return new JObject{["width"]=target.width,["height"]=target.height,["maxPresentationByteDifference"]=max,["changedPresentationBytes"]=changed,["presented"]=Ref(stem+".rgba8"),["nativeFinal"]=Ref(stem+"-native.rgba8"),["preview"]=Ref(stem+".png"),["fullResolution"]=true,["depthHierarchyUpdated"]=true};
    }
    public static void Run()
    {
        Directory.CreateDirectory(Out);EditorSceneManager.OpenScene(RemielleNativeUIPresentationBuild.ScenePath);
        var view=Object.FindFirstObjectByType<RemielleNativeUIPresentation>();var camera=view.GetComponent<Camera>();camera.enabled=false;
        var driver=view.model;driver.autoplay=false;driver.nativeAnimation.Stop();driver.ResetSourcePose();driver.ApplyPose();
        view.ReleaseGraph();File.WriteAllText(Out+"targets-before.json",TargetInventory().ToString());int before=PrivateTargets();var cases=new JArray();RenderTexture target=null;GameObject second=null;
        try
        {
            for(int i=0;i<6;i++)
            {
                camera.targetTexture=null;Release(target);target=Target(i==2?1377:i==4?1280:1920,i==2?823:i==4?720:1080);camera.targetTexture=target;
                view.SetProfile(i%2);view.viewYaw=new[]{0,65,180,0,65,180}[i];view.lightEuler=new Vector3(0,i==3?180:i==4?90:0,0);
                if(i==0){driver.ResetSourcePose();driver.ApplyPose();}else driver.Sample(i%2==0?"Walk_Start":"Idle_Loop",.37f);
                view.ResetHistory();long first=view.PresentedFrames;
                for(int frame=0;frame<16;frame++)camera.Render();
                if(view.PresentedFrames-first!=16||view.Graph.Temporal.SampleIndex!=16)throw new Exception("Visible camera history/frame count mismatch");
                var row=CheckImage(view,target,Out+"view-"+i);row["id"]="view-"+i;row["profile"]=view.Graph.Profile.profileName;row["yaw"]=view.viewYaw;row["frames"]=16;cases.Add(row);
            }
            // Exercise the same copied-image-effect contract as Scene view,
            // without creating, opening or controlling an Editor window.
            second=new GameObject("Independent Scene-type camera audit");var sceneCamera=second.AddComponent<Camera>();sceneCamera.CopyFrom(camera);sceneCamera.enabled=false;sceneCamera.cameraType=CameraType.SceneView;
            var sceneView=second.AddComponent<RemielleNativeUIPresentation>();EditorUtility.CopySerialized(view,sceneView);sceneView.autoFrame=false;
            using(var framing=new RemielleNativeUIFrameGraph(sceneView.profiles[sceneView.profileIndex],driver,sceneCamera))framing.FrameCamera(35,-3);
            var sceneTarget=Target(960,540);sceneCamera.targetTexture=sceneTarget;
            try
            {
                long primaryFrames=view.Graph.Frames;uint primarySamples=view.Graph.Temporal.SampleIndex;
                for(int i=0;i<4;i++)sceneCamera.Render();
                if(view.Graph.Frames!=primaryFrames||view.Graph.Temporal.SampleIndex!=primarySamples||ReferenceEquals(view.Graph,sceneView.Graph)||view.Graph.Temporal.Color==sceneView.Graph.Temporal.Color)
                    throw new Exception("Camera histories are shared");
                var row=CheckImage(sceneView,sceneTarget,Out+"scene-camera");row["id"]="scene-camera";row["cameraHistoryIndependent"]=true;row["frames"]=4;cases.Add(row);
            }
            finally{sceneCamera.targetTexture=null;Release(sceneTarget);Object.DestroyImmediate(second);second=null;}
            camera.targetTexture=null;Release(target);target=null;view.ReleaseGraph();
            File.WriteAllText(Out+"targets-after.json",TargetInventory().ToString());
            if(PrivateTargets()!=before)throw new Exception("Presentation graph leaked private targets: before="+before+" after="+PrivateTargets());
            var files=new JArray();foreach(string name in new[]{"RemielleNativeUIPresentationProfile.cs","RemielleNativeUIFrameGraph.cs","RemielleNativeUIPresentation.cs","RemielleNativeUIReviewControls.cs"})files.Add(Ref("Assets/RenderingReview/Runtime/"+name));
            files.Add(Ref("Assets/Editor/RemielleNativeUIPresentationAudit.cs"));files.Add(Ref(RemielleNativeUIPresentationBuild.ScenePath));
            File.WriteAllText(Out+"verification.json",new JObject{["pass"]=true,["cameraFrames"]=100,["cases"]=cases,["privateTargetsBefore"]=before,["privateTargetsAfter"]=PrivateTargets(),["implementation"]=files,["boundary"]="Actual camera OnRenderImage output, full pixel dimensions including odd resize, native color transfer and independent Scene-type camera. Real continuous Player, scene-view GUI acceptance and accessory consumer attribution remain separate."}.ToString());
            Debug.Log("REMIELLE_NATIVE_PRESENTATION_CAMERA_OK 100");
        }
        finally{camera.targetTexture=null;Release(target);if(second)Object.DestroyImmediate(second);view.ReleaseGraph();}
    }
}
