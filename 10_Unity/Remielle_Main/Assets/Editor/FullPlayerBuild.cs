using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
public static class FullPlayerBuild
{
    public static void Run() => BuildTo("E:/ZZZ/local-only/RemielleModelReadiness/20260904/Player");
    public static void BuildTo(string folder)
    {
        Directory.CreateDirectory(folder);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes=new[]{"Assets/V3/Remielle_AnimationReview.unity"},
            locationPathName=folder+"/RemielleAudit.exe",target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development
        });
        var s=report.summary;
        File.WriteAllText(folder+"/build-result.json",JsonUtility.ToJson(new Result
        {
            pass=s.result==BuildResult.Succeeded,result=s.result.ToString(),errors=s.totalErrors,warnings=s.totalWarnings,
            bytes=s.totalSize,seconds=s.totalTime.TotalSeconds
        },true));
        if(s.result!=BuildResult.Succeeded)throw new Exception("Player build failed: "+s.result);
        Debug.Log("FULL_PLAYER_BUILD_VERIFIED");
    }
    [Serializable] class Result{public bool pass;public string result;public int errors,warnings;public ulong bytes;public double seconds;}
}
