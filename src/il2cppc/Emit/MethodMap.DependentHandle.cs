namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.DependentHandle -- System.Runtime.DependentHandle icall mappings
// (src/runtime/include/internal/il2cpp_dependhandle.h -- the dependhandle.cpp
// degraded weak-primary table: primary registered as a weak slot, secondary
// as a strong root range).
// Owners: System.Runtime.DependentHandle (+ the corert-era spelling
// System.Runtime.CompilerServices.DependentHandle).
//
// VALUE MODEL: a DependentHandle value holds an intptr_t table handle. It
// rides in the M-stage pointer-carrier slot; instance receivers follow the
// IntPtr domain precedent (ldloca -> "deref:i" reads the carrier slot and the
// int32 value sign-extends to intptr_t on the call; plain value -> "").
//
// SURFACE (all icalls VERIFIED in the runtime):
//   GetPrimary()                    -> icall_dependenthandle_get_target
//   SetPrimaryAndSecondary(obj,obj) -> icall_dependenthandle_set_primary_and_secondary
//   Free()                          -> icall_dependenthandle_free (bool, dropped)
//   GetPrimaryAndSecondary(out obj) -> icall_dependenthandle_get_target_and_dependent
//                                     (Il2CppObject** out slot via the "addr:o"
//                                     emitter cast)
//   nInitialize(obj,obj,IntPtr&)     -> icall_dependenthandle_n_init (adapter)
//   nGetPrimary(IntPtr,Object&)      -> icall_dependenthandle_n_get_primary (adapter)
//   nGetPrimaryAndSecondary(h,o&,o&) -> icall_dependenthandle_n_get_primary_and_secondary (adapter)
//   nFree(IntPtr)                    -> icall_dependenthandle_free (bool, dropped)
//   get_IsAllocated / GetHashCode / DependentHandle(IntPtr) -> pure managed
//                                     surface (handle != 0 / IntPtr hash) --
//                                     no icall, not wired here
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDependentHandle(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "GetPrimary":
                // instance object GetPrimary(): the handle's primary referent
                // (null when collected). The receiver is the intptr carrier.
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_dependenthandle_get_target", 0, true, true, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            case "GetPrimaryAndSecondary":
                // instance object GetPrimaryAndSecondary(out object): returns
                // the primary + writes the secondary -- the "addr:o" cast
                // carries the Object& out slot (same arm as the n* statics).
                if (count != 1 || ps[0] != 0x1C) return null;
                return I("il2cpp::runtime::icall_dependenthandle_get_target_and_dependent", 1, true, true, true,
                    new[] { receiverIsAddress ? "deref:i" : "", "addr:o" });
            case "SetPrimaryAndSecondary":
                // instance void SetPrimaryAndSecondary(object, object): both
                // referents are managed references ("" passthrough).
                if (count != 2) return null;
                return I("il2cpp::runtime::icall_dependenthandle_set_primary_and_secondary", 2, false, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "", "", "" });
            case "Free":
                // instance void Free(): returns bool in the runtime (dropped).
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_dependenthandle_free", 0, false, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            // net48 static-extern n* surface (FSI-decoded mscorlib counts --
            // Object = 0x1C, IntPtr& = IntPtr element byte, Object& = 0x1C
            // element byte; ReadParamByte strips the 0x10 BYREF marker). All
            // static (inst false): casts ride per-param, no receiver entry.
            case "nInitialize":
                // static void nInitialize(Object, Object, IntPtr&): alloc the
                // handle + store through the IntPtr& slot ("addr:i" -- the
                // slot is int32-sized in M-stage; the adapter narrows).
                if (count != 3 || ps[0] != 0x1C || ps[1] != 0x1C || !IsIntPtrByte(ps[2])) return null;
                return I("il2cpp::runtime::icall_dependenthandle_n_init", 3, false, false, false,
                    new[] { "", "", "addr:i" });
            case "nGetPrimary":
                // static void nGetPrimary(IntPtr, Object&): adapter writes
                // the primary through the Object& slot ("addr:o").
                if (count != 2 || !IsIntPtrByte(ps[0]) || ps[1] != 0x1C) return null;
                return I("il2cpp::runtime::icall_dependenthandle_n_get_primary", 2, false, false, false,
                    new[] { "", "addr:o" });
            case "nGetPrimaryAndSecondary":
                // static void nGetPrimaryAndSecondary(IntPtr, Object&,
                // Object&): both out slots ride "addr:o".
                if (count != 3 || !IsIntPtrByte(ps[0]) || ps[1] != 0x1C || ps[2] != 0x1C) return null;
                return I("il2cpp::runtime::icall_dependenthandle_n_get_primary_and_secondary", 3, false, false, false,
                    new[] { "", "addr:o", "addr:o" });
            case "nFree":
                // static void nFree(IntPtr): runtime bool dropped (the
                // instance-Free precedent).
                if (count != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::icall_dependenthandle_free", 1, false, false, false,
                    new[] { "" });
            default:
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such member): DependentHandle::{name} -- the runtime surface covers GetPrimary / " +
                    "SetPrimaryAndSecondary / Free / nInitialize / nGetPrimary / nGetPrimaryAndSecondary / nFree " +
                    "(il2cpp_dependhandle.h); get_IsAllocated and GetHashCode are pure-managed (handle != 0 / IntPtr hash) " +
                    "[Reference Source: net48 mscorlib System.Runtime.DependentHandle]");
        }
    }
}
