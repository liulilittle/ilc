using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ReflectionExtensions dispatch tests: prove the ReflectionExtensions
// domain (MethodMap.ReflectionExtensions.cs -> TryMapReflectionExtensions) maps
// GetTypeCode(Type) to the il2cpp_typeext.h primitive il2cpp_type_get_typecode.
public class MethodMapTestsTypeGetTypeCode
{
    //--------------------------------------------------------------------------
    // GetTypeCode(Type) -- static. Wraps il2cpp_type_get_typecode. The single
    // argument is a Type VALUE (the type's full-name string -- the simplified
    // Type model), the exact shape the 1-param icall consumes.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_GetTypeCodeType_MapsToTypeCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "GetTypeCode",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 }), "GetTypeCode"); // (Type) -> int32

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_typecode", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        // Verify the Type object -> klass conversion cast is applied
        Assert.Equal("klass", resolved.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode -- verify the generated call site compiles correctly with the
    // Type object -> klass conversion. The runtime icall takes Il2CppClass* but
    // the IL argument is a Type object (Il2CppObject*), so the cast code "klass"
    // must dereference the klass field.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_GetTypeCodeType_HasKlassCast()
    {
        var resolved = ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "GetTypeCode",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 }), "GetTypeCode"); // (Type) -> int32

        // The cast code must be "klass" (not raw passthrough "") to convert
        // Type object (Il2CppObject*) to Il2CppClass* via klass field deref
        Assert.NotNull(resolved.ParamTypeNames);
        Assert.Single(resolved.ParamTypeNames);
        Assert.Equal("klass", resolved.ParamTypeNames[0]);
        // Verify the icall name is correct
        Assert.Equal("il2cpp::runtime::il2cpp_type_get_typecode", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode with wrong arity (0 params) -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_GetTypeCodeNoParams_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "GetTypeCode",
                new byte[] { 0x00, 0x00, 0x08 }), "GetTypeCode")); // () -> int32

        Assert.Contains("ReflectionExtensions.GetTypeCode", ex.Message);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode with wrong argument type (string instead of Type) -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_GetTypeCodeStringArg_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "GetTypeCode",
                new byte[] { 0x00, 0x01, 0x08, 0x0E }), "GetTypeCode")); // (string) -> int32

        Assert.Contains("ReflectionExtensions.GetTypeCode", ex.Message);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode with 2 params -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_GetTypeCodeTwoParams_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "GetTypeCode",
                new byte[] { 0x00, 0x02, 0x08, 0x12, 0x05, 0x12, 0x05 }), "GetTypeCode")); // (Type, Type) -> int32

        Assert.Contains("ReflectionExtensions.GetTypeCode", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss -- an unwired ReflectionExtensions member falls back to the
    // generated-name path, proving the domain returns null instead of misrouting.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReflectionExtensions_UnmappedMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("Microsoft.Reflection", "ReflectionExtensions", "SomeUnmappedMethod",
            new byte[] { 0x00, 0x00, 0x08 }), "SomeUnmappedMethod"); // ()

        Assert.StartsWith("Microsoft_Reflection_ReflectionExtensions_SomeUnmappedMethod_", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Harness -- same synthetic-PE pipeline as MethodRefResolverDispatchTests.
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? arg0Type = null, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, arg0Type, null);
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
