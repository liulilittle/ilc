using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.RuntimeHelpers dispatch tests (MethodMap.RuntimeHelpers.cs ->
// TryMapRuntimeHelpers, reached through the `case
// "System.Runtime.CompilerServices.RuntimeHelpers"` line): the net48 audit
// §3 b-class foldables (GetHashCode/Equals/GetObjectValue) and the LOUD
// JIT-prepare / stack-probe / CER hints.
//
// Same synthetic-PE pipeline as MethodMapTests.Interlocked: one
// MemberReference resolved through the public Resolve entry.
public class MethodMapTestsRuntimeHelpers
{
    //--------------------------------------------------------------------------
    // GetHashCode(object) -> the identity-hash dispatch icall.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GetHashCode_Object_MapsToObjectHashIcall()
    {
        var resolved = ResolveMember(Synthesize("GetHashCode", Sig(0x08, 0x1C)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_object_get_hash_code", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Equals(object, object) -> the reference-equality fold marker (the same
    // fold Object.ReferenceEquals uses -- a C++ pointer comparison).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Equals_TwoObjects_MapsToRefEqMarker()
    {
        var resolved = ResolveMember(Synthesize("Equals", Sig(0x02, 0x1C, 0x1C)), "Equals");

        Assert.Equal(ResolvedMethod.RefEqMarker, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetObjectValue(object) -> the identity marker (the transpiler's box
    // already produced the boxed value -- the wrapper is an identity).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GetObjectValue_Object_MapsToIdentityMarker()
    {
        var resolved = ResolveMember(Synthesize("GetObjectValue", Sig(0x1C, 0x1C)), "GetObjectValue");

        Assert.Equal(ResolvedMethod.IdentityMarker, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // P0 batch A: the net48 audit §3 JIT-prepare / stack-probe / CER hints are
    // now wired to the runtime helpers (probe-measured semantics -- see
    // il2cpp_runtime_helpers.h); the LOUD rejection is gone.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("PrepareDelegate", 0x1C, 1, "il2cpp::runtime::icall_runtime_helpers_prepare_delegate")]
    [InlineData("PrepareContractedDelegate", 0x1C, 1, "il2cpp::runtime::icall_runtime_helpers_prepare_contracted_delegate")]
    [InlineData("EnsureSufficientExecutionStack", 0x01, 0, "il2cpp::runtime::icall_runtime_helpers_ensure_sufficient_execution_stack")]
    [InlineData("ProbeForSufficientStack", 0x01, 0, "il2cpp::runtime::icall_runtime_helpers_probe_for_sufficient_stack")]
    [InlineData("ExecuteCodeWithGuaranteedCleanup", 0x1C, 3, "il2cpp::runtime::icall_runtime_helpers_execute_code_with_guaranteed_cleanup")]
    [InlineData("InitializeArray", 0x1C, 2, "il2cpp::runtime::icall_runtime_helpers_initialize_array")]
    public void Resolve_P0BatchA_MapsToRuntimeHelpersIcall(string memberName, byte ret, int paramCount, string cpp)
    {
        var ps = new byte[paramCount];
        for (var i = 0; i < ps.Length; i++) ps[i] = 0x1C;
        var resolved = ResolveMember(Synthesize(memberName, Sig(ret, ps)), memberName);

        Assert.Equal(cpp, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(paramCount, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // The remaining net48 audit §3 members (type/module-init drivers) still
    // have no runtime counterpart -- LOUD, self-identifying.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("_RunClassConstructor", 0x1C)]
    [InlineData("_RunModuleConstructor", 0x1C)]
    public void Resolve_LoudMembers_ThrowsLoud(string memberName, byte ret)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize(memberName, Sig(ret, 0x1C)), memberName));

        Assert.Contains($"System.Runtime.CompilerServices.RuntimeHelpers::{memberName}", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the Interlocked-test pipeline).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Runtime.CompilerServices"),
            mdb.GetOrAddString("RuntimeHelpers"));
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
