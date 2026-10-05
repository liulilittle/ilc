using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.SafeHandle dispatch tests: prove the System.Runtime.InteropServices
// SafeHandle domain (TryMapSafeHandle in MethodMap.SafeHandle.cs) routes each
// member to the matching safehandle.h entry point. All three members are
// INSTANCE methods, so IsInstance must stay true -- the emitter passes the
// receiver (Il2CppObject*) as arg[0] and the explicit params follow.
//
//   DangerousAddRef(ref bool success) -> il2cpp::runtime::icall_safehandle_dangerous_add_ref
//     (1 explicit param; casts ["", "addr:i"] -- receiver passthrough, the
//     ref bool slot as int32_t*)
//   DangerousRelease()                -> il2cpp::runtime::icall_safehandle_dangerous_release
//   SetHandleAsInvalid()              -> il2cpp::runtime::icall_safehandle_set_handle_as_invalid
//
// Known-but-unwireable shapes are LOUD; out-of-surface members (DangerousGetHandle,
// Dispose, ctors) fall back to the generated-name path (loud link error).
//
// Signature blobs are the raw bytes RawSignature consumes. DangerousAddRef's
// single `ref bool` is BYREF(0x10) + BOOLEAN(0x02) -- a MANUAL blob (the Sig
// helper's count is the byte count, which must be 1 for the single logical
// param); RawSignature consumes the 0x10 marker and reports ps[0] = 0x02.
public class MethodMapSafeHandleTests
{
    //--------------------------------------------------------------------------
    // DangerousAddRef(ref bool success) -- instance, void, one ref bool param
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SafeHandle_DangerousAddRef_MapsToAddRefIcall()
    {
        // header 0x00, count=1, ret=0x01 (void), param BYREF(0x10)+BOOLEAN(0x02)
        var resolved = ResolveMember(
            Synthesize("System.Runtime.InteropServices", "SafeHandle", "DangerousAddRef",
                new byte[] { 0x00, 0x01, 0x01, 0x10, 0x02 }), "DangerousAddRef");

        Assert.Equal("il2cpp::runtime::icall_safehandle_dangerous_add_ref", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "addr:i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SafeHandle_DangerousAddRefWrongShape_ThrowsLoud()
    {
        // A non-bool / different-arity shape has no runtime counterpart -> LOUD.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(
                Synthesize("System.Runtime.InteropServices", "SafeHandle", "DangerousAddRef",
                    new byte[] { 0x00, 0x01, 0x01, 0x10, 0x08 }), "DangerousAddRef"));

        Assert.Contains("System.Runtime.InteropServices.SafeHandle::DangerousAddRef", ex.Message);
    }

    //--------------------------------------------------------------------------
    // DangerousRelease() -- instance, void, no explicit params
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SafeHandle_DangerousRelease_MapsToReleaseIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.InteropServices", "SafeHandle", "DangerousRelease", Sig(0x01)), "DangerousRelease");

        Assert.Equal("il2cpp::runtime::icall_safehandle_dangerous_release", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // SetHandleAsInvalid() -- instance, void, no explicit params
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SafeHandle_SetHandleAsInvalid_MapsToInvalidIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.InteropServices", "SafeHandle", "SetHandleAsInvalid", Sig(0x01)), "SetHandleAsInvalid");

        Assert.Equal("il2cpp::runtime::icall_safehandle_set_handle_as_invalid", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Out-of-surface members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SafeHandle_DangerousGetHandle_FallsBackToGeneratedName()
    {
        // DangerousGetHandle is outside the mapped surface (the runtime has no
        // stored handle field -- the minimal model tracks only the ref count)
        // -- the fallback keeps it loud at C++ link time.
        var resolved = ResolveMember(Synthesize("System.Runtime.InteropServices", "SafeHandle", "DangerousGetHandle", Sig(0x17)), "DangerousGetHandle");

        Assert.StartsWith("System_Runtime_InteropServices_SafeHandle_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Debug.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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
