using UnityEngine;
using System.Collections.Generic;

// Explicit scene profiles: native LUTs vary between menu and combat captures.
[ExecuteAlways]
public class RemielleRuntimeProfile : MonoBehaviour
{
    public Texture2D characterMenu, characterBattle;
    // Supplied by the rendering-review scene. Keeping these transient prevents
    // rendering experiments from changing the protected model prefab.
    [System.NonSerialized] public Texture2D capturedWhite;
    [System.NonSerialized] public Texture2D secondaryWings;
    [System.NonSerialized] public Texture2D secondaryWeapon01;
    [System.NonSerialized] public Texture2D secondaryMaskWeapon01;
    [System.NonSerialized] public Texture2D secondaryMaskBody2;
    [System.NonSerialized] public Texture2D specialWeapon01;
    public bool battle;
    readonly Dictionary<SkinnedMeshRenderer,Material[]> originals=new();
    readonly Dictionary<Material,Material> instances=new();
    public void Apply(bool useBattle)
    {
        battle=useBattle;
        foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!originals.ContainsKey(renderer))
            {
                var materials=renderer.sharedMaterials;originals.Add(renderer,materials);
                var runtime=(Material[])materials.Clone();
                for(int i=0;i<runtime.Length;i++)
                {
                    var original=runtime[i];
                    if(original==null||!original.HasProperty("_Lut2DTex")||original.GetFloat("_MaterialType")==3)continue;
                    if(!instances.TryGetValue(original,out var instance))
                    {instance=new Material(original){name=original.name+" (Runtime Profile)"};instances.Add(original,instance);}
                    runtime[i]=instance;
                }
                renderer.sharedMaterials=runtime;
            }
        }
        foreach(var pair in instances)
        {
            var original=pair.Key;var material=pair.Value;
            material.SetTexture("_Lut2DTex",battle?characterBattle:characterMenu);
            material.SetVector("_Lut2DTexParam",new Vector4(1f/1024,1f/32,31,battle?1.0717734098434448f:1));
            material.SetFloat("_EnableLUT",1);
            ApplyCapturedMaterialState(original.name,material,battle);
        }
    }
    void ApplyCapturedMaterialState(string sourceName,Material material,bool useBattle)
    {
        if(!HasCapturedBattleResources())return;
        if(!material.HasProperty("_SecondaryEmission"))return;
        material.SetFloat("_SecondaryEmission",0);
        material.SetFloat("_SecondaryEmissionChannel",0);
        material.SetFloat("_SecondaryEmissionUseUV2",0);
        material.SetFloat("_SecondaryEmissionMaskChannel",0);
        material.SetFloat("_MultiplyAlbedo",1);
        material.SetColor("_SecondaryEmissionColor",Color.white);
        material.SetVector("_SecondaryEmissionTexSpeed",Vector4.zero);
        material.SetTexture("_SecondaryEmissionTex",null);
        material.SetTexture("_SecondaryEmissionMaskTex",null);
        if(material.HasProperty("_SpecialWeaponEmission"))
        {
            material.SetFloat("_SpecialWeaponEmission",0);
            material.SetTexture("_SpecialWeaponEmissionTex",null);
            material.SetTexture("_SpecialWeaponEmissionMaskTex",null);
            material.SetVector("_SpecialWeaponEmissionTexSpeed",Vector4.zero);
            material.SetColor("_SpecialWeaponEmissionColor",Color.white);
            material.SetColor("_SpecialWeaponEmissionColor2",Color.white);
            material.SetVector("_SpecialWeaponMergeParam01",new Vector4(0,0,1,0));
            material.SetVector("_SpecialWeaponMergeParam02",Vector4.zero);
            material.SetVector("_SpecialWeaponMergeParam03",new Vector4(0,0,0,1));
        }
        if(!useBattle)return;
        switch(sourceName)
        {
            case "MAT_Remielle_Wings":
                RequireTexture(secondaryWings,name+" Wings secondary emission");
                RequireTexture(capturedWhite,name+" captured white");
                SetSecondary(material,secondaryWings,capturedWhite,new Color(0.09660401195f,0.05233179033f,0.5283018947f,1));
                break;
            case "MAT_Remielle_Weapon_01":
                RequireTexture(secondaryWeapon01,name+" Weapon_01 secondary emission");
                RequireTexture(secondaryMaskWeapon01,name+" Weapon_01 secondary mask");
                RequireTexture(specialWeapon01,name+" Weapon_01 signature emission");
                RequireTexture(capturedWhite,name+" captured white");
                SetSecondary(material,secondaryWeapon01,secondaryMaskWeapon01,new Color(0.3254716992f,0.3886048496f,1,1));
                if(!material.HasProperty("_SpecialWeaponEmission"))throw new System.InvalidOperationException("Signature weapon shader properties missing: "+material.shader.name);
                material.SetFloat("_SpecialWeaponEmission",1);
                material.SetTexture("_SpecialWeaponEmissionTex",specialWeapon01);
                material.SetTexture("_SpecialWeaponEmissionMaskTex",capturedWhite);
                material.SetColor("_SpecialWeaponEmissionColor",new Color(50.59871674f,35.88465118f,217.35849f,1));
                break;
            case "MAT_Remielle_Origin_Body_2":
                RequireTexture(capturedWhite,name+" captured white");
                RequireTexture(secondaryMaskBody2,name+" Body_2 secondary mask");
                SetSecondary(material,capturedWhite,secondaryMaskBody2,new Color(0.4170523584f,0.43884781f,1.245283008f,1));
                break;
        }
    }
    static void SetSecondary(Material material,Texture emission,Texture mask,Color color)
    {
        material.SetFloat("_SecondaryEmission",1);
        material.SetFloat("_SecondaryEmissionChannel",1);
        material.SetFloat("_SecondaryEmissionUseUV2",0);
        material.SetFloat("_SecondaryEmissionMaskChannel",0);
        material.SetFloat("_MultiplyAlbedo",0);
        material.SetColor("_SecondaryEmissionColor",color);
        material.SetTexture("_SecondaryEmissionTex",emission);
        material.SetTexture("_SecondaryEmissionMaskTex",mask);
    }
    static void RequireTexture(Texture texture,string label)
    {
        if(!texture)throw new System.InvalidOperationException("Missing captured runtime texture: "+label);
    }
    bool HasCapturedBattleResources()
    {
        bool any=capturedWhite||secondaryWings||secondaryWeapon01||secondaryMaskWeapon01||secondaryMaskBody2||specialWeapon01;
        bool all=capturedWhite&&secondaryWings&&secondaryWeapon01&&secondaryMaskWeapon01&&secondaryMaskBody2&&specialWeapon01;
        if(any&&!all)throw new System.InvalidOperationException("Captured battle material resources are only partially assigned");
        return all;
    }
    // ExecuteAlways enables cleanup after explicit editor audit/preview calls;
    // simply opening a scene must not instantiate or change its materials.
    void Start(){if(Application.isPlaying)Apply(battle);}
    void OnDestroy()
    {
        foreach(var pair in originals)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;
        foreach(var material in instances.Values)
            if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);
        originals.Clear();instances.Clear();
    }
}
