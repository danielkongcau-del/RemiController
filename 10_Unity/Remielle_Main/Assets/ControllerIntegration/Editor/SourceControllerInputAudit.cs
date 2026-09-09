using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceControllerInputAudit
    {
        const string Key="RemielleControllerInputAudit";
        static double started;
        [InitializeOnLoadMethod]
        static void Resume()
        {
            if(!SessionState.GetBool(Key,false))return;
            started=EditorApplication.timeSinceStartup;EditorApplication.update-=Watch;EditorApplication.update+=Watch;
        }
        static void Watch()
        {
            if(EditorApplication.timeSinceStartup-started>180){SessionState.SetBool(Key,false);EditorApplication.Exit(2);return;}
            if(!EditorApplication.isPlaying)return;
            var probe=Object.FindFirstObjectByType<ControllerInputProbe>();if(!probe||!probe.completed)return;
            SessionState.SetBool(Key,false);Debug.Log("REMIELLE_CONTROLLER_INPUT_"+(probe.passed?"PASS":"FAIL"));EditorApplication.Exit(probe.passed?0:1);
        }
        public static void Run()=>Run(false);
        public static void RunDash()=>Run(true);
        static void Run(bool dash)
        {
            SourceControllerBuild.Run();
            var probe=new GameObject("VirtualInputProbe").AddComponent<ControllerInputProbe>();
            probe.controller=Object.FindFirstObjectByType<RemielleSourceController>();probe.reportDirectory=QualifiedIntegrationAudit.Output;
            probe.dashScenario=dash;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),dash?"Assets/ControllerIntegration/Scenes/Remielle_ControllerDashProbe.unity":"Assets/ControllerIntegration/Scenes/Remielle_ControllerInputProbe.unity");
            SessionState.SetBool(Key,true);Resume();EditorApplication.EnterPlaymode();
        }
    }
}
