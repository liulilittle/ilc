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
            default:
                return null;
        }
    }
}
