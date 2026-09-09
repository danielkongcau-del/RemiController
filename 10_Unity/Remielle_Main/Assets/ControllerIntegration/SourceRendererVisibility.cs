using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

namespace Remielle.Controller
{
    // One exclusive visibility owner for the qualified presentation renderers.
    // The ability host must call a source action only after its prerequisites
    // are met. Constructor registration does not activate any ability.
    public sealed class SourceRendererVisibility : IDisposable
    {
        [Serializable] public sealed class Binding
        {
            public string sourceBlock,cab,pathID;
            public SkinnedMeshRenderer renderer;
            public Mesh importedMesh;
            public string Identity=>Key(sourceBlock,cab,pathID);
        }
        readonly NativeRendererVisibility stacks=new NativeRendererVisibility();
        readonly Dictionary<string,Binding> bindings=new Dictionary<string,Binding>();
        readonly Dictionary<string,bool> baseline=new Dictionary<string,bool>();
        readonly Dictionary<string,JObject> actions=new Dictionary<string,JObject>();
        bool disposed;
        public int RendererCount=>bindings.Count;
        public static string Key(string block,string cab,string pathID)=>block.Replace('\\','/').ToLowerInvariant()+"|"+cab+"|"+pathID;
        public SourceRendererVisibility(IEnumerable<Binding> renderers,string json)
        {
            var pack=JObject.Parse(json);
            if((string)pack["schema"]!="remielle-visibility-events-v1")throw new ArgumentException("Unknown source visibility contract");
            var objects=new HashSet<SkinnedMeshRenderer>();
            foreach(var b in renderers)
            {
                if(b==null||string.IsNullOrEmpty(b.sourceBlock)||string.IsNullOrEmpty(b.cab)||string.IsNullOrEmpty(b.pathID)||
                    !b.renderer||!b.importedMesh||b.renderer.sharedMesh!=b.importedMesh||!objects.Add(b.renderer))throw new ArgumentException("Unqualified or duplicate source renderer");
                bindings.Add(b.Identity,b);baseline.Add(b.Identity,b.renderer.enabled);stacks.Register(b.Identity,b.renderer.enabled);
            }
            if(bindings.Count!=27)throw new ArgumentException("Expected the 27 source presentation renderers");
            foreach(JObject row in pack["actions"])
            {
                foreach(var target in row["rendererTargets"])
                    if(!bindings.ContainsKey(TargetKey(target)))throw new ArgumentException("Visibility target has no qualified renderer binding");
                string type=(string)row["action"]["$type"];
                if(type!="PushRenderVisibleAction"&&type!="PopRenderVisibleAction")throw new ArgumentException("Unsupported source visibility action");
                actions.Add(ActionKey((string)row["ability"],(string)row["pointer"]),(JObject)row.DeepClone());
            }
        }
        static string TargetKey(JToken target)=>Key((string)target["sourceBlock"],(string)target["cab"],(string)target["rendererPathID"]);
        static string ActionKey(string ability,string pointer)=>ability+"\n"+pointer;
        public void Execute(string ability,string pointer)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceRendererVisibility));
            var row=actions[ActionKey(ability,pointer)];var action=row["action"];
            // Reject a changed/destroyed target before mutating the stack.
            foreach(var b in bindings.Values)if(!b.renderer||b.renderer.sharedMesh!=b.importedMesh)throw new InvalidOperationException("Bound renderer was replaced");
            string tag=(string)action["Tag"];
            if((string)action["$type"]=="PopRenderVisibleAction")stacks.Pop(tag);
            else stacks.Push(tag,(bool)action["Visible"],(bool)action["ApplyAllRenderers"],row["rendererTargets"].Select(TargetKey));
            foreach(var pair in bindings)pair.Value.renderer.enabled=stacks.GetVisible(pair.Key);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var pair in bindings)if(pair.Value.renderer)pair.Value.renderer.enabled=baseline[pair.Key];
        }
    }
}
