namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.GCHandle -- System.Runtime.InteropServices.GCHandle icall mappings
// (src/runtime/include/internal/il2cpp_gchandle.h -- the gchandle.cpp handle
// table: 8 icalls over a static non-moving entry table, the gcext weak-ref
// model).
// Owner: System.Runtime.InteropServices.GCHandle
//
// VALUE MODEL: a GCHandle value rides in an int32_t slot -- the raw handle
// value (the report's proposed TypeCppName slot "System.Runtime.InteropServices.GCHandle"
// => "int32_t"; if the transpiler already generalizes single-int32-field
// structs to int32 slots, the CodeGenerator entry is a no-op -- the IntPtr
// slot is the existing precedent). Instance receivers are therefore:
//   * ldloca receiver (receiverIsAddress) -> "deref:i" (read the int32 slot)
//   * plain VALUE receiver -> "" pass-through
//
// SURFACE (all 8 icalls VERIFIED in the runtime):
//   Alloc(object[, GCHandleType])  -> icall_gchandle_alloc_normal / alloc
//   Free()                         -> icall_gchandle_free
//   get_Target / set_Target        -> icall_gchandle_get_target / set_target
//   get_IsAllocated                -> icall_gchandle_is_allocated
//   AddrOfPinnedObject()           -> icall_gchandle_addr_of_pinned (intptr)
//   ToIntPtr/FromIntPtr            -> the int32-slot identity folds (both are
//                                     STATIC 1-arg in net48; the handle value
//                                     IS the int32 slot and an IntPtr rides the
//                                     same slot -- no runtime icall needed)
//   op_Equality/op_Inequality      -> il2cpp_intptr_op_equality/inequality
//                                     (the raw handle-value comparison, the
//                                     IntPtr domain precedent)
//
// PARAM BYTES: object params are 0x1C (OBJECT); GCHandleType is a boxed enum
// that RawSignature decodes as 0x11 (VALUETYPE). IsIntPtrByte accepts the
// native-int carriers for the IntPtr-typed surface.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapGCHandle(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Alloc":
                // GCHandle.Alloc(object) -> int32 handle (Normal), and
                // GCHandle.Alloc(object, GCHandleType) with the explicit type.
                if (count == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_gchandle_alloc_normal", 1, true, false, false, new[] { "" });
                if (count == 2 && ps[0] == 0x1C && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_gchandle_alloc", 2, true, false, false, new[] { "", "i" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): GCHandle.Alloc arity {count} -- net48 has only Alloc(object) and " +
                    "Alloc(object, GCHandleType) [Reference Source runtime_interopservices_gchandle.cs:91,97]");
            case "Free":
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_gchandle_free", 0, false, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            case "get_Target":
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_gchandle_get_target", 0, true, true, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            case "set_Target":
                if (count != 1 || ps[0] != 0x1C) return null;
                return I("il2cpp::runtime::icall_gchandle_set_target", 1, false, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "", "" });
            case "AddrOfPinnedObject":
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_gchandle_addr_of_pinned", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "" }, "ptr");
            case "get_IsAllocated":
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_gchandle_is_allocated", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            case "ToIntPtr":
                // GCHandle.ToIntPtr(GCHandle value): STATIC in net48 (verified
                // against the net48 reference assembly). The handle value IS
                // the int32 slot and an IntPtr rides the SAME slot (intptr.h),
                // so the result is the argument verbatim -- the identity fold.
                return I(ResolvedMethod.IdentityMarker, 1, true, false, false);
            case "InternalGetHandleType":
                if (count == 1 && IsIntPtrByte(ps[0]))
                    return I("il2cpp::runtime::icall_gchandle_get_handle_type", 1, true, false, false, new[] { "" });
                return null;
            case "FromIntPtr":
                // GCHandle.FromIntPtr(IntPtr): the reverse identity -- the
                // IntPtr value IS the handle (both ride the int32 slot). The
                // result is the argument (the identity fold on the first
                // parameter).
                return I(ResolvedMethod.IdentityMarker, 1, true, false, false);
            case "op_Equality":
            case "op_Inequality":
                // static bool op_Equality(GCHandle, GCHandle): the raw handle
                // value comparison (il2cpp_intptr_op_equality, the IntPtr
                // domain precedent -- both operands ride in int32 slots and
                // sign-extend to intptr_t on the call).
                if (count != 2 || ps[0] != 0x11 || ps[1] != 0x11) return null;
                return I(name == "op_Equality"
                        ? "il2cpp::runtime::il2cpp_intptr_op_equality"
                        : "il2cpp::runtime::il2cpp_intptr_op_inequality",
                    2, true, false, false, new[] { "", "" });
            default:
                // stacktrace-real message correction: this catch-all used to
                // claim "net48 has no such member". net48's GCHandle DOES
                // declare more members than the list below -- op_Explicit in
                // both directions, plus the Object overrides -- so the honest
                // reason is "not wired here yet", not "does not exist".
                throw new NotSupportedException(
                    $"System.Runtime.InteropServices.GCHandle::{name} is not wired in this runtime yet -- the wired " +
                    "surface covers Alloc/Free/Target/AddrOfPinnedObject/IsAllocated/ToIntPtr/FromIntPtr/op_Equality/" +
                    "op_Inequality (net48 also declares op_Explicit and the Object overrides) " +
                    "[Reference Source runtime_interopservices_gchandle.cs:91-264]");
        }
    }
}
