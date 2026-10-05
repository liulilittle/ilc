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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        // The TypeSpec instantiation (when the call's parent carries one) is
        // AUTHORITATIVE -- it disambiguates the FIRST pass too (the List
        // precedent): a hint-less Dictionary<string,string> call must not
        // silently take the string->int32 arm. Otherwise the folded receiver
        // name decides; a null tag keeps the historical int32 mapping.
        var instTag = instName ?? receiverType;
        // RISK5 R2: a null tag DEFERS (fold re-resolves with tracked receiver).
        if (instTag is null) return null;
        // The fold-tracked receiver instance name (when present) must be the
        // string->int32 expansion Dictionary`2_string_int.
        var siReceiver = instTag is "System.Collections.Generic.Dictionary`2_string_int";
        // Phase-2 absorption: the string->string expansion rides the SHARED
        // generic table (dictgen.h) through the ref-key twins
        // (icall_dict_gen_*_o with kind literals ["0","0"]) -- the dictss
        // shim is deleted. The value casts are "" (managed reference
        // passthrough) and get_Item returns a managed reference (ReturnsRef,
        // no ResultKind).
        var ssReceiver = instTag is "System.Collections.Generic.Dictionary`2_string_string";
        // G4 nested: Dictionary<string,List<int>> -- the value is a managed
        // reference (List<int>) exactly like dictss's string value, so every
        // object-model arm is SHARED (no boxing); only get_Item additionally
        // TRACKS the value instance name so member calls on the returned list
        // dispatch as List`1_int.
        var nestedListReceiver = instTag is "System.Collections.Generic.Dictionary`2_string_System.Collections.Generic.List`1_int";
        // Phase-2 absorption: the Uri->string expansion rides the same shared
        // generic table through the ref-key twins with kind literals
        // ["2","0"] (the Uri key normalizes via icall_uri_to_string inside
        // the twin, then the string-kind table path -- the T4 uristr
        // contract, no twin family). Keyless arms (.ctor/get_Count/Clear/
        // get_Values) share the gen icalls directly (no key arg).
        // get_Keys/GetEnumerator stay LOUD: Keys would need a Uri[]
        // reconstruction the snapshot cannot provide (parity with the
        // dictss GetEnumerator gap).
        var uriReceiver = instTag is "System.Collections.Generic.Dictionary`2_System.Uri_string";
        // Phase-1 (dictgen): the int->string expansion rides the SINGLE shared
        // generic icall family icall_dict_gen_* (dictgen.h) with kind codes
        // appended as literals (keyKind=1 int32-boxed, valueKind=0
        // string-ref). The int32 key arrives unboxed ("i" cast) and is boxed
        // inside the icall; string values/references are ReturnsRef.
        var isReceiver = instTag is "System.Collections.Generic.Dictionary`2_int_string";
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"Dictionary`2 member '{name}' is not a wired expansion (receiver '{receiverType ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only the string->int32 dictsi domain and the dictgen domains (int32->string, string->string, Uri->string) are wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> icall_dict_*_ctor(self, 0); the
                // injected 0 lands AFTER the receiver (AppendZeroArg --
                // Hashtable precedent). Capacity ctor -> (self, capacity).
                if (count == 0)
                    return siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_ctor", 0, false, false, true, null, null, appendZero: true)
                        : ssReceiver || nestedListReceiver || uriReceiver || isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_ctor", 0, false, false, true, null, null, appendZero: true)
                            : Loud();
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && (ssReceiver || nestedListReceiver || uriReceiver || isReceiver))
                    return I("il2cpp::runtime::icall_dict_gen_ctor", 1, false, false, true, new[] { "", "i" });
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && siReceiver)
                    return I("il2cpp::runtime::icall_dict_si_ctor", 1, false, false, true, new[] { "", "i" });
                return Loud();
            case "Add":
                // void Add(TKey, TValue) -- the generic DEFINITION signature:
                // ps [0x13, 0x13] (both VAR, indices consumed by
                // ReadParamByte). Concrete dictcase blob 20 02 01 13 00 13 01.
                // Phase-2 absorption: string->string (+nested) and Uri->string
                // ride the ref-key twins (kind literals ["0","0"] / ["2","0"]).
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_add_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_add_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_add", 2, false, false, true, new[] { "", "", "i" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_add", 2, false, false, true, new[] { "", "i", "" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "set_Item":
                // indexer SET: void set_Item(TKey, TValue) -- insert-or-replace
                // (same VAR [13,13] shape as Add).
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_set_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_set_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_set", 2, false, false, true, new[] { "", "", "i" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_set", 2, false, false, true, new[] { "", "i", "" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "get_Item":
                // indexer GET: TValue get_Item(TKey) -- the value or
                // KeyNotFoundException on a miss. The RETURN is TValue (0x13),
                // and RawSignature does not consume a VAR-return index, so the
                // index byte leaks into ps[0] (dictcase blob 20 01 13 01 13 00
                // -> ps [0x01]) -- guard on COUNT ONLY (List get_Item). The
                // string->string value is a managed reference (ReturnsRef).
                if (count != 1) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_o", 1, true, true, true, new[] { "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_o", 1, true, true, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" },
                        returnTypeName: nestedListReceiver ? "System.Collections.Generic.List`1_int" : null)
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get", 1, true, false, true, new[] { "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_get", 1, true, true, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "TryGetValue":
                // bool TryGetValue(TKey, out TValue) -- the BYREF (0x10)
                // consumes its element (VAR 0x13), so ps is [13,13] like Add;
                // the out param is the ldloca ADDRESS ("" passthrough).
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_o", 2, true, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_o", 2, true, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_try_get_value", 2, true, false, true, new[] { "", "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_try_get_value", 2, true, false, true, new[] { "", "", "" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "ContainsKey":
                // bool ContainsKey(TKey): ps [0x13] (dictcase 20 01 02 13 00).
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_contains_key", 1, true, false, true, new[] { "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_contains_key", 1, true, false, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "Remove":
                // bool Remove(TKey): ps [0x13] (same shape as ContainsKey).
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_remove", 1, true, false, true, new[] { "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_remove", 1, true, false, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                            : Loud();
            case "get_Count":
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver || uriReceiver || isReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_count", 0, true, false, true, new[] { "" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get_count", 0, true, false, true, new[] { "" })
                        : Loud();
            case "Clear":
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver || uriReceiver || isReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_clear", 0, false, false, true, new[] { "" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_clear", 0, false, false, true, new[] { "" })
                        : Loud();
            case "get_Keys":
                // Keys: a fresh string[] SNAPSHOT array in insertion order
                // (the gen ordered table). ReturnTypeName tracks the array so
                // ICollection`1<string> members over the snapshot dispatch on
                // the receiver hint.
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_keys_o", 0, true, true, true, new[] { "" },
                        appendLiterals: new[] { "0" },
                        returnTypeName: "System.String[]")
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get_keys", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.String[]")
                        : Loud();
            case "get_Values":
                // Values: a fresh snapshot array, positionally consistent with
                // Keys; int32[] for dictsi, string[] for the gen domains.
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver || uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_values_o", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.String[]")
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get_values", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Int32[]")
                        : Loud();
            case "GetEnumerator":
                // foreach: the snapshot PAIR enumerator object (P25d
                // il2cpp_dict_si_get_enumerator -- the runtime walks frozen
                // Keys+Values arrays). ReturnTypeName tracks the Enumerator
                // pseudo-type so its struct members (MoveNext / get_Current /
                // Dispose, owner "Dictionary`2+Enumerator") and the IDisposable
                // arm re-resolve to the enumerator icalls. (dictss: the
                // KeyValuePair<string,string> enumeration is not wired yet --
                // LOUD, the Keys/Values snapshots cover the g4dictstrstr
                // surface.) Phase-1 dictgen: same snapshot discipline over the
                // int32[]+string[] snapshots; the tracked name carries the
                // _int_string suffix so the Enumerator/KVP arms below branch
                // to the gen icalls. get_Keys/get_Values stay LOUD for the
                // int_string expansion (no anchor needs them yet).
                return count == 0 && siReceiver
                    ? I("il2cpp::il2cpp_dict_si_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Dictionary`2.Enumerator")
                    : count == 0 && isReceiver
                        ? I("il2cpp::il2cpp_dict_gen_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Generic.Dictionary`2.Enumerator_int_string")
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
                // Phase-1 dictgen: the tracked "...Enumerator_int_string"
                // receiver rides the same snapshot discipline (the gen
                // enumerator object has the identical keys/values/index
                // layout), so MoveNext/Dispose share the si walk while
                // get_Current builds the int_string KVP holder.
                switch (name)
                {
                    case "MoveNext":
                        if (receiverType?.EndsWith("Enumerator_int_string", StringComparison.Ordinal) == true)
                            return count == 0
                                ? I("il2cpp::il2cpp_dict_gen_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                                : null;
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "get_Current":
                        // The per-pair KeyValuePair<string,int> holder object.
                        if (receiverType?.EndsWith("Enumerator_int_string", StringComparison.Ordinal) == true)
                            return count == 0
                                ? I("il2cpp::il2cpp_dict_gen_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                                    returnTypeName: "System.Collections.Generic.KeyValuePair`2_int_string")
                                : null;
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                                returnTypeName: "System.Collections.Generic.KeyValuePair`2")
                            : null;
                    case "Dispose":
                        if (receiverType?.EndsWith("Enumerator_int_string", StringComparison.Ordinal) == true)
                            return count == 0
                                ? I("il2cpp::il2cpp_dict_gen_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                                : null;
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
                        // Phase-1 dictgen: the int_string KVP holder carries
                        // the key inline (int32 return, not a reference).
                        if (receiverType?.EndsWith("KeyValuePair`2_int_string", StringComparison.Ordinal) == true)
                            return count == 0
                                ? I("il2cpp::il2cpp_kvp_gen_int_string_get_key", 0, true, false, true, new[] { "deref:r" })
                                : null;
                        return count == 0
                            ? I("il2cpp::il2cpp_kvp_string_int_get_key", 0, true, true, true, new[] { "deref:r" },
                                returnTypeName: "System.String")
                            : null;
                    case "get_Value":
                        // Phase-1 dictgen: the string value is a managed
                        // reference (ReturnsRef, tracked as String).
                        if (receiverType?.EndsWith("KeyValuePair`2_int_string", StringComparison.Ordinal) == true)
                            return count == 0
                                ? I("il2cpp::il2cpp_kvp_gen_int_string_get_value", 0, true, true, true, new[] { "deref:r" },
                                    returnTypeName: "System.String")
                                : null;
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
                        // G4: the string->string values snapshot enumerates
                        // through the keycollection-style wrapper (its fields
                        // live in en->keys, en->values stays null) -- the int32
                        // valuecollection walker would dereference the null
                        // values slot.
                        if (receiverType == "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator_string"
                            || receiverType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator")
                            return count == 0
                                ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                                : null;
                        return count == 0
                            ? I("il2cpp::il2cpp_dict_si_valuecollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                            : null;
                    case "get_Current":
                        // G4: the string->string values snapshot is enumerated
                        // through the keycollection-style wrapper (its tracked
                        // pseudo-type is the KeyCollection.Enumerator name), so
                        // the element is a managed reference; the plain
                        // ValueCollection track is the int32 wrapper.
                        if (receiverType == "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator_string"
                            || receiverType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator")
                            return count == 0
                                ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                                    returnTypeName: "System.String")
                                : null;
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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
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
                        // G4: the string->string expansion's get_Values returns
                        // the string[] snapshot (the same verdict as Keys), so
                        // its enumerator is the KEYCOLLECTION-style wrapper
                        // (reference elements). The tracked pseudo-type is the
                        // EXISTING KeyCollection.Enumerator name so every
                        // downstream member (MoveNext / get_Current / the
                        // foreach-finally IDisposable::Dispose) reuses the
                        // already-tracked reference-element dispatch; the int32
                        // domain keeps the int32 valuecollection wrapper. The
                        // nested generic instantiation name carries the element
                        // suffix (...ValueCollection_string_string).
                        if (instName?.EndsWith("_string_string", StringComparison.Ordinal) == true
                            || receiverType == "System.String[]")
                            return I("il2cpp::il2cpp_dict_si_keycollection_get_enumerator", 0, true, true, true, new[] { "" },
                                returnTypeName: "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator");
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
