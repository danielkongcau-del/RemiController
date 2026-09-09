using System;
using System.IO;
using UnityEditor;
public static class RemielleImportSettings
{
 public static void ApplyAnimationSettings()
 {
  // Native globalgamemanagers / PlayerSettings (class 129, path 1) explicitly
  // enables this. Keep the signed source curves; clamp at the skinning stage.
  PlayerSettings.legacyClampBlendShapeWeights = true;
 }
 public static void Apply(string folder)
 {
  ApplyAnimationSettings();
  foreach(var path in Directory.GetFiles(folder,"*.glb"))
  {
   var importer=AssetImporter.GetAtPath(path);if(importer==null)throw new InvalidOperationException("GLB importer missing: "+path);
   var serialized=new SerializedObject(importer);
   var setting=serialized.FindProperty("_blendShapeFrameWeight");
   if(setting==null)throw new InvalidOperationException("UnityGLTF blendshape setting missing");
   var option=setting.FindPropertyRelative("_option");
   // Unity source animation weights use 0..100; glTF targets use 0..1.
   if(option.intValue!=1){option.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();}
  }
 }
}
