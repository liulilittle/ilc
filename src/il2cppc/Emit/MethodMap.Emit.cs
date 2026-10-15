namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Emit -- System.Reflection.Emit.ModuleBuilder token QCall mappings
// (runtime stubs il2cpp_qcall_emit_* in stubs.h -- dual independent token
// pools, MemberRef base 0x0A000001 / TypeSpec base 0x1B000001).
// Owners: System.Reflection.Emit.ModuleBuilder,
//   System.Security.Principal.Win32, System.Security.Policy.PEFileEvidenceFactory,
//   System.AppDomain, System.IO.IsolatedStorage.IsolatedStorageFile
//
// SHAPES (verified against net48 mscorlib via framework reflection --
// powershell 5.1, [Reflection.Emit.ModuleBuilder].GetMethods(60) for the
// ModuleBuilder rows; full-assembly scan for the batch-2 rows):
//   GetMemberRef(RuntimeModule, RuntimeModule, int, int)            -> 4 slots
//   GetMemberRefFromSignature(RuntimeModule, int, string, byte[], int) -> 5
//   GetMemberRefOfMethodInfo(RuntimeModule, int, IRuntimeMethodInfo) -> 3
//   GetMemberRefOfFieldInfo(RuntimeModule, int, RuntimeTypeHandle, int) -> 4
//   GetTokenFromTypeSpec(RuntimeModule, byte[], int)                -> 3
//   Win32.ImpersonateLoggedOnUser(SafeAccessTokenHandle)            -> 1 slot
//   PEFileEvidenceFactory.FireEvidenceGeneratedEvent(SafePEFileHandle, enum) -> 2
//   AppDomain.GetGrantSet(AppDomainHandle, ObjectHandleOnStack)     -> 2 slots
//   IsolatedStorageFile.GetUsage(SafeIsolatedStorageFileHandle)     -> 1 slot
// Each returns the next pool token as an opaque int32 (D10 monotonic).
//
// CAST RULE (reuses existing EmitIcallCallArgs arms only, no new codes):
//   string (0x0E)          -> "u16:System.String" (StackFold UTF-16
//                             pre-marshal; requires IsPInvoke=true, which these
//                             QCalls carry -- they are [DllImport("QCall")]
//                             native imports, same as MethodRefResolver's
//                             P/Invoke branch doctrine).
//   everything else        -> "i" (object/module slots flatten via the i-arm
//                             ref shape static_cast<int32_t>(
//                             reinterpret_cast<intptr_t>(obj)); ints verbatim).
//
// SCOPE (C7 minimal): only the five QCalls above are mapped; any other
// System.Reflection.Emit.ModuleBuilder member falls through to the
// generated-name path. GetMemberRefOfFieldInfo tkField rides the generic
// "i" arm (int32 verbatim) into the 4-slot stub, which returns the next
// MemberRef pool token without decoding it.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEmit(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Reflection.Emit.ModuleBuilder"
            && owner != "System.Security.Principal.Win32"
            && owner != "System.Security.Policy.PEFileEvidenceFactory"
            && owner != "System.AppDomain"
            && owner != "System.IO.IsolatedStorage.IsolatedStorageFile") return null;
        var cpp = (owner, name, count) switch
        {
            ("System.Reflection.Emit.ModuleBuilder", "GetMemberRef", 4) => "il2cpp::runtime::il2cpp_qcall_emit_get_member_ref",
            ("System.Reflection.Emit.ModuleBuilder", "GetMemberRefFromSignature", 5) => "il2cpp::runtime::il2cpp_qcall_emit_get_member_ref_from_signature",
            ("System.Reflection.Emit.ModuleBuilder", "GetMemberRefOfMethodInfo", 3) => "il2cpp::runtime::il2cpp_qcall_emit_get_member_ref_of_method_info",
            ("System.Reflection.Emit.ModuleBuilder", "GetMemberRefOfFieldInfo", 4) => "il2cpp::runtime::il2cpp_qcall_emit_get_member_ref_of_field_info",
            ("System.Reflection.Emit.ModuleBuilder", "GetTokenFromTypeSpec", 3) => "il2cpp::runtime::il2cpp_qcall_emit_get_token_from_type_spec",
            ("System.Security.Principal.Win32", "ImpersonateLoggedOnUser", 1) => "il2cpp::runtime::il2cpp_qcall_emit_impersonate_logged_on_user",
            // Zero-call QCall colliding with the 2-param SDK SetThreadToken:
            // throwing stub; the mapping suppresses the naked decl.
            ("System.Security.Principal.Win32", "SetThreadToken", 1) => "il2cpp::runtime::icall_win32_set_thread_token_unsupported",
            // GN-20 last net48 icall audit realGap (docs/audit/icall-inventory-
            // net48.md row 492): Win32.OpenThreadToken(TokenAccessLevels,
            // WinSecurityContext, out SafeAccessTokenHandle&) is the inner
            // layer of WindowsIdentity.GetCurrent()'s managed GetCurrentToken
            // chain. The SafeAccessTokenHandle OUT object cannot be
            // constructed in the ILC model (safehandle.h: "no SafeHandle
            // object model") and user code can never reach this internal
            // method (the AOT runtime substitutes the PUBLIC WindowsIdentity
            // surface -- see MethodMap.WindowsIdentity.cs/identity.h, which
            // carries the token as a raw intptr value-slotted handle-gap).
            // This arm closes the audit realGap with a net48-ABI-shaped stub
            // (int32 result, the real 3-param signature) that returns 0; the
            // W7 boundary is recorded in the p-identity GAPS.md.
            ("System.Security.Principal.Win32", "OpenThreadToken", 3) => "il2cpp::runtime::icall_security_principal_win32_open_thread_token",
            ("System.Security.Policy.PEFileEvidenceFactory", "FireEvidenceGeneratedEvent", 2) => "il2cpp::runtime::il2cpp_qcall_emit_fire_evidence_generated_event",
            ("System.AppDomain", "GetGrantSet", 2) => "il2cpp::runtime::il2cpp_qcall_emit_get_grant_set",
            ("System.IO.IsolatedStorage.IsolatedStorageFile", "GetUsage", 1) => "il2cpp::runtime::il2cpp_qcall_emit_get_usage",
            _ => null,
        };
        if (cpp == null) return null;
        if (ps.Length != count) return null;
        var casts = new string[count];
        for (var i = 0; i < count; i++)
            casts[i] = ps[i] == 0x0E ? "u16:System.String" : "i";
        // BCL truth (measured): FireEvidenceGeneratedEvent / GetGrantSet
        // return void (CallStmt, no push); the rest return int32/uint64.
        var returnsValue = name is not ("FireEvidenceGeneratedEvent" or "GetGrantSet" or "SetThreadToken");
        // GetUsage returns UInt64: ride the int64 carrier (bitwise identical;
        // the ulong store converts value-preserving).
        if (name == "GetUsage")
            return I(cpp, count, true, false, false, casts, "l") with { IsPInvoke = true };
        return I(cpp, count, returnsValue, false, false, casts) with { IsPInvoke = true };
    }
}
