namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Stack -- System.Collections.Stack + System.Collections.Generic.
// Stack`1 icall mappings (stackqueue.h). Owners: System.Collections.Stack,
// System.Collections.Generic.Stack`1
//
// NAMESPACE: the stackqueue.h/.cpp icalls live in PLAIN namespace il2cpp.
//
// NON-GENERIC: elements are Il2CppObject* slots (box before Push; Pop/Peek
// stay Il2CppObject*).
//
// G4 GENERIC Stack<string>: the string member rides the same reference slots
// with no boxing; Contains scans with value equality (ordinal strings);
// ToArray is tracked as System.String[] (the snapshot is a reference array).
// instName is AUTHORITATIVE (the List/Dictionary/HashSet precedent); a null
// tag keeps the historical non-generic mapping.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStack(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        var stringReceiver = instTag is "System.Collections.Generic.Stack`1_string";
        // G4 W1: the other reference-element instantiations (object/class)
        // share the Il2CppObject* slot ABI with the non-generic stack (and with
        // the wired Stack<string>); their members ride the same il2cpp_stack_*
        // icalls (no runtime entry).
        var refReceiver = stringReceiver
            || instTag is "System.Collections.Generic.Stack`1_object" or "System.Collections.Generic.Stack`1_class";
        // T2 W4: typed value elements ride box/unbox forwarders (stackint_obj).
        var intReceiver = instTag is "System.Collections.Generic.Stack`1_int";
        var longReceiver = instTag is "System.Collections.Generic.Stack`1_long";
        var doubleReceiver = instTag is "System.Collections.Generic.Stack`1_double";
        var nonGeneric = instTag is null or "System.Collections.Stack";

        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_stack_create", 0, true, true, true, new[] { "drop" });
                throw new NotSupportedException($"Stack ctor with {count} arg(s) not wired (the runtime has only the parameterless create icall)");
            case "Push":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_stack_int_push", 1, false, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_stack_long_push", 1, false, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_stack_double_push", 1, false, false, true, new[] { "", "d" });
                return nonGeneric
                    ? I("il2cpp::il2cpp_stack_push", 1, false, false, true)
                    : refReceiver
                        ? I("il2cpp::il2cpp_stack_push", 1, false, false, true, new[] { "", "" })
                        : null;
            case "Pop":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_stack_int_pop", 0, true, false, true, null, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_stack_long_pop", 0, true, false, true, null, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_stack_double_pop", 0, true, false, true, null, "d");
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_stack_pop", 0, true, true, true)
                    : null;
            case "Peek":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_stack_int_peek", 0, true, false, true, null, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_stack_long_peek", 0, true, false, true, null, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_stack_double_peek", 0, true, false, true, null, "d");
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_stack_peek", 0, true, true, true)
                    : null;
            case "Contains":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_stack_int_contains", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_stack_long_contains", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_stack_double_contains", 1, true, false, true, new[] { "", "d" });
                return refReceiver || nonGeneric
                    ? I("il2cpp::il2cpp_stack_contains", 1, true, false, true, new[] { "", "" })
                    : null;
            case "get_Count":
                return I("il2cpp::il2cpp_stack_get_count", 0, true, false, true);
            case "Clear":
                return I("il2cpp::il2cpp_stack_clear", 0, false, false, true);
            case "ToArray":
                // W1: reference instantiations track the element array
                // (string[]/object[]); the `_class` form has NO recoverable
                // element type (the suffix collapses to "class") so it stays
                // LOUD rather than mis-tracking.
                if (intReceiver)
                    return I("il2cpp::runtime::icall_stack_int_to_array", 0, true, true, true,
                        returnTypeName: "System.Int32[]");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_stack_long_to_array", 0, true, true, true,
                        returnTypeName: "System.Int64[]");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_stack_double_to_array", 0, true, true, true,
                        returnTypeName: "System.Double[]");
                if (refReceiver)
                {
                    var arrElemType = StackArrayElementType(instTag);
                    return arrElemType == null
                        ? null
                        : I("il2cpp::il2cpp_stack_to_array", 0, true, true, true, returnTypeName: arrElemType);
                }
                return nonGeneric
                    ? I("il2cpp::il2cpp_stack_to_array", 0, true, true, true)
                    : null;
            case "GetEnumerator":
                if (intReceiver)
                    return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Stack`1.Enumerator_int");
                if (longReceiver)
                    return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Stack`1.Enumerator_long");
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Stack`1.Enumerator_double");
                return refReceiver
                    ? I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: StackEnumeratorTrackedType(instTag))
                    : nonGeneric
                        ? I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Stack.Enumerator")
                        : null;
            default: return null;
        }
    }

    // W1: the ToArray element-array tracking for a reference-element Stack`1
    // instantiation. `_class` is deliberately null (LOUD): the suffix collapses
    // every class element to "class", so the concrete element type is not
    // recoverable. string/object are exact (net48: string[]/object[]).
    private static string? StackArrayElementType(string? instTag) => instTag switch
    {
        "System.Collections.Generic.Stack`1_string" => "System.String[]",
        "System.Collections.Generic.Stack`1_object" => "System.Object[]",
        _ => null,
    };

    // W1: the enumerator receiver pseudo-type for a reference-element
    // instantiation (the List`1.Enumerator_<elem> precedent). The runtime
    // object is the shared non-generic snapshot enumerator (stackqueue.h).
    private static string? StackEnumeratorTrackedType(string? instTag) => instTag switch
    {
        "System.Collections.Generic.Stack`1_string" => "System.Collections.Generic.Stack`1.Enumerator_string",
        "System.Collections.Generic.Stack`1_object" => "System.Collections.Generic.Stack`1.Enumerator_object",
        "System.Collections.Generic.Stack`1_class" => "System.Collections.Generic.Stack`1.Enumerator_class",
        _ => null,
    };

    // W1: the get_Current result tracking for a reference-element enumerator.
    // `_class` tracks System.Object (the concrete class name is not recoverable
    // from the suffix; a downstream `(T)o` is a normal reference castclass).
    private static string? StackElemTypeName(string? enumeratorToken) => enumeratorToken switch
    {
        "System.Collections.Generic.Stack`1.Enumerator_string" => "System.String",
        "System.Collections.Generic.Stack`1.Enumerator_object" => "System.Object",
        "System.Collections.Generic.Stack`1.Enumerator_class" => "System.Object",
        _ => null,
    };

    //--------------------------------------------------------------------------
    // TryMapStackEnumerator -- G4 B2 (b2b8): the Stack<string>+Enumerator
    // STRUCT members over the non-generic snapshot enumerator (get_Current
    // passes the string reference through; Dispose is the shared no-op).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapStackEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "MoveNext":
                return count == 0
                    ? I("il2cpp::il2cpp_stack_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                if (count != 0) return null;
                if (receiverType == "System.Collections.Generic.Stack`1.Enumerator_int")
                    return I("il2cpp::runtime::icall_stack_int_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (receiverType == "System.Collections.Generic.Stack`1.Enumerator_long")
                    return I("il2cpp::runtime::icall_stack_long_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (receiverType == "System.Collections.Generic.Stack`1.Enumerator_double")
                    return I("il2cpp::runtime::icall_stack_double_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                return StackElemTypeName(receiverType) is { } refElem
                    ? I("il2cpp::il2cpp_stack_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: refElem)
                    : I("il2cpp::il2cpp_stack_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.String");
            case "Reset":
                return count == 0
                    ? I("il2cpp::il2cpp_stack_enumerator_reset", 0, false, false, true, new[] { "deref:r" })
                    : null;
            case "Dispose":
                return count == 0
                    ? I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true, new[] { "deref:r" })
                    : null;
            default: return null;
        }
    }
}
