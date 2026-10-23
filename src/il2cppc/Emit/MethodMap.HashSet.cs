namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.HashSet -- System.Collections.Generic.HashSet`1 wiring.
// Owners: System.Collections.Generic.HashSet`1 (+Enumerator)
//
// STORAGE (see src/runtime/include/internal/hashset.h): ONE slot-ordered
// carrier table for every element kind -- the net48 model (insertion order with
// LIFO free-slot reuse), which is what makes `foreach` byte-exact. The
// historical split (string->dictsi, value->boxed hashtable) enumerated in
// BUCKET order and failed probe hseta1 (oracle 3,1,7,2,5 vs 1,2,3,5,7).
//
// AOT GENERIC EXPANSION CONVENTION (mirror MethodMap.List.cs / Dictionary.cs):
// HashSet calls arrive as MemberRefs (NOT MethodSpecs -- genArgs is null) and
// the instantiation lives in the TypeSpec parent, decoded to the receiver
// instance name ("System.Collections.Generic.HashSet`1_string" / "_int" / a
// dotted user type / "System.Decimal" / ...). instName (when present) is
// AUTHORITATIVE; otherwise the folded receiver name decides; a null tag DEFERS
// (the fold re-resolves with the tracked receiver).
//
// The MemberRef signature is the GENERIC DEFINITION signature (T arrives as
// ELEMENT_TYPE_VAR 0x13 -- Add: 20 01 02 13 00 -> ps [13]; get_Count: 20 00 08;
// Clear/ctor: 20 00 01).
//
// ELEMENT FAMILIES (the icall suffix):
//   _int   int32            (carrier: boxed Int32; "i" at the C++ boundary)
//   _long  int64            ("l")           _double double ("d")
//   _uint  uint32 bits ("i")                _dec decimal (const Il2CppDecimal*)
//   _string / _ref / dotted user type -> the reference family ("")
// A user STRUCT element rides the reference family too: the transpiler boxes a
// struct element on entry (ValueType boxes are value-equal through
// icall_object_equals), so the set semantics are the net48 ones.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    // The element family of a HashSet instantiation.
    private enum HashSetKind { Ref, I4, I8, D, U4, Dec }

    private static HashSetKind? HashSetKindOf(string? instTag)
    {
        if (instTag == null) return null;
        switch (instTag)
        {
            case "System.Collections.Generic.HashSet`1_int": return HashSetKind.I4;
            case "System.Collections.Generic.HashSet`1_long": return HashSetKind.I8;
            case "System.Collections.Generic.HashSet`1_double": return HashSetKind.D;
            case "System.Collections.Generic.HashSet`1_uint": return HashSetKind.U4;
            case "System.Collections.Generic.HashSet`1_System.Decimal": return HashSetKind.Dec;
            case "System.Collections.Generic.HashSet`1_string":
            case "System.Collections.Generic.HashSet`1_object":
            case "System.Collections.Generic.HashSet`1_class":
                return HashSetKind.Ref;
        }
        // Any other CLOSED instantiation (a user class / struct / enum / a
        // nested generic): its element is a managed reference at the C++ ABI
        // (value types are boxed at the call site), so it rides the reference
        // family. The prefix check keeps `HashSet`1_` from matching the raw
        // definition name.
        if (instTag.StartsWith("System.Collections.Generic.HashSet`1_", StringComparison.Ordinal)
            && instTag.Length > "System.Collections.Generic.HashSet`1_".Length)
        {
            return HashSetKind.Ref;
        }
        return null;
    }

    // The concrete C++ parameter/return code for an element family.
    private static string HashSetCode(HashSetKind k, bool asArgument) => k switch
    {
        HashSetKind.I4 => "i",
        HashSetKind.U4 => "i",
        HashSetKind.I8 => "l",
        HashSetKind.D => "d",
        HashSetKind.Dec => "dec",
        _ => "",
    };

    private static string HashSetIcallSuffix(HashSetKind k) => k switch
    {
        HashSetKind.I4 => "int",
        HashSetKind.I8 => "long",
        HashSetKind.D => "double",
        HashSetKind.U4 => "uint",
        HashSetKind.Dec => "dec",
        _ => "ref",
    };

    // The runtime enumerator get_Current family of a tracked enumerator type.
    private static HashSetKind? HashSetEnumeratorKind(string? receiverType)
    {
        if (receiverType == null) return null;
        // The int family tracks the SHARED dictsi keycollection token (the
        // historical G4 surface); get_Current unboxes the int32.
        if (receiverType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator")
            return HashSetKind.I4;
        if (!receiverType.StartsWith("System.Collections.Generic.HashSet`1.Enumerator_", StringComparison.Ordinal))
            return null;
        var suffix = receiverType["System.Collections.Generic.HashSet`1.Enumerator_".Length..];
        return suffix switch
        {
            "int" or "System.Int32" => HashSetKind.I4,
            "long" or "System.Int64" => HashSetKind.I8,
            "double" or "System.Double" => HashSetKind.D,
            "uint" or "System.UInt32" => HashSetKind.U4,
            "System.Decimal" => HashSetKind.Dec,
            _ => HashSetKind.Ref,
        };
    }

    private static string HashSetEnumeratorTrackedType(string? instTag, HashSetKind k)
    {
        // The int family tracks the HISTORICAL dictsi keycollection token (the
        // G4 surface; get_Current unboxes the int32). Every other family
        // tracks its own token carrying the ELEMENT name, so the enumerator
        // dispatch and the downstream element type resolve.
        if (k == HashSetKind.I4) return "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator";
        if (k == HashSetKind.I8) return "System.Collections.Generic.HashSet`1.Enumerator_long";
        if (k == HashSetKind.D) return "System.Collections.Generic.HashSet`1.Enumerator_double";
        if (k == HashSetKind.U4) return "System.Collections.Generic.HashSet`1.Enumerator_uint";
        if (k == HashSetKind.Dec) return "System.Collections.Generic.HashSet`1.Enumerator_System.Decimal";
        var elem = HashSetElementName(instTag);
        return "System.Collections.Generic.HashSet`1.Enumerator_" + elem;
    }

    // The managed element type name of an instantiation ("string" ->
    // "System.String"; a dotted user type / "System.Decimal" pass through).
    private static string HashSetElementName(string? instTag)
    {
        const string prefix = "System.Collections.Generic.HashSet`1_";
        if (instTag == null || !instTag.StartsWith(prefix, StringComparison.Ordinal)) return "System.Object";
        var s = instTag[prefix.Length..];
        return s switch
        {
            "string" => "System.String",
            "object" or "class" => "System.Object",
            "int" => "System.Int32",
            "long" => "System.Int64",
            "double" => "System.Double",
            "uint" => "System.UInt32",
            _ => s,
        };
    }

    private static ResolvedMethod? TryMapHashSet(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null,
        string? paramTypes = null)
    {
        var instTag = instName ?? receiverType;
        var kind = HashSetKindOf(instTag);
        if (kind == null) return null; // DEFER

        var k = kind.Value;
        var code = HashSetCode(k, true);
        var suffix = HashSetIcallSuffix(k);

        // The T-bearing parameter of the generic DEFINITION signature.
        bool IsVarShape() => ps.Length == 1 && ps[0] == 0x13;

        ResolvedMethod Icall(string member, int pc, bool rv, bool rr, string[] casts,
            string? resultKind = null, bool appendZero = false, string? returnTypeName = null) =>
            I($"il2cpp::runtime::icall_hashset_{suffix}_{member}", pc, rv, rr, true, casts,
                resultKind, appendZero: appendZero, returnTypeName: returnTypeName);

        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return Icall("ctor", 0, false, false, Array.Empty<string>(), appendZero: true);
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08)
                    return Icall("ctor", 1, false, false, new[] { "", "i" });
                // .ctor(IEnumerable<T>) / .ctor(IEqualityComparer<T>) -- both are
                // a single GENERICINST (0x15) parameter, so the shape cannot tell
                // them apart: the decoded parameter-type text is authoritative.
                if (count == 1 && ps.Length == 1 && (ps[0] == 0x15 || ps[0] == 0x1D))
                {
                    var p0 = HashSetParamDefName(paramTypes, 0);
                    if (p0 == "System.Collections.Generic.IEqualityComparer`1")
                        return Icall("ctor_comparer", 1, false, false, new[] { "", "" });
                    if (p0 == "System.Collections.Generic.IEnumerable`1" || ps[0] == 0x1D)
                        return Icall("ctor_enum", 1, false, false, new[] { "", "" });
                }
                if (count == 2 && ps.Length == 2)
                {
                    // .ctor(int capacity, IEqualityComparer<T>)
                    if (ps[0] == 0x08 && ps[1] == 0x15)
                        return Icall("ctor_comparer", 2, false, false, new[] { "", "i", "" });
                    // .ctor(IEnumerable<T>, IEqualityComparer<T>)
                    if (ps[0] == 0x15 && ps[1] == 0x15
                        && HashSetParamDefName(paramTypes, 0) == "System.Collections.Generic.IEnumerable`1")
                        return Icall("ctor_enum_comparer", 2, false, false, new[] { "", "", "" });
                }
                return Loud(instTag, name, ps);

            case ".ctor_capacity_comparer":
                return Loud(instTag, name, ps);

            case "Add":
                if (count != 1 || !IsVarShape()) return Loud(instTag, name, ps);
                return Icall("add", 1, true, false, new[] { "", code });

            case "Remove":
                if (count != 1 || !IsVarShape()) return Loud(instTag, name, ps);
                return Icall("remove", 1, true, false, new[] { "", code });

            case "Contains":
                if (count != 1 || !IsVarShape()) return Loud(instTag, name, ps);
                return Icall("contains", 1, true, false, new[] { "", code });

            case "get_Count":
                if (count != 0) return Loud(instTag, name, ps);
                return Icall("get_count", 0, true, false, new[] { "" });

            case "Clear":
                if (count != 0) return Loud(instTag, name, ps);
                return Icall("clear", 0, false, false, new[] { "" });

            case "CopyTo":
                // CopyTo(T[]) / CopyTo(T[], int) / CopyTo(T[], int, int).
                // The 3-arg range form exists only for the int32 family; the
                // other families expose the 1/2-arg forms (the runtime copies
                // the whole set).
                if (k == HashSetKind.I4 && count == 3 && ps.Length >= 3 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_hashset_int_copy_to_range", 3, false, false, true,
                        new[] { "", "a", "i", "i" });
                if (count == 2 && ps.Length >= 2 && ps[0] == 0x1D)
                    return Icall("copy_to", 2, false, false, new[] { "", "a", "i" });
                if (count == 1 && ps.Length >= 1 && ps[0] == 0x1D)
                    return Icall("copy_to", 1, false, false, new[] { "", "a" }, appendZero: true);
                return Loud(instTag, name, ps);

            case "TryGetValue":
                // bool TryGetValue(T equalValue, out T actualValue)
                if (count != 2 || ps.Length < 2 || ps[0] != 0x13) return Loud(instTag, name, ps);
                return Icall("try_get_value", 2, true, false, new[] { "", code, "addr:" + AddrCode(k) });

            case "get_Comparer":
                if (count != 0) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_get_comparer", 0, true, true, true, new[] { "" });

            case "GetEnumerator":
                if (count != 0) return Loud(instTag, name, ps);
                return I($"il2cpp::il2cpp_hashset_{suffix}_get_enumerator", 0, true, true, true,
                    new[] { "" }, returnTypeName: HashSetEnumeratorTrackedType(instTag, k));

            // ---- set algebra: one shared runtime family (carrier-based) ----
            case "UnionWith":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_union_with", 1, false, false, true, new[] { "", "" });
            case "IntersectWith":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_intersect_with", 1, false, false, true, new[] { "", "" });
            case "ExceptWith":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_except_with", 1, false, false, true, new[] { "", "" });
            case "SymmetricExceptWith":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_symmetric_except_with", 1, false, false, true, new[] { "", "" });
            case "SetEquals":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_set_equals", 1, true, false, true, new[] { "", "" });
            case "IsSubsetOf":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_is_subset_of", 1, true, false, true, new[] { "", "" });
            case "IsSupersetOf":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_is_superset_of", 1, true, false, true, new[] { "", "" });
            case "IsProperSubsetOf":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_is_proper_subset_of", 1, true, false, true, new[] { "", "" });
            case "IsProperSupersetOf":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_is_proper_superset_of", 1, true, false, true, new[] { "", "" });
            case "Overlaps":
                if (count != 1) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_overlaps", 1, true, false, true, new[] { "", "" });
            case "RemoveWhere":
                if (count != 1 || k != HashSetKind.I4) return Loud(instTag, name, ps);
                return I("il2cpp::runtime::icall_hashset_remove_where", 1, true, false, true, new[] { "", "" });

            default:
                return Loud(instTag, name, ps);
        }
    }

    private static string AddrCode(HashSetKind k) => k switch
    {
        HashSetKind.I4 => "i",
        HashSetKind.U4 => "i",
        HashSetKind.I8 => "l",
        HashSetKind.D => "d",
        HashSetKind.Dec => "dec",
        _ => "a",
    };

    private static ResolvedMethod Loud(string? instTag, string name, byte[] ps) =>
        throw new NotSupportedException(
            $"HashSet`1 member '{name}' is not a wired expansion (receiver '{instTag ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}])");

    //--------------------------------------------------------------------------
    // HashSetParamDefName -- the generic DEFINITION name of an encoded
    // parameter type ("r:System.Collections.Generic.IEnumerable`1<...>" ->
    // "System.Collections.Generic.IEnumerable`1"). Empty when unavailable.
    //--------------------------------------------------------------------------
    private static string HashSetParamDefName(string? paramTypes, int index)
    {
        if (string.IsNullOrEmpty(paramTypes)) return "";
        var parts = CodeGenerator.SplitTopLevel(paramTypes!).ToArray();
        if (index >= parts.Length) return "";
        var s = parts[index];
        if (s.StartsWith("r:", StringComparison.Ordinal)) s = s[2..];
        var lt = s.IndexOf('<');
        return lt >= 0 ? s[..lt] : s;
    }

    //--------------------------------------------------------------------------
    // TryMapHashSetEnumerator -- the HashSet`1+Enumerator STRUCT members. The
    // runtime enumerator IS the shared keycollection enumerator object over a
    // SLOT-ORDERED snapshot (hashset.cpp SnapshotCarriers), so MoveNext /
    // Dispose reuse that domain's icalls and get_Current reads/unboxes the
    // element per family.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapHashSetEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        var kind = HashSetEnumeratorKind(receiverType);
        if (kind == null) return null;
        var k = kind.Value;
        switch (name)
        {
            case "MoveNext":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_keycollection_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                if (count != 0) return null;
                switch (k)
                {
                    case HashSetKind.I4:
                        return I("il2cpp::runtime::icall_hashset_int_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                    case HashSetKind.I8:
                        return I("il2cpp::runtime::icall_hashset_long_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                    case HashSetKind.D:
                        return I("il2cpp::runtime::icall_hashset_double_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                    case HashSetKind.U4:
                        return I("il2cpp::runtime::icall_hashset_uint_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
                    case HashSetKind.Dec:
                        return I("il2cpp::runtime::icall_hashset_dec_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                            returnTypeName: "System.Decimal");
                    default:
                        // The string / reference element passes the carrier
                        // through as a managed reference.
                        return I("il2cpp::il2cpp_dict_si_keycollection_enumerator_get_current", 0, true, true, true,
                            new[] { "deref:r" }, returnTypeName: HashSetRefElementType(receiverType));
                }
            case "Dispose":
                return count == 0
                    ? I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" })
                    : null;
            default:
                return null;
        }
    }

    // The managed element type a reference-family enumerator yields.
    private static string? HashSetRefElementType(string? receiverType) =>
        receiverType is "System.Collections.Generic.HashSet`1.Enumerator_System.String"
            or "System.Collections.Generic.HashSet`1.Enumerator_string"
            ? "System.String"
            : receiverType is null ? null : "System.Object";
}
