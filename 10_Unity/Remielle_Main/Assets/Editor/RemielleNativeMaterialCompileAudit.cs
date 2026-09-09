using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class RemielleNativeMaterialCompileAudit
{
    const string ShaderPath="Assets/RenderingReview/Shader/GeneratedNative/CapturedNativeMaterialBodies.shader";
    const string Out="E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/native-material-compile-gate.json";
    static string HashFile(string path)
    {
        using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    public static void Run()
    {
        AssetDatabase.ImportAsset(ShaderPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if(!shader)throw new Exception("Generated captured native material shader is missing");
        var activated=new JArray();var material=new Material(shader);
        try
        {
            for(int i=0;i<shader.passCount;i++)
            {
                if(!material.SetPass(i))throw new Exception("Native material pass failed: "+i);
                activated.Add(new JObject{{"pass",i},{"keyword","disabled"}});
            }
            material.EnableKeyword("CAPTURED_S3_HANDLE_B");
            var special=material.FindPass("PS_77bdd348772c62c8");
            if(special<0||!material.SetPass(special))throw new Exception("Draw 374 sampler variant failed");
            activated.Add(new JObject{{"pass",special},{"keyword","CAPTURED_S3_HANDLE_B"}});
        }
        finally{UnityEngine.Object.DestroyImmediate(material);}
        var messages=ShaderUtil.GetShaderMessages(shader);
        var compilerMessages=new JArray();var errors=new JArray();
        foreach(var message in messages){var row=new JObject{{"severity",message.severity.ToString()},{"message",message.message},{"platform",message.platform.ToString()},{"line",message.line}};compilerMessages.Add(row);if(message.severity.ToString()=="Error")errors.Add(row);}
        var pass=shader.passCount==6&&shader.isSupported&&errors.Count==0;
        var source=JObject.Parse(File.ReadAllText("E:/ZZZ/local-only/RemielleRenderingReview/20260905/native-material-compile-source.json"));
        var includes=new JArray();
        foreach(var group in new[]{"vertexShaders","pixelShaders"})foreach(var row in (JArray)source[group])
        {
            var path=(string)row["generatedInclude"];var hash=HashFile(path);
            if(hash!=(string)row["generatedIncludeSha256"])throw new Exception("Generated shader evidence is stale: "+path);
            includes.Add(new JObject{{"path",path},{"sha256",hash}});
        }
        File.WriteAllText(Out,new JObject{
            ["schema"]="remielle-native-material-compile-gate-v1",
            ["pass"]=pass,["shader"]=ShaderPath,["passes"]=shader.passCount,
            ["supported"]=shader.isSupported,["compilerMessageCount"]=messages.Length,["compilerMessages"]=compilerMessages,["errors"]=errors,
            ["shaderSha256"]=HashFile(ShaderPath),["verifiedIncludes"]=includes,
            ["activatedVariants"]=activated,
            ["boundary"]="Two VS and six PS translations, with seven captured sampler binding patterns. Separate original-DXBC GPU tests cover captured constants with three explicit sampler fixtures. Original sampler descriptors, full game-image equivalence and live inputs remain pending."
        }.ToString());
        if(!pass)throw new Exception("Captured native material bodies did not compile: "+errors);
    }
}
