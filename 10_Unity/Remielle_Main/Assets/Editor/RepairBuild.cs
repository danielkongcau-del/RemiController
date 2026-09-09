using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class RepairBuild
{
 public static void Run()
 {
  AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
  var errors = new System.Collections.Generic.List<string>();
  Application.LogCallback handler=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(message);};
  Application.logMessageReceived+=handler;
  try { V2aAssembly.Run(); if(errors.Count>0)throw new Exception(string.Join("\n",errors));
  V2bMaterialBind.Run(); if(errors.Count>0)throw new Exception(string.Join("\n",errors)); RepairAssetAudit.Run(); }
  finally { Application.logMessageReceived-=handler; }
  Debug.Log("REPAIR_BUILD_VERIFIED");
 }
}
