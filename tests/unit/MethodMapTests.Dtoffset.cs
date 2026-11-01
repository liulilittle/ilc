using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Dtoffset dispatch tests: pin the DateTimeOffset ToString wiring.
//
// The 0-arg overload has its own runtime entry (culture "G" pattern +
// " zzz"); ToString(string) appends the trailing nullptr provider literal and
// ToString(string, IFormatProvider) passes the provider carrier through
// (ProviderCulture) -- the DateTime cultureprobe precedent. The provider-only
// ToString(IFormatProvider) rides the same format icall with the provider in
// the trailing slot and a null-format literal (ArgOrder [0,-1,1]).
//
// PEs are synthesized with System.Reflection.Metadata's own writer, exactly
// like MethodRefResolverDispatchTests.cs. Signature bytes: 0x0E = string,
// 0x12 = class (followed by its TypeHandle byte 0x05).
public class MethodMapTestsDtoffset
{
    [Fact]
    public void Resolve_Dtoffset_ToString_ZeroArg_MapsToDefaultIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dto" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dtoffset_ToStringFormat_AppendsNullProviderLiteral()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        // The runtime ABI takes a trailing IFormatProvider; the format-only
        // IL call appends the "nullptr" literal (host default zh-CN).
        Assert.Equal(new[] { "deref:dto", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "nullptr" }, resolved.AppendLiterals);
        Assert.False(resolved.ProviderCulture);
    }

    [Fact]
    public void Resolve_Dtoffset_ToStringFormatWithProvider_PassesProviderThrough()
    {
        // ToString(string, IFormatProvider): 2 args, the provider a CLASS
        // (0x12) followed by its TypeHandle byte (0x05). The provider carrier
        // must reach the trailing runtime slot (ProviderCulture) instead of
        // being dropped.
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dto", "", "drop" }, resolved.ParamTypeNames);
        Assert.True(resolved.ProviderCulture);
    }

    [Fact]
    public void Resolve_Dtoffset_ToStringProviderOnly_MapsToFormatIcall()
    {
        // ToString(IFormatProvider): provider-only. The runtime ABI takes
        // (dto, format, provider); the ArgOrder mechanism places the
        // provider in the TRAILING slot with a null-format literal, so the
        // provider never lands in the format slot.
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "ToString",
            new byte[] { 0x00, 0x01, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dto", "", "drop" }, resolved.ParamTypeNames);
        Assert.True(resolved.ProviderCulture);
        Assert.Equal(new[] { 0, -1, 1 }, resolved.ArgOrder);
    }

    [Fact]
    public void Resolve_Object_GetHashCode_DtoffsetReceiver_MapsToDtoffsetHashIcall()
    {
        // B4 (dtoprobe [hc]): callvirt Object::GetHashCode on a tracked
        // DateTimeOffset receiver (Roslyn emits the Object owner for the
        // override) -- net48 UtcDateTime ticks-fold, no offset component.
        var resolved = ResolveMember(Synthesize("System", "Object", "GetHashCode", Sig(0x08)), "GetHashCode",
            receiverType: "System.DateTimeOffset");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "deref:dto" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_IFormattable_ToString_DtoffsetReceiver_MapsToIfaceFormatter()
    {
        // dtoiface: DateTimeOffset really implements IFormattable in net48 (the
        // oracle's typeof(X).GetInterfaces() carries it) and the runtime
        // composite engine has the System.DateTimeOffset arm, so the receiver
        // is ACCEPTED here. It used to be deliberately excluded because the
        // transpiler's `box` arm had no DateTimeOffset helper, which rejected
        // the cast at the BOX instruction before this arm could be reached.
        var resolved = ResolveMember(Synthesize("System", "IFormattable", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString",
            receiverType: "System.DateTimeOffset");

        Assert.Equal("il2cpp::runtime::icall_format_iface_tostring", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.True(resolved.ProviderCulture);
        Assert.Equal(new[] { "", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dtoffset_CompareToObject_MapsToValueVsObjectHelper()
    {
        // DateTimeOffset.CompareTo(object) used to be a transpile-time LOUD
        // ("a boxed DateTimeOffset has no runtime box class"). That reason
        // became false when System_DateTimeOffset_class and the box icalls
        // landed, so the overload is wired: the receiver is the 16-byte value
        // slot (deref:dto), the argument the managed object. Oracle:
        // tests/cases/dtoobjcmp1.
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "CompareTo", Sig(0x08, 0x1C)), "CompareTo");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_compare_object", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dto", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dtoffset_EqualsObject_MapsToValueVsObjectHelper()
    {
        // The Equals(object) counterpart -- net48 answers false for null and
        // for any non-DateTimeOffset object, and compares the UTC instant for
        // a boxed DateTimeOffset. Oracle: tests/cases/dtoobjcmp1.
        var resolved = ResolveMember(Synthesize("System", "DateTimeOffset", "Equals", Sig(0x02, 0x1C)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_dtoffset_equals_object", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dto", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Object_Equals_DtoffsetAddressReceiver_MapsToValueVsObjectHelper()
    {
        // The constrained callvirt shape `dto.Equals(obj)`: Roslyn declares the
        // MemberRef on System.Object, so the receiver HINT is what routes the
        // call. An ADDRESS receiver (the value slot) cannot be passed to the
        // generic icall_boxed_equals_ext2 (two Il2CppObject*).
        var resolved = ResolveMember(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)), "Equals",
            receiverType: "System.DateTimeOffset", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_dtoffset_equals_object", resolved.CppName);
        Assert.Equal(new[] { "deref:dto", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Object_Equals_GenericArm_RequestsReceiverReresolve()
    {
        // Every other receiver keeps the generic boxed-value dispatcher, but it
        // must ASK for the receiver-aware re-resolve -- otherwise the
        // DateTimeOffset/Guid/IPAddress arms (which need the hint) can never
        // fire on a hint-less first pass.
        var resolved = ResolveMember(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_boxed_equals_ext2", resolved.CppName);
        Assert.True(resolved.ReresolveWithReceiver);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        string? receiverType = null, string? arg0Type = null, bool receiverIsAddress = false)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, receiverIsAddress, arg0Type, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
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
