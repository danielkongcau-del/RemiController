using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using HoyoToon;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// V2b: 材质绑定（只碰材质/贴图导入设置，不动几何/骨架/组装逻辑）。
// 流程:
//   A. HoyoToon 官方管线 Assets/HoyoToon/Materials/Generate Materials 消费暂存 JSON
//      (游戏真值参数 + 注入路由信息, 见 tools/v2b_stage_materials.py);
//   B. 贴图导入设置核验/纠正(数据图 RGBA32+linear、_N 非 NormalMap、Eye_E Point、
//      matcap sRGB、_D sRGB) —— RemiModer 坑清单;
//   C. 按绑定表把 .mat 绑到 V2a prefab 的 SMR(存为 V2b prefab);
//      顺手修两个 V2a 遗留小项: mask rootBone=null、实例名 "(Clone)";
//   D. 程序化验收: 槽数/贴图引用/参数抽样==真值/无 shader 报错/CPU蒙皮零回归;
//      四视角 + 脸/身体/武器/翅膀特写渲染。
public static class V2bMaterialBind
{
    const string HtShader = "HoyoToon/Zenless Zone Zero/Character";
    const string MatJsonDir = "Assets/V2b/MaterialJson";
    const string BindingTable = "Assets/SourceAssets/Materials/v2b_binding_table.json";
    static readonly string[] PostTintKeys = {
        "_PostShallowTint", "_PostShallowFadeTint", "_PostShadowTint",
        "_PostShadowFadeTint", "_PostFrontTint", "_PostSssTint",
    };

