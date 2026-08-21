using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Object.cs domain-ext dispatch tests (ObjectExt):
//   * Object.ToString on TRACKED boxed-value receivers must route to the
//     value-semantics formatting icall (icall_object_to_string) instead of
//     the generated-name path -- boxed values print their VALUE, not a
//     klass name. Covers the numeric box family incl. the Byte/SByte/Int16/
//     UInt16 receivers the emitter can box (CodeGenerator box_ switch).
//   * Object.Equals must distinguish the receiver type: a TRACKED
//     System.Delegate receiver routes to the STRUCTURAL delegate comparison
//     (icall_delegate_equals -- same target+method per leaf), while every
//     other receiver keeps the boxed-value/reference-equality semantics
//     (icall_boxed_equals_ext2). This mirrors .NET, where Delegate.Equals
//     overrides Object.Equals and is value-structured.
//
// PEs are synthesized with System.Reflection.Metadata's writer (same
// pipeline the transpiler reads). Signature blobs via Sig(ret, params...)
// -- the raw bytes RawSignature consumes (0x02=bool, 0x1C=object, 0x0E=string).
public class MethodMapTestsObjectExt
{
    //--------------------------------------------------------------------------
    // Object.ToString -- boxed-value receivers -> value formatting icall
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_ToString_TrackedBoxedInt32_MapsToValueFormattingIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Object", "ToString", Sig(0x0E)), "ToString",
            receiverType: "System.Int32");

        Assert.Equal("il2cpp::runtime::icall_object_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef); // string result
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Object_ToString_TrackedBoxedByte_MapsToValueFormattingIcall()
    {
        // Byte/SByte/Int16/UInt16 are emittable box types (CodeGenerator box_
        // switch) -- their ToString must route to the value-semantics icall
        // (value formatting is the runtime task; wiring maps the icall now).
        foreach (var t in new[] { "System.Byte", "System.SByte", "System.Int16", "System.UInt16" })
        {
            var resolved = ResolveMember(Synthesize("System", "Object", "ToString", Sig(0x0E)), "ToString",
                receiverType: t);

            Assert.Equal("il2cpp::runtime::icall_object_to_string", resolved.CppName);
        }
    }

    [Fact]
    public void Resolve_Object_ToString_TrackedString_IsIdentityViaValueSemantics()
    {
        var resolved = ResolveMember(Synthesize("System", "Object", "ToString", Sig(0x0E)), "ToString",
            receiverType: "System.String");

        Assert.Equal("il2cpp::runtime::icall_object_to_string", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_Object_ToString_UntrackedReceiver_FallsThroughToGeneratedName()
    {
        // No tracked receiver: the boxed-value dispatch must NOT fire -- the
        // resolution falls through to the generated-name path (the loud
        // vtable failure downstream is the deliberate guard).
        var resolved = ResolveMember(Synthesize("System", "Object", "ToString", Sig(0x0E)), "ToString");

        Assert.StartsWith("System_Object_ToString_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Object.Equals -- receiver-type-distinguished dispatch
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_Equals_TrackedDelegateReceiver_MapsToStructuralDelegateEquals()
    {
        // callvirt Object::Equals(object) with a TRACKED System.Delegate
        // receiver: .NET Delegate.Equals is STRUCTURAL (same target+method),
        // so the delegate-ext icall must win over boxed/reference equality.
        var resolved = ResolveMember(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)), "Equals",
            receiverType: "System.Delegate");

        Assert.Equal("il2cpp::runtime::icall_delegate_equals", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Object_Equals_TrackedBoxedReceiver_KeepsBoxedValueEquality()
    {
        // A NON-delegate tracked receiver keeps the boxed-value/reference
        // equality semantics (regression guard: the delegate route must be
        // receiver-type-specific).
        var resolved = ResolveMember(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)), "Equals",
            receiverType: "System.Int32");

        Assert.Equal("il2cpp::runtime::icall_boxed_equals_ext2", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Object_Equals_UntrackedReceiver_KeepsBoxedValueEquality()
    {
        // Untracked receiver: default Object.Equals semantics unchanged.
        var resolved = ResolveMember(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_boxed_equals_ext2", resolved.CppName);
        Assert.True(resolved.IsInstance);
    }

    //--------------------------------------------------------------------------
    // MemberInfo::get_Name -- receiver-type-distinguished dispatch
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MemberInfo_GetName_StringReceiver_FoldsToIdentity()
    {
        // M7 delegate2: f.Method.Name -- Delegate.get_Method returns the bound
        // method's NAME STRING (icall_delegate_get_method_name), so the
        // follow-up MemberInfo::get_Name on a "System.String" receiver is the
        // IdentityMarker (the string IS the name -- CultureInfo precedent).
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "get_Name", Sig(0x0E)), "get_Name",
            receiverType: "System.String");

        Assert.Equal(ResolvedMethod.IdentityMarker, resolved.CppName);
        Assert.True(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_MemberInfo_GetName_TypeReceiver_MapsToShortNameIcall()
    {
        // o.GetType().Name -- the GetType icall returns the type's FULL name
        // string; .NET Type.Name is the SHORT name, so the getter maps to the
        // runtime truncation primitive (not identity).
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "get_Name", Sig(0x0E)), "get_Name",
            receiverType: "System.Type");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

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
