namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Array -- System.Array icall mappings (il2cpp_array.h + the
// generic-instantiation surface: Array.Sort<T>/BinarySearch<T>/IndexOf<T>/
// LastIndexOf<T> dispatch on the generic argument type bytes).
// Owners: System.Array
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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Sort":
                if (genArgs != null)
                {
                    return genArgs[0] switch
                    {
                        // Array.Sort<T>(T[]) count==1 -- the no-comparer family:
                        // the icall layer for int32/string, the ArraySort
                        // direct-call domain for uint32/int64 (arraysort.h).
                        0x0E when count == 1 => I("il2cpp::runtime::icall_array_sort_strings", 1, false, false, false),
                        0x08 when count == 1 => I("il2cpp::runtime::icall_array_sort", 1, false, false, false),
                        0x09 or 0x0A when count == 1 => null,
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
                        0x08 when count == 2 => I("il2cpp::il2cpp_array_sort_int32_comparison", 2, false, false, false, new[] { "a", "" }),
                        0x0E or 0x1C or 0x12 when count == 2 => I("il2cpp::il2cpp_array_sort_ref_comparison", 2, false, false, false, new[] { "a", "" }),
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
                        0x08 => I("il2cpp::runtime::icall_array_binary_search_int32", 2, true, false, false),
                        0x0E => I("il2cpp::runtime::icall_array_binary_search_string", 2, true, false, false),
                        // M6: int64 yields to the ArraySort direct-call
                        // domain (il2cpp_array_binary_search_int64); only
                        // the 2-arg form (ArraySort.cs maps any count to
                        // the 2-param call, so wider arities stay LOUD).
                        0x0A when count == 2 => null,
                        _ => throw new NotSupportedException($"Array.BinarySearch<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
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
                        0x0A or 0x0E when count == 2 => null,
                        _ => throw new NotSupportedException($"Array.IndexOf<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
                    };
                }
                return I("il2cpp::runtime::icall_array_index_of", 2, true, false, false);
            case "LastIndexOf":
                if (genArgs != null)
                {
                    if (genArgs[0] == 0x08) return I("il2cpp::runtime::icall_array_last_index_of_int32", 2, true, false, false);
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
                        _ => throw new NotSupportedException($"Array.Empty<T> generic argument 0x{genArgs[0]:X2} not wired in M5"),
                    };
                }
                throw new NotSupportedException("Array.Empty non-generic not wired in M5");
            case "Clear":
                return I("il2cpp::runtime::icall_array_clear_int32", 3, false, false, false);
            case "Reverse":
                if (count == 1) return I("il2cpp::runtime::icall_array_reverse", 1, false, false, false);
                return I("il2cpp::runtime::icall_array_reverse_range", 3, false, false, false);
            case "Copy":
                // icall_array_copy(src, srcIndex, dst, dstIndex, length) -- the
                // (Array, Array, int) overload would need two injected zeros.
                if (count == 5) return I("il2cpp::runtime::icall_array_copy", 5, false, false, false);
                throw new NotSupportedException($"Array.Copy with {count} arg(s) not wired: the runtime icall needs (src, srcIndex, dst, dstIndex, length)");
            case "FindIndex":
                // arrayext2 (2026-08-09, rtfind-b23): the DELEGATE form is
                // now wired -- icall_array_find_index (il2cpp_array_ext2.h:145)
                // consumes the Predicate<T> DELEGATE (single-cast static
                // methodPtr ABI, the linq where_i4 callback pattern) directly.
                //   C++ 签名: icall_array_find_index(Il2CppArray* arr,
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
                    return I("il2cpp::runtime::icall_array_find_index", 4, true, false, false,
                        new[] { "a", "", "i", "i" });
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
                throw new NotSupportedException(
                    $"Array.ConvertAll<TInput,TOutput> generic arguments 0x{(genArgs?[0] ?? 0):X2},0x{(genArgs is { Length: 2 } g ? g[1] : 0):X2} not wired: only (int32,int32) (0x08,0x08) has the converter icall icall_array_convert_all_i4_i4 (il2cpp_array_ext2.h:181)");
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
                return I("il2cpp::il2cpp_array_get_length", 0, true, false, true);
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
            // (icallc2 report §3 -- arrayext.cpp). All instance, receiver
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
            default: return null;
        }
    }
}
