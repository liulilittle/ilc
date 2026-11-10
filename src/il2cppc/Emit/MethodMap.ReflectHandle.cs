namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ReflectHandle -- reflection handle value-semantics mappings
// (src/runtime/include/internal/reflecthandle.h -- the b23refh domain:
// RuntimeTypeHandle / RuntimeMethodHandle / RuntimeFieldHandle value ops).
// Owners: System.RuntimeTypeHandle / System.RuntimeMethodHandle /
// System.RuntimeFieldHandle
//
// VALUE MODEL (refh-b23 section 2.1): each handle value type wraps a raw runtime
// pointer -- RuntimeTypeHandle = Il2CppClass*, RuntimeMethodHandle = const
// Il2CppMethodInfo*, RuntimeFieldHandle = const Il2CppFieldInfo*. These ride
// in the M-stage pointer carrier (an intptr_t slot, the IntPtr precedent).
// Instance receivers are therefore:
//   * ldloca receiver (receiverIsAddress) -> "deref:l" (read the intptr slot)
//   * plain VALUE receiver -> "" pass-through
// DEPENDENCY (read-only CodeGenerator domain, reported): the emitter must
// (a) add `#include "reflecthandle.h"` to generated.h (refh P1), and
// (b) give the handle value types an intptr_t slot via TypeCppName, and
// (c) for the ADDRESS receiver form emit the pointer deref to Il2CppClass*.
// Until then the mappings are resolver-complete but generated C++ that
// exercises them needs those CodeGenerator patches (CG-3).
//
// SURFACE (all functions VERIFIED in the runtime, namespace il2cpp):
//   get_Value        -> il2cpp_rthandle_get_value (Il2CppClass* -> intptr_t)
//   Equals           -> il2cpp_rthandle_equals (Il2CppClass*, Il2CppClass*)
//   GetHashCode      -> il2cpp_rthandle_get_hash_code
//   GetRuntimeType   -> il2cpp_rthandle_get_runtime_type (-> Type, ref)
//   + the method/field handle twins (il2cpp_rmethodhandle_* /
//     il2cpp_rfieldhandle_*) -- value + equals + hash only.
//
// All defensive: null/default handles return 0/nullptr, never throw (net48
// probe). Unknown members -> LOUD with the covered surface.
//
// LOUD classification (impl-type2): all three LOUD throws are
// WHITELIST category (4) -- net48-nonexistent shapes, not statically-analyzable
// statements. The owners are exactly the three net48 handle value types
// (System.RuntimeTypeHandle / System.RuntimeMethodHandle /
// System.RuntimeFieldHandle); the wired surface is get_Value / Equals /
// GetHashCode plus GetRuntimeType on the TYPE handle only -- net48
// RuntimeMethodHandle/RuntimeFieldHandle declare NO GetRuntimeType member, and
// the remaining member catch-all names the wired surface (pinned by the
// runtime reflecthandle.h contract, which is the authority for this domain).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapReflectHandle(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        var prefix = owner switch
        {
            "System.RuntimeTypeHandle" => "rthandle",
            "System.RuntimeMethodHandle" => "rmethodhandle",
            "System.RuntimeFieldHandle" => "rfieldhandle",
            _ => throw new NotSupportedException($"{owner} is not a reflection-handle owner wired by MethodMap.ReflectHandle.cs"),
        };
        switch (name)
        {
            case "get_Value":
                // instance intptr get_Value(): the raw handle pointer. The
                // handle VALUE is modelled as an intptr_t slot (the
                // reflecthandle.h value model -- the transpiler gives the
                // handle types an intptr_t TypeCppName), so the _intptr
                // overload answers the value (the address receiver is
                // dereferenced by the "deref:l" cast). A null/default handle
                // answers 0 (net48).
                if (count != 0) return null;
                return I($"il2cpp::il2cpp_{prefix}_get_value_intptr", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, "ptr");
            case "Equals":
                // instance bool Equals(handle): identity (pointer equality,
                // name-equality for the type handle via icall_type_equals).
                if (count != 1) return null;
                return I($"il2cpp::il2cpp_{prefix}_equals", 1, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "", receiverIsAddress ? "deref:l" : "" });
            case "GetHashCode":
                // instance int32 GetHashCode(): deterministic hash over the
                // handle identity (null/default -> 0, never throws).
                if (count != 0) return null;
                return I($"il2cpp::il2cpp_{prefix}_get_hash_code", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" });
            case "GetRuntimeType":
                // instance Type GetRuntimeType(): handle -> Type (the full-name
                // string model); only the TYPE handle has this member -- the
                // method/field handles have no runtime helper (LOUD).
                if (count != 0) return null;
                if (prefix != "rthandle")
                    throw new NotSupportedException(
                        $"{owner}.GetRuntimeType not wired: only RuntimeTypeHandle has GetRuntimeType in the reflect-handle surface (reflecthandle.h)");
                return I("il2cpp::il2cpp_rthandle_get_runtime_type", 0, true, true, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, returnTypeName: "System.Type");
            case "GetFunctionPointer":
                // instance IntPtr GetFunctionPointer(): the method ENTRY ADDRESS
                // (the AOT function pointer, typeinfo.h fnPtr). net48
                // (RuntimeHandles.cs:1066): a zero handle throws
                // ArgumentNullException; otherwise the entry pointer is returned.
                // Only the METHOD handle has this member -- the type/field
                // handles declare no GetFunctionPointer in net48.
                if (count != 0) return null;
                if (prefix != "rmethodhandle")
                    throw new NotSupportedException(
                        $"{owner}.GetFunctionPointer not wired: only RuntimeMethodHandle has GetFunctionPointer in the reflect-handle surface (reflecthandle.h)");
                return I($"il2cpp::il2cpp_{prefix}_get_function_pointer_intptr", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, "ptr");
            default:
                throw new NotSupportedException(
                    $"{owner}::{name} not wired: the reflect-handle surface covers get_Value / Equals / " +
                    "GetHashCode (plus GetRuntimeType on the type handle) -- see reflecthandle.h");
        }
    }
}
