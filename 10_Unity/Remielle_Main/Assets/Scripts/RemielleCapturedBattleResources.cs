using UnityEngine;

// Review-scene boundary for captured runtime-only textures.  The protected V3
// model prefab remains independent of rendering experiments.
[DefaultExecutionOrder(-10000)]
public class RemielleCapturedBattleResources : MonoBehaviour
{
    public RemielleRuntimeProfile profile;
    public Texture2D capturedWhite;
    public Texture2D secondaryWings;
    public Texture2D secondaryWeapon01;
    public Texture2D secondaryMaskWeapon01;
    public Texture2D secondaryMaskBody2;
    public Texture2D specialWeapon01;
    public void Apply()
    {
        if(!profile)throw new System.InvalidOperationException("Runtime profile missing from captured battle resources");
        profile.capturedWhite=capturedWhite;profile.secondaryWings=secondaryWings;
        profile.secondaryWeapon01=secondaryWeapon01;profile.secondaryMaskWeapon01=secondaryMaskWeapon01;
        profile.secondaryMaskBody2=secondaryMaskBody2;profile.specialWeapon01=specialWeapon01;
    }
    void Awake(){Apply();}
}
