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
//   GetPrimaryAndSecondary(out obj) -> LOUD (the runtime
//                                     icall_dependenthandle_get_target_and_dependent
//                                     takes an Il2CppObject** out slot -- the
//                                     emitter has NO "addr:o" cast for a
//                                     reference-typed out argument; the
//                                     CodeGenerator cast switch (3224-3232
//                                     region) needs a
//                                     reinterpret_cast<Il2CppObject**> arm,
//                                     a read-only-domain patch)
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
                // out object: the emitter has no reference-typed out-address
                // cast ("addr:o" -- CodeGenerator read-only patch) -- LOUD.
                throw new NotSupportedException(
                    "DependentHandle.GetPrimaryAndSecondary not wired: the runtime icall takes an " +
                    "Il2CppObject** out slot and the emitter has no 'addr:o' cast for a reference-typed " +
                    "out argument (CodeGenerator EmitIcallCallArgs cast switch needs " +
                    "reinterpret_cast<Il2CppObject**> -- read-only-domain patch; alternatively the runtime " +
                    "could add a returning variant)");
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
            default:
                throw new NotSupportedException(
                    $"DependentHandle::{name} not wired: the runtime surface covers GetPrimary / " +
                    "SetPrimaryAndSecondary / Free (il2cpp_dependhandle.h); get_IsAllocated and GetHashCode " +
                    "are pure-managed (handle != 0 / IntPtr hash), GetPrimaryAndSecondary needs the emitter 'addr:o' cast");
        }
    }
}
