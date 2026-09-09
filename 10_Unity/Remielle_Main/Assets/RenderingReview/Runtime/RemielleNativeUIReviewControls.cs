using System;
using System.Linq;
using UnityEngine;

// Presentation and animation review only. No movement, attack, ability event
// or gameplay state-machine logic is implemented here.
[DefaultExecutionOrder(250)]
public sealed class RemielleNativeUIReviewControls : MonoBehaviour
{
    public RemielleNativeUIPresentation presentation;
    public bool showUI=true;
    string[] clips=Array.Empty<string>();
    int selected;
    bool faceView;
    Transform head;
    float fps;
    Vector2 scroll;
    void Start()
    {
        if(!presentation||!presentation.model)throw new InvalidOperationException("Native presentation controls are not configured");
        clips=presentation.model.nativeAnimation.Cast<AnimationState>().Select(s=>s.name).OrderBy(n=>n).ToArray();
        selected=Mathf.Max(0,Array.IndexOf(clips,presentation.model.initialClip));
        head=presentation.model.bones.Single(b=>b.source.name=="Bip001 Head").source;
        Application.runInBackground=true;
    }
    void Update()
    {
        #if ENABLE_INPUT_SYSTEM
        if(UnityEngine.InputSystem.Keyboard.current?.hKey.wasPressedThisFrame==true)showUI=!showUI;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if(Input.GetKeyDown(KeyCode.H))showUI=!showUI;
#endif
        if(Time.unscaledDeltaTime>0)fps=Mathf.Lerp(fps,1f/Time.unscaledDeltaTime,.08f);
    }
    void LateUpdate()
    {
        if(!faceView||!head)return;
        var camera=presentation.GetComponent<Camera>();var offset=Quaternion.Euler(presentation.viewElevation,presentation.viewYaw,0)*Vector3.forward;
        camera.transform.SetPositionAndRotation(head.position+offset*.65f,Quaternion.LookRotation(-offset,Vector3.up));
    }
    public void PlayClip(string name)
    {
        selected=Array.IndexOf(clips,name);presentation.model.Play(name,.18f);
    }
    void OnGUI()
    {
        if(!Application.isPlaying||Application.isBatchMode||!presentation||clips.Length==0)return;
        if(!showUI){if(GUI.Button(new Rect(16,16,130,32),"显示控制面板 · H"))showUI=true;return;}
        GUILayout.BeginArea(new Rect(16,16,300,Mathf.Min(Screen.height-32,740)),GUI.skin.box);
        scroll=GUILayout.BeginScrollView(scroll);
        GUILayout.Label("Remielle · 原生展示");
        GUILayout.Label(presentation.PresentedWidth+" × "+presentation.PresentedHeight+"   "+Mathf.RoundToInt(fps)+" FPS");
        int page=GUILayout.Toolbar(presentation.profileIndex,new[]{"角色展示","时装商店"});if(page!=presentation.profileIndex)presentation.SetProfile(page);
        GUILayout.Space(7);GUILayout.Label("动画 · 原始速度");
        int next=GUILayout.SelectionGrid(selected,clips,1);if(next!=selected)PlayClip(clips[next]);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button(Time.timeScale==0?"继续":"暂停"))Time.timeScale=Time.timeScale==0?1:0;
        if(GUILayout.Button("默认姿态")){presentation.model.nativeAnimation.Stop();presentation.model.ResetSourcePose();presentation.model.ApplyPose();selected=-1;presentation.ResetHistory();}
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("全身")){faceView=false;presentation.autoFrame=true;presentation.distanceMultiplier=1;presentation.ResetHistory();}
        if(GUILayout.Button("面部")){faceView=true;presentation.autoFrame=false;presentation.ResetHistory();}
        GUILayout.EndHorizontal();
        GUILayout.Label("镜头方位");presentation.viewYaw=GUILayout.HorizontalSlider(presentation.viewYaw,-180,180);
        GUILayout.Label("灯光方位");var light=presentation.lightEuler;light.y=GUILayout.HorizontalSlider(light.y,-180,180);presentation.lightEuler=light;
        GUILayout.Label("主光强度");float main=GUILayout.HorizontalSlider(presentation.mainMultiplier.x,0,2);presentation.mainMultiplier=Vector3.one*main;
        GUILayout.Label("环境光强度");presentation.ambientMultiplier=GUILayout.HorizontalSlider(presentation.ambientMultiplier,0,2);
        if(GUILayout.Button("恢复采集灯光")){presentation.lightEuler=Vector3.zero;presentation.mainMultiplier=Vector3.one;presentation.ambientMultiplier=1;presentation.ResetHistory();}
        if(GUILayout.Button("隐藏面板 · H"))showUI=false;
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
}
