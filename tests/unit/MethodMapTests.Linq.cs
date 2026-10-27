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
    // ToList<Guid>(IEnumerable<Guid>) -> il2cpp_linq_to_list_guid. Guid is the
    // remaining 16-byte value element; the listobj storage boxes it through
    // icall_box_guid on entry (listobj.h). The 0x11 byte alone covers every
    // value type, so the MethodSpec FULL NAME (System.Guid) is the authority.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ToList_Guid_MapsToBoxedGuid()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericNamedArg("ToList",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x1C, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            ns: "System", typeName: "Guid"));

        Assert.Equal("il2cpp::il2cpp_linq_to_list_guid", resolved.CppName);
        Assert.Equal("System.Collections.Generic.List`1_System.Guid", resolved.ReturnTypeName);
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
        Assert.Contains("DateTime/TimeSpan/Guid", ex.Message);
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
    // D-LINQ-FAM: Take over a REFERENCE source (string) now resolves to the
    // family kernel with the reference cell (15) -- the runtime ships
    // il2cpp_linq_take_family, so the old int32-only LOUD is gone.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Take_StringElement_MapsToRefFamilyKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Take",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_take_family", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "15" }, resolved.AppendLiterals);
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
        Assert.Contains("int32/int64/double/float keys", ex.Message);
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
        Assert.Contains("int32/int64/double/float keys", ex.Message);
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

    // BATCH4: get_Key dispatches on the tracked grouping type's key-family marker
    // ("System.Linq.IGrouping_key_i8/_dec/_dt/_guid"), set by the
    // IEnumerator<IGrouping<TKey,TElement>> instantiation name.
    [Theory]
    [InlineData("System.Linq.IGrouping_key_i8", "il2cpp::il2cpp_linq_grouping_get_key_i8", "l")]
    [InlineData("System.Linq.IGrouping_key_dec", "il2cpp::il2cpp_linq_grouping_get_key_dec", "dec")]
    [InlineData("System.Linq.IGrouping_key_dt", "il2cpp::il2cpp_linq_grouping_get_key_dt", "dt")]
    [InlineData("System.Linq.IGrouping_key_guid", "il2cpp::il2cpp_linq_grouping_get_key_guid", "guid")]
    public void Resolve_GetKey_GroupingKeyMarker_MapsToWideKeyGetter(string receiverType, string cpp, string resultKind)
    {
        var resolved = ResolveMemberRefWithReceiver(
            SynthesizeMemberRef("System.Linq", "IGrouping`2", "get_Key", new byte[] { 0x00, 0x00, 0x08 }),
            "get_Key", receiverType);

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    // BATCH4: the int32-slot key path is unchanged -- an unmarked grouping type
    // still maps to the int32 getter.
    [Fact]
    public void Resolve_GetKey_GroupingUnmarked_MapsToGroupingGetKey()
    {
        var resolved = ResolveMemberRefWithReceiver(
            SynthesizeMemberRef("System.Linq", "IGrouping`2", "get_Key", new byte[] { 0x00, 0x00, 0x08 }),
            "get_Key", "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_get_key", resolved.CppName);
    }

    // BATCH4: Zip's result cell ABI -- a reference result rides a reference
    // array (resKind 6), a 1/2-byte integral / bool / char result rides a narrow
    // cell (7..12), and a reference source rides the mixed entry (side kind 1).
    [Theory]
    [InlineData(0x0E, 0x0E, 0x0E, "1", "1", "6")] // ref,ref -> ref result
    [InlineData(0x0E, 0x0E, 0x08, "1", "1", "0")] // ref,ref -> int32 result
    [InlineData(0x0E, 0x08, 0x08, "1", "0", "0")] // ref,int -> int32 result
    [InlineData(0x08, 0x0E, 0x08, "0", "1", "0")] // int,ref -> int32 result
    [InlineData(0x08, 0x08, 0x05, "0", "0", "8")] // byte result (1-byte cell)
    [InlineData(0x08, 0x08, 0x06, "0", "0", "9")] // short result (2-byte cell)
    [InlineData(0x08, 0x08, 0x02, "0", "0", "12")] // bool result (1-byte cell)
    public void Resolve_Zip_MapsResultCellAbi(int tFirst, int tSecond, int tRes,
        string firstKind, string secondKind, string resKind)
    {
        var resolved = ResolveMethodSpec(SynthesizeZip((byte)tFirst, (byte)tSecond, (byte)tRes));

        Assert.Equal("il2cpp::il2cpp_linq_zip_family", resolved.CppName);
        Assert.Equal(new[] { firstKind, secondKind, resKind }, resolved.AppendLiterals);
    }

    // Zip's 0x11 VALUE-TYPE result cell ABI: the generic-argument NAME selects
    // the cell (13 decimal / 14 DateTime|TimeSpan / 15 Guid).
    [Theory]
    [InlineData("System.Decimal", "13")]
    [InlineData("System.DateTime", "14")]
    [InlineData("System.TimeSpan", "14")]
    [InlineData("System.Guid", "15")]
    public void Resolve_Zip_ValueTypeResult_MapsResultCellAbi(string resTypeName, string resKind)
    {
        var resolved = ResolveMethodSpec(SynthesizeZipValResult(0x08, 0x08, resTypeName));

        Assert.Equal("il2cpp::il2cpp_linq_zip_family", resolved.CppName);
        Assert.Equal(new[] { "0", "0", resKind }, resolved.AppendLiterals);
    }

    // Zip with a System.Decimal ELEMENT source: the first side maps to
    // IL2CPP_LIST_KIND_DEC (12) so the runtime reads the 16-byte decimal cell.
    [Fact]
    public void Resolve_Zip_DecimalElementSource_MapsDecimalListKind()
    {
        var resolved = ResolveMethodSpec(SynthesizeZipDecFirst(0x08, 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_zip_family", resolved.CppName);
        Assert.Equal(new[] { "12", "0", "13" }, resolved.AppendLiterals);
    }

    // Zip<TSource,TSecond,TResult>(IEnumerable<TSource>, IEnumerable<TSecond>,
    // Func<TSource,TSecond,TResult>): the signature the Zip branch reads.
    private static MemoryStream SynthesizeZip(byte tFirst, byte tSecond, byte tRes)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func3 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Func`3"));
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(3); // GenericParameterCount
        bb.WriteCompressedInteger(3); // param count
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2); // VAR 2 (TResult) -- the return type
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0); // VAR 0 (TSource)
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1); // VAR 1 (TSecond)
        WriteGenericInstArg(bb, func3, 3);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2); // VAR 2
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Zip"),
            mdb.GetOrAddBlob(bb.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        spec.WriteByte(tFirst);
        spec.WriteByte(tSecond);
        spec.WriteByte(tRes);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Zip with a 0x11 VALUE-TYPE result: the byte alone cannot name the type, so
    // the MethodSpec's third argument is a named VALUETYPE TypeRef (the
    // transpiler resolves System.Decimal / System.DateTime / System.TimeSpan /
    // System.Guid to the result cell ABI 13 / 14 / 15).
    private static MemoryStream SynthesizeZipValResult(byte tFirst, byte tSecond, string resTypeName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func3 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Func`3"));
        var lastDot = resTypeName.LastIndexOf('.');
        var resNs = lastDot > 0 ? resTypeName[..lastDot] : "";
        var resName = lastDot > 0 ? resTypeName[(lastDot + 1)..] : resTypeName;
        var resRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(resNs), mdb.GetOrAddString(resName));
        var resCoded = (MetadataTokens.GetRowNumber(resRef) << 2) | 1;
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(3);
        bb.WriteCompressedInteger(3);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        WriteGenericInstArg(bb, func3, 3);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Zip"),
            mdb.GetOrAddBlob(bb.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        spec.WriteByte(tFirst);
        spec.WriteByte(tSecond);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(resCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Zip with a System.Decimal ELEMENT source (the first generic argument is a
    // named VALUETYPE TypeRef): the transpiler maps it to IL2CPP_LIST_KIND_DEC
    // (12) so the runtime reads the 16-byte decimal cell.
    private static MemoryStream SynthesizeZipDecFirst(byte tSecond, byte tRes)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func3 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Func`3"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(3);
        bb.WriteCompressedInteger(3);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        WriteGenericInstArg(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        WriteGenericInstArg(bb, func3, 3);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(2);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Zip"),
            mdb.GetOrAddBlob(bb.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(decCoded);
        spec.WriteByte(tSecond);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(decCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    private static void WriteGenericInstArg(BlobBuilder bb, TypeReferenceHandle def, int argc)
    {
        bb.WriteByte(0x15);
        bb.WriteByte(0x12); // CLASS marker
        bb.WriteCompressedInteger((MetadataTokens.GetRowNumber(def) << 2) | 1); // TypeRef coded
        bb.WriteCompressedInteger(argc);
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

    //--------------------------------------------------------------------------
    // The List<T> selector aggregate with a DECIMAL projection (net48
    // Sum/Min/Max/Average(source, selector) over a decimal selector): the
    // 16-byte decimal result rides an explicit result slot (the decimal T3
    // contract -- the slot is the FIRST icall parameter and ResultKind "dec"
    // prepends it). Before this change the decimal projection LOUD-rejected.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ListInt_SumDecimalProjection_MapsToDecimalResultSlotKernel()
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericListAggDecimalProjection("Sum", "System.Decimal"),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_sum_sel_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
        Assert.NotNull(resolved.AppendLiterals);
        Assert.Equal("0", resolved.AppendLiterals![0]);
    }

    // The ARRAY-source selector aggregate with a DECIMAL projection (net48
    // Sum<TSource,decimal>): the same 16-byte result-slot contract.
    [Fact]
    public void Resolve_ArrayInt_SumDecimalProjection_MapsToDecimalResultSlotKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericListAggDecimalProjection("Sum", "System.Decimal"));

        Assert.Equal("il2cpp::il2cpp_linq_sum_sel_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (B-1): the REFERENCE-source selector aggregates with a DECIMAL
    // projection (net48 Sum/Min/Max/Average<TSource>(source,
    // Func<TSource,decimal>) -- Enumerable.cs:1528/1735/1939/2133). The old
    // mapping LOUD-rejected the decimal projection ("the reference selector-
    // aggregate surface covers an int32-slot (char/int32), int64, double or
    // float projection only").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_RefSelectorDecimalProjection_MapsToDecimalResultSlotKernels()
    {
        foreach (var (name, icall) in new[]
        {
            ("Sum", "il2cpp::il2cpp_linq_sum_sel_ref_dec"),
            ("Min", "il2cpp::il2cpp_linq_min_sel_ref_dec"),
            ("Max", "il2cpp::il2cpp_linq_max_sel_ref_dec"),
            ("Average", "il2cpp::il2cpp_linq_average_sel_ref_dec"),
        })
        {
            var resolved = ResolveMethodSpec(SynthesizeRefSelectorDecimalProjection(name));
            Assert.Equal(icall, resolved.CppName);
            Assert.Equal("dec", resolved.ResultKind);
            Assert.Equal(2, resolved.ParamCount);
            Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        }
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (B-2): the DECIMAL-BUCKET projection aggregates (net48
    // Sum/Min/Max/Average<decimal>(bucket, Func<decimal,int32/int64/float/
    // double>)). The bucket's decimal cells feed the selector and the fold runs
    // over the projected values. The old mapping LOUD-rejected every
    // non-decimal projection.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Sum", "i", "il2cpp::il2cpp_linq_grouping_sum_dec_proj_i4", null)]
    [InlineData("Sum", "l", "il2cpp::il2cpp_linq_grouping_sum_dec_proj_i8", "l")]
    [InlineData("Sum", "f", "il2cpp::il2cpp_linq_grouping_sum_dec_proj_f", "d")]
    [InlineData("Sum", "d", "il2cpp::il2cpp_linq_grouping_sum_dec_proj_d", "d")]
    [InlineData("Min", "i", "il2cpp::il2cpp_linq_grouping_min_dec_proj_i4", null)]
    [InlineData("Min", "l", "il2cpp::il2cpp_linq_grouping_min_dec_proj_i8", "l")]
    [InlineData("Max", "i", "il2cpp::il2cpp_linq_grouping_max_dec_proj_i4", null)]
    [InlineData("Max", "d", "il2cpp::il2cpp_linq_grouping_max_dec_proj_d", "d")]
    [InlineData("Average", "i", "il2cpp::il2cpp_linq_grouping_average_dec_proj_i4", "d")]
    [InlineData("Average", "l", "il2cpp::il2cpp_linq_grouping_average_dec_proj_i8", "d")]
    [InlineData("Average", "f", "il2cpp::il2cpp_linq_grouping_average_dec_proj_f", "d")]
    [InlineData("Average", "d", "il2cpp::il2cpp_linq_grouping_average_dec_proj_d", "d")]
    public void Resolve_GroupingDecimalProjectionAggregate_MapsToProjectionKernel(
        string name, string projCode, string icall, string? resultKind)
    {
        byte projByte = projCode switch
        {
            "i" => 0x08,
            "l" => 0x0A,
            "f" => 0x0C,
            _ => 0x0D,
        };
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecSelProj(name, projByte), receiverType: "System.Linq.IGrouping");

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(resultKind, resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (B-3): the DECIMAL-SOURCE element / predicate operators
    // (net48 FirstOrDefault/LastOrDefault/Single/SingleOrDefault with and
    // without a predicate, Count(pred), Any(pred), All, LongCount, TakeWhile /
    // SkipWhile -- Enumerable.cs:1056/1109/1157/1176/1194/1213/1341/1352/605/
    // 618/647/661) plus the source-only DefaultIfEmpty<decimal>() (:998). The
    // element returns ride the decimal result slot; the predicate crosses as
    // bool(*)(decimal) BY VALUE. Before this change the predicate forms fell
    // into the int32-only gate (LOUD) and Count(pred)/Any(pred) silently ran
    // the INT32 kernels over the 16-byte decimal cells.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("FirstOrDefault", "il2cpp::il2cpp_linq_first_or_default_dec")]
    [InlineData("LastOrDefault", "il2cpp::il2cpp_linq_last_or_default_dec")]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_dec")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_dec")]
    public void Resolve_DecimalSource_ElementReturn_NoPredicate_MapsToDecimalKernel(
        string name, string icall)
    {
        // decimal <Name>(IEnumerable<decimal>)
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource(name,
            new byte[] { 0x10, 0x01, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            0x02));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("First", "il2cpp::il2cpp_linq_first_pred_dec")]
    [InlineData("FirstOrDefault", "il2cpp::il2cpp_linq_first_or_default_pred_dec")]
    [InlineData("Last", "il2cpp::il2cpp_linq_last_pred_dec")]
    [InlineData("LastOrDefault", "il2cpp::il2cpp_linq_last_or_default_pred_dec")]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_pred_dec")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_pred_dec")]
    public void Resolve_DecimalSource_ElementReturn_Predicate_MapsToDecimalPredKernel(
        string name, string icall)
    {
        // decimal <Name>(IEnumerable<decimal>, Func<decimal,bool>)
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource(name,
            new byte[] { 0x10, 0x01, 0x02, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
            0x02));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DecimalSource_CountPredicate_MapsToDecimalCountKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource("Count",
            new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
            0x02));

        Assert.Equal("il2cpp::il2cpp_linq_count_pred_dec", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("Any", "il2cpp::il2cpp_linq_any_pred_dec")]
    [InlineData("All", "il2cpp::il2cpp_linq_all_dec")]
    public void Resolve_DecimalSource_BoolPredicate_MapsToDecimalBoolKernel(string name, string icall)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource(name,
            new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
            0x02));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DecimalSource_LongCount_MapsToDecimalLongCountKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource("LongCount",
            new byte[] { 0x10, 0x01, 0x01, 0x0A,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            0x02));

        Assert.Equal("il2cpp::il2cpp_linq_long_count_dec", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("TakeWhile", "il2cpp::il2cpp_linq_take_while_dec")]
    [InlineData("SkipWhile", "il2cpp::il2cpp_linq_skip_while_dec")]
    public void Resolve_DecimalSource_TakeSkipWhile_MapsToDecimalRangeKernel(string name, string icall)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource(name,
            new byte[] { 0x10, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
            0x02));

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DecimalSource_DefaultIfEmpty1_MapsToSourceOnlyKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource("DefaultIfEmpty",
            new byte[] { 0x10, 0x01, 0x01, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            0x02));

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty1_dec", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (B-3): the DECIMAL-BUCKET operator surface -- each entry
    // snapshots the bucket's decimal cells and reuses the DECIMAL-SOURCE
    // kernel. The old mapping LOUD-rejected every non-agnostic operator over
    // a decimal bucket. OrderBy over the bucket sorts by a projected key of
    // the int32/int64/double/decimal families.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(BucketShape.ElementNoPred, "First", "il2cpp::il2cpp_linq_grouping_first_dec")]
    [InlineData(BucketShape.ElementNoPred, "FirstOrDefault", "il2cpp::il2cpp_linq_grouping_first_or_default_dec")]
    [InlineData(BucketShape.ElementNoPred, "Last", "il2cpp::il2cpp_linq_grouping_last_dec")]
    [InlineData(BucketShape.ElementNoPred, "LastOrDefault", "il2cpp::il2cpp_linq_grouping_last_or_default_dec")]
    [InlineData(BucketShape.ElementNoPred, "Single", "il2cpp::il2cpp_linq_grouping_single_dec")]
    [InlineData(BucketShape.ElementNoPred, "SingleOrDefault", "il2cpp::il2cpp_linq_grouping_single_or_default_dec")]
    [InlineData(BucketShape.ElementPred, "First", "il2cpp::il2cpp_linq_grouping_first_pred_dec")]
    [InlineData(BucketShape.ElementPred, "FirstOrDefault", "il2cpp::il2cpp_linq_grouping_first_or_default_pred_dec")]
    [InlineData(BucketShape.ElementPred, "Last", "il2cpp::il2cpp_linq_grouping_last_pred_dec")]
    [InlineData(BucketShape.ElementPred, "LastOrDefault", "il2cpp::il2cpp_linq_grouping_last_or_default_pred_dec")]
    [InlineData(BucketShape.ElementPred, "Single", "il2cpp::il2cpp_linq_grouping_single_pred_dec")]
    [InlineData(BucketShape.ElementPred, "SingleOrDefault", "il2cpp::il2cpp_linq_grouping_single_or_default_pred_dec")]
    [InlineData(BucketShape.ElementAt, "ElementAt", "il2cpp::il2cpp_linq_grouping_element_at_dec")]
    public void Resolve_DecimalBucket_ElementOperators_MapsToSnapshotKernel(
        BucketShape shape, string name, string icall)
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp(name, BucketSig(shape)),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(shape == BucketShape.ElementNoPred ? 1 : 2, resolved.ParamCount);
        Assert.Equal(
            shape == BucketShape.ElementAt ? new[] { "", "i" }
                : shape == BucketShape.ElementNoPred ? new[] { "" }
                : new[] { "", "" },
            resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData(BucketShape.RangeNoPred, "Reverse", "il2cpp::il2cpp_linq_grouping_reverse_dec")]
    [InlineData(BucketShape.RangeNoPred, "Distinct", "il2cpp::il2cpp_linq_grouping_distinct_dec")]
    [InlineData(BucketShape.RangePred, "Where", "il2cpp::il2cpp_linq_grouping_where_dec")]
    [InlineData(BucketShape.RangePred, "TakeWhile", "il2cpp::il2cpp_linq_grouping_take_while_dec")]
    [InlineData(BucketShape.RangePred, "SkipWhile", "il2cpp::il2cpp_linq_grouping_skip_while_dec")]
    [InlineData(BucketShape.RangeIndex, "Take", "il2cpp::il2cpp_linq_grouping_take_dec")]
    [InlineData(BucketShape.RangeIndex, "Skip", "il2cpp::il2cpp_linq_grouping_skip_dec")]
    [InlineData(BucketShape.TwoSeq, "Except", "il2cpp::il2cpp_linq_grouping_except_dec")]
    [InlineData(BucketShape.TwoSeq, "Concat", "il2cpp::il2cpp_linq_grouping_concat_dec")]
    [InlineData(BucketShape.SeqDecValue, "DefaultIfEmpty", "il2cpp::il2cpp_linq_grouping_default_if_empty_dec")]
    public void Resolve_DecimalBucket_RangeOperators_MapsToSnapshotKernel(
        BucketShape shape, string name, string icall)
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp(name, BucketSig(shape)),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
        Assert.Equal(shape == BucketShape.RangeNoPred ? 1 : 2, resolved.ParamCount);
        Assert.Equal(shape == BucketShape.RangeIndex ? new[] { "", "i" }
            : shape == BucketShape.RangeNoPred ? new[] { "" }
            : shape == BucketShape.TwoSeq ? new[] { "", "a" }
            : new[] { "", "" },
            resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData(BucketShape.BoolPred, "Any", "il2cpp::il2cpp_linq_grouping_any_pred_dec")]
    [InlineData(BucketShape.BoolPred, "All", "il2cpp::il2cpp_linq_grouping_all_dec")]
    [InlineData(BucketShape.TwoSeqBool, "SequenceEqual", "il2cpp::il2cpp_linq_grouping_sequence_equal_dec")]
    public void Resolve_DecimalBucket_BoolOperators_MapsToSnapshotKernel(
        BucketShape shape, string name, string icall)
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp(name, BucketSig(shape)),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(shape == BucketShape.TwoSeqBool ? new[] { "", "a" } : new[] { "", "" },
            resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DecimalBucket_CountPredicate_MapsToSnapshotKernel()
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp("Count", BucketSig(BucketShape.IntPred)),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_count_pred_dec", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DecimalBucket_LongCount_MapsToSnapshotKernel()
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp("LongCount", BucketSig(BucketShape.LongCount)),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_long_count_dec", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData(0x08, false, "il2cpp::il2cpp_linq_grouping_order_by_key_dec_i4")]
    [InlineData(0x0A, false, "il2cpp::il2cpp_linq_grouping_order_by_key_dec_i8")]
    [InlineData(0x0D, false, "il2cpp::il2cpp_linq_grouping_order_by_key_dec_d")]
    [InlineData(0x11, false, "il2cpp::il2cpp_linq_grouping_order_by_key_dec_dec")]
    [InlineData(0x08, true, "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_i4")]
    [InlineData(0x0A, true, "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_i8")]
    [InlineData(0x0D, true, "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_d")]
    [InlineData(0x11, true, "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_dec")]
    public void Resolve_DecimalBucket_OrderBy_MapsToKeySortKernel(byte keyByte, bool desc, string icall)
    {
        var resolved = ResolveMethodSpecWithReceiver(
            SynthesizeGenericGroupingDecOp(desc ? "OrderByDescending" : "OrderBy",
                new byte[] { 0x10, 0x02, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                    0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                    0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, keyByte == 0x11 ? (byte)0x11 : keyByte },
                genCount: 2, keyByte: keyByte),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    // Aggregate with a DECIMAL accumulator (net48 Aggregate<TSource,decimal>):
    // the 16-byte result rides an explicit result slot and the seed crosses as a
    // decimal value slot.
    [Fact]
    public void Resolve_AggregateDecimalAccumulator_MapsToDecimalResultSlotKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericAggregateDecimal("Aggregate"));

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "a", "dec", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (A): the DECIMAL-SOURCE Aggregate family (net48 Aggregate
    // over a System.Decimal source -- Enumerable.cs:1379/1391/1399). The old
    // mapping answered null (the phantom "no BCL mapping") for the no-seed
    // form, ran the 3-arity seed fold over the int32 reader (I4At) for a
    // decimal source (a silent wrong value) and mapped the 4-arity
    // (seed, func, resultSelector) form to the 3-parameter kernel (the
    // resultSelector was dropped and the emitter consumed one stack value too
    // few -- a stack desync).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AggregateDecimalSource_NoSeed_MapsToDecimalFoldKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeAggregateDecNoSeed());

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AggregateDecimalSource_Seed_MapsToDecimalSourceFoldKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeAggregateDecSeed());

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "a", "dec", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AggregateDecimalAccumulator_ResultSelector_MapsToRsKernel()
    {
        // int32 source, decimal accumulator, decimal TResult
        // (net48 Aggregate<int,decimal,decimal>(source, seed, func, resultSelector)).
        var resolved = ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "a", "dec", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AggregateDecimalAccumulator_ResultSelectorTResultFamilies()
    {
        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_i4",
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x08)).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_i8",
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x0A)).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_f",
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x0C)).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_d",
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x0D)).CppName);
        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_dec_rs_ref",
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x08, 0x11, 0x0E)).CppName);
    }

    [Fact]
    public void Resolve_AggregateDecimalSource_ResultSelector_MapsToDecimalSourceRsKernel()
    {
        // decimal source, decimal accumulator, decimal TResult.
        var resolved = ResolveMethodSpec(SynthesizeAggregateDecRs(0x11, 0x11, 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_decsrc_rs_dec", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "a", "dec", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AggregateDecimalSource_NonDecimalAccumulator_ThrowsLoud()
    {
        // A decimal source with a non-decimal accumulator has no fold: the old
        // mapping silently routed it through the ref/int32 fold kernels (a
        // wild read of the 16-byte cells). It must be a transpile-time LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeAggregateDecRs(0x11, 0x0A, 0x0A)));
        Assert.Contains("non-decimal accumulator", ex.Message);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (C): ToArray over a DECIMAL bucket materializes the 16-byte
    // decimal cells (grouping_get_elements copies the bucket's own element
    // width), so the tracked result type is System.Decimal[] -- the old
    // tracking said System.Int32[] and downstream element reads/foreaches
    // resolved the int32 family against a 16-byte array.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GroupingDecimalBucket_ToArray_TracksDecimalArray()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGroupingDecToArray(),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_get_elements", resolved.CppName);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    // The decimal-bucket GroupBy SELECTOR aggregate (net48
    // Sum<decimal>(IGrouping<,decimal>, Func<decimal,decimal>)): the 16-byte
    // result rides an explicit result slot and the bucket's decimal cells feed
    // the selector.
    [Fact]
    public void Resolve_GroupingDecimalSelectorAggregate_MapsToDecimalBucketKernel()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGenericGroupingDecSel("Sum"),
            receiverType: "System.Linq.IGrouping");

        Assert.Equal("il2cpp::il2cpp_linq_grouping_sum_dec_sel", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    // GroupBy over a REFERENCE-element source (net48 GroupBy<string,int>):
    // the reference-source grouping surface (elemKind 1, rsKind 0, keyKind 0).
    [Fact]
    public void Resolve_GroupBy_ReferenceSource_MapsToGroupingFullKey()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGroupByRef(0x0E, 0x08),
            receiverType: "System.String[]");

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "1", "0", "0", "1" }, resolved.AppendLiterals);
    }

    // GroupBy with a REFERENCE key (net48 GroupBy<string,string>): the bucket
    // key rides the keyRef slot (keyKind 11).
    [Fact]
    public void Resolve_GroupBy_ReferenceKey_MapsWithKeyKind11()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGroupByRef(0x0E, 0x0E),
            receiverType: "System.String[]");

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "1", "0", "11", "1" }, resolved.AppendLiterals);
    }

    // GroupBy over a DateTime SOURCE (net48 GroupBy<DateTime,int>): the
    // DateTime-source grouping surface (srcKind 3, elemKind 5).
    [Fact]
    public void Resolve_GroupBy_DateTimeSource_MapsToGroupingFullKey()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupByDt(0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "5", "0", "0", "3" }, resolved.AppendLiterals);
    }

    // Where over a DECIMAL source (net48 Where<decimal>): the decimal-source
    // predicate surface (il2cpp_linq_where_dec).
    [Fact]
    public void Resolve_Where_DecimalSource_MapsToWhereDec()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericSelector("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x11 }));

        Assert.Equal("il2cpp::il2cpp_linq_where_dec", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    // GroupBy over a Guid SOURCE (net48 GroupBy<Guid,int>): the Guid-source
    // grouping surface (srcKind 4, elemKind 6).
    [Fact]
    public void Resolve_GroupBy_GuidSource_MapsToGroupingFullKey()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupByGuid(0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "6", "0", "0", "4" }, resolved.AppendLiterals);
    }

    // OrderBy / OrderByDescending over a DECIMAL source with an int32 TKey (net48
    // OrderBy<decimal,int>): the decimal-source keyed sort.
    [Theory]
    [InlineData("OrderBy", "il2cpp::il2cpp_linq_order_by_key_dec_i4")]
    [InlineData("OrderByDescending", "il2cpp::il2cpp_linq_order_by_key_desc_dec_i4")]
    public void Resolve_OrderBy_DecimalSource_MapsToDecKeySort(string name, string cpp)
    {
        var resolved = ResolveMethodSpec(SynthesizeOrderByDec(name, 0x08));

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    // The DECIMAL-source set / concat / equality operators (net48 Distinct /
    // Except / Concat / DefaultIfEmpty / SequenceEqual): the decimal-source
    // surface.
    [Theory]
    [InlineData("Distinct", "il2cpp::il2cpp_linq_distinct_dec", 1)]
    [InlineData("Except", "il2cpp::il2cpp_linq_except_dec", 2)]
    [InlineData("Concat", "il2cpp::il2cpp_linq_concat_dec", 2)]
    [InlineData("SequenceEqual", "il2cpp::il2cpp_linq_sequence_equal_dec", 2)]
    public void Resolve_DecimalSource_SetOps_MapToDecimalKernels(string name, string cpp, int paramCount)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: paramCount == 1
                ? new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 }
                : new byte[] { 0x10, 0x01, 0x02, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                    0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x11 }));

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(paramCount, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_DefaultIfEmpty_DecimalSource_MapsToDecimalKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("DefaultIfEmpty",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02, 0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00, 0x11 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x11 }));

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_dec", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    // Select over a DECIMAL source with a REFERENCE result (net48
    // Select<decimal,string>): the decimal-source reference-result surface.
    [Fact]
    public void Resolve_Select_DecimalSource_ReferenceResult_MapsToDecimalKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeSelectDecRef(0x0E));

        Assert.Equal("il2cpp::il2cpp_linq_select_dec_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    // OrderBy / OrderByDescending over a DECIMAL source with an int64 / double /
    // decimal TKey (net48 OrderBy<decimal,TKey>): the decimal-source wide-key sort.
    [Theory]
    [InlineData("OrderBy", 0x0A, "il2cpp::il2cpp_linq_order_by_key_dec_i8")]
    [InlineData("OrderByDescending", 0x0A, "il2cpp::il2cpp_linq_order_by_key_desc_dec_i8")]
    [InlineData("OrderBy", 0x0D, "il2cpp::il2cpp_linq_order_by_key_dec_d")]
    [InlineData("OrderByDescending", 0x0D, "il2cpp::il2cpp_linq_order_by_key_desc_dec_d")]
    public void Resolve_OrderBy_DecimalSource_WideKey_MapsToDecKeySort(string name, byte tKey, string cpp)
    {
        var resolved = ResolveMethodSpec(SynthesizeOrderByDec(name, tKey));

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_OrderBy_DecimalSource_DecimalKey_MapsToDecKeySort()
    {
        var resolved = ResolveMethodSpec(SynthesizeOrderByDec("OrderBy", 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_order_by_key_dec_dec", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    // GroupBy over a DECIMAL source with an ELEMENT selector (net48
    // GroupBy<decimal,int,int>): the decimal-source grouping surface with the
    // projected int32 element family.
    [Fact]
    public void Resolve_GroupBy_DecimalSource_ElementSelector_MapsToGroupingFullKey()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupByDecSel(0x08, 0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "0", "0", "0", "2" }, resolved.AppendLiterals);
    }

    // GroupBy over a DECIMAL source with a DECIMAL TElement (net48
    // GroupBy<decimal,int,decimal>): the decimal bucket family (4).
    [Fact]
    public void Resolve_GroupBy_DecimalSource_DecimalElement_MapsToDecimalBucket()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupByDecSel(0x08, 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "4", "0", "0", "2" }, resolved.AppendLiterals);
    }

    //--------------------------------------------------------------------------
    // The INDEX overloads over a List<T> (net48 Func<TSource,int,bool> /
    // Func<TSource,int,TResult>): the callback receives the element AND its
    // zero-based source index. Before this change both LOUD-rejected ("the
    // runtime has no list index-predicate/selector entry").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Where_ListIndexPredicate_MapsToListWhereIdx()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x03, 0x13, 0x00, 0x08, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_where_idx", resolved.CppName);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Select_ListIndexSelector_MapsToListSelectIdx()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x03, 0x13, 0x00, 0x08, 0x0A },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x0A }),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_select_idx", resolved.CppName);
        Assert.Equal("System.Int64[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    // The 1-arg List Where/Select keep their non-index entries (the Func`2
    // shape) -- the index entries must NOT capture the common overload.
    [Fact]
    public void Resolve_Where_ListOneArgPredicate_StaysListWhere()
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_where", resolved.CppName);
    }

    //==========================================================================
    // The DOUBLE-element List key-selector entries (net48
    // OrderBy<TSource,TKey>(List<TSource>, Func<TSource,TKey>) with
    // TSource=TKey=double). Before this change a List<double>.OrderBy LOUD-
    // rejected ("the list key-selector surface covers int32 and reference
    // elements").
    //==========================================================================
    [Theory]
    [InlineData("OrderBy", "il2cpp::il2cpp_linq_list_order_by_key_d")]
    [InlineData("OrderByDescending", "il2cpp::il2cpp_linq_list_order_by_key_desc_d")]
    public void Resolve_ListDouble_OrderBy_MapsToListDoubleKey(string name, string icall)
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0D, 0x0D }),
            receiverType: "System.Collections.Generic.List`1_double");

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Double[]", resolved.ReturnTypeName);
    }

    // A List<double>.OrderBy with a non-double key (e.g. an int32 projection)
    // stays LOUD -- the list double key-selector surface covers a double/float
    // key only.
    [Fact]
    public void Resolve_ListDouble_OrderByIntKey_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpecWithReceiver(SynthesizeGeneric("OrderBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x08 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0D, 0x08 }),
            receiverType: "System.Collections.Generic.List`1_double"));

        Assert.Contains("List<double>", ex.Message);
        Assert.Contains("double/float key only", ex.Message);
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
    [InlineData("OrderBy", 0x0D, "il2cpp::il2cpp_linq_order_by_key_i8_d")]
    [InlineData("OrderByDescending", 0x08, "il2cpp::il2cpp_linq_order_by_key_desc_i8_i4")]
    [InlineData("OrderByDescending", 0x0A, "il2cpp::il2cpp_linq_order_by_key_desc_i8_i8")]
    [InlineData("OrderByDescending", 0x0D, "il2cpp::il2cpp_linq_order_by_key_desc_i8_d")]
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
    // (reference) stays LOUD -- no i8-source kernel exists. The float/double key
    // family is wired now (il2cpp_linq_order_by_key[_desc]_i8_d).
    //--------------------------------------------------------------------------
    [Theory]
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

    // p0linq4 (D4) extension: an int32 source with an INT64 / DOUBLE key now has
    // a fused kernel (il2cpp_linq_then_by_key[_desc]_i4_i8 / _i4_d) -- the marker
    // is returned, never a silent int32 sort.
    [Theory]
    [InlineData(0x0A, ResolvedMethod.ThenByCombineMarker)]
    [InlineData(0x0D, ResolvedMethod.ThenByCombineMarker)]
    public void Resolve_ThenBy_Int32SourceWideKey_MapsToCompositeMarker(byte keyByte, string marker)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ThenBy",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, keyByte }));

        Assert.Equal(marker, resolved.CppName);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    // The int32-source OrderBy with an int64 / double key maps to the wide-key
    // entry (il2cpp_linq_order_by_key[_desc]_i4_i8 / _i4_d).
    [Theory]
    [InlineData("OrderBy", 0x0A, "il2cpp::il2cpp_linq_order_by_key_i4_i8")]
    [InlineData("OrderBy", 0x0D, "il2cpp::il2cpp_linq_order_by_key_i4_d")]
    [InlineData("OrderByDescending", 0x0A, "il2cpp::il2cpp_linq_order_by_key_desc_i4_i8")]
    [InlineData("OrderByDescending", 0x0D, "il2cpp::il2cpp_linq_order_by_key_desc_i4_d")]
    public void Resolve_OrderBy_Int32SourceWideKey_MapsToWideKeyIcall(
        string name, byte keyByte, string icall)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, keyByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //==========================================================================
    // The reference-source selector-aggregate family (net48
    // Sum/Min/Max/Average<TSource>(Func<TSource,TResult>)): the projection's
    // RESULT slot picks the kernel. Before this change only Min/Max with an
    // int32-slot projection were wired; Sum/Average and the int64/double/float
    // projections LOUD-rejected.
    //==========================================================================
    [Theory]
    [InlineData("Sum", 0x08, "il2cpp::il2cpp_linq_sum_sel_ref_i4", null)]
    [InlineData("Average", 0x08, "il2cpp::il2cpp_linq_average_sel_ref_i4", "d")]
    [InlineData("Min", 0x08, "il2cpp::il2cpp_linq_min_sel_ref_i4", null)]
    [InlineData("Max", 0x08, "il2cpp::il2cpp_linq_max_sel_ref_i4", null)]
    [InlineData("Sum", 0x0A, "il2cpp::il2cpp_linq_sum_sel_ref_i8", "l")]
    [InlineData("Min", 0x0A, "il2cpp::il2cpp_linq_min_sel_ref_i8", "l")]
    [InlineData("Max", 0x0A, "il2cpp::il2cpp_linq_max_sel_ref_i8", "l")]
    [InlineData("Average", 0x0A, "il2cpp::il2cpp_linq_average_sel_ref_i8", "d")]
    [InlineData("Sum", 0x0D, "il2cpp::il2cpp_linq_sum_sel_ref_d", "d")]
    [InlineData("Min", 0x0D, "il2cpp::il2cpp_linq_min_sel_ref_d", "d")]
    [InlineData("Max", 0x0D, "il2cpp::il2cpp_linq_max_sel_ref_d", "d")]
    [InlineData("Average", 0x0D, "il2cpp::il2cpp_linq_average_sel_ref_d", "d")]
    [InlineData("Sum", 0x0C, "il2cpp::il2cpp_linq_sum_sel_ref_f", "d")]
    [InlineData("Average", 0x0C, "il2cpp::il2cpp_linq_average_sel_ref_f", "d")]
    public void Resolve_RefSelectorAggregate_MapsToRefSelectorKernel(
        string name, byte proj, string icall, string? rk)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericSelector(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, proj },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(rk, resolved.ResultKind);
    }

    //==========================================================================
    // The WIDE-source selector-aggregate family (net48
    // Sum/Min/Max/Average<TSource>(Func<TSource,TResult>) with TSource = long /
    // float / double). Before this change every one of these LOUD-rejected
    // ("the stage-1 selector-aggregate surface is int32 (0x08) element-family").
    //==========================================================================
    [Theory]
    [InlineData("Sum", 0x0A, 0x0A, "il2cpp::il2cpp_linq_sum_sel_i8src_i8", "l")]
    [InlineData("Min", 0x0A, 0x0A, "il2cpp::il2cpp_linq_min_sel_i8src_i8", "l")]
    [InlineData("Max", 0x0A, 0x0A, "il2cpp::il2cpp_linq_max_sel_i8src_i8", "l")]
    [InlineData("Average", 0x0A, 0x0A, "il2cpp::il2cpp_linq_average_sel_i8src_i8", "d")]
    [InlineData("Sum", 0x0A, 0x08, "il2cpp::il2cpp_linq_sum_sel_i8src_i4", null)]
    [InlineData("Average", 0x0A, 0x08, "il2cpp::il2cpp_linq_average_sel_i8src_i4", "d")]
    [InlineData("Sum", 0x0D, 0x0D, "il2cpp::il2cpp_linq_sum_sel_dsrc_d", "d")]
    [InlineData("Min", 0x0D, 0x0D, "il2cpp::il2cpp_linq_min_sel_dsrc_d", "d")]
    [InlineData("Max", 0x0D, 0x0D, "il2cpp::il2cpp_linq_max_sel_dsrc_d", "d")]
    [InlineData("Sum", 0x0D, 0x08, "il2cpp::il2cpp_linq_sum_sel_dsrc_i4", null)]
    [InlineData("Average", 0x0D, 0x08, "il2cpp::il2cpp_linq_average_sel_dsrc_i4", "d")]
    [InlineData("Sum", 0x0C, 0x0C, "il2cpp::il2cpp_linq_sum_sel_fsrc_f", "d")]
    [InlineData("Average", 0x0C, 0x0C, "il2cpp::il2cpp_linq_average_sel_fsrc_f", "d")]
    [InlineData("Sum", 0x0C, 0x08, "il2cpp::il2cpp_linq_sum_sel_fsrc_i4", null)]
    public void Resolve_WideSelectorAggregate_MapsToWideSelectorKernel(
        string name, byte srcByte, byte projByte, string icall, string? rk)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericSelector(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, projByte },
            genericSignature: new byte[] { 0x0A, 0x01, srcByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(rk, resolved.ResultKind);
    }

    // A wide-source selector with an unsupported projection (a reference
    // result) stays LOUD.
    [Fact]
    public void Resolve_WideSelectorAggregate_RefProjection_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGenericSelector("Sum",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x0A,
                0x15, 0x12, 0x09, 0x01, 0x0A,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x0E },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0A })));

        Assert.Contains("wide-source selector-aggregate surface", ex.Message);
    }

    //==========================================================================
    // impl-linq3 (A): the NARROW-source selector-aggregate family (net48
    // Sum/Min/Max/Average<TSource,TResult> with TSource = sbyte / byte / short
    // / ushort / uint / char). Before this change every narrow source with a
    // selector LOUD-rejected inside the wide-source gate. The kernel suffix
    // mirrors the arrayext2 narrow families: _i1src/_u1src/_i2src/_u2src/
    // _u4src/_csrc; the projection picks the fold kernel.
    //==========================================================================
    [Theory]
    [InlineData("Sum", 0x04, 0x08, "il2cpp::il2cpp_linq_sum_sel_i1src_i4", null)]
    [InlineData("Min", 0x04, 0x08, "il2cpp::il2cpp_linq_min_sel_i1src_i4", null)]
    [InlineData("Max", 0x04, 0x08, "il2cpp::il2cpp_linq_max_sel_i1src_i4", null)]
    [InlineData("Average", 0x04, 0x08, "il2cpp::il2cpp_linq_average_sel_i1src_i4", "d")]
    [InlineData("Sum", 0x05, 0x08, "il2cpp::il2cpp_linq_sum_sel_u1src_i4", null)]
    [InlineData("Min", 0x05, 0x08, "il2cpp::il2cpp_linq_min_sel_u1src_i4", null)]
    [InlineData("Max", 0x05, 0x08, "il2cpp::il2cpp_linq_max_sel_u1src_i4", null)]
    [InlineData("Average", 0x05, 0x08, "il2cpp::il2cpp_linq_average_sel_u1src_i4", "d")]
    [InlineData("Sum", 0x06, 0x08, "il2cpp::il2cpp_linq_sum_sel_i2src_i4", null)]
    [InlineData("Min", 0x06, 0x08, "il2cpp::il2cpp_linq_min_sel_i2src_i4", null)]
    [InlineData("Max", 0x06, 0x08, "il2cpp::il2cpp_linq_max_sel_i2src_i4", null)]
    [InlineData("Average", 0x06, 0x08, "il2cpp::il2cpp_linq_average_sel_i2src_i4", "d")]
    [InlineData("Sum", 0x07, 0x08, "il2cpp::il2cpp_linq_sum_sel_u2src_i4", null)]
    [InlineData("Min", 0x07, 0x08, "il2cpp::il2cpp_linq_min_sel_u2src_i4", null)]
    [InlineData("Max", 0x07, 0x08, "il2cpp::il2cpp_linq_max_sel_u2src_i4", null)]
    [InlineData("Average", 0x07, 0x08, "il2cpp::il2cpp_linq_average_sel_u2src_i4", "d")]
    [InlineData("Sum", 0x09, 0x08, "il2cpp::il2cpp_linq_sum_sel_u4src_i4", null)]
    [InlineData("Min", 0x09, 0x08, "il2cpp::il2cpp_linq_min_sel_u4src_i4", null)]
    [InlineData("Max", 0x09, 0x08, "il2cpp::il2cpp_linq_max_sel_u4src_i4", null)]
    [InlineData("Average", 0x09, 0x08, "il2cpp::il2cpp_linq_average_sel_u4src_i4", "d")]
    [InlineData("Sum", 0x03, 0x08, "il2cpp::il2cpp_linq_sum_sel_csrc_i4", null)]
    [InlineData("Min", 0x03, 0x08, "il2cpp::il2cpp_linq_min_sel_csrc_i4", null)]
    [InlineData("Max", 0x03, 0x08, "il2cpp::il2cpp_linq_max_sel_csrc_i4", null)]
    [InlineData("Average", 0x03, 0x08, "il2cpp::il2cpp_linq_average_sel_csrc_i4", "d")]
    public void Resolve_NarrowSelectorAggregate_IntProjection_MapsToNarrowKernel(
        string name, byte srcByte, byte projByte, string icall, string? rk)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericSelector(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, projByte },
            genericSignature: new byte[] { 0x0A, 0x01, srcByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(rk, resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    // The wide projections over one representative narrow source (byte): the
    // same _{srcSuffix}_{projSuffix} kernel naming as the wide family.
    [Theory]
    [InlineData("Sum", 0x05, 0x0A, "il2cpp::il2cpp_linq_sum_sel_u1src_i8", "l")]
    [InlineData("Min", 0x05, 0x0A, "il2cpp::il2cpp_linq_min_sel_u1src_i8", "l")]
    [InlineData("Max", 0x05, 0x0A, "il2cpp::il2cpp_linq_max_sel_u1src_i8", "l")]
    [InlineData("Average", 0x05, 0x0A, "il2cpp::il2cpp_linq_average_sel_u1src_i8", "d")]
    [InlineData("Sum", 0x05, 0x0C, "il2cpp::il2cpp_linq_sum_sel_u1src_f", "d")]
    [InlineData("Min", 0x05, 0x0C, "il2cpp::il2cpp_linq_min_sel_u1src_f", "d")]
    [InlineData("Max", 0x05, 0x0C, "il2cpp::il2cpp_linq_max_sel_u1src_f", "d")]
    [InlineData("Average", 0x05, 0x0C, "il2cpp::il2cpp_linq_average_sel_u1src_f", "d")]
    [InlineData("Sum", 0x05, 0x0D, "il2cpp::il2cpp_linq_sum_sel_u1src_d", "d")]
    [InlineData("Min", 0x05, 0x0D, "il2cpp::il2cpp_linq_min_sel_u1src_d", "d")]
    [InlineData("Max", 0x05, 0x0D, "il2cpp::il2cpp_linq_max_sel_u1src_d", "d")]
    [InlineData("Average", 0x05, 0x0D, "il2cpp::il2cpp_linq_average_sel_u1src_d", "d")]
    [InlineData("Sum", 0x03, 0x0D, "il2cpp::il2cpp_linq_sum_sel_csrc_d", "d")]
    [InlineData("Average", 0x03, 0x0C, "il2cpp::il2cpp_linq_average_sel_csrc_f", "d")]
    [InlineData("Max", 0x09, 0x0A, "il2cpp::il2cpp_linq_max_sel_u4src_i8", "l")]
    [InlineData("Min", 0x06, 0x0C, "il2cpp::il2cpp_linq_min_sel_i2src_f", "d")]
    public void Resolve_NarrowSelectorAggregate_WideProjection_MapsToNarrowKernel(
        string name, byte srcByte, byte projByte, string icall, string? rk)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericSelector(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x09, 0x01, srcByte,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, projByte },
            genericSignature: new byte[] { 0x0A, 0x01, srcByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(rk, resolved.ResultKind);
    }

    // A narrow-source selector with an unsupported projection (a reference
    // result) stays LOUD.
    [Fact]
    public void Resolve_NarrowSelectorAggregate_RefProjection_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGenericSelector("Sum",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x05,
                0x15, 0x12, 0x09, 0x01, 0x05,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x0E },
            genericSignature: new byte[] { 0x0A, 0x01, 0x05 })));

        Assert.Contains("value-source selector-aggregate surface", ex.Message);
    }

    //==========================================================================
    // The reference-source Select with a VALUE result (net48
    // Select<TSource,TResult> over a reference source): int32 / int64 / double /
    // float. Before this change the non-reference non-decimal results LOUD-
    // rejected.
    //==========================================================================
    [Theory]
    [InlineData(0x08, "il2cpp::il2cpp_linq_select_ref_i4", "System.Int32[]")]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_select_ref_i8", "System.Int64[]")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_select_ref_d", "System.Double[]")]
    [InlineData(0x0C, "il2cpp::il2cpp_linq_select_ref_f", "System.Double[]")]
    public void Resolve_RefSelect_ValueResult_MapsToRefValueKernel(byte resByte, string icall, string retType)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, resByte,
                0x15, 0x12, 0x09, 0x01, 0x0E,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x0E, resByte },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0E, resByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(retType, resolved.ReturnTypeName);
    }

    // The reference-source INDEX Select with a VALUE result (net48
    // Select<TSource,int,TResult>): the selector receives the element AND its
    // zero-based source index.
    [Theory]
    [InlineData(0x08, "il2cpp::il2cpp_linq_select_idx_ref_i4", "System.Int32[]")]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_select_idx_ref_i8", "System.Int64[]")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_select_idx_ref_d", "System.Double[]")]
    [InlineData(0x0C, "il2cpp::il2cpp_linq_select_idx_ref_f", "System.Double[]")]
    public void Resolve_RefSelect_IndexValueResult_MapsToRefIndexValueKernel(byte resByte, string icall, string retType)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, resByte,
                0x15, 0x12, 0x09, 0x01, 0x0E,
                0x15, 0x12, 0x0B, 0x03, 0x13, 0x00, 0x0E, 0x08, resByte },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0E, resByte }));

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(retType, resolved.ReturnTypeName);
    }

    //==========================================================================
    // The reference-source Select with a VALUE-STRUCT result (net48
    // Select<TSource,TResult> over a reference source): DateTime / TimeSpan /
    // Guid. The 0x11 VALUETYPE byte is name-resolved by the MethodSpec TypeRef.
    //==========================================================================
    [Theory]
    [InlineData("System.DateTime", false, "il2cpp::il2cpp_linq_select_ref_dt", "System.DateTime[]")]
    [InlineData("System.TimeSpan", false, "il2cpp::il2cpp_linq_select_ref_ts", "System.TimeSpan[]")]
    [InlineData("System.Guid", false, "il2cpp::il2cpp_linq_select_ref_guid", "System.Guid[]")]
    [InlineData("System.DateTime", true, "il2cpp::il2cpp_linq_select_idx_ref_dt", "System.DateTime[]")]
    [InlineData("System.TimeSpan", true, "il2cpp::il2cpp_linq_select_idx_ref_ts", "System.TimeSpan[]")]
    [InlineData("System.Guid", true, "il2cpp::il2cpp_linq_select_idx_ref_guid", "System.Guid[]")]
    public void Resolve_RefSelect_ValueStructResult_MapsToRefValueStructKernel(string resType, bool indexForm, string icall, string retType)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericValueSourceValueResult("Select", indexForm, 0x0E, resType));
        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(retType, resolved.ReturnTypeName);
    }

    //==========================================================================
    // The INT32-source Select with a VALUE-STRUCT result (net48 Select<int,
    // TResult>): DateTime / TimeSpan / Guid. The selector returns the struct BY
    // VALUE (the same convention as the decimal result).
    //==========================================================================
    [Theory]
    [InlineData("System.DateTime", false, "il2cpp::il2cpp_linq_select_i4_dt", "System.DateTime[]")]
    [InlineData("System.TimeSpan", false, "il2cpp::il2cpp_linq_select_i4_ts", "System.TimeSpan[]")]
    [InlineData("System.Guid", false, "il2cpp::il2cpp_linq_select_i4_guid", "System.Guid[]")]
    [InlineData("System.DateTime", true, "il2cpp::il2cpp_linq_select_idx_i4_dt", "System.DateTime[]")]
    [InlineData("System.TimeSpan", true, "il2cpp::il2cpp_linq_select_idx_i4_ts", "System.TimeSpan[]")]
    [InlineData("System.Guid", true, "il2cpp::il2cpp_linq_select_idx_i4_guid", "System.Guid[]")]
    public void Resolve_I4Select_ValueStructResult_MapsToI4ValueStructKernel(string resType, bool indexForm, string icall, string retType)
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericValueSourceValueResult("Select", indexForm, 0x08, resType));
        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(retType, resolved.ReturnTypeName);
    }

    //==========================================================================
    // The DECIMAL-source Select with a VALUE result (net48
    // Select<decimal,TResult> and the (element,index) form): int32 / int64 /
    // double / float. Before this change the non-decimal results LOUD-rejected
    // ("the decimal-element source surface covers a System.Decimal result only").
    //==========================================================================
    [Theory]
    [InlineData(0x08, 2, "il2cpp::il2cpp_linq_select_dec_i4", "System.Int32[]")]
    [InlineData(0x0A, 2, "il2cpp::il2cpp_linq_select_dec_i8", "System.Int64[]")]
    [InlineData(0x0D, 2, "il2cpp::il2cpp_linq_select_dec_d", "System.Double[]")]
    [InlineData(0x0C, 2, "il2cpp::il2cpp_linq_select_dec_f", "System.Double[]")]
    [InlineData(0x08, 3, "il2cpp::il2cpp_linq_select_idx_dec_i4", "System.Int32[]")]
    [InlineData(0x0A, 3, "il2cpp::il2cpp_linq_select_idx_dec_i8", "System.Int64[]")]
    [InlineData(0x0D, 3, "il2cpp::il2cpp_linq_select_idx_dec_d", "System.Double[]")]
    public void Resolve_DecimalSourceSelect_ValueResult_MapsToDecimalValueKernel(
        byte resByte, int funcArgs, string icall, string retType)
    {
        var memberRef = funcArgs == 3
            ? new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x03, 0x13, 0x00, 0x08, resByte }
            : new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, resByte };
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource("Select", memberRef, resByte));
        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(retType, resolved.ReturnTypeName);
    }

    // The decimal-source INDEX Select with a decimal result (net48
    // Select<decimal,int,decimal>).
    [Fact]
    public void Resolve_DecimalSourceSelect_IndexDecimal_MapsToSelectIdxDecDec()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericDecimalSource("Select",
            new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x03, 0x13, 0x00, 0x08, 0x11 },
            0x11));
        Assert.Equal("il2cpp::il2cpp_linq_select_idx_dec_dec", resolved.CppName);
        Assert.Equal("System.Decimal[]", resolved.ReturnTypeName);
    }

    //==========================================================================
    // D-LINQ-FAM (loud-linq-fam): the FAMILY-GENERIC element operators over the
    // int64/float/double/narrow array families. The runtime ships ONE kernel per
    // operator (linq_family.h) with the cell code as the trailing literal; the
    // wide/narrow dispatch (TryMapLinqWide) wires them. Before this change the
    // operators below LOUD-rejected over every non-int32 array ("the stage-1
    // runtime LINQ surface is int32 (0x08) element-family").
    //
    // Signature shapes (System.Linq.Enumerable):
    //   Take/Skip/ElementAt(source, int)
    //   Reverse/Distinct/DefaultIfEmpty/LongCount(source)
    //   Concat/Except/Union/Intersect/SequenceEqual(source, second)
    //   Contains(source, value)
    //==========================================================================

    // The 1-arg IEnumerable<TSource> MemberRef signature.
    private static byte[] FamI1() =>
        new byte[] { 0x10, 0x01, 0x01, 0x15,
            0x12, 0x09, 0x01, 0x13, 0x00,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00 };

    // The 2-arg (IEnumerable<TSource>, IEnumerable<TSource>) MemberRef signature.
    private static byte[] FamI2() =>
        new byte[] { 0x10, 0x01, 0x02, 0x15,
            0x12, 0x09, 0x01, 0x13, 0x00,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00 };

    // The 2-arg (IEnumerable<TSource>, int) MemberRef signature.
    private static byte[] FamIInt() =>
        new byte[] { 0x10, 0x01, 0x02, 0x15,
            0x12, 0x09, 0x01, 0x13, 0x00,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
            0x08 };

    [Theory]
    // name, elemByte, expected icall, tracked return type, cell literal
    [InlineData("Reverse", 0x0A, "il2cpp::il2cpp_linq_reverse_family", "System.Int64[]", "8")]
    [InlineData("Reverse", 0x0D, "il2cpp::il2cpp_linq_reverse_family", "System.Double[]", "10")]
    [InlineData("Reverse", 0x05, "il2cpp::il2cpp_linq_reverse_family", "System.Byte[]", "1")]
    [InlineData("Distinct", 0x0A, "il2cpp::il2cpp_linq_distinct_family", "System.Int64[]", "8")]
    [InlineData("Distinct", 0x0D, "il2cpp::il2cpp_linq_distinct_family", "System.Double[]", "10")]
    [InlineData("DefaultIfEmpty", 0x0A, "il2cpp::il2cpp_linq_default_if_empty_family", "System.Int64[]", "8")]
    [InlineData("DefaultIfEmpty", 0x0C, "il2cpp::il2cpp_linq_default_if_empty_family", "System.Single[]", "9")]
    public void Resolve_WideNarrow_OneArgArrayOperators_MapToFamilyKernels(
        string name, byte elem, string expected, string retType, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(retType, resolved.ReturnTypeName);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    [Theory]
    // name, elemByte, expected icall, tracked return type (null = scalar), cell
    [InlineData("Concat", 0x0A, "il2cpp::il2cpp_linq_concat_family", "System.Int64[]", "8")]
    [InlineData("Concat", 0x0D, "il2cpp::il2cpp_linq_concat_family", "System.Double[]", "10")]
    [InlineData("Except", 0x0A, "il2cpp::il2cpp_linq_except_family", "System.Int64[]", "8")]
    [InlineData("Union", 0x0A, "il2cpp::il2cpp_linq_union_family", "System.Int64[]", "8")]
    [InlineData("Intersect", 0x0A, "il2cpp::il2cpp_linq_intersect_family", "System.Int64[]", "8")]
    [InlineData("SequenceEqual", 0x0A, "il2cpp::il2cpp_linq_sequence_equal_family", null, "8")]
    public void Resolve_WideNarrow_TwoSequenceOperators_MapToFamilyKernels(
        string name, byte elem, string expected, string? retType, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamI2(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "a" }, resolved.ParamTypeNames);
        if (retType != null)
        {
            Assert.True(resolved.ReturnsRef);
            Assert.Equal(retType, resolved.ReturnTypeName);
        }
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    [Theory]
    [InlineData("Take", 0x0A, "il2cpp::il2cpp_linq_take_family", "System.Int64[]", "8")]
    [InlineData("Take", 0x0D, "il2cpp::il2cpp_linq_take_family", "System.Double[]", "10")]
    [InlineData("Skip", 0x0A, "il2cpp::il2cpp_linq_skip_family", "System.Int64[]", "8")]
    [InlineData("Skip", 0x05, "il2cpp::il2cpp_linq_skip_family", "System.Byte[]", "1")]
    public void Resolve_WideNarrow_TakeSkip_MapToFamilyKernels(
        string name, byte elem, string expected, string retType, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamIInt(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
        Assert.Equal(retType, resolved.ReturnTypeName);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    [Theory]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_long_count_family", "8")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_long_count_family", "10")]
    public void Resolve_WideNarrow_LongCount_MapToFamilyKernel(byte elem, string expected, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("LongCount",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x01, 0x08, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    [Theory]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_contains_family_i8", "8")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_contains_family_d", "10")]
    [InlineData(0x05, "il2cpp::il2cpp_linq_contains_family_i4", "1")]
    public void Resolve_WideNarrow_Contains_MapToFamilyKernel(byte elem, string expected, string cell)
    {
        // Contains<TSource>(IEnumerable<TSource> source, TSource value): the
        // value parameter is the TSource element byte itself.
        var sig = new System.Collections.Generic.List<byte> { 0x10, 0x01, 0x02, 0x02,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00 };
        sig.Add(elem);
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Contains",
            memberRefSignature: sig.ToArray(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    [Theory]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_element_at_fam_i8", "l", "8")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_element_at_fam_d", "d", "10")]
    [InlineData(0x05, "il2cpp::il2cpp_linq_element_at_fam_i4", null, "1")]
    public void Resolve_WideNarrow_ElementAt_MapToFamilyKernel(byte elem, string expected, string? resultKind, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ElementAt",
            memberRefSignature: FamIInt(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(resultKind, resolved.ResultKind);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    // D-LINQ-FAM: the family-generic element operators over a REFERENCE source
    // (string) -- the reference cell (15). TryMapLinqWide only runs for the
    // value families, so the reference source rides the ref block.
    [Theory]
    [InlineData("Take", "il2cpp::il2cpp_linq_take_family", 2)]
    [InlineData("Skip", "il2cpp::il2cpp_linq_skip_family", 2)]
    [InlineData("Reverse", "il2cpp::il2cpp_linq_reverse_family", 1)]
    [InlineData("Concat", "il2cpp::il2cpp_linq_concat_family", 2)]
    [InlineData("Distinct", "il2cpp::il2cpp_linq_distinct_family", 1)]
    [InlineData("Except", "il2cpp::il2cpp_linq_except_family", 2)]
    [InlineData("Union", "il2cpp::il2cpp_linq_union_family", 2)]
    [InlineData("Intersect", "il2cpp::il2cpp_linq_intersect_family", 2)]
    [InlineData("DefaultIfEmpty", "il2cpp::il2cpp_linq_default_if_empty_family", 1)]
    [InlineData("SequenceEqual", "il2cpp::il2cpp_linq_sequence_equal_family", 2)]
    [InlineData("LongCount", "il2cpp::il2cpp_linq_long_count_family", 1)]
    public void Resolve_RefFamily_ArrayOperators_MapToFamilyKernels(string name, string expected, int pc)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: pc == 2 ? FamI2() : FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(pc, resolved.ParamCount);
        Assert.Equal(new[] { "15" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_RefFamily_Contains_MapsToRefKernel()
    {
        // Contains<string>(IEnumerable<string>, string): the value rides the
        // reference slot (cast ""), the source the "a" array cast.
        var sig = new System.Collections.Generic.List<byte> { 0x10, 0x01, 0x02, 0x02,
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00 };
        sig.Add(0x0E);
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Contains",
            memberRefSignature: sig.ToArray(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_contains_family_ref", resolved.CppName);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "15" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_RefFamily_ElementAt_MapsToRefKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("ElementAt",
            memberRefSignature: FamIInt(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_element_at_fam_ref", resolved.CppName);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.Equal(new[] { "15" }, resolved.AppendLiterals);
    }

    //==========================================================================
    // D-LINQ-FAM (loud-linq-single): the SOURCE-ONLY Single / SingleOrDefault
    // operators over the int64/float/double/narrow array families. The runtime
    // already ships ONE family kernel per result ABI (linq_family.h
    // il2cpp_linq_single_fam_i4/_i8/_d/_ref and single_or_default_fam_*), but the
    // wide/narrow dispatch (TryMapLinqWide) did not wire them, so
    // long[].Single() / double[].SingleOrDefault() LOUD-rejected.
    //
    // net48 semantics (the kernel's contract): Single throws
    // InvalidOperationException for an empty OR a multi-element source;
    // SingleOrDefault answers default(T) for an empty source and throws only
    // for more than one element.
    //==========================================================================
    [Theory]
    [InlineData("Single", 0x0A, "il2cpp::il2cpp_linq_single_fam_i8", "l", "8")]
    [InlineData("Single", 0x0D, "il2cpp::il2cpp_linq_single_fam_d", "d", "10")]
    [InlineData("Single", 0x0C, "il2cpp::il2cpp_linq_single_fam_d", "d", "9")]
    [InlineData("Single", 0x05, "il2cpp::il2cpp_linq_single_fam_i4", null, "1")]
    [InlineData("SingleOrDefault", 0x0A, "il2cpp::il2cpp_linq_single_or_default_fam_i8", "l", "8")]
    [InlineData("SingleOrDefault", 0x0D, "il2cpp::il2cpp_linq_single_or_default_fam_d", "d", "10")]
    [InlineData("SingleOrDefault", 0x05, "il2cpp::il2cpp_linq_single_or_default_fam_i4", null, "1")]
    public void Resolve_WideNarrow_Single_MapToFamilyKernels(
        string name, byte elem, string expected, string? resultKind, string cell)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, elem }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
        Assert.Equal(new[] { cell }, resolved.AppendLiterals);
    }

    // The int32 array path: Single/SingleOrDefault ride the family kernel with
    // the int32 cell (7).
    [Theory]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_fam_i4")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_fam_i4")]
    public void Resolve_Int32_Single_MapToFamilyKernel(string name, string expected)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "7" }, resolved.AppendLiterals);
    }

    // The reference source (string): the ref cell (15) and the Il2CppObject*
    // result ABI.
    [Theory]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_fam_ref")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_fam_ref")]
    public void Resolve_RefFamily_Single_MapsToRefKernel(string name, string expected)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.Equal(new[] { "15" }, resolved.AppendLiterals);
    }

    //--------------------------------------------------------------------------
    // BATCH-3 (b5-gaps item 1): the reference-source (element, index) overloads
    // (net48 Func<TSource,int,...>). The index discriminator is the SECOND
    // parameter's GenericInst argument count (3 for Func<T,int,...>, 2 for
    // Func<T,...>); the runtime kernels il2cpp_linq_where_idx_ref /
    // il2cpp_linq_select_idx_ref_ref are the reference counterparts of the
    // int32 index entries.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Where_StringElement_Index_MapsToWhereIdxRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Where",
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x03, 0x13, 0x00, 0x08, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_where_idx_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Select_StringElement_IndexRef_MapsToSelectIdxRefRef()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("Select",
            memberRefSignature: new byte[] { 0x10, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x03, 0x13, 0x00, 0x08, 0x13, 0x01 },
            genericSignature: new byte[] { 0x0A, 0x02, 0x0E, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_linq_select_idx_ref_ref", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    // BATCH-3 (b5-gaps item 2): Single / SingleOrDefault WITH a predicate. The
    // 2-parameter form (source + Func<TSource,bool>) routes to the new
    // il2cpp_linq_single[_or_default]_pred_i4/_ref kernels.
    [Theory]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_pred_i4")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_pred_i4")]
    public void Resolve_Int32_SinglePredicate_MapsToKernel(string name, string expected)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("Single", "il2cpp::il2cpp_linq_single_pred_ref")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_single_or_default_pred_ref")]
    public void Resolve_RefFamily_SinglePredicate_MapsToKernel(string name, string expected)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(name,
            memberRefSignature: new byte[] { 0x10, 0x01, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0B, 0x02, 0x13, 0x00, 0x02 },
            genericSignature: new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    // The List<int> source: the list-domain single kernels (no array cast).
    [Theory]
    [InlineData("Single", "il2cpp::il2cpp_linq_list_single_i4")]
    [InlineData("SingleOrDefault", "il2cpp::il2cpp_linq_list_single_or_default_i4")]
    public void Resolve_ListInt_Single_MapsToListKernel(string name, string expected)
    {
        var resolved = ResolveMethodSpecWithReceiver(SynthesizeGeneric(name,
            memberRefSignature: FamI1(),
            genericSignature: new byte[] { 0x0A, 0x01, 0x08 }),
            receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal(new[] { "0" }, resolved.AppendLiterals);
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

    // Selector-form helper: like SynthesizeGeneric but the PE also carries the
    // IEnumerable`1 and Func`2 TypeRefs, so the MethodSignatureCoder.Decode path
    // (LinqSelectorProjectionType) can RESOLVE their names -- the plain
    // SynthesizeGeneric PE has only the Enumerable TypeRef and the Decode path
    // would read an out-of-range TypeRef handle.
    //   row 1: System.Linq.Enumerable          (coded 0x05)
    //   row 2: System.Collections.Generic.IEnumerable`1 (coded 0x09)
    //   row 3: System.Func`2                   (coded 0x0D)
    private static MemoryStream SynthesizeGenericSelector(string methodName, byte[] memberRefSignature,
        byte[] genericSignature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }

        // DECIMAL-source helper: a MethodSpec whose TSource is a VALUETYPE TypeRef
    // (System.Decimal) so IsDecimalGenArg resolves the name; TResult is the
    // raw dispatch byte. Carries the IEnumerable`1 / Func`2 / Decimal TypeRefs
    // so the Decode path can resolve them.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeGenericDecimalSource(string methodName, byte[] memberRefSignature,
        byte resByte)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(memberRefSignature));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(decRef) << 2) | 1);
        if (resByte == 0x11)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(decRef) << 2) | 1);
        }
        else
        {
            spec.WriteByte(resByte);
        }
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // VALUE-source Select helper: TSource is a primitive byte (0x08 int32 /
    // 0x0E string) and TResult is a named VALUETYPE TypeRef (the 0x11 byte is
    // name-resolved to System.DateTime / System.TimeSpan / System.Guid).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: <result value type>        (coded 0x11)
    private static MemoryStream SynthesizeGenericValueSourceValueResult(string methodName, bool indexForm,
        byte srcByte, string resTypeName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var lastDot = resTypeName.LastIndexOf('.');
        var resNs = lastDot > 0 ? resTypeName[..lastDot] : "";
        var resName = lastDot > 0 ? resTypeName[(lastDot + 1)..] : resTypeName;
        var resRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(resNs), mdb.GetOrAddString(resName));
        var coded = (MetadataTokens.GetRowNumber(resRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(coded);
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(srcByte);
        // Func`2 is TypeRef row 3 -> TypeDefOrRef coded (3<<2)|1 = 0x0D (a tag-3
        // byte would decode as a ModuleDefinition handle and throw in M2).
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D);
        sig.WriteByte(indexForm ? (byte)0x03 : (byte)0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(srcByte);
        if (indexForm) sig.WriteByte(0x08);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(coded);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(srcByte);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(coded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // List<T> selector-aggregate helper: TSource is int32 (0x08) and the
    // projection is a named VALUETYPE TypeRef (System.Decimal). The Func`2
    // carries exactly TWO arguments (TSource, TResult) so the MethodSignature
    // Decode path (LinqSelectorProjectionType) parses it.
    private static MemoryStream SynthesizeGenericListAggDecimalProjection(string methodName, string resTypeName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var lastDot = resTypeName.LastIndexOf('.');
        var resNs = lastDot > 0 ? resTypeName[..lastDot] : "";
        var resName = lastDot > 0 ? resTypeName[(lastDot + 1)..] : resTypeName;
        var resRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(resNs), mdb.GetOrAddString(resName));
        var resCoded = (MetadataTokens.GetRowNumber(resRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x08);                                        // return int32
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x08);                                        // IEnumerable<int32>
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x08);                                        // Func<int32, ...> arg1
        sig.WriteByte(0x11); sig.WriteCompressedInteger(resCoded);  // arg2 (the value type)
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x08);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Aggregate<TSource,TAccumulate> with a DECIMAL accumulator (net48
    // Aggregate<TSource,decimal>(source, seed, Func<decimal,TSource,decimal>)):
    // genCount 2, paramCount 3, the seed/return are VAR 1 (TAccumulate) and the
    // Func`3 carries (TAccumulate, TSource, TAccumulate). TSource is int32
    // (0x08) and TAccumulate is the named System.Decimal VALUETYPE TypeRef.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`3               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeGenericAggregateDecimal(string methodName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`3"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TAccumulate (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // TAccumulate (VAR 1)
        // Func`3 is TypeRef row 3 -> TypeDefOrRef coded (3<<2)|1 = 0x0D.
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x08);                                           // TSource = int32
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TAccumulate = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    //--------------------------------------------------------------------------
    // impl-linq2 (A): the DECIMAL-SOURCE Aggregate helpers.
    //
    // SynthesizeAggregateDecNoSeed -- Aggregate<decimal>(IEnumerable<decimal>,
    // Func<decimal,decimal,decimal>): genCount 1, paramCount 2, ret VAR 0.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`3               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    //--------------------------------------------------------------------------
    private static MemoryStream SynthesizeAggregateDecNoSeed()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`3"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       // return TSource (VAR 0)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   IEnumerable<decimal>
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   Func<decimal, decimal, decimal>
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Aggregate"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // SynthesizeAggregateDecSeed -- Aggregate<decimal,decimal>(
    // IEnumerable<decimal>, decimal, Func<decimal,decimal,decimal>): genCount 2,
    // paramCount 3, ret VAR 1, the seed is TAccumulate (VAR 1).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`3               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeAggregateDecSeed()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`3"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TAccumulate (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   IEnumerable<decimal>
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // TAccumulate (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   Func<TAcc, TSrc, TAcc>
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(0x13); sig.WriteByte(0x01);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Aggregate"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TAccumulate = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // SynthesizeAggregateDecRs -- Aggregate<TSource,TAccumulate,TResult>(
    // source, seed, Func<TAcc,TSource,TAcc>, Func<TAcc,TResult>): genCount 3,
    // paramCount 4, ret VAR 2. The named decimal slots (tSrc/tAcc/tRes == 0x11)
    // are written as the named System.Decimal VALUETYPE TypeRef so the decoded
    // full-name table says System.Decimal.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`3               (coded 0x0D)
    //   row 4: System.Func`2               (coded 0x11)
    //   row 5: System.Decimal              (coded 0x15)
    private static MemoryStream SynthesizeAggregateDecRs(byte tSrc, byte tAcc, byte tRes)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`3"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x03);
        sig.WriteByte(0x04);
        sig.WriteByte(0x13); sig.WriteByte(0x02);                       // return TResult (VAR 2)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   IEnumerable<TSource>
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // TAccumulate (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   Func<TAcc, TSrc, TAcc>
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(0x13); sig.WriteByte(0x01);
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x11); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   Func<TAcc, TResult>
        sig.WriteByte(0x13); sig.WriteByte(0x02);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Aggregate"),
            mdb.GetOrAddBlob(sig.ToArray()));
        // One decimal TypeRef row (row 5) -> coded (5<<2)|1 = 0x15.
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        WriteSpecArg(spec, tSrc, decCoded);
        WriteSpecArg(spec, tAcc, decCoded);
        WriteSpecArg(spec, tRes, decCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    private static void WriteSpecArg(BlobBuilder spec, byte argByte, int decCoded)
    {
        if (argByte == 0x11)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger(decCoded);
        }
        else
        {
            spec.WriteByte(argByte);
        }
    }

    // impl-linq2 (B-1): the REFERENCE-source selector aggregate with a DECIMAL
    // projection (net48 Sum/Min/Max/Average<TSource>(Func<TSource,decimal>)):
    // genCount 1, paramCount 2, the return IS System.Decimal and the Func`2
    // carries (TSource, decimal).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeRefSelectorDecimalProjection(string methodName, byte srcByte = 0x0E)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);      // return System.Decimal
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       // IEnumerable<TSource>
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       // Func<TSource, decimal>
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(srcByte);                                        // TSource (0x0E string)
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // impl-linq2 (B-2): the decimal-bucket PROJECTION aggregate (net48
    // Sum/Min/Max/Average<decimal>(bucket, Func<decimal,TProj>)): genCount 1,
    // paramCount 2, TSource is the named System.Decimal VALUETYPE TypeRef and
    // the Func`2 carries (decimal, TProj) with a raw TProj byte.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeGenericGroupingDecSelProj(string methodName, byte projByte)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(projByte);                                        // return TProj
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);      // IEnumerable<decimal>
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);      // Func<decimal, TProj>
        sig.WriteByte(projByte);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // impl-linq2 (C): ToArray<decimal>() over a DECIMAL GroupBy bucket
    // (net48 Enumerable.ToArray<TSource>(IEnumerable<TSource>) -- Enumerable.cs
    // :942): genCount 1, paramCount 1, TSource is the named System.Decimal
    // VALUETYPE TypeRef, the return is TSource[] (SZARRAY of the valuetype).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Decimal              (coded 0x0D)
    private static MemoryStream SynthesizeGroupingDecToArray()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x01);
        sig.WriteByte(0x1D);                                            // return TSource[]
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);      //   of System.Decimal
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       // IEnumerable<TSource>
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("ToArray"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Decimal-bucket GroupBy selector aggregate (net48
    // Sum<decimal>(IGrouping<,decimal>, Func<decimal,decimal>)): genCount 1,
    // paramCount 2, TSource is the named System.Decimal VALUETYPE TypeRef and
    // the Func`2 carries (decimal, decimal).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeGenericGroupingDecSel(string methodName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x08);                                        // return int32
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);  // IEnumerable<decimal>
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);  // Func<decimal, ...> arg1
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);  // arg2 (the value type)
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // impl-linq2 (B-3): MemberRef signature shapes for the decimal-bucket
    // operator tests. The receiver row layout matches
    // SynthesizeGenericGroupingDecSelProj (Enumerable / IEnumerable`1 /
    // Func`2 / Decimal as rows 1-4, so a decimal element crosses as
    // VALUETYPE(0x11) + coded(0x11)).
    public enum BucketShape
    {
        ElementNoPred,   // decimal <Name>(IEnumerable<decimal>)
        ElementPred,     // decimal <Name>(IEnumerable<decimal>, Func<decimal,bool>)
        ElementAt,       // decimal ElementAt(IEnumerable<decimal>, int)
        RangeNoPred,     // IEnumerable<decimal> <Name>(IEnumerable<decimal>)
        RangePred,       // IEnumerable<decimal> <Name>(IEnumerable<decimal>, Func<decimal,bool>)
        RangeIndex,      // IEnumerable<decimal> <Name>(IEnumerable<decimal>, int)
        TwoSeq,          // IEnumerable<decimal> <Name>(IEnumerable<decimal>, IEnumerable<decimal>)
        SeqDecValue,     // IEnumerable<decimal> DefaultIfEmpty(IEnumerable<decimal>, decimal)
        BoolPred,        // bool <Name>(IEnumerable<decimal>, Func<decimal,bool>)
        IntPred,         // int Count(IEnumerable<decimal>, Func<decimal,bool>)
        TwoSeqBool,      // bool SequenceEqual(IEnumerable<decimal>, IEnumerable<decimal>)
        LongCount,       // long LongCount(IEnumerable<decimal>)
    }

    private static byte[] BucketSig(BucketShape shape) => shape switch
    {
        // decimal <Name>(IEnumerable<decimal>)
        BucketShape.ElementNoPred =>
            new byte[] { 0x10, 0x01, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
        // decimal <Name>(IEnumerable<decimal>, Func<decimal,bool>)
        BucketShape.ElementPred or BucketShape.RangePred or BucketShape.BoolPred
            or BucketShape.IntPred =>
            new byte[] { 0x10, 0x01, 0x02,
                shape == BucketShape.ElementPred ? (byte)0x13
                    : shape == BucketShape.BoolPred ? (byte)0x02 : (byte)0x08,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x0D, 0x02, 0x13, 0x00, 0x02 },
        // decimal ElementAt / IEnumerable<decimal> Take|Skip(.., int)
        BucketShape.ElementAt or BucketShape.RangeIndex =>
            new byte[] { 0x10, 0x01, 0x02,
                shape == BucketShape.ElementAt ? (byte)0x13 : (byte)0x15,
                0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x08 },
        // IEnumerable<decimal> <Name>(IEnumerable<decimal>)
        BucketShape.RangeNoPred =>
            new byte[] { 0x10, 0x01, 0x01, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
        // IEnumerable<decimal> <Name>(IEnumerable<decimal>, IEnumerable<decimal>)
        BucketShape.TwoSeq =>
            new byte[] { 0x10, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
        // IEnumerable<decimal> DefaultIfEmpty(IEnumerable<decimal>, decimal)
        BucketShape.SeqDecValue =>
            new byte[] { 0x10, 0x01, 0x02, 0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x11, 0x11 },
        // bool SequenceEqual(IEnumerable<decimal>, IEnumerable<decimal>)
        BucketShape.TwoSeqBool =>
            new byte[] { 0x10, 0x01, 0x02, 0x02,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
        // long LongCount(IEnumerable<decimal>)
        BucketShape.LongCount =>
            new byte[] { 0x10, 0x01, 0x01, 0x0A,
                0x15, 0x12, 0x09, 0x01, 0x13, 0x00 },
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    // impl-linq2 (B-3): the decimal-BUCKET operator MethodSpec (TSource is the
    // named System.Decimal TypeRef; genCount 2 carries a raw TKey byte for the
    // OrderBy forms -- a 0x11 key crosses as VALUETYPE + the Decimal coded
    // index so IsDecimalGenArg resolves it).
    private static MemoryStream SynthesizeGenericGroupingDecOp(string methodName,
        byte[] memberRefSignature, int genCount = 1, byte keyByte = 0x08)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(memberRefSignature));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(genCount);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        if (genCount >= 2)
        {
            if (keyByte == 0x11)
            {
                spec.WriteByte(0x11);
                spec.WriteCompressedInteger(decCoded);
            }
            else
            {
                spec.WriteByte(keyByte);
            }
        }
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // GroupBy over a REFERENCE-element source (net48
    // GroupBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>) with
    // TSource = System.String, TKey = int32): the reference-source grouping
    // surface (elemKind 1).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    private static MemoryStream SynthesizeGroupByRef(byte tSource, byte tKey)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TKey (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(tSource);
        spec.WriteByte(tKey);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // GroupBy over a DECIMAL source with an ELEMENT selector (net48
    // GroupBy<TSource,TKey,TElement>(IEnumerable<TSource>, Func<TSource,TKey>,
    // Func<TSource,TElement>)): the decimal-source grouping surface with the
    // projected element family.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeGroupByDecSel(byte tKey, byte tElem)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x03);
        sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TKey (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x02);                       //   VAR 2 (TElement)
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        spec.WriteByte(tKey);
        if (tElem == 0x11)
        {
            spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded); // TElement = System.Decimal
        }
        else
        {
            spec.WriteByte(tElem);
        }
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // GroupBy over a DateTime SOURCE (net48 GroupBy<DateTime,int>): the
    // DateTime-source grouping surface (srcKind 3, elemKind 5).
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.DateTime             (coded 0x11)
    private static MemoryStream SynthesizeGroupByDt(byte tKey)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var dtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var dtCoded = (MetadataTokens.GetRowNumber(dtRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TKey (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(dtCoded);     // TSource = System.DateTime
        spec.WriteByte(tKey);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // GroupBy over a Guid SOURCE (net48 GroupBy<Guid,int>): the Guid-source
    // grouping surface (srcKind 4, elemKind 6).
    private static MemoryStream SynthesizeGroupByGuid(byte tKey)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var guidRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));
        var guidCoded = (MetadataTokens.GetRowNumber(guidRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TKey (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(guidCoded);   // TSource = System.Guid
        spec.WriteByte(tKey);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // OrderBy / OrderByDescending over a DECIMAL source (net48
    // OrderBy<decimal,TKey>): the decimal-source keyed sort.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeOrderByDec(string methodName, byte tKey)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TKey (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       //   VAR 1
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        if (tKey == 0x11)
        {
            spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded); // TKey = System.Decimal
        }
        else
        {
            spec.WriteByte(tKey);
        }
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Select over a DECIMAL source with a REFERENCE result (net48
    // Select<decimal,string>): the decimal-source reference-result surface.
    //   row 1: System.Linq.Enumerable      (coded 0x05)
    //   row 2: ...IEnumerable`1            (coded 0x09)
    //   row 3: System.Func`2               (coded 0x0D)
    //   row 4: System.Decimal              (coded 0x11)
    private static MemoryStream SynthesizeSelectDecRef(byte tRes)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x02);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                       // return TResult (VAR 1)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0 (TSource)
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                       //   VAR 0
        sig.WriteByte(tRes);                                            //   TResult
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Select"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x11); spec.WriteCompressedInteger(decCoded);   // TSource = System.Decimal
        spec.WriteByte(tRes);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // p0linq4 (D1): a ONE-argument MethodSpec whose generic argument is a
    // VALUETYPE TypeRef (0x11 + TypeDefOrRef coded index) -- the shape the
    // decoded generic-argument FULL NAME table needs to tell System.Decimal
    // from the other 0x11 value types.
    private static MemoryStream SynthesizeGenericNamedArg(string methodName, byte[] memberRefSignature,
        string ns, string typeName)    {
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

    // BATCH4: the receiver-aware MemberReference overload -- the grouping
    // get_Key dispatch keys on the tracked receiver type's key-family marker.
    private static ResolvedMethod ResolveMemberRefWithReceiver(MemoryStream pe, string memberName, string? receiverType)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, receiverType, null);
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
