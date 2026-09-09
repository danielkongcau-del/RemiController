using System;
using System.IO;
using UnityEngine;
public static class RuntimeRepairBuild
{
    public static void Run()
    {
        RepairBuild.Run();
        RuntimeLutAudit.Run();
        NativeAnimationBuild.Run();
        NativeAnimationReview.Run();
        File.WriteAllText("E:/ZZZ/local-only/RemielleRuntimeRepair/20260904/unity-pipeline-pass.json",
            "{\"pass\":true,\"verifiedUtc\":\""+DateTime.UtcNow.ToString("o")+"\",\"unity\":\""+Application.unityVersion+"\"}");
        Debug.Log("RUNTIME_REPAIR_PIPELINE_VERIFIED");
    }
}
