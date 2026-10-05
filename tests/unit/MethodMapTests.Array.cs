using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Array get_Rank dispatch tests -- the System.Array rank wiring
// (MethodMap.Array.cs -> icall_array_get_rank in il2cpp_array_ext.h). The
// receiver (an Il2CppObject*) passes through uncast, exactly like the
// get_Length mapping it mirrors (MethodMap.Array.cs:117).
//
// net48 semantics: Array.get_Rank returns the number of dimensions -- a
// multi-dimensional array returns its rank, a one-dimensional array returns
// 1. The RUNTIME decides the value from the array's rank field (the
// allocator stamps 1 in the one-dimensional scope; multi-dimensional
// creation is future work). The RESOLVER cannot distinguish array ranks at
// the MemberRef level (the call signature is identical), so every rank maps
// to the same icall -- the per-rank behavior is a runtime-test concern
// (test_arrayext.cpp).
public class MethodMapArrayTests
{
    //--------------------------------------------------------------------------
    // get_Rank -- the icall mapping (the audit gap §5 #4).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_get_Rank_MapsToRankIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Array", "get_Rank", Sig(0x08)), "get_Rank");

        Assert.Equal("il2cpp::runtime::icall_array_get_rank", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Empty(resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // get_Length -- the neighboring mapping must stay stable (the mirror the
    // get_Rank case copies; a regression here red-flags a mis-merge).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_get_Length_MapsToLengthHelper()
    {
        var resolved = ResolveMember(Synthesize("System", "Array", "get_Length", Sig(0x08)), "get_Length");

        Assert.Equal("il2cpp::il2cpp_array_get_length", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // FindIndex (arrayext2, 2026-08-09, rtfind-b23) -- the DELEGATE form is
    // now wired: icall_array_find_index (il2cpp_array_ext2.h:145) consumes the
    // Predicate<T> DELEGATE (single-cast static methodPtr ABI). The int32
    // instantiation maps to it; any other generic argument stays LOUD. The
    // 4-arg .NET shape's IL order is (array, start, count, match) while the
    // icall order is (arr, predicate, start, count) -- the emitted C++ needs
    // an arg-reorder the emitter lacks (see MethodMap.Array.cs comment).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_FindIndexInt32_MethodSpec_MapsToDelegateIcall()
    {
        // FindIndex<int> -- the MethodSpec carries the int32 (0x08) generic
        // argument; the resolver's MethodSpec path dispatches on genArgs[0].
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "FindIndex",
            new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x08, 0x08, 0x08 },
            new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_find_index", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "a", "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_FindIndexNonInt32_MethodSpec_ThrowsLoud()
    {
        // FindIndex<string> -- only int32 has the delegate icall; other
        // generic arguments are LOUD (never silently drop the delegate).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeGeneric("System", "Array", "FindIndex",
                new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x08, 0x08, 0x08 },
                new byte[] { 0x0A, 0x01, 0x0E })));

        Assert.Contains("System.Array::FindIndex", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ARRFUNCS (2026-08-09, wir9-b23) -- the remaining arrayext2 delegate
    // icalls (il2cpp_array_ext2.h:154-191): Find/Exists/TrueForAll/ConvertAll
    // consume a single-cast STATIC delegate (Predicate<int>/Converter<int,int>,
    // the same ABI as FindIndex -- the array "a" cast + "" delegate slot), and
    // Resize takes the byref array slot address + int newSize. All are 2-arg
    // static shapes in IL order == icall order (NO IcallArgOrder entry).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Find", "il2cpp::runtime::icall_array_find_i4")]
    [InlineData("Exists", "il2cpp::runtime::icall_array_exists_i4")]
    [InlineData("TrueForAll", "il2cpp::runtime::icall_array_true_for_all_i4")]
    public void Resolve_Array_DelegatePredicate_MethodSpec_MapsToIcall(string member, string icall)
    {
        // <member><int>(int32[], Predicate<int>) -- MemberRef: DEFAULT header,
        // 2 params, i4 ret, SZARRAY(int32) + int32 params; MethodSpec: int32.
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", member,
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x08, 0x08 },
            new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ConvertAllIntInt_MethodSpec_MapsToConverterIcall()
    {
        // int32[] ConvertAll<int,int>(int32[], Converter<int,int>) -- MemberRef:
        // SZARRAY(int32) ret + 2 params; MethodSpec: TWO generic args (int,int).
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "ConvertAll",
            new byte[] { 0x00, 0x02, 0x1D, 0x08, 0x1D, 0x08, 0x08 },
            new byte[] { 0x0A, 0x02, 0x08, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_convert_all_i4_i4", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);   // the result is a NEW int32[]
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ResizeInt_MethodSpec_MapsToResizeIcall()
    {
        // void Resize<int>(ref int32[], int) -- MemberRef: void ret, BYREF
        // SZARRAY(int32) + int params (ReadParamByte maps BYREF to the element
        // byte 0x1D); MethodSpec: int32.
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Resize",
            new byte[] { 0x00, 0x02, 0x01, 0x10, 0x1D, 0x08, 0x08 },
            new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_resize_i4", resolved.CppName);
        Assert.False(resolved.ReturnsValue);   // void
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        // "addr:a" -- the byref array slot: the ldloca address of the caller's
        // Il2CppObject* local must reach the Il2CppArray** param exactly
        // (CodeGenerator EmitIcallCallArgs "addr:a" arm).
        Assert.Equal(new[] { "addr:a", "i" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("Find")]
    [InlineData("Exists")]
    [InlineData("TrueForAll")]
    [InlineData("Resize")]
    public void Resolve_Array_NonInt32_MethodSpec_ThrowsLoud(string member)
    {
        // Find<string> etc. -- only int32 (0x08) has the runtime delegate/
        // resize icall; any other generic argument stays LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeGeneric("System", "Array", member,
                new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x08, 0x08 },
                new byte[] { 0x0A, 0x01, 0x0E })));

        Assert.Contains("System.Array::" + member, ex.Message);
    }

    //--------------------------------------------------------------------------
    // ICALL-C (2026-08-07): the three rebuilt array icalls (arrayext.cpp,
    // icallc2 report §3) -- get_LongLength / GetLowerBound / Initialize. All
    // instance, receiver passes through uncast; get_LongLength rides the "l"
    // result kind (int64_t).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_get_LongLength_MapsToLongLengthIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Array", "get_LongLength", Sig(0x0A)), "get_LongLength");

        Assert.Equal("il2cpp::runtime::icall_array_get_long_length", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Array_GetLowerBound_MapsToLowerBoundIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Array", "GetLowerBound", Sig(0x08, 0x08)), "GetLowerBound");

        Assert.Equal("il2cpp::runtime::icall_array_get_lower_bound", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_Initialize_MapsToInitializeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Array", "Initialize", Sig(0x01)), "Initialize");

        Assert.Equal("il2cpp::runtime::icall_array_initialize", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the MethodRefResolverDispatchTests
    // pipeline: one MemberReference, resolved via the public Resolve entry).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    // Signature blob: header 0x00 (DEFAULT), param count, return byte, then one
    // byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
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

    // The synthesized PE holds exactly one MethodSpec row (row 1) whose
    // underlying MemberReference is the System.Array member (the ArraySort
    // test pattern).
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, null, null);
    }

    // System.Array::<member> MemberReference + one MethodSpecification with
    // the GENERICINST blob (0x0A header, 1 generic argument byte).
    private static MemoryStream SynthesizeGeneric(string ns, string typeName, string memberName,
        byte[] memberRefSignature, byte[] genericSignature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
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
