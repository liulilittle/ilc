namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.GC -- System.GC icall mappings (il2cpp_gc.h).
// Owners: System.GC
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapGC(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Collect":
                if (count == 0) return I("il2cpp::runtime::icall_gc_collect", 0, false, false, false);
                // CG6 fix (gcops, 2026-08-09): the int32 generation argument
                // is DROPPED (the runtime has only the parameterless
                // full-collection icall) but ParamCount MUST be 1 -- the fold
                // pops ParamCount stack slots at the call, so 0 left the IL
                // argument stranded on the shadow stack (cg6 stack-desync
                // attribution). The "drop" cast skips it at emission.
                if (count == 1) return I("il2cpp::runtime::icall_gc_collect", 1, false, false, false, new[] { "drop" });
                // Collect(int generation, GCCollectionMode mode) -- batch
                // 150c: both arguments are dropped (the runtime has only the
                // parameterless full-collection icall; generation/mode are
                // ignored -- documented deviation). The fold still pops all
                // ParamCount slots.
                if (count == 2) return I("il2cpp::runtime::icall_gc_collect", 2, false, false, false, new[] { "drop", "drop" });
                throw new NotSupportedException("GC.Collect(int) not wired: the runtime has only the parameterless icall");
            case "GetTotalMemory":
                return I("il2cpp::runtime::icall_gc_get_total_memory", 1, true, false, false, new[] { "drop" }, "l");
            case "GetGeneration": return I("il2cpp::runtime::icall_gc_get_generation", 1, true, false, false);
            case "KeepAlive": return I("il2cpp::runtime::icall_gc_keep_alive", 1, false, false, false);
            case "SuppressFinalize": return I("il2cpp::runtime::icall_gc_suppress_finalize", 1, false, false, false);
            case "ReRegisterForFinalize": return I("il2cpp::runtime::icall_gc_re_register_for_finalize", 1, false, false, false);
            case "GetAllocatedBytes": return I("il2cpp::runtime::icall_gc_get_allocated_bytes", 0, true, false, false, null, "l");
            // P6: GC.GetTotalAllocatedBytes(bool precise): approximate total
            // allocated bytes. The 'precise' arg is dropped (the runtime's
            // counter is always approximate; no generational sub-allocator).
            case "GetTotalAllocatedBytes": return I("il2cpp::runtime::icall_gc_get_total_allocated_bytes_approximate", 1, true, false, false, new[] { "drop" }, "l");
            // M7 gcext wiring (2026-08-08): GC.WaitForPendingFinalizers() -> the
            // direct finalizer trigger (il2cpp_gc_ext.h -- icall_gc_finalize_trigger
            // runs any due/pending finalizers synchronously, the gcext two-
            // collection model's synchronous counterpart to .NET's finalizer
            // thread; see the gcext.cpp docblock + test_gcext.cpp §9). Static,
            // void, 0 params -- the same shape as the parameterless Collect.
            // The runtime surface was verified present (gcext.cpp:181) before
            // wiring -- no fabricated entry.
            case "WaitForPendingFinalizers":
                return I("il2cpp::runtime::icall_gc_finalize_trigger", 0, false, false, false);
            // GC latency-mode / LOH-compaction-mode preferences (net48 + netcore).
            // The collector is a single-threaded mark-sweep with no generational
            // tuning, so these are stored preferences echoed back by the getters
            // (see gcapi.cpp). GCLatencyMode / GCLargeObjectHeapCompactionMode are
            // int32 enums passed by value.
            case "GetGCLatencyMode": return I("il2cpp::runtime::icall_gc_get_latency_mode", 0, true, false, false);
            case "SetGCLatencyMode": return I("il2cpp::runtime::icall_gc_set_latency_mode", 1, false, false, false);
            case "GetLOHCompactionMode": return I("il2cpp::runtime::icall_gc_get_loh_compaction_mode", 0, true, false, false);
            case "SetLOHCompactionMode": return I("il2cpp::runtime::icall_gc_set_loh_compaction_mode", 1, false, false, false);
            case "IsServerGC":
            case "get_IsServerGC": return I("il2cpp::runtime::icall_gc_is_server_gc", 0, true, false, false);
            // P6 batch: GC stats methods -- all runtime functions already exist
            // in il2cpp_gc.h / gc.cpp; just need MethodMap wiring.
            case "GetSegmentSize": return I("il2cpp::runtime::il2cpp_gc_get_segment_size", 0, true, false, false, null, "l");
            case "GetLastGCPercentTimeInGC": return I("il2cpp::runtime::il2cpp_gc_get_last_gc_percent_time_in_gc", 0, true, false, false);
            case "GetGenerationSize": return I("il2cpp::runtime::il2cpp_gc_get_generation_size", 1, true, false, false, new[] { "i" }, "l");
            case "GetMaxGeneration": return I("il2cpp::runtime::il2cpp_gc_get_max_generation", 0, true, false, false);
            case "GetAllocatedBytesForCurrentThread": return I("il2cpp::runtime::il2cpp_gc_get_allocated_bytes_for_current_thread", 0, true, false, false, null, "l");
            case "GetFragmentedBytes": return I("il2cpp::runtime::il2cpp_gc_get_fragmented_bytes", 0, true, false, false, null, "l");
            default: return null;
        }
    }
}
