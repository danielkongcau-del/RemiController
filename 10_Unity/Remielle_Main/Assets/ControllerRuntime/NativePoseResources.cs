using System;
using System.Collections.Generic;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    // Storage created by cef2a0/cfb700/cfbca0/ce5550. Descriptor production,
    // Avatar defaults and humanoid evaluation remain separate from allocation.
    public sealed class NativePoseResources
    {
        internal readonly NativePoseStream Stream;
        internal readonly byte[] Root;
        readonly byte[] bytes,additional,secondary;
        public const int AdditionalStorageSize=0x520;
        public int ExtraByteCount=>bytes.Length;
        public bool HasAdditionalPose=>additional!=null;
        public bool HasSecondaryPose=>secondary!=null;
        public byte RootFlag { get; internal set; }
        public NativePoseStream Pose=>new NativePoseStream(Stream.Pose(),Stream.Mask(),Stream.StreamFlag,
            Stream.ReferenceFlag0,Stream.ReferenceFlag1,Stream.BindingLayout);
        public byte[] RootStorage=>(byte[])Root.Clone();
        public byte[] ByteValues=>(byte[])bytes.Clone();
        public byte[] AdditionalStorage=>additional==null?null:(byte[])additional.Clone();
        public byte[] SecondaryStorage=>secondary==null?null:(byte[])secondary.Clone();

        NativePoseResources(int[] counts,int extra,bool context82,bool context88,NativePoseBindingLayout layout)
        {
            var t=new float[checked(counts[0]*4)];var q=new float[checked(counts[1]*4)];
            var s=Enumerable.Repeat(1f,checked(counts[2]*4)).ToArray();
            for(int i=0;i<counts[1];i++)q[i*4+3]=1f;
            Stream=new NativePoseStream(new NativePoseData(t,q,s,new float[counts[3]],new uint[counts[4]]),
                counts.Select(n=>new byte[n]).ToArray(),bindingLayout:layout);
            bytes=new byte[extra];Root=new byte[NativeDefaultPoseInput.RootStorageSize];InitializeRoot(Root);
            if(context82)
            {
                additional=new byte[AdditionalStorageSize];InitializeAdditional(additional);
                if(!context88){secondary=new byte[AdditionalStorageSize];InitializeAdditional(secondary);}
            }
        }

        // Explicit original value descriptor tags. Tags 2/5 and values outside
        // 1..9 are ignored by both native counters. Byte tags have no mask array.
        public static NativePoseResources FromDescriptorTypes(IReadOnlyList<int> types,bool context82,bool context88)
        {
            if(types==null)throw new ArgumentNullException(nameof(types));
            var counts=new int[5];int extra=0;
            foreach(int type in types)switch(type)
            {
                case 1:counts[3]++;break;case 3:counts[4]++;break;
                case 6:counts[0]++;break;case 7:counts[1]++;break;case 8:counts[2]++;break;
                case 4:case 9:extra++;break;
            }
            return new NativePoseResources(counts,extra,context82,context88,null);
        }

        // An already qualified layout is owned by the caller. This does not
        // claim to build the game's merged Avatar/value binding descriptor list.
        public static NativePoseResources ForLayout(NativePoseBindingLayout layout,bool context82,bool context88)
        {
            if(layout==null)throw new ArgumentNullException(nameof(layout));
            return new NativePoseResources(layout.Counts(),0,context82,context88,layout);
        }

        static void Size(byte[] storage,int expected)
        {if(storage==null||storage.Length!=expected)throw new ArgumentException("Original storage extent differs");}
        static void One(byte[] storage,int at)
        {storage[at]=0;storage[at+1]=0;storage[at+2]=0x80;storage[at+3]=0x3f;}
        static void Trs(byte[] storage,int at)
        {
            Array.Clear(storage,at,0x30);One(storage,at+0x1c);
            for(int i=0;i<4;i++)One(storage,at+0x20+4*i);
        }
        public static void InitializeRoot(byte[] storage)
        {
            Size(storage,NativeDefaultPoseInput.RootStorageSize);
            Array.Clear(storage,0,4);Array.Clear(storage,0x30,4);
            Array.Clear(storage,0x10,0x20);One(storage,0x2c);
            for(int at=0x40;at<=0xd0;at+=0x30)Trs(storage,at);
            Array.Clear(storage,0x100,0x14);Trs(storage,0x120);Array.Clear(storage,0x150,4);
        }
        public static void InitializeAdditional(byte[] storage)
        {
            Size(storage,AdditionalStorageSize);Trs(storage,0);Array.Clear(storage,0x30,0x20);
            for(int i=0;i<4;i++)
            {
                int at=0x50+i*0x60;Trs(storage,at);Array.Clear(storage,at+0x30,8);
                Array.Clear(storage,at+0x40,0x10);Array.Clear(storage,at+0x50,4);
            }
            foreach(int at in new[]{0x1d0,0x260}){Trs(storage,at);Array.Clear(storage,at+0x30,0x60);}
            Array.Clear(storage,0x2f0,0xdc);Array.Clear(storage,0x3d0,0x150);
        }
    }
}
