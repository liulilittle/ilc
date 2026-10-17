namespace Il2Cpp.Transpiler.Emit;

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
                // ilistException: get_Message on a tracked Exception receiver
                // (e.g. an element read from List<Exception>). The message was
                // captured at ctor time into the runtime side table; read it
                // back through the runtime icall (miss -> empty string).
                if (receiverType == "System.Exception")
                    return I("il2cpp::runtime::icall_exception_get_message", 0, true, true, true, new[] { "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Exception.get_Message on receiver '{receiverType}' not wired: only catch variables (tracked as the exception entry, in scope with the DotNetException carrier __il2cpp_ex) carry a message in the M3 carrier model");
            case ".ctor":
                // ilistException: System.Exception::.ctor(string) -- the BCL
                // exception ctor is a no-op carrier in M3, but the message
                // must be captured into the runtime side table so
                // get_Message can answer it later. Map the string-ctor to
                // the runtime icall that does both (nop + capture).
                if (count == 1 && ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_exception_ctor_message", 1, false, false, true, new[] { "", "s" });
                // nb1 EXC-CHAIN (p-nb-exc RED fix): System.Exception::.ctor
                // (string, Exception) -- capture BOTH the message and the
                // INNER OBJECT into the runtime side table (previously the
                // 2-arg ctor folded to nothing and the message was silently
                // lost -- the exploration p-nextbatch diff [exc.list.msg]
                // expected=outer actual=). The 2-arg ps: string (0x0E) +
                // Exception CLASS (0x12). The inner arg passes through "".
                if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_exception_ctor_message_inner", 2, false, false, true, new[] { "", "s", "" });
                return null;
            // nb1 EXC-CHAIN: get_InnerException on a CONSTRUCTED-exception
            // receiver (tracked as "System.Exception") -- read the inner
            // object from the side table (null when the chain has none).
            // ReturnTypeName "System.Exception" keeps downstream
            // get_Message/GetBaseException folds resolving on the returned
            // object. The catch-variable carrier ("System.Type") has no
            // inner model -- best-effort null (the honest answer).
            case "get_InnerException":
                if (receiverType == "System.Exception")
                    return I("il2cpp::runtime::icall_exception_get_inner_exception", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Exception");
                if (receiverType == "System.Type")
                    return I("il2cpp::runtime::icall_exception_get_null_ref", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Exception");
                return null;
            // nb1 EXC-CHAIN: GetBaseException() -- the root of the inner
            // chain (the receiver itself when it has no inner; net48
            // semantics). Same receiver-shape handling as get_InnerException.
            case "GetBaseException":
                if (receiverType == "System.Exception")
                    return I("il2cpp::runtime::icall_exception_get_base_exception", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Exception");
                if (receiverType == "System.Type")
                    return I("il2cpp::runtime::icall_exception_get_null_ref", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Exception");
                return null;
            // nb1 EXC-CHAIN: constant-null property surface (net48 returns
            // null for a constructed, never-thrown exception). Source /
            // HelpLink / StackTrace are strings; TargetSite is a MethodBase
            // (still null). The receiver passes through "" (dropped by the
            // runtime icall -- icall_exception_get_null_ref).
            case "get_Source":
            case "get_HelpLink":
            case "get_StackTrace":
                return I("il2cpp::runtime::icall_exception_get_null_ref", 0, true, true, true,
                    new[] { "" }, returnTypeName: "System.String");
            case "get_TargetSite":
                return I("il2cpp::runtime::icall_exception_get_null_ref", 0, true, true, true,
                    new[] { "" }, returnTypeName: "System.Reflection.MethodBase");
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
