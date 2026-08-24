namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Dictionary -- System.Collections.Generic.Dictionary`2 wiring
// (the string->int32 AOT expansion, rtdict-b23 DICT-GC).
// Owners: System.Collections.Generic.Dictionary`2
//
// DISPOSITION (rtdict-b23, 2026-08-10): WIRED for the Dictionary<string,int>
// instantiation ONLY -- the runtime surface is dictsi.h + the new GC-OBJECT
// wrapper (dictsi_obj.h): System_Collections_Generic_Dictionary_2_string_int_class
// holds the raw Il2CppDictSI* in a NON-reference slot (refFieldBitmap 0), and
// the il2cpp::runtime::icall_dict_si_* wrappers below forward to the raw
// dictsi domain with the two wrapper-only semantics (set_Item insert-or-
// replace via il2cpp_dict_si_set; get_Item miss -> KeyNotFoundException).
//
// AOT GENERIC EXPANSION CONVENTION (mirror MethodMap.List.cs): Dictionary
// calls arrive as MemberRefs (NOT MethodSpecs -- genArgs is null) and the
// instantiation lives ONLY in the TypeSpec parent, decoded to the receiver
// instance name "System.Collections.Generic.Dictionary`2_string_int" by
// GenericInstantiationName. The int32-value-ness is therefore enforced by:
//   1. the folded receiver instance name (when tracked) must be the
//      string->int32 expansion; any other name -> LOUD,
//   2. the parameter count + signature bytes: the MemberRef signature is the
//      GENERIC DEFINITION signature (TKey/TValue arrive as ELEMENT_TYPE_VAR
//      0x13 -- probe-verified net48 dictcase blobs, e.g. Add is
//      `20 02 01 13 00 13 01`), exactly like List. The int32 capacity .ctor
//      param is the one concrete byte (0x08). ReadParamByte consumes each
//      VAR's compressed index, so Add/set_Item/TryGetValue read ps [13,13]
//      and ContainsKey/Remove read [13]. get_Item's RETURN is TValue (VAR) --
//      its index leaks into ps[0] (RawSignature consumes no VAR-return
//      index), so get_Item guards on COUNT ONLY (the List precedent).
//
// RECEIVER/ARGS (instance icalls, hashtable/stack precedent): the receiver
// rides in args[0] as a managed reference ("" cast); the string key arrives
// as Il2CppObject* ("" cast -- the icall casts internally); int32 values cast
// "i"; the TryGetValue out int is the ldloca ADDRESS ("" passthrough -- the
// icall writes through the pointer).
//
// GC CONTRACT (see dictsi_obj.h): the wrapper does NOT root its keys -- the
// caller keeps every stored key reachable. The e2e (dictcase) keys are
// string LITERALS interned by the g_strPool (rooted for the process
// lifetime), so the dict's raw key pointers never dangle.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDictionary(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The fold-tracked receiver instance name (when present) must be the
        // string->int32 expansion Dictionary`2_string_int.
        var siReceiver = receiverType is null or "System.Collections.Generic.Dictionary`2_string_int";
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"Dictionary`2 member '{name}' is not the Dictionary<string,int> expansion (receiver '{receiverType ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only the string->int32 dictsi domain is wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> icall_dict_si_ctor(self, 0); the
                // injected 0 lands AFTER the receiver (AppendZeroArg --
                // Hashtable precedent). Capacity ctor -> icall_dict_si_ctor
                // (self, capacity).
                if (count == 0)
                    return siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_ctor", 0, false, false, true, null, null, appendZero: true)
                        : Loud();
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && siReceiver)
                    return I("il2cpp::runtime::icall_dict_si_ctor", 1, false, false, true, new[] { "", "i" });
                return Loud();
            case "Add":
                // void Add(TKey, TValue) -- the generic DEFINITION signature:
                // ps [0x13, 0x13] (both VAR, indices consumed by
                // ReadParamByte). Concrete dictcase blob 20 02 01 13 00 13 01.
                return count == 2 && ps.Length == 2 && ps[0] == 0x13 && ps[1] == 0x13 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_add", 2, false, false, true, new[] { "", "", "i" })
                    : Loud();
            case "set_Item":
                // indexer SET: void set_Item(TKey, TValue) -- insert-or-replace
                // (same VAR [13,13] shape as Add).
                return count == 2 && ps.Length == 2 && ps[0] == 0x13 && ps[1] == 0x13 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_set", 2, false, false, true, new[] { "", "", "i" })
                    : Loud();
            case "get_Item":
                // indexer GET: TValue get_Item(TKey) -- the value or
                // KeyNotFoundException on a miss. The RETURN is TValue (0x13),
                // and RawSignature does not consume a VAR-return index, so the
                // index byte leaks into ps[0] (dictcase blob 20 01 13 01 13 00
                // -> ps [0x01]) -- guard on COUNT ONLY (List get_Item).
                return count == 1 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_get", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "TryGetValue":
                // bool TryGetValue(TKey, out TValue) -- the BYREF (0x10)
                // consumes its element (VAR 0x13), so ps is [13,13] like Add;
                // the out param is the ldloca ADDRESS ("" passthrough).
                return count == 2 && ps.Length == 2 && ps[0] == 0x13 && ps[1] == 0x13 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_try_get_value", 2, true, false, true, new[] { "", "", "" })
                    : Loud();
            case "ContainsKey":
                // bool ContainsKey(TKey): ps [0x13] (dictcase 20 01 02 13 00).
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_contains_key", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "Remove":
                // bool Remove(TKey): ps [0x13] (same shape as ContainsKey).
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_remove", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "get_Count":
                return count == 0 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_get_count", 0, true, false, true, new[] { "" })
                    : Loud();
            case "Clear":
                return count == 0 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_clear", 0, false, false, true, new[] { "" })
                    : Loud();
            case "get_Keys":
                // Keys: a fresh string[] SNAPSHOT array in the deterministic
                // hash-bucket order (dictsi.h). ReturnTypeName tracks the array
                // so ICollection`1<string> members over the snapshot dispatch
                // on the receiver hint (MethodMap.Dictionary.TryMapDictInterface).
                return count == 0 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_get_keys", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.String[]")
                    : Loud();
            case "get_Values":
                // Values: a fresh int32[] SNAPSHOT array, positionally
                // consistent with Keys.
                return count == 0 && siReceiver
                    ? I("il2cpp::runtime::icall_dict_si_get_values", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Int32[]")
                    : Loud();
            case "GetEnumerator":
                // foreach: the snapshot PAIR enumerator object (P25d
                // il2cpp_dict_si_get_enumerator -- the runtime walks frozen
                // Keys+Values arrays). ReturnTypeName tracks the Enumerator
                // pseudo-type so its struct members (MoveNext / get_Current /
                // Dispose, owner "Dictionary`2+Enumerator") and the IDisposable
                // arm re-resolve to the enumerator icalls.
                return count == 0 && siReceiver
                    ? I("il2cpp::il2cpp_dict_si_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Dictionary`2.Enumerator")
                    : Loud();
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapDictEnumeration -- P25d (dictp25): the Dictionary<string,int>
    // ENUMERATION STRUCT owners. The C# compiler calls the Enumerator struct
    // members DIRECTLY on the concrete nested types (owner
    // "System.Collections.Generic.Dictionary"2+Enumerator" etc.) and the
    // KeyValuePair<string,int> struct members on "System.Collections.Generic.
    // KeyValuePair"2". The receiver is the ADDRESS of the struct loc (the
    // ldloca "deref:r" shape -- the same pattern as List`1+Enumerator), and
    // every icall takes the managed enumerator / pair object.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapDictEnumeration(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Collections.Generic.Dictionary`2+Enumerator":
                switch (name)
                {
                    case "MoveNext":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "get_Current":
                        // The per-pair KeyValuePair<string,int> holder object.
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                                returnTypeName: "System.Collections.Generic.KeyValuePair`2")
                            : null;
                    case "Dispose":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                            : null;
                    default:
                        return null;
                }
            case "System.Collections.Generic.KeyValuePair`2":
                switch (name)
                {
                    case "get_Key":
                        return count == 0
                            ? I("il2cpp::il2cpp_kvp_string_int_get_key", 0, true, true, true, new[] { "deref:r" },
                                returnTypeName: "System.String")
                            : null;
                    case "get_Value":
                        return count == 0
                            ? I("il2cpp::il2cpp_kvp_string_int_get_value", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    default:
                        return null;
                }
            case "System.Collections.Generic.Dictionary`2+KeyCollection+Enumerator":
                switch (name)
                {
                    case "MoveNext":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "get_Current":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                                returnTypeName: "System.String")
                            : null;
                    case "Dispose":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                            : null;
                    default:
                        return null;
                }
            case "System.Collections.Generic.Dictionary`2+ValueCollection+Enumerator":
                switch (name)
                {
                    case "MoveNext":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_valuecollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "get_Current":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_valuecollection_enumerator_get_current", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "Dispose":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_valuecollection_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                            : null;
                    default:
                        return null;
                }
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapDictCollection -- P25d: the Dictionary<string,int> KeyCollection /
    // ValueCollection CLass receivers. The runtime verdict is that KeyCollection
    // IS the Keys snapshot array and ValueCollection IS the Values snapshot
    // array (get_Keys/get_Values return those arrays); GetEnumerator wraps the
    // array and Count returns the array length.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapDictCollection(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Collections.Generic.Dictionary`2+KeyCollection":
                switch (name)
                {
                    case "GetEnumerator":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_keycollection_get_enumerator", 0, true, true, true, new[] { "" },
                                returnTypeName: "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator")
                            : null;
                    case "get_Count":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_collection_get_count", 0, true, false, true, new[] { "" })
                            : null;
                    default:
                        return null;
                }
            case "System.Collections.Generic.Dictionary`2+ValueCollection":
                switch (name)
                {
                    case "GetEnumerator":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_valuecollection_get_enumerator", 0, true, true, true, new[] { "" },
                                returnTypeName: "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator")
                            : null;
                    case "get_Count":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_collection_get_count", 0, true, false, true, new[] { "" })
                            : null;
                    default:
                        return null;
                }
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapDictInterface -- P25d: the GENERIC interface receivers that alias a
    // Dictionary<string,int> value: IDictionary`2<string,int> (the dict wrapper)
    // and ICollection`1 (the Keys/Values snapshot ARRAYS or the dict wrapper).
    // The receiver hint MUST be tracked -- on the first pass (receiverType==null)
    // this DEFERS (null -> generated-name -> the receiver-aware re-resolve), the
    // TextReader / sldeep precedent.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapDictInterface(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        const string dictReceiver = "System.Collections.Generic.Dictionary`2_string_int";
        if (receiverType == null)
            return null;

        if (owner == "System.Collections.Generic.IDictionary`2" && receiverType == dictReceiver)
        {
            switch (name)
            {
                case "get_Count":
                    return count == 0
                        ? I("il2cpp::runtime::icall_dict_si_get_count", 0, true, false, true, new[] { "" })
                        : null;
                case "set_Item":
                    // indexer SET -> icall_dict_si_set (insert-or-replace).
                    return count == 2
                        ? I("il2cpp::runtime::icall_dict_si_set", 2, false, false, true, new[] { "", "", "i" })
                        : null;
                case "get_Item":
                    return count == 1
                        ? I("il2cpp::runtime::icall_dict_si_get", 1, true, false, true, new[] { "", "" })
                        : null;
                case "Add":
                    return count == 2
                        ? I("il2cpp::runtime::icall_dict_si_add", 2, false, false, true, new[] { "", "", "i" })
                        : null;
                case "Remove":
                    return count == 1
                        ? I("il2cpp::runtime::icall_dict_si_remove", 1, true, false, true, new[] { "", "" })
                        : null;
                case "TryGetValue":
                    return count == 2
                        ? I("il2cpp::runtime::icall_dict_si_try_get_value", 2, true, false, true, new[] { "", "", "" })
                        : null;
                case "ContainsKey":
                    return count == 1
                        ? I("il2cpp::runtime::icall_dict_si_contains_key", 1, true, false, true, new[] { "", "" })
                        : null;
                case "get_Keys":
                    return count == 0
                        ? I("il2cpp::runtime::icall_dict_si_get_keys", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.String[]")
                        : null;
                case "get_Values":
                    return count == 0
                        ? I("il2cpp::runtime::icall_dict_si_get_values", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Int32[]")
                        : null;
                default:
                    return null;
            }
        }

        if (owner == "System.Collections.Generic.ICollection`1")
        {
            if (receiverType == "System.String[]")
            {
                // The Keys snapshot array: Count = length; Contains = ordinal
                // scan (net48 KeyCollection<string>.Contains is dictionary
                // lookup-true -- the scan returns the identical boolean).
                switch (name)
                {
                    case "get_Count":
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_collection_get_count", 0, true, false, true, new[] { "" })
                            : null;
                    case "Contains":
                        return count == 1
                            ? I("il2cpp::il2cpp_dict_si_keys_contains", 1, true, false, true, new[] { "", "" })
                            : null;
                    default:
                        return null;
                }
            }
            if (receiverType == dictReceiver)
            {
                // ICollection<KeyValuePair<K,V>>.Count over the dict wrapper.
                if (name == "get_Count" && count == 0)
                    return I("il2cpp::runtime::icall_dict_si_get_count", 0, true, false, true, new[] { "" });
                return null;
            }
        }
        return null;
    }
}
