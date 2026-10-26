namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.SortedList -- System.Collections.SortedList icall mappings
// (sortedlist.h) + the G4 SortedList`2<string,int> expansion
// (sortedlistint_obj.h).
// Owners: System.Collections.SortedList,
//         System.Collections.Generic.SortedList`2
//
// DISPOSITION (dictsl-b23, 2026-08-11): the full sorted-array icall surface
// is WIRED for the non-generic SortedList domain (dictcase SortedList section
// green). Key/value are Il2CppObject* slots: the IL `box` opcode boxes value
// keys/values before the call (box wiring), so key/value args carry "" (the
// icalls cast internally); int32 index/capacity args carry "i"; indexer GET /
// GetByIndex / GetKey / GetValueAt / Keys / Values results stay
// Il2CppObject* -- the CALLER's castclass/unbox turns them back into values.
//
// SEMANTICS BASELINE (net48, see sortedlist.h):
//   * Add: duplicate key -> ArgumentException (list unchanged), null key ->
//     ArgumentNullException.
//   * get_Item (indexer GET by key, il2cpp_sortedlist_get_by_key): null for
//     both "missing" and "stored null" -- indistinguishable, like .NET.
//   * set_Item (indexer SET by key, il2cpp_sortedlist_set_by_key): replaces an
//     existing key's value or inserts at the sorted position -- never throws
//     for a missing key (unlike Add).
//   * ContainsKey / IndexOfKey / Remove: null key -> ArgumentNullException;
//     Remove of an absent key is a no-op.
//   * GetByIndex / GetKey / GetValueAt / RemoveAt: index outside [0, Count) ->
//     ArgumentOutOfRangeException.
//   * Keys / Values: SNAPSHOT arrays (not live views), index-aligned, in the
//     list's ascending enumeration order.
//   * Clear: drop every pair; Count -> 0, capacity unchanged.
//
// .ctor: the runtime create icall is SELF-MUTATING (void create(self,
// capacity)) -- the newobj temp stays the first argument and the capacity is
// the injected trailing 0 for the parameterless ctor (AppendZeroArg, the
// Hashtable precedent) or the int32 capacity arg for the 1-arg ctor.
//
// G4 (2026-09-12): SortedList<string,int> rides the non-generic sorted-array
// storage through the sortedlistint_obj.h wrapper layer (box/unbox at the
// C++ boundary, KeyNotFoundException on a miss, Remove's void->bool verdict,
// Keys -> a real string[] snapshot). instName is AUTHORITATIVE (the
// List/Dictionary/Queue precedent); a null tag keeps the historical
// non-generic mapping, any OTHER instantiation falls back to the
// generated-name path (loud at link) instead of silently taking the
// string/int arm.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapSortedList(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null,
        string? paramTypes = null)
    {
        var instTag = instName ?? receiverType;
        if (instTag is "System.Collections.Generic.SortedList`2_string_int")
            return TryMapSortedListSi(name, ps, count, paramTypes);
        // sorte1: the int-key/string-value mirror of the string_int expansion
        // (sortedlistis_obj.h -- box the int key at the C++ boundary).
        if (instTag is "System.Collections.Generic.SortedList`2_int_string")
            return TryMapSortedListIs(name, ps, count, paramTypes);
        if (instTag is not (null or "System.Collections.SortedList"))
            return null;

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> create(self, 0) (the injected 0 lands
                // AFTER the receiver -- AppendZeroArg); capacity ctor ->
                // create(self, capacity).
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 0, false, false, true, null, null, appendZero: true);
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 1, false, false, true, new[] { "", "i" });
                // SL-CTOR: the net48 (IComparer) / (IComparer, int) /
                // (IDictionary) / (IDictionary, IComparer) ctor overloads
                // (collections_sortedlist.cs:129/143/154/166). Both interface
                // parameter kinds carry the CLASS byte 0x12, so the DECODED
                // parameter-type text is the discriminator (the
                // SortedSetParamDefName precedent). The generic SortedList`2
                // rides the same non-generic sorted-array object, so its
                // (IComparer<TKey>) / (IComparer<TKey>, int) ctors reuse the
                // same icalls.
                var p0 = SortedSetParamDefName(paramTypes, 0);
                var p1 = SortedSetParamDefName(paramTypes, 1);
                if (count == 1 && ps[0] == 0x12 && p0 == "System.Collections.IComparer")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 1, false, false, true,
                        new[] { "", "", "" }, appendLiterals: new[] { "0" });
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x08 && p0 == "System.Collections.IComparer")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 2, false, false, true,
                        new[] { "", "", "i" });
                if (count == 1 && ps[0] == 0x12 && p0 == "System.Collections.IDictionary")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_from_dict", 1, false, false, true,
                        new[] { "", "", "" }, appendLiterals: new[] { "nullptr" });
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12
                    && p0 == "System.Collections.IDictionary" && p1 == "System.Collections.IComparer")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_from_dict", 2, false, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException($"SortedList ctor with {count} arg(s) not wired (the parameterless / capacity / (IComparer) / (IComparer,int) / (IDictionary) / (IDictionary,IComparer) forms map to the sortedlist create icalls; the decoded param kinds were [{string.Join(", ", paramTypes ?? "")}])");
            case "Add": return I("il2cpp::runtime::il2cpp_sortedlist_add", 2, false, false, true, new[] { "", "", "" });
            case "set_Item": return I("il2cpp::runtime::il2cpp_sortedlist_set_by_key", 2, false, false, true, new[] { "", "", "" });
            case "get_Item": return I("il2cpp::runtime::il2cpp_sortedlist_get_by_key", 1, true, true, true, new[] { "", "" });
            case "GetByIndex": return I("il2cpp::runtime::il2cpp_sortedlist_get_by_index", 1, true, true, true, new[] { "", "i" });
            case "GetKey": return I("il2cpp::runtime::il2cpp_sortedlist_get_key_at", 1, true, true, true, new[] { "", "i" });
            case "GetValueAt": return I("il2cpp::runtime::il2cpp_sortedlist_get_value_at", 1, true, true, true, new[] { "", "i" });
            case "IndexOfKey": return I("il2cpp::runtime::il2cpp_sortedlist_index_of_key", 1, true, false, true, new[] { "", "" });
            case "ContainsKey": return I("il2cpp::runtime::il2cpp_sortedlist_contains_key", 1, true, false, true, new[] { "", "" });
            case "Remove": return I("il2cpp::runtime::il2cpp_sortedlist_remove_by_key", 1, false, false, true, new[] { "", "" });
            case "RemoveAt": return I("il2cpp::runtime::il2cpp_sortedlist_remove_at", 1, false, false, true, new[] { "", "i" });
            case "SetByIndex": return I("il2cpp::runtime::il2cpp_sortedlist_set_at", 2, false, false, true, new[] { "", "i", "" });
            case "get_Count": return I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" });
            case "get_Capacity": return I("il2cpp::runtime::il2cpp_sortedlist_get_capacity", 0, true, false, true, new[] { "" });
            // D-SL-3 (sldeep): the Keys / Values snapshots are ILCollection-typed
            // at the C# surface. ReturnTypeName makes the fold track the result
            // as a distinct pseudo-type so ICollection.Count / IEnumerable
            // enumeration on the snapshot can dispatch by receiver type (see
            // MethodMap.Collections.cs -- the Console.OutStream precedent).
            case "get_Keys": return I("il2cpp::runtime::il2cpp_sortedlist_get_keys", 0, true, true, true, new[] { "" },
                returnTypeName: "System.Collections.SortedList.KeyList");
            case "get_Values": return I("il2cpp::runtime::il2cpp_sortedlist_get_values", 0, true, true, true, new[] { "" },
                returnTypeName: "System.Collections.SortedList.ValueList");
            case "Clear": return I("il2cpp::runtime::il2cpp_sortedlist_clear", 0, false, false, true, new[] { "" });
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // G4: SortedList<string,int> -- the non-generic sorted-array storage plus
    // the sortedlistint_obj.h box/unbox wrappers (see that header for the
    // semantics contract). VAR signatures: params [0x13, 0x13] for the
    // key+value pairs; a VAR-return's index byte leaks into ps[0] for the
    // get_Item key overload (the dictsi get_Item precedent), while the
    // int-index overload carries a concrete 0x08 param byte.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedListSi(string name, byte[] ps, int count, string? paramTypes = null)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 0, false, false, true, null, null, appendZero: true);
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 1, false, false, true, new[] { "", "i" });
                // SL-CTOR: the generic SortedList<string,int> (IComparer<string>)
                // / (IComparer<string>, int) ctors (collections_sortedlist.cs
                // generic overloads). The comparer is a GENERICINST class
                // signature (0x15) -- the decoded parameter-type text names
                // System.Collections.Generic.IComparer`1. The generic list rides
                // the SAME non-generic sorted-array object, so the comparer
                // icall is shared.
                var p0 = SortedSetParamDefName(paramTypes, 0);
                if (count == 1 && ps[0] == 0x15 && p0 == "System.Collections.Generic.IComparer`1")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 1, false, false, true,
                        new[] { "", "", "" }, appendLiterals: new[] { "0" });
                if (count == 2 && ps[0] == 0x15 && ps[1] == 0x08 && p0 == "System.Collections.Generic.IComparer`1")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 2, false, false, true,
                        new[] { "", "", "i" });
                throw new NotSupportedException($"SortedList<string,int> ctor with {count} arg(s) not wired (the parameterless / capacity / (IComparer<string>) / (IComparer<string>,int) forms map to the sortedlist create icalls; the decoded param kinds were [{string.Join(", ", paramTypes ?? "")}])");
            case "Add":
                if (count != 2) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_add", 2, false, false, true, new[] { "", "", "i" });
            case "set_Item":
                if (count != 2) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_set_by_key", 2, false, false, true, new[] { "", "", "i" });
            case "get_Item":
                // indexer GET by key -> unboxed value or KeyNotFoundException.
                // net48 emits the OPEN generic signature (ret VAR1 + param
                // VAR0 -- the dictcase observation), so the by-key form is the
                // only one distinguishable here; the rare this[int] overload
                // falls back to the generated-name path (loud at link).
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_by_key", 1, true, false, true, new[] { "", "" }, "i");
            case "GetByIndex":
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_by_index", 1, true, false, true, new[] { "", "i" }, "i");
            case "GetValueAt":
                // same runtime verdict as GetByIndex (the value at the sorted
                // index), unboxed.
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_by_index", 1, true, false, true, new[] { "", "i" }, "i");
            case "GetKey":
                // TKey GetKey(int): the key is already a managed string
                // reference in the core slot (ReturnsRef).
                if (count != 1) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_get_key_at", 1, true, true, true, new[] { "", "i" });
            case "IndexOfKey":
                if (count != 1) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_index_of_key", 1, true, false, true, new[] { "", "" });
            case "RemoveAt":
                if (count != 1) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_remove_at", 1, false, false, true, new[] { "", "i" });
            case "get_Capacity":
                if (count != 0) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_get_capacity", 0, true, false, true, new[] { "" });
            case "SetByIndex":
                if (count != 2) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_set_at", 2, false, false, true, new[] { "", "i", "i" });
            case "ContainsKey":
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_contains_key", 1, true, false, true, new[] { "", "" });
            case "ContainsValue":
                // net48 value equality over the sorted entries; the wrapper
                // scans the values snapshot (no core contains_value icall).
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_contains_value", 1, true, false, true, new[] { "", "i" });
            case "Remove":
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_remove", 1, true, false, true, new[] { "", "" });
            case "get_Count":
                if (count != 0) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" });
            case "get_Keys":
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_keys", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.String[]");
            case "get_Values":
                // net48: Values returns a LIVE IList<TValue> view; the wrapper
                // returns the list itself and the view pseudo-type routes the
                // interface members (indexer/Count/GetEnumerator) to the
                // current-entry icalls (TryMapSortedListSiView below).
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_values", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.Generic.SortedList`2.ValueList_string_int");
            case "TryGetValue":
                // bool TryGetValue(TKey, out TValue): the out param is the
                // ldloca ADDRESS ("" passthrough), the key is a managed
                // reference. The wrapper delegates to the sortedlist core and
                // unboxes the value.
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_try_get_value", 2, true, false, true, new[] { "", "", "" });
            case "GetEnumerator":
                // foreach KeyValuePair<TKey,TValue>: the shared pair-enumerator
                // object over the sorted keys/values snapshots (the dictsi
                // domain); the result tracks Dictionary`2.Enumerator so the
                // Enumerator/KVP struct members resolve.
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_sortedlist_si_get_enumerator", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.Generic.Dictionary`2.Enumerator");
            case "Clear":
                if (count != 0) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_clear", 0, false, false, true, new[] { "" });
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // sorte1: SortedList<int,string> -- the int-key/string-value mirror of the
    // string_int expansion (sortedlistis_obj.h). The key crosses as int32 ("i"),
    // the value is a managed string reference ("").
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedListIs(string name, byte[] ps, int count, string? paramTypes = null)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 0, false, false, true, null, null, appendZero: true);
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 1, false, false, true, new[] { "", "i" });
                // SL-CTOR: the generic SortedList<int,string> (IComparer<int>)
                // / (IComparer<int>, int) ctors -- the int-key mirror of the
                // string_int expansion (same shared sorted-array object).
                var p0 = SortedSetParamDefName(paramTypes, 0);
                if (count == 1 && ps[0] == 0x15 && p0 == "System.Collections.Generic.IComparer`1")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 1, false, false, true,
                        new[] { "", "", "" }, appendLiterals: new[] { "0" });
                if (count == 2 && ps[0] == 0x15 && ps[1] == 0x08 && p0 == "System.Collections.Generic.IComparer`1")
                    return I("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", 2, false, false, true,
                        new[] { "", "", "i" });
                throw new NotSupportedException($"SortedList<int,string> ctor with {count} arg(s) not wired (the parameterless / capacity / (IComparer<int>) / (IComparer<int>,int) forms map to the sortedlist create icalls; the decoded param kinds were [{string.Join(", ", paramTypes ?? "")}])");
            case "Add":
                if (count != 2) return null;
                return I("il2cpp::runtime::icall_sortedlist_is_add", 2, false, false, true, new[] { "", "i", "" });
            case "get_Item":
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_is_get_by_key", 1, true, true, true, new[] { "", "i" });
            case "IndexOfKey":
                if (count != 1) return null;
                return I("il2cpp::runtime::icall_sortedlist_is_index_of_key", 1, true, false, true, new[] { "", "i" });
            case "get_Count":
                if (count != 0) return null;
                return I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" });
            case "get_Keys":
                // The int32[] snapshot; the tracked System.Int32[] receiver lets
                // the downstream enumeration resolve.
                if (count != 0) return null;
                return I("il2cpp::runtime::icall_sortedlist_is_get_keys", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Int32[]");
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // G4: SortedDictionary<string,int> -- same observable contract as
    // SortedList<string,int> for the wired surface (sorted-by-key order,
    // duplicate Add -> ArgumentException, missing get_Item ->
    // KeyNotFoundException, Remove -> bool, Keys -> sorted snapshot), so it
    // REUSES the sortedlistint_obj.h wrapper layer and the
    // il2cpp_sortedlist_class() descriptor instead of a second storage
    // domain. Only the shared member names delegate; index members never
    // occur on SortedDictionary and unwired members (TryGetValue) return
    // null -> generated-name fallback (loud at link).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedDictionary(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null,
        string? paramTypes = null)
    {
        var instTag = instName ?? receiverType;
        if (instTag is not "System.Collections.Generic.SortedDictionary`2_string_int")
            return null;
        return TryMapSortedListSi(name, ps, count, paramTypes);
    }

    //--------------------------------------------------------------------------
    // P3-extra: SortedList<string,int>.Values view + enumerator routing.
    //
    // get_Values returns the LIST itself (net48 live-view semantics; see
    // sortedlistint_obj.cpp) with the tracked pseudo-type
    // "System.Collections.Generic.SortedList`2.ValueList_string_int". The C#
    // compiler then emits GENERIC interface callvirts on that receiver, whose
    // declaring owners are the interfaces (not the class), so the concrete
    // si arm never sees them. This helper is the receiver-gated pre-check the
    // resolver runs BEFORE the list/dict/iterator-interface fallbacks:
    //   IList`1.get_Item      -> icall_sortedlist_si_get_by_index (live, unboxed)
    //   ICollection`1.get_Count -> il2cpp_sortedlist_get_count    (live)
    //   IEnumerable`1.GetEnumerator -> icall_sortedlist_si_values_get_enumerator
    //                             (snapshot + core array enumerator; the result
    //                              tracks "System.Collections.SortedList.Enumerator")
    //   IEnumerator`1.MoveNext/get_Current/Reset on that tracked enumerator:
    //     get_Current unboxes the core element for the int32 carrier.
    // Any other receiver/owner returns null -> the existing fallbacks stay in
    // charge (unknown shapes remain LOUD at link).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedListSiView(string owner, string name, int count, string? receiverType)
    {
        if (receiverType is "System.Collections.Generic.SortedList`2.ValueList_string_int")
        {
            switch (owner)
            {
                case "System.Collections.Generic.IList`1":
                    if (name == "get_Item" && count == 1)
                        return I("il2cpp::runtime::icall_sortedlist_si_get_by_index", 1, true, false, true, new[] { "", "i" }, "i");
                    return null;
                case "System.Collections.Generic.ICollection`1":
                    if (name == "get_Count" && count == 0)
                        return I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" });
                    return null;
                case "System.Collections.Generic.IEnumerable`1":
                    if (name == "GetEnumerator" && count == 0)
                        return I("il2cpp::runtime::icall_sortedlist_si_values_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.SortedList.Enumerator");
                    return null;
                default:
                    return null;
            }
        }
        if (receiverType is "System.Collections.SortedList.Enumerator")
        {
            if (owner == "System.Collections.Generic.IEnumerator`1")
            {
                switch (name)
                {
                    case "MoveNext":
                        if (count == 0)
                            return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", 0, true, false, true, new[] { "" });
                        return null;
                    case "get_Current":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_sortedlist_si_enumerator_get_current", 0, true, false, true, new[] { "" }, "i");
                        return null;
                    case "Reset":
                        if (count == 0)
                            return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", 0, false, false, true, new[] { "" });
                        return null;
                    case "Dispose":
                        // sorte1: the foreach-finally Dispose on the shared
                        // snapshot enumerator object -- the receiver is the
                        // address-taken local (deref:r), the core dispose is a
                        // no-op (the snapshot enumerator is not IDisposable).
                        if (count == 0)
                            return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                        return null;
                    default:
                        return null;
                }
            }
            return null;
        }
        return null;
    }

    //--------------------------------------------------------------------------
    // sorte1: the SortedDictionary<string,int> / SortedList<string,int> Values
    // view GetEnumerator. Both get_Values implementations track the SAME shared
    // pseudo-type ("System.Collections.Generic.SortedList`2.ValueList_string_int");
    // the compiler's pattern foreach then calls the CONCRETE nested
    // ValueCollection.GetEnumerator (the declaring owner is the nested type, not
    // the IEnumerable`1 interface the interface arm above handles), so it routes
    // here. The mapping is the shared sortedlist value-collection enumerator
    // (snapshot + core array enumerator), whose result tracks the
    // "System.Collections.SortedList.Enumerator" pseudo-type so the IEnumerator`1
    // MoveNext/get_Current/Reset arms in TryMapSortedListSiView resolve.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedListValueCollection(string owner, string name, int count, string? receiverType)
    {
        if ((owner == "System.Collections.Generic.SortedList`2+ValueCollection"
                || owner == "System.Collections.Generic.SortedDictionary`2+ValueCollection")
            && receiverType is "System.Collections.Generic.SortedList`2.ValueList_string_int"
            && name == "GetEnumerator" && count == 0)
            return I("il2cpp::runtime::icall_sortedlist_si_values_get_enumerator", 0, true, true, true, new[] { "" },
                returnTypeName: "System.Collections.SortedList.Enumerator");
        return null;
    }

    //--------------------------------------------------------------------------
    // sorte1: the CONCRETE nested ValueCollection+Enumerator members. The
    // shared enumerator object (tracked "System.Collections.SortedList.Enumerator")
    // is a struct-like object whose MoveNext/get_Current/Reset the compiler
    // calls DIRECTLY on the nested owner (not through IEnumerator`1); the
    // interface arm in TryMapSortedListSiView handles the interface-owner form,
    // this helper handles the nested-owner form.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedListValueEnumerator(string owner, string name, int count, string? receiverType)
    {
        if (receiverType is not "System.Collections.SortedList.Enumerator") return null;
        // The nested-owner receiver is the address-taken enumerator local (the
        // "deref:r" shape the KeyCollection enumerator uses), so the cast
        // dereferences it to the managed enumerator object.
        if (name == "MoveNext" && count == 0)
            return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", 0, true, false, true, new[] { "deref:r" });
        if (name == "get_Current" && count == 0)
            return I("il2cpp::runtime::icall_sortedlist_si_enumerator_get_current", 0, true, false, true, new[] { "deref:r" }, "i");
        if (name == "Reset" && count == 0)
            return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", 0, false, false, true, new[] { "deref:r" });
        return null;
    }

    //--------------------------------------------------------------------------
    // G4 residual (g4sorteddict): SortedDictionary<string,int>.KeyCollection
    // enumeration. P3 maps get_Keys -> the String[] Keys snapshot (frozen
    // copy, see TryMapSortedDictionary); the compiler's pattern-based foreach
    // then calls the nested KeyCollection.GetEnumerator (returning the nested
    // struct Enumerator) and its struct members directly on the address-taken
    // local. The snapshot enumerator reuses the sortedlist array-enumerator
    // object (Il2CppArrayEnumerator, sortedlist.h): the frozen Keys snapshot
    // IS an Il2CppArray of string references, and the core enumerator icalls
    // walk its slots. The struct local carries the enumerator object
    // reference (deref:r -- the P25b List<int>.Enumerator precedent).
    //
    // SEMANTICS (probed against net48, g4gaps probe g4keys_semantics):
    // net48's KeyCollection is a LIVE view and its enumerator is fail-fast
    // (a mutation between MoveNexts throws InvalidOperationException); this
    // AOT domain enumerates the frozen Keys snapshot taken at get_Keys time
    // (the P3 Keys-as-snapshot contract, D-STK-1 family divergence). The
    // observable order (ascending key order) and the pre-mutation contents
    // are identical.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSortedDictionaryKeyCollection(string owner, string name, byte[] ps, int count,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner == "System.Collections.Generic.SortedDictionary`2+KeyCollection")
        {
            // GetEnumerator on the tracked String[] Keys snapshot. A foreign /
            // untracked receiver stays null -> the P26 BCL-owner guard shouts.
            if (name == "GetEnumerator" && count == 0 && receiverType == "System.String[]")
                return I("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator");
            return null;
        }
        if (owner == "System.Collections.Generic.SortedDictionary`2+KeyCollection+Enumerator")
        {
            switch (name)
            {
                case "MoveNext":
                    return count == 0
                        ? I("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                        : null;
                case "get_Current":
                    // TKey (string) reference read -- the snapshot slot.
                    return count == 0
                        ? I("il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current", 0, true, true, true, new[] { "deref:r" })
                        : null;
                case "Reset":
                    return count == 0
                        ? I("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", 0, false, false, true, new[] { "deref:r" })
                        : null;
                default:
                    return null;
            }
        }
        return null;
    }

    //--------------------------------------------------------------------------
    // G4 residual (g4sortedlist2): IList<TKey>.get_Item on the tracked Keys
    // String[] snapshot (`sl.Keys[i]`). The compiler emits an INTERFACE
    // callvirt (IList`1 owns the indexer), so the concrete-owner arms never
    // see it; the tracked snapshot receiver is a real Il2CppArray, so the
    // element read IS Array.GetValue's reference-slot read
    // (il2cpp_array_get_value, arrayextra.h: reference arrays return the slot
    // itself). net48's KeyList indexer is a LIVE view read; this AOT domain
    // reads the frozen snapshot taken at get_Keys time (same D-STK-1 family
    // divergence as the enumeration above). Exception-type divergence:
    // net48 KeyList[i] throws ArgumentOutOfRangeException, the array access
    // throws IndexOutOfRangeException (probed; accepted for this stage).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapSnapshotArrayInterface(string owner, string name, int count, string? receiverType)
    {
        if (owner == "System.Collections.Generic.IList`1" && name == "get_Item" && count == 1
            && receiverType == "System.String[]")
            return I("il2cpp::il2cpp_array_get_value", 1, true, true, true, new[] { "a", "i" });
        return null;
    }
}


