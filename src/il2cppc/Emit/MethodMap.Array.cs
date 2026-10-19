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
    private static ResolvedMethod? TryMapArray(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? paramTypes = null)
    {
        switch (name)
        {
            case "Sort":
                if (genArgs != null)
                {
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
                    return genArgs[0] switch
                    {
                        // Array.Sort<T>(T[]) count==1 -- the no-comparer family:
                        // the icall layer for int32/string, the ArraySort
                        // direct-call domain for uint32/int64 (arraysort.h).
                        0x0E when count == 1 => I("il2cpp::runtime::icall_array_sort_strings", 1, false, false, false),
                        0x08 when count == 1 => I("il2cpp::runtime::icall_array_sort", 1, false, false, false),
                        0x09 or 0x0A or 0x0C or 0x0D when count == 1 => null,
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
                        // a5reds (strsort_ordinal): the SAME generic Sort<T>
                        0x0E or 0x1C or 0x12 when count == 2 =>
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
                        _ => throw new NotSupportedException($"Array.Sort<T> generic argument 0x{genArgs[0]:X2} with {count} arg(s) not wired in M5"),
                    };
                }
                // Non-generic Array.Sort(Array[, IComparer]).
                return count switch
                {
                    1 => I("il2cpp::runtime::icall_array_sort", 1, false, false, false),
                    // D-ICMP-3 (icomparedeep): Array.Sort(Array, IComparer) --
                    // the comparer-driven NON-GENERIC sort (arraysort.h
                    // il2cpp_array_sort_icomparer; a null comparer means the
                    // default comparer, net48). int32 value arrays (elements
                    // boxed per comparison) and reference arrays are supported.
                    2 => I("il2cpp::il2cpp_array_sort_icomparer", 2, false, false, false, new[] { "a", "" }),
                    _ => throw new NotSupportedException($"Array.Sort(Array, ...) with {count} arg(s) not wired in M5"),
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
                        // M6: int64 yields to the ArraySort direct-call
                        // domain (il2cpp_array_binary_search_int64); only
                        // the 2-arg form (ArraySort.cs maps any count to
                        // the 2-param call, so wider arities stay LOUD).
                        // W7: double follows the same yield.
                        0x0A or 0x0D when count == 2 => null,
                        _ => throw new NotSupportedException($"Array.BinarySearch<T> generic argument 0x{genArgs[0]:X2} with {count} arg(s) not wired in M5"),
                    };
                }
                throw new NotSupportedException("Array.BinarySearch(Array, object) non-generic not wired in M5");
            case "IndexOf":
                if (genArgs != null)
                {
                    return genArgs[0] switch
                    {
                        0x08 => I("il2cpp::runtime::icall_array_index_of_int32", 2, true, false, false),
                        // M6: int64/string yield to the ArraySort direct-call
                        // domain (il2cpp_array_index_of_int64/_string); only
                        // the 2-arg form (same arity caveat as BinarySearch).
                        // W7: double follows the same yield.
                        0x0A or 0x0D or 0x0E when count == 2 => null,
                        _ => throw new NotSupportedException($"Array.IndexOf<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
                    };
                }
                return I("il2cpp::runtime::icall_array_index_of", 2, true, false, false);
            case "LastIndexOf":
                if (genArgs != null)
                {
                    // W7: the double/string/long reverse scans (arraysort.h).
                    // Only the 2-arg form (array, value) is wired.
                    if (genArgs[0] == 0x08) return I("il2cpp::runtime::icall_array_last_index_of_int32", 2, true, false, false);
                    if (genArgs[0] == 0x0D && count == 2) return I("il2cpp::il2cpp_array_last_index_of_double", 2, true, false, false, new[] { "a", "d" });
                    if (genArgs[0] == 0x0E && count == 2) return I("il2cpp::il2cpp_array_last_index_of_string", 2, true, false, false, new[] { "a", "" });
                    if (genArgs[0] == 0x0A && count == 2) return I("il2cpp::il2cpp_array_last_index_of_long", 2, true, false, false, new[] { "a", "l" });
                    throw new NotSupportedException($"Array.LastIndexOf<T> generic argument 0x{genArgs[0]:X2} not wired in M5");
                }
                throw new NotSupportedException("Array.LastIndexOf(Array, object) non-generic not wired in M5");
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
                        _ => throw new NotSupportedException($"Array.Empty<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
                    };
                }
                throw new NotSupportedException("Array.Empty non-generic not wired in M5");
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
                            _ => throw new NotSupportedException($"Array.Reverse<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
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
                if (count == 5) return I("il2cpp::runtime::icall_array_copy", 5, false, false, false);
                if (count == 3) return I("il2cpp::runtime::icall_array_copy_3", 3, false, false, false);
                if (count == 6) return I("il2cpp::runtime::icall_array_copy", 6, false, false, false,
                    new[] { "", "i", "", "i", "i", "drop" });
                throw new NotSupportedException($"Array.Copy with {count} arg(s) not wired: the runtime icalls need (src, srcIndex, dst, dstIndex, length) or (src, dst, length)");
            case "ConstrainedCopy":
                // CONSTRAINED (2026-08-22): Array.ConstrainedCopy(Array, int,
                // Array, int, int) -- net48 has ONLY this 5-arg form. The
                // constrained contract (every failure precedes ANY mutation)
                // is exactly what icall_array_copy already implements
                // (arrayext.cpp:188-211: null -> indices -> ranges ->
                // element-kind ArrayTypeMismatch all throw BEFORE the
                // memmove), so the entry is the SAME icall as Copy above.
                if (count == 5) return I("il2cpp::runtime::icall_array_copy", 5, false, false, false);
                throw new NotSupportedException($"Array.ConstrainedCopy with {count} arg(s) not wired: only the (Array, int, Array, int, int) shape exists in net48");
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
                if (genArgs != null && genArgs[0] == 0x08)   // FindIndex<int>
                {
                    // 2-arg overload (array, match): the whole range via the
                    // dedicated icall_array_find_index_2 wrapper.
                    if (ps.Length == 2)
                        return I("il2cpp::runtime::icall_array_find_index_2", 2, true, false, false,
                            new[] { "a", "" });
                    return I("il2cpp::runtime::icall_array_find_index", 4, true, false, false,
                        new[] { "a", "", "i", "i" });
                }
                throw new NotSupportedException(
                    $"Array.FindIndex<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the delegate icall icall_array_find_index (il2cpp_array_ext2.h:145); 2/3-arg overloads fold to the 4-arg shape in the runtime");
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
                if (genArgs != null && genArgs[0] == 0x08)   // Find<int>
                    return I("il2cpp::runtime::icall_array_find_i4", 2, true, false, false,
                        new[] { "a", "" });
                throw new NotSupportedException(
                    $"Array.Find<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the predicate icall icall_array_find_i4 (il2cpp_array_ext2.h:161)");
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
                throw new NotSupportedException(
                    $"Array.FindAll<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the predicate icall icall_array_find_all_i4 (il2cpp_array_ext2.h:201)");
            case "Exists":
                // bool Array.Exists<int>(int32[], Predicate<int>): 1 when any
                // element matches, 0 otherwise. The int32 result rides the bool
                // slot (M2: bool in an int32_t carrier).
                if (genArgs != null && genArgs[0] == 0x08)   // Exists<int>
                    return I("il2cpp::runtime::icall_array_exists_i4", 2, true, false, false,
                        new[] { "a", "" });
                throw new NotSupportedException(
                    $"Array.Exists<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the predicate icall icall_array_exists_i4 (il2cpp_array_ext2.h:166)");
            case "TrueForAll":
                // bool Array.TrueForAll<int>(int32[], Predicate<int>): 1 when
                // EVERY element matches (empty vacuously true).
                if (genArgs != null && genArgs[0] == 0x08)   // TrueForAll<int>
                    return I("il2cpp::runtime::icall_array_true_for_all_i4", 2, true, false, false,
                        new[] { "a", "" });
                throw new NotSupportedException(
                    $"Array.TrueForAll<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the predicate icall icall_array_true_for_all_i4 (il2cpp_array_ext2.h:172)");
            case "ConvertAll":
                // int32[] Array.ConvertAll<int,int>(int32[], Converter<int,int>):
                // a NEW int32[] of the same length; the source is never modified.
                // The result is a managed reference (ReturnsRef) -- the icall
                // returns Il2CppArray*; the result temp stays Il2CppObject*.
                if (genArgs != null && genArgs.Length == 2
                    && genArgs[0] == 0x08 && genArgs[1] == 0x08)   // ConvertAll<int,int>
                    return I("il2cpp::runtime::icall_array_convert_all_i4_i4", 2, true, true, false,
                        new[] { "a", "" });
                // net9/10 E2E icallcase: ConvertAll<string,int>(string[], int.Parse)
                // -- the string source variant (arrayext2 str_i4 icall).
                if (genArgs != null && genArgs.Length == 2
                    && genArgs[0] == 0x0E && genArgs[1] == 0x08)   // ConvertAll<string,int>
                    return I("il2cpp::runtime::icall_array_convert_all_str_i4", 2, true, true, false,
                        new[] { "a", "" });
                // icallwide E2E: ConvertAll<int,long>(int32[], x => (long)x * 2)
                // -- the int64-output variant (arrayext2 i4_i8 icall).
                if (genArgs != null && genArgs.Length == 2
                    && genArgs[0] == 0x08 && genArgs[1] == 0x0A)   // ConvertAll<int,long>
                    return I("il2cpp::runtime::icall_array_convert_all_i4_i8", 2, true, true, false,
                        new[] { "a", "" });
                throw new NotSupportedException(
                    $"Array.ConvertAll<TInput,TOutput> generic arguments 0x{(genArgs?[0] ?? 0):X2},0x{(genArgs is { Length: 2 } g ? g[1] : 0):X2} not wired: only (int32,int32) (0x08,0x08), (string,int32) and (int32,int64) have converter icalls");
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
                throw new NotSupportedException(
                    $"Array.Resize<T> generic argument 0x{(genArgs?[0] ?? 0):X2} not wired: only int32 (0x08) has the resize icall icall_array_resize_i4 (il2cpp_array_ext2.h:191)");
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
                // object GetValue(int index): reference arrays return the slot
                // (may be null); byte/int32/int64 box as System.Byte/Int32/
                // Int64; any other stride throws NotSupportedException. The
                // 1-D scope wires ONLY the (int index) overload -- ps[0] must
                // be I4 (0x08); the long-index / MD / params-int[] overloads
                // stay LOUD (no int64-index entry exists in any runtime
                // domain, and a blind static_cast<int32_t> would truncate).
                if (count == 1 && ps is { Length: >= 1 } && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_array_get_value", 1, true, true, true, new[] { "a", "i" });
                throw new NotSupportedException(
                    $"Array.GetValue with {count} arg(s)/index type 0x{(ps is { Length: >= 1 } gp ? gp[0] : 0):X2} not wired: only the 1-D GetValue(int) shape (arrayextra.h il2cpp_array_get_value)");
            case "SetValue":
                // void SetValue(object value, int index): reference arrays
                // store the reference (null legal, write barrier always run);
                // byte/int32/int64 unbox through the array's primitive type
                // (wrong boxed type -> InvalidCastException, null on a value
                // array -> ArgumentNullException). The index must be I4
                // (ps[1] == 0x08) -- the long-index overload stays LOUD for
                // the same truncation reason as GetValue above.
                if (count == 2 && ps is { Length: >= 2 } && ps[1] == 0x08)
                    return I("il2cpp::il2cpp_array_set_value", 2, false, false, true, new[] { "a", "", "i" });
                throw new NotSupportedException(
                    $"Array.SetValue with {count} arg(s)/index type 0x{(ps is { Length: >= 2 } sp ? sp[1] : 0):X2} not wired: only the 1-D SetValue(object, int) shape (arrayextra.h il2cpp_array_set_value)");
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
