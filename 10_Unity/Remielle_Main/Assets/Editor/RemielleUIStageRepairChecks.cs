// Sequenced in one editor process so source texture construction is completed
// before either audit consumes it. Saved model, animation and scene are untouched.
public static class RemielleUIStageRepairChecks
{
    public static void RunAll()
    {
        Run();
        RemielleNativeUISequenceGpuAudit.RunCapturedSequences();
    }
    public static void Run()
    {
        RemielleUITextureAssetBuild.Run();
        RemielleNativeUIMaterialGpuAudit.Run();
        RemielleNativeUISequenceGpuAudit.Run();
    }
    public static void RunBase()
    {
        RemielleUITextureAssetBuild.Run();
        RemielleNativeUIMaterialGpuAudit.Run();
    }
}
