using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// V2a: 几何组装（禁碰材质）。
// 一个共享嵌套骨架（清单由 tools/v2a_build_manifest.py 生成，数学定案见
// RemielleAssetVault\skinning-verification\skinning-math-derivation.md），
// 26 个网格 SMR.bones 按名重映射到共享骨架；Ramiel_Mask 不并入，保留自身骨架。
// 统一模式：rootBone=Pelvis、_Wrapper_Y180 表现层、顶点级 bounds、程序化验收。
public static class V2aAssembly
{
    [Serializable] class BoneDef { public string name; public string parent; public string source; public float[] t; public float[] q; public float[] s; }
    [Serializable] class MeshDef { public string glb; public string name; public string[] joints; public float[] aabbMin; public float[] aabbMax; public int vertexCount; }
    [Serializable] class ManifestDef
    {
        public string skeletonRootName; public string pelvisBone; public string[] pathBones;
        public float[] wrapperRotationEuler; public float[] standRotationEuler;
        public BoneDef[] bones; public MeshDef[] meshes;
    }

    [MenuItem("Remielle/V2a Assembly Geometry")]
    public static void Run()
    {
        RemielleImportSettings.Apply("Assets/SourceAssets/Meshes");
        var mfPath = "Assets/SourceAssets/Skeleton/v2a_assembly_manifest.json";
        var manifest = JsonUtility.FromJson<ManifestDef>(File.ReadAllText(mfPath));
        if (manifest == null || manifest.bones == null || manifest.bones.Length == 0)
        { Debug.LogError("V2a: manifest missing/broken: " + mfPath); return; }

        // ---- 表现层根（只旋转实例，不烘焙进网格/骨骼数据） ----
        var wrapper = new GameObject("_Wrapper_Y180");
        wrapper.transform.rotation = Euler(manifest.wrapperRotationEuler);
        var stand = new GameObject("_Stand_Rx90_Y180");
        stand.transform.SetParent(wrapper.transform, false);
        stand.transform.rotation = Euler(manifest.standRotationEuler);
        var skelRoot = new GameObject(manifest.skeletonRootName);
        skelRoot.transform.SetParent(stand.transform, false);

        // ---- 共享嵌套骨架（清单已按父先子排序） ----
        var map = new Dictionary<string, Transform>();
        foreach (var b in manifest.bones)
        {
            var go = new GameObject(b.name);
            var parent = string.IsNullOrEmpty(b.parent) ? skelRoot.transform : map[b.parent];
            go.transform.SetParent(parent, false);
            go.transform.localPosition = V3(b.t);
            go.transform.localRotation = new Quaternion(b.q[0], b.q[1], b.q[2], b.q[3]);
            go.transform.localScale = V3(b.s);
            map[b.name] = go.transform;
        }
        var pelvis = map[manifest.pelvisBone];
        int nBind = manifest.bones.Count(x => x.source == "bind");
        int nRest = manifest.bones.Count(x => x.source == "rest");
        int nPath = manifest.bones.Count(x => x.source == "path");
        Debug.Log($"V2a: skeleton built bones={map.Count} bind={nBind} rest={nRest} path={nPath}");
        // 2 根 path_<hash> 骨挂在骨架根（父链不可解析），按约定报告
        foreach (var pb in manifest.pathBones)
            Debug.Log($"V2a: path bone attached to skeleton root: {pb}");

        // ---- 朝向断言（防倒立复发；蒙皮/AABB/剪影全是旋转不变量，抓不到翻转） ----
        Transform headT = null, pelvisT = null;
        map.TryGetValue("Bip001 Head", out headT);
        map.TryGetValue(manifest.pelvisBone, out pelvisT);
        if (headT == null || pelvisT == null)
        { Debug.LogError("V2a: orientation assertion failed - Head/Pelvis bone missing"); return; }
        Vector3 headW = headT.position, pelvisW = pelvisT.position;
        bool upright = headW.y > pelvisW.y;
        Debug.Log($"V2a: orientation Head=({headW.x:F4},{headW.y:F4},{headW.z:F4}) Pelvis=({pelvisW.x:F4},{pelvisW.y:F4},{pelvisW.z:F4}) upright={upright}");
        if (!upright)
        { Debug.LogError($"V2a: orientation assertion FAILED - Head y({headW.y:F4}) <= Pelvis y({pelvisW.y:F4}) (upside down!)"); return; }

        // 正反断言：脸朝 +Z（相机在 +Z 拍正面）。嘴骨在脸面、头骨在头心，
        // 脸朝 +Z 时嘴的世界 z 必大于头骨（作者俯卧脸朝 −Y，靠 stand 的 yaw180 转正）。
        Transform mouthT = null;
        map.TryGetValue("Skn_M_Mouth", out mouthT);
        if (mouthT == null)
        { Debug.LogError("V2a: facing assertion failed - Skn_M_Mouth bone missing"); return; }
        Vector3 mouthW = mouthT.position;
        bool facing = mouthW.z > headW.z;
        Debug.Log($"V2a: facing Mouth=({mouthW.x:F4},{mouthW.y:F4},{mouthW.z:F4}) vs Head z={headW.z:F4} facing=+Z:{facing}");
        if (!facing)
        { Debug.LogError($"V2a: facing assertion FAILED - Mouth z({mouthW.z:F4}) <= Head z({headW.z:F4}) (showing back to +Z camera!)"); return; }

        // ---- 网格组装 ----
        var report = new StringBuilder("{\"meshes\":[\n");
        var total = new Bounds(); bool totalFirst = true;
        var smrs = new List<SkinnedMeshRenderer>();
        var maskSmr = new List<SkinnedMeshRenderer>();
        foreach (var m in manifest.meshes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.glb);
            if (prefab == null) { Debug.LogError($"V2a: glb missing {m.glb}"); continue; }
            var inst = Object.Instantiate(prefab);
            var src = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (src == null) { Debug.LogError($"V2a: no SMR in {m.glb}"); DestroyGO(inst); continue; }
            bool isMask = m.glb.Contains("Ramiel_Mask");
            SkinnedMeshRenderer smr;
            if (isMask)
            {
                // 不并入主骨架：保留自身导入骨架。实例直接挂 stand（wrapper 已完成
                // D=C·S 补偿，再包一层 Y180 会双重翻转把 mask 甩离身体，勿加）。
                inst.transform.SetParent(stand.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                if (PrefabUtility.IsPartOfPrefabInstance(inst))
                    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                smr = src;
                var ownPelvis = smr.bones.FirstOrDefault(b => b != null && b.name == manifest.pelvisBone);
                if (ownPelvis != null) smr.rootBone = ownPelvis;
                maskSmr.Add(smr);
            }
            else
            {
                var mapped = new Transform[src.bones.Length];
                int missing = 0, orderMismatch = 0;
                for (int i = 0; i < src.bones.Length; i++)
                {
                    var nm = src.bones[i] != null ? src.bones[i].name : m.joints[i];
                    if (i < m.joints.Length && nm != m.joints[i]) orderMismatch++;
                    if (!map.TryGetValue(nm, out var bt)) missing++;
                    else mapped[i] = bt;
                }
                if (orderMismatch > 0) Debug.LogWarning($"V2a: {m.name} joint order mismatch vs manifest: {orderMismatch}");
                if (missing > 0) Debug.LogError($"V2a: {m.name} bones missing in shared skeleton: {missing}");
                var go = new GameObject("SMR_" + m.name);
                go.transform.SetParent(stand.transform, false);
                smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = src.sharedMesh;      // 材质原样拷贝，不做任何修改
                smr.sharedMaterials = src.sharedMaterials;
                smr.bones = mapped;
                smr.rootBone = pelvis;
                DestroyGO(inst);
                smrs.Add(smr);
            }
            smr.updateWhenOffscreen = true;

            // ---- 程序化验收：CPU 蒙皮 = 恒等？世界 AABB = JSON 真值？ ----
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var bps = mesh.bindposes;
            var bones = smr.bones;
            var ws = mesh.boneWeights;
            var M = smr.transform.localToWorldMatrix;   // 期望空间：SMR 局部(=骨架/表现链)
            var D = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1f, 1f, -1f));
            var authorToExpected = M * D;               // 作者空间 -> 期望世界
            float maxDev = 0f;
            var measured = new Bounds(); bool first = true;
            for (int vi = 0; vi < verts.Length; vi++)
            {
                var v = verts[vi];
                var bw = ws[vi];
                Vector3 acc = Vector3.zero;
                if (bw.weight0 > 0f) acc += bw.weight0 * (bones[bw.boneIndex0].localToWorldMatrix * bps[bw.boneIndex0]).MultiplyPoint3x4(v);
                if (bw.weight1 > 0f) acc += bw.weight1 * (bones[bw.boneIndex1].localToWorldMatrix * bps[bw.boneIndex1]).MultiplyPoint3x4(v);
                if (bw.weight2 > 0f) acc += bw.weight2 * (bones[bw.boneIndex2].localToWorldMatrix * bps[bw.boneIndex2]).MultiplyPoint3x4(v);
                if (bw.weight3 > 0f) acc += bw.weight3 * (bones[bw.boneIndex3].localToWorldMatrix * bps[bw.boneIndex3]).MultiplyPoint3x4(v);
                maxDev = Mathf.Max(maxDev, Vector3.Magnitude(acc - M.MultiplyPoint3x4(v)));
                if (first) { measured = new Bounds(acc, Vector3.zero); first = false; }
                else measured.Encapsulate(acc);
            }
            // JSON 真值 AABB 变换到期望世界
            var expected = new Bounds(authorToExpected.MultiplyPoint3x4(V3(m.aabbMin)), Vector3.zero);
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(V3(m.aabbMax)));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMin[0], m.aabbMin[1], m.aabbMax[2])));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMin[0], m.aabbMax[1], m.aabbMin[2])));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMax[0], m.aabbMin[1], m.aabbMin[2])));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMin[0], m.aabbMax[1], m.aabbMax[2])));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMax[0], m.aabbMin[1], m.aabbMax[2])));
            expected.Encapsulate(authorToExpected.MultiplyPoint3x4(new Vector3(m.aabbMax[0], m.aabbMax[1], m.aabbMin[2])));
            float aabbErr = Mathf.Max(
                Vector3.Magnitude((measured.center - measured.extents) - (expected.center - expected.extents)),
                Vector3.Magnitude((measured.center + measured.extents) - (expected.center + expected.extents)));
            bool pass = maxDev < 5e-4f && aabbErr < 5e-3f;
            if (totalFirst) { total = measured; totalFirst = false; } else total.Encapsulate(measured);
            var mmn = measured.center - measured.extents; var mmx = measured.center + measured.extents;
            var emn = expected.center - expected.extents; var emx = expected.center + expected.extents;
            Debug.Log($"V2a: {(isMask ? "[mask]" : "")}{m.name} verts={verts.Length} maxDev={maxDev:E2} aabbErr={aabbErr:E2} {(pass ? "PASS" : "FAIL")}");
            report.Append("  {\"name\":\"" + m.name + "\",\"verts\":" + verts.Length +
                          ",\"maxDev\":\"" + maxDev.ToString("E4") +
                          "\",\"aabbErr\":\"" + aabbErr.ToString("E4") +
                          "\",\"pass\":" + (pass ? "true" : "false") +
                          ",\"measuredMin\":[" + Fmt(mmn.x) + "," + Fmt(mmn.y) + "," + Fmt(mmn.z) + "]" +
                          ",\"measuredMax\":[" + Fmt(mmx.x) + "," + Fmt(mmx.y) + "," + Fmt(mmx.z) + "]" +
                          ",\"expectedMin\":[" + Fmt(emn.x) + "," + Fmt(emn.y) + "," + Fmt(emn.z) + "]" +
                          ",\"expectedMax\":[" + Fmt(emx.x) + "," + Fmt(emx.y) + "," + Fmt(emx.z) + "]" +
                          "},\n");
        }

        Debug.Log($"V2a: total skinned bounds center={total.center} size={total.size}");

        // ---- 四视角站立展示图（默认材质） ----
        var lightGo = new GameObject("V2aLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.shadows = LightShadows.None;
        light.color = Color.white; light.intensity = 1f;
        var camGo = new GameObject("V2aCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 1000f;
        Directory.CreateDirectory("Renders");
        float size = Mathf.Max(total.size.x, total.size.y, total.size.z);
        var dirs = new[] { new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(-1, 0, 0) };
        var names = new[] { "posZ", "negZ", "posX", "negX" };
        for (int k = 0; k < 4; k++)
        {
            var pos = total.center + dirs[k] * (size * 2.2f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(-dirs[k], Vector3.up);
            lightGo.transform.rotation = Quaternion.LookRotation(-dirs[k], Vector3.up);
            var rt = new RenderTexture(720, 1024, 24);
            cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(720, 1024, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 720, 1024), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
            File.WriteAllBytes($"Renders/v2a_stand_from_{names[k]}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        Debug.Log("V2a: 4 view renders -> Renders/v2a_stand_from_*.png");

        // ---- 保存 prefab（供编辑器实看；表现层旋转保留在 wrapper/stand 上，未烘焙） ----
        Directory.CreateDirectory("Assets/V2a");
        PrefabUtility.SaveAsPrefabAsset(wrapper, "Assets/V2a/Remielle_V2a_Assembled.prefab");
        AssetDatabase.SaveAssets();
        Debug.Log("V2a: prefab saved -> Assets/V2a/Remielle_V2a_Assembled.prefab");

        if (report.Length > 2) report.Length -= 2; // 去掉最后的 ",\n"
        report.Append("\n],\n");
        report.Append($"\"totalBoundsCenter\":[{Fmt(total.center.x)},{Fmt(total.center.y)},{Fmt(total.center.z)}],\n");
        report.Append($"\"totalBoundsSize\":[{Fmt(total.size.x)},{Fmt(total.size.y)},{Fmt(total.size.z)}],\n");
        report.Append("\"pathBones\":[" + string.Join(",", manifest.pathBones.Select(p => "\"" + p + "\"")) + "],\n");
        report.Append("\"orientation\":{\"headWorld\":[" + Fmt(headW.x) + "," + Fmt(headW.y) + "," + Fmt(headW.z) + "]," +
                      "\"pelvisWorld\":[" + Fmt(pelvisW.x) + "," + Fmt(pelvisW.y) + "," + Fmt(pelvisW.z) + "]," +
                      "\"mouthWorld\":[" + Fmt(mouthW.x) + "," + Fmt(mouthW.y) + "," + Fmt(mouthW.z) + "]," +
                      "\"upright\":" + (upright ? "true" : "false") + "," +
                      "\"facingPlusZ\":" + (facing ? "true" : "false") + "},\n");
        report.Append("\"boneCounts\":{\"bind\":" + nBind + ",\"rest\":" + nRest + ",\"path\":" + nPath + "}\n");
        report.Append("}\n");
        File.WriteAllText("Renders/v2a_report.json", report.ToString());
        Debug.Log("V2a: report -> Renders/v2a_report.json");

        Object.DestroyImmediate(wrapper);
        Object.DestroyImmediate(lightGo);
        Object.DestroyImmediate(camGo);
    }

    static Quaternion Euler(float[] e) => Quaternion.Euler(e[0], e[1], e[2]);
    static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);
    static string Fmt(float f) => f.ToString("E4");
    static void DestroyGO(GameObject go)
    {
        if (go != null) Object.DestroyImmediate(go);
    }
}
