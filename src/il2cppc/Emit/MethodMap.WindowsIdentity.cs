namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.WindowsIdentity -- System.Security.Principal.WindowsIdentity
// icall mappings (src/runtime/include/internal/identity.h).
// Owner: System.Security.Principal.WindowsIdentity
//
// OBJECT GAP -- handle-gap design (the Process=PID precedent, MethodMap.
// Process.cs): identity.h has NO WindowsIdentity object model. The net48
// public surface is substituted at the operand boundary -- the AOT runtime
// never runs mscorlib's managed GetCurrentToken chain (whose inner step is the
// audited Win32.OpenThreadToken internal call, mapped separately in MethodMap.
// Emit.cs). The managed "WindowsIdentity value" is carried in an intptr_t
// VALUE slot as the TOKEN HANDLE:
//
//   GetCurrent()        -> il2cpp_security_identity_get_current_token
//                          (static, 0 args, token handle result, tracked type
//                          "System.Security.Principal.WindowsIdentity")
//   get_Token           -> "::identity::" (the identity IS its token: the
//                          IntPtr property pushes the receiver, same fold as
//                          Process.get_Id)
//   get_IsAuthenticated -> il2cpp_security_identity_is_authenticated
//                          (instance, receiver = the token handle)
//   get_Name            -> il2cpp_security_identity_get_name (instance,
//                          receiver = the token handle, managed string result)
//
// The receiver of the instance icalls is the intptr_t token value slot
// (cast "" pass-through -- the icall parameter IS intptr_t). No Il2CppObject*
// is involved. Shape/byte-exactness is pinned by the p-identity probe (non-
// zero token, authenticated, non-empty "<domain>\<account>" name).
//
// LOUD / unlisted: the remaining WindowsIdentity surface (IsGuest/IsSystem/
// IsAnonymous/AuthenticationType/User/Impersonate/RunImpersonated/ctors with
// a token) has no runtime model and returns null from this map -- the
// generated-name path then fails LOUD "not wired" (documented W7 boundary,
// p-identity GAPS.md). The ctor path is intentionally unmapped: constructing
// a WindowsIdentity from a foreign IntPtr token requires the SafeAccessToken-
// Handle model that does not exist (safehandle.h).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapWindowsIdentity(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Security.Principal.WindowsIdentity")
            return null;
        switch (name)
        {
            case "GetCurrent":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_security_identity_get_current_token", 0, true, false, false,
                        returnTypeName: "System.Security.Principal.WindowsIdentity");
                return null;
            case "get_Token":
                if (count == 0)
                    // The identity IS its token: IntPtr.Token folds to the
                    // receiver (the intptr_t handle value). A probe asserting
                    // shape calls Token.ToInt64() (mapped, MethodMap.IntPtr).
                    return I(ResolvedMethod.IdentityMarker, 0, true, false, true);
                return null;
            case "get_IsAuthenticated":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_security_identity_is_authenticated", 0, true, false, true, new[] { "" });
                return null;
            case "get_Name":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_security_identity_get_name", 0, true, true, true, new[] { "" });
                return null;
            default:
                return null;
        }
    }
}