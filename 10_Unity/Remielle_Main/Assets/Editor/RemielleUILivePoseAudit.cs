using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Read the saved model into an isolated scene. Never rebuild or save its assets.
public static class RemielleUILivePoseAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/"; // D1-c 写根（读根保留 A 类）
    const string Prefab = "Assets/V3/Remielle_V3_Animated.prefab";
    static string Sha(string path)
    {
        using var file = File.OpenRead(path); using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
    static JObject Ref(string path) => new JObject { ["path"] = Path.GetFullPath(path), ["sha256"] = Sha(path) };
    static JArray Matrix(Matrix4x4 m) => new JArray(Enumerable.Range(0, 16).Select(i => m[i]));
    static JArray Vec(Vector3 v) => new JArray(v.x, v.y, v.z);
    static JObject Save(string path, Array values)
    {
        var bytes = new byte[Buffer.ByteLength(values)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes); return Ref(path);
    }
    static float Error(Matrix4x4 a, Matrix4x4 b) => Enumerable.Range(0, 16).Max(i => Mathf.Abs(a[i] - b[i]));

    public static void Run()
    {
        var selection = JObject.Parse(File.ReadAllText(Out + "mesh-bindings.json"));
        var nativeRoots = JObject.Parse(File.ReadAllText(Out + "native-root-bindings.json"))["meshes"]
            .ToDictionary(x => (string)x["mesh"], x => (string)x["rootName"]);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        var driver = root.GetComponent<RemielleNativeAnimation>();
        var face = root.GetComponent<RemielleFaceLighting>();
        var all = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var meshes = new JArray(); var poses = new JArray();
        var bindings = new System.Collections.Generic.Dictionary<string, RemielleNativeUIMeshBinding>();
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        Directory.CreateDirectory(WriteRoot + "unity-pose");
        try
        {
            driver.ResetSourcePose(); driver.ApplyPose();
            foreach (var row in selection["meshes"])
            {
                string name = (string)row["name"];
                var smr = all.Single(x => x.name == "SMR_" + name && x.enabled);
                var mesh = smr.sharedMesh;
                if (!smr.rootBone || mesh.bindposes.Length != smr.bones.Length)
                    throw new Exception("Invalid skin binding: " + name);
                var link = driver.bones.Single(x => x.source.name == nativeRoots[name]);
                bindings.Add(name, new RemielleNativeUIMeshBinding(smr, mesh, link.source));
                string stem = Out + "unity-pose/" + name;
                var topology = new JArray();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    topology.Add(new JObject { ["submesh"] = sub, ["indices"] = Save(stem + "-sub" + sub + ".u32", mesh.GetIndices(sub)) });
                meshes.Add(new JObject { ["name"] = name, ["renderer"] = smr.name, ["vertices"] = mesh.vertexCount,
                    ["meshAsset"] = AssetDatabase.GetAssetPath(mesh), ["rootBone"] = smr.rootBone.name,
                    ["rootSource"] = link.source.name, ["rootSourceRestMatrix"] = Matrix(link.source.localToWorldMatrix),
                    ["rootSourceRestScale"] = Vec(link.source.lossyScale), ["rootBasis"] = Matrix(link.basis),
                    ["rendererMatrix"] = Matrix(smr.localToWorldMatrix), ["rootTargetMatrix"] = Matrix(link.target.localToWorldMatrix),
                    ["positions"] = Save(stem + "-rest.f32", mesh.vertices.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray()),
                    ["normals"] = Save(stem + "-normals.f32", mesh.normals.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray()),
                    ["tangents"] = Save(stem + "-tangents.f32", mesh.tangents.SelectMany(v => new[] { v.x, v.y, v.z, v.w }).ToArray()),
                    ["topology"] = topology });
            }
            var samples = driver.nativeAnimation.Cast<AnimationState>().Select(s => (name: s.name, time: s.length * 0.37f)).ToList();
            samples.Insert(0, ("__rest", 0));
            foreach (var sample in samples)
            {
                if (sample.name != "__rest") driver.Sample(sample.name, sample.time);
                var posed = new JArray();
                foreach (var row in meshes)
                {
                    string name = (string)row["name"];
                    var smr = all.Single(x => x.name == "SMR_" + name && x.enabled);
                    var link = driver.bones.Single(x => x.source.name == nativeRoots[name]);
                    float basisError = Error(link.source.localToWorldMatrix * link.basis, link.target.localToWorldMatrix);
                    if (!float.IsFinite(basisError) || basisError > 0.0005f) throw new Exception("Root basis mismatch: " + name);
                    smr.BakeMesh(baked, false);
                    var p = baked.vertices; var n = baked.normals; var t = baked.tangents;
                    if (p.Length != (int)row["vertices"] || n.Length != p.Length || t.Length != p.Length)
                        throw new Exception("Current skin channels missing: " + name);
                    var values = new float[p.Length * 10];
                    for (int i = 0; i < p.Length; i++)
                    {
                        values[i * 10] = p[i].x; values[i * 10 + 1] = p[i].y; values[i * 10 + 2] = p[i].z;
                        values[i * 10 + 3] = n[i].x; values[i * 10 + 4] = n[i].y; values[i * 10 + 5] = n[i].z;
                        values[i * 10 + 6] = t[i].x; values[i * 10 + 7] = t[i].y; values[i * 10 + 8] = t[i].z; values[i * 10 + 9] = t[i].w;
                    }
                    if (values.Any(v => !float.IsFinite(v))) throw new Exception("Nonfinite live skin: " + name);
                    var binding = bindings[name]; binding.Prepare();
                    if (binding.PositionRoundTripMaxError > 0.00001f) throw new Exception("Native root round trip failed: " + name);
                    var native = new float[p.Length * 13];
                    for (int i = 0; i < p.Length; i++)
                    {
                        var np = binding.Positions[i]; var nn = binding.Normals[i]; var nt = binding.Tangents[i]; var pp = binding.PreviousPositions[i];
                        int at = i * 13;
                        native[at] = np.x; native[at + 1] = np.y; native[at + 2] = np.z;
                        native[at + 3] = nn.x; native[at + 4] = nn.y; native[at + 5] = nn.z;
                        native[at + 6] = nt.x; native[at + 7] = nt.y; native[at + 8] = nt.z; native[at + 9] = nt.w;
                        native[at + 10] = pp.x; native[at + 11] = pp.y; native[at + 12] = pp.z;
                    }
                    posed.Add(new JObject { ["name"] = name, ["vertices"] = p.Length, ["basisMaxError"] = basisError,
                        ["rendererMatrix"] = Matrix(smr.localToWorldMatrix), ["rootSourceMatrix"] = Matrix(link.source.localToWorldMatrix),
                        ["rootTargetMatrix"] = Matrix(link.target.localToWorldMatrix),
                        ["nativeObjectToWorld"] = Matrix(binding.ObjectToWorld), ["previousNativeObjectToWorld"] = Matrix(binding.PreviousObjectToWorld),
                        ["hasHistory"] = binding.HasHistory, ["roundTripMaxError"] = binding.PositionRoundTripMaxError,
                        ["nativeSkin"] = Save(Out + "unity-pose/" + name + "-" + sample.name + "-native.f32", native),
                        ["skin"] = Save(Out + "unity-pose/" + name + "-" + sample.name + ".f32", values) });
                    binding.Commit();
                }
                poses.Add(new JObject { ["clip"] = sample.name, ["time"] = sample.time, ["meshes"] = posed,
                    ["headPosition"] = Vec(face.head.position), ["headForward"] = Vec(face.head.TransformDirection(face.headLocalForward).normalized),
                    ["headRight"] = Vec(face.head.TransformDirection(face.headLocalRight).normalized),
                    ["headUp"] = Vec(face.head.TransformDirection(face.headLocalUp).normalized) });
            }
            File.WriteAllText(WriteRoot + "unity-pose-readback.json", new JObject { ["schema"] = "remielle-ui-current-skin-input-v1",
                ["pass"] = true, ["meshes"] = meshes, ["poses"] = poses, ["prefab"] = Ref(Prefab),
                ["meshBindings"] = Ref(Out + "mesh-bindings.json"), ["implementation"] = Ref("Assets/Editor/RemielleUILivePoseAudit.cs"),
                ["nativeRoots"] = Ref(Out + "native-root-bindings.json"),
                ["meshBindingImplementation"] = Ref("Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs"),
                ["boundary"] = "Current saved skinning snapshots in renderer-local space; CPU BakeMesh, not GPU rendering validation." }.ToString());
            Debug.Log("REMIELLE_UI_CURRENT_SKIN_READ " + meshes.Count + " meshes, " + poses.Count + " poses");
        }
        finally { foreach (var binding in bindings.Values) binding.Dispose(); Object.DestroyImmediate(baked); Object.DestroyImmediate(root); }
    }
}
