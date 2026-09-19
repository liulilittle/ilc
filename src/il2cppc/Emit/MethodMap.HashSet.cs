namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.HashSet -- System.Collections.Generic.HashSet`1 wiring
// (the string AOT expansion hset-b23 D-HSET-1 + the G4 int expansion).
// Owners: System.Collections.Generic.HashSet`1 (+Enumerator)
//
// DISPOSITION: WIRED for HashSet<string> (the runtime hashset.h wrapper over
// the dictsi domain) and HashSet<int> (G4: the non-generic hashtable object
// model with BOXED int32 members -- value hash/equality, rooted entries).
//
// AOT GENERIC EXPANSION CONVENTION (mirror MethodMap.List.cs / Dictionary.cs):
// HashSet calls arrive as MemberRefs (NOT MethodSpecs -- genArgs is null) and
// the instantiation lives in the TypeSpec parent, decoded to the receiver
// instance name "System.Collections.Generic.HashSet`1_string" / "_int" by
// GenericInstantiationName. The instName (when present) is AUTHORITATIVE and
// disambiguates the FIRST pass; otherwise the folded receiver name decides,
// and a null tag keeps the historical string mapping.
//
// The MemberRef signature is the GENERIC DEFINITION signature (T arrives as
// ELEMENT_TYPE_VAR 0x13 -- probe-verified net48 hsprobe blob 20 01 02 13 00
// -> ps [13]; get_Count 20 00 08; Clear/ctor 20 00 01).
//
// RECEIVER/ARGS: the receiver rides in args[0] as a managed reference ("").
// The string member is Il2CppObject* (""); the int member is int32 ("i").
//
// GC CONTRACT: HashSet<string> does NOT root its members (dictsi caller
// contract -- the e2e uses interned literals); HashSet<int> stores boxed ints
// in the hashtable, whose entries ROOT every key.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHashSet(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        // The TypeSpec instantiation is authoritative (the List/Dictionary
        // precedent); otherwise the folded receiver name decides.
        var instTag = instName ?? receiverType;
        var stringReceiver = instTag is null or "System.Collections.Generic.HashSet`1_string";
        var intReceiver = instTag is "System.Collections.Generic.HashSet`1_int";
        var longReceiver = instTag is "System.Collections.Generic.HashSet`1_long";
        var doubleReceiver = instTag is "System.Collections.Generic.HashSet`1_double";
        var uintReceiver = instTag is "System.Collections.Generic.HashSet`1_uint";
        var objectReceiver = instTag is "System.Collections.Generic.HashSet`1_object";
        var classReceiver = instTag is "System.Collections.Generic.HashSet`1_class";
        var refReceiver = objectReceiver || classReceiver;
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"HashSet`1 member '{name}' is not a wired expansion (receiver '{instTag ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only the string-member and int-member set domains are wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> icall_hashset_*_ctor(self, 0); the
                // injected 0 lands AFTER the receiver (AppendZeroArg).
                // Capacity ctor -> (self, capacity).
                if (count == 0)
                {
                    if (intReceiver)
                        return I("il2cpp::runtime::icall_hashset_int_ctor", 0, false, false, true, null, null, appendZero: true);
                    if (longReceiver)
                        return I("il2cpp::runtime::icall_hashset_long_ctor", 0, false, false, true, null, null, appendZero: true);
                    if (doubleReceiver)
                        return I("il2cpp::runtime::icall_hashset_double_ctor", 0, false, false, true, null, null, appendZero: true);
                    if (uintReceiver)
                        return I("il2cpp::runtime::icall_hashset_uint_ctor", 0, false, false, true, null, null, appendZero: true);
                    if (refReceiver)
                        return I("il2cpp::runtime::icall_hashset_ref_ctor", 0, false, false, true, null, null, appendZero: true);
                    if (stringReceiver)
                        return I("il2cpp::runtime::icall_hashset_string_ctor", 0, false, false, true, null, null, appendZero: true);
                    return Loud();
                }
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08)
                {
                    if (intReceiver)
                        return I("il2cpp::runtime::icall_hashset_int_ctor", 1, false, false, true, new[] { "", "i" });
                    if (longReceiver)
                        return I("il2cpp::runtime::icall_hashset_long_ctor", 1, false, false, true, new[] { "", "i" });
                    if (doubleReceiver)
                        return I("il2cpp::runtime::icall_hashset_double_ctor", 1, false, false, true, new[] { "", "i" });
                    if (uintReceiver)
                        return I("il2cpp::runtime::icall_hashset_uint_ctor", 1, false, false, true, new[] { "", "i" });
                    if (refReceiver)
                        return I("il2cpp::runtime::icall_hashset_ref_ctor", 1, false, false, true, new[] { "", "i" });
                    if (stringReceiver)
                        return I("il2cpp::runtime::icall_hashset_string_ctor", 1, false, false, true, new[] { "", "i" });
                    return Loud();
                }
                return Loud();
            case "Add":
                // bool Add(T): the generic DEFINITION signature -- ps [0x13].
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (intReceiver)
                    return I("il2cpp::runtime::icall_hashset_int_add", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_hashset_long_add", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_hashset_double_add", 1, true, false, true, new[] { "", "d" });
                if (uintReceiver)
                    return I("il2cpp::runtime::icall_hashset_uint_add", 1, true, false, true, new[] { "", "i" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_hashset_ref_add", 1, true, false, true, new[] { "", "" });
                if (stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_add", 1, true, false, true, new[] { "", "" });
                return Loud();
            case "Contains":
                // bool Contains(T): same ps [0x13] shape as Add.
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (intReceiver)
                    return I("il2cpp::runtime::icall_hashset_int_contains", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_hashset_long_contains", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_hashset_double_contains", 1, true, false, true, new[] { "", "d" });
                if (uintReceiver)
                    return I("il2cpp::runtime::icall_hashset_uint_contains", 1, true, false, true, new[] { "", "i" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_hashset_ref_contains", 1, true, false, true, new[] { "", "" });
                if (stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_contains", 1, true, false, true, new[] { "", "" });
                return Loud();
            case "Remove":
                // bool Remove(T): same ps [0x13] shape as Add.
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (intReceiver)
                    return I("il2cpp::runtime::icall_hashset_int_remove", 1, true, false, true, new[] { "", "i" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_hashset_long_remove", 1, true, false, true, new[] { "", "l" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_hashset_double_remove", 1, true, false, true, new[] { "", "d" });
                if (uintReceiver)
                    return I("il2cpp::runtime::icall_hashset_uint_remove", 1, true, false, true, new[] { "", "i" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_hashset_ref_remove", 1, true, false, true, new[] { "", "" });
                if (stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_remove", 1, true, false, true, new[] { "", "" });
                return Loud();
            case "get_Count":
                if (count != 0) return Loud();
                if (intReceiver)
                    return I("il2cpp::runtime::icall_hashset_int_get_count", 0, true, false, true, new[] { "" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_hashset_long_get_count", 0, true, false, true, new[] { "" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_hashset_double_get_count", 0, true, false, true, new[] { "" });
                if (uintReceiver)
                    return I("il2cpp::runtime::icall_hashset_uint_get_count", 0, true, false, true, new[] { "" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_hashset_ref_get_count", 0, true, false, true, new[] { "" });
                if (stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_get_count", 0, true, false, true, new[] { "" });
                return Loud();
            case "Clear":
                if (count != 0) return Loud();
                if (intReceiver)
                    return I("il2cpp::runtime::icall_hashset_int_clear", 0, false, false, true, new[] { "" });
                if (longReceiver)
                    return I("il2cpp::runtime::icall_hashset_long_clear", 0, false, false, true, new[] { "" });
                if (doubleReceiver)
                    return I("il2cpp::runtime::icall_hashset_double_clear", 0, false, false, true, new[] { "" });
                if (uintReceiver)
                    return I("il2cpp::runtime::icall_hashset_uint_clear", 0, false, false, true, new[] { "" });
                if (refReceiver)
                    return I("il2cpp::runtime::icall_hashset_ref_clear", 0, false, false, true, new[] { "" });
                if (stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_clear", 0, false, false, true, new[] { "" });
                return Loud();
            case "GetEnumerator":
                // G4: the int expansion enumerates the boxed-int keys snapshot
                // through the dictsi keycollection wrapper; the tracked
                // pseudo-type REUSES the KeyCollection.Enumerator token (the
                // runtime object IS that wrapper, and the token is already
                // routed by the foreach-finally IDisposable arm), so the
                // struct members dispatch via TryMapHashSetEnumerator.
                if (count != 0) return null;
                if (intReceiver)
                    return I("il2cpp::il2cpp_hashset_int_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator");
                if (longReceiver)
                    return I("il2cpp::il2cpp_hashset_long_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.HashSet`1.Enumerator_long");
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_hashset_double_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.HashSet`1.Enumerator_double");
                if (uintReceiver)
                    return I("il2cpp::il2cpp_hashset_uint_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.HashSet`1.Enumerator_uint");
                if (refReceiver)
                    return I("il2cpp::il2cpp_hashset_ref_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.HashSet`1.Enumerator_ref");
                if (stringReceiver)
                    return I("il2cpp::il2cpp_hashset_string_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.Generic.HashSet`1.Enumerator_string");
                return null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapHashSetEnumerator -- the HashSet<int>+Enumerator STRUCT members.
    // The runtime enumerator IS the dictsi keycollection enumerator object
    // (boxed-int snapshot in its keys slot), so MoveNext/Dispose reuse that
    // domain's icalls and get_Current unboxes the element.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapHashSetEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The HashSet<int> GetEnumerator tracks the dictsi keycollection token
        // (get_Current unboxes the int32). G4 B2 (b2b8): the HashSet<string>
        // expansion tracks its own token; its runtime object is the SAME dictsi
        // keycollection wrapper and get_Current passes the string reference.
        var isIntEnum = receiverType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator";
        var isStringEnum = receiverType == "System.Collections.Generic.HashSet`1.Enumerator_string";
        var isLongEnum = receiverType == "System.Collections.Generic.HashSet`1.Enumerator_long";
        var isDoubleEnum = receiverType == "System.Collections.Generic.HashSet`1.Enumerator_double";
        var isUintEnum = receiverType == "System.Collections.Generic.HashSet`1.Enumerator_uint";
        var isRefEnum = receiverType == "System.Collections.Generic.HashSet`1.Enumerator_ref";
        if (!isIntEnum && !isStringEnum && !isLongEnum && !isDoubleEnum && !isUintEnum && !isRefEnum)
            return null;
        switch (name)
        {
            case "MoveNext":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                if (count != 0) return null;
                if (isStringEnum)
                    return I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.String");
                if (isLongEnum)
                    return I("il2cpp::runtime::icall_hashset_long_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (isDoubleEnum)
                    return I("il2cpp::runtime::icall_hashset_double_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (isUintEnum)
                    return I("il2cpp::runtime::icall_hashset_uint_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                if (isRefEnum)
                    return I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: "System.Object");
                return I("il2cpp::runtime::icall_hashset_int_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
            case "Dispose":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                    : null;
            default:
                return null;
        }
    }
}
