namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Array -- System.Array icall mappings (il2cpp_array.h + the
// generic-instantiation surface: Array.Sort<T>/BinarySearch<T>/IndexOf<T>/
// LastIndexOf<T> dispatch on the generic argument type bytes).
// Owners: System.Array
//
// Domains: the icall layer (il2cpp_array_ext.h / _ext2.h), the ArraySort
// direct-call domain (arraysort.h, see MethodMap.ArraySort.cs) and -- since
// ARRAYEXTRA (2026-08-22) -- the arrayextra.h direct-call domain for the
// boxed element accessors (GetValue/SetValue; runtime element-kind dispatch)
// plus the ConstrainedCopy alias of icall_array_copy (validation fully
// precedes mutation there, which IS the constrained contract).
//
// Dispatch: the generic Sort/IndexOf/BinarySearch instantiations whose
// element type has NO icall-layer entry YIELD here (return null) to the
// ArraySort direct-call domain (MethodMap.ArraySort.cs, arraysort.h): uint32/
// int64 sorts, int64/string index-of, int64 binary-search. Everything the
// direct-call domain cannot map stays LOUD (NotSupportedException), so no
// shape ever falls through to the bogus generated-name path.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static bool SecondIsArrayParam(string? paramTypes)
    {
        var t1 = SortedSetParamDefName(paramTypes, 1);
        return t1 == "System.Array" || (t1 != null && t1.StartsWith("SZARRAY", StringComparison.Ordinal));
    }

    // BATCH4: the Array.ConvertAll<TInput,TOutput> converter entry name, or null
    // when the family pair has no converter icall. The families are the ones
    // the runtime's general converter template covers (int32 / int64 / double /
    // the reference families); float has no converter icall yet.
    private static string? ConvertAllEntry(byte ti, byte to)
    {
        static string? InSuffix(byte b) => b switch
        {
            0x08 => "i4",
            0x0A or 0x0B => "i8",
            0x0D => "d",
            // arrgen1b: the float converter family (the double-slot source ABI).
            0x0C => "f",
            // Reference-element families (0x15 GENERICINST is NOT included:
            // the blob does not say whether the instantiation is a value or a
            // reference type -- the dynamic fallback / class-3 whitelist owns
            // it).
            0x0E or 0x12 or 0x13 or 0x1C or 0x1D or 0x1E or 0x14 or 0x1B => "str",
            _ => null,
        };
        static string? OutSuffix(byte b) => b switch
        {
            0x08 => "i4",
            0x0A or 0x0B => "i8",
            0x0D => "d",
            0x0C => "f",
            0x0E or 0x12 or 0x13 or 0x1C or 0x1D or 0x1E or 0x14 or 0x1B => "str",
            _ => null,
        };
        var i = InSuffix(ti);
        var o = OutSuffix(to);
        return i == null || o == null ? null : $"il2cpp::runtime::icall_array_convert_all_{i}_{o}";
    }

    // arrgen1b: the narrow-integral element families (bool/char/sbyte/byte/
    // short/ushort/uint) ride the int32 predicate ABI (MethodSignatureCoder
    // "b"/"c"/"i1"/"u1"/"i2"/"u2"/"i"); the suffix selects the matching
    // runtime kernel (il2cpp_array_ext2.h). null = not a narrow family.
    private static string? NarrowSuffix(byte elem) => elem switch
    {
        0x02 => "_b",
        0x03 => "_c",
        0x04 => "_i1",
        0x05 => "_u1",
        0x06 => "_i2",
        0x07 => "_u2",
        0x09 => "_u4",
        _ => null,
    };

    // Element-family classification for the generic Array surface. A reference
    // family element is an Il2CppObject* slot (string/object/class/array or a
    // closed reference generic instantiation); a narrow integral rides the
    // int32 IL slot with a 1/2-byte array cell; IntPtr/UIntPtr ride the native
    // 8-byte cell. 0x15 (GENERICINST) is deliberately NOT a reference family:
    // the MethodSpec blob does not say whether the instantiation is a value or
    // a reference type, so those shapes stay LOUD (class 2/3).
    private static bool IsRefElemFamily(byte b) =>
        b is 0x0E or 0x12 or 0x1C or 0x13 or 0x1E or 0x1D or 0x14 or 0x1B;

    private static bool IsNarrowElemFamily(byte b) =>
        b is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07;

    // The narrow kind literal consumed by the arraysort.cpp kernels:
    // 8 bool, 9 char, 10 sbyte, 11 byte, 12 short, 13 ushort.
    private static string? NarrowKindLiteral(byte b) => b switch
    {
        0x02 => "8",
        0x03 => "9",
        0x04 => "10",
        0x05 => "11",
        0x06 => "12",
        0x07 => "13",
        _ => null,
    };

    // ConvertAll dynamic-kernel kind literal (arrayext2.cpp
    // icall_array_convert_all_dyn): 0 ref, 1 i4, 2 i8, 3 f, 4 d, 8..13 the
    // narrow integrals. null = no materialized converter cell.
    private static string? ConvKindLiteral(byte b)
    {
        if (b is 0x0E or 0x12 or 0x1C or 0x13 or 0x1D or 0x1E or 0x14 or 0x1B) return "0";
        if (b == 0x08) return "1";
        if (b is 0x0A or 0x0B) return "2";
        if (b == 0x0C) return "3";
        if (b == 0x0D) return "4";
        return NarrowKindLiteral(b);
    }

    // 0x11 value types whose array cell the runtime already models as an
    // int64 tick/word (DateTime/TimeSpan) or as the native pointer cell
    // (IntPtr/UIntPtr) can ride the int64/uint64 kernels. Every OTHER 0x11
    // value type has no materialized array element kind/descriptor and stays
    // LOUD (class 3).
    private static bool IsInt64CellValueType(byte[]? genArgs, string[]? genArgNames) =>
        genArgs is { Length: >= 1 } && genArgs[0] == 0x11
        && genArgNames is { Length: >= 1 }
        && genArgNames[0] is "System.DateTime" or "System.TimeSpan";

    private static bool IsUInt64CellValueType(byte[]? genArgs, string[]? genArgNames) =>
        genArgs is { Length: >= 1 } && genArgs[0] == 0x19
        && genArgNames is { Length: >= 1 }
        && genArgNames[0] == "System.UIntPtr";

    private static bool IsInt64Cell(byte[]? genArgs, string[]? genArgNames) =>
        genArgs is { Length: >= 1 }
        && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames));

    private static ResolvedMethod? TryMapArray(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string[]? genArgNames, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? paramTypes = null)
    {
        switch (name)
        {
            case "Sort":
                if (genArgs != null)
                {
                    // nb9 D2/D3: Sort<TKey,TValue>(keys, items[, comparer]) has
                    // TWO generic arguments; the single-array overloads have
                    // one. The parameter COUNT alone cannot tell the 2-arg
                    // keys/items form from Sort<T>(T[], Comparison<T>), so the
                    // generic-argument count is checked FIRST.
                    if (genArgs.Length >= 2)
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_sort_keys_items_default", 2, false, false, false,
                                new[] { "a", "a" });
                        if (count == 3)
                            return I("il2cpp::il2cpp_array_sort_keys_items", 3, false, false, false,
                                new[] { "a", "a", "" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_sort_keys_items_default_range", 4, false, false, false,
                                new[] { "a", "a", "i", "i" });
                        if (count == 5)
                            return I("il2cpp::il2cpp_array_sort_keys_items_range", 5, false, false, false,
                                new[] { "a", "a", "i", "i", "" });
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Array.Sort<TKey,TValue> arity {count} -- " +
                            "net48 has (keys, items), (keys, items, IComparer), (keys, items, int, int) and " +
                            "(keys, items, int, int, IComparer) only " +
                            "[Reference Source mscorlib/system/array.cs:1787, :1800, :1813, :1855]");
                    }
                    // nb9 H-B3: Array.Sort<T>(T[], IComparer<T>) and
                    // Array.Sort<T>(T[], Comparison<T>) share the same
                    // GENERICINST parameter bytes, so the overload must be
                    // selected from the DECODED second parameter's generic
                    // DEFINITION name (the MemberRef signature, authoritative).
                    // Only a positively identified IComparer`1 takes the
                    // comparer route; everything else keeps the historical
                    // Comparison<T> route (a StringComparer singleton is an
                    // IComparer<string> and lands here too).
        var secondIsIComparer =
                        SortedSetParamDefName(paramTypes, 1) == "System.Collections.Generic.IComparer`1";
                    // nb9 zz-b4-cmp4: the RANGE overload's comparer is the
                    // FOURTH parameter (array, index, length, comparer).
                    var fourthIsIComparer =
                        SortedSetParamDefName(paramTypes, 3) == "System.Collections.Generic.IComparer`1";
                    return genArgs[0] switch
                    {
                        // Array.Sort<T>(T[]) count==1 -- the no-comparer family:
                        // the icall layer for int32/string, the ArraySort
                        // direct-call domain for uint32/int64 (arraysort.h).
                        0x0E when count == 1 => I("il2cpp::runtime::icall_array_sort_strings", 1, false, false, false),
                        0x08 when count == 1 => I("il2cpp::runtime::icall_array_sort", 1, false, false, false),
                        // nb9 D8: Sort<object>/Sort<T class>(T[]) and its range
                        // form use the element-kind-driven default sorter.
                        0x12 or 0x1C when count == 1 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        // arrgen1b: uint64 (0x0B) yields to the ArraySort
                        // direct-call domain (no dedicated elemKind; the 8-byte
                        // cell rides the int64 direct-call sort there).
                        0x09 or 0x0A or 0x0C or 0x0D or 0x0B when count == 1 => null,
                        // Array.Sort<T>(T[], Comparison<T>) count==2 -- the
                        // DELEGATE sort (cg8-b23). The System.Comparison`1
                        // delegate construction (newobj placeholder + ctor
                        // rewrite) is now wired through the CG8 machinery
                        // (CodeGenerator.cs: System_Comparison_ + BclAllocExpr
                        // System.Comparison`1_* nullptr placeholders), so the
                        // runtime entries (arraysort.h
                        // il2cpp_array_sort_int32_comparison / _ref_comparison,
                        // tested in test_arraysort) are reachable. int32 arrays
                        // compare raw element values; string/object/class arrays
                        // pass the element slots (ref comparison).
                        // nb9 H-B3: IComparer<T> (a user comparer or the
                        // StringComparer singletons) takes the comparer-driven
                        // sort; the runtime resolves the user Compare(T,T) slot
                        // per element family. Comparison<T> keeps the delegate
                        // sort (the ABI decision above).
                        0x08 when count == 2 =>
                            secondIsIComparer
                                ? I("il2cpp::il2cpp_array_sort_icomparer", 2, false, false, false, new[] { "a", "" })
                                : I("il2cpp::il2cpp_array_sort_int32_comparison", 2, false, false, false, new[] { "a", "" }),
                        // nb9 E6/D17: Comparison<T> for uint (carried as int32;
                        // the delegate supplies the unsigned order), int64,
                        // float and double.
                        // B8-1: uint[] carries elemKind 5, so the runtime's
                        // DEFAULT comparer path (null / Comparer<uint>.Default /
                        // Comparer<uint>.Create) orders it UNSIGNED -- the
                        // int32 carrier above is the ABI, not the semantics.
                        0x09 when count == 2 =>
                            secondIsIComparer
                                ? I("il2cpp::il2cpp_array_sort_icomparer", 2, false, false, false, new[] { "a", "" })
                                : I("il2cpp::il2cpp_array_sort_int32_comparison", 2, false, false, false, new[] { "a", "" }),
                        0x0A when count == 2 && !secondIsIComparer => I("il2cpp::il2cpp_array_sort_int64_comparison", 2, false, false, false, new[] { "a", "" }),
                        0x0C when count == 2 && !secondIsIComparer => I("il2cpp::il2cpp_array_sort_float_comparison", 2, false, false, false, new[] { "a", "" }),
                        0x0D when count == 2 && !secondIsIComparer => I("il2cpp::il2cpp_array_sort_double_comparison", 2, false, false, false, new[] { "a", "" }),
                        // a5reds (strsort_ordinal): the SAME generic Sort<T>
                        0x0E or 0x1C or 0x12 or 0x13 or 0x1D or 0x14 or 0x1B when count == 2 =>
                            secondIsIComparer
                                || arg0Type is "System.StringComparer.Ordinal" or "System.StringComparer.OrdinalIgnoreCase"
                                // The second argument is an IComparer<T>
                                // (MethodMap.Collections.cs TryMapStringComparer
                                // ReturnTypeName for the singletons), NOT a
                                // Comparison<T> delegate: route to the
                                // comparer-driven sort (arraysort.cpp
                                // recognizes the singletons; IL order is
                                // (array, comparer) -- the same slot order as
                                // the non-generic arm below).
                                ? I("il2cpp::il2cpp_array_sort_icomparer", 2, false, false, false, new[] { "a", "" })
                                : I("il2cpp::il2cpp_array_sort_ref_comparison", 2, false, false, false, new[] { "a", "" }),
                        // nb9 P0: Array.Sort<T>(T[], int, int) -- the
                        // no-comparer RANGE sort (arraysort.h partial sorts;
                        // double/float added for the nb9 P0 arity surface).
                        0x08 when count == 3 => I("il2cpp::il2cpp_array_sort_int32", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x09 when count == 3 => I("il2cpp::il2cpp_array_sort_uint32", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x09 when count == 4 && fourthIsIComparer => I("il2cpp::il2cpp_array_sort_icomparer_uint32_range", 4, false, false, false, new[] { "a", "i", "i", "" }),
                        0x0A when count == 3 => I("il2cpp::il2cpp_array_sort_int64", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x0C when count == 3 => I("il2cpp::il2cpp_array_sort_float", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x0D when count == 3 => I("il2cpp::il2cpp_array_sort_double", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x0E when count == 3 => I("il2cpp::il2cpp_array_sort_string", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x12 or 0x1C when count == 3 => I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        // arrgen1b: uint64 (0x0B) range sort rides the
                        // element-kind-driven partial sorter (elemSize 8).
                        0x0B when count == 3 => I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        // nb9 zz-b4-cmp4: Array.Sort<T>(T[], int, int,
                        // IComparer<T>) -- only [index, index+length) is
                        // ordered. The 4th parameter's generic definition is
                        // the authoritative overload discriminator. The
                        // Comparison<T> range form stays LOUD (no delegate
                        // range entry -- report section 7).
                        // nb9 L-11/D7: the wide value-family IComparer sorts.
                        0x0A when count == 2 && secondIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_int64", 2, false, false, false, new[] { "a", "" }),
                        0x0C when count == 2 && secondIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_float", 2, false, false, false, new[] { "a", "" }),
                        0x0D when count == 2 && secondIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_double", 2, false, false, false, new[] { "a", "" }),
                        0x0A when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_int64_range", 4, false, false, false, new[] { "a", "i", "i", "" }),
                        0x0C when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_float_range", 4, false, false, false, new[] { "a", "i", "i", "" }),
                        0x0D when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_double_range", 4, false, false, false, new[] { "a", "i", "i", "" }),
                        0x08 or 0x0E or 0x1C or 0x12 or 0x13 or 0x1D or 0x14 or 0x1B when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_range", 4, false, false, false,
                                new[] { "a", "i", "i", "" }),
                        // Narrow-integral families: the kind literal selects
                        // the real cell width (arraysort.cpp).
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 1 =>
                            I("il2cpp::il2cpp_array_sort_narrow", 1, false, false, false, new[] { "a" },
                                appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 2 =>
                            secondIsIComparer
                                ? I("il2cpp::il2cpp_array_sort_narrow_icomparer", 2, false, false, false,
                                    new[] { "a", "" }, appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! })
                                : I("il2cpp::il2cpp_array_sort_narrow_comparison", 2, false, false, false,
                                    new[] { "a", "" }, appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 3 =>
                            I("il2cpp::il2cpp_array_sort_narrow_range", 3, false, false, false,
                                new[] { "a", "i", "i" }, appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_narrow_icomparer_range", 4, false, false, false,
                                new[] { "a", "i", "i", "" }, appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                        // IntPtr (0x18) rides the signed int64 kernels;
                        // UIntPtr (0x19) the unsigned uint64 kernels.
                        0x18 when count == 1 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        0x18 when count == 2 =>
                            secondIsIComparer
                                ? I("il2cpp::il2cpp_array_sort_icomparer_int64", 2, false, false, false, new[] { "a", "" })
                                : I("il2cpp::il2cpp_array_sort_int64_comparison", 2, false, false, false, new[] { "a", "" }),
                        0x18 when count == 3 => I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        0x18 when count == 4 && fourthIsIComparer =>
                            I("il2cpp::il2cpp_array_sort_icomparer_int64_range", 4, false, false, false, new[] { "a", "i", "i", "" }),
                        0x19 when count == 1 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        0x19 when count == 3 => I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        // Closed reference generic instantiations / arrays of
                        // arrays / function pointers are reference slots.
                        0x13 or 0x1D or 0x14 or 0x1B when count == 1 =>
                            I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        0x13 or 0x1D or 0x14 or 0x1B when count == 3 =>
                            I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        // DateTime/TimeSpan are 8-byte tick cells; the
                        // element-kind sorter orders them by ticks.
                        _ when count == 1 && IsInt64CellValueType(genArgs, genArgNames) =>
                            I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        _ when count == 3 && IsInt64CellValueType(genArgs, genArgNames) =>
                            I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                        _ when count == 4 && !fourthIsIComparer =>
                            throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Array.Sort<T> arity 4 whose fourth " +
                                "parameter is not IComparer<T> -- net48 has no (T[], int, int, Comparison<T>) " +
                                "overload [Reference Source mscorlib/system/array.cs:1822]"),
                        _ when count == 5 =>
                            throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Array.Sort<T> arity {count} -- the " +
                                "single-array generic overloads take at most 4 parameters " +
                                "[Reference Source mscorlib/system/array.cs:1779-1903]"),
                        0x15 =>
                            throw new NotSupportedException(
                                $"LOUD whitelist (runtime generic-instantiation kind unknown): Array.Sort<0x15> with " +
                                $"{count} arg(s) -- the MethodSpec blob does not say whether the closed generic " +
                                "instantiation is a value or a reference type, so no element family can be selected " +
                                "[Reference Source mscorlib/system/array.cs:1779-1903]"),
                        _ => throw new NotSupportedException(
                            $"LOUD whitelist (unmaterialized value-type array descriptor): Array.Sort<T> generic " +
                            $"argument 0x{genArgs[0]:X2} with {count} arg(s) -- the value array carries no element " +
                            "kind/descriptor, so no comparer/box path can be built for this value type " +
                            "[Reference Source mscorlib/system/array.cs:1779-1903]"),
                    };
                }
                // Non-generic Array.Sort(Array[, IComparer]).
                return count switch
                {
                    // nb9 D6: the element-kind-driven non-generic sort (string
                    // and wider value arrays included; the old icall was
                    // int32-only).
                    1 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                    // nb9 D5: Sort(Array, Array) vs Sort(Array, IComparer) are
                    // told apart by the second argument's type (an array type
                    // means keys/items).
                    2 when SecondIsArrayParam(paramTypes) =>
                        I("il2cpp::il2cpp_array_sort_keys_items_default", 2, false, false, false, new[] { "a", "a" }),
                    2 => I("il2cpp::il2cpp_array_sort_icomparer", 2, false, false, false, new[] { "a", "" }),
                    // nb9 D4/E2: Sort(Array, int, int[, IComparer]) vs
                    // Sort(Array keys, Array items, IComparer): the second
                    // argument type decides (an array type means keys/items).
                    3 when SecondIsArrayParam(paramTypes) =>
                        I("il2cpp::il2cpp_array_sort_keys_items", 3, false, false, false, new[] { "a", "a", "" }),
                    3 => I("il2cpp::il2cpp_array_sort_any_range", 3, false, false, false, new[] { "a", "i", "i" }),
                    4 when SecondIsArrayParam(paramTypes) =>
                        I("il2cpp::il2cpp_array_sort_keys_items_default_range", 4, false, false, false, new[] { "a", "a", "i", "i" }),
                    4 => I("il2cpp::il2cpp_array_sort_any_range_icomparer", 4, false, false, false, new[] { "a", "i", "i", "" }),
                    5 => I("il2cpp::il2cpp_array_sort_keys_items_range", 5, false, false, false, new[] { "a", "a", "i", "i", "" }),
                    _ => throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Array.Sort(Array, ...) arity {count} -- " +
                        "net48 has arities 1..5 only " +
                        "[Reference Source mscorlib/system/array.cs:1643-1735]"),
                };
            case "BinarySearch":
                if (genArgs != null)
                {
                    return genArgs[0] switch
                    {
                        // nb9 H-B4: the 2-arg form is the default-comparer
                        // search; the 3-arg Array.BinarySearch<T>(T[], T,
                        // IComparer<T>) needs its OWN entry (the old mapping
                        // emitted the 2-param icall for EVERY arity -> the
                        // third argument shifted the value/comparer slots and
                        // the generated C++ did not compile). Only the 2/3
                        // forms exist for the wired families; a wider arity
                        // stays LOUD.
                        0x08 when count == 2 => I("il2cpp::runtime::icall_array_binary_search_int32", 2, true, false, false),
                        0x08 when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_int32", 3, true, false, false, new[] { "a", "i", "" }),
                        0x0E when count == 2 => I("il2cpp::runtime::icall_array_binary_search_string", 2, true, false, false),
                        0x0E or 0x1C or 0x12 when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_ref", 3, true, false, false, new[] { "a", "", "" }),
                        // nb9 zz-b4-bsearch2: the RANGE form
                        // Array.BinarySearch<T>(T[], int, int, T, IComparer<T>)
                        // (the comparer is parameter 4).
                        0x08 when count == 5 && SortedSetParamDefName(paramTypes, 4) == "System.Collections.Generic.IComparer`1" =>
                            I("il2cpp::il2cpp_array_binary_search_icomparer_int32_range", 5, true, false, false,
                                new[] { "a", "i", "i", "i", "" }),
                        0x0E or 0x1C or 0x12 when count == 5 && SortedSetParamDefName(paramTypes, 4) == "System.Collections.Generic.IComparer`1" =>
                            I("il2cpp::il2cpp_array_binary_search_icomparer_ref_range", 5, true, false, false,
                                new[] { "a", "i", "i", "", "" }),
                        // M6: int64 yields to the ArraySort direct-call
                        // domain (il2cpp_array_binary_search_int64); only
                        // the 2-arg form (ArraySort.cs maps any count to
                        // the 2-param call, so wider arities stay LOUD).
                        // W7: double follows the same yield.
                        0x0A or 0x0D when count == 2 => null,
                        // nb9 P0: everything else (the 4-arg range form and
                        // the remaining families) yields to the ArraySort
                        // direct-call domain.
                        _ => null,
                    };
                }
                // Non-generic Array.BinarySearch(Array, object[, ...]).
                return count switch
                {
                    2 => I("il2cpp::il2cpp_array_binary_search_any", 2, true, false, false, new[] { "a", "" }),
                    3 => I("il2cpp::il2cpp_array_binary_search_any_icomparer", 3, true, false, false, new[] { "a", "", "" }),
                    4 => I("il2cpp::il2cpp_array_binary_search_any_range", 4, true, false, false, new[] { "a", "i", "i", "" }),
                    5 => I("il2cpp::il2cpp_array_binary_search_any_icomparer_range", 5, true, false, false, new[] { "a", "i", "i", "", "" }),
                    _ => throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Array.BinarySearch(Array, object, ...) " +
                        $"arity {count} -- net48 has arities 2..5 only " +
                        "[Reference Source mscorlib/system/array.cs:841-911]"),
                };
            case "IndexOf":
                if (genArgs != null)
                {
                    // nb9 P0 (arity mispair): every arm is gated by count. Only
                    // the 2-arg icall-layer shape is claimed here; the 3/4-arg
                    // (array, value, startIndex[, count]) forms yield to the
                    // ArraySort direct-call domain (arraysort.h *_start/*_range).
                    return genArgs[0] switch
                    {
                        0x08 when count == 2 => I("il2cpp::runtime::icall_array_index_of_int32", 2, true, false, false),
                        0x0A or 0x0D or 0x0E when count == 2 => null,
                        _ => null, // wider arities and the float/ref families: ArraySort
                    };
                }
                // Non-generic Array.IndexOf(Array, object[, startIndex[, count]]):
                // int32/ref element dispatch in the ArraySort domain.
                if (count == 2) return I("il2cpp::il2cpp_array_index_of_any_start", 2, true, false, false, new[] { "a", "" }, appendZero: true);
                if (count == 3) return I("il2cpp::il2cpp_array_index_of_any_start", 3, true, false, false, new[] { "a", "", "i" });
                if (count == 4) return I("il2cpp::il2cpp_array_index_of_any_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                throw new NotSupportedException($"LOUD whitelist (net48 has no such overload): Array.IndexOf(Array, object, ...) arity {count} -- net48 has arities 2..4 only [Reference Source mscorlib/system/array.cs:1282-1315]");
            case "LastIndexOf":
                if (genArgs != null)
                {
                    // nb9 P0 (arity mispair): count-gated. The 3/4-arg shapes
                    // yield to ArraySort (*_start/*_range).
                    if (count == 2)
                    {
                        if (genArgs[0] == 0x08) return I("il2cpp::runtime::icall_array_last_index_of_int32", 2, true, false, false);
                        if (genArgs[0] == 0x0D) return I("il2cpp::il2cpp_array_last_index_of_double", 2, true, false, false, new[] { "a", "d" });
                        if (genArgs[0] == 0x0E) return I("il2cpp::il2cpp_array_last_index_of_string", 2, true, false, false, new[] { "a", "" });
                        if (genArgs[0] == 0x0A) return I("il2cpp::il2cpp_array_last_index_of_long", 2, true, false, false, new[] { "a", "l" });
                    }
                    return null; // wider arities / other families: ArraySort
                }
                // Non-generic Array.LastIndexOf(Array, object[, ...]).
                if (count == 2) return I("il2cpp::il2cpp_array_last_index_of_any", 2, true, false, false, new[] { "a", "" });
                if (count == 3) return I("il2cpp::il2cpp_array_last_index_of_any_start", 3, true, false, false, new[] { "a", "", "i" });
                if (count == 4) return I("il2cpp::il2cpp_array_last_index_of_any_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                throw new NotSupportedException($"LOUD whitelist (net48 has no such overload): Array.LastIndexOf(Array, object, ...) arity {count} -- net48 has arities 2..4 only [Reference Source mscorlib/system/array.cs:1422-1454]");
            case "Empty":
                // Array.Empty<T>() -- generic static method (MethodSpec
                // genArgs = [T]; Roslyn lowers no-arg TrimStart()/TrimEnd()
                // params calls to Array.Empty<char>() in optimize builds).
                // The zero-length array comes from a per-type runtime icall:
                // the emitter injects only literal 0 / count arguments, so
                // the (elemSize, isRefElement) constants a direct
                // il2cpp_array_new call needs are unreachable. Dispatch on
                // the generic argument byte like Sort/BinarySearch above.
                if (genArgs != null)
                {
                    return genArgs[0] switch
                    {
                        0x03 => I("il2cpp::runtime::icall_array_empty_char", 0, true, true, false),
                        0x08 => I("il2cpp::runtime::icall_array_empty_int32", 0, true, true, false),
                        0x0E => I("il2cpp::runtime::icall_array_empty_string", 0, true, true, false),
                        // P19 exprtree: Array.Empty<ParameterExpression>() (the
                        // Expression.Lambda params lowering) and
                        // Array.Empty<object>() (the Delegate.DynamicInvoke
                        // params lowering) -- CLASS/OBJECT elements (0x12/0x1C)
                        // return an empty object[].
                        0x12 => I("il2cpp::runtime::icall_array_empty_object", 0, true, true, false),
                        0x1C => I("il2cpp::runtime::icall_array_empty_object", 0, true, true, false),
                        // W7: long/double typed empty factories (arrayext2).
                        0x0A => I("il2cpp::runtime::icall_array_empty_long", 0, true, true, false),
                        0x0D => I("il2cpp::runtime::icall_array_empty_double", 0, true, true, false),
                        // arrgen1: the float / narrow-integral empty factories
                        // (a zero-length array of the matching element stride).
                        0x0C => I("il2cpp::runtime::icall_array_empty_float", 0, true, true, false),
                        0x04 => I("il2cpp::runtime::icall_array_empty_sbyte", 0, true, true, false),
                        0x05 => I("il2cpp::runtime::icall_array_empty_byte", 0, true, true, false),
                        0x02 => I("il2cpp::runtime::icall_array_empty_bool", 0, true, true, false),
                        0x06 => I("il2cpp::runtime::icall_array_empty_short", 0, true, true, false),
                        0x07 => I("il2cpp::runtime::icall_array_empty_ushort", 0, true, true, false),
                        0x09 => I("il2cpp::runtime::icall_array_empty_uint", 0, true, true, false),
                        // W7 (T2): the uint64 zero-length factory (the one
                        // primitive element family without a dedicated factory).
                        0x0B => I("il2cpp::runtime::icall_array_empty_uint64", 0, true, true, false),
                        // Closed reference generic instantiations / arrays of
                        // arrays / function pointers are reference slots.
                        0x13 or 0x1D or 0x14 or 0x1B =>
                            I("il2cpp::runtime::icall_array_empty_object", 0, true, true, false),
                        // IntPtr/UIntPtr are native-width value cells.
                        0x18 => I("il2cpp::runtime::icall_array_empty_long", 0, true, true, false),
                        0x19 => I("il2cpp::runtime::icall_array_empty_uint64", 0, true, true, false),
                        // DateTime/TimeSpan are 8-byte tick cells.
                        _ when IsInt64CellValueType(genArgs, genArgNames) =>
                            I("il2cpp::runtime::icall_array_empty_long", 0, true, true, false),
                        _ => throw new NotSupportedException(
                            $"LOUD whitelist (unmaterialized value-type array descriptor): Array.Empty<T> generic " +
                            $"argument 0x{genArgs[0]:X2} -- a BCL value type whose {{T}}_class / array element " +
                            "kind was never materialized, so the zero-length array cannot be built " +
                            "[Reference Source mscorlib/system/array.cs:1080]"),
                    };
                }
                throw new NotSupportedException(
                    "LOUD whitelist (net48 has no such overload): Array.Empty is generic only " +
                    "(Array.Empty<T>()) [Reference Source mscorlib/system/array.cs:1080]");
            case "Clear":
                return I("il2cpp::runtime::icall_array_clear_int32", 3, false, false, false);
            case "Reverse":
                if (count == 1)
                {
                    // Generic Array.Reverse<T>(T[]) carries the element kind in
                    // genArgs -- route string/object arrays to the reference-slot
                    // reversers (arrayextra.h) instead of the int32-only
                    // icall_array_reverse (e2e_arrsort finding). The non-generic
                    // Reverse(Array) form keeps the historical int32 contract.
                    if (genArgs != null)
                    {
                        return genArgs[0] switch
                        {
                            0x08 => I("il2cpp::runtime::icall_array_reverse", 1, false, false, false),
                            // W7: long reuses the arrayextra int64 1-arg
                            // reverser (mapping-only); double is its 8-byte
                            // twin (new, arrayextra).
                            0x0A => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                            0x0D => I("il2cpp::il2cpp_array_reverse_double", 1, false, false, false, new[] { "a" }),
                            0x0E => I("il2cpp::il2cpp_array_reverse_string", 1, false, false, false, new[] { "a" }),
                            0x1C or 0x12 => I("il2cpp::il2cpp_array_reverse_object", 1, false, false, false, new[] { "a" }),
                            // arrgen1: the float / narrow-integral reversers
                            // (the raw two-pointer swap, one per element stride).
                            0x0C => I("il2cpp::il2cpp_array_reverse_float", 1, false, false, false, new[] { "a" }),
                            0x04 => I("il2cpp::il2cpp_array_reverse_sbyte", 1, false, false, false, new[] { "a" }),
                            0x05 => I("il2cpp::il2cpp_array_reverse_byte", 1, false, false, false, new[] { "a" }),
                            0x02 => I("il2cpp::il2cpp_array_reverse_bool", 1, false, false, false, new[] { "a" }),
                            0x06 => I("il2cpp::il2cpp_array_reverse_short", 1, false, false, false, new[] { "a" }),
                            0x07 => I("il2cpp::il2cpp_array_reverse_ushort", 1, false, false, false, new[] { "a" }),
                            0x03 => I("il2cpp::il2cpp_array_reverse_char", 1, false, false, false, new[] { "a" }),
                            0x09 => I("il2cpp::il2cpp_array_reverse_uint", 1, false, false, false, new[] { "a" }),
                            // W7 (T2): the uint64 reverser (8-byte slot twin).
                            0x0B => I("il2cpp::il2cpp_array_reverse_uint64", 1, false, false, false, new[] { "a" }),
                            // Closed reference generic instantiations / arrays
                            // of arrays / function pointers: reference slots.
                            0x13 or 0x1D or 0x14 or 0x1B =>
                                I("il2cpp::il2cpp_array_reverse_object", 1, false, false, false, new[] { "a" }),
                            0x18 => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                            0x19 => I("il2cpp::il2cpp_array_reverse_uint64", 1, false, false, false, new[] { "a" }),
                            _ when IsInt64CellValueType(genArgs, genArgNames) =>
                                I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (unmaterialized value-type array descriptor): Array.Reverse<T> " +
                                $"generic argument 0x{genArgs[0]:X2} -- a BCL value type with no materialized " +
                                "array element kind/descriptor [Reference Source mscorlib/system/array.cs:1579]"),
                        };
                    }
                    // Non-generic Reverse(Array): genArgs is null (the
                    // MemberReference path passes none). The tracked first-
                    // argument type names the real element width -- routing
                    // a typed array to the int32-only contract throws
                    // RequireInt32Array at runtime (g4arrval RED: long[] via
                    // Reverse(Array) binding). Untyped/unknown stays on the
                    // historical int32 contract (zero behavior change).
                    return arg0Type switch
                    {
                        "System.Int64[]" => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                        "System.Double[]" => I("il2cpp::il2cpp_array_reverse_double", 1, false, false, false, new[] { "a" }),
                        "System.String[]" => I("il2cpp::il2cpp_array_reverse_string", 1, false, false, false, new[] { "a" }),
                        "System.Object[]" => I("il2cpp::il2cpp_array_reverse_object", 1, false, false, false, new[] { "a" }),
                        // arrgen1: the float / narrow-integral tracked array names
                        // (the same raw two-pointer swap, one per element stride).
                        "System.Single[]" => I("il2cpp::il2cpp_array_reverse_float", 1, false, false, false, new[] { "a" }),
                        "System.SByte[]" => I("il2cpp::il2cpp_array_reverse_sbyte", 1, false, false, false, new[] { "a" }),
                        "System.Byte[]" => I("il2cpp::il2cpp_array_reverse_byte", 1, false, false, false, new[] { "a" }),
                        "System.Boolean[]" => I("il2cpp::il2cpp_array_reverse_bool", 1, false, false, false, new[] { "a" }),
                        "System.Int16[]" => I("il2cpp::il2cpp_array_reverse_short", 1, false, false, false, new[] { "a" }),
                        "System.UInt16[]" => I("il2cpp::il2cpp_array_reverse_ushort", 1, false, false, false, new[] { "a" }),
                        "System.Char[]" => I("il2cpp::il2cpp_array_reverse_char", 1, false, false, false, new[] { "a" }),
                        "System.UInt32[]" => I("il2cpp::il2cpp_array_reverse_uint", 1, false, false, false, new[] { "a" }),
                        // W7 (T2): the uint64 reverser (8-byte slot twin).
                        "System.UInt64[]" => I("il2cpp::il2cpp_array_reverse_uint64", 1, false, false, false, new[] { "a" }),
                        // 8-byte value cells with a known tick/word layout.
                        "System.DateTime[]" => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                        "System.TimeSpan[]" => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                        "System.IntPtr[]" => I("il2cpp::il2cpp_array_reverse_int64", 1, false, false, false, new[] { "a" }),
                        "System.UIntPtr[]" => I("il2cpp::il2cpp_array_reverse_uint64", 1, false, false, false, new[] { "a" }),
                        _ => I("il2cpp::runtime::icall_array_reverse", 1, false, false, false),
                    };
                }
                return I("il2cpp::runtime::icall_array_reverse_range", 3, false, false, false);
            case "Copy":
                // icall_array_copy(src, srcIndex, dst, dstIndex, length) --
                // the 3-arg overload (src, dst, length) forwards through
                // icall_array_copy_3 (both indices zero; the emitter has no
                // constant-injection facility). The netcore 6-arg form adds
                // `bool reliable` -- a memory-model hint the simplified GC
                // does not need: dropped ("drop" cast), same arity-pop
                // discipline as GC.Collect(int).
                // net48 long-index overloads (array.cs:320/:329): the long
                // arguments are validated against the int32 range by the
                // runtime wrapper, then the int32 core runs.
                if (count == 5 && ps is { Length: >= 5 } && ps[1] == 0x0A && ps[3] == 0x0A)
                    return I("il2cpp::runtime::icall_array_copy_long", 5, false, false, false,
                        new[] { "", "l", "", "l", "l" });
                if (count == 3 && ps is { Length: >= 3 } && ps[2] == 0x0A)
                    return I("il2cpp::runtime::icall_array_copy_3_long", 3, false, false, false,
                        new[] { "", "", "l" });
                if (count == 5) return I("il2cpp::runtime::icall_array_copy", 5, false, false, false);
                if (count == 3) return I("il2cpp::runtime::icall_array_copy_3", 3, false, false, false);
                if (count == 6) return I("il2cpp::runtime::icall_array_copy", 6, false, false, false,
                    new[] { "", "i", "", "i", "i", "drop" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Array.Copy arity {count} -- net48 has " +
                    "(Array, Array, int), (Array, Array, long), (Array, int, Array, int, int) and " +
                    "(Array, long, Array, long, long) [Reference Source mscorlib/system/array.cs:270, :292, :320, :329]");
            case "ConstrainedCopy":
                // CONSTRAINED (2026-08-22): Array.ConstrainedCopy(Array, int,
                // Array, int, int) -- net48 has ONLY this 5-arg form. The
                // constrained contract (every failure precedes ANY mutation)
                // is exactly what icall_array_copy already implements
                // (arrayext.cpp:188-211: null -> indices -> ranges ->
                // element-kind ArrayTypeMismatch all throw BEFORE the
                // memmove), so the entry is the SAME icall as Copy above.
                if (count == 5) return I("il2cpp::runtime::icall_array_copy", 5, false, false, false);
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Array.ConstrainedCopy arity {count} -- " +
                    "net48 has only the 5-arg (Array, int, Array, int, int) form " +
                    "[Reference Source mscorlib/system/array.cs:314]");
            case "FindIndex":
                // arrayext2 (2026-08-09, rtfind-b23): the DELEGATE form is
                // now wired -- icall_array_find_index (il2cpp_array_ext2.h:145)
                // consumes the Predicate<T> DELEGATE (single-cast static
                // methodPtr ABI, the linq where_i4 callback pattern) directly.
                //   C++ signature: icall_array_find_index(Il2CppArray* arr,
                //   Il2CppObject* predicate, int32_t startIndex, int32_t count)
                // net48: null array -> ANE, start/count out of range -> AOO,
                // null match -> ANE; returns the first index whose element
                // matches, or -1. Casts: "a" array -> Il2CppArray*, "" the
                // delegate (managed ref slot passthrough), "i" start/count.
                // ARG-ORDER CONSTRAINT (must hold at emission): the .NET 4-arg
                // overload's IL pushes (array, startIndex, count, match) --
                // the delegate is LAST -- while the icall's parameter order is
                // (arr, predicate, start, count) -- the delegate is 2nd. The
                // emitter (CodeGenerator.cs EmitIcallCallArgs) emits args
                // positionally with no reordering facility, so the emitted
                // call must be icall_array_find_index(array, match, start,
                // count) -- a resolver-only mapping cannot express the
                // reorder; see the wir8 report for the emitter gap.
                // arrgen1: the int64 (0x0A) and string/reference (0x0E) families
                // have their own delegate icalls (il2cpp_array_ext2.h); the
                // string family takes a reference-element array.
                // arrgen1b: the double (0x0D), float (0x0C) and uint64 (0x0B)
                // families too (the float predicate ABI is the double slot --
                // MethodMap.MathF.cs float-in-double model).
                if (genArgs != null)
                {
                    var suffix = genArgs[0] switch
                    {
                        0x08 => "",
                        0x0A => "_i8",
                        0x0E => "_str",
                        // Reference-element generic instantiations / arrays of
                        // arrays / function pointers ride the reference-slot
                        // predicate ABI.
                        0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B => "_str",
                        // IntPtr / DateTime / TimeSpan are 8-byte value cells;
                        // the predicate ABI is the int64 one.
                        0x18 => "_i8",
                        0x0D => "_d",
                        0x0C => "_f",
                        0x0B => "_u8",
                        _ when IsInt64CellValueType(genArgs, genArgNames) => "_i8",
                        _ => NarrowSuffix(genArgs[0]),
                    };
                    if (suffix != null)
                    {
                        // 2-arg overload (array, match): the whole range via the
                        // dedicated *_find_index_2 wrapper.
                        if (ps.Length == 2)
                            return I($"il2cpp::runtime::icall_array_find_index_2{suffix}", 2, true, false, false,
                                new[] { "a", "" });
                        if (ps.Length == 3)
                            return I($"il2cpp::runtime::icall_array_find_index_3{suffix}", 3, true, false, false,
                                new[] { "a", "", "i" });
                        return I($"il2cpp::runtime::icall_array_find_index{suffix}", 4, true, false, false,
                            new[] { "a", "", "i", "i" });
                    }
                }
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.FindIndex<T> generic argument " + $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box descriptor [Reference Source mscorlib/system/array.cs:1130-1150]");
            // ARRFUNCS (2026-08-09, wir9-b23): the five remaining arrayext2
            // delegate/resize icalls (il2cpp_array_ext2.h:154-191) -- the
            // arrfuncs e2e surface. ALL are 2-arg static shapes whose IL push
            // order EQUALS the icall parameter order (unlike FindIndex's 4-arg
            // reorder), so NO IcallArgOrder entry is needed for any of them.
            // The Predicate<int>/Converter<int,int> delegates ride the same
            // single-cast static methodPtr ABI as FindIndex ("" passthrough
            // managed ref slot); the array rides the "a" cast.
            case "Find":
                // int Array.Find<int>(int32[], Predicate<int>): the VALUE of the
                // first matching element, or default(int)=0 (empty -> 0).
                // arrgen1: the int64 (0x0A), string (0x0E) and double (0x0D)
                // families have their own icalls; the result carrier follows the
                // element kind (int64 -> "l", string -> a managed reference,
                // double -> "d").
                if (genArgs != null && genArgs[0] == 0x08)   // Find<int>
                    return I("il2cpp::runtime::icall_array_find_i4", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0A)   // Find<long>
                    return I("il2cpp::runtime::icall_array_find_i8", 2, true, false, false,
                        new[] { "a", "" }, "l");
                if (genArgs != null && genArgs[0] == 0x0E)   // Find<string>
                    return I("il2cpp::runtime::icall_array_find_str", 2, true, true, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0D)   // Find<double>
                    return I("il2cpp::runtime::icall_array_find_d", 2, true, false, false,
                        new[] { "a", "" }, "d");
                // arrgen1b: the float family. The result rides ResultKind "d"
                // (the transpiler's float-in-double slot model -- the float
                // return widens to double losslessly).
                if (genArgs != null && genArgs[0] == 0x0C)   // Find<float>
                    return I("il2cpp::runtime::icall_array_find_f", 2, true, false, false,
                        new[] { "a", "" }, "d");
                // arrgen1b: the uint64 family (the "q" slot rides an int64_t
                // temp; the value bits are identical).
                if (genArgs != null && genArgs[0] == 0x0B)   // Find<ulong>
                    return I("il2cpp::runtime::icall_array_find_u8", 2, true, false, false,
                        new[] { "a", "" }, "l");
                // arrgen1b: the narrow-integral families (the int32 predicate ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns1)
                    return I($"il2cpp::runtime::icall_array_find{ns1}", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_find_str", 2, true, true, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_find_i8", 2, true, false, false, new[] { "a", "" }, "l");
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.Find<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:1093]");
            case "FindLast":
                // arrayc1: int Array.FindLast<int>(int32[], Predicate<int>) --
                // the VALUE of the LAST matching element, or default(int)=0
                // (empty -> 0). Mirrors the Find arm with a BACKWARD scan (the
                // icall icall_array_find_last_i4). Same single-cast static
                // methodPtr predicate ABI; the 2-arg shape's IL order equals the
                // icall order (no IcallArgOrder entry needed).
                if (genArgs != null && genArgs[0] == 0x08)   // FindLast<int>
                    return I("il2cpp::runtime::icall_array_find_last_i4", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1: the int64 / double / string families (the same
                // single-cast static methodPtr predicate ABI and backward scan;
                // the result carrier follows the element kind).
                if (genArgs != null && genArgs[0] == 0x0A)   // FindLast<long>
                    return I("il2cpp::runtime::icall_array_find_last_i8", 2, true, false, false,
                        new[] { "a", "" }, "l");
                if (genArgs != null && genArgs[0] == 0x0D)   // FindLast<double>
                    return I("il2cpp::runtime::icall_array_find_last_d", 2, true, false, false,
                        new[] { "a", "" }, "d");
                if (genArgs != null && genArgs[0] == 0x0E)   // FindLast<string>
                    return I("il2cpp::runtime::icall_array_find_last_str", 2, true, true, false,
                        new[] { "a", "" });
                // arrgen1b: the float family (the double-slot predicate ABI;
                // the result rides ResultKind "d" -- float-in-double model).
                if (genArgs != null && genArgs[0] == 0x0C)   // FindLast<float>
                    return I("il2cpp::runtime::icall_array_find_last_f", 2, true, false, false,
                        new[] { "a", "" }, "d");
                // arrgen1b: the uint64 family (the "q" slot rides an int64_t
                // temp; the value bits are identical).
                if (genArgs != null && genArgs[0] == 0x0B)   // FindLast<ulong>
                    return I("il2cpp::runtime::icall_array_find_last_u8", 2, true, false, false,
                        new[] { "a", "" }, "l");
                // arrgen1b: the narrow-integral families (the int32 predicate ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns2)
                    return I($"il2cpp::runtime::icall_array_find_last{ns2}", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_find_last_str", 2, true, true, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_find_last_i8", 2, true, false, false, new[] { "a", "" }, "l");
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.FindLast<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:1176]");
            case "FindLastIndex":
                // arrayc1: int Array.FindLastIndex<int>(int32[], Predicate<int>)
                // and its range overloads -- the index of the LAST match in
                // [startIndex, startIndex + count), or -1. Mirrors the FindIndex
                // arm with a BACKWARD scan. The 3/4-arg IL order is
                // (array[, startIndex[, count]], match) while the icall order is
                // (arr, predicate, startIndex, count), so the delegate is LAST in
                // IL -- the IcallArgOrder table reorders the 3/4-arg forms.
                // arrgen1b: every wired family has its own delegate icall
                // (int32/int64/string/double/float/uint64 -- the float
                // predicate ABI is the double slot); the suffix selects the
                // family.
                if (genArgs != null)
                {
                    var suffix = genArgs[0] switch
                    {
                        0x08 => "",
                        0x0A => "_i8",
                        0x0E => "_str",
                        // Reference-element generic instantiations / arrays of
                        // arrays / function pointers ride the reference-slot
                        // predicate ABI.
                        0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B => "_str",
                        // IntPtr / DateTime / TimeSpan are 8-byte value cells.
                        0x18 => "_i8",
                        0x0D => "_d",
                        0x0C => "_f",
                        0x0B => "_u8",
                        _ when IsInt64CellValueType(genArgs, genArgNames) => "_i8",
                        _ => NarrowSuffix(genArgs[0]),
                    };
                    if (suffix != null)
                    {
                        if (ps.Length == 2)
                            return I($"il2cpp::runtime::icall_array_find_last_index_2{suffix}", 2, true, false, false,
                                new[] { "a", "" });
                        if (ps.Length == 3)
                            return I($"il2cpp::runtime::icall_array_find_last_index_3{suffix}", 3, true, false, false,
                                new[] { "a", "", "i" });
                        return I($"il2cpp::runtime::icall_array_find_last_index{suffix}", 4, true, false, false,
                            new[] { "a", "", "i", "i" });
                    }
                }
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.FindLastIndex<T> generic argument " + $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box descriptor [Reference Source mscorlib/system/array.cs:1194-1212]");
            case "ForEach":
                // arrayc1: void Array.ForEach<int>(int32[], Action<int>) --
                // invoke the action once per element in source order; the source
                // is never modified. Same single-cast static methodPtr action
                // ABI and 2-arg IL order as Find (no IcallArgOrder entry).
                if (genArgs != null && genArgs[0] == 0x08)   // ForEach<int>
                    return I("il2cpp::runtime::icall_array_for_each_i4", 2, false, false, false,
                        new[] { "a", "" });
                // arrgen1: the int64 / double / string families (the same
                // single-cast static methodPtr action ABI).
                if (genArgs != null && genArgs[0] == 0x0A)   // ForEach<long>
                    return I("il2cpp::runtime::icall_array_for_each_i8", 2, false, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0D)   // ForEach<double>
                    return I("il2cpp::runtime::icall_array_for_each_d", 2, false, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0E)   // ForEach<string>
                    return I("il2cpp::runtime::icall_array_for_each_str", 2, false, false, false,
                        new[] { "a", "" });
                // arrgen1b: the float family (the double-slot action ABI).
                if (genArgs != null && genArgs[0] == 0x0C)   // ForEach<float>
                    return I("il2cpp::runtime::icall_array_for_each_f", 2, false, false, false,
                        new[] { "a", "" });
                // arrgen1b: the uint64 family.
                if (genArgs != null && genArgs[0] == 0x0B)   // ForEach<ulong>
                    return I("il2cpp::runtime::icall_array_for_each_u8", 2, false, false, false,
                        new[] { "a", "" });
                // arrgen1b: the narrow-integral families (the int32 action ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns3)
                    return I($"il2cpp::runtime::icall_array_for_each{ns3}", 2, false, false, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_for_each_str", 2, false, false, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_for_each_i8", 2, false, false, false, new[] { "a", "" });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.ForEach<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:1249]");
            case "FindAll":
                // int32[] Array.FindAll<int>(int32[], Predicate<int>): a NEW
                // int32[] holding EVERY element whose predicate returns true,
                // in source order (an empty result is a zero-length array); the
                // source is never modified. Same single-cast static methodPtr
                // predicate ABI as Find (il2cpp_array_ext2.h:201). The result
                // is a managed reference (ReturnsRef) -- the icall returns
                // Il2CppArray*; the result temp stays Il2CppObject*.
                if (genArgs != null && genArgs[0] == 0x08)   // FindAll<int>
                    return I("il2cpp::runtime::icall_array_find_all_i4", 2, true, true, false,
                        new[] { "a", "" });
                // arrgen1: the int64 and string families (the result is a NEW
                // array of the matching elements -- a managed reference).
                if (genArgs != null && genArgs[0] == 0x0A)   // FindAll<long>
                    return I("il2cpp::runtime::icall_array_find_all_i8", 2, true, true, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0E)   // FindAll<string>
                    return I("il2cpp::runtime::icall_array_find_all_str", 2, true, true, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0D)   // FindAll<double>
                    return I("il2cpp::runtime::icall_array_find_all_d", 2, true, true, false,
                        new[] { "a", "" });
                // arrgen1b: the float family (a NEW float[]; the double-slot
                // predicate ABI).
                if (genArgs != null && genArgs[0] == 0x0C)   // FindAll<float>
                    return I("il2cpp::runtime::icall_array_find_all_f", 2, true, true, false,
                        new[] { "a", "" });
                // arrgen1b: the uint64 family (a NEW ulong[]).
                if (genArgs != null && genArgs[0] == 0x0B)   // FindAll<ulong>
                    return I("il2cpp::runtime::icall_array_find_all_u8", 2, true, true, false,
                        new[] { "a", "" });
                // arrgen1b: the narrow-integral families (the int32 predicate ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns4)
                    return I($"il2cpp::runtime::icall_array_find_all{ns4}", 2, true, true, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_find_all_str", 2, true, true, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_find_all_i8", 2, true, true, false, new[] { "a", "" });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.FindAll<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:1111]");
            case "Exists":
                // bool Array.Exists<int>(int32[], Predicate<int>): 1 when any
                // element matches, 0 otherwise. The int32 result rides the bool
                // slot (M2: bool in an int32_t carrier).
                if (genArgs != null && genArgs[0] == 0x08)   // Exists<int>
                    return I("il2cpp::runtime::icall_array_exists_i4", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1: the int64 / string / double families (the bool
                // result rides the int32 slot).
                if (genArgs != null && genArgs[0] == 0x0A)   // Exists<long>
                    return I("il2cpp::runtime::icall_array_exists_i8", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0E)   // Exists<string>
                    return I("il2cpp::runtime::icall_array_exists_str", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0D)   // Exists<double>
                    return I("il2cpp::runtime::icall_array_exists_d", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the float family (the bool result rides the int32
                // slot; the double-slot predicate ABI).
                if (genArgs != null && genArgs[0] == 0x0C)   // Exists<float>
                    return I("il2cpp::runtime::icall_array_exists_f", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the uint64 family.
                if (genArgs != null && genArgs[0] == 0x0B)   // Exists<ulong>
                    return I("il2cpp::runtime::icall_array_exists_u8", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the narrow-integral families (the bool result rides
                // the int32 slot; the int32 predicate ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns5)
                    return I($"il2cpp::runtime::icall_array_exists{ns5}", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_exists_str", 2, true, false, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_exists_i8", 2, true, false, false, new[] { "a", "" });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.Exists<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:1089]");
            case "TrueForAll":
                // bool Array.TrueForAll<int>(int32[], Predicate<int>): 1 when
                // EVERY element matches (empty vacuously true).
                if (genArgs != null && genArgs[0] == 0x08)   // TrueForAll<int>
                    return I("il2cpp::runtime::icall_array_true_for_all_i4", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1: the int64 / string / double families (the bool
                // result rides the int32 slot).
                if (genArgs != null && genArgs[0] == 0x0A)   // TrueForAll<long>
                    return I("il2cpp::runtime::icall_array_true_for_all_i8", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0E)   // TrueForAll<string>
                    return I("il2cpp::runtime::icall_array_true_for_all_str", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && genArgs[0] == 0x0D)   // TrueForAll<double>
                    return I("il2cpp::runtime::icall_array_true_for_all_d", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the float family (the bool result rides the int32
                // slot; the double-slot predicate ABI).
                if (genArgs != null && genArgs[0] == 0x0C)   // TrueForAll<float>
                    return I("il2cpp::runtime::icall_array_true_for_all_f", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the uint64 family.
                if (genArgs != null && genArgs[0] == 0x0B)   // TrueForAll<ulong>
                    return I("il2cpp::runtime::icall_array_true_for_all_u8", 2, true, false, false,
                        new[] { "a", "" });
                // arrgen1b: the narrow-integral families (the bool result rides
                // the int32 slot; the int32 predicate ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns6)
                    return I($"il2cpp::runtime::icall_array_true_for_all{ns6}", 2, true, false, false,
                        new[] { "a", "" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_true_for_all_str", 2, true, false, false, new[] { "a", "" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_true_for_all_i8", 2, true, false, false, new[] { "a", "" });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.TrueForAll<T> generic " +
                    $"argument 0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element " +
                    "kind/box descriptor [Reference Source mscorlib/system/array.cs:1903]");
            case "ConvertAll":
            {
                // BATCH4: the general ConvertAll<TInput,TOutput> surface over the
                // int32 / int64 / double / reference element families. A NEW array
                // of the same length whose element i is converter(array[i]); the
                // source is never modified. The result is a managed reference
                // (ReturnsRef) -- the icall returns Il2CppArray*; the result temp
                // stays Il2CppObject*.
                byte ti = genArgs is { Length: >= 1 } ? genArgs[0] : (byte)0x08;
                byte to = genArgs is { Length: >= 2 } ? genArgs[1] : ti;
                string? entry = ConvertAllEntry(ti, to);
                if (entry != null)
                    return I(entry, 2, true, true, false, new[] { "a", "" });
                // The narrow-integral families (and their mixed pairs with the
                // typed families) ride the kind-parameterized dynamic kernel.
                string? inKind = ConvKindLiteral(ti);
                string? outKind = ConvKindLiteral(to);
                if (inKind != null && outKind != null)
                    return I("il2cpp::runtime::icall_array_convert_all_dyn", 2, true, true, false,
                        new[] { "a", "" }, appendLiterals: new[] { inKind, outKind });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type converter descriptor): " +
                    $"Array.ConvertAll<TInput,TOutput> generic arguments 0x{ti:X2},0x{to:X2} -- a BCL value " +
                    "type (or a closed GENERICINST whose value/reference nature is not in the MethodSpec blob) " +
                    "has no materialized converter cell/box descriptor " +
                    "[Reference Source mscorlib/system/array.cs:1031]");
            }
            case "Resize":
                // void Array.Resize<int>(ref int32[] array, int newSize): the
                // byref array slot arrives as an ldloca LocAddrExpr -- the
                // ADDRESS of the caller's Il2CppObject* array local. The icall
                // declares Il2CppArray** (il2cpp_array_ext2.h:191), so the
                // address takes the "addr:a" exact-reinterpret cast
                // (CodeGenerator EmitIcallCallArgs) -- "" would pass
                // Il2CppObject** and fail to compile.
                if (genArgs != null && genArgs[0] == 0x08)   // Resize<int>
                    return I("il2cpp::runtime::icall_array_resize_i4", 2, false, false, false,
                        new[] { "addr:a", "i" });
                // arrgen1: the int64 / string families (the same byref array
                // slot ABI -- "addr:a" -- and the same net48 contract).
                if (genArgs != null && genArgs[0] == 0x0A)   // Resize<long>
                    return I("il2cpp::runtime::icall_array_resize_i8", 2, false, false, false,
                        new[] { "addr:a", "i" });
                if (genArgs != null && genArgs[0] == 0x0E)   // Resize<string>
                    return I("il2cpp::runtime::icall_array_resize_str", 2, false, false, false,
                        new[] { "addr:a", "i" });
                // arrgen1: the double family (the same byref array slot ABI).
                if (genArgs != null && genArgs[0] == 0x0D)   // Resize<double>
                    return I("il2cpp::runtime::icall_array_resize_d", 2, false, false, false,
                        new[] { "addr:a", "i" });
                // arrgen1b: the float family (the same byref array slot ABI).
                if (genArgs != null && genArgs[0] == 0x0C)   // Resize<float>
                    return I("il2cpp::runtime::icall_array_resize_f", 2, false, false, false,
                        new[] { "addr:a", "i" });
                // arrgen1b: the uint64 family (the same byref array slot ABI).
                if (genArgs != null && genArgs[0] == 0x0B)   // Resize<ulong>
                    return I("il2cpp::runtime::icall_array_resize_u8", 2, false, false, false,
                        new[] { "addr:a", "i" });
                // arrgen1b: the narrow-integral families (the same byref array
                // slot ABI).
                if (genArgs != null && NarrowSuffix(genArgs[0]) is { } ns7)
                    return I($"il2cpp::runtime::icall_array_resize{ns7}", 2, false, false, false,
                        new[] { "addr:a", "i" });
                if (genArgs != null && IsRefElemFamily(genArgs[0]))
                    return I("il2cpp::runtime::icall_array_resize_str", 2, false, false, false,
                        new[] { "addr:a", "i" });
                if (genArgs != null && (genArgs[0] == 0x18 || IsInt64CellValueType(genArgs, genArgNames)))
                    return I("il2cpp::runtime::icall_array_resize_i8", 2, false, false, false,
                        new[] { "addr:a", "i" });
                if (genArgs != null && genArgs[0] == 0x19)
                    return I("il2cpp::runtime::icall_array_resize_u8", 2, false, false, false,
                        new[] { "addr:a", "i" });
                throw new NotSupportedException(
                    "LOUD whitelist (unmaterialized value-type array descriptor): Array.Resize<T> generic argument " +
                    $"0x{(genArgs?[0] ?? 0):X2} -- a BCL value type with no materialized array element kind/box " +
                    "descriptor [Reference Source mscorlib/system/array.cs:50]");
            case "GetLength":
                return I("il2cpp::runtime::icall_array_get_length", 1, true, false, true, new[] { "", "i" });
            case "GetUpperBound":
                return I("il2cpp::runtime::icall_array_get_upper_bound", 1, true, false, true, new[] { "", "i" });
            case "get_Length":
                // a5reds (raudit4): the receiver cast "a" is REQUIRED -- a
                // System.Array-typed receiver arrives as Il2CppObject* (e.g.
                // the `(Array)Enum.GetValues(...)` isinst result) and the
                // inline header helper takes Il2CppArray*; without the cast
                // the emitted argument is a raw Il2CppObject* (C++ error).
                // Normal `arr.Length` rides IL ldlen (CodeGenerator wraps it),
                // so only this callvirt path needed the cast.
                return I("il2cpp::il2cpp_array_get_length", 0, true, false, true, new[] { "a" });
            case "get_Rank":
                // Array.get_Rank: the number of dimensions (net48: a multi-
                // dimensional array returns its rank, a one-dimensional array
                // returns 1). The runtime icall reads the rank field the
                // allocator stamps on every array (il2cpp_array_ext.h); the
                // one-dimensional scope always carries 1 -- the multi-
                // dimensional creation path is future work, the field is
                // already in place. Same shape as get_Length above (receiver
                // passes through uncast).
                return I("il2cpp::runtime::icall_array_get_rank", 0, true, false, true);
            // ICALL-C (2026-08-07): the three rebuilt array icalls
            // (icallc2 report section 3 -- arrayext.cpp). All instance, receiver
            // passes through uncast (the il2cpp_array_ext.h signatures take
            // Il2CppObject*).
            case "get_LongLength":
                // long get_LongLength: the length widened to int64 (net48
                // LongLength == Length for 1-D) -- "l" result kind.
                return I("il2cpp::runtime::icall_array_get_long_length", 0, true, false, true, null, "l");
            case "GetLowerBound":
                // int GetLowerBound(int dimension): zero-based 1-D returns 0;
                // the runtime validates dimension != 0 (IndexOutOfRange).
                return I("il2cpp::runtime::icall_array_get_lower_bound", 1, true, false, true, new[] { "", "i" });
            case "Initialize":
                // void Initialize(): non-reference arrays zero their element
                // storage (memset) -- the observable net48 contract.
                return I("il2cpp::runtime::icall_array_initialize", 0, false, false, true);
            // ARRAYEXTRA (2026-08-22): the GetValue/SetValue boxed element
            // accessors (arrayextra.h -- the transpiler-facing direct-call
            // domain built for exactly this wiring). Both dispatch on the
            // array's element kind AT RUNTIME (isRefElem/elemSize), so one
            // mapping covers every supported kind: reference arrays move the
            // slot itself, byte/int32/int64 value arrays box/unbox through the
            // shared boxing.cpp layout.
            case "GetValue":
                // net48 GetValue overloads (array.cs:353/:368/:388/:399/:413/
                // :423/:435/:449). The 1-D and the multi-dimensional index
                // shapes all box through the arrayextra element core; the
                // params int[]/long[] forms take the index array.
                if (count == 1 && ps is { Length: >= 1 } && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_array_get_value", 1, true, true, true, new[] { "a", "i" });
                if (count == 1 && ps is { Length: >= 1 } && ps[0] == 0x0A)
                    return I("il2cpp::il2cpp_array_get_value_long", 1, true, true, true, new[] { "a", "l" });
                if (count == 1 && ps is { Length: >= 1 } && ps[0] == 0x1D)
                {
                    var idxDef = SortedSetParamDefName(paramTypes, 0);
                    if (idxDef == "l[]")
                        return I("il2cpp::il2cpp_array_get_value_indices_long", 1, true, true, true, new[] { "a", "a" });
                    return I("il2cpp::il2cpp_array_get_value_indices", 1, true, true, true, new[] { "a", "a" });
                }
                if (count == 2 && ps is { Length: >= 2 } && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::il2cpp_array_get_value_2d", 2, true, true, true, new[] { "a", "i", "i" });
                if (count == 2 && ps is { Length: >= 2 } && ps[0] == 0x0A && ps[1] == 0x0A)
                    return I("il2cpp::il2cpp_array_get_value_2d_long", 2, true, true, true, new[] { "a", "l", "l" });
                if (count == 3 && ps is { Length: >= 3 } && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::il2cpp_array_get_value_3d", 3, true, true, true, new[] { "a", "i", "i", "i" });
                if (count == 3 && ps is { Length: >= 3 } && ps[0] == 0x0A && ps[1] == 0x0A && ps[2] == 0x0A)
                    return I("il2cpp::il2cpp_array_get_value_3d_long", 3, true, true, true, new[] { "a", "l", "l", "l" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Array.GetValue arity {count} / index bytes " +
                    $"0x{(ps is { Length: >= 1 } gp ? gp[0] : 0):X2} -- net48 has (int), (long), (int,int), " +
                    "(int,int,int), (long,long), (long,long,long), (params int[]) and (params long[]) only " +
                    "[Reference Source mscorlib/system/array.cs:353-449]");
            case "SetValue":
                // net48 SetValue overloads (array.cs:482/:494/:505/:516/:532/
                // :542/:554/:568). The value slot is the object reference; the
                // indices select the 1-D / multi-dimensional / params forms.
                if (count == 2 && ps is { Length: >= 2 } && ps[1] == 0x08)
                    return I("il2cpp::il2cpp_array_set_value", 2, false, false, true, new[] { "a", "", "i" });
                if (count == 2 && ps is { Length: >= 2 } && ps[1] == 0x0A)
                    return I("il2cpp::il2cpp_array_set_value_long", 2, false, false, true, new[] { "a", "", "l" });
                if (count == 2 && ps is { Length: >= 2 } && ps[1] == 0x1D)
                {
                    var idxDef = SortedSetParamDefName(paramTypes, 1);
                    if (idxDef == "l[]")
                        return I("il2cpp::il2cpp_array_set_value_indices_long", 2, false, false, true, new[] { "a", "", "a" });
                    return I("il2cpp::il2cpp_array_set_value_indices", 2, false, false, true, new[] { "a", "", "a" });
                }
                if (count == 3 && ps is { Length: >= 3 } && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::il2cpp_array_set_value_2d", 3, false, false, true, new[] { "a", "", "i", "i" });
                if (count == 3 && ps is { Length: >= 3 } && ps[1] == 0x0A && ps[2] == 0x0A)
                    return I("il2cpp::il2cpp_array_set_value_2d_long", 3, false, false, true, new[] { "a", "", "l", "l" });
                if (count == 4 && ps is { Length: >= 4 } && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::il2cpp_array_set_value_3d", 4, false, false, true, new[] { "a", "", "i", "i", "i" });
                if (count == 4 && ps is { Length: >= 4 } && ps[1] == 0x0A && ps[2] == 0x0A && ps[3] == 0x0A)
                    return I("il2cpp::il2cpp_array_set_value_3d_long", 4, false, false, true, new[] { "a", "", "l", "l", "l" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Array.SetValue arity {count} / index bytes " +
                    $"0x{(ps is { Length: >= 2 } sp ? sp[1] : 0):X2} -- net48 has (object,int), (object,long), " +
                    "(object,int,int), (object,int,int,int), (object,long,long), (object,long,long,long), " +
                    "(object,params int[]) and (object,params long[]) only " +
                     "[Reference Source mscorlib/system/array.cs:472-568]");
            // P6: Array.GetCorElementTypeOfElementType -- returns the CorElementType
            // of the array's element type (stub: returns 4 = ELEMENT_TYPE_I4).
            case "GetCorElementTypeOfElementType": return I("il2cpp::runtime::icall_array_get_cor_element_type_of_element_type", 1, true, false, false);
            case "GetDataPtrOffsetInternal": return I("il2cpp::runtime::icall_array_get_data_ptr_offset_internal", 0, true, true, false);
            // netcore31/net6 audit gap closure:
            // GetElementSize() -- INSTANCE 0-arg form (metadata-verified:
            // count=0 ret=I4): the receiver's element stride in bytes
            // (icall_array_get_element_size, arrayext.cpp).
            case "GetElementSize":
                if (count == 0)
                    return I("il2cpp::runtime::icall_array_get_element_size", 0, true, false, true);
                return null;
            // netcore31 audit gap closure: GetRawArrayData() -- INSTANCE
            // `ref byte` return (raw sig verified: 20-00-10-05). The runtime
            // entry returns the element-data pointer; the "ptr" result kind
            // carries it in an intptr_t slot (the M4.5 IntPtr carrier).
            case "GetRawArrayData":
                if (count == 0)
                    return I("il2cpp::runtime::icall_array_get_raw_data", 0, true, false, true, null, "ptr");
                return null;
            // InternalGetValue(index) -- metadata-verified shape: 1 arg of
            // NATIVE INT (0x18 -- the net6 internal index carrier), object
            // return. Same boxed-element read as GetValue(int)
            // (arrayextra.h runtime kind dispatch); the index rides the
            // int32 slot ("i" cast) -- exact because the valid index range
            // of the one-dimensional arrays IS int32.
            case "InternalGetValue":
                if (count == 1 && ps is { Length: >= 1 } && ps[0] == 0x18)
                    return I("il2cpp::il2cpp_array_get_value", 1, true, true, true, new[] { "a", "i" });
                return null;
            default: return null;
        }
    }
}
