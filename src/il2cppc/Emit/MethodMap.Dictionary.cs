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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null,
        string? keyTypeName = null, string? valueTypeName = null)
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
        // b5-gaps section 104: the int->int expansion. The VALUE is a boxed
        // int32 (the same carrier the section-78 kernel already stores), so the
        // instance surface rides the SAME shared generic table with
        // (keyKind=1, valueKind=1): get_Item unboxes the boxed value, get_Values
        // returns a real int32[] snapshot, Add/set_Item/TryGetValue/ContainsKey/
        // Remove take the int32 key.
        var iiReceiver = instTag is "System.Collections.Generic.Dictionary`2_int_int";
        // b5-gaps section 106: the int->int64 expansion. The VALUE is a boxed
        // int64 (icall_box_long -- the same 8-byte payload slot as the boxed
        // int32), so the instance surface rides the SAME shared generic table
        // with (keyKind=1, valueKind=16): get_Item unboxes the boxed value,
        // get_Values returns a real int64[] snapshot.
        var ilReceiver = instTag is "System.Collections.Generic.Dictionary`2_int_long";
        // b5-gaps section 108: the INT64-KEY expansions. The long key rides the
        // dictgen entry's parallel keyI64 slot (kGenKind_Int64Key = 18). The
        // three instantiations share the key scan but differ in the VALUE family:
        //   long_string -> a managed reference carrier (icall_dict_gen_*_kl)
        //   long_int    -> a boxed int32 carrier   (icall_dict_gen_*_kl_i)
        //   long_long   -> a boxed int64 carrier   (icall_dict_gen_*_kl_l)
        // The value-family split is required because get_Item/Add/TryGetValue
        // must return/accept the value in its own family (a reference carrier vs
        // an unboxed int32/int64 leaf); the key-only arms (ContainsKey/Remove/
        // get_Keys) are value-family-agnostic and share klReceiver.
        var klReceiver = instTag is "System.Collections.Generic.Dictionary`2_long_int"
            || instTag is "System.Collections.Generic.Dictionary`2_long_string"
            || instTag is "System.Collections.Generic.Dictionary`2_long_long";
        var klStrReceiver = instTag is "System.Collections.Generic.Dictionary`2_long_string";
        var klIntReceiver = instTag is "System.Collections.Generic.Dictionary`2_long_int";
        var klLongReceiver = instTag is "System.Collections.Generic.Dictionary`2_long_long";
        // b5-gaps section 106: a LINQ ToDictionary result whose KEY is a string
        // and whose VALUE is an int64 -- the gen wrapper (a boxed int64 carrier).
        var slValueGenReceiver = receiverType is "System.Collections.Generic.Dictionary`2_string_long_gen";
        if (slValueGenReceiver) siReceiver = false;
        // b5-gaps section 106: the IL INSTANTIATION name (instName) of the same
        // shape -- a LINQ ToDictionary string->int64 result is tracked by the
        // returnTypeName, but a callvirt on the local may reach the dispatcher
        // with only the IL name (receiverType untracked). The instance path has
        // NO Dictionary<string,long> wrapper (the SI domain is string->int32
        // only), so routing this name to the gen arms is unambiguous.
        var slInstReceiver = instTag is "System.Collections.Generic.Dictionary`2_string_long";
        if (slInstReceiver) siReceiver = false;
        // b5-gaps section 104: a LINQ ToDictionary result whose KEY is a string
        // and whose VALUE is an int32. The LINQ kernel materialises it through
        // the DICTGEN wrapper (keysRoot + a boxed int32 value carrier), so its
        // instance surface must ride the gen icalls -- the SI domain's own
        // string->int32 layout is a DIFFERENT wrapper object (dictsi_obj.h) and
        // dispatching a gen wrapper through the si icalls read the wrong slots
        // (probe-todict-i4val segfaulted at the get_Item dispatch). The IL
        // TypeSpec parent name is IDENTICAL for both shapes, so the dispatcher
        // keys on the TRACKED receiver type (the LINQ call's dictName carries a
        // _gen suffix) to tell them apart.
        var siValueGenReceiver = receiverType is "System.Collections.Generic.Dictionary`2_string_int_gen";
        if (siValueGenReceiver) siReceiver = false;
        // Value-type tuple key: the key is a BOXED ValueTuple -- the runtime
        // reads the boxed payload structurally (net48
        // EqualityComparer<ValueTuple<..>>.Default). The element kinds come
        // from the KEY type name ("System.ValueTuple`2_int_int" -> "ii"); an
        // unsupported element (nested tuple / user struct / BCL value struct)
        // leaves the key unwired (LOUD) rather than degrading to identity
        // semantics. The value kind is 1 (int32-boxed) for an int32 value,
        // else 0 (managed reference).
        var tupleKinds = ValueTupleSupport.KeyKinds(keyTypeName);
        var tupleReceiver = tupleKinds != null;
        var tupleValueKind = valueTypeName is "int" or "System.Int32" ? "1" : "0";
        var tupleKeyCast = tupleReceiver ? "box:vt:" + tupleKinds! : "";
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"LOUD whitelist (no runtime descriptor/model): Dictionary`2 member '{name}' is not a wired expansion " +
            $"(receiver '{receiverType ?? "<untracked>"}', instName '{instName ?? "<null>"}', keyType '{keyTypeName ?? "<null>"}', " +
            $"ps [{BitConverter.ToString(ps)}]) -- only the string->int32 dictsi domain, the dictgen domains (int32->string, " +
            "string->string, Uri->string) and value-type tuple keys are wired " +
            "[Reference Source: net48 mscorlib System.Collections.Generic.Dictionary`2]");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> icall_dict_*_ctor(self, 0); the
                // injected 0 lands AFTER the receiver (AppendZeroArg --
                // Hashtable precedent). Capacity ctor -> (self, capacity).
                if (count == 0)
                    return siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_ctor", 0, false, false, true, null, null, appendZero: true)
                        : ssReceiver || nestedListReceiver || uriReceiver || isReceiver || iiReceiver || ilReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver || tupleReceiver || klReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_ctor", 0, false, false, true, null, null, appendZero: true)
                            : Loud();
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && (ssReceiver || nestedListReceiver || uriReceiver || isReceiver || iiReceiver || ilReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver || klReceiver))
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
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_add_o", 2, false, false, true, new[] { "", tupleKeyCast, "" },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_add_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_add", 2, false, false, true, new[] { "", "", "i" })
                        : slValueGenReceiver || slInstReceiver
                            // b5-gaps section 106: the LINQ string->int64
                            // wrapper -- the int64 value arrives as a VALUE
                            // slot ("l"); the runtime boxes it.
                            ? I("il2cpp::runtime::icall_dict_si_add_l", 2, false, false, true, new[] { "", "", "l" })
                            : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_add", 2, false, false, true, new[] { "", "i", "" },
                                appendLiterals: new[] { "1", "0" })
                            : iiReceiver
                                // b5-gaps section 104: the int32 value arrives as a
                                // VALUE slot ("i") -- the runtime boxes it.
                                ? I("il2cpp::runtime::icall_dict_gen_add_i", 2, false, false, true, new[] { "", "i", "i" },
                                    appendLiterals: new[] { "1", "1" })
                                    : ilReceiver
                                        // b5-gaps section 106: the int64 value arrives
                                        // as a VALUE slot ("l") -- the runtime boxes it.
                                        ? I("il2cpp::runtime::icall_dict_gen_add_l", 2, false, false, true, new[] { "", "i", "l" },
                                            appendLiterals: new[] { "1", "16" })
                                        : klStrReceiver
                                            // b5-gaps section 108: the int64 key rides
                                            // the dedicated table arm; the value is a
                                            // managed reference carrier.
                                            ? I("il2cpp::runtime::icall_dict_gen_add_kl", 2, false, false, true, new[] { "", "l", "" },
                                                appendLiterals: new[] { "18", "0" })
                                            : klIntReceiver
                                                ? I("il2cpp::runtime::icall_dict_gen_add_kl_i", 2, false, false, true, new[] { "", "l", "i" },
                                                    appendLiterals: new[] { "18", "1" })
                                                : klLongReceiver
                                                    ? I("il2cpp::runtime::icall_dict_gen_add_kl_l", 2, false, false, true, new[] { "", "l", "l" },
                                                        appendLiterals: new[] { "18", "16" })
                                                    : Loud();
            case "set_Item":
                // indexer SET: void set_Item(TKey, TValue) -- insert-or-replace
                // (same VAR [13,13] shape as Add).
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_set_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_set_o", 2, false, false, true, new[] { "", tupleKeyCast, "" },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_set_o", 2, false, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_set", 2, false, false, true, new[] { "", "", "i" })
                        : slValueGenReceiver || slInstReceiver
                            ? I("il2cpp::runtime::icall_dict_si_set_l", 2, false, false, true, new[] { "", "", "l" })
                            : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_set", 2, false, false, true, new[] { "", "i", "" },
                                appendLiterals: new[] { "1", "0" })
                            : iiReceiver
                                ? I("il2cpp::runtime::icall_dict_gen_set_i", 2, false, false, true, new[] { "", "i", "i" },
                                    appendLiterals: new[] { "1", "1" })
                                : ilReceiver
                                    ? I("il2cpp::runtime::icall_dict_gen_set_l", 2, false, false, true, new[] { "", "i", "l" },
                                        appendLiterals: new[] { "1", "16" })
                                    : klStrReceiver
                                        ? I("il2cpp::runtime::icall_dict_gen_set_kl", 2, false, false, true, new[] { "", "l", "" },
                                            appendLiterals: new[] { "18", "0" })
                                        : klIntReceiver
                                            ? I("il2cpp::runtime::icall_dict_gen_set_kl_i", 2, false, false, true, new[] { "", "l", "i" },
                                                appendLiterals: new[] { "18", "1" })
                                            : klLongReceiver
                                                ? I("il2cpp::runtime::icall_dict_gen_set_kl_l", 2, false, false, true, new[] { "", "l", "l" },
                                                    appendLiterals: new[] { "18", "16" })
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
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_o", 1, true, true, true, new[] { "", tupleKeyCast },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_o", 1, true, true, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" },
                        returnTypeName: nestedListReceiver ? "System.Collections.Generic.List`1_int" : null)
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get", 1, true, false, true, new[] { "", "" })
                        : slValueGenReceiver || slInstReceiver
                            // b5-gaps section 106: the LINQ string->int64
                            // wrapper -- the value is a boxed int64 carrier.
                            ? I("il2cpp::runtime::icall_dict_si_get_l", 1, true, false, true, new[] { "", "" })
                            : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_get", 1, true, true, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                    : siValueGenReceiver
                        // b5-gaps section 104: the LINQ string->int32 wrapper --
                        // the value is a boxed int32 carrier, unboxed to the leaf.
                        ? I("il2cpp::runtime::icall_dict_gen_get_i", 1, true, false, true,
                            new[] { "", "i" },
                            appendLiterals: new[] { "0", "1" })
                    : iiReceiver
                                    // b5-gaps section 104: the int32 value is a BOXED
                                    // carrier -- the get_Item result is unboxed back to
                                    // the int32 leaf (ReturnsRef false, ResultKind
                                    // null -> the int32 result slot).
                                    ? I("il2cpp::runtime::icall_dict_gen_get_i", 1, true, false, true, new[] { "", "i" },
                                        appendLiterals: new[] { "1", "1" })
                                    : ilReceiver
                                        // b5-gaps section 106: the int64 value is a
                                        // BOXED int64 carrier -- unboxed to the leaf.
                                        ? I("il2cpp::runtime::icall_dict_gen_get_l", 1, true, false, true, new[] { "", "i" },
                                            appendLiterals: new[] { "1", "16" })
                                        : klStrReceiver
                                            // b5-gaps section 108: the int64 key rides
                                            // the dedicated get arm; the value is a
                                            // managed reference carrier (ReturnsRef).
                                            ? I("il2cpp::runtime::icall_dict_gen_get_kl", 1, true, true, true, new[] { "", "l" },
                                                appendLiterals: new[] { "18", "0" })
                                            : klIntReceiver
                                                ? I("il2cpp::runtime::icall_dict_gen_get_kl_i", 1, true, false, true, new[] { "", "l" },
                                                    appendLiterals: new[] { "18", "1" })
                                                : klLongReceiver
                                                    ? I("il2cpp::runtime::icall_dict_gen_get_kl_l", 1, true, false, true, new[] { "", "l" },
                                                        appendLiterals: new[] { "18", "16" })
                                                    : Loud();
            case "TryGetValue":
                // bool TryGetValue(TKey, out TValue) -- the BYREF (0x10)
                // consumes its element (VAR 0x13), so ps is [13,13] like Add;
                // the out param is the ldloca ADDRESS ("" passthrough).
                if (count != 2 || ps.Length != 2 || ps[0] != 0x13 || ps[1] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_o", 2, true, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_o", 2, true, false, true, new[] { "", tupleKeyCast, "" },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_o", 2, true, false, true, new[] { "", "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_try_get_value", 2, true, false, true, new[] { "", "", "" })
                        : slValueGenReceiver || slInstReceiver
                            // b5-gaps section 106: the out value is written
                            // back through the int64 address.
                            ? I("il2cpp::runtime::icall_dict_si_try_get_value_l", 2, true, false, true,
                                new[] { "", "", "" })
                            : isReceiver
                        ? I("il2cpp::runtime::icall_dict_gen_try_get_value", 2, true, false, true, new[] { "", "", "" },
                            appendLiterals: new[] { "1", "0" })
                        : siValueGenReceiver
                            // b5-gaps section 104: the out value is written
                            // back through the int32 address (the runtime
                            // unboxes into the caller's slot).
                            ? I("il2cpp::runtime::icall_dict_gen_try_get_value_i", 2, true, false, true,
                                new[] { "", "i", "deref:i" },
                                appendLiterals: new[] { "0", "1" })
                            : iiReceiver
                                // b5-gaps section 104: the out value is written
                                // back through the int32 address (the runtime
                                // unboxes into the caller's slot).
                                ? I("il2cpp::runtime::icall_dict_gen_try_get_value_i", 2, true, false, true,
                                    new[] { "", "i", "deref:i" },
                                    appendLiterals: new[] { "1", "1" })
                                : ilReceiver
                                    // b5-gaps section 106: the out value is written
                                    // back through the int64 address (the address
                                    // passes through -- the runtime writes the slot).
                                    ? I("il2cpp::runtime::icall_dict_gen_try_get_value_l", 2, true, false, true,
                                        new[] { "", "i", "" },
                                        appendLiterals: new[] { "1", "16" })
                                    : klStrReceiver
                                        ? I("il2cpp::runtime::icall_dict_gen_try_get_value_kl", 2, true, false, true,
                                            new[] { "", "l", "" },
                                            appendLiterals: new[] { "18", "0" })
                                        : klIntReceiver
                                            ? I("il2cpp::runtime::icall_dict_gen_try_get_value_kl_i", 2, true, false, true,
                                                new[] { "", "l", "" },
                                                appendLiterals: new[] { "18", "1" })
                                            : klLongReceiver
                                                ? I("il2cpp::runtime::icall_dict_gen_try_get_value_kl_l", 2, true, false, true,
                                                    new[] { "", "l", "" },
                                                    appendLiterals: new[] { "18", "16" })
                                                : Loud();
            case "ContainsKey":
                // bool ContainsKey(TKey): ps [0x13] (dictcase 20 01 02 13 00).
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", tupleKeyCast },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_contains_key", 1, true, false, true, new[] { "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_contains_key", 1, true, false, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                            : siValueGenReceiver
                                ? I("il2cpp::runtime::icall_dict_gen_contains_key_o", 1, true, false, true, new[] { "", "" },
                                    appendLiterals: new[] { "0", "1" })
                                : iiReceiver
                                    ? I("il2cpp::runtime::icall_dict_gen_contains_key", 1, true, false, true, new[] { "", "i" },
                                        appendLiterals: new[] { "1", "1" })
                                    : ilReceiver
                                        ? I("il2cpp::runtime::icall_dict_gen_contains_key", 1, true, false, true, new[] { "", "i" },
                                            appendLiterals: new[] { "1", "16" })
                                        : klReceiver
                                            ? I("il2cpp::runtime::icall_dict_gen_contains_key_kl", 1, true, false, true, new[] { "", "l" },
                                                appendLiterals: new[] { "18", "0" })
                                            : Loud();
            case "Remove":
                // bool Remove(TKey): ps [0x13] (same shape as ContainsKey).
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                return uriReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "2", "0" })
                    : tupleReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", tupleKeyCast },
                        appendLiterals: new[] { "3", tupleValueKind })
                    : ssReceiver || nestedListReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", "" },
                        appendLiterals: new[] { "0", "0" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_remove", 1, true, false, true, new[] { "", "" })
                        : isReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_remove", 1, true, false, true, new[] { "", "i" },
                                appendLiterals: new[] { "1", "0" })
                            : siValueGenReceiver
                                ? I("il2cpp::runtime::icall_dict_gen_remove_o", 1, true, false, true, new[] { "", "" },
                                    appendLiterals: new[] { "0", "1" })
                                : iiReceiver
                                    ? I("il2cpp::runtime::icall_dict_gen_remove", 1, true, false, true, new[] { "", "i" },
                                        appendLiterals: new[] { "1", "1" })
                                    : ilReceiver
                                        ? I("il2cpp::runtime::icall_dict_gen_remove", 1, true, false, true, new[] { "", "i" },
                                            appendLiterals: new[] { "1", "16" })
                                        : klReceiver
                                            ? I("il2cpp::runtime::icall_dict_gen_remove_kl", 1, true, false, true, new[] { "", "l" },
                                                appendLiterals: new[] { "18", "0" })
                                            : Loud();
            case "get_Count":
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver || uriReceiver || isReceiver || iiReceiver || ilReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver || tupleReceiver || klReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_count", 0, true, false, true, new[] { "" })
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get_count", 0, true, false, true, new[] { "" })
                        : Loud();
            case "Clear":
                if (count != 0) return Loud();
                return ssReceiver || nestedListReceiver || uriReceiver || isReceiver || iiReceiver || ilReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver || tupleReceiver || klReceiver
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
                return ssReceiver || nestedListReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver
                    ? I("il2cpp::runtime::icall_dict_gen_get_keys_o", 0, true, true, true, new[] { "" },
                        appendLiterals: new[] { "0" },
                        returnTypeName: "System.String[]")
                    : siReceiver
                        ? I("il2cpp::runtime::icall_dict_si_get_keys", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.String[]")
                        // b5-gaps section 108: the int64-key snapshot (a real
                        // int64[] -- net48's Dictionary<long,T>.Keys layout).
                        : klReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_get_keys_kl", 0, true, true, true, new[] { "" },
                                returnTypeName: "System.Int64[]")
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
                        // b5-gaps section 104: the int32 value snapshot (the
                        // icall twin reads the wrapper slot itself).
                        : siValueGenReceiver || iiReceiver
                            ? I("il2cpp::runtime::icall_dict_gen_get_values_i", 0, true, true, true, new[] { "" },
                                returnTypeName: "System.Int32[]")
                            // b5-gaps section 108: the int64-key expansions. The
                            // value snapshot family follows the VALUE carrier: a
                            // reference (string[]), a boxed int32 (int32[]), or a
                            // boxed int64 (int64[]).
                            : klStrReceiver
                                ? I("il2cpp::runtime::icall_dict_gen_get_values_o", 0, true, true, true, new[] { "" },
                                    returnTypeName: "System.String[]")
                                : klIntReceiver
                                    ? I("il2cpp::runtime::icall_dict_gen_get_values_i", 0, true, true, true, new[] { "" },
                                        returnTypeName: "System.Int32[]")
                                    : klLongReceiver
                                        ? I("il2cpp::runtime::icall_dict_gen_get_values_l", 0, true, true, true, new[] { "" },
                                            returnTypeName: "System.Int64[]")
                                        // b5-gaps section 106: the int64 value snapshot (a
                                        // real int64[] -- net48's Dictionary<int,long>.Values).
                                        : ilReceiver || slValueGenReceiver || slInstReceiver
                                            ? I("il2cpp::runtime::icall_dict_gen_get_values_l", 0, true, true, true, new[] { "" },
                                                returnTypeName: "System.Int64[]")
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
                    : count == 0 && (isReceiver || iiReceiver || ilReceiver || siValueGenReceiver || slValueGenReceiver || slInstReceiver || klReceiver)
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
            // sorte1: the SortedDictionary<string,int> / SortedList<string,int>
            // foreach pair-enumerator is the SAME shared object (the tracked
            // "Dictionary`2.Enumerator" receiver), so it reuses the dictsi walk.
            case "System.Collections.Generic.SortedDictionary`2+Enumerator":
            case "System.Collections.Generic.SortedList`2+Enumerator":
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
