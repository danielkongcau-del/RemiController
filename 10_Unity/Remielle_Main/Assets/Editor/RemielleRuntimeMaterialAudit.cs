using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RemielleRuntimeMaterialAudit
{
    const string ScenePath="Assets/RenderingReview/Remielle_LightingReview.unity";
    const string Out="E:/ZZZ/local-only/RemielleRenderingReview/20260905/runtime-material-verification.json";
    static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<1e-5f;
    static void ColorEquals(Material m,string property,Color expected)
    {
        Color actual=m.GetColor(property);
        Require(Near(actual.r,expected.r)&&Near(actual.g,expected.g)&&Near(actual.b,expected.b)&&Near(actual.a,expected.a),property+" mismatch on "+m.name+": "+actual);
    }
    static Material Find(Material[] materials,string sourceName)
    {
        var rows=materials.Where(m=>m&&m.name==sourceName+" (Runtime Profile)").Distinct().ToArray();
        Require(rows.Length==1,"Expected one runtime material for "+sourceName+", found "+rows.Length);
        return rows[0];
    }
    static void TextureEquals(Material m,string property,Texture expected)
    {
        Require(m.GetTexture(property)==expected,property+" binding mismatch on "+m.name);
    }
    public static void Run()
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/HoyoToonZenlessZoneZero.shader");
        var maskShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/ReviewBloomMask.shader");
        Require(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Review character shader unavailable or invalid");
        Require(maskShader&&maskShader.isSupported&&!ShaderUtil.ShaderHasError(maskShader),"Review Bloom mask shader unavailable or invalid");
        var profile=UnityEngine.Object.FindFirstObjectByType<RemielleRuntimeProfile>();
        Require(profile,"Runtime profile missing from review scene");
        var resources=UnityEngine.Object.FindFirstObjectByType<RemielleCapturedBattleResources>();
        Require(resources,"Captured battle resource boundary missing from review scene");resources.Apply();
        profile.Apply(false);
        var menu=profile.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
        foreach(var sourceName in new[]{"MAT_Remielle_Wings","MAT_Remielle_Weapon_01","MAT_Remielle_Origin_Body_2"})
        {
            var m=Find(menu,sourceName);Require(m.GetFloat("_SecondaryEmission")<.5f,"Menu secondary emission enabled on "+sourceName);
            Require(m.GetFloat("_SpecialWeaponEmission")<.5f,"Menu signature emission enabled on "+sourceName);
        }
        profile.Apply(true);
        var battle=profile.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
        var wings=Find(battle,"MAT_Remielle_Wings");
        var weapon=Find(battle,"MAT_Remielle_Weapon_01");
        var body2=Find(battle,"MAT_Remielle_Origin_Body_2");
        foreach(var m in new[]{wings,weapon,body2})
        {
            Require(Near(m.GetFloat("_SecondaryEmission"),1)&&Near(m.GetFloat("_SecondaryEmissionChannel"),1),"Captured secondary flags missing on "+m.name);
            Require(Near(m.GetFloat("_SecondaryEmissionUseUV2"),0)&&Near(m.GetFloat("_SecondaryEmissionMaskChannel"),0)&&Near(m.GetFloat("_MultiplyAlbedo"),0),"Captured secondary routing mismatch on "+m.name);
        }
        TextureEquals(wings,"_SecondaryEmissionTex",profile.secondaryWings);TextureEquals(wings,"_SecondaryEmissionMaskTex",profile.capturedWhite);
        TextureEquals(weapon,"_SecondaryEmissionTex",profile.secondaryWeapon01);TextureEquals(weapon,"_SecondaryEmissionMaskTex",profile.secondaryMaskWeapon01);
        TextureEquals(body2,"_SecondaryEmissionTex",profile.capturedWhite);TextureEquals(body2,"_SecondaryEmissionMaskTex",profile.secondaryMaskBody2);
        ColorEquals(wings,"_SecondaryEmissionColor",new Color(.09660401195f,.05233179033f,.5283018947f,1));
        ColorEquals(weapon,"_SecondaryEmissionColor",new Color(.3254716992f,.3886048496f,1,1));
        ColorEquals(body2,"_SecondaryEmissionColor",new Color(.4170523584f,.43884781f,1.245283008f,1));
        Require(Near(weapon.GetFloat("_SpecialWeaponEmission"),1),"Signature weapon flag missing");
        TextureEquals(weapon,"_SpecialWeaponEmissionTex",profile.specialWeapon01);TextureEquals(weapon,"_SpecialWeaponEmissionMaskTex",profile.capturedWhite);
        ColorEquals(weapon,"_SpecialWeaponEmissionColor",new Color(50.59871674f,35.88465118f,217.35849f,1));
        profile.Apply(false);
        Require(Find(profile.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray(),"MAT_Remielle_Weapon_01").GetFloat("_SpecialWeaponEmission")<.5f,"Battle-to-menu signature emission did not reset");
        Directory.CreateDirectory(Path.GetDirectoryName(Out));
        File.WriteAllText(Out,new JObject{
            ["pass"]=true,["scene"]=ScenePath,["shader"]=shader.name,["maskShader"]=maskShader.name,
            ["runtimeMaterials"]=battle.Count(m=>m.name.EndsWith(" (Runtime Profile)")),
            ["secondaryMaterials"]=new JArray("MAT_Remielle_Wings","MAT_Remielle_Weapon_01","MAT_Remielle_Origin_Body_2"),
            ["signatureMaterial"]="MAT_Remielle_Weapon_01",
            ["menuResetVerified"]=true,["sourceAssetsModified"]=false
        }.ToString());
        // Drop unsaved runtime material instances before the player build.
        EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        Debug.Log("REMIELLE_RUNTIME_MATERIAL_VERIFIED");
    }
}
