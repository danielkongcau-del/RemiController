using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceVisibilityAudit
    {
        const string Air="RemielleOrigin_AirCombat_TriggerEvent",Support="RemielleOrigin_SupportAttack_MainStory";
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var checks=new JArray();report["checks"]=checks;GameObject visual=null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
                var all=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var before=all.Select(r=>r.enabled).ToArray();
                var bindings=SourceVisibilityBuild.Prepare(visual);var drivers=all.Except(bindings.Select(b=>b.renderer)).ToArray();
                void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
                var floater=bindings.Single(b=>b.renderer.name=="SMR_Remielle_Floater_02").renderer;
                var weapons=bindings.Where(b=>new[]{"Remielle_Weapon_02_L","Remielle_Weapon_02_R","Remielle_Weapon_03_L","Remielle_Weapon_03_R","Remielle_Weapon_04"}.Contains(b.renderer.name.Substring(4))).Select(b=>b.renderer).ToArray();
                string add="/Modifiers/AirCombat_ReleaseFloater_MeshVisible_Modifier/OnAdded/0",remove="/Modifiers/AirCombat_ReleaseFloater_MeshVisible_Modifier/OnRemoved/0";
                string hide="/Modifiers/TriggerRemielleOriginSuportAttackModifier/OnAdded/8",pop="/AbilityMixins/1/ConfigList/0/ActionList/0";
                using(var host=new SourceRendererVisibility(bindings,File.ReadAllText(SourceVisibilityBuild.AssetPath)))
                {
                    Check(all.Select(r=>r.enabled).SequenceEqual(before),"registration-does-not-activate-source-abilities");
                    host.Execute(Air,"/DefaultModifier/OnAdded/6");Check(!floater.enabled&&weapons.All(r=>!r.enabled),"air-entry-hides-five-weapons-and-floater");
                    host.Execute(Air,"/AbilityMixins/8/TriggerEventConfig/0/TriggerActions/0/SuccessActions/5");Check(!floater.enabled&&weapons.All(r=>r.enabled),"qte-restore-does-not-restore-floater");
                    host.Execute(Air,add);Check(floater.enabled,"floater-on-added-pushes-visible");
                    host.Execute(Air,remove);Check(!floater.enabled,"floater-on-removed-pushes-false");
                    host.Execute(Support,hide);Check(bindings.All(b=>!b.renderer.enabled)&&drivers.All(r=>!r.enabled),"hide-all-affects-only-27-presentation-renderers");
                    host.Execute(Support,pop);Check(!floater.enabled&&weapons.All(r=>r.enabled)&&drivers.All(r=>!r.enabled),"pop-restores-prior-tags-and-keeps-drivers-disabled");
                    host.Execute(Air,"/Modifiers/AirCombat_180s_SafeGuard_Modifier/DelayHandlers/0/TimeUpActions/0/SuccessActions/2");Check(!floater.enabled&&weapons.All(r=>r.enabled),"safeguard-restores-only-listed-weapons");
                    // Same-tag false remains in slot 2, below the new hide in
                    // slot 3. Incorrectly popping it would let hide reuse slot
                    // 2 and a new floater tag take slot 3, reversing the result.
                    host.Execute(Support,hide);host.Execute(Air,add);Check(!floater.enabled,"removed-floater-tag-retains-its-slot-priority");
                    bool rejected=false;try{host.Execute(Air,"/not-an-authored-pointer");}catch(System.Collections.Generic.KeyNotFoundException){rejected=true;}
                    Check(rejected&&!floater.enabled,"unknown-action-does-not-mutate-renderers");
                }
                Check(all.Select(r=>r.enabled).SequenceEqual(before),"dispose-restores-all-original-renderer-flags");
                var duplicate=bindings.ToArray();duplicate[1]=duplicate[0];bool invalid=false;
                try{using var bad=new SourceRendererVisibility(duplicate,File.ReadAllText(SourceVisibilityBuild.AssetPath));}catch(ArgumentException){invalid=true;}
                Check(invalid&&all.Select(r=>r.enabled).SequenceEqual(before),"duplicate-binding-rejected-before-mutation");
                report["pass"]=true;report["renderers"]=bindings.Length;report["excludedExpressionDrivers"]=drivers.Length;
                report["abilityActivationIntegrated"]=false;report["gpuVisibilityVerified"]=false;
                Debug.Log("REMIELLE_SOURCE_VISIBILITY_PASS "+checks.Count);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{if(visual)UnityEngine.Object.DestroyImmediate(visual);File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-visibility-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
