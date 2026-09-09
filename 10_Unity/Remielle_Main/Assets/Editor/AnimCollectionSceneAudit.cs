// zcode 2026-09-07: real-runtime audit for the AnimCollection scene in the
// codex batch-audit tradition: open the scene, enter Play Mode, run real
// frames with the components active, capture every runtime exception via
// logMessageReceived, verify runtime state, write a JSON report and exit.
// Survives the enter-play domain reload via [InitializeOnLoad] + SessionState
// (static subscriptions are cleared on reload). Guards against the exact
// failure this scene shipped with once: components that pass static YAML
// review but throw every frame under the new Input System (activeInputHandler=1).
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AnimCollectionSceneAudit
{
    const string ScenePath = "Assets/RenderingReview/AnimCollection/AnimCollection.unity";
    const string ReportPath = "E:/ZZZ/local-only/RemielleZcode/takeover-smoke/animcollection-runtime-verification.json";
    const string SessionKey = "zcode.animcollection.audit.active";

    static int phase; // 0=entering play, 1=playing frames, 2=leaving play
    static int framesRun;
    static readonly StringBuilder Report = new StringBuilder();
    static bool pass = true;
    static int exceptionCount;
    static bool subscribed;

    static AnimCollectionSceneAudit()
    {
        // Re-arm after the enter-play domain reload.
        if (SessionState.GetBool(SessionKey, false)) Subscribe();
    }

    [MenuItem("Remielle/Audit AnimCollection Runtime")]
    public static void Run()
    {
        SessionState.SetBool(SessionKey, true);
        Subscribe();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        phase = 0; framesRun = 0; exceptionCount = 0; pass = true;
        EditorApplication.isPlaying = true;
    }

    static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        Application.logMessageReceived += Capture;
        EditorApplication.update += Pump;
    }

    static void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;
        Application.logMessageReceived -= Capture;
        EditorApplication.update -= Pump;
    }

    static void Capture(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        {
            exceptionCount++;
            if (exceptionCount <= 3) Report.AppendLine("  \"runtimeException_" + exceptionCount + "\": \"" + Truncate(condition.Replace("\"", "'").Replace("\n", " "), 160) + "\",");
        }
    }

    static void Pump()
    {
        if (phase == 0)
        {
            if (EditorApplication.isPlaying && Application.isPlaying)
            {
                phase = 1;
                Debug.Log("[AnimCollectionAudit] entered play mode");
            }
            return;
        }
        if (phase == 1)
        {
            framesRun++;
            if (framesRun >= 60) { phase = 2; Verify(); EditorApplication.isPlaying = false; }
            return;
        }
        if (phase == 2 && !EditorApplication.isPlaying && !Application.isPlaying) Finish();
    }

    static void Verify()
    {
        try
        {
            var controls = UnityEngine.Object.FindObjectsByType<AnimCollectionControls>(FindObjectsSortMode.None).FirstOrDefault();
            int hits = controls != null ? controls.RuntimePartCount : -1;
            Report.AppendLine("  \"partRendererHits\": " + hits + ",");
            if (hits != 27) pass = false;

            var free = UnityEngine.Object.FindObjectsByType<AnimCollectionFreeCamera>(FindObjectsSortMode.None).FirstOrDefault();
            Report.AppendLine("  \"freeCameraPresent\": " + (free != null).ToString().ToLower() + ",");
            if (free == null) pass = false;

            var follow = UnityEngine.Object.FindObjectsByType<RemielleReviewControls>(FindObjectsSortMode.None).FirstOrDefault();
            bool decoupled = follow != null && follow.follow == null;
            Report.AppendLine("  \"followDecoupled\": " + decoupled.ToString().ToLower() + ",");
            if (!decoupled) pass = false;

            bool defaults = controls != null && controls.VerifyRuntimeDefaults();
            Report.AppendLine("  \"groupDefaultsVerified\": " + defaults.ToString().ToLower() + ",");
            if (!defaults) pass = false;
        }
        catch (Exception error)
        {
            pass = false;
            Report.AppendLine("  \"verifyError\": \"" + error.Message.Replace("\"", "'") + "\",");
        }
        Report.AppendLine("  \"framesRun\": " + framesRun + ",");
        Report.AppendLine("  \"runtimeExceptionCount\": " + exceptionCount + ",");
        if (exceptionCount > 0) pass = false;
    }

    static void Finish()
    {
        Unsubscribe();
        SessionState.SetBool(SessionKey, false);
        Report.Insert(0, "{\n  \"schema\": \"zcode-animcollection-runtime-v2\",\n");
        Report.AppendLine("  \"pass\": " + (pass ? "true" : "false"));
        Report.AppendLine("}");
        System.IO.File.WriteAllText(ReportPath, Report.ToString());
        Debug.Log((pass ? "ANIM_COLLECTION_RUNTIME_AUDIT_OK " : "ANIM_COLLECTION_RUNTIME_AUDIT_FAILED ") + ReportPath);
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
