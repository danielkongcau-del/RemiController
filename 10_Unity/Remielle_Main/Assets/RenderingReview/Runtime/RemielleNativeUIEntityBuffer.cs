using System;
using System.Runtime.InteropServices;
using UnityEngine;

// One captured entity is eight float4s. Preserve unrelated entities and flags.
public sealed class RemielleNativeUIEntityBuffer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Entity { public Vector4 a,b,c,d,e,f,g,h; }
    public readonly RemielleNativeUIProfile.Resource Source;
    public readonly GraphicsBuffer Buffer;
    public readonly byte[] Bytes;
    readonly Entity[] upload;
    bool disposed;
    public RemielleNativeUIEntityBuffer(RemielleNativeUIProfile.Resource source)
    {
        if(source?.structured==null||source.structured.Length==0||source.structured.Length%128!=0)
            throw new ArgumentException("Invalid native entity buffer");
        Source=source;Bytes=(byte[])source.structured.Clone();upload=new Entity[Bytes.Length/128];
        Buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,upload.Length,128);
        try { Upload(); } catch { Buffer.Dispose();throw; }
    }
    public void Reset() { System.Buffer.BlockCopy(Source.structured,0,Bytes,0,Bytes.Length); }
    public void Upload()
    {
        if(disposed)throw new ObjectDisposedException(nameof(RemielleNativeUIEntityBuffer));
        var pin=GCHandle.Alloc(upload,GCHandleType.Pinned);
        try { Marshal.Copy(Bytes,0,pin.AddrOfPinnedObject(),Bytes.Length); } finally { pin.Free(); }
        Buffer.SetData(upload);
    }
    public void Dispose() { if(disposed)return;disposed=true;Buffer.Dispose(); }
}
