using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Array/ArraySort W7 expansion (T2): the double/float/long generic
// static surface. Sort<double/float> quicksort with the net48 total order
// (NaN sorts first, NaN == NaN); IndexOf<double> uses Double.Equals
// semantics (finds NaN); BinarySearch<double> is the total-order lower
// bound; LastIndexOf<double/string/long> are reverse scans; Empty<long/
// double> are typed empty factories; Reverse<long> reuses the arrayextra
// int64 1-arg reverser (mapping-only), Reverse<double> is its 8-byte twin.
// Array.Sort<bool> stays LOUD (out of the minimal T2 scope).
public class MethodMapArrayValTests
{
    [Fact]
    public void Resolve_Array_SortDouble_MethodSpec_ResolvesToDirectSortDouble()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::il2cpp_array_sort_double", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_SortFloat_MethodSpec_ResolvesToDirectSortFloat()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0C }, new byte[] { 0x0A, 0x01, 0x0C }));

        Assert.Equal("il2cpp::il2cpp_array_sort_float", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_IndexOfDouble_MethodSpec_ResolvesToDirectIndexOfDouble()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "IndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::il2cpp_array_index_of_double", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_BinarySearchDouble_MethodSpec_ResolvesToDirectBinarySearchDouble()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "BinarySearch",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::il2cpp_array_binary_search_double", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_LastIndexOfDouble_MethodSpec_ResolvesToDirectLastIndexOfDouble()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "LastIndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::il2cpp_array_last_index_of_double", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_LastIndexOfString_MethodSpec_ResolvesToDirectLastIndexOfString()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "LastIndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0E, 0x0E }, new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_array_last_index_of_string", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_LastIndexOfLong_MethodSpec_ResolvesToDirectLastIndexOfLong()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "LastIndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0A, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::il2cpp_array_last_index_of_long", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "a", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_EmptyLong_MethodSpec_ResolvesToEmptyLongIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_long", resolved.CppName);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Array_EmptyDouble_MethodSpec_ResolvesToEmptyDoubleIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_double", resolved.CppName);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Array_ReverseLong_MethodSpec_ResolvesToDirectReverseInt64()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Reverse",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::il2cpp_array_reverse_int64", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ReverseDouble_MethodSpec_ResolvesToDirectReverseDouble()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Reverse",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0D }, new byte[] { 0x0A, 0x01, 0x0D }));

        Assert.Equal("il2cpp::il2cpp_array_reverse_double", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    // Non-generic Array.Reverse(Array) MemberRef shape: the resolver gets
    // genArgs=null (MethodRefResolver MemberReference path) and must route
    // by the tracked argument type -- the historical int32-only contract
    // throws RequireInt32Array for typed arrays (g4arrval RED).
    [Fact]
    public void Resolve_Array_ReverseLong_MemberRef_ResolvesToDirectReverseInt64()
    {
        var resolved = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", "System.Int64[]");

        Assert.Equal("il2cpp::il2cpp_array_reverse_int64", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ReverseDouble_MemberRef_ResolvesToDirectReverseDouble()
    {
        var resolved = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", "System.Double[]");

        Assert.Equal("il2cpp::il2cpp_array_reverse_double", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ReverseString_MemberRef_ResolvesToDirectReverseString()
    {
        var resolved = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", "System.String[]");

        Assert.Equal("il2cpp::il2cpp_array_reverse_string", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_ReverseObject_MemberRef_ResolvesToDirectReverseObject()
    {
        var resolved = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", "System.Object[]");

        Assert.Equal("il2cpp::il2cpp_array_reverse_object", resolved.CppName);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_Reverse_MemberRef_UnknownArg_KeepsInt32Contract()
    {
        var untyped = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", null);
        var int32 = ResolveMemberWithArg0(
            Synthesize("System", "Array", "Reverse", new byte[] { 0x00, 0x01, 0x01, 0x1C }),
            "Reverse", "System.Int32[]");

        Assert.Equal("il2cpp::runtime::icall_array_reverse", untyped.CppName);
        Assert.Equal("il2cpp::runtime::icall_array_reverse", int32.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMemberWithArg0(MemoryStream pe, string memberName, string? arg0Type)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        foreach (var h in md.MemberReferences)
        {
            if (md.GetString(md.GetMemberReference(h).Name) == memberName)
                return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(h),
                    Array.Empty<TypeDef>(), null, false, arg0Type);
        }
        throw new InvalidOperationException("member not found: " + memberName);
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
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

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
