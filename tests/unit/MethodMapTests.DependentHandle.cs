using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.DependentHandle dispatch tests: prove the
// System.Runtime.DependentHandle domain (MethodMap.DependentHandle.cs ->
// TryMapDependentHandle) maps to the dependhandle.cpp degraded weak-primary
// table icalls (il2cpp_dependhandle.h):
//
//   GetPrimary()                    -> icall_dependenthandle_get_target (inst)
//   SetPrimaryAndSecondary(obj,obj) -> icall_dependenthandle_set_primary_and_secondary
//   Free()                          -> icall_dependenthandle_free (inst)
//   GetPrimaryAndSecondary(out obj) -> LOUD (no emitter "addr:o" cast)
//
// VALUE MODEL: the handle rides in the pointer carrier (intptr_t slot);
// instance receivers follow the IntPtr precedent (ldloca -> "deref:i" reads
// the carrier slot, sign-extended to intptr_t; plain value -> "").
public class MethodMapDependentHandleTests
{
    [Fact]
    public void Resolve_DependentHandle_GetPrimary_AddressReceiver_MapsToIcall()
    {
        // instance object GetPrimary(): ret object (0x12 CLASS + TypeHandle
        // byte 0x05), 0 params.
        var m = ResolveMember(Synthesize("System.Runtime", "DependentHandle", "GetPrimary", Sig(0x12, 0x05)),
            "GetPrimary", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_get_target", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_SetPrimaryAndSecondary_AddressReceiver_MapsToIcall()
    {
        // instance void SetPrimaryAndSecondary(object, object): 2 ref params.
        var m = ResolveMember(Synthesize("System.Runtime", "DependentHandle", "SetPrimaryAndSecondary",
            Sig(0x01, 0x1C, 0x1C)), "SetPrimaryAndSecondary", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_set_primary_and_secondary", m.CppName);
        Assert.True(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "deref:i", "", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_Free_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime", "DependentHandle", "Free", Sig(0x01)),
            "Free", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_free", m.CppName);
        Assert.True(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    // NOTE (2026-09-08): GetPrimaryAndSecondary was LOUD until the "addr:o"
    // emitter cast landed; it now routes to
    // icall_dependenthandle_get_target_and_dependent -- covered by
    // Resolve_DependentHandle_GetPrimaryAndSecondary_MapsToIcall above.
    // The old ThrowsLoudAddrCastGap assertion was removed with the gap.

    [Fact]
    public void Resolve_DependentHandle_CompilerServicesOwner_GetPrimary_MapsToIcall()
    {
        // The corert-era spelling routes identically.
        var m = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DependentHandle", "GetPrimary", Sig(0x12, 0x05)),
            "GetPrimary", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_get_target", m.CppName);
    }

    //--------------------------------------------------------------------------
    // net48 static-extern n* surface (FSI-decoded mscorlib counts -- owner is
    // System.Runtime.CompilerServices.DependentHandle): adapters bridge the
    // void-plus-out-slot shape (IntPtr& rides "addr:i", Object& "addr:o").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DependentHandle_NInitialize_MapsToAdapter()
    {
        var m = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DependentHandle", "nInitialize",
            Sig(0x01, 0x1C, 0x1C, 0x10, 0x18)), "nInitialize");

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_n_init", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal(new[] { "", "", "addr:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_NGetPrimary_MapsToAdapter()
    {
        var m = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DependentHandle", "nGetPrimary",
            Sig(0x01, 0x18, 0x10, 0x1C)), "nGetPrimary");

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_n_get_primary", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "addr:o" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_NGetPrimaryAndSecondary_MapsToAdapter()
    {
        var m = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DependentHandle", "nGetPrimaryAndSecondary",
            Sig(0x01, 0x18, 0x10, 0x1C, 0x10, 0x1C)), "nGetPrimaryAndSecondary");

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_n_get_primary_and_secondary", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal(new[] { "", "addr:o", "addr:o" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_NFree_MapsToFree()
    {
        var m = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DependentHandle", "nFree",
            Sig(0x01, 0x18)), "nFree");

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_free", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DependentHandle_GetPrimaryAndSecondary_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime", "DependentHandle", "GetPrimaryAndSecondary",
            Sig(0x12, 0x05, 0x10, 0x1C)), "GetPrimaryAndSecondary");

        Assert.Equal("il2cpp::runtime::icall_dependenthandle_get_target_and_dependent", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", "addr:o" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (MethodMapTests.IntPtr.cs pipeline)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        bool receiverIsAddress = false)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, receiverIsAddress, null);
    }

    private static byte[] Sig(byte ret, params byte[] ps)
    {
        // Build a REAL MemberRef signature blob (the walking-Sig contract of
        // MethodMapTests.Marshal/Net): CLASS/VALUETYPE returns (0x11/0x12/0x40)
        // carry a trailing TypeHandle byte; params with trailing data
        // (0x11/0x12/0x40/0x1D/0x1F/0x10) consume their element/coded-index
        // byte. The param count derives from the walk, not ps.Length.
        var retBytes = new List<byte> { ret };
        var body = new List<byte>();
        var count = 0;
        var idx = 0;
        if (ret is 0x11 or 0x12 or 0x40) { retBytes.Add(ps[0]); idx = 1; }
        while (idx < ps.Length)
        {
            count++;
            var b = ps[idx++];
            body.Add(b);
            if (b is 0x11 or 0x12 or 0x40 or 0x1D or 0x1F or 0x10) body.Add(ps[idx++]);
        }
        var bytes = new byte[3 + retBytes.Count + body.Count];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        var off = 3;
        if (ret is 0x11 or 0x12 or 0x40) bytes[off++] = ps[0];
        body.CopyTo(bytes, off);
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
