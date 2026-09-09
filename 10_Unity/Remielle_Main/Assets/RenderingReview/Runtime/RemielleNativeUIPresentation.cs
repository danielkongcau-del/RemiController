using System;
using UnityEngine;

// Scene view uses Unity's image-effect copy, hence its own camera and graph.
// OnRenderImage runs after animation/face updates and writes the visible target.
[ExecuteAlways,ImageEffectAllowedInSceneView,RequireComponent(typeof(Camera)),DefaultExecutionOrder(300)]
public sealed class RemielleNativeUIPresentation : MonoBehaviour
{
    public RemielleNativeAnimation model;
    public RemielleNativeUIPresentationProfile[] profiles;
    [Range(0,1)] public int profileIndex=1;
    public Vector3 lightEuler;
    public Vector3 mainMultiplier=Vector3.one;
    [Range(0,3)] public float ambientMultiplier=1;
    public bool autoFrame=true;
    [Range(-180,180)] public float viewYaw;
    [Range(-60,60)] public float viewElevation=-2.68f;
    [Range(1,2)] public float distanceMultiplier=1;
    public RemielleNativeUIFrameGraph Graph {get;private set;}
    public long PresentedFrames {get;private set;}
    public int PresentedWidth {get;private set;}
    public int PresentedHeight {get;private set;}
    public string LastError {get;private set;}
    public event Action<RemielleNativeUIPresentation,RenderTexture> FramePresented;
    Camera renderCamera;
    RemielleNativeAnimation boundModel;
    bool reset=true,dirty=true;
    void OnEnable(){dirty=true;reset=true;LastError=null;}
    void OnValidate(){dirty=true;reset=true;}
    public void ResetHistory(){reset=true;}
    public void SetProfile(int index)
    {
        if(profiles==null||index<0||index>=profiles.Length)throw new ArgumentOutOfRangeException(nameof(index));
        if(profileIndex==index)return;profileIndex=index;dirty=true;reset=true;
    }
    public void ReleaseGraph(){Graph?.Dispose();Graph=null;boundModel=null;dirty=true;reset=true;}
    void OnDisable(){ReleaseGraph();}
    void OnDestroy(){ReleaseGraph();}
    void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        bool previousSrgb=GL.sRGBWrite;
        try
        {
            if(!renderCamera)renderCamera=GetComponent<Camera>();
            // Orthographic Scene editing keeps its regular editor drawing.
            // The recovered game page and its depth reconstruction are perspective.
            if(!model||profiles==null||profileIndex<0||profileIndex>=profiles.Length||!profiles[profileIndex]||renderCamera.orthographic||source.width<4||source.height<4)
            {GL.sRGBWrite=destination?destination.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(source,destination);return;}
            if(dirty||Graph==null||Graph.Profile!=profiles[profileIndex]||boundModel!=model)
            {ReleaseGraph();Graph=new RemielleNativeUIFrameGraph(profiles[profileIndex],model,renderCamera);boundModel=model;dirty=false;}
            renderCamera.aspect=(float)source.width/source.height;
            if(autoFrame&&renderCamera.cameraType==CameraType.Game)Graph.FrameCamera(viewYaw,viewElevation,distanceMultiplier);
            Graph.Render(source.width,source.height,Quaternion.Euler(lightEuler),mainMultiplier,ambientMultiplier,reset);reset=false;
            GL.sRGBWrite=destination?destination.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;
            Graphics.Blit(Graph.FinalColor,destination);
            PresentedWidth=source.width;PresentedHeight=source.height;PresentedFrames++;LastError=null;
            FramePresented?.Invoke(this,destination);
        }
        catch(Exception exception)
        {
            ReleaseGraph();LastError=exception.ToString();Debug.LogException(exception,this);
            GL.sRGBWrite=destination?destination.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(source,destination);
        }
        // Unity requires the destination to remain active when this callback returns.
        // Observers may perform readbacks, so restore this target explicitly.
        finally{Graphics.SetRenderTarget(destination);GL.sRGBWrite=previousSrgb;}
    }
}
