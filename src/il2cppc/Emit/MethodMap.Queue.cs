namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Queue -- System.Collections.Queue + System.Collections.Generic.
// Queue`1 icall mappings (stackqueue.h / queueint_obj.h). Owners:
// System.Collections.Queue, System.Collections.Generic.Queue`1
//
// NAMESPACE: the stackqueue.h/.cpp icalls are declared in PLAIN namespace
// il2cpp (unlike most BCL icall families under il2cpp::runtime) -- the
// non-generic CppNames below must stay "il2cpp::il2cpp_queue_*". The G4
// Queue<int> wrappers (queueint_obj.h) live in il2cpp::runtime
// ("il2cpp::runtime::icall_queue_int_*").
//
// NON-GENERIC: elements are Il2CppObject* slots (the IL `box` opcode boxes
// value arguments before Enqueue; Dequeue/Peek results stay Il2CppObject*).
//
// G4 GENERIC Queue<int>: the int member rides the same object through the
// boxing/unboxing wrappers; ToArray materialises a real int32[] so the
// tracked System.Int32[] loads with ldelem.i4. instName is AUTHORITATIVE
// (the List/Dictionary/HashSet precedent); a null tag keeps the historical
// non-generic mapping.
// A4 virtual dispatch: GetEnumerator is the fully-instantiated generic arm (token computed
// uniformly); get_Current unknown tokens fall to the object shared arm. The
// int/long/double Enqueue/Dequeue/Peek/Contains/ToArray/get_Current fast paths
// are mechanically derived (box->shared->unbox, queueint_obj.cpp), kept because
// the ABI differs + there is no generic box emission; item by item see the
// round-trip specialization audit.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapQueue(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        // Null-tag deferral (RISK5 R4) applies to GENERIC instantiations
        // only: a null tag on the explicit non-generic owner keeps the
        // historical mapping -- newobj .ctor resolution carries no
        // receiver/instName by construction (StackFold.EmitNewObj resolves
        // for ParamCount only, CodeGenerator.ResolveByToken resolves with a
        // null hint), so deferring it emits an undefined
        // System_Collections_Queue__ctor_* symbol (613 mingw regression).
        var nonGeneric = owner is "System.Collections.Queue" || instTag is "System.Collections.Queue";
        if (instTag is null && !nonGeneric) return null;
        var intReceiver = instTag is "System.Collections.Generic.Queue`1_int";
        // T2 W4: typed value elements ride box/unbox forwarders (queueint_obj).
        var longReceiver = instTag is "System.Collections.Generic.Queue`1_long";
        var doubleReceiver = instTag is "System.Collections.Generic.Queue`1_double";
        // G4 W1: the reference-element instantiations (string/object/class)
        // share the Il2CppObject* slot ABI with the non-generic queue; their
        // members ride the same il2cpp_queue_* icalls (no runtime entry).
        var refReceiver = instTag is "System.Collections.Generic.Queue`1_string"
            or "System.Collections.Generic.Queue`1_object"
            or "System.Collections.Generic.Queue`1_class"
            || IsQueueDottedRefTag(instTag);

        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_queue_create", 0, true, true, true, new[] { "drop" });
                // net48 Queue(int capacity): the create icall takes the
                // capacity as its sole argument (the fold-supplied newobj temp
                // is dropped).
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_queue_create_with_capacity", 1, true, true, true, new[] { "drop", "i" });
                throw new NotSupportedException($"Queue ctor with {count} arg(s) not wired (only the parameterless and int-capacity shapes exist)");
            case "Enqueue":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_queue_int_enqueue", 1, false, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_queue_long_enqueue", 1, false, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_queue_double_enqueue", 1, false, false, true, new[] { "", "d" });
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_queue_enqueue", 1, false, false, true)
                    : null;
            case "Dequeue":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_queue_int_dequeue", 0, true, false, true, null, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_queue_long_dequeue", 0, true, false, true, null, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_queue_double_dequeue", 0, true, false, true, null, "d");
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_queue_dequeue", 0, true, true, true)
                    : null;
            case "Peek":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_queue_int_peek", 0, true, false, true, null, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_queue_long_peek", 0, true, false, true, null, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_queue_double_peek", 0, true, false, true, null, "d");
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_queue_peek", 0, true, true, true)
                    : null;
            case "Contains":
                if (intReceiver)
                    return I("il2cpp::runtime::icall_queue_int_contains", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_queue_long_contains", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_queue_double_contains", 1, true, false, true, new[] { "", "d" });
                return nonGeneric || refReceiver
                    ? I("il2cpp::il2cpp_queue_contains", 1, true, false, true, new[] { "", "" })
                    : null;
            case "get_Count":
                return I("il2cpp::il2cpp_queue_get_count", 0, true, false, true);
            case "Clear":
                return I("il2cpp::il2cpp_queue_clear", 0, false, false, true);
            case "ToArray":
                // G4: the int expansion materialises an int32[] (HEAD-first);
                // the non-generic domain keeps the object[] snapshot.
                // W1: the reference instantiations track the element array
                // (string[]/object[]); the `_class` form has NO recoverable
                // element type (the suffix collapses to "class") so it stays
                // LOUD (null -> phantom rejection) rather than mis-tracking.
                if (intReceiver)
                    return I("il2cpp::runtime::icall_queue_int_to_array", 0, true, true, true,
                        returnTypeName: "System.Int32[]");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_queue_long_to_array", 0, true, true, true,
                        returnTypeName: "System.Int64[]");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_queue_double_to_array", 0, true, true, true,
                        returnTypeName: "System.Double[]");
                if (refReceiver)
                {
                    var arrElemType = QueueArrayElementType(instTag);
                    return arrElemType == null
                        ? null
                        : I("il2cpp::il2cpp_queue_to_array", 0, true, true, true, returnTypeName: arrElemType);
                }
                return nonGeneric
                    ? I("il2cpp::il2cpp_queue_to_array", 0, true, true, true)
                    : null;
            case "GetEnumerator":
                // A4 virtual dispatch: a single fully-instantiated generic arm -- same snapshot
                // icall, token mechanically computed by the unified function, eliminating the
                // per-int/long/double/string/object/class/dotted arms (generic
                // no-duplicate-specialization rule). Unknown value-type instantiation token is
                // null -> LOUD (no generic box emission, must not slip into the object arm).
                var enumToken = QueueEnumeratorTrackedType(instTag);
                return enumToken == null
                    ? null
                    : I("il2cpp::il2cpp_queue_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: enumToken);
            default: return null;
        }
    }

    // W1: the ToArray element-array tracking for a reference-element Queue`1
    // instantiation. The `_class` form is deliberately null (LOUD): the
    // generic-instantiation suffix collapses every class element to the literal
    // "class" (GenericInstantiationName), so the concrete element type cannot
    // be recovered -- emitting System.Object[] would be a wrong element type
    // for a downstream typed consumer. string/object are exact (net48:
    // string[]/object[]).
    private static string? QueueArrayElementType(string? instTag) => instTag switch
    {
        "System.Collections.Generic.Queue`1_string" => "System.String[]",
        "System.Collections.Generic.Queue`1_object" => "System.Object[]",
        _ => IsQueueDottedRefTag(instTag) ? QueueDottedElemName(instTag!) + "[]" : null,
    };

    // W1: the enumerator receiver pseudo-type for a reference-element
    // instantiation (the List`1.Enumerator_<elem> precedent). The member
    // dispatch (TryMapQueueEnumerator) reads the suffix to pick the reference
    // get_Current; the runtime object is the shared non-generic snapshot
    // enumerator (stackqueue.h).
    // URI-FIX (diffuri001): a dotted class-element instantiation (Queue<Uri>
    // -> "Queue`1_System.Uri", the GenericInstantiationName CLASS-arg form)
    // rides the same Il2CppObject* slots; the remainder is namespace-qualified
    // (contains '.'), unlike the short primitive suffixes.
    private static bool IsQueueDottedRefTag(string? instTag)
    {
        const string prefix = "System.Collections.Generic.Queue`1_";
        return instTag != null
            && instTag.StartsWith(prefix, StringComparison.Ordinal)
            && instTag.Substring(prefix.Length).Contains('.');
    }

    private static string QueueDottedElemName(string instTag)
    {
        const string prefix = "System.Collections.Generic.Queue`1_";
        return instTag.Substring(prefix.Length);
    }

    private static string? QueueEnumeratorTrackedType(string? instTag) => instTag switch
    {
        // A4 virtual dispatch: non-generic + fully-known instantiation tokens are computed
        // uniformly (the generic arm's only token source).
        null or "System.Collections.Queue" => "System.Collections.Queue.Enumerator",
        "System.Collections.Generic.Queue`1_int" => "System.Collections.Generic.Queue`1.Enumerator_int",
        "System.Collections.Generic.Queue`1_string" => "System.Collections.Generic.Queue`1.Enumerator_string",
        "System.Collections.Generic.Queue`1_object" => "System.Collections.Generic.Queue`1.Enumerator_object",
        "System.Collections.Generic.Queue`1_class" => "System.Collections.Generic.Queue`1.Enumerator_class",
        "System.Collections.Generic.Queue`1_long" => "System.Collections.Generic.Queue`1.Enumerator_long",
        "System.Collections.Generic.Queue`1_double" => "System.Collections.Generic.Queue`1.Enumerator_double",
        _ => IsQueueDottedRefTag(instTag)
            ? "System.Collections.Generic.Queue`1.Enumerator_" + QueueDottedElemName(instTag!)
            : null,
    };

    // W1: the get_Current result tracking for a reference-element enumerator.
    // `_class` tracks System.Object (the concrete class name is not recoverable
    // from the suffix; a downstream `(T)o` is a normal reference castclass).
    private static string? QueueElemTypeName(string? enumeratorToken) => enumeratorToken switch
    {
        "System.Collections.Generic.Queue`1.Enumerator_string" => "System.String",
        "System.Collections.Generic.Queue`1.Enumerator_object" => "System.Object",
        "System.Collections.Generic.Queue`1.Enumerator_class" => "System.Object",
        _ => null,
    };

    //--------------------------------------------------------------------------
    // TryMapQueueEnumerator -- G4 B2 (b2b8): the Queue<int>+Enumerator STRUCT
    // members. The runtime object is the non-generic snapshot enumerator over
    // the boxed-int slots; get_Current unboxes, MoveNext/Reset/Dispose reuse
    // the shared enumerator surface (Dispose is the null-safe shared no-op).
    // W1: the reference-element instantiations track their own token
    // ("...Enumerator_<elem>"); get_Current then returns the Il2CppObject*
    // element directly (no unbox) instead of the int32 payload.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapQueueEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "MoveNext":
                return count == 0
                    ? I("il2cpp::il2cpp_queue_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                if (count != 0) return null;
                // value-type fast path (mechanically derived, see audit): snapshot shared,
                // only the unbox branches by kind.
                if (receiverType == "System.Collections.Generic.Queue`1.Enumerator_int")
                    return I("il2cpp::runtime::icall_queue_int_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (receiverType == "System.Collections.Generic.Queue`1.Enumerator_long")
                    return I("il2cpp::runtime::icall_queue_long_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (receiverType == "System.Collections.Generic.Queue`1.Enumerator_double")
                    return I("il2cpp::runtime::icall_queue_double_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                // URI-FIX: dotted-element enumerators pass the reference through
                // with the exact element type (Queue<Uri> Current -> System.Uri).
                if (receiverType != null
                    && receiverType.StartsWith("System.Collections.Generic.Queue`1.Enumerator_", StringComparison.Ordinal)
                    && QueueElemTypeName(receiverType) is null)
                {
                    const string eprefix = "System.Collections.Generic.Queue`1.Enumerator_";
                    var elem = receiverType.Substring(eprefix.Length);
                    if (elem.Contains('.'))
                        return I("il2cpp::il2cpp_queue_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                            returnTypeName: elem);
                }
                return QueueElemTypeName(receiverType) is { } refElem
                    ? I("il2cpp::il2cpp_queue_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: refElem)
                    // iron rule: unconstrained/unknown tokens fall to the object shared arm
                    // (no silent int fallback).
                    : I("il2cpp::il2cpp_queue_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.Object");
            case "Reset":
                return count == 0
                    ? I("il2cpp::il2cpp_queue_enumerator_reset", 0, false, false, true, new[] { "deref:r" })
                    : null;
            case "Dispose":
                return count == 0
                    ? I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true, new[] { "deref:r" })
                    : null;
            default: return null;
        }
    }
}
