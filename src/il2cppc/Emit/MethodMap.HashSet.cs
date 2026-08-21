namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.HashSet -- System.Collections.Generic.HashSet`1 wiring
// (the string AOT expansion, hset-b23 D-HSET-1).
// Owners: System.Collections.Generic.HashSet`1
//
// DISPOSITION (hset-b23, 2026-08-12): WIRED for the HashSet<string>
// instantiation ONLY -- the runtime surface is hashset.h (the GC-OBJECT
// wrapper, set-of-unique-members over the dictsi domain):
// System_Collections_Generic_HashSet_1_string_class holds the raw
// Il2CppDictSI* in a NON-reference slot (refFieldBitmap 0), and the
// il2cpp::runtime::icall_hashset_string_* wrappers forward to the raw dictsi
// domain with the set-of-unique-members semantics (Add returns false for a
// duplicate, never throws; Remove/Contains by ordinal content equality).
//
// AOT GENERIC EXPANSION CONVENTION (mirror MethodMap.List.cs / Dictionary.cs):
// HashSet calls arrive as MemberRefs (NOT MethodSpecs -- genArgs is null) and
// the instantiation lives ONLY in the TypeSpec parent, decoded to the receiver
// instance name "System.Collections.Generic.HashSet`1_string" by
// GenericInstantiationName. The string-ness is therefore enforced by:
//   1. the folded receiver instance name (when tracked) must be the string
//      expansion; any other name -> LOUD,
//   2. the parameter count + signature bytes: the MemberRef signature is the
//      GENERIC DEFINITION signature (T arrives as ELEMENT_TYPE_VAR 0x13 --
//      probe-verified net48 hsprobe blobs, hset-b23). Add/Contains/Remove are
//      `20 01 02 13 00` -> ps [13] (the VAR index is consumed by
//      ReadParamByte); get_Count is `20 00 08`; Clear and the parameterless
//      .ctor are `20 00 01`.
//
// RECEIVER/ARGS (instance icalls, hashtable/dictsi precedent): the receiver
// rides in args[0] as a managed reference ("" cast); the string member arrives
// as Il2CppObject* ("" cast -- the icall casts internally); the bool/int32
// returns are the runtime's primitive return (no extra casts).
//
// GC CONTRACT (see hashset.h): the wrapper does NOT root its members -- the
// caller keeps every stored member reachable. The e2e (hashsetdeep) members
// are string LITERALS interned by the g_strPool (rooted for the process
// lifetime), so the set's raw key pointers never dangle.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHashSet(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The fold-tracked receiver instance name (when present) must be the
        // string expansion HashSet`1_string.
        var stringReceiver = receiverType is null or "System.Collections.Generic.HashSet`1_string";
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"HashSet`1 member '{name}' is not the HashSet<string> expansion (receiver '{receiverType ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only the string-member set domain is wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> icall_hashset_string_ctor(self, 0);
                // the injected 0 lands AFTER the receiver (AppendZeroArg --
                // Dictionary precedent). Capacity ctor -> (self, capacity).
                if (count == 0)
                    return stringReceiver
                        ? I("il2cpp::runtime::icall_hashset_string_ctor", 0, false, false, true, null, null, appendZero: true)
                        : Loud();
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && stringReceiver)
                    return I("il2cpp::runtime::icall_hashset_string_ctor", 1, false, false, true, new[] { "", "i" });
                return Loud();
            case "Add":
                // bool Add(T): the generic DEFINITION signature -- ps [0x13]
                // (VAR index consumed by ReadParamByte). Concrete hsprobe blob
                // 20 01 02 13 00.
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && stringReceiver
                    ? I("il2cpp::runtime::icall_hashset_string_add", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "Contains":
                // bool Contains(T): same ps [0x13] shape as Add.
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && stringReceiver
                    ? I("il2cpp::runtime::icall_hashset_string_contains", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "Remove":
                // bool Remove(T): same ps [0x13] shape as Add.
                return count == 1 && ps.Length == 1 && ps[0] == 0x13 && stringReceiver
                    ? I("il2cpp::runtime::icall_hashset_string_remove", 1, true, false, true, new[] { "", "" })
                    : Loud();
            case "get_Count":
                return count == 0 && stringReceiver
                    ? I("il2cpp::runtime::icall_hashset_string_get_count", 0, true, false, true, new[] { "" })
                    : Loud();
            case "Clear":
                return count == 0 && stringReceiver
                    ? I("il2cpp::runtime::icall_hashset_string_clear", 0, false, false, true, new[] { "" })
                    : Loud();
            default:
                return null;
        }
    }
}
