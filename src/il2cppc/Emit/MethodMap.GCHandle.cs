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
//   ToIntPtr/FromIntPtr            -> LOUD (the report's icall_gchandle_to_
//                                     intptr/from_intptr do NOT exist in the
//                                     runtime -- §4.4 lists them as conditional
//                                     runtime additions; wired LOUD, never a
//                                     fabricated API)
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
                throw new NotSupportedException($"GCHandle.Alloc with {count} param(s) not wired (alloc expects object[, GCHandleType])");
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
                // GCHandle -> IntPtr would be the identity (the handle value
                // IS the int32); the report's icall_gchandle_to_intptr is a
                // conditional runtime addition that does NOT exist -- LOUD.
                throw new NotSupportedException(
                    "GCHandle.ToIntPtr not wired: the runtime has no icall_gchandle_to_intptr (gchandle-b23 report §4.4 lists it as a conditional addition)");
            case "FromIntPtr":
                throw new NotSupportedException(
                    "GCHandle.FromIntPtr not wired: the runtime has no icall_gchandle_from_intptr (gchandle-b23 report §4.4 lists it as a conditional addition)");
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
                throw new NotSupportedException(
                    $"System.Runtime.InteropServices.GCHandle::{name} not wired: the runtime surface covers " +
                    "Alloc/Free/Target/AddrOfPinnedObject/IsAllocated (il2cpp_gchandle.h)");
        }
    }
}
