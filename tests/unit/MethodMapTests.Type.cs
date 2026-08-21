using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Type dispatch tests: prove the Type domain (MethodMap.Type.cs ->
// TryMapType) maps System.Type's wired members to the il2cpp_typeext.h
// primitives, that the fold linkage (ReturnTypeName "System.Type") is set on
// GetTypeFromHandle, that the tracked-argument gates dispatch correctly, and
// that known-unwireable members fail LOUD (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap).
//
// Harness: same as MethodRefResolverDispatchTests.cs -- a synthesized
// ECMA-335 PE whose only MemberReference is the member under test, resolved
// through the same public Resolve entry point the transpiler uses.
// Signature blobs are the raw bytes RawSignature consumes; a 0x11/0x12
// (valuetype/class) byte consumes a trailing TypeDefOrRef coded index. The
// synthesized PE holds exactly ONE TypeReference (the owner, row 1), so the
// coded index is (1<<2)|1 = 0x05 after each such byte (0x06 would decode as
// TypeSpecification and MethodSignatureCoder rejects it).
public class MethodMapTestsType
{
    //--------------------------------------------------------------------------
    // GetTypeFromHandle -- the Type-value producer. Returns the type NAME
    // string (managed ref) and records the result as "System.Type" so later
    // .Name/.GetTypeCode/.Equals/.IsAssignableFrom calls on it resolve
    // (the object.GetType() linkage -- same tracked shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeFromHandle_MapsToTypeExtIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeFromHandle",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x11, 0x05 }), "GetTypeFromHandle"); // RuntimeTypeHandle

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_type_from_handle", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the name string
        Assert.Equal("System.Type", resolved.ReturnTypeName); // fold linkage
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // get_Name -- the Type VALUE is the FULL name string (icall_object_get_type
    // reads klass->name, e.g. "System.Int32"); .NET Type.Name is the SHORT
    // name ("Int32"), so the property maps to the runtime truncation
    // primitive instead of the identity (the old identity printed the full
    // name -- the boxing2/boxdeep GetType output mismatch).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetName_MapsToShortNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "get_Name",
            new byte[] { 0x00, 0x00, 0x0E }), "get_Name");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the short-name string
        Assert.Equal("System.String", resolved.ReturnTypeName); // WriteLine(string) linkage
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount); // the receiver is the implicit this
    }

    //--------------------------------------------------------------------------
    // MemberInfo::get_Name on a tracked GetType() result -- the Roslyn
    // MemberRef parent for `o.GetType().Name` (the boxing2/boxdeep shape).
    // Same truncation mapping (the receiver is the full-name string).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MemberInfo_GetName_OnTrackedType_MapsToShortNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "get_Name",
            new byte[] { 0x00, 0x00, 0x0E }), "get_Name", receiverType: "System.Type");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode -- static overload (the typeext primitive); int32 result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeCodeStatic_MapsToTypeCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeCode",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 }), "GetTypeCode"); // (Type)

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_typecode", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode -- the INSTANCE overload (IConvertible impl) is unwireable:
    // the receiver is the name-string Type value and the runtime has no
    // string->TypeCode path -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeCodeInstance_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "GetTypeCode",
                new byte[] { 0x00, 0x00, 0x08 }), "GetTypeCode")); // ()

        Assert.Contains("System.Type::GetTypeCode", ex.Message);
    }

    //--------------------------------------------------------------------------
    // IsAssignableFrom -- instance: target = this, source = the argument.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_IsAssignableFrom_MapsToAssignableIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "IsAssignableFrom",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05, 0x12, 0x05 }), "IsAssignableFrom"); // (Type)

        Assert.Equal("il2cpp::runtime::il2cpp_type_is_assignable_from", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Equals -- instance type identity, wired only for a TRACKED Type
    // argument (a non-Type argument cannot be passed to the descriptor-
    // taking icall; .NET's "false for non-Type" has no runtime support).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_Equals_OnTrackedTypeArgument_MapsToTypeEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "Equals", arg0Type: "System.Type"); // (object)

        Assert.Equal("il2cpp::runtime::icall_type_equals", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_Equals_UntrackedArgument_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "Equals",
                new byte[] { 0x00, 0x01, 0x02, 0x1C }), "Equals")); // no tracked arg type

        Assert.Contains("System.Type::Equals", ex.Message);
    }

    //--------------------------------------------------------------------------
    // GetProperty/GetMethod/GetField (string) -- M5.5 typeinfo queries: the
    // receiver is the Type VALUE (full-name string); the runtime resolves
    // the typeinfo registry and returns the MEMBER-NAME string on a hit or
    // null (the simplified member model -- the member VALUE is its name
    // string). ReturnTypeName "System.String" keeps WriteLine/Concat
    // linkage on the result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetPropertyString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetProperty"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the member-name string
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetMethodString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetMethod",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetMethod"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_method", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetFieldString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetField",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetField"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_field", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetPropertyTwoParams_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "GetProperty",
                new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x08 }), "GetProperty")); // (string, BindingFlags)

        Assert.Contains("System.Type::GetProperty", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss -- an unwired Type member falls back to the generated-
    // name path (owner_sanitized_name_<hash>), proving the domain returns
    // null instead of misrouting.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_UnmappedMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperties",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetProperties"); // ()

        Assert.StartsWith("System_Type_GetProperties_", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
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
