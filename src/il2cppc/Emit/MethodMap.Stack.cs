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
// A4 virtual dispatch: GetEnumerator is the fully-instantiated generic arm (isomorphic to
// the Queue side); get_Current unknown tokens fall to the object shared arm. The
// int/long/double fast paths are mechanically derived (stackint_obj), kept
// because the ABI differs + there is no generic box emission; item by item see
// the round-trip specialization audit.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStack(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        // Null-tag deferral (RISK5 R5) applies to GENERIC instantiations
        // only: a null tag on the explicit non-generic owner keeps the
        // historical mapping -- newobj .ctor resolution carries no
        // receiver/instName by construction (StackFold.EmitNewObj resolves
        // for ParamCount only, CodeGenerator.ResolveByToken resolves with a
        // null hint), so deferring it emits an undefined
        // System_Collections_Stack__ctor_* symbol (613 mingw regression).
        var nonGeneric = owner is "System.Collections.Stack" || instTag is "System.Collections.Stack";
        if (instTag is null && !nonGeneric) return null;
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

        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_stack_create", 0, true, true, true, new[] { "drop" });
                // net48 Stack(int capacity): the create icall takes the
                // capacity as its sole argument (the fold-supplied newobj temp
                // is dropped).
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_stack_create_with_capacity", 1, true, true, true, new[] { "drop", "i" });
                throw new NotSupportedException($"Stack ctor with {count} arg(s) not wired (only the parameterless and int-capacity shapes exist)");
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
                // A4 virtual dispatch: a single fully-instantiated generic arm (isomorphic to
                // the Queue side; generic no-duplicate-specialization rule).
                // Unknown value-type instantiation token is null -> LOUD.
                var enumToken = StackEnumeratorTrackedType(instTag);
                return enumToken == null
                    ? null
                    : I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: enumToken);
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
        // A4 virtual dispatch: non-generic + fully-known instantiation tokens are computed
        // uniformly (the generic arm's only token source).
        null or "System.Collections.Stack" => "System.Collections.Stack.Enumerator",
        "System.Collections.Generic.Stack`1_int" => "System.Collections.Generic.Stack`1.Enumerator_int",
        "System.Collections.Generic.Stack`1_long" => "System.Collections.Generic.Stack`1.Enumerator_long",
        "System.Collections.Generic.Stack`1_double" => "System.Collections.Generic.Stack`1.Enumerator_double",
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
                    // iron rule: unconstrained/unknown tokens fall to the object shared arm
                    // (no accidental string tracking).
                    : I("il2cpp::il2cpp_stack_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.Object");
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
