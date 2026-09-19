namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.SortedSet -- the G4 System.Collections.Generic.SortedSet`1<int>
// expansion (sortedset_obj.h over the sortedlist.h storage).
// Owners: System.Collections.Generic.SortedSet`1, ...SortedSet`1+Enumerator
//
// DESIGN: SortedSet<int> rides the non-generic sorted-array storage with
// BOXED int32 keys (ascending order from the core's boxed-Int32 sort
// contract; uniqueness from the core's duplicate-key contract, pre-checked
// by the wrapper so Add returns the net48 bool). All member calls carry the
// int32 in/out at the C++ boundary ("i"); Min/Max unbox the key bounds;
// GetEnumerator returns the dictsi keycollection wrapper over the ascending
// boxed-int keys snapshot (the HashSet<int> enumeration precedent) -- the
// tracked token REUSES Dictionary`2.KeyCollection.Enumerator so MoveNext /
// Dispose / the finally-IDisposable arm dispatch unchanged, and the element
// get_Current unboxes through the sortedset wrapper.
//
// instName is AUTHORITATIVE (the List/Dictionary/Queue precedent); a null /
// non-int tag returns null so other instantiations fall back to the
// generated-name path (loud at link) instead of silently taking the int arm.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapSortedSet(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        var intReceiver = instTag is "System.Collections.Generic.SortedSet`1_int";
        var stringReceiver = instTag is "System.Collections.Generic.SortedSet`1_string";
        var longReceiver = instTag is "System.Collections.Generic.SortedSet`1_long";
        var doubleReceiver = instTag is "System.Collections.Generic.SortedSet`1_double";
        var objectReceiver = instTag is "System.Collections.Generic.SortedSet`1_object";
        var classReceiver = instTag is "System.Collections.Generic.SortedSet`1_class";
        var refReceiver = stringReceiver || objectReceiver || classReceiver;
        if (!(intReceiver || stringReceiver || longReceiver || doubleReceiver || objectReceiver || classReceiver))
            return null;

        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 0, false, false, true, null, null, appendZero: true);
                if (count == 1)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 1, false, false, true, new[] { "", "i" });
                return null;
            case "Add":
                if (count != 1) return null;
                if (intReceiver)
                    return I("il2cpp::runtime::icall_sortedset_int_add", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_sortedset_long_add", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_sortedset_double_add", 1, true, false, true, new[] { "", "d" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_sortedset_ref_add", 1, true, false, true, new[] { "", "" });
                return null;
            case "Contains":
                if (count != 1) return null;
                if (intReceiver)
                    return I("il2cpp::runtime::icall_sortedset_int_contains", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_sortedset_long_contains", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_sortedset_double_contains", 1, true, false, true, new[] { "", "d" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_sortedset_ref_contains", 1, true, false, true, new[] { "", "" });
                return null;
            case "Remove":
                if (count != 1) return null;
                if (intReceiver)
                    return I("il2cpp::runtime::icall_sortedset_int_remove", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_sortedset_long_remove", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_sortedset_double_remove", 1, true, false, true, new[] { "", "d" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_sortedset_ref_remove", 1, true, false, true, new[] { "", "" });
                return null;
            case "get_Count":
                return count == 0
                    ? I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" })
                    : null;
            case "get_Min":
                if (count != 0) return null;
                if (intReceiver)
                    return I("il2cpp::runtime::icall_sortedset_int_get_min", 0, true, false, true, new[] { "" }, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_sortedset_long_get_min", 0, true, false, true, new[] { "" }, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_sortedset_double_get_min", 0, true, false, true, new[] { "" }, "d");
                if (refReceiver)
                    return I("il2cpp::runtime::icall_sortedset_ref_get_min", 0, true, true, true, new[] { "" });
                return null;
            case "get_Max":
                if (count != 0) return null;
                if (intReceiver)
                    return I("il2cpp::runtime::icall_sortedset_int_get_max", 0, true, false, true, new[] { "" }, "i");
                if (longReceiver)
                    return I("il2cpp::runtime::icall_sortedset_long_get_max", 0, true, false, true, new[] { "" }, "l");
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_sortedset_double_get_max", 0, true, false, true, new[] { "" }, "d");
                if (refReceiver)
                    return I("il2cpp::runtime::icall_sortedset_ref_get_max", 0, true, true, true, new[] { "" });
                return null;
            case "GetEnumerator":
                if (count != 0) return null;
                if (intReceiver)
                    return I("il2cpp::il2cpp_sortedset_int_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator");
                if (longReceiver)
                    return I("il2cpp::il2cpp_sortedset_long_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.SortedSet`1.Enumerator_long");
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_sortedset_double_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.SortedSet`1.Enumerator_double");
                if (refReceiver)
                    return I("il2cpp::il2cpp_sortedset_ref_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.SortedSet`1.Enumerator_ref");
                return null;
            case "Clear":
                return count == 0
                    ? I("il2cpp::runtime::il2cpp_sortedlist_clear", 0, false, false, true, new[] { "" })
                    : null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapSortedSetEnumerator -- the SortedSet<int>+Enumerator STRUCT
    // members. The runtime enumerator IS the dictsi keycollection enumerator
    // object (ascending boxed-int keys snapshot in its keys slot), so
    // MoveNext/Dispose reuse that domain's icalls and get_Current unboxes
    // through the sortedset wrapper (the TryMapHashSetEnumerator shape).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedSetEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        var isIntEnum = receiverType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator";
        var isLongEnum = receiverType == "System.Collections.Generic.SortedSet`1.Enumerator_long";
        var isDoubleEnum = receiverType == "System.Collections.Generic.SortedSet`1.Enumerator_double";
        var isRefEnum = receiverType == "System.Collections.Generic.SortedSet`1.Enumerator_ref";
        if (!isIntEnum && !isLongEnum && !isDoubleEnum && !isRefEnum)
            return null;
        switch (name)
        {
            case "MoveNext":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                if (count != 0) return null;
                if (isLongEnum)
                    return I("il2cpp::runtime::icall_sortedset_long_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (isDoubleEnum)
                    return I("il2cpp::runtime::icall_sortedset_double_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (isRefEnum)
                    return I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.Object");
                return I("il2cpp::runtime::icall_sortedset_int_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
            case "Dispose":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                    : null;
            default:
                return null;
        }
    }
}
