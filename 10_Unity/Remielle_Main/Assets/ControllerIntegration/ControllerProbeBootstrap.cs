using System;
using System.IO;
using UnityEngine;

namespace Remielle.Controller
{
    // Opt-in background Player validation. The normal preview has no test
    // devices, automated input or automatic exit.
    public sealed class ControllerProbeBootstrap : MonoBehaviour
    {
        ControllerInputProbe probe;
        ControllerSpecialProbe specialProbe;
        ControllerHitProbe hitProbe;
        ControllerBufferProbe bufferProbe;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if(Application.isEditor)return;
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--controller-input-probe");
            bool dash=false;if(i<0){i=Array.IndexOf(args,"--controller-dash-probe");dash=i>=0;}
            bool combo=false;if(i<0){i=Array.IndexOf(args,"--controller-combo-probe");combo=i>=0;}
            bool special=false;if(i<0){i=Array.IndexOf(args,"--controller-special-probe");special=i>=0;}
            bool hit=false;if(i<0){i=Array.IndexOf(args,"--controller-hit-probe");hit=i>=0;}
            bool buffer=false;if(i<0){i=Array.IndexOf(args,"--controller-buffer-probe");buffer=i>=0;}
            if(i<0)return;
            if(i+1>=args.Length)throw new ArgumentException("Controller probe needs an output directory");
            var controller=FindFirstObjectByType<RemielleSourceController>();
            if(!controller)throw new InvalidOperationException("Controller preview scene missing");
            Application.runInBackground=true;
            var host=new GameObject("ControllerPlayerProbe");var bootstrap=host.AddComponent<ControllerProbeBootstrap>();
            if(buffer)
            {
                bootstrap.bufferProbe=host.AddComponent<ControllerBufferProbe>();bootstrap.bufferProbe.controller=controller;
                bootstrap.bufferProbe.reportDirectory=Path.GetFullPath(args[i+1]);return;
            }
            if(hit)
            {
                bootstrap.hitProbe=host.AddComponent<ControllerHitProbe>();bootstrap.hitProbe.controller=controller;
                bootstrap.hitProbe.reportDirectory=Path.GetFullPath(args[i+1]);return;
            }
            if(special)
            {
                bootstrap.specialProbe=host.AddComponent<ControllerSpecialProbe>();bootstrap.specialProbe.controller=controller;
                bootstrap.specialProbe.reportDirectory=Path.GetFullPath(args[i+1]);return;
            }
            bootstrap.probe=host.AddComponent<ControllerInputProbe>();bootstrap.probe.controller=controller;bootstrap.probe.reportDirectory=Path.GetFullPath(args[i+1]);
            bootstrap.probe.dashScenario=dash;
            bootstrap.probe.comboScenario=combo;
        }
        void LateUpdate(){if(probe&&probe.completed)Application.Quit(probe.passed?0:1);if(specialProbe&&specialProbe.completed)Application.Quit(specialProbe.passed?0:1);if(hitProbe&&hitProbe.completed)Application.Quit(hitProbe.passed?0:1);if(bufferProbe&&bufferProbe.completed)Application.Quit(bufferProbe.passed?0:1);}
    }
}
