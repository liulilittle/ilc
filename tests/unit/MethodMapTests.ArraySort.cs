using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ArraySort dispatch tests -- the System.Array direct-call surface
// (MethodMap.ArraySort.cs -> the arraysort.h typed API). Coverage contract:
//   * CopyTo(Array, int) -- instance CopyTo has no TryMapArray case, so the
//     ?? fallback (MethodRefResolver.cs "System.Array" dispatch line) routes
//     it to il2cpp_array_copy_to (instance: this = the source array).
//   * Sort/IndexOf/BinarySearch generic calls go through TryMapArray FIRST.
//     The int32/string icall variants are CLAIMED by MethodMap.Array.cs (the
//     icall layer, il2cpp_array_ext.h/_ext2.h) -- the tests below pin that
//     resolution so the claim is explicit.
//   * The int64 (and string-IndexOf / uint32-sort) variants YIELD in
//     MethodMap.Array.cs (return null for the 1/2-arg forms) and resolve to
//     the arraysort.h direct calls here -- the tests below pin the direct
//     calls (il2cpp_array_sort_uint32/int64, il2cpp_array_index_of_int64/
//     _string, il2cpp_array_binary_search_int64).
//   * Sort<T>(T[], Comparison<T>) is WIRED (cg8-b23): the Comparison`1
//     delegate construction (CG8 ctor rewrite, CodeGenerator.cs) makes the
//     delegate form reachable -- int32 arrays map to
//     il2cpp_array_sort_int32_comparison, ref-element (string/object/class)
//     arrays to il2cpp_array_sort_ref_comparison (arraysort.h). The tests
//     below pin both mappings.
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (same pipeline as MethodRefResolverDispatchTests): one MemberReference,
// optionally one MethodSpecification carrying the GENERICINST blob
// (0x0A header, 1 argument byte). Raw signature bytes: 0x08=i4, 0x09=u4,
// 0x0A=i8, 0x0E=string, 0x1C=object, 0x1D=SZARRAY, 0x12=class (+ coded
// TypeDefOrRef index byte, discarded by RawSignature).
public class MethodMapArraySortTests
{
    //--------------------------------------------------------------------------
    // CopyTo(Array, int) -- the reachable direct call: instance method, this
    // = the source array (cast slot 0), dst array slot, int32 index slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_CopyTo_MapsToDirectCall()
    {
        var resolved = ResolveMember(SynthesizeMember("System", "Array", "CopyTo",
            new byte[] { 0x00, 0x02, 0x01, 0x12, 0x05, 0x08 }), "CopyTo");

        Assert.Equal("il2cpp::il2cpp_array_copy_to", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "a", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false, false, false }, resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // Sort/IndexOf/BinarySearch -- the generic MethodSpec surface. int32/
    // string resolve through the MethodMap.Array.cs icall claims (pinned).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_SortInt32_MethodSpec_ResolvesViaArrayDomainIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x08 }, new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_sort", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_SortString_MethodSpec_ResolvesViaArrayDomainIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0E }, new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::runtime::icall_array_sort_strings", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_IndexOfInt32_MethodSpec_ResolvesViaArrayDomainIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "IndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x08, 0x08 }, new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_index_of_int32", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_BinarySearchInt32_MethodSpec_ResolvesViaArrayDomainIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "BinarySearch",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x08, 0x08 }, new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_binary_search_int32", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Array.Empty<T> -- the generic STATIC zero-length-array factory (the
    // MethodSpec shape Roslyn emits for no-arg params calls like
    // TrimStart()/TrimEnd() in optimize builds). Signature: `static !!0[]
    // Empty<T>()` = [0x00 header, 0x00 param count, 0x1D SZARRAY + 0x03/0x08/
    // 0x0E element]; the generic argument byte comes from the MethodSpec
    // GENERICINST blob, and the per-type runtime icall carries the
    // elemSize/isRefElement constants the emitter cannot inject.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_EmptyChar_MethodSpec_ResolvesToEmptyCharIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x03 }, new byte[] { 0x0A, 0x01, 0x03 }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_char", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_EmptyInt32_MethodSpec_ResolvesToEmptyInt32Icall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x08 }, new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_int32", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_EmptyString_MethodSpec_ResolvesToEmptyStringIcall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x0E }, new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_string", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_EmptyInt64_MethodSpec_ResolvesToEmptyLongIcall()
    {
        // W7 (T2): Array.Empty<long> is wired (icall_array_empty_long) -- the
        // former LOUD gap is now a mapping (spec-change/test-sync rule).
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
            new byte[] { 0x00, 0x00, 0x1D, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::runtime::icall_array_empty_long", resolved.CppName);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Array_EmptyBoolean_MethodSpec_ThrowsLoud_Gap()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Empty",
                new byte[] { 0x00, 0x00, 0x1D, 0x02 }, new byte[] { 0x0A, 0x01, 0x02 })));

