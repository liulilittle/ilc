namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.SafeHandle -- System.Runtime.InteropServices.SafeHandle icall
// mappings (src/runtime/include/internal/safehandle.h -- il2cpp::runtime::
// icall_safehandle_*). Owners: System.Runtime.InteropServices.SafeHandle
//
// net48 semantics (audit icall-inventory-net48.md section 5 item 10): handle
// reference counting around P/Invoke. The runtime has NO SafeHandle object
// model (no class descriptor, no BclAllocExpr allocation path -- a SafeHandle
// cannot be constructed end-to-end in ILC), so the icalls implement the
// MINIMAL reference-counting semantics in a flat side table keyed by the
// receiver object address (see safehandle.h for the full contract):
//
//   DangerousAddRef(ref bool success) -> icall_safehandle_dangerous_add_ref
//     (Il2CppObject* self, int32_t* success). On success success = true and
//     the reference count is incremented; a closed handle throws
//     ObjectDisposedException BEFORE success is written (the caller's slot
//     stays false). The `ref bool` slot crosses as int32_t* ("addr:i" -- the
//     bool's int32 slot ABI), the receiver as Il2CppObject* ("" passthrough).
//   DangerousRelease()                -> icall_safehandle_dangerous_release
//     (Il2CppObject* self). Decrements; an unbalanced release (ref count 0)
//     throws ObjectDisposedException.
//   SetHandleAsInvalid()              -> icall_safehandle_set_handle_as_invalid
//     (Il2CppObject* self). Marks the handle closed (idempotent); the ref
//     count is unchanged (net48: only the closed bit is set).
//
// Calls on derived handle types (Microsoft.Win32.SafeHandles.SafeFileHandle
// etc.) route here because csc declares inherited members on the BASE type
// (the xmlcase precedent). Unmapped members (DangerousGetHandle, Dispose,
// ctors, ...) fall to the generated-name path (loud link error).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapSafeHandle(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "DangerousAddRef":
                // (ref bool success): one explicit param; RawSignature consumes
                // the BYREF marker so ps carries the element byte (0x02 = bool).
                if (count == 1 && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_safehandle_dangerous_add_ref", 1, false, false, true, new[] { "", "addr:i" });
                throw new NotSupportedException($"SafeHandle.DangerousAddRef with {count} param(s) not wired: the icall takes (ref bool success)");
            case "DangerousRelease":
                if (count == 0)
                    return I("il2cpp::runtime::icall_safehandle_dangerous_release", 0, false, false, true);
                return null;
            case "SetHandleAsInvalid":
                if (count == 0)
                    return I("il2cpp::runtime::icall_safehandle_set_handle_as_invalid", 0, false, false, true);
                return null;
            default: return null;
        }
    }
}
