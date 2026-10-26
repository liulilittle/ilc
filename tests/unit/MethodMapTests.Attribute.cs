using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Attribute dispatch tests: prove the BCL domain dispatch routes
// the System.Attribute query surface to TryMapAttribute (MethodMap.Attribute.cs)
// and that the resolved contract matches the runtime registry primitives
// il2cpp_attribute_get_custom_named / il2cpp_attribute_get_single_named
// (src/runtime/include/internal/attribute.h -- the managed-string ABI the
// transpiler emits since D-ATTR-1, 2026-08-11).
//
// Signature blobs are the raw bytes RawSignature consumes; CLASS params
// (0x12) carry a TypeDefOrRef coded index byte after them. The coded index
// MUST point at a real row: MethodSignatureCoder.Decode resolves it
// (MethodSignatureCoder.cs:73) -- the synthesized PE holds exactly one
// TypeReference (the owner), so the TypeRef row-1 coded byte is
// (1<<2)|1 = 0x05. Ret byte 0x1C (object) avoids the 0x11/0x12 ret handle
// consumption.
public class MethodMapAttributeTests
{
    //--------------------------------------------------------------------------
    // Query surface -- GetCustomAttributes(Type) -> Attribute[]
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Attribute_GetCustomAttributes_Type_MapsToRegistryLookup()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "GetCustomAttributes", Sig(0x1C, 1, 0x12, 0x05)),
            "GetCustomAttributes");

        Assert.Equal("il2cpp::runtime::il2cpp_attribute_get_custom_named", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("System.Attribute[]", resolved.ReturnTypeName);
        Assert.True(resolved.ParamIsRef[0]); // the queried element is a GC ref slot
    }

    //--------------------------------------------------------------------------
    // Query surface -- R4 (B3): the attributeType/inherit overloads route to
    // the net48-semantics _ex_named primitives (baseType-chain inheritance
    // walk + attributeType filter + AttributeUsage rules, attribute.cpp).
    // The fixed runtime signature is (element, attributeType, inherit); the
    // arity-2 (element, inherit) shape injects a nullptr attributeType slot
    // (argOrder slot literal -1); (element, attributeType) appends inherit=1;
    // the 3-arg form is identity.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Attribute_GetCustomAttributes_TypeBool_MapsToExNamedInherit()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "GetCustomAttributes",
                Sig(0x1C, 2, 0x12, 0x05, 0x02)), // (Type, bool inherit)
            "GetCustomAttributes");

        Assert.Equal("il2cpp::runtime::il2cpp_attribute_get_custom_ex_named", resolved.CppName);
        Assert.Equal(new[] { 0, -1, 1 }, resolved.ArgOrder); // nullptr attributeType injected
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Attribute_GetCustomAttributes_TypeTypeBool_MapsToExNamedFull()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "GetCustomAttributes",
                Sig(0x1C, 3, 0x12, 0x05, 0x12, 0x05, 0x02)), // (Type, Type, bool)
            "GetCustomAttributes");

        Assert.Equal("il2cpp::runtime::il2cpp_attribute_get_custom_ex_named", resolved.CppName);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true, true, false }, resolved.ParamIsRef);
        Assert.Equal(3, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Attribute_GetCustomAttribute_TypeTypeBool_MapsToSingleExNamed()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "GetCustomAttribute",
                Sig(0x1C, 3, 0x12, 0x05, 0x12, 0x05, 0x02)), // (Type, Type, bool)
            "GetCustomAttribute");

        Assert.Equal("il2cpp::runtime::il2cpp_attribute_get_single_ex_named", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Query surface -- D-ATTR-1 (2026-08-11): the singular overload maps to the
    // managed-string single-match primitive il2cpp_attribute_get_single_named
    // (first-or-AmbiguousMatch semantics are a future runtime refinement) and
    // tracks a single Attribute result
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Attribute_GetCustomAttribute_Type_TracksSingleResult()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "GetCustomAttribute", Sig(0x1C, 1, 0x12, 0x05)),
            "GetCustomAttribute");

        Assert.Equal("il2cpp::runtime::il2cpp_attribute_get_single_named", resolved.CppName);
        Assert.Equal("System.Attribute", resolved.ReturnTypeName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Known instance surface -- LOUD rejection (BCL wiring convention)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Attribute_GetTypeId_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Attribute", "get_TypeId", Sig(0x1C, 0)), "get_TypeId"));

        Assert.Contains("System.Attribute::get_TypeId", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
        //--------------------------------------------------------------------------
    // RF-N6 (NEXT2-DECISION section ② RF-N6): MEMBER-level GetCustomAttributes.
    // The receiver is the MEMBER-NAME string (the simplified member model);
    // the bool inherit form maps to il2cpp_attribute_member_get_custom (the
    // RF-N6 member-attribute registry + typeinfo base-chain scan). The
    // (Type, bool) attributeType-filter form is not wired and fails LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MemberInfo_GetCustomAttributes_Bool_MapsToMemberIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "GetCustomAttributes",
                Sig(0x1C, 1, 0x02)), // (bool inherit) -> object[]
            "GetCustomAttributes");

        Assert.Equal("il2cpp::il2cpp_attribute_member_get_custom", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("System.Object[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_MemberInfo_GetCustomAttributes_TypeBool_MapsToNamedMemberIcall()
    {
        // net48 GetCustomAttributes(Type attributeType, bool inherit): the
        // attributeType-filter form. The receiver is the MEMBER-NAME string
        // (the simplified member model); attributeType rides as a
        // managed-string full name; inherit is the bool.
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "GetCustomAttributes",
                Sig(0x1C, 2, 0x12, 0x05, 0x02)), // (Type, bool)
            "GetCustomAttributes");

        Assert.Equal("il2cpp::il2cpp_attribute_member_get_custom_named", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Object[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Attribute_UnwiredMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Attribute", "Frobnicate", Sig(0x1C, 0)), "Frobnicate");

        Assert.StartsWith("System_Attribute_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as
    // MethodRefResolverDispatchTests: real ECMA-335 assembly whose only
    // MemberReference is the member under test)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT), param COUNT (not raw byte count
    // -- CLASS params 0x12 carry a trailing coded-index byte), return byte,
    // then the raw param bytes RawSignature decodes.
    private static byte[] Sig(byte ret, int paramCount, params byte[] rawParamBytes)
    {
        var bytes = new byte[3 + rawParamBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)paramCount;
        bytes[2] = ret;
        Array.Copy(rawParamBytes, 0, bytes, 3, rawParamBytes.Length);
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
