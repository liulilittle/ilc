namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.List -- System.Collections.Generic.List`1 (int32 AOT expansion)
// icall mappings (il2cpp_list_i4_*, see src/runtime/include/internal/listi4.h).
// Owners: System.Collections.Generic.List`1
//
// AOT generic expansion convention: only the List<int32> instantiation has a
// runtime icall domain. List<T> calls arrive as MemberRefs (NOT MethodSpecs --
// genArgs is always null), and the T-bearing parameters appear in the
// signature as ELEMENT_TYPE_VAR (0x13), NOT as the instantiated type byte
// (probe of genlist.exe: Add sig=20 01 01 13 00, get_Item sig=20 01 13 00 08
// with parent TypeSpec[15 12 ... 08]). The instantiation itself lives only in
// the TypeSpec parent, which TryMapList does not see -- so the int32-ness is
// enforced by:
//   1. the folded receiver instance name ("System.Collections.Generic.List`1_int",
//      when the fold tracked one); any other name -> LOUD,
//   2. the parameter COUNT (decoded reliably from the signature header),
//   3. the T-bearing parameter shape 0x13 (VAR) where one exists.
// A non-VAR T shape (a signature that cannot be the List`1 definition
// signature) -> LOUD. (Note: get_Item's RETURN type is T (0x13) -- its VAR
// index leaks into ps as a 0x00 artifact because RawSignature's ret handling
// does not consume a VAR-return index; get_Item therefore guards on count
// only. A List<string> instance cannot survive transpilation anyway: the
// GenericInstantiationName TypeSpec decoder accepts only 0x08 int.)
//
// Instance methods: the receiver rides in a managed reference slot ("" cast);
// value parameters cast with "i". Method names are unique within List`1.
//
// Wired surface (D-LISTQ-1..5: 2026-08-12): .ctor(parameterless + capacity),
// get_Count / Clear / Add / get_Item / set_Item / RemoveAt / Insert / Remove /
// IndexOf / Contains / ToArray. Capacity ctor, set_Item, Insert, Remove and
// ToArray map to their listi4.cpp icalls (set_Item/Insert/Remove/ToArray were
// implemented in the runtime but unreachable -- no TryMapList arm; the
// capacity ctor runtime icall was added in the same batch).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapList(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The fold-tracked receiver instance name (when present) must be the int32 AOT
        // expansion name List`1_int.
        var i4Receiver = receiverType is null or "System.Collections.Generic.List`1_int";
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"List`1 member '{name}' is not the List<int32> AOT expansion (receiver '{receiverType ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only il2cpp_list_i4_* is wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> il2cpp_list_i4_create (the runtime
                // create icall takes NO self and RETURNS the new object --
                // Queue/Stack/ArrayList convention; the newobj temp is
                // dropped). Capacity ctor (List<int>(int32)) ->
                // il2cpp_list_i4_create_with_capacity (D-LISTQ-5): the
                // capacity arg is the only T-free parameter, so the int32
                // shape is ps[0]==0x08 + the i4Receiver gate.
                if (count == 0)
                    return I("il2cpp::il2cpp_list_i4_create", 0, true, true, true, new[] { "drop" });
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && i4Receiver)
                    return I("il2cpp::il2cpp_list_i4_create_with_capacity", 1, true, true, true, new[] { "drop", "i" });
                throw new NotSupportedException($"List`1 ctor with {count} arg(s) not wired (the runtime has only the parameterless il2cpp_list_i4_create and the capacity ctor)");
            case "get_Count":
                return count == 0 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_get_count", 0, true, false, true)
                    : Loud();
            case "Clear":
                return count == 0 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_clear", 0, false, false, true)
                    : Loud();
            case "Add":
                // void Add(T): the single T parameter arrives as VAR (0x13).
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_add", 1, false, false, true, new[] { "", "i" })
                    : Loud();
            case "get_Item":
                // T get_Item(int32): the return T (VAR) index leaks into ps
                // (RawSignature does not consume a VAR-return index), so the
                // shape check is count-only.
                return count == 1 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_get_item", 1, true, false, true, new[] { "", "i" })
                    : Loud();
            case "set_Item":
                // D-LISTQ-1: void set_Item(int32 index, T value) -- the index
                // is T-free int32 (0x08) but the value is T and arrives as VAR
                // (0x13), like Add's value (the header's VAR convention); the
                // shape is 08-13 + the i4Receiver gate.
                return count == 2 && ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_set_item", 2, false, false, true, new[] { "", "i", "i" })
                    : Loud();
            case "Insert":
                // D-LISTQ-2: void Insert(int32 index, T value) -- index is
                // int32 (0x08), value is T (0x13), exactly like set_Item.
                return count == 2 && ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_insert", 2, false, false, true, new[] { "", "i", "i" })
                    : Loud();
            case "Remove":
                // D-LISTQ-3: bool Remove(T value) -- the value parameter
                // arrives as VAR (0x13), like Add/IndexOf/Contains.
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_remove", 1, true, false, true, new[] { "", "i" })
                    : Loud();
            case "ToArray":
                // D-LISTQ-4: T[] ToArray() -- count-only guard (the SZARRAY
                // return's element byte is consumed by RawSignature). The
                // result is tracked as "System.Int32[]" so a downstream
                // Length/indexer/foreach on the array resolves (linq ToArray
                // precedent).
                return count == 0 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_to_array", 0, true, true, true, returnTypeName: "System.Int32[]")
                    : Loud();
            case "RemoveAt":
                // void RemoveAt(int32): the index parameter is plain int32.
                return count == 1 && ps.Length == 1 && ps[0] == 0x08 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_remove_at", 1, false, false, true, new[] { "", "i" })
                    : Loud();
            case "IndexOf":
                // int32 IndexOf(T): the T parameter arrives as VAR (0x13).
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_index_of", 1, true, false, true, new[] { "", "i" })
                    : Loud();
            case "Contains":
                // bool Contains(T): the T parameter arrives as VAR (0x13).
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && i4Receiver
                    ? I("il2cpp::il2cpp_list_i4_contains", 1, true, false, true, new[] { "", "i" })
                    : Loud();
            default: return null;
        }
    }
}
