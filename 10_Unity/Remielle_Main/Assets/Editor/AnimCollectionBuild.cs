// zcode 2026-09-07, AnimCollection scene build: duplicates the V3 animation
// review setup (model + 15 clips + preview panel) and lays a 3x3 grid of the
// native Dojo floor tile (Whitfield_Ground_DojoFloor_01_01, FrameScene draw
// 393, 48 m per tile -> 144 x 144 m) centered under the character, plus the
// per-group accessory visibility toggles (display-set defaults from the
// visibility contract: seven-mesh set visible, weapons/stickers/floater
// hidden but kept in the prefab). Runs in batch on the closed source project;
// artifacts are copied to ZCODE afterwards.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimCollectionBuild
{
    const string SourceScene = "Assets/V3/Remielle_AnimationReview.unity";
    const string TargetScene = "Assets/RenderingReview/AnimCollection/AnimCollection.unity";
    const string FloorDir = "Assets/RenderingReview/AnimCollection";
    const string FloorAlbedo = FloorDir + "/Textures/frame_79d8f4a0.dds";

    [MenuItem("Remielle/Build AnimCollection Scene")]
    public static void Run()
    {
        var texImporter = AssetImporter.GetAtPath(FloorAlbedo) as TextureImporter;
        if (texImporter != null)
        {
            texImporter.textureType = TextureImporterType.Default;
            texImporter.sRGBTexture = true;
            texImporter.wrapMode = TextureWrapMode.Repeat;
            texImporter.SaveAndReimport();
        }
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorAlbedo);
        if (albedo == null) throw new Exception("Floor albedo missing at " + FloorAlbedo);

        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        var character = UnityEngine.Object.FindObjectsByType<RemielleNativeAnimation>(FindObjectsSortMode.None).FirstOrDefault();
        if (character == null) throw new Exception("Source scene has no RemielleNativeAnimation");

        character.Sample(character.initialClip, 0f);
        var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>();
        // Floor surface must land exactly at the character foot world height.
        float groundY = renderers.Min(r => r.sharedMesh ? r.sharedMesh.bounds.min.y * r.transform.lossyScale.y : 0f);

        // 3x3 grid of the 48 m tile: 144 x 144 m with the character at center.
        var root = new GameObject("NativeDojoFloor");
        for (int gx = -1; gx <= 1; gx++)
        for (int gz = -1; gz <= 1; gz++)
            CreateFloorTile("FrameScene_Draw_000393", root.transform, groundY, albedo, gx * 48f, gz * 48f, (gx + 1) * 3 + (gz + 1));

        AddAccessoryControls(character);
        DecoupleCamera(character);

        if (UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).All(l => l.type != LightType.Directional))
        {
            var sun = new GameObject("AnimCollectionSun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.0f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(TargetScene));
        EditorSceneManager.SaveScene(scene, TargetScene);
        Debug.Log("ANIM_COLLECTION_SCENE_SAVED " + TargetScene);
        AssetDatabase.SaveAssets();
    }

    static void CreateFloorTile(string objName, Transform parent, float groundY, Texture albedo, float offsetX, float offsetZ, int index)
    {
        var meshPath = FloorDir + "/Meshes/" + objName + ".obj";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) throw new Exception("Floor mesh missing: " + meshPath);
        var go = new GameObject(objName + "_tile" + index);
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Standard")) { name = "NativeDojoFloor_01" };
        mat.SetTexture("_MainTex", albedo);
        mat.SetFloat("_Glossiness", 0.08f);
        mr.sharedMaterial = mat;
        // Captured world-space tile: recentre on the character, seat the
        // surface at the foot height, then offset to the grid slot.
        var b = mesh.bounds;
        go.transform.localPosition = new Vector3(-b.center.x + offsetX, -b.min.y + groundY, -b.center.z + offsetZ);
        Debug.Log($"[AnimCollection] floor {go.name} bounds={b.size} offset=({offsetX},{offsetZ}) grounded y={groundY:F3}");
    }

    static void DecoupleCamera(RemielleNativeAnimation character)
    {
        // Clearing follow makes RemielleReviewControls.LateUpdate return
        // early (camera stays where it is) while its OnGUI preview panel
        // keeps working. The free camera then owns orientation/motion.
        var controls = UnityEngine.Object.FindObjectsByType<RemielleReviewControls>(FindObjectsSortMode.None).FirstOrDefault();
        if (controls == null) throw new Exception("Source scene has no RemielleReviewControls");
        controls.follow = null;
        var cam = controls.GetComponent<Camera>();
        if (cam == null) throw new Exception("ReviewControls is not on a camera");
        var free = cam.GetComponent<AnimCollectionFreeCamera>();
        if (free == null) free = cam.gameObject.AddComponent<AnimCollectionFreeCamera>();
        // Serialized values override code defaults; refresh speed/sensitivity
        // so existing scenes pick up the latest user-adjusted values.
        free.lookSensitivityDegPerPixel = 0.0375f;
        free.moveSpeedMetersPerSecond = 3f;
        free.verticalSpeedMetersPerSecond = 3f;
    }

    static void AddAccessoryControls(RemielleNativeAnimation character)
    {
        // zcode: per-group toggles; defaults mirror the native display set.
        var existing = character.GetComponent<AnimCollectionControls>();
        if (existing == null) existing = character.gameObject.AddComponent<AnimCollectionControls>();
        existing.wingsVisible = true;
        existing.qteWeaponsVisible = false;
        existing.alwaysWeaponsVisible = false;
        existing.cannonsVisible = false;
        existing.stickersVisible = false;
        existing.floaterVisible = false;
    }
}
