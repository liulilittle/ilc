namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ArraySort -- System.Array direct-call mappings (arraysort.h).
// Owners: System.Array (Sort/IndexOf/BinarySearch/CopyTo)
//
// This domain maps the GENERATED-CODE-facing typed API in
// src/runtime/include/internal/arraysort.h (il2cpp_array_sort_int32/uint32/
// int64/string/object, il2cpp_array_copy_to, il2cpp_array_index_of_*,
// il2cpp_array_binary_search_*). The generic-instantiation overloads
// (Sort<T>/IndexOf<T>/BinarySearch<T> -- MethodSpec tokens) dispatch on the
// generic argument bytes (genArgs[0]: 0x08 i4, 0x09 u4, 0x0A i8, 0x0E string,
// 0x1C object); the no-comparer family is separated from the Comparison<T>
// overloads by the parameter count (1 vs 2).
//
// Dispatch reality: BclResolveCore tries TryMapArray FIRST (?? fallback).
// MethodMap.Array.cs claims the icall-layer variants (il2cpp_array_ext.h /
// _ext2.h: int32/string sorts, int32 index-of, int32/string binary-search)
// and YIELDS the rest -- uint32/int64 sorts, int64/string index-of, int64
// binary-search (and the 1-arg sort shapes those instantiate) -- so those
// names fall through to THIS file's arraysort.h direct calls. CopyTo has no
// TryMapArray case and always lands here. Shapes neither domain can map
// throw NotSupportedException (LOUD) in the claiming file, so nothing falls
// to the bogus generated-name path.
//
// Delegate ABI (Sort<T>(T[], Comparison<T>)): WIRED since cg8-b23. The
// System.Comparison`1 delegate construction (newobj placeholder + ctor
// rewrite) is handled by the CodeGenerator.cs CG8 machinery (the same
// il2cpp_delegate_new single-cast static methodPtr ABI as Predicate/
// Converter), so the delegate form routes through MethodMap.Array.cs to the
// runtime delegate-sort entries (arraysort.h il2cpp_array_sort_int32_comparison
// / _ref_comparison / the wide _int64/_double/_float_comparison family,
// tested in test_arraysort). B8-1: the count==2 arm is now claimed by
// MethodMap.Array.cs for EVERY wired family (i4/u4/i8/f/d/string/object/class),
// so this file's count==2 throw is unreachable for them -- it remains only as
// a defensive LOUD guard for a shape the icall layer did not claim.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapArraySort(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string[]? genArgNames, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Sort":
                if (genArgs == null) return null;
                if (count == 1)
                {
                    // Array.Sort<T>(T[]): the no-comparer family -- typed
                    // ascending in-place sorts (arraysort.h). The runtime
                    // entry takes Il2CppArray* -- "a" cast on the array
                    // argument (CodeGenerator EmitIcallCallArgs; the icall
                    // layer's Il2CppObject* params need no cast).
                    return genArgs[0] switch
                    {
                        0x08 => I("il2cpp::il2cpp_array_sort_int32", 1, false, false, false, new[] { "a" }),
                        0x09 => I("il2cpp::il2cpp_array_sort_uint32", 1, false, false, false, new[] { "a" }),
                        0x0A => I("il2cpp::il2cpp_array_sort_int64", 1, false, false, false, new[] { "a" }),
                        0x0C => I("il2cpp::il2cpp_array_sort_float", 1, false, false, false, new[] { "a" }),
                        0x0D => I("il2cpp::il2cpp_array_sort_double", 1, false, false, false, new[] { "a" }),
                        0x0E => I("il2cpp::il2cpp_array_sort_string", 1, false, false, false, new[] { "a" }),
                        // arrgen1b: uint64 (0x0B) rides the int64 direct-call
                        // sort (the 8-byte cell; the bits are identical).
                        0x0B => I("il2cpp::il2cpp_array_sort_int64", 1, false, false, false, new[] { "a" }),
                        // Narrow integrals: the kind literal selects the cell
                        // width (arraysort.cpp).
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 =>
                            I("il2cpp::il2cpp_array_sort_narrow", 1, false, false, false, new[] { "a" },
                                appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                        // IntPtr / UIntPtr / DateTime / TimeSpan / reference
                        // generic instantiations: the element-kind / native
                        // width sorters.
                        0x18 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        0x19 => I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        0x13 or 0x1D or 0x14 or 0x1B or 0x12 or 0x1C =>
                            I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        _ when IsInt64CellValueType(genArgs, genArgNames) =>
                            I("il2cpp::il2cpp_array_sort_any", 1, false, false, false, new[] { "a" }),
                        _ => throw new NotSupportedException(
                            $"LOUD whitelist (unmaterialized value-type array descriptor): Array.Sort<T> generic " +
                            $"argument 0x{genArgs[0]:X2} -- a BCL value type with no materialized array element " +
                            "kind/box descriptor [Reference Source mscorlib/system/array.cs:1779]"),
                    };
                }
                if (count == 2)
                {
                    // Array.Sort<T>(T[], Comparison<T> | IComparer<T>): CLAIMED
                    // by MethodMap.Array.cs for every wired element family (the
                    // count==2 arms dispatch on the second parameter's decoded
                    // MemberRef signature: System.Collections.Generic.IComparer`1
                    // -> the comparer sort, everything else -> the delegate
                    // sort). This throw is a DEFENSIVE guard only: reaching it
                    // means the icall layer yielded a count==2 shape it does not
                    // know, and a silent fall-through would emit a bogus
                    // generated-name call.
                    throw new NotSupportedException(
                        "LOUD whitelist (unreachable defensive guard / unmaterialized value-type comparer " +
                        "descriptor): Array.Sort<T> with 2 args reached the ArraySort direct-call domain -- the " +
                        "icall layer (MethodMap.Array.cs) claims every wired Comparison<T>/IComparer<T> family, so " +
                        "only an unmaterialized value-type comparer ABI can reach this arm " +
                        "[Reference Source mscorlib/system/array.cs:1805-1889]");
                }
                return null;
            case "CopyTo":
                // instance Array.CopyTo(Array, int) -- this is the SOURCE
                // array: il2cpp_array_copy_to(src = this, dst, dstIndex);
                // both array slots take Il2CppArray* ("a" cast).
                return I("il2cpp::il2cpp_array_copy_to", 2, false, false, true, new[] { "a", "a", "i" });
            // nb9 P0 (arity mispair): IndexOf/LastIndexOf/BinarySearch map the
            // REAL arity. count 2 = (array, value); 3 = (array, value,
            // startIndex) for the scans or (array, value, comparer) for the
            // search; 4 = (array, value, startIndex, count) / (array, index,
            // length, value); 5 = (array, index, length, value, comparer).
            // Every entry takes the arguments in exactly the runtime order.
            case "IndexOf":
                if (genArgs == null) return null;
                // A3: DateTime/TimeSpan generic instantiations (0x11 VALUETYPE).
                // The value parameter is the runtime 8-byte struct -- no box is
                // needed, so the element-kind scans compare ticks directly.
                // The element type comes from the MethodSpec generic argument
                // NAME (the only authoritative source): the StackFold
                // arg0Type/arg1Type hints exist only for temporaries carrying
                // an objTypes entry, so a plain local/parameter/field needle
                // used to fall through to the NotSupported arm below.
                if (genArgs.Length == 1 && genArgs[0] == 0x11
                    && genArgNames is { Length: >= 1 })
                {
                    if (genArgNames[0] == "System.DateTime")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_index_of_datetime", 2, true, false, false, new[] { "a", "" });
                        if (count == 3)
                            return I("il2cpp::il2cpp_array_index_of_datetime_start", 3, true, false, false, new[] { "a", "", "i" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_index_of_datetime_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                    }
                    if (genArgNames[0] == "System.TimeSpan")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_index_of_timespan", 2, true, false, false, new[] { "a", "" });
                        if (count == 3)
                            return I("il2cpp::il2cpp_array_index_of_timespan_start", 3, true, false, false, new[] { "a", "", "i" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_index_of_timespan_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                    }
                }
                return genArgs[0] switch
                {
                    // int32 is claimed by the icall-layer domain (2-arg only);
                    // here the 3/4-arg forms and the other families.
                    0x08 when count == 3 => I("il2cpp::il2cpp_array_index_of_int32_start", 3, true, false, false, new[] { "a", "i", "i" }),
                    // nb9 D17: uint32 scans (equality needs no sign; the
                    // bits pass through the int32 ABI).
                    0x09 when count == 2 => I("il2cpp::runtime::icall_array_index_of_int32", 2, true, false, false),
                    0x09 when count == 3 => I("il2cpp::il2cpp_array_index_of_int32_start", 3, true, false, false, new[] { "a", "i", "i" }),
                    0x09 when count == 4 => I("il2cpp::il2cpp_array_index_of_int32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x08 when count == 4 => I("il2cpp::il2cpp_array_index_of_int32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x0A when count == 2 => I("il2cpp::il2cpp_array_index_of_int64", 2, true, false, false, new[] { "a", "l" }),
                    0x0A when count == 3 => I("il2cpp::il2cpp_array_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x0A when count == 4 => I("il2cpp::il2cpp_array_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    // arrgen1b: uint64 (0x0B) rides the int64 scans (equality
                    // is bitwise, so the unsigned payload compares correctly).
                    0x0B when count == 2 => I("il2cpp::il2cpp_array_index_of_int64", 2, true, false, false, new[] { "a", "l" }),
                    0x0B when count == 3 => I("il2cpp::il2cpp_array_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x0B when count == 4 => I("il2cpp::il2cpp_array_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    0x0C when count == 2 => I("il2cpp::il2cpp_array_index_of_float_start", 2, true, false, false, new[] { "a", "d" }, appendZero: true),
                    0x0C when count == 3 => I("il2cpp::il2cpp_array_index_of_float_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0C when count == 4 => I("il2cpp::il2cpp_array_index_of_float_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0D when count == 2 => I("il2cpp::il2cpp_array_index_of_double", 2, true, false, false, new[] { "a", "d" }),
                    0x0D when count == 3 => I("il2cpp::il2cpp_array_index_of_double_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0D when count == 4 => I("il2cpp::il2cpp_array_index_of_double_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0E when count == 2 => I("il2cpp::il2cpp_array_index_of_string", 2, true, false, false, new[] { "a", "" }),
                    0x0E when count == 3 => I("il2cpp::il2cpp_array_index_of_string_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x0E when count == 4 => I("il2cpp::il2cpp_array_index_of_string_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 2 => I("il2cpp::il2cpp_array_index_of_object_start", 2, true, false, false, new[] { "a", "" }, appendZero: true),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 3 => I("il2cpp::il2cpp_array_index_of_object_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 4 => I("il2cpp::il2cpp_array_index_of_object_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    // Narrow integrals (kind literal selects the cell width).
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 2 =>
                        I("il2cpp::il2cpp_array_index_of_narrow", 2, true, false, false, new[] { "a", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 3 =>
                        I("il2cpp::il2cpp_array_index_of_narrow_start", 3, true, false, false, new[] { "a", "i", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 4 =>
                        I("il2cpp::il2cpp_array_index_of_narrow_range", 4, true, false, false, new[] { "a", "i", "i", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    // IntPtr/UIntPtr: equality is bitwise, so the int64 scan is
                    // exact for both.
                    0x18 or 0x19 when count == 2 => I("il2cpp::il2cpp_array_index_of_int64", 2, true, false, false, new[] { "a", "l" }),
                    0x18 or 0x19 when count == 3 => I("il2cpp::il2cpp_array_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x18 or 0x19 when count == 4 => I("il2cpp::il2cpp_array_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    _ => throw new NotSupportedException(
                        "LOUD whitelist (unmaterialized value-type array descriptor): Array.IndexOf<T> generic " +
                        $"argument 0x{genArgs[0]:X2} with {count} arg(s) -- a BCL value type with no materialized " +
                        "array element kind/box descriptor [Reference Source mscorlib/system/array.cs:1370-1391]"),
                };
            case "LastIndexOf":
                if (genArgs == null) return null;
                // DateTime/TimeSpan: ticks-only equality (Kind ignored), same
                // genArgNames authority as IndexOf.
                if (genArgs.Length == 1 && genArgs[0] == 0x11
                    && genArgNames is { Length: >= 1 })
                {
                    if (genArgNames[0] == "System.DateTime")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_last_index_of_datetime", 2, true, false, false, new[] { "a", "" });
                        if (count == 3)
                            return I("il2cpp::il2cpp_array_last_index_of_datetime_start", 3, true, false, false, new[] { "a", "", "i" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_last_index_of_datetime_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                    }
                    if (genArgNames[0] == "System.TimeSpan")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_last_index_of_timespan", 2, true, false, false, new[] { "a", "" });
                        if (count == 3)
                            return I("il2cpp::il2cpp_array_last_index_of_timespan_start", 3, true, false, false, new[] { "a", "", "i" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_last_index_of_timespan_range", 4, true, false, false, new[] { "a", "", "i", "i" });
                    }
                }
                return genArgs[0] switch
                {
                    0x08 when count == 3 => I("il2cpp::il2cpp_array_last_index_of_int32_start", 3, true, false, false, new[] { "a", "i", "i" }),
                    0x09 when count == 2 => I("il2cpp::runtime::icall_array_last_index_of_int32", 2, true, false, false),
                    0x09 when count == 3 => I("il2cpp::il2cpp_array_last_index_of_int32_start", 3, true, false, false, new[] { "a", "i", "i" }),
                    0x09 when count == 4 => I("il2cpp::il2cpp_array_last_index_of_int32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x08 when count == 4 => I("il2cpp::il2cpp_array_last_index_of_int32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x0A when count == 2 => I("il2cpp::il2cpp_array_last_index_of_long", 2, true, false, false, new[] { "a", "l" }),
                    0x0A when count == 3 => I("il2cpp::il2cpp_array_last_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x0A when count == 4 => I("il2cpp::il2cpp_array_last_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    // arrgen1b: uint64 (0x0B) rides the int64 reverse scans.
                    0x0B when count == 2 => I("il2cpp::il2cpp_array_last_index_of_long", 2, true, false, false, new[] { "a", "l" }),
                    0x0B when count == 3 => I("il2cpp::il2cpp_array_last_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x0B when count == 4 => I("il2cpp::il2cpp_array_last_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    0x0C when count == 2 => I("il2cpp::il2cpp_array_last_index_of_float", 2, true, false, false, new[] { "a", "d" }),
                    0x0C when count == 3 => I("il2cpp::il2cpp_array_last_index_of_float_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0C when count == 4 => I("il2cpp::il2cpp_array_last_index_of_float_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0D when count == 2 => I("il2cpp::il2cpp_array_last_index_of_double", 2, true, false, false, new[] { "a", "d" }),
                    0x0D when count == 3 => I("il2cpp::il2cpp_array_last_index_of_double_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0D when count == 4 => I("il2cpp::il2cpp_array_last_index_of_double_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0E when count == 2 => I("il2cpp::il2cpp_array_last_index_of_string", 2, true, false, false, new[] { "a", "" }),
                    0x0E when count == 3 => I("il2cpp::il2cpp_array_last_index_of_string_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x0E when count == 4 => I("il2cpp::il2cpp_array_last_index_of_string_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 2 => I("il2cpp::il2cpp_array_last_index_of_any", 2, true, false, false, new[] { "a", "" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 3 => I("il2cpp::il2cpp_array_last_index_of_object_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 4 => I("il2cpp::il2cpp_array_last_index_of_object_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 2 =>
                        I("il2cpp::il2cpp_array_last_index_of_narrow", 2, true, false, false, new[] { "a", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 3 =>
                        I("il2cpp::il2cpp_array_last_index_of_narrow_start", 3, true, false, false, new[] { "a", "i", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 4 =>
                        I("il2cpp::il2cpp_array_last_index_of_narrow_range", 4, true, false, false, new[] { "a", "i", "i", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x18 or 0x19 when count == 2 => I("il2cpp::il2cpp_array_last_index_of_long", 2, true, false, false, new[] { "a", "l" }),
                    0x18 or 0x19 when count == 3 => I("il2cpp::il2cpp_array_last_index_of_int64_start", 3, true, false, false, new[] { "a", "l", "i" }),
                    0x18 or 0x19 when count == 4 => I("il2cpp::il2cpp_array_last_index_of_int64_range", 4, true, false, false, new[] { "a", "l", "i", "i" }),
                    _ => throw new NotSupportedException(
                        "LOUD whitelist (unmaterialized value-type array descriptor): Array.LastIndexOf<T> generic " +
                        $"argument 0x{genArgs[0]:X2} with {count} arg(s) -- a BCL value type with no materialized " +
                        "array element kind/box descriptor [Reference Source mscorlib/system/array.cs:1509-1529]"),
                };
            case "BinarySearch":
                if (genArgs == null) return null;
                // A3: DateTime/TimeSpan generic instantiations (0x11 VALUETYPE),
                // by value -- the search compares ticks (Kind ignored). Same
                // genArgNames authority as IndexOf above.
                if (genArgs.Length == 1 && genArgs[0] == 0x11
                    && genArgNames is { Length: >= 1 })
                {
                    if (genArgNames[0] == "System.DateTime")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_binary_search_datetime", 2, true, false, false, new[] { "a", "" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_binary_search_datetime_range", 4, true, false, false, new[] { "a", "i", "i", "" });
                    }
                    if (genArgNames[0] == "System.TimeSpan")
                    {
                        if (count == 2)
                            return I("il2cpp::il2cpp_array_binary_search_timespan", 2, true, false, false, new[] { "a", "" });
                        if (count == 4)
                            return I("il2cpp::il2cpp_array_binary_search_timespan_range", 4, true, false, false, new[] { "a", "i", "i", "" });
                    }
                }
                return genArgs[0] switch
                {
                    0x08 when count == 2 => I("il2cpp::il2cpp_array_binary_search_int32", 2, true, false, false, new[] { "a", "i" }),
                    0x09 when count == 2 => I("il2cpp::il2cpp_array_binary_search_uint32", 2, true, false, false, new[] { "a", "i" }),
                    0x09 when count == 4 => I("il2cpp::il2cpp_array_binary_search_uint32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x09 when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_uint32", 3, true, false, false, new[] { "a", "i", "" }),
                    0x08 when count == 4 => I("il2cpp::il2cpp_array_binary_search_int32_range", 4, true, false, false, new[] { "a", "i", "i", "i" }),
                    0x0A when count == 2 => I("il2cpp::il2cpp_array_binary_search_int64", 2, true, false, false, new[] { "a", "l" }),
                    0x0A when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64", 3, true, false, false, new[] { "a", "l", "" }),
                    0x0A when count == 4 => I("il2cpp::il2cpp_array_binary_search_int64_range", 4, true, false, false, new[] { "a", "i", "i", "l" }),
                    0x0A when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64_range", 5, true, false, false, new[] { "a", "i", "i", "l", "" }),
                    // arrgen1b: uint64 (0x0B) rides the int64 searches (the
                    // 8-byte cell; the bits are identical).
                    0x0B when count == 2 => I("il2cpp::il2cpp_array_binary_search_int64", 2, true, false, false, new[] { "a", "l" }),
                    0x0B when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64", 3, true, false, false, new[] { "a", "l", "" }),
                    0x0B when count == 4 => I("il2cpp::il2cpp_array_binary_search_int64_range", 4, true, false, false, new[] { "a", "i", "i", "l" }),
                    0x0B when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64_range", 5, true, false, false, new[] { "a", "i", "i", "l", "" }),
                    0x0C when count == 2 => I("il2cpp::il2cpp_array_binary_search_float", 2, true, false, false, new[] { "a", "d" }),
                    0x0C when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_float", 3, true, false, false, new[] { "a", "d", "" }),
                    0x0C when count == 4 => I("il2cpp::il2cpp_array_binary_search_float_range", 4, true, false, false, new[] { "a", "i", "i", "d" }),
                    0x0C when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_float_range", 5, true, false, false, new[] { "a", "i", "i", "d", "" }),
                    0x0D when count == 2 => I("il2cpp::il2cpp_array_binary_search_double", 2, true, false, false, new[] { "a", "d" }),
                    0x0D when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_double", 3, true, false, false, new[] { "a", "d", "" }),
                    0x0D when count == 4 => I("il2cpp::il2cpp_array_binary_search_double_range", 4, true, false, false, new[] { "a", "i", "i", "d" }),
                    0x0D when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_double_range", 5, true, false, false, new[] { "a", "i", "i", "d", "" }),
                    0x0E when count == 2 => I("il2cpp::il2cpp_array_binary_search_string", 2, true, false, false, new[] { "a", "" }),
                    0x0E when count == 4 => I("il2cpp::il2cpp_array_binary_search_string_range", 4, true, false, false, new[] { "a", "i", "i", "" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 2 => I("il2cpp::il2cpp_array_binary_search_object", 2, true, false, false, new[] { "a", "" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 4 => I("il2cpp::il2cpp_array_binary_search_object_range", 4, true, false, false, new[] { "a", "i", "i", "" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_ref", 3, true, false, false, new[] { "a", "", "" }),
                    0x12 or 0x1C or 0x13 or 0x1D or 0x14 or 0x1B when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_ref_range", 5, true, false, false, new[] { "a", "i", "i", "", "" }),
                    // Narrow integrals (kind literal selects the cell width).
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 2 =>
                        I("il2cpp::il2cpp_array_binary_search_narrow", 2, true, false, false, new[] { "a", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 3 =>
                        I("il2cpp::il2cpp_array_binary_search_narrow_icomparer", 3, true, false, false, new[] { "a", "i", "" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 4 =>
                        I("il2cpp::il2cpp_array_binary_search_narrow_range", 4, true, false, false, new[] { "a", "i", "i", "i" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 when count == 5 =>
                        I("il2cpp::il2cpp_array_binary_search_narrow_icomparer_range", 5, true, false, false, new[] { "a", "i", "i", "i", "" },
                            appendLiterals: new[] { NarrowKindLiteral(genArgs[0])! }),
                    // IntPtr (signed) rides the int64 searches; UIntPtr's
                    // comparer-free search rides the unsigned uint64 twin.
                    0x18 or 0x19 when count == 2 => I("il2cpp::il2cpp_array_binary_search_incomparable", 2, true, false, false, new[] { "a", "l" }),
                    0x18 when count == 3 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64", 3, true, false, false, new[] { "a", "l", "" }),
                    0x18 or 0x19 when count == 4 => I("il2cpp::il2cpp_array_binary_search_incomparable_range", 4, true, false, false, new[] { "a", "i", "i", "l" }),
                    0x18 when count == 5 => I("il2cpp::il2cpp_array_binary_search_icomparer_int64_range", 5, true, false, false, new[] { "a", "i", "i", "l", "" }),
                    
                    
                    _ when count == 3 || count == 5 =>
                        throw new NotSupportedException(
                            "LOUD whitelist (unmaterialized value-type comparer descriptor): Array.BinarySearch<T> " +
                            $"generic argument 0x{genArgs[0]:X2} with {count} args -- the comparer-driven search for " +
                            "this value type has no materialized comparer/box ABI " +
                            "[Reference Source mscorlib/system/array.cs:987-1011]"),
                    _ => throw new NotSupportedException(
                        "LOUD whitelist (unmaterialized value-type array descriptor): Array.BinarySearch<T> " +
                        $"generic argument 0x{genArgs[0]:X2} with {count} arg(s) -- a BCL value type with no " +
                        "materialized array element kind/box descriptor " +
                        "[Reference Source mscorlib/system/array.cs:987-1011]"),
                };
            default: return null;
        }
    }
}
