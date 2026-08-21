namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Exception -- System.Exception -- the callvirt surface inherited
// from System.Object (GetType) and the get_Message carrier read.
// Owners: System.Exception
//
// MESSAGE CARRIER (excext, 2026-08-07): the M3 exception transport
// (il2cpp_exception.h) DOES carry the message -- DotNetException holds
// { m_typeName, m_message } with message_text() accessor. A catch-variable
// receiver (the fold tracks the catch entry as "System.Type") therefore maps
// get_Message to the carrier read: the generated catch parameter __il2cpp_ex
// is in scope wherever the catch variable is, so the mapping emits
//   il2cpp::il2cpp_string_intern_literal(__il2cpp_ex.message_text())
// with the receiver DROPPED (the same intern-literal expression the
// ExceptionEntryExpr emission uses for the type name; the nested call is
// expressed in the CppName -- the op_Inequality prefix precedent). A NON-catch
// receiver (a plain `new Exception(...)` object -- the BCL exception ctor is
// a no-op carrier in M3) has no reachable message -> LOUD.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapException(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "GetType":
                // M5 wiring: GetType() on a catch-variable entry (tracked as
                // "System.Type" -- the entry IS the exception type-name
                // string) is an identity -- the fold pushes the receiver.
                // Any OTHER receiver falls through to the vtable path.
                return receiverType == "System.Type"
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true)
                    : null;
            case "get_Message":
                // The callvirt FIRST resolution (StackFold.cs:1365) has no
                // receiver hint -- defer (null) so the receiver-aware
                // re-resolve below dispatches the catch-variable shape.
                if (receiverType == null) return null;
                if (receiverType == "System.Type")
                    return I("il2cpp::il2cpp_string_intern_literal(__il2cpp_ex.message_text()",
                        0, true, true, true, new[] { "drop" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Exception.get_Message on receiver '{receiverType}' not wired: only catch variables (tracked as the exception entry, in scope with the DotNetException carrier __il2cpp_ex) carry a message in the M3 carrier model");
            case "ToString":
                // Exception.ToString() on the carrier cannot render; fall
                // through to the vtable path (loud downstream failure).
                return null;
            // P6: Exception internal helpers (stubs).
            case "SaveStackTracesFromDeepCopy": return I("il2cpp::runtime::icall_exception_save_stack_traces_from_deep_copy", 0, false, false, true);
            case "GetExceptionCount": return I("il2cpp::runtime::icall_exception_get_exception_count", 0, true, false, false);
            default:
                return null;
        }
    }
}
