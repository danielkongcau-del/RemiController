// zcode 2026-09-07: C0 batch compile gate. Running this method in batch mode
// proves the whole project (vendored UnityHFSM, the four embedded
// com.nickmaltbie packages and the ControllerIntegration layer) compiles: a
// script compile failure makes the editor exit non-zero before this method
// can run and log the marker.
using UnityEditor;
using UnityEngine;

public static class ControllerIntegrationCompileCheck
{
    public static void Run()
    {
        Debug.Log("[C0] ControllerIntegration compile check OK");
    }
}
