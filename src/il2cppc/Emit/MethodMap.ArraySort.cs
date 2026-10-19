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
// / _ref_comparison, tested in test_arraysort). Value-element comparer sorts
// without a runtime entry (uint32/int64) stay LOUD here.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapArraySort(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
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
                        _ => throw new NotSupportedException(
                            $"Array.Sort<T> generic argument 0x{genArgs[0]:X2} has no direct-call sort (only int32/uint32/int64/float/double/string arrays)"),
                    };
                }
                if (count == 2)
                {
                    // Array.Sort<T>(T[], Comparison<T>) -- unwireable, see the
                    // file header (delegate-to-function-pointer ABI absent).
                    throw new NotSupportedException(
                        "Array.Sort<T>(T[], Comparison<T>) is not wired: the runtime callback sort takes a plain function pointer (ArrayCompareFn) but the transpiler has no delegate-to-function-pointer ABI; value-element comparer sorts have no runtime entry");
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
                    0x0C when count == 2 => I("il2cpp::il2cpp_array_index_of_float_start", 2, true, false, false, new[] { "a", "d" }, appendZero: true),
                    0x0C when count == 3 => I("il2cpp::il2cpp_array_index_of_float_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0C when count == 4 => I("il2cpp::il2cpp_array_index_of_float_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0D when count == 2 => I("il2cpp::il2cpp_array_index_of_double", 2, true, false, false, new[] { "a", "d" }),
                    0x0D when count == 3 => I("il2cpp::il2cpp_array_index_of_double_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0D when count == 4 => I("il2cpp::il2cpp_array_index_of_double_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0E when count == 2 => I("il2cpp::il2cpp_array_index_of_string", 2, true, false, false, new[] { "a", "" }),
                    0x0E when count == 3 => I("il2cpp::il2cpp_array_index_of_string_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x0E when count == 4 => I("il2cpp::il2cpp_array_index_of_string_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    0x12 or 0x1C when count == 2 => I("il2cpp::il2cpp_array_index_of_object_start", 2, true, false, false, new[] { "a", "" }, appendZero: true),
                    0x12 or 0x1C when count == 3 => I("il2cpp::il2cpp_array_index_of_object_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x12 or 0x1C when count == 4 => I("il2cpp::il2cpp_array_index_of_object_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    _ => throw new NotSupportedException(
                        $"Array.IndexOf<T> generic argument 0x{genArgs[0]:X2} with {count} arg(s) has no direct-call"),
                };
            case "LastIndexOf":
                if (genArgs == null) return null;
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
                    0x0C when count == 2 => I("il2cpp::il2cpp_array_last_index_of_float", 2, true, false, false, new[] { "a", "d" }),
                    0x0C when count == 3 => I("il2cpp::il2cpp_array_last_index_of_float_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0C when count == 4 => I("il2cpp::il2cpp_array_last_index_of_float_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0D when count == 2 => I("il2cpp::il2cpp_array_last_index_of_double", 2, true, false, false, new[] { "a", "d" }),
                    0x0D when count == 3 => I("il2cpp::il2cpp_array_last_index_of_double_start", 3, true, false, false, new[] { "a", "d", "i" }),
                    0x0D when count == 4 => I("il2cpp::il2cpp_array_last_index_of_double_range", 4, true, false, false, new[] { "a", "d", "i", "i" }),
                    0x0E when count == 2 => I("il2cpp::il2cpp_array_last_index_of_string", 2, true, false, false, new[] { "a", "" }),
                    0x0E when count == 3 => I("il2cpp::il2cpp_array_last_index_of_string_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x0E when count == 4 => I("il2cpp::il2cpp_array_last_index_of_string_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    0x12 or 0x1C when count == 2 => I("il2cpp::il2cpp_array_last_index_of_any", 2, true, false, false, new[] { "a", "" }),
                    0x12 or 0x1C when count == 3 => I("il2cpp::il2cpp_array_last_index_of_object_start", 3, true, false, false, new[] { "a", "", "i" }),
                    0x12 or 0x1C when count == 4 => I("il2cpp::il2cpp_array_last_index_of_object_range", 4, true, false, false, new[] { "a", "", "i", "i" }),
                    _ => throw new NotSupportedException(
                        $"Array.LastIndexOf<T> generic argument 0x{genArgs[0]:X2} with {count} arg(s) has no direct-call"),
                };
            case "BinarySearch":
                if (genArgs == null) return null;
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
                    0x12 or 0x1C when count == 2 => I("il2cpp::il2cpp_array_binary_search_object", 2, true, false, false, new[] { "a", "" }),
                    0x12 or 0x1C when count == 4 => I("il2cpp::il2cpp_array_binary_search_object_range", 4, true, false, false, new[] { "a", "i", "i", "" }),
                    _ => throw new NotSupportedException(
                        $"Array.BinarySearch<T> generic argument 0x{genArgs[0]:X2} with {count} arg(s) has no direct-call"),
                };
            default: return null;
        }
    }
}
