namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Collections -- System.Collections INTERFACE member dispatch
// (D-SL-3, sldeep). Owners: System.Collections.ICollection / IEnumerable /
// IEnumerator -- the interface surface of the non-generic collections.
//
// The ONLY concrete runtime objects reachable through these interfaces in the
// wired surface are the SortedList Keys / Values SNAPSHOT arrays (sortedlist.h
// -- get_Keys / get_Values return reference arrays whose C# compile-time type
// is ICollection). The resolver therefore dispatches on the RECEIVER's tracked
// pseudo-type (set by MethodMap.SortedList.cs get_Keys/get_Values
// ReturnTypeName, and by the get_enumerator result below):
//
//   receiver == "System.Collections.SortedList.KeyList" / ".ValueList"
//     ICollection::get_Count    -> il2cpp_sortedlist_snapshot_get_count
//     IEnumerable::GetEnumerator-> il2cpp_sortedlist_snapshot_get_enumerator
//   receiver == "System.Collections.SortedList.Enumerator"
//     IEnumerator::MoveNext     -> il2cpp_sortedlist_enumerator_move_next
//     IEnumerator::get_Current  -> il2cpp_sortedlist_enumerator_get_current
//     IEnumerator::Reset        -> il2cpp_sortedlist_enumerator_reset
//
// C42 sqenum-b23 (Stack/Queue foreach): the Stack / Queue containers also
// implement IEnumerable with snapshot enumerators (stackqueue.h -- the frozen
// items array + count/head at GetEnumerator time):
//
//   receiver == "System.Collections.Stack"
//     IEnumerable::GetEnumerator -> il2cpp_stack_get_enumerator
//   receiver == "System.Collections.Stack.Enumerator"
//     IEnumerator::MoveNext     -> il2cpp_stack_enumerator_move_next
//     IEnumerator::get_Current  -> il2cpp_stack_enumerator_get_current
//     IEnumerator::Reset        -> il2cpp_stack_enumerator_reset
//   (the Queue arms mirror with il2cpp_queue_* and the
//    "System.Collections.Queue.Enumerator" pseudo-type)
//
// All icalls are INSTANCE (receiver rides in arg[0] as a managed reference,
// "" passthrough -- the runtime casts internally). MoveNext returns int32
// (bool carrier); get_Current returns Il2CppObject* (the key/value element);
// get_Count returns int32 (the snapshot length == list count at snapshot
// time). The FIRST resolution pass has receiverType==null (the fold defers
// the receiver-aware re-resolve -- the TextReader/IDisposable precedent), so
// every arm requires a KNOWN tracked receiver pseudo-type; any other receiver
// falls through to the generated-name path (loud).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCollectionInterface(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Collections.ICollection":                // ICollection.get_Count on a Keys / Values snapshot: the
                // snapshot array length (see sortedlist.cpp).
                if (name == "get_Count" && count == 0
                    && receiverType is "System.Collections.SortedList.KeyList" or "System.Collections.SortedList.ValueList")
                    return I("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_count", 0, true, false, true, new[] { "" });
                return null;
            case "System.Collections.IEnumerable":
                // IEnumerable.GetEnumerator on a Keys / Values snapshot: wrap
                // the array in a snapshot enumerator (ReturnTypeName tracks the
                // enumerator for the IEnumerator member dispatch below).
                if (name == "GetEnumerator" && count == 0
                    && receiverType is "System.Collections.SortedList.KeyList" or "System.Collections.SortedList.ValueList")
                    return I("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.SortedList.Enumerator");
                // C42 sqenum-b23: the Stack / Queue containers (foreach).
                if (name == "GetEnumerator" && count == 0)
                {
                    if (receiverType == "System.Collections.Stack")
                        return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Stack.Enumerator");
                    if (receiverType == "System.Collections.Queue")
                        return I("il2cpp::il2cpp_queue_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Queue.Enumerator");
                }
                return null;
            case "System.Collections.IEnumerator":
                // IEnumerator members on the snapshot enumerator object.
                if (receiverType == "System.Collections.SortedList.Enumerator")
                {
                    switch (name)
                    {
                        case "MoveNext":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", 0, true, false, true, new[] { "" });
                            return null;
                        case "get_Current":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current", 0, true, true, true, new[] { "" });
                            return null;
                        case "Reset":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", 0, false, false, true, new[] { "" });
                            return null;
                        default:
                            return null;
                    }
                }
                // C42 sqenum-b23: the Stack / Queue enumerator objects.
                if (receiverType == "System.Collections.Stack.Enumerator" || receiverType == "System.Collections.Queue.Enumerator")
                {
                    bool isStack = receiverType == "System.Collections.Stack.Enumerator";
                    switch (name)
                    {
                        case "MoveNext":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_move_next" : "il2cpp::il2cpp_queue_enumerator_move_next",
                                    0, true, false, true, new[] { "" });
                            return null;
                        case "get_Current":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_get_current" : "il2cpp::il2cpp_queue_enumerator_get_current",
                                    0, true, true, true, new[] { "" });
                            return null;
                        case "Reset":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_reset" : "il2cpp::il2cpp_queue_enumerator_reset",
                                    0, false, false, true, new[] { "" });
                            return null;
                        default:
                            return null;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapComparer -- D-ICMP-2 (icomparedeep): System.Collections.Comparer,
    // the non-generic DEFAULT comparer. The static get_Default / get_Default
    // Invariant map to the runtime singleton getter (arraysort.h
    // il2cpp_comparer_default); its result is tracked under the pseudo-type
    // "System.Collections.Comparer" (ReturnTypeName) so the INSTANCE
    // Compare(object, object) callvirt resolves on the receiver hint. Compare
    // dispatches on the runtime argument types (null-first, string ordinal,
    // numeric/char/bool boxes, user IComparable fallback -- net48 Comparer
    // semantics).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapComparer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_Default":
            case "get_DefaultInvariant":
                if (count == 0)
                    return I("il2cpp::il2cpp_comparer_default", 0, true, true, false,
                        returnTypeName: "System.Collections.Comparer");
                return null;
            case "Compare":
                // instance int Compare(object, object): the receiver rides in
                // arg[0] ("" passthrough), the two compared objects follow.
                if (count == 2)
                    return I("il2cpp::il2cpp_comparer_compare", 2, true, false, true, new[] { "", "", "" });
                return null;
            default:
                return null;
        }
    }
}
