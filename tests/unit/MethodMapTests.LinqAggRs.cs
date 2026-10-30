using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// D2 (impl-aggrs): resolver-level coverage for the two surfaces b87d225f left
// LOUD:
//   * List<T>.Aggregate(seed, func, resultSelector) -> the List _rs projection
//     matrix (il2cpp_linq_list_aggregate_seed_<acc>_<elem>_rs_<res>);
//   * DefaultIfEmpty(source, defaultValue) -> the family/val kernels for the
//     int32 / wide / narrow / reference ARRAY surfaces and the List surface.
//
// These are the RED->GREEN unit witnesses: on the b87d225f baseline the first
// group throws the "list seed-fold matrix covers the 3-parameter form only"
// LOUD and the second throws the "DefaultIfEmpty(source, defaultValue) ... is
// not wired" LOUD.
public class MethodMapTestsLinqAggRs
{
    //--------------------------------------------------------------------------
    // List<T>.Aggregate(seed, func, resultSelector)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ListAggregate4_Int32_Int32_Int32()
    {
        var r = Resolve(SynthesizeAgg4(0x08, 0x08, 0x08, false, false, false),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i4_i4_rs_i4", r.CppName);
        Assert.Equal(4, r.ParamCount);
        Assert.Equal(new[] { "", "i", "", "" }, r.ParamTypeNames);
        Assert.Null(r.ResultKind);
        Assert.False(r.ReturnsRef);
        Assert.Equal(new[] { "0" }, r.AppendLiterals);
    }

    [Theory]
    [InlineData(0x0A, "_rs_i8", "l")]
    [InlineData(0x0C, "_rs_f", "d")]
    [InlineData(0x0D, "_rs_d", "d")]
    public void Resolve_ListAggregate4_Int32Acc_WideResult(byte resByte, string suffix, string kind)
    {
        var r = Resolve(SynthesizeAgg4(0x08, 0x08, resByte, false, false, false),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i4_i4" + suffix, r.CppName);
        Assert.Equal(4, r.ParamCount);
        Assert.Equal(kind, r.ResultKind);
    }

    [Fact]
    public void Resolve_ListAggregate4_Int32Acc_DecimalResult_UsesResultSlot()
    {
        var r = Resolve(SynthesizeAgg4(0x08, 0x08, 0x11, false, false, true),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i4_i4_rs_dec", r.CppName);
        Assert.Equal(4, r.ParamCount);
        Assert.Equal("dec", r.ResultKind);
        Assert.Equal(new[] { "", "i", "", "" }, r.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ListAggregate4_Int32Acc_RefResult()
    {
        var r = Resolve(SynthesizeAgg4(0x08, 0x08, 0x0E, false, false, false),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i4_i4_rs_ref", r.CppName);
        Assert.True(r.ReturnsRef);
        Assert.Null(r.ResultKind);
    }

    [Fact]
    public void Resolve_ListAggregate4_Int64Diagonal()
    {
        var r = Resolve(SynthesizeAgg4(0x0A, 0x0A, 0x0A, false, false, false),
            "System.Collections.Generic.List`1_long");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i8_i8_rs_i8", r.CppName);
        Assert.Equal(new[] { "", "l", "", "" }, r.ParamTypeNames);
        Assert.Equal("l", r.ResultKind);
        Assert.Equal(new[] { "2" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_ListAggregate4_FloatDiagonal_SeedRidesDoubleStack()
    {
        var r = Resolve(SynthesizeAgg4(0x0C, 0x0C, 0x0D, false, false, false),
            "System.Collections.Generic.List`1_float");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_f_f_rs_d", r.CppName);
        Assert.Equal(new[] { "", "d", "", "" }, r.ParamTypeNames);
        Assert.Equal("d", r.ResultKind);
        Assert.Equal(new[] { "3" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_ListAggregate4_DoubleDiagonal()
    {
        var r = Resolve(SynthesizeAgg4(0x0D, 0x0D, 0x0C, false, false, false),
            "System.Collections.Generic.List`1_double");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_d_d_rs_f", r.CppName);
        Assert.Equal(new[] { "", "d", "", "" }, r.ParamTypeNames);
        Assert.Equal(new[] { "4" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_ListAggregate4_RefDiagonal()
    {
        var r = Resolve(SynthesizeAgg4(0x0E, 0x0E, 0x08, false, false, false),
            "System.Collections.Generic.List`1_string");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_ref_ref_rs_i4", r.CppName);
        Assert.Equal(new[] { "", "", "", "" }, r.ParamTypeNames);
        Assert.False(r.ReturnsRef);
    }

    [Fact]
    public void Resolve_ListAggregate4_DecimalDiagonal_UsesValueSlots()
    {
        var r = Resolve(SynthesizeAgg4(0x11, 0x11, 0x11, true, true, true),
            "System.Collections.Generic.List`1");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_dec_dec_rs_dec", r.CppName);
        Assert.Equal(4, r.ParamCount);
        Assert.Equal(new[] { "", "dec", "", "" }, r.ParamTypeNames);
        Assert.Equal("dec", r.ResultKind);
        Assert.Equal(new[] { "12" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_ListAggregate4_OffDiagonal_Int64Acc_Int32Elem()
    {
        var r = Resolve(SynthesizeAgg4(0x08, 0x0A, 0x0D, false, false, false),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i8_i4_rs_d", r.CppName);
        Assert.Equal(new[] { "", "l", "", "" }, r.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // DefaultIfEmpty(source, defaultValue)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DefaultIfEmpty2_Int32Array()
    {
        var r = Resolve(SynthesizeDie2(0x08, false), null);

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_family_i4", r.CppName);
        Assert.Equal(2, r.ParamCount);
        Assert.Equal(new[] { "a", "i" }, r.ParamTypeNames);
        Assert.Equal(new[] { "7" }, r.AppendLiterals);
        Assert.True(r.ReturnsRef);
    }

    [Theory]
    [InlineData(0x0A, "il2cpp::il2cpp_linq_default_if_empty_family_i8", "l", "8")]
    [InlineData(0x0C, "il2cpp::il2cpp_linq_default_if_empty_family_d", "d", "9")]
    [InlineData(0x0D, "il2cpp::il2cpp_linq_default_if_empty_family_d", "d", "10")]
    public void Resolve_DefaultIfEmpty2_WideArray(byte elemByte, string cpp, string cast, string cell)
    {
        var r = Resolve(SynthesizeDie2(elemByte, false), null);

        Assert.Equal(cpp, r.CppName);
        Assert.Equal(2, r.ParamCount);
        Assert.Equal(new[] { "a", cast }, r.ParamTypeNames);
        Assert.Equal(new[] { cell }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_DefaultIfEmpty2_NarrowArray_WidensToInt32()
    {
        var r = Resolve(SynthesizeDie2(0x05, false), null);

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_family_i4", r.CppName);
        Assert.Equal(new[] { "a", "i" }, r.ParamTypeNames);
        Assert.Equal(new[] { "1" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_DefaultIfEmpty2_RefArray()
    {
        var r = Resolve(SynthesizeDie2(0x0E, false), null);

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_family_ref", r.CppName);
        Assert.Equal(new[] { "a", "" }, r.ParamTypeNames);
        Assert.Equal(new[] { "15" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_DefaultIfEmpty2_DecimalArray_KeepsDecimalKernel()
    {
        var r = Resolve(SynthesizeDie2(0x11, true), null);

        Assert.Equal("il2cpp::il2cpp_linq_default_if_empty_dec", r.CppName);
        Assert.Equal(new[] { "a", "" }, r.ParamTypeNames);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List`1_int", 0x08, false,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_i4", "i", "0")]
    [InlineData("System.Collections.Generic.List`1_long", 0x0A, false,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_i8", "l", "2")]
    [InlineData("System.Collections.Generic.List`1_float", 0x0C, false,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_d", "d", "3")]
    [InlineData("System.Collections.Generic.List`1_double", 0x0D, false,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_d", "d", "4")]
    [InlineData("System.Collections.Generic.List`1_string", 0x0E, false,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_ref", "", "1")]
    [InlineData("System.Collections.Generic.List`1", 0x11, true,
        "il2cpp::il2cpp_linq_list_default_if_empty_val_dec", "", "12")]
    public void Resolve_DefaultIfEmpty2_List(string receiver, byte elemByte, bool decimalName,
        string cpp, string cast, string kindLiteral)
    {
        var r = Resolve(SynthesizeDie2(elemByte, decimalName), receiver);

        Assert.Equal(cpp, r.CppName);
        Assert.Equal(2, r.ParamCount);
        Assert.Equal(new[] { "", cast }, r.ParamTypeNames);
        Assert.Equal(new[] { kindLiteral }, r.AppendLiterals);
        Assert.True(r.ReturnsRef);
    }

    // impl-loudvalstruct (B): the 2-argument DefaultIfEmpty over a BY-VALUE
    // value-struct ARRAY source. The runtime ships one entry per struct family
    // (dt / ts / guid / dtoffset), each taking the default in the struct's own
    // by-value ABI and writing the fresh cell at its REAL width -- a DateTime
    // cell holds the packed ticks+kind image, never a raw struct copy.
    [Theory]
    [InlineData("System.DateTime", "il2cpp::il2cpp_linq_default_if_empty_family_dt", "12")]
    [InlineData("System.TimeSpan", "il2cpp::il2cpp_linq_default_if_empty_family_ts", "13")]
    [InlineData("System.Guid", "il2cpp::il2cpp_linq_default_if_empty_family_guid", "14")]
    [InlineData("System.DateTimeOffset", "il2cpp::il2cpp_linq_default_if_empty_family_dtoffset", "16")]
    public void Resolve_DefaultIfEmpty2_ValueStructArray(string typeName, string cpp, string cell)
    {
        var r = Resolve(SynthesizeDie2Named(typeName), null);

        Assert.Equal(cpp, r.CppName);
        Assert.Equal(2, r.ParamCount);
        Assert.Equal(new[] { "a", "" }, r.ParamTypeNames);
        Assert.Equal(new[] { cell }, r.AppendLiterals);
        Assert.Equal(typeName + "[]", r.ReturnTypeName);
        Assert.True(r.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // impl-loudvalstruct (C): a BY-VALUE VALUE-STRUCT TResult. The projection
    // matrix used to carry only int32/int64/float/double/decimal/reference, so
    // every List.Aggregate(seed, func, resultSelector) whose selector returned a
    // DateTime/TimeSpan/Guid/DateTimeOffset was a transpile-time LOUD even
    // though the selector leaf returns exactly that struct by value.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.DateTime", "dt", "dt")]
    [InlineData("System.TimeSpan", "ts", "ts")]
    [InlineData("System.Guid", "guid", "guidv")]
    [InlineData("System.DateTimeOffset", "dtoffset", "dto")]
    public void Resolve_ListAggregate4_ValueStructResult(string typeName, string suffix, string kind)
    {
        var r = Resolve(SynthesizeAgg4NamedResult(0x08, 0x08, typeName),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_i4_i4_rs_" + suffix, r.CppName);
        Assert.Equal(4, r.ParamCount);
        Assert.Equal(new[] { "", "i", "", "" }, r.ParamTypeNames);
        Assert.Equal(kind, r.ResultKind);
        Assert.Equal(typeName, r.ReturnTypeName);
        Assert.Equal(new[] { "0" }, r.AppendLiterals);
    }

    [Fact]
    public void Resolve_ListAggregate4_DecimalAcc_ValueStructResult()
    {
        var r = Resolve(SynthesizeAgg4NamedResult(0x08, 0x11, "System.DateTime", decAcc: true),
            "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_linq_list_aggregate_seed_dec_i4_rs_dt", r.CppName);
        Assert.Equal(new[] { "", "dec", "", "" }, r.ParamTypeNames);
        Assert.Equal("dt", r.ResultKind);
    }

    [Fact]
    public void Resolve_ListAggregate4_UserStructResult_StaysLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Resolve(SynthesizeAgg4NamedResult(0x08, 0x08, "Cases.Pair"),
                "System.Collections.Generic.List`1_int"));
        Assert.Contains("not a supported result family", ex.Message);
    }

    [Fact]
    public void Resolve_ArrayAggregate4_DecimalAcc_ValueStructResult_StaysLoud()
    {
        // The ARRAY surface has no value-struct result projection; only the
        // LIST matrix gained one. A source PROVEN to be an array keeps the LOUD
        // (the hint-less pass defers to the post-pop re-resolve instead).
        var ex = Assert.Throws<NotSupportedException>(() =>
            Resolve(SynthesizeAgg4NamedResult(0x08, 0x11, "System.DateTime", decAcc: true),
                "System.Int32[]"));
        Assert.Contains("not a supported result family", ex.Message);
    }

    // impl-loudvalstruct (D): a List<T> of a BY-VALUE VALUE STRUCT now has its
    // own list storage kind (13..16) and a snapshot that unboxes into the
    // family's cell, so DefaultIfEmpty(source, value) crosses the default in the
    // struct's own by-value ABI. Before this the family fell into the reference
    // tag and the value landed in an Il2CppObject* slot.
    [Theory]
    [InlineData("System.DateTime", "il2cpp::il2cpp_linq_list_default_if_empty_val_dt", "13")]
    [InlineData("System.TimeSpan", "il2cpp::il2cpp_linq_list_default_if_empty_val_ts", "14")]
    [InlineData("System.Guid", "il2cpp::il2cpp_linq_list_default_if_empty_val_guid", "15")]
    [InlineData("System.DateTimeOffset", "il2cpp::il2cpp_linq_list_default_if_empty_val_dtoffset", "16")]
    public void Resolve_DefaultIfEmpty2_ValueStructList(string typeName, string cpp, string kindLiteral)
    {
        var r = Resolve(SynthesizeDie2Named(typeName), "System.Collections.Generic.List`1_" + typeName);

        Assert.Equal(cpp, r.CppName);
        Assert.Equal(2, r.ParamCount);
        Assert.Equal(new[] { "", "" }, r.ParamTypeNames);
        Assert.Equal(new[] { kindLiteral }, r.AppendLiterals);
        Assert.Equal(typeName + "[]", r.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // impl-loudvalstruct (D): the SOURCE-ONLY aggregates over a List<decimal>.
    //
    // net48 carries CONCRETE non-generic overloads Sum/Min/Max/Average(this
    // IEnumerable<decimal>) -- no MethodSpec, so the element family can only be
    // read from the tracked RECEIVER name. The suffix table in TryMapLinqList
    // only covered "2"/"3"/"4", so a decimal list fell to the int32 entry: a
    // 4-byte slice of every 16-byte cell plus an int32 result assigned into a
    // decimal slot (MEASURED: audit-fix3-d2k, "no match for 'operator='
    // (Il2CppDecimal = int32_t)").
    //
    // Two tracked receiver forms exist and BOTH must resolve to kind 12: the AOT
    // underscore form ("List`1_System.Decimal") and the generic form a METHOD
    // PARAMETER / FIELD carries ("List`1<V:System.Decimal>"). The latter used to
    // fold into the reference tag because 0x11 maps to "ref" in
    // LinqSourceFamily.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Collections.Generic.List`1_System.Decimal")]
    [InlineData("System.Collections.Generic.List`1<V:System.Decimal>")]
    public void Resolve_SourceOnlySum_DecimalList_MapsToDecimalKernel(string receiver)
    {
        var r = ResolveMemberRef(SynthesizeSumDecimal(), receiver);

        Assert.Equal("il2cpp::il2cpp_linq_list_sum_dec", r.CppName);
        Assert.Equal(1, r.ParamCount);
        Assert.Equal(new[] { "" }, r.ParamTypeNames);
        Assert.Equal("dec", r.ResultKind);
        Assert.Equal(new[] { "12" }, r.AppendLiterals);
    }

    // The SIGNATURE element byte is authoritative whenever it names a family:
    // an int32 signature answers the int32 kernel no matter what the receiver
    // name says (the receiver is only consulted for the 0x11 byte, where the
    // signature cannot tell decimal from the other value types).
    [Theory]
    [InlineData("System.Collections.Generic.List`1_int")]
    [InlineData("System.Collections.Generic.List`1<V:System.Int32>")]
    [InlineData("System.Collections.Generic.List`1_double")]
    public void Resolve_SourceOnlySum_Int32Signature_KeepsInt32Kernel(string receiver)
    {
        var r = ResolveMemberRef(SynthesizeSumConcrete(), receiver);
        Assert.Equal("il2cpp::il2cpp_linq_list_sum_i4", r.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers
    //--------------------------------------------------------------------------
    private static ResolvedMethod Resolve(MemoryStream pe, string? receiver)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), receiver, false, receiver, null);
    }

    // Aggregate<TSource,TAccumulate,TResult>(IEnumerable<TSource>, TAccumulate,
    //   Func<TAccumulate,TSource,TAccumulate>, Func<TAccumulate,TResult>)
    // 4 params, 3 generic params. TypeRef rows: 1 Enumerable, 2 IEnumerable`1,
    // 3 Func`3, 4 Func`2, 5 Decimal.
    private static MemoryStream SynthesizeAgg4(byte tSrc, byte tAcc, byte tRes,
        bool decSrc, bool decAcc, bool decRes)
    {
        var (mdb, enumerableRef, decCoded) = NewMetadata();
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x03);
        sig.WriteByte(0x04);
        sig.WriteByte(0x13); sig.WriteByte(0x02);                        // return TResult
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        //   IEnumerable<TSource>
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        // TAccumulate
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        //   Func<TAcc,TSource,TAcc>
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(0x13); sig.WriteByte(0x01);
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x11); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        //   Func<TAcc,TResult>
        sig.WriteByte(0x13); sig.WriteByte(0x02);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Aggregate"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        WriteSpecArg(spec, tSrc, decSrc, decCoded);
        WriteSpecArg(spec, tAcc, decAcc, decCoded);
        WriteSpecArg(spec, tRes, decRes, decCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // DefaultIfEmpty<TSource>(IEnumerable<TSource>, TSource): 2 params,
    // 1 generic param.
    private static MemoryStream SynthesizeDie2(byte tSrc, bool decSrc)
    {
        var (mdb, enumerableRef, decCoded) = NewMetadata();
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        // return TSource
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        //   IEnumerable<TSource>
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        // TSource defaultValue
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("DefaultIfEmpty"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        WriteSpecArg(spec, tSrc, decSrc, decCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // Aggregate<TSource,TAccumulate,TResult> with a NAMED value-struct TResult.
    // The 0x11 byte is shared by every value type, so the decoded full name is
    // what tells the struct families apart (the same rule the DefaultIfEmpty
    // form uses).
    private static MemoryStream SynthesizeAgg4NamedResult(byte tSrc, byte tAcc, string resTypeName,
        bool decAcc = false)
    {
        var (mdb, enumerableRef, decCoded) = NewMetadata();
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        string ns = resTypeName.Contains('.') ? resTypeName[..resTypeName.LastIndexOf('.')] : "System";
        string simple = resTypeName.Contains('.') ? resTypeName[(resTypeName.LastIndexOf('.') + 1)..] : resTypeName;
        var resRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(simple));
        var resCoded = (MetadataTokens.GetRowNumber(resRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x03);
        sig.WriteByte(0x04);
        sig.WriteByte(0x13); sig.WriteByte(0x02);                        // return TResult
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        //   IEnumerable<TSource>
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        // TAccumulate
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x0D); sig.WriteByte(0x03);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        //   Func<TAcc,TSource,TAcc>
        sig.WriteByte(0x13); sig.WriteByte(0x00);
        sig.WriteByte(0x13); sig.WriteByte(0x01);
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x11); sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x01);                        //   Func<TAcc,TResult>
        sig.WriteByte(0x13); sig.WriteByte(0x02);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Aggregate"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(3);
        WriteSpecArg(spec, tSrc, false, decCoded);
        WriteSpecArg(spec, tAcc, decAcc, decCoded);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(resCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // DefaultIfEmpty<TSource>(IEnumerable<TSource>, TSource) with TSource a NAMED
    // value struct (System.DateTime / System.TimeSpan / System.Guid /
    // System.DateTimeOffset). The 0x11 byte is shared by every value type, so the
    // decoded full name is what tells the struct families apart.
    private static MemoryStream SynthesizeDie2Named(string typeName)
    {
        var (mdb, enumerableRef, _) = NewMetadata();
        var vtRef = mdb.AddTypeReference(mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
                new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle)),
            mdb.GetOrAddString("System"), mdb.GetOrAddString(typeName["System.".Length..]));
        var vtCoded = (MetadataTokens.GetRowNumber(vtRef) << 2) | 1;
        var sig = new BlobBuilder();
        sig.WriteByte(0x10);
        sig.WriteByte(0x01);
        sig.WriteByte(0x02);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        // return TSource
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        //   IEnumerable<TSource>
        sig.WriteByte(0x13); sig.WriteByte(0x00);                        // TSource defaultValue
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("DefaultIfEmpty"),
            mdb.GetOrAddBlob(sig.ToArray()));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(1);
        spec.WriteByte(0x11);
        spec.WriteCompressedInteger(vtCoded);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec.ToArray()));
        return Serialize(mdb);
    }

    // A MemberRef (NO MethodSpec) resolve -- the CONCRETE non-generic overload
    // shape net48 uses for Sum/Min/Max/Average(IEnumerable<T>).
    private static ResolvedMethod ResolveMemberRef(MemoryStream pe, string? receiver)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MemberReferenceHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), receiver, false, receiver, null);
    }

    // Enumerable.Sum(IEnumerable<System.Decimal>) -> System.Decimal, with NO
    // MethodSpec (the CONCRETE net48 overload). The signature header is 0x00
    // (DEFAULT calling convention, NOT generic -- 0x10 would claim one generic
    // parameter and desync the decoder). The resolver synthesizes the element
    // byte from this signature's GenericInst argument, so a 0x11 element with no
    // name table is exactly the case where only the tracked RECEIVER name can
    // tell a decimal list from the other value types.
    private static MemoryStream SynthesizeSumDecimal()
    {
        var (mdb, enumerableRef, decCoded) = NewMetadata();
        var sig = new BlobBuilder();
        sig.WriteByte(0x00);                                             // DEFAULT, NOT generic
        sig.WriteByte(0x01);                                             // 1 parameter
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);       // return System.Decimal
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x11); sig.WriteCompressedInteger(decCoded);       //   IEnumerable<System.Decimal>
        mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Sum"),
            mdb.GetOrAddBlob(sig.ToArray()));
        return Serialize(mdb);
    }

    // Enumerable.Sum(IEnumerable<int>) -> int32, with NO MethodSpec (the
    // CONCRETE net48 overload shape). The SIGNATURE element byte is deliberately
    // int32: the point under test is that a DECIMAL receiver name -- which is
    // all the resolver has for these non-generic overloads -- selects the
    // decimal list kernel instead of the int32 one. The real decimal-signature
    // path is covered end-to-end by audit-fix3-d2k.
    private static MemoryStream SynthesizeSumConcrete()
    {
        var (mdb, enumerableRef, _) = NewMetadata();
        var sig = new BlobBuilder();
        sig.WriteByte(0x00);                                             // DEFAULT, NOT generic
        sig.WriteByte(0x01);                                             // 1 parameter
        sig.WriteByte(0x08);                                             // return int32
        sig.WriteByte(0x15); sig.WriteByte(0x12); sig.WriteByte(0x09); sig.WriteByte(0x01);
        sig.WriteByte(0x08);                                             //   IEnumerable<int32>
        mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Sum"),
            mdb.GetOrAddBlob(sig.ToArray()));
        return Serialize(mdb);
    }

    private static (MetadataBuilder, TypeReferenceHandle, int) NewMetadata()
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
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Decimal"));
        var decCoded = (MetadataTokens.GetRowNumber(decRef) << 2) | 1;
        return (mdb, enumerableRef, decCoded);
    }

    private static void WriteSpecArg(BlobBuilder spec, byte argByte, bool decimalName, int decCoded)
    {
        if (argByte == 0x11 && decimalName)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger(decCoded);
        }
        else
        {
            spec.WriteByte(argByte);
        }
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