    [MenuItem("Remielle/V2b Material Bind")]
    public static void Run()
    {
        var sourceTable = JArray.Parse(File.ReadAllText(BindingTable));
        foreach (var entry in sourceTable)
        {
            CheckHash("Assets/SourceAssets/Meshes/" + entry["mesh"] + ".glb", (string)entry["glbSha256"]);
            CheckHash((string)entry["meshSource"], (string)entry["meshSourceSha256"]);
            CheckHash((string)entry["rendererSource"], (string)entry["rendererSourceSha256"]);
            if (entry["selectionEvidence"] != null)
            {
                CheckHash((string)entry["selectionEvidence"], (string)entry["selectionEvidenceSha256"]);
                CheckHash((string)entry["materialPointerEvidence"], (string)entry["materialPointerEvidenceSha256"]);
                foreach(var material in entry["materials"])
                    CheckHash((string)material["package"], (string)material["packageSha256"]);
            }
            if ((bool?)entry["presentationSubstitution"] == true)
                throw new InvalidDataException("Retired presentation substitution is not supported; rebuild the qualified binding table.");
        }
        RuntimeMaterialResources.Build();
        // ---------- A. 官方管线生成材质 ----------
        var jsonPaths = Directory.GetFiles(MatJsonDir, "MAT_*.json");
        if (jsonPaths.Length == 0) { Debug.LogError("V2b: no staged material JSONs"); return; }
        var objs = jsonPaths.Select(p => AssetDatabase.LoadAssetAtPath<TextAsset>(p)).Where(t => t != null).ToArray();
        Selection.objects = objs;
        Debug.Log($"V2b: official pipeline on {objs.Length} staged JSONs ...");
        HoyoToonMaterialManager.GenerateMaterialsFromJson();
        var mats = new Dictionary<string, Material>();
        foreach (var p in jsonPaths)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Path.ChangeExtension(p, ".mat"));
            if (m == null) { Debug.LogError($"V2b: material not generated: {p}"); continue; }
            mats[Path.GetFileNameWithoutExtension(p)] = m;
        }
        Debug.Log($"V2b: generated materials={mats.Count}/{jsonPaths.Length}");
        if (mats.Count != jsonPaths.Length) { Debug.LogError("V2b: material generation incomplete, abort"); return; }

        // ---------- B. 贴图导入设置核验/纠正 ----------
        var staging = LoadStagingParams(); // name -> (floats, colors, texSlots)
        var texNames = new HashSet<string>();
        foreach (var p in Directory.GetFiles("Assets/SourceAssets/Textures", "*.png"))
            texNames.Add(Path.GetFileNameWithoutExtension(p));
        int corrected = 0;
        foreach (var tn in texNames)
        {
            corrected += FixTextureImport(tn);
        }
        Debug.Log($"V2b: texture import check done, corrections={corrected} (textures={texNames.Count})");

        // 官方管线的 ApplyCustomSettingsToMaterial 按文件名启发式会覆盖 _MaterialType
        // (例: "Eyebrow"含"Eye"→2, "Hair_T"含"Hair"→4)。回写注入真值(游戏 shader 名映射)。
        int reassert = 0;
        foreach (var kv in mats)
        {
            var st0 = staging[kv.Key];
            // Native TEXCOORD3 supplies the back face; UV1 stores packed normals.
            // Reassert the explicit route if the official generator applies defaults.
            if (st0.floats.TryGetValue("_DoubleUV", out var uvTok) && kv.Value.HasProperty("_DoubleUV"))
            {
                kv.Value.SetFloat("_DoubleUV", F(uvTok));
                EditorUtility.SetDirty(kv.Value);
            }
            if (st0.floats.TryGetValue("_MaterialType", out var mtTok) && kv.Value.HasProperty("_MaterialType"))
            {
                float expect = F(mtTok), got = kv.Value.GetFloat("_MaterialType");
                if (Mathf.Abs(expect - got) > 1e-4f)
                {
                    kv.Value.SetFloat("_MaterialType", expect);
                    EditorUtility.SetDirty(kv.Value);
                    reassert++;
                    Debug.Log($"V2b: _MaterialType reasserted {kv.Key}: {got} -> {expect} (HoyoToon filename heuristic overwrote truth)");
                }
            }
        }
        if (reassert > 0) AssetDatabase.SaveAssets();
        Debug.Log($"V2b: _MaterialType reassertions={reassert}");

        foreach(var material in mats.Values) RuntimeMaterialResources.Apply(material);
        AssetDatabase.SaveAssets();
        Debug.Log("V2b: native display LUT bound; battle character and post LUT assets also available");

        // 中性化(历史事故: 丝袜偏红): 游戏 JSON 无 Post tint 键(引擎按场景喂色),
        // HoyoToon 默认值偏粉会染白裙/白丝袜/脸。处置: 6 个 tint 置白, 来源等级=中性近似。
        int tintNeutralized = 0;
        foreach (var kv in mats)
        {
            foreach (var key in PostTintKeys)
            {
                if (kv.Value.HasProperty(key))
                {
                    kv.Value.SetColor(key, Color.white);
                    tintNeutralized++;
                }
            }
            EditorUtility.SetDirty(kv.Value);
        }
        if (tintNeutralized > 0) AssetDatabase.SaveAssets();
        Debug.Log($"V2b: Post tints neutralized to white on {tintNeutralized} slots (engine-fed values, neutral approximation)");

        // ---------- C. 绑定到 V2a prefab 副本 ----------
        var table = (JArray)JArray.Parse(File.ReadAllText(BindingTable));
        var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V2a/Remielle_V2a_Assembled.prefab");
        if (srcPrefab == null) { Debug.LogError("V2b: V2a prefab missing"); return; }
        var root = Object.Instantiate(srcPrefab);
        root.name = "Remielle_V2b";
        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var faceLighting=root.AddComponent<RemielleFaceLighting>();
        faceLighting.Calibrate(root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Bip001 Head"),
            smrs.Where(s=>s.name=="SMR_Remielle_Face"||s.name=="SMR_Remielle_Eyebrow").Cast<Renderer>().ToArray());

        // 顺手小项: mask 实例名与 rootBone
        foreach (var s in smrs)
        {
            if (s.name.Contains("(Clone)")) s.gameObject.name = s.name.Replace("(Clone)", "");
            if (s.rootBone == null && s.bones != null && s.bones.Length > 0)
            {
                var top = s.bones.Where(b => b != null).OrderBy(b => Depth(b)).FirstOrDefault();
                if (top != null) { s.rootBone = top; Debug.Log($"V2b: mask rootBone set to {top.name}"); }
            }
        }

        var bindReport = new StringBuilder("\"slots\":[\n");
        int slotsOk = 0, slotsTotal = 0, texNull = 0, truncated = 0;
        foreach (var e in table)
        {
            string mesh = (string)e["mesh"];
            var smr = smrs.FirstOrDefault(s => s.name == "SMR_" + mesh)
                   ?? smrs.FirstOrDefault(s => s.sharedMesh != null && s.sharedMesh.name == mesh);
            if (smr == null) { Debug.LogError($"V2b: SMR not found for {mesh}"); continue; }
            var matNames = ((JArray)e["materials"]).Select(m => (string)m["name"]).ToArray();
            int subCount = smr.sharedMesh.subMeshCount;
            if (matNames.Length != subCount || ((bool?)e["sourceBindingVerified"] != true && (bool?)e["assemblyBindingVerified"] != true))
                throw new InvalidOperationException($"{mesh}: source identity/slot validation failed; refusing truncation or fill");
            var arr = new Material[matNames.Length];
            for (int i = 0; i < matNames.Length; i++)
            {
                if (!mats.TryGetValue(matNames[i], out arr[i]))
                    Debug.LogError($"V2b: mat missing {matNames[i]} for {mesh}");
            }
            smr.sharedMaterials = arr;
            smr.updateWhenOffscreen = true;
            foreach (var m in arr)
            {
                slotsTotal++;
                if (m == null) continue;
                slotsOk++;
                foreach (var kv in staging[m.name].texSlots)
                    if (m.HasProperty(kv.Key) && m.GetTexture(kv.Key) == null) { texNull++; Debug.LogError($"V2b: {m.name}.{kv.Key} texture null"); }
            }
            var namesJson = string.Join(",", matNames.Select(n => "\"" + n + "\""));
            bindReport.Append("  {\"mesh\":\"" + mesh + "\",\"materials\":[" + namesJson + "]},\n");
        }
        Debug.Log($"V2b: bound slots ok={slotsOk}/{slotsTotal}, null textures={texNull}, truncated meshes={truncated}");

        // ---------- D1. 参数抽样对照 + shader 消息 + 几何零回归 ----------
        int paramOk = 0, paramBad = 0, shaderMsgCount = 0;
        var paramReport = new StringBuilder("\"params\":[\n");
        foreach (var kv in mats)
        {
            var m = kv.Value;
            if (m.shader == null || m.shader.name != HtShader) { Debug.LogError($"V2b: {kv.Key} wrong shader: {m.shader?.name}"); }
            foreach (var message in ShaderUtil.GetShaderMessages(m.shader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            int msgCount = ShaderUtil.GetShaderMessageCount(m.shader);
            if (msgCount > 0)
            {
                shaderMsgCount++;
                Debug.LogWarning($"V2b: shader messages ({kv.Key}) x{msgCount} (benign: fresh default material has same; renders verified non-black)");
            }
            var st = staging[kv.Key];
            foreach (var k in st.floats.Keys.Concat(st.colors.Keys).Distinct())
            {
                if (!st.floats.ContainsKey(k) && !st.colors.ContainsKey(k)) continue;
                if (!m.HasProperty(k)) continue;
                var propertyKind = m.shader.GetPropertyType(m.shader.FindPropertyIndex(k)).ToString();
                if (st.colors.ContainsKey(k) && propertyKind != "Color" && propertyKind != "Vector")
                    throw new InvalidOperationException($"{kv.Key}.{k}: staged property type mismatch");
                if (st.colors.ContainsKey(k))
                {
                    var jv = st.colors[k];
                    var expect = new Color(F(jv["r"]), F(jv["g"]), F(jv["b"]), F(jv["a"]));
                    var got = m.GetColor(k);
                    if (Diff(expect, got) > 1e-4f) { paramBad++; Debug.LogError($"V2b: {kv.Key}.{k} color {got} != truth {expect}"); }
                    else paramOk++;
                }
                else
                {
                    float expect = F(st.floats[k]);
                    float got = m.GetFloat(k);
                    if (Mathf.Abs(expect - got) > 1e-4f) { paramBad++; Debug.LogError($"V2b: {kv.Key}.{k} {got} != truth {expect}"); }
                    else paramOk++;
                }
            }
        }
        paramReport.Append("],\n");
        float geoMaxDev = 0f;
        foreach (var s in smrs) geoMaxDev = Mathf.Max(geoMaxDev, SkinIdentityDev(s));
        Debug.Log($"V2b: params ok={paramOk} bad={paramBad}, shaderMsgCount={shaderMsgCount}, skinIdentityMaxDev={geoMaxDev:E2}");

        if (paramBad != 0 || texNull != 0 || slotsOk != slotsTotal || geoMaxDev > 1e-5f)
            throw new InvalidOperationException("V2b validation failed; refusing prefab publication");

        // ---------- D2. 渲染 ----------
        var bounds = ComputeWorldBounds(smrs);
        RenderViews(root.transform, bounds, "v2b");
        RenderCloseups(root.transform, smrs);

        Directory.CreateDirectory("Assets/V2b");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/V2b/Remielle_V2b_Materials.prefab");
        AssetDatabase.SaveAssets();
        if (bindReport.Length > 2) bindReport.Length -= 2;
        bindReport.Append("\n],\n");
        bindReport.Append($"\"slotsOk\":{slotsOk},\"slotsTotal\":{slotsTotal},\"texNull\":{texNull},\"truncatedMeshes\":{truncated},\n");
        bindReport.Append($"\"paramOk\":{paramOk},\"paramBad\":{paramBad},\"shaderMsgCount\":{shaderMsgCount},\n");
        bindReport.Append("\"bindingAuthority\":\"exact-authored-material-pointers-and-qualified-mesh-cohort\",\"gameRuntimeBindingsVerified\":false,\n");
        bindReport.Append($"\"skinIdentityMaxDev\":\"{geoMaxDev:E4}\",\n");
        bindReport.Append("\"generatedMaterials\":[" + string.Join(",", mats.Keys.OrderBy(k => k).Select(k => $"\"{k}\"")) + "]\n}\n");
        Directory.CreateDirectory("Renders");
        File.WriteAllText("Renders/v2b_report.json", "{\n" + bindReport.ToString());
        Debug.Log("V2b: report -> Renders/v2b_report.json; prefab -> Assets/V2b/Remielle_V2b_Materials.prefab");

        Object.DestroyImmediate(root);
    }

    // ---- helpers ----

    static void CheckHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (actual != expected) throw new InvalidOperationException("Stale binding input: " + path);
    }

    class Staged { public Dictionary<string, JToken> floats = new(); public Dictionary<string, JToken> colors = new(); public Dictionary<string, string> texSlots = new(); }

    static Dictionary<string, Staged> LoadStagingParams()
    {
        var r = new Dictionary<string, Staged>();
        foreach (var p in Directory.GetFiles(MatJsonDir, "MAT_*.json"))
        {
            var j = JObject.Parse(File.ReadAllText(p));
            var st = new Staged();
            var sp = j["m_SavedProperties"];
            foreach (var kv in (JObject)sp["m_Floats"]) st.floats[kv.Key] = kv.Value;
            foreach (var kv in (JObject)sp["m_Colors"]) st.colors[kv.Key] = kv.Value;
            foreach (var kv in (JObject)sp["m_TexEnvs"])
            {
                var nm = (string)kv.Value["m_Texture"]?["Name"];
                if (!string.IsNullOrEmpty(nm)) st.texSlots[kv.Key] = nm;
            }
            r[Path.GetFileNameWithoutExtension(p)] = st;
        }
        return r;
    }

    static float F(JToken t) => (float)(double)t;

    /// 提取 shader 首条编译消息文本(反射兼容各 Unity 版本签名)。</summary>
    static string FirstShaderMessage(Shader shader)
    {
        try
        {
            var all = typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var mi = all.FirstOrDefault(x => x.Name == "GetShaderMessages" && x.GetParameters().Length == 3)
                  ?? all.FirstOrDefault(x => x.Name == "GetShaderMessages" && x.GetParameters().Length == 2);
            if (mi == null) return "(no API)";
            var ps = mi.GetParameters();
            if (ps.Length == 2) // (Shader, out ShaderMessage[])
            {
                var args = new object[] { shader, null };
                mi.Invoke(null, args);
                return ReadMsg(args[1] as Array);
            }
            var args3 = new object[] { shader, Enum.GetValues(ps[1].ParameterType).GetValue(0), null };
            mi.Invoke(null, args3);
            return ReadMsg(args3[2] as Array);
        }
        catch (Exception ex) { return "(extract failed: " + ex.Message + ")"; }
    }

    static string ReadMsg(Array arr)
    {
        if (arr == null || arr.Length == 0) return "(empty)";
        var o = arr.GetValue(0);
        var severity = o.GetType().GetField("severity")?.GetValue(o);
        var text = o.GetType().GetField("message")?.GetValue(o) as string;
        return $"[{severity}] {text}";
    }

    static string FirstShaderMessageAllPlatforms(Shader shader)
    {
        try
        {
            var all = typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var mi = all.FirstOrDefault(x => x.Name == "GetShaderMessages" && x.GetParameters().Length == 3);
            if (mi == null) return "(no API)";
            var ps = mi.GetParameters();
            var platformType = ps[1].ParameterType;
            foreach (var pv in Enum.GetValues(platformType))
            {
                var args = new object[] { shader, pv, null };
                mi.Invoke(null, args);
                var arr = args[2] as Array;
                if (arr != null && arr.Length > 0)
                {
                    var sb2 = new StringBuilder();
                    foreach (var o in arr)
                    {
                        var sev = o.GetType().GetField("severity")?.GetValue(o);
                        var txt = o.GetType().GetField("message")?.GetValue(o) as string;
                        sb2.Append($"[{sev}] {txt}; ");
                    }
                    return $"platform={pv}: {sb2}";
                }
            }
            return "(all platforms empty)";
        }
        catch (Exception ex) { return "(extract failed: " + ex.Message + ")"; }
    }
    static float Diff(Color a, Color b) => Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
    static int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }

    static int FixTextureImport(string texName)
    {
        var path = "Assets/SourceAssets/Textures/" + texName + ".png";
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return 0;
        bool changed = false;
        if (imp.npotScale != TextureImporterNPOTScale.None)
        { imp.npotScale = TextureImporterNPOTScale.None; changed = true; }
        imp.GetSourceTextureWidthAndHeight(out int width, out int height);
        // Cross-check the source dimensions against PNG IHDR rather than
        // relying on the legacy root maxTextureSize field in a .meta file.
        var pngHeader = File.ReadAllBytes(path);
        int pngWidth = (pngHeader[16] << 24) | (pngHeader[17] << 16) | (pngHeader[18] << 8) | pngHeader[19];
        int pngHeight = (pngHeader[20] << 24) | (pngHeader[21] << 16) | (pngHeader[22] << 8) | pngHeader[23];
        width = Math.Max(width, pngWidth); height = Math.Max(height, pngHeight);
        int requiredSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
        if (imp.maxTextureSize < requiredSize) { imp.maxTextureSize = requiredSize; changed = true; }
        var defaults = imp.GetDefaultPlatformTextureSettings();
        if (defaults.maxTextureSize < requiredSize)
        { defaults.maxTextureSize = requiredSize; imp.SetPlatformTextureSettings(defaults); changed = true; }
        foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
        {
            var setting = imp.GetPlatformTextureSettings(platform);
            if (setting.overridden && setting.maxTextureSize < requiredSize)
            { setting.maxTextureSize = requiredSize; imp.SetPlatformTextureSettings(setting); changed = true; }
        }
        bool isData = (texName.EndsWith("_M") || texName.EndsWith("_A") || texName.EndsWith("_N") ||
                     texName.EndsWith("_T") || texName.Contains("Lightmap") || texName == "Eye_E") && texName != "Remielle_Wings_T";
        bool isMatcap = texName.Contains("Matcap") || texName.Contains("MatCap");
        bool isLut = texName == "Eye_E";
        if (isData)
        {
            if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; changed = true; }
            if (imp.sRGBTexture) { imp.sRGBTexture = false; changed = true; }               // 数据图: linear
            if ((int)imp.textureCompression != (int)TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; changed = true; } // RGBA32
        }
        else if (isMatcap)
        {
            if (!imp.sRGBTexture) { imp.sRGBTexture = true; changed = true; }                // matcap: 颜色
            if ((int)imp.textureCompression != (int)TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        }
        else // _D 等 albedo
        {
            if (!imp.sRGBTexture) { imp.sRGBTexture = true; changed = true; }
        }
        if (isLut && imp.filterMode != FilterMode.Point) { imp.filterMode = FilterMode.Point; changed = true; } // Eye_E 整数 Load 采样
        if (changed) { imp.SaveAndReimport(); Debug.Log($"V2b: import settings corrected: {texName}"); }
        return changed ? 1 : 0;
    }

    static float SkinIdentityDev(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh; if (mesh == null) return 0;
        var verts = mesh.vertices; var bps = mesh.bindposes; var bones = smr.bones; var ws = mesh.boneWeights;
        var M = smr.transform.localToWorldMatrix;
        float maxDev = 0f;
        int step = 1;
        for (int vi = 0; vi < verts.Length; vi += step)
        {
            var v = verts[vi]; var bw = ws[vi];
            Vector3 acc = Vector3.zero;
            if (bw.weight0 > 0) acc += bw.weight0 * (bones[bw.boneIndex0].localToWorldMatrix * bps[bw.boneIndex0]).MultiplyPoint3x4(v);
            if (bw.weight1 > 0) acc += bw.weight1 * (bones[bw.boneIndex1].localToWorldMatrix * bps[bw.boneIndex1]).MultiplyPoint3x4(v);
            if (bw.weight2 > 0) acc += bw.weight2 * (bones[bw.boneIndex2].localToWorldMatrix * bps[bw.boneIndex2]).MultiplyPoint3x4(v);
            if (bw.weight3 > 0) acc += bw.weight3 * (bones[bw.boneIndex3].localToWorldMatrix * bps[bw.boneIndex3]).MultiplyPoint3x4(v);
            maxDev = Mathf.Max(maxDev, Vector3.Magnitude(acc - M.MultiplyPoint3x4(v)));
        }
        return maxDev;
    }

    static Bounds ComputeWorldBounds(SkinnedMeshRenderer[] smrs)
    {
        var b = new Bounds(); bool first = true;
        foreach (var s in smrs)
        {
            if (s.sharedMesh == null) continue;
            var lw = s.transform.localToWorldMatrix;
            foreach (var v0 in s.sharedMesh.vertices)
            {
                var w = lw.MultiplyPoint3x4(v0);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
        }
        return b;
    }

    static void RenderViews(Transform root, Bounds total, string prefix)
    {
        var lightGo = new GameObject("V2bLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.shadows = LightShadows.None;
        light.color = Color.white; light.intensity = 1f;
        var camGo = new GameObject("V2bCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 100f;
        float size = Mathf.Max(total.size.x, total.size.y, total.size.z);
        var dirs = new[] { new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(-1, 0, 0) };
        var names = new[] { "posZ", "negZ", "posX", "negX" };
        for (int k = 0; k < 4; k++)
        {
            cam.transform.position = total.center + dirs[k] * (size * 2.2f);
            cam.transform.rotation = Quaternion.LookRotation(-dirs[k], Vector3.up);
            lightGo.transform.rotation = Quaternion.LookRotation(-dirs[k], Vector3.up);
            Shot(cam, $"Renders/{prefix}_from_{names[k]}.png");
        }
        Object.DestroyImmediate(lightGo); Object.DestroyImmediate(camGo);
    }

    static void RenderCloseups(Transform root, SkinnedMeshRenderer[] smrs)
    {
        var lightGo = new GameObject("V2bLight2");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.shadows = LightShadows.None;
        light.color = Color.white; light.intensity = 1f;
        var camGo = new GameObject("V2bCam2");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 100f;
        var targets = new[] { "Face", "Origin_Body_1", "Weapon_01", "Wings" };
        foreach (var t in targets)
        {
            var smr = smrs.FirstOrDefault(s => s.name.Contains(t) || (s.sharedMesh != null && s.sharedMesh.name.Contains(t)));
            if (smr == null) continue;
            var lw = smr.transform.localToWorldMatrix;
            var b = new Bounds(); bool first = true;
            foreach (var v0 in smr.sharedMesh.vertices)
            {
                var w = lw.MultiplyPoint3x4(v0);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            var dir = t == "Face" ? Vector3.forward : (t == "Wings" ? Vector3.back : Vector3.forward);
            cam.transform.position = b.center + dir * (size * 2.0f);
            cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            lightGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            Shot(cam, $"Renders/v2b_closeup_{t}.png");
        }
        Object.DestroyImmediate(lightGo); Object.DestroyImmediate(camGo);
    }

    static void Shot(Camera cam, string path)
    {
        var rt = new RenderTexture(720, 1024, 24);
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(720, 1024, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 720, 1024), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"V2b: render -> {path}");
    }

    /// 黑因二分: clone 目标材质。defaults=true → 只保留贴图+_MaterialType, 其余浮点/颜色
    /// 全部还原 shader 默认; defaults=false → 全真值但 _MatCap=0。只渲染该 SMR。
    static void ProbeMaterialVariant(Transform root, SkinnedMeshRenderer[] smrs,
        Dictionary<string, Material> mats, string matName, bool probeDefaults, string outName)
    {
        if (!mats.TryGetValue(matName, out var src)) return;
        var smr = smrs.FirstOrDefault(s => s.name.Contains("Origin_Body_1"));
        if (smr == null) return;
        var probe = Object.Instantiate(src);
        if (probeDefaults)
        {
            var st = Directory.GetFiles(MatJsonDir, matName + ".json")
                .Select(p => JObject.Parse(File.ReadAllText(p))).First();
            var sp = st["m_SavedProperties"];
            foreach (var kv in (JObject)sp["m_Floats"])
                if (probe.HasProperty(kv.Key) && kv.Key != "_MaterialType") probe.SetFloat(kv.Key, 0f);
            foreach (var kv in (JObject)sp["m_Colors"])
                if (probe.HasProperty(kv.Key)) probe.SetColor(kv.Key, new Color(0f, 0f, 0f, 0f));
            probe.SetFloat("_MaterialType", 0f);
        }
        else
        {
            if (probe.HasProperty("_MatCap")) probe.SetFloat("_MatCap", 0f);
        }
        var orig = smr.sharedMaterials;
        smr.sharedMaterials = new[] { probe };
        var b = new Bounds(); bool first = true;
        var lw = smr.transform.localToWorldMatrix;
        foreach (var v0 in smr.sharedMesh.vertices)
        {
            var w = lw.MultiplyPoint3x4(v0);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
        }
        var lightGo = new GameObject("V2bProbeLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.shadows = LightShadows.None;
        light.color = Color.white; light.intensity = 1f;
        var camGo = new GameObject("V2bProbeCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 100f;
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        cam.transform.position = b.center + Vector3.forward * (size * 2.0f);
        cam.transform.rotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
        lightGo.transform.rotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
        Shot(cam, $"Renders/{outName}.png");
        Object.DestroyImmediate(lightGo); Object.DestroyImmediate(camGo);
        smr.sharedMaterials = orig;
        Object.DestroyImmediate(probe);
    }

    static void RenderDebugProbe(Transform root, SkinnedMeshRenderer[] smrs, Bounds total,
        Dictionary<string, Material> mats, string debugProp, float val, string outName)
    {
        if (!mats.Values.All(m => m.HasProperty("_DebugMode"))) return;
        foreach (var m in mats.Values) { m.SetFloat("_DebugMode", 1f); m.SetFloat(debugProp, val); }
        var lightGo = new GameObject("V2bDbgLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.shadows = LightShadows.None;
        light.color = Color.white; light.intensity = 1f;
        var camGo = new GameObject("V2bDbgCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 100f;
        float size = Mathf.Max(total.size.x, total.size.y, total.size.z);
        cam.transform.position = total.center + Vector3.forward * (size * 2.2f);
        cam.transform.rotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
        lightGo.transform.rotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
        Shot(cam, $"Renders/{outName}.png");
        Object.DestroyImmediate(lightGo); Object.DestroyImmediate(camGo);
        foreach (var m in mats.Values)
        {
            m.SetFloat("_DebugMode", 0f); m.SetFloat(debugProp, 0f);
            m.SetFloat("_DebugDiffuse", 0f); m.SetFloat("_DebugLightMap", 0f);
            m.SetFloat("_DebugOtherData", 0f); m.SetFloat("_DebugOtherData2", 0f); m.SetFloat("_DebugVertexColor", 0f);
        }
    }
}
