// zcode 2026-09-06, loop aggregate-pose-resources: ports the original cd3060
// aggregate-node preparation semantics — ready gate before the metadata
// refresh, container demand check, two work arrays, per-layer table factory
// and default-allow marks. Container management under count drift
// (cd4bd0/fe480/10cd10) and the merged-descriptor producers are explicit
// uncovered boundaries (native-aggregate-pose-resources-evidence).
using System;
using System.Collections.Generic;

namespace Remielle.ControllerRuntime
{
    // Product of the original cfbca0 factory (0x50-byte shape); the two
    // region fills happen inside one cf8290 call per layer.
    public sealed class NativeAggregateLayerTable
    {
        public bool RegionsFilled;
        public int RegionFillCalls;
    }

    public sealed class NativeAggregatePoseResources
    {
        public bool Ready { get; private set; }      // node +0x104
        public bool Dirty { get; private set; }      // node +0x102
        public int LayerCount { get; private set; }  // container 1 demand count
        public int ContainerCapacity { get; private set; }
        public int MetadataCapacity { get; private set; }
        public int MetadataCount { get; private set; }
        public object Derived { get; private set; }              // cfb700 product
        public NativeAggregateLayerTable[] SlotTable { get; private set; } = Array.Empty<NativeAggregateLayerTable>(); // +0x130
        public byte[] Marks { get; private set; } = Array.Empty<byte>();     // +0x140, default allow
        public Dictionary<string, int> Calls { get; } = new Dictionary<string, int>();
        public List<long> Allocations { get; } = new List<long>(); // packed size<<32|alignment

        public static NativeAggregatePoseResources Preset(int layerCount)
        {
            var resources = new NativeAggregatePoseResources
            {
                LayerCount = layerCount, ContainerCapacity = layerCount * 2, MetadataCapacity = layerCount * 2, MetadataCount = layerCount
            };
            return resources;
        }

        public void MarkResourcesDirty() => Dirty = true;

        void Called(long rva) { string key = "0x" + rva.ToString("x"); Calls[key] = Calls.TryGetValue(key, out int n) ? n + 1 : 1; }
        void Alloc(int size, int alignment) { Allocations.Add(((long)size << 32) | (uint)alignment); Called(0xc79d70); }

        public void Prepare(int sourceCount, object mergedDescriptors)
        {
            if (mergedDescriptors == null) throw new ArgumentNullException(nameof(mergedDescriptors));
            Called(0xcd3060);
            // The ready early path (0xcd34e9) also republishes the gate and
            // clears the dirty byte before returning; nothing else is touched.
            if (Ready) { Dirty = false; return; }
            Called(0xcd9cb0);
            // Metadata refresh (cd9cb0): a matching metadata count skips the
            // refresh entirely (the original je); drift refreshes every
            // source entry and rebuilds the array through the engine resize
            // chain only when capacity is insufficient.
            bool metadataRefresh = MetadataCount != sourceCount;
            if (metadataRefresh)
            {
                Called(0x130bc60);
                if (MetadataCapacity < sourceCount)
                {
                    Called(0x10cd10); Called(0xa632f0); Called(0x682ce0); Called(0x681960); Called(0x67e230);
                    Alloc(0x20 * sourceCount, 16);
                    MetadataCapacity = sourceCount * 2;
                }
                MetadataCount = sourceCount;
                Called(0xcd8ae0);   // dirty propagation after the refresh
            }
            // Container 1 demand check with real drift handling (zcode loop
            // #2): drain old elements, report and release via the engine
            // entries, then resize through fe480 -> a632f0.
            if (LayerCount != sourceCount)
            {
                Called(0xcd4bd0);   // the original je skips the whole drain/resize block on matching counts
                for (int i = 0; i < LayerCount; i++)
                {
                    Called(0xcf0170);          // per-element drain
                    Called(0xce4880);          // dispatch forward
                    for (int k = 0; k < 5; k++) Called(0xba7a0); // empty receiver callbacks
                }
                if (LayerCount > 0)
                {
                    Called(0x682c00);          // drain-completion report
                    Called(0x67ed20);          // release entry
                    Called(0x67f1e0);          // ownership query: no owner, release skipped
                }
                if (sourceCount == 0)
                {
                    // Draining every layer leaves container 1 fully empty and
                    // the matching zero counts skip the resize entirely.
                    LayerCount = 0; ContainerCapacity = 0;
                }
                else
                {
                    Called(0xfe480); Called(0xa632f0); Called(0x682ce0); Called(0x681960); Called(0x67e230);
                    Alloc(8 * sourceCount, 16);
                    LayerCount = sourceCount; ContainerCapacity = sourceCount * 2;
                }
            }
            // Main loop: per-source initialization (metadata refresh only),
            // the derived set (always once), work arrays and per-layer tables.
            if (sourceCount > 0)
            {
                SlotTable = new NativeAggregateLayerTable[sourceCount];
                Marks = new byte[sourceCount];
                if (metadataRefresh)
                {
                    for (int i = 0; i < sourceCount; i++)
                    {
                        Alloc(56, 8);              // cef2a0 stream factory object
                        Called(0xcfb700); Alloc(96, 64); Alloc(96, 8);
                        Called(0xcef2a0); Alloc(80, 8);
                        Called(0xce5550); Alloc(0x160, 16);
                        Called(0xcfbca0);   // metadata entry table factory (no arena byte here)
                    }
                }
                Called(0xcfb700); Alloc(96, 64); Alloc(96, 8);  // derived set
                for (int i = 0; i < sourceCount; i++) Marks[i] = 1; // default allow
                Alloc(8 * sourceCount, 8);
                Alloc(sourceCount, 1);
                for (int i = 0; i < sourceCount; i++)
                {
                    Called(0xcfa460);          // record matching over (empty) descriptor tables
                    Called(0xcfbca0);          // layer table factory
                    var table = new NativeAggregateLayerTable();
                    Called(0xcf8290);          // two region fills inside one call
                    table.RegionsFilled = true; table.RegionFillCalls = 2;
                    SlotTable[i] = table;
                    Alloc(80, 8);
                }
            }
            else
            {
                Called(0xcfb700); Alloc(96, 64); Alloc(96, 8);
            }
            Derived = new object();
            Ready = true; Dirty = false;
        }
    }
}