        Assert.Contains("System.Array::Empty", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Direct-call pins -- the int64/uint32/string element variants YIELD in
    // MethodMap.Array.cs (return null for the 1/2-arg forms) and resolve to
    // the arraysort.h direct calls below. Object elements (no typed sort)
    // and the Comparison<T> overloads stay LOUD -- the last test pins that.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_SortInt64_MethodSpec_ResolvesToInt64DirectCall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::il2cpp_array_sort_int64", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_SortUInt32_MethodSpec_ResolvesToUInt32DirectCall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x09 }, new byte[] { 0x0A, 0x01, 0x09 }));

        Assert.Equal("il2cpp::il2cpp_array_sort_uint32", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_IndexOfString_MethodSpec_ResolvesToStringDirectCall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "IndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0E, 0x0E }, new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::il2cpp_array_index_of_string", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_IndexOfInt64_MethodSpec_ResolvesToInt64DirectCall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "IndexOf",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0A, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::il2cpp_array_index_of_int64", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_BinarySearchInt64_MethodSpec_ResolvesToInt64DirectCall()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "BinarySearch",
            new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x0A, 0x0A }, new byte[] { 0x0A, 0x01, 0x0A }));

        Assert.Equal("il2cpp::il2cpp_array_binary_search_int64", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_SortInt64_ThreeArgForm_ThrowsLoud()
    {
        // Array.Sort<long>(long[], int, int): the 3-arg (index, length) form
        // has no direct call here (the 2-param sort has no range overload) --
        // it must stay LOUD, never fall to the generated-name path.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
                new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x0A, 0x08, 0x08 }, new byte[] { 0x0A, 0x01, 0x0A })));

        Assert.Contains("System.Array::Sort", ex.Message);
    }

    [Fact]
    public void Resolve_Array_SortInt32WithComparison_MethodSpec_MapsToInt32Comparison()
    {
        // cg8-b23 (icomparedeep): Array.Sort<int>(int32[], Comparison<int>)
        // now resolves to the runtime DELEGATE sort il2cpp_array_sort_int32_comparison
        // (arraysort.h) -- the Comparison`1 delegate construction (CG8 ctor
        // rewrite) makes the delegate form reachable. Array "a" cast + "" delegate
        // slot (the icomparer-sort precedent).
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x02, 0x01, 0x1D, 0x08, 0x12, 0x05 }, new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::il2cpp_array_sort_int32_comparison", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Array_SortObjectWithComparison_MethodSpec_MapsToRefComparison()
    {
        // Sort<object>(object[], Comparison<object>): params = object[] +
        // Comparison<T> (0x12 CLASS + coded index), generic argument 0x1C --
        // now the ref-element delegate sort il2cpp_array_sort_ref_comparison.
        var resolved = ResolveMethodSpec(SynthesizeGeneric("System", "Array", "Sort",
            new byte[] { 0x00, 0x02, 0x01, 0x1D, 0x1C, 0x12, 0x05 }, new byte[] { 0x0A, 0x01, 0x1C }));

        Assert.Equal("il2cpp::il2cpp_array_sort_ref_comparison", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // The synthesized PE holds exactly one MethodSpec row (row 1) whose
    // underlying MemberReference is the System.Array member.
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

    private static MemoryStream SynthesizeMember(string ns, string typeName, string memberName, byte[] signature)
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

    // System.Array::<member> MemberReference (row 1, coded index 0x05 for the
    // 0x12 CLASS params) + one MethodSpecification with the GENERICINST blob
    // (0x0A header, 1 generic argument byte).
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
