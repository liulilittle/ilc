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
    //--------------------------------------------------------------------------
    // excmsg: the Argument*Exception ctor SHAPE table (net48, verified against
    // the reference runtime -- see the notes in StackFold/EmitThrowCall).
    //
    // The overloads share parameter-type BYTES (string vs Exception), so the
    // decoded ParamTypes text is the only reliable discriminator:
    //   ArgumentException                (msg) | (msg,inner) | (msg,param) | (msg,param,inner)
    //   ArgumentNullException            (param) | (msg,inner) | (param,msg)     [1 string = PARAM]
    //   ArgumentOutOfRangeException      (param) | (msg,inner) | (param,msg) | (param,actual,msg)
    // A 2-arg (string, Exception) form is ALWAYS (message, innerException) --
    // net48 has no (paramName, Exception) ctor, so ParamName stays null.
    //
    // Returns the IL argument index of each semantic slot (-1 = "no argument":
    // the runtime then substitutes the default/generic text, or omits the line).
    // kind: 0 ArgumentException / 1 ArgumentNullException / 2 AOOR.
    //--------------------------------------------------------------------------
    public static bool IsArgExceptionType(string typeName) =>
        typeName is "System.ArgumentException" or "System.ArgumentNullException"
            or "System.ArgumentOutOfRangeException";

    // Any receiver whose managed type is a System.Exception subclass -- these
    // are the CONSTRUCTED-instance shapes backed by the runtime side table
    // (message / paramName / inner keyed by object pointer).
    public static bool IsExceptionReceiverType(string? typeName) => typeName is
        "System.Exception" or "System.SystemException" or "System.ArgumentException"
        or "System.ArgumentNullException" or "System.ArgumentOutOfRangeException";

    public static int ArgExceptionKind(string typeName) => typeName switch
    {
        "System.ArgumentNullException" => 1,
        "System.ArgumentOutOfRangeException" => 2,
        _ => 0,
    };

    // 'paramTypes' is the MethodSignatureCoder encoding ("r:System.String,...").
    public static bool TryArgExceptionCtorShape(string typeName, string? paramTypes,
        out int kind, out int messageArg, out int paramArg, out int innerArg, out int actualArg, out bool hasText)
    {
        kind = ArgExceptionKind(typeName);
        messageArg = -1; paramArg = -1; innerArg = -1; actualArg = -1; hasText = false;
        if (!IsArgExceptionType(typeName)) return false;
        var ps = string.IsNullOrEmpty(paramTypes) ? Array.Empty<string>() : paramTypes.Split(',');
        bool IsStr(int i) => i < ps.Length && ps[i] == "r:System.String";
        bool IsEx(int i) => i < ps.Length && ps[i] == "r:System.Exception";
        bool IsObj(int i) => i < ps.Length && (ps[i] == "r:System.Object" || ps[i].StartsWith("r:", StringComparison.Ordinal));
        switch (ps.Length)
        {
            case 0:
                return true;
            case 1 when IsStr(0):
                // ArgumentException / ArgumentNullException / AOOR single string:
                // ArgumentException treats it as the MESSAGE; the other two as the
                // PARAMETER NAME (their 1-arg ctor is (paramName)).
                if (kind == 0) { messageArg = 0; hasText = true; }
                else { paramArg = 0; }
                return true;
            case 2 when IsStr(0) && IsEx(1):
                messageArg = 0; innerArg = 1; hasText = true;
                return true;
            case 2 when kind == 0 && IsStr(0) && IsStr(1):
                messageArg = 0; paramArg = 1; hasText = true;
                return true;
            case 2 when kind != 0 && IsStr(0) && IsStr(1):
                paramArg = 0; messageArg = 1; hasText = true;
                return true;
            case 3 when kind == 0 && IsStr(0) && IsStr(1) && IsEx(2):
                messageArg = 0; paramArg = 1; innerArg = 2; hasText = true;
                return true;
            case 3 when kind == 2 && IsStr(0) && IsObj(1) && IsStr(2):
                paramArg = 0; actualArg = 1; messageArg = 2; hasText = true;
                return true;
            case 4 when kind == 2 && IsStr(0) && IsObj(1) && IsStr(2) && IsEx(3):
                paramArg = 0; actualArg = 1; messageArg = 2; innerArg = 3; hasText = true;
                return true;
            default:
                return false;
        }
    }

    private static ResolvedMethod? TryMapException(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? paramTypes)
    {
        switch (name)
        {
            case "GetType":
                // M5 wiring: GetType() on a catch-variable entry (tracked as
                // "System.Type" -- the entry IS the exception type-name
                // string) is an identity -- the fold pushes the receiver.
                if (receiverType == "System.Type")
                    return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true);
                // excmsg: GetType() on a CONSTRUCTED exception instance -- the
                // runtime descriptor stamped at allocation answers the real
                // type name (net48 GetType().Name == the concrete class).
                // A hint-less first pass DEFERS (null) so the receiver-aware
                // re-resolve below selects this arm.
                if (receiverType != null)
                    return I("il2cpp::runtime::icall_object_get_type", 0, true, true, true,
                        returnTypeName: "System.Type");
                return null;
            case "get_Message":
                // The callvirt FIRST resolution (StackFold.cs:1365) has no
                // receiver hint -- defer (null) so the receiver-aware
                // re-resolve below dispatches the catch-variable shape.
                if (receiverType == null) return null;
                // The catch-variable CARRIER is the exception type-name string
                // (in scope with the DotNetException __il2cpp_ex).
                if (receiverType == "System.Type")
                    return I("il2cpp::il2cpp_string_intern_literal(__il2cpp_ex.message_text()",
                        0, true, true, true, new[] { "drop" }, returnTypeName: "System.String");
                // ilistException/excmsg: get_Message on a CONSTRUCTED exception
                // receiver (a tracked Exception / Argument* instance, e.g. an
                // element read from List<Exception> or `new ArgumentException`).
                // The message was composed and captured at ctor time into the
                // runtime side table; read it back through the runtime icall.
                if (IsExceptionReceiverType(receiverType))
                    return I("il2cpp::runtime::icall_exception_get_message", 0, true, true, true, new[] { "" }, returnTypeName: "System.String");
                throw new NotSupportedException(
                    $"LOUD whitelist (no runtime descriptor/model): Exception.get_Message on receiver '{receiverType}' -- only catch " +
                    "variables (tracked as the exception entry, in scope with the DotNetException carrier __il2cpp_ex) and the " +
                    "modelled exception hierarchy (Exception/SystemException/Argument*) carry a message in the M3 carrier model " +
                    "[Reference Source: net48 mscorlib System.Exception.get_Message]");
            case ".ctor":
                // excmsg: the Argument*Exception ctors capture the net48
                // Message/ParamName semantics into the runtime side table so a
                // CONSTRUCTED instance answers .Message / .ParamName exactly
                // like the reference runtime (no thread-local involved).
                // Slots: (self, message, paramName, inner, actual, kind, hasText);
                // ArgOrder maps each slot to the ctor's IL argument and the two
                // trailing literals carry the type kind + the has-message flag.
                if (IsArgExceptionType(owner) && TryArgExceptionCtorShape(owner, paramTypes, out var kind,
                        out var msgArg, out var paramArg, out var innerArg, out var actualArg, out var hasText))
                {
                    // The ctor call carries the receiver at IL slot 0, so every
                    // semantic slot shifts by one (-1 = "no argument").
                    static int Shift(int i) => i < 0 ? -1 : i + 1;
                    return new ResolvedMethod("il2cpp::runtime::icall_exception_ctor_arg", count, false, false, false,
                        Enumerable.Repeat(false, count).ToArray(), true,
                        ParamTypeNames: new[] { "", "s", "s", "", "" },
                        ArgOrder: new[] { 0, Shift(msgArg), Shift(paramArg), Shift(innerArg), Shift(actualArg) },
                        AppendLiterals: new[] { kind.ToString(), hasText ? "1" : "0" });
                }
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
            case "get_ParamName":
                // excmsg: the INSTANCE value stored by the ctor. A receiver
                // without a side-table entry (the catch CARRIER -- the
                // type-name string) falls back to the throw-time snapshot
                // inside the runtime icall, so every receiver shape answers
                // without a silent wrong value.
                return I("il2cpp::runtime::icall_exception_get_param_name", 0, true, true, true, new[] { "" }, returnTypeName: "System.String");
            // excapi1 (2026-10-24): Exception.HResult -- the net48 default
            // _HResult stored per constructed instance (the runtime side table
            // entry set by the ctor icalls; COR_E_EXCEPTION 0x80131500 for the
            // base, COR_E_ARGUMENT 0x80070057 for ArgumentException). A receiver
            // with no entry (the catch carrier / a ctor-noop instance) falls
            // back to the base default inside the runtime icall.
            case "get_HResult":
                return I("il2cpp::runtime::icall_exception_get_hresult", 0, true, false, true, new[] { "" });
            // excapi1 (2026-10-24): Exception.Data -- the per-exception
            // IDictionary store (a Hashtable object created/cached by the
            // runtime icall). The declared return type keeps the indexer
            // (System.Collections.IDictionary.this[object]) resolving on the
            // returned object.
            case "get_Data":
                return I("il2cpp::runtime::icall_exception_get_data", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.IDictionary");
            case "get_InnerException":
                if (IsExceptionReceiverType(receiverType))
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
                if (IsExceptionReceiverType(receiverType))
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
                // excapi1 (2026-10-24): Exception.ToString() on a CONSTRUCTED
                // exception -- the runtime icall renders "<TypeName>: <Message>"
                // (the composed message / the type-based default text). The
                // catch-variable carrier ("System.Type") keeps the vtable path
                // (loud downstream failure) as before.
                if (IsExceptionReceiverType(receiverType))
                    return I("il2cpp::runtime::icall_exception_to_string", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.String");
                return null;
            // P6: Exception internal helpers (stubs).
            case "SaveStackTracesFromDeepCopy": return I("il2cpp::runtime::icall_exception_save_stack_traces_from_deep_copy", 0, false, false, true);
            case "GetExceptionCount": return I("il2cpp::runtime::icall_exception_get_exception_count", 0, true, false, false);
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // excapi1: System.Collections.IDictionary (System.Exception.Data). The
    // runtime's per-exception store is a Hashtable object, so the interface's
    // surface maps to the Hashtable icalls over that receiver:
    //   get_Count       -> icall_hashtable_get_count
    //   get_Item(object)-> icall_hashtable_get   (returns the boxed value)
    //   set_Item(object)-> icall_hashtable_set   (stores the boxed value)
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapExceptionData(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_Count":
                return I("il2cpp::runtime::icall_hashtable_get_count", 0, true, false, true, new[] { "" });
            case "get_Item":
                if (ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_hashtable_get", 1, true, true, true, new[] { "", "" });
                return null;
            case "set_Item":
                if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C)
                    return I("il2cpp::runtime::icall_hashtable_set", 2, false, false, true, new[] { "", "", "" });
                return null;
            default:
                return null;
        }
    }
}
