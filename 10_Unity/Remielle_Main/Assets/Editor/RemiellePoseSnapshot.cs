using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

// Unity caches GPU skinning within a frame. Batch callbacks that render several
// sampled poses must bake each snapshot, otherwise later images can show stale
// bones/expressions even though BakeMesh and the animation assertions are correct.
public sealed class RemiellePoseSnapshot : System.IDisposable
{
    readonly List<GameObject> copies = new List<GameObject>();
    readonly SkinnedMeshRenderer[] sources;
    public RemiellePoseSnapshot(GameObject root)
    {
        sources=root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s=>s.enabled&&s.gameObject.activeInHierarchy).ToArray();
        try
        {
            foreach(var source in sources)
            {
                var copy=new GameObject(source.name+"_PoseSnapshot");copies.Add(copy);
                copy.layer=source.gameObject.layer;copy.transform.SetParent(source.transform,false);
                var mesh=new Mesh();source.BakeMesh(mesh);
                copy.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=copy.AddComponent<MeshRenderer>();renderer.sharedMaterials=source.sharedMaterials;
                renderer.shadowCastingMode=source.shadowCastingMode;renderer.receiveShadows=source.receiveShadows;
                var block=new MaterialPropertyBlock();source.GetPropertyBlock(block);renderer.SetPropertyBlock(block);
                // Indexed blocks override the renderer-wide block. Material
                // animation uses these slots; snapshots must preserve them too.
                for(int slot=0;slot<source.sharedMaterials.Length;slot++)
                {
                    block.Clear();source.GetPropertyBlock(block,slot);
                    if(!block.isEmpty)renderer.SetPropertyBlock(block,slot);
                }
                source.enabled=false;
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        foreach(var copy in copies)
        {
            if(copy==null)continue;
            var filter=copy.GetComponent<MeshFilter>();if(filter!=null)Object.DestroyImmediate(filter.sharedMesh);
            Object.DestroyImmediate(copy);
        }
        foreach(var source in sources)if(source!=null)source.enabled=true;
        copies.Clear();
    }
}
