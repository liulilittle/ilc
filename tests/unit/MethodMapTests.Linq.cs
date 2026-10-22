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
    // ToList<string>(IEnumerable<string>) -> il2cpp_linq_to_list_ref.
    //
    // P0LINQ1 (D1): net48's Enumerable.ToList<TSource> is generic in TSource,
    // so a reference-element source materializes List<T> through the listobj
    // domain (the same List<T> object the List<string> surface uses). The
    // tracked return type is the CONCRETE instantiation name so MethodMap.List
    // selects the reference element arms.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_String_MapsToToListRefIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ToList",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_to_list_ref", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.Collections.Generic.List`1_string", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // ToList<long>/ToList<double>/ToList<float> -> the matching listobj
    // materializers (P0LINQ1 D1); the tracked type carries the element suffix.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_Long_Double_Float_MapToFamilyMaterializers()
    {
        Assert.Equal("il2cpp::il2cpp_linq_to_list_i8",
            ResolveMethodSpec(SynthesizeGeneric("ToList",
                new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0A })).CppName);
        Assert.Equal("System.Collections.Generic.List`1_long",
            ResolveMethodSpec(SynthesizeGeneric("ToList",
                new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0A })).ReturnTypeName);
        Assert.Equal("il2cpp::il2cpp_linq_to_list_d",
            ResolveMethodSpec(SynthesizeGeneric("ToList",
                new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0D })).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_to_list_f",
            ResolveMethodSpec(SynthesizeGeneric("ToList",
                new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0C })).CppName);
    }

    //--------------------------------------------------------------------------
    // ToList<decimal>(IEnumerable<decimal>) -> LOUD: no List<T> storage domain
    // exists for a 16-byte value element (P0LINQ1 D1 boundary).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_Decimal_MapsToBoxedDec()
    {
        // p0linq4 (D1): ToList<decimal> is wired now -- the listobj storage is
        // a reference array, so the element is BOXED on entry
        // (il2cpp_linq_to_list_dec). The tracked List name carries the element
        // so MethodMap.List dispatches the boxed arms. The MethodSpec carries
        // the VALUETYPE TypeRef so the decoded full-name table says
        // System.Decimal (the 0x11 byte alone covers every value type).
        var resolved = ResolveMethodSpec(SynthesizeGenericNamedArg("ToList",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            ns: "System", typeName: "Decimal"));

        Assert.Equal("il2cpp::il2cpp_linq_to_list_dec", resolved.CppName);
        Assert.Equal("System.Collections.Generic.List`1_System.Decimal", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Sum/Min/Max/Average over decimal[] -> the 16-byte decimal kernels
    // (P0LINQ1 D2). ResultKind "dec" is the result-slot ABI marker.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DecimalAggregates_MapToDecimalKernels()
    {
        foreach (var (name, icall) in new[]
        {
            ("Sum", "il2cpp::il2cpp_linq_sum_dec"),
            ("Min", "il2cpp::il2cpp_linq_min_dec"),
            ("Max", "il2cpp::il2cpp_linq_max_dec"),
            ("Average", "il2cpp::il2cpp_linq_average_dec"),
        })
        {
            var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
                new byte[] { 0x10, 0x01, 0x01, 0x11, 0x15, 0x11, 0x0B, 0x01, 0x11, 0x0B },
                new byte[] { 0x0A, 0x01, 0x11 }));
            Assert.Equal(icall, resolved.CppName);
            Assert.Equal("dec", resolved.ResultKind);
            Assert.Equal(1, resolved.ParamCount);
            Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        }
    }

    //--------------------------------------------------------------------------
    // Sum/Min/Max over string[] -> LOUD: net48's Comparer<string>.Default is
    // culture-aware (new[]{"B","a"}.Min() is "a"); ordinal would silently
    // misorder mixed-case input (P0LINQ1 D2 decision, loud-p0-18-strmin-loud).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringMinMax_ThrowsLoud()
    {
        foreach (var name in new[] { "Min", "Max", "Sum", "Average" })
        {
            var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric(name,
                new byte[] { 0x10, 0x01, 0x01, 0x0E, 0x15, 0x12, 0x09, 0x01, 0x0E },
                new byte[] { 0x0A, 0x01, 0x0E })));
            Assert.Contains("culture-aware collation", ex.Message);
        }
    }

    //--------------------------------------------------------------------------
    // Select with TResult = decimal (P0LINQ1 D3): the source family selects the
    // runtime entry, and the result is a 16-byte decimal[].
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SelectDecimalResult_MapsToSourceFamilyKernel()
    {
        // decimal -> decimal: the 0x11 source and 0x11 result are both named
        // System.Decimal in the MethodSpec full-name table.
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x08 }));
        Assert.Equal("il2cpp::il2cpp_linq_select_i4_i4", resolved.CppName);

        // The int32 -> decimal shape: the source byte is 0x08, the result byte
        // 0x11. IsDecimalGenArg requires the decoded full name, which the
        // synthetic MethodSpec cannot carry (the generic signature holds only
        // the dispatch byte), so this shape must stay LOUD rather than
        // fabricate a decimal kernel for an unknown 0x11 struct.
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x11 })));
        Assert.Contains("System.Decimal only", ex.Message);
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

        Assert.Equal("il2cpp::il2cpp_linq_list_count", resolved.CppName);
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

        Assert.Equal("il2cpp::il2cpp_linq_list_count", resolved.CppName);
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
    public void Resolve_ToArray_MapsToToArrayFamilyIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ToArray",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1D, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_linq_to_array_family", resolved.CppName);
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
    // nb9 fix P0-4: Count only reads the array header, so a reference
    // element family is supported too (no longer LOUD).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Count_StringElement_MapsToCount()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Count",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_count", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
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

    //--------------------------------------------------------------------------
    // P0-11: Any() reads only the array header (il2cpp_linq_any), so it is
    // element-family agnostic. A reference-element source must resolve instead
    // of hitting the int32-only gate.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Any_StringElement_MapsToAny()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Any",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_any", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Any_ClassElement_MapsToAny()
    {
        Assert.Equal("il2cpp::il2cpp_linq_any",
            ResolveMethodSpec(SynthesizeGeneric("Any",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x12 })).CppName);
    }

    // List<int>.Any() resolves too (the i4 generic argument was never gated;
    // the runtime List-receiver wiring is a separate gap, see b5-gaps.md).
    [Fact]
    public void Resolve_Any_ListReceiver_Resolves()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Any",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1");

        Assert.NotNull(resolved);
        Assert.Equal(1, resolved.ParamCount);
    }

    // Any(predicate) over a reference source -> il2cpp_linq_any_ref (linq.h).
    [Fact]
    public void Resolve_Any_Predicate_StringElement_MapsToAnyRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Any",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_any_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    // Count(predicate) over a reference source -> il2cpp_linq_count_ref.
    [Fact]
    public void Resolve_Count_Predicate_StringElement_MapsToCountRef()
    {
        Assert.Equal("il2cpp::il2cpp_linq_count_ref",
            ResolveMethodSpec(SynthesizeGeneric("Count",
                new byte[] { 0x10, 0x01, 0x02, 0x08,
                    0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                    0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
                new byte[] { 0x0A, 0x01, 0x0E })).CppName);
    }

    //--------------------------------------------------------------------------
    // B5 D-1: Any(predicate)/Count(predicate) over the WIDE and NARROW array
    // families must not fall into the int32 kernel (the old dispatch mapped
    // every family to il2cpp_linq_any_i4, reading 4 bytes from a 1/2/8-byte
    // cell). Each family resolves to its own width-correct entry, and the
    // narrow cell-kind code is an APPENDED literal (never a consumed IL arg).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Any", 0x0A, "il2cpp::il2cpp_linq_any_i8")]     // int64
    [InlineData("Count", 0x0A, "il2cpp::il2cpp_linq_count_i8")]
    [InlineData("All", 0x0A, "il2cpp::il2cpp_linq_all_i8")]
    [InlineData("Any", 0x0D, "il2cpp::il2cpp_linq_any_d")]      // double
    [InlineData("Count", 0x0D, "il2cpp::il2cpp_linq_count_d")]
    [InlineData("Any", 0x0C, "il2cpp::il2cpp_linq_any_f")]      // float
    [InlineData("Any", 0x05, "il2cpp::il2cpp_linq_any_narrow")] // byte
    [InlineData("Count", 0x06, "il2cpp::il2cpp_linq_count_narrow")] // short
    public void Resolve_Predicate_WideAndNarrowFamilies_MapToTheirOwnWidth(string name, byte elem, string expected)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        if (expected.EndsWith("_narrow", StringComparison.Ordinal))
        {
            Assert.NotNull(resolved.AppendLiterals);
            Assert.Single(resolved.AppendLiterals!);
        }
        else
        {
            Assert.Null(resolved.AppendLiterals);
        }
    }

    // B5 D-2: every List<T> operator takes the LIST OBJECT uncast ("") -- never
    // the "a" Il2CppArray* cast (offsetof(Il2CppArray,length) ==
    // offsetof(Il2CppListI4,items) == 8 -> silent wrong value / AV).
    [Theory]
    [InlineData("Any", "il2cpp::il2cpp_linq_list_any", false, null, true)]
    [InlineData("Count", "il2cpp::il2cpp_linq_list_count", false, null, true)]
    [InlineData("All", "il2cpp::il2cpp_linq_list_all_pred", true, null, false)]
    [InlineData("Where", "il2cpp::il2cpp_linq_list_where", true, "System.Int32[]", false)]
    [InlineData("Select", "il2cpp::il2cpp_linq_list_select", true, "System.Int32[]", false)]
    [InlineData("First", "il2cpp::il2cpp_linq_list_first_i4", false, null, true)]
    [InlineData("FirstOrDefault", "il2cpp::il2cpp_linq_list_first_or_default_i4", false, null, true)]
    public void Resolve_ListInt_AllRequiredOps_PassListObjectThrough(
        string name, string expected, bool hasPredicate, string? returnType, bool listOnly)
    {
        byte[] memberRef;
        byte[] genericArgs;
        if (name is "Any" or "Count" or "First" or "FirstOrDefault")
        {
            memberRef = new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 };
            genericArgs = new byte[] { 0x0A, 0x01, 0x08 };
        }
        else if (name == "Select")
        {
            memberRef = new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x08 };
            genericArgs = new byte[] { 0x0A, 0x02, 0x08, 0x08 };
        }
        else
        {
            memberRef = new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 };
            genericArgs = new byte[] { 0x0A, 0x01, 0x08 };
        }

        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric(name, memberRef, genericArgs),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal(expected, resolved.CppName);
        Assert.All(resolved.ParamTypeNames!, c => Assert.NotEqual("a", c));
        Assert.Equal("", resolved.ParamTypeNames![0]);
        if (hasPredicate) Assert.Equal("", resolved.ParamTypeNames![1]);
        if (returnType != null) Assert.Equal(returnType, resolved.ReturnTypeName);
        Assert.Equal(listOnly ? 1 : 2, resolved.ParamCount);
    }

    // B5 D-4: the already-implemented all_ref kernel (linq.h / linq.cpp) was
    // never wired -- All over a reference source must resolve to it.
    [Fact]
    public void Resolve_All_StringElement_MapsToAllRef()
    {
        Assert.Equal("il2cpp::il2cpp_linq_all_ref",
            ResolveMethodSpec(SynthesizeGeneric("All",
                memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x02,
                    0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                    0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
                genericSignature: new byte[] { 0x0A, 0x01, 0x0E })).CppName);
    }
    //--------------------------------------------------------------------------
    // P0-10: the reference-family map/element entries already exist in the
    // runtime -- wire them for a reference-element TSource.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Where_StringElement_MapsToWhereRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_where_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Select_StringToString_MapsToSelectRefRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0E, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_select_ref_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_First_StringElement_MapsToFirstRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("First",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_first_ref", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_FirstOrDefault_StringElement_MapsToFirstOrDefaultRef()
    {
        Assert.Equal("il2cpp::il2cpp_linq_first_or_default_ref",
            ResolveMethodSpec(SynthesizeGeneric("FirstOrDefault",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0E })).CppName);
    }

    // B5 D-4: Last over a reference source used to be LOUD because linq.h had
    // no last_ref kernel; the kernel now exists (linq.h / linq.cpp) and is
    // wired, so Last<string> resolves to the real entry.
    [Fact]
    public void Resolve_Last_StringElement_MapsToLastRef()
    {
        Assert.Equal("il2cpp::il2cpp_linq_last_ref",
            ResolveMethodSpec(SynthesizeGeneric("Last",
                new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0E })).CppName);
    }

    //--------------------------------------------------------------------------
    // p0conv2 (D3): OrderBy over an INT64 (long[]) source with a key selector.
    // The int32-source entry reads 4-byte cells, so the int64 source has its
    // own family: il2cpp_linq_order_by_key[_desc]_i8_<keyfam> (8-byte element
    // cells, a fresh int64[] result). The KEY family picks the suffix: int32
    // (0x08 slot primitives) -> _i8_i4, int64 -> _i8_i8.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("OrderBy", 0x08, "il2cpp::il2cpp_linq_order_by_key_i8_i4")]
    [InlineData("OrderBy", 0x0A, "il2cpp::il2cpp_linq_order_by_key_i8_i8")]
    [InlineData("OrderByDescending", 0x08, "il2cpp::il2cpp_linq_order_by_key_desc_i8_i4")]
    [InlineData("OrderByDescending", 0x0A, "il2cpp::il2cpp_linq_order_by_key_desc_i8_i8")]
    public void Resolve_OrderBy_LongSource_MapsToI8KeyIcall(
        string name, byte keyByte, string icall)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0A, keyByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int64[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // p0conv2 (D3): an int64-source OrderBy with an unsupported KEY family
    // (float/double/reference) stays LOUD -- no i8-source kernel exists.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(0x0D)]
    [InlineData(0x0E)]
    [InlineData(0x12)]
    public void Resolve_OrderBy_LongSource_UnsupportedKeyFamily_ThrowsLoud(byte keyByte)
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("OrderBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0A, keyByte })));

        Assert.Contains("int64-source key-selector surface", ex.Message);
    }

    // p0linq4 (D4): ThenBy / ThenByDescending over an INT64 source. The
    // (element, key) pair decides the fused-composite kernel:
    //   long[] + int32 key -> il2cpp_linq_then_by_key[_desc]_i8_i4
    //   long[] + int64 key -> il2cpp_linq_then_by_key[_desc]_i8_i8
    // and the tracked result is a System.Int64[].
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(0x08, "System.Int64[]")]
    [InlineData(0x0A, "System.Int64[]")]
    public void Resolve_ThenBy_LongSource_MapsToI8CompositeMarker(byte keyByte, string resultArray)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ThenBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0A, keyByte }));

        Assert.Equal(ResolvedMethod.ThenByCombineMarker, resolved.CppName);
        Assert.Equal(resultArray, resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ThenByDescending_LongSource_MapsToDescCompositeMarker()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ThenByDescending",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0A, 0x0A }));

        Assert.Equal(ResolvedMethod.ThenByDescCombineMarker, resolved.CppName);
        Assert.Equal("System.Int64[]", resolved.ReturnTypeName);
    }

    // p0linq4 (D4): an int32 source with an INT64 key has no fused kernel
    // (there is no then_by_key_i4_i8 entry) -- LOUD, never a silent i4 sort.
    [Fact]
    public void Resolve_ThenBy_Int32SourceInt64Key_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGeneric("ThenBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x0A })));

        Assert.Contains("then-by surface", ex.Message);
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

    // p0linq4 (D1): a ONE-argument MethodSpec whose generic argument is a
    // VALUETYPE TypeRef (0x11 + TypeDefOrRef coded index) -- the shape the
    // decoded generic-argument FULL NAME table needs to tell System.Decimal
    // from the other 0x11 value types.
    private static MemoryStream SynthesizeGenericNamedArg(string methodName, byte[] memberRefSignature,
        string ns, string typeName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"), mdb.GetOrAddString("Enumerable"));
        var elemRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(methodName), mdb.GetOrAddBlob(memberRefSignature));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11); // VALUETYPE
        spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(elemRef) << 2) | 1); // TypeRef coded
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec));
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
