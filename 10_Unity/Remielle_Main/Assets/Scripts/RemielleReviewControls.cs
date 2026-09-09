using UnityEngine;
[DefaultExecutionOrder(200)]
public class RemielleReviewControls : MonoBehaviour
{
    public RemielleNativeAnimation model;
    public RemielleRuntimeProfile profile;
    public RemiellePostLut post;
    public Texture2D battlePost;
    public Transform follow;
    public float followDistance=8;
    Vector2 scroll;
    void Start(){post.lut=profile.battle?battlePost:null;}
    void LateUpdate()
    {
        if(follow==null)return;
        var center=follow.position+Vector3.up*.15f;
        transform.position=center+Vector3.forward*followDistance;
        transform.LookAt(center);
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12,12,220,Screen.height-24),GUI.skin.box);
        GUILayout.Label("Remielle - native animation review");
        bool battle=GUILayout.Toggle(profile.battle,"Combat LUT profile");
        if(battle!=profile.battle){profile.Apply(battle);post.lut=battle?battlePost:null;}
        scroll=GUILayout.BeginScrollView(scroll);
        foreach(AnimationState state in model.nativeAnimation)
            if(GUILayout.Button(state.name))model.Play(state.name);
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
}
