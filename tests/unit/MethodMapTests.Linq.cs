using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Linq dispatch tests (MethodMap.Linq.cs -> TryMapLinq, reached
// through the `case "System.Linq.Enumerable"` line): the gen2-b23 wiring
// covers the full int32-family operator surface --
//   * Count() / Count(pred)    -> il2cpp_linq_count / il2cpp_linq_count_i4
//   * Any() / Any(pred)        -> il2cpp_linq_any / il2cpp_linq_any_i4
//   * All(pred)                -> il2cpp_linq_all_i4
//   * Sum / Min / Max          -> il2cpp_linq_sum_i4 / min_i4 / max_i4
//   * Where(pred)              -> il2cpp_linq_where_i4
//   * Select(sel)              -> il2cpp_linq_select_i4_i4
//   * First / First(pred)      -> il2cpp_linq_first_i4 / first_predicate
//   * FirstOrDefault           -> il2cpp_linq_first_or_default_i4
//   * FirstOrDefault(pred)     -> il2cpp_linq_first_or_default_predicate
//   * ToArray()                -> il2cpp_linq_to_array
//   * ToList<TSource>          -> il2cpp_linq_to_list (List<int32> materializer)
//
// Every operator is a GENERIC static extension call: the synthesized PE
// carries a MethodSpecification whose generic argument byte (0x08 = int32)
// selects the i4 family icall. The source array rides the "a" cast
// (Il2CppObject* -> Il2CppArray*), the predicate/selector delegate the ""
// passthrough (managed ref slot, the single-cast static methodPtr ABI).
//
// LOUD (read-only runtime surface gaps, pinned here):
//   * ToList with a reference-element generic argument -- List<int32> only.
public class MethodMapTestsLinq
{
    //--------------------------------------------------------------------------
    // ToList<int32>(IEnumerable<int32>) -> il2cpp_linq_to_list: the source
    // array passes with the "a" (Il2CppArray*) cast; the result is a
    // List<int> managed reference (ReturnsRef true, ReturnTypeName tracked so
    // downstream List members resolve).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_Int32_MapsToToListIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ToList",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_to_list", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.Collections.Generic.List`1", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // ToList<string>(IEnumerable<string>) -> LOUD: il2cpp_linq_to_list
    // materializes List<int32> only (linq_map.h:84-93); a List<object>
    // runtime does not exist in stage 1.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_String_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("ToList",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E })));

        Assert.Contains("System.Linq.Enumerable::ToList", ex.Message);
        Assert.Contains("List<int32>", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Count() (1 param) -> il2cpp_linq_count: int32 result, the source array
    // rides the "a" cast.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_Plain_MapsToCountIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Count",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // D-LINQC-1: Count() over a List<int32> receiver (the il2cpp_linq_to_list
    // materializer -- tracked as "System.Collections.Generic.List`1" by the
    // ToList ReturnTypeName) must route to the listi4 get_Count icall:
    // il2cpp_linq_count(Il2CppArray*) would mis-cast the List object as an
    // array (reinterpret_cast<Il2CppArray*>) and read a garbage count. The
    // receiver is the FIRST stack value of the static extension call
    // (arg0Type); the managed-ref "" cast passes the List object through
    // unchanged (never "a").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_ListReceiver_MapsToI4GetCount()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Count",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1");

        Assert.Equal("il2cpp::il2cpp_list_i4_get_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // D-LINQC-1: the same dispatch for the newobj AOT expansion receiver name
    // ("System.Collections.Generic.List`1_int" -- a new List<int32> instance)
    // and for the fold's callvirt re-resolve receiver hint.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_ListReceiver_AotExpansionName_MapsToI4GetCount()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Count",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_get_count", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // D-LINQC-1: a non-List tracked receiver keeps the array count icall -- the
    // Where() result is tracked as "System.Int32[]".
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_ArrayReceiver_StaysCountIcall()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Count",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Int32[]");

        Assert.Equal("il2cpp::il2cpp_linq_count", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Count(predicate) (2 params) -> il2cpp_linq_count_i4: array + delegate
    // ("" passthrough).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_Predicate_MapsToCountI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Count",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_count_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Any() -> il2cpp_linq_any; Any(predicate) -> il2cpp_linq_any_i4.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Any_Plain_And_Predicate()
    {
        var plain = ResolveMethodSpec(SynthesizeGeneric("Any",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));
        Assert.Equal("il2cpp::il2cpp_linq_any", plain.CppName);
        Assert.Equal(1, plain.ParamCount);

        var pred = ResolveMethodSpec(SynthesizeGeneric("Any",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));
        Assert.Equal("il2cpp::il2cpp_linq_any_i4", pred.CppName);
        Assert.Equal(2, pred.ParamCount);
        Assert.Equal(new[] { "a", "" }, pred.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // All(predicate) -> il2cpp_linq_all_i4.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_All_Predicate_MapsToAllI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("All",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_all_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Where(predicate) -> il2cpp_linq_where_i4: returns a fresh int32[]
    // (ReturnsRef true, tracked as "System.Int32[]" so downstream ToArray/
    // Count/indexer resolve on the array).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Where_Predicate_MapsToWhereI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_where_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Select(selector) -> il2cpp_linq_select_i4_i4 (int32 -> int32 family).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Select_Selector_MapsToSelectI4I4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_select_i4_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Sum / Min / Max -> the i4 aggregate icalls (int32 result).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Sum_Min_Max_MapsToI4Aggregates()
    {
        Assert.Equal("il2cpp::il2cpp_linq_sum_i4",
            ResolveMethodSpec(SynthesizeGeneric("Sum",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_min_i4",
            ResolveMethodSpec(SynthesizeGeneric("Min",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_max_i4",
            ResolveMethodSpec(SynthesizeGeneric("Max",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
    }

    //--------------------------------------------------------------------------
    // First / FirstOrDefault (no-predicate) -> the i4 entries.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_First_And_FirstOrDefault_MapToI4Entries()
    {
        Assert.Equal("il2cpp::il2cpp_linq_first_i4",
            ResolveMethodSpec(SynthesizeGeneric("First",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_first_or_default_i4",
            ResolveMethodSpec(SynthesizeGeneric("FirstOrDefault",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
    }

    //--------------------------------------------------------------------------
    // First(predicate) (2 params) -> il2cpp_linq_first_predicate: array +
    // delegate ("" passthrough). Walks from the start returning the first
    // match (no match -> InvalidOperationException).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_First_Predicate_MapsToFirstPredicateIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("First",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_first_predicate", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // FirstOrDefault(predicate) (2 params) -> il2cpp_linq_first_or_default_predicate:
    // array + delegate ("" passthrough). Walks from the start returning the
    // first match; no match or empty source -> 0 (the int32 default, never
    // throws).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_FirstOrDefault_Predicate_MapsToFirstOrDefaultPredicateIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("FirstOrDefault",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_first_or_default_predicate", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Take(count) -> il2cpp_linq_take_i4: the first `count` elements
    // materialize eagerly into a fresh int32[] (net48 slice semantics: count
    // <= 0 -> empty, count >= length -> full copy). The count rides the "i"
    // cast (int32 slot -> int32_t), the source array the "a" cast; the result
    // is tracked as "System.Int32[]" so downstream operators resolve.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Take_MapsToTakeI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Take",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_take_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Skip(count) -> il2cpp_linq_skip_i4: everything after the first `count`
    // elements (net48: count <= 0 -> full copy, count >= length -> empty).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Skip_MapsToSkipI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Skip",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_skip_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // A non-int32 element family is LOUD for Take/Skip too (the i4 slice
    // icalls are int32-family-only, like every other operator).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Take_StringElement_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("Take",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E })));

        Assert.Contains("System.Linq.Enumerable::Take", ex.Message);
        Assert.Contains("int32 (0x08)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ToArray() -> il2cpp_linq_to_array (fresh int32[] clone, tracked as
    // "System.Int32[]").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToArray_MapsToToArrayIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ToArray",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1D, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_to_array", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // OrderBy(keySelector) -> il2cpp_linq_order_by_key_i4 (the D-LINQ-1 ABI
    // bridge): Enumerable.OrderBy passes a 1-arg KEY SELECTOR
    // Func<TSource,TKey>, NOT a 2-arg Comparison<T>; the runtime caches each
    // element's key EXACTLY ONCE (net48 EnumerableSorter semantics) and runs
    // the stable sort on the cached keys. Result tracked as "System.Int32[]"
    // so downstream operators resolve.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_OrderBy_KeySelector_MapsToOrderByKeyIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("OrderBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_order_by_key_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // OrderBy with a non-int32 KEY type (TKey != 0x08) is LOUD:
    // il2cpp_linq_order_by_key_i4 caches int32 keys only -- a reference-key
    // family has no stage-1 key icall.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_OrderBy_StringKey_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("OrderBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x0E })));

        Assert.Contains("System.Linq.Enumerable::OrderBy", ex.Message);
        Assert.Contains("int32 (0x08)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // A non-int32 element family is LOUD for the operator surface (the
    // ref-family icalls have no transpiler callback-ABI producer).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_StringElement_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("Count",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E })));

        Assert.Contains("System.Linq.Enumerable::Count", ex.Message);
        Assert.Contains("int32 (0x08)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Reverse() -> il2cpp_linq_reverse_i4: a fresh int32[] with elements in
    // reverse order (ReturnsRef true, tracked as "System.Int32[]").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Reverse_MapsToReverseI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Reverse",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_reverse_i4", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Last() -> il2cpp_linq_last_i4: last element; empty ->
    // InvalidOperationException.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Last_MapsToLastI4Icall()
    {
        Assert.Equal("il2cpp::il2cpp_linq_last_i4",
            ResolveMethodSpec(SynthesizeGeneric("Last",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
    }

    //--------------------------------------------------------------------------
    // LastOrDefault() -> il2cpp_linq_last_or_default_i4.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_LastOrDefault_MapsToLastOrDefaultI4Icall()
    {
        Assert.Equal("il2cpp::il2cpp_linq_last_or_default_i4",
            ResolveMethodSpec(SynthesizeGeneric("LastOrDefault",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x08 })).CppName);
    }

    //--------------------------------------------------------------------------
    // ElementAt(index) -> il2cpp_linq_element_at_i4: 2 params (array + int32
    // index); out-of-range -> ArgumentOutOfRangeException.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ElementAt_MapsToElementAtI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ElementAt",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_element_at_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Contains(value) -> il2cpp_linq_contains_i4: 2 params (array + int32
    // value); bool result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Contains_MapsToContainsI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Contains",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_contains_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Aggregate(func) -> il2cpp_linq_aggregate_i4: 2 params (array + binary
    // func delegate); int32 result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Aggregate_MapsToAggregateI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Aggregate",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x00, 0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Average() -> il2cpp_linq_average_i4: double result (resultKind "d").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Average_MapsToAverageI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Average",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_average_i4", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // OrderByDescending(keySelector) -> il2cpp_linq_order_by_key_desc_i4:
    // identical to OrderBy but with DESCENDING key order. Same 2 params
    // (array + delegate keySelector), result tracked as "System.Int32[]".
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_OrderByDescending_MapsToOrderByKeyDescI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("OrderByDescending",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_order_by_key_desc_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // OrderByDescending with non-int32 key -> LOUD (same gate as OrderBy).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_OrderByDescending_StringKey_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("OrderByDescending",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x0E })));

        Assert.Contains("System.Linq.Enumerable::OrderByDescending", ex.Message);
        Assert.Contains("int32 (0x08)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // LongCount() -> il2cpp_linq_long_count: int64 return (resultKind "l").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_LongCount_MapsToLongCountIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("LongCount",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_long_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // SequenceEqual(second) -> il2cpp_linq_sequence_equal_i4: 2 params
    // (array + array), bool result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SequenceEqual_MapsToSequenceEqualI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("SequenceEqual",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_sequence_equal_i4", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "a" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // DefaultIfEmpty() -> il2cpp_linq_default_if_empty_i4: 1 param (array),
    // returns array (ReturnsRef true, tracked as "System.Int32[]").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DefaultIfEmpty_MapsToDefaultIfEmptyI4Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("DefaultIfEmpty",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_i4", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the ArraySort-test pipeline).
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // D-LINQ-4 grouping surface: per-bucket Count / whole-result Count /
    // instance get_Key / per-bucket ToArray route to the grouping icalls
    // (OBJECT receivers -- never the "a" array cast).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_GroupingReceiver_MapsToGroupingGetCount()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Count",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_get_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Count_GroupingResultReceiver_MapsToResultGetCount()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Count",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Linq.IGroupingResult");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_result_get_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GetKey_GroupingOwner_MapsToGroupingGetKey()
    {
        var resolved = ResolveMemberRef(SynthesizeMemberRef("System.Linq", "IGrouping`2", "get_Key",
            new byte[] { 0x00, 0x00, 0x08 }), "get_Key");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_get_key", resolved.CppName);
        // INSTANCE accessor: ParamCount counts the EXPLICIT parameters only
        // (the emitter prepends the receiver); get_Key takes none.
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // D-LINQ-4 array return: a method returning SZARRAY (int[]) must carry
    // ReturnsRef=true (the array is a managed reference, Il2CppArray*).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ArrayReturn_HasReturnsRefTrue()
    {
        // int[] GetArray() -- parameterless, returns SZARRAY of int32.
        // Signature: 0x00 (no hasThis) 0x00 (param count 0) 0x1D (SZARRAY) 0x08 (int32 element).
        var resolved = ResolveMemberRef(SynthesizeMemberRef("System.Linq", "Enumerable", "GetArray",
            new byte[] { 0x00, 0x00, 0x1D, 0x08 }), "GetArray");

        Assert.True(resolved.ReturnsValue, "array return must return a value");
        Assert.True(resolved.ReturnsRef, "SZARRAY return must be classified as reference (Il2CppArray*)");
    }

    [Fact]
    public void Resolve_StringArrayReturn_HasReturnsRefTrue()
    {
        // string[] GetStringArray() -- parameterless, returns SZARRAY of string.
        // Signature: 0x00 (no hasThis) 0x00 (param count 0) 0x1D (SZARRAY) 0x0E (string element).
        var resolved = ResolveMemberRef(SynthesizeMemberRef("System.Linq", "Enumerable", "GetStringArray",
            new byte[] { 0x00, 0x00, 0x1D, 0x0E }), "GetStringArray");

        Assert.True(resolved.ReturnsValue, "string array return must return a value");
        Assert.True(resolved.ReturnsRef, "SZARRAY of string return must be classified as reference (Il2CppArray*)");
    }

    [Fact]
    public void Resolve_ToArray_GroupingReceiver_MapsToGroupingGetElements()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("ToArray",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1D, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_get_elements", resolved.CppName);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

    // D-LINQC-1: the receiver/arg0-type-aware overload -- Enumerable.Count is a
    // STATIC extension call, so its source is the FIRST stack value (arg0Type);
    // receiverType is the fold's callvirt re-resolve hint (accepted too).
    private static ResolvedMethod ResolveMethodSpecWithReceiver(MemoryStream pe, string? receiverType)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(),
            receiverType, false, receiverType, null);
    }

    // System.Linq.Enumerable::<name><TSource...>(...): a MemberReference
    // (row 1) with a GENERIC method-definition signature (header 0x10 +
    // GenericParameterCount + param count + ret + GenericInst params) + one
    // MethodSpecification (GENERICINST 0x0A + arg bytes).
    private static MemoryStream SynthesizeGeneric(string methodName, byte[] memberRefSignature, byte[] genericSignature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"), mdb.GetOrAddString("Enumerable"));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(methodName), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }

    // D-LINQ-4: instance member on a non-Enumerable owner (IGrouping`2
    // get_Key) -- plain MemberReference, no MethodSpecification.
    private static ResolvedMethod ResolveMemberRef(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static MemoryStream SynthesizeMemberRef(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
