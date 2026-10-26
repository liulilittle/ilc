using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Debug dispatch tests: prove the System.Diagnostics.Debug /
// System.Diagnostics.Trace domains (TryMapDebug in MethodMap.Debug.cs) route
// each member to the matching debugext.h entry point. All members are
// STATIC, so IsInstance must stay false -- the generated-name fallback
// classifies static members INSTANCE and StackFold pops this from an empty
// stack (the standard unwired-gap failure shape).
//
//   Debug.Assert(bool)          -> il2cpp::runtime::il2cpp_debug_assert
//   Debug.Assert(bool, string)  -> il2cpp::runtime::il2cpp_debug_assert
//   Debug.WriteLine(string)     -> il2cpp::runtime::il2cpp_debug_write_line
//   Debug.Fail(string)          -> il2cpp::runtime::il2cpp_debug_fail
//   Trace.Write(string)         -> il2cpp::runtime::il2cpp_trace_write
//   Trace.WriteLine(string)     -> il2cpp::runtime::il2cpp_debug_write_line
//
// RUNTIME ABI GAP: the stubs take const char* but the emitter's "s" cast
// produces (Il2CppString*) -- the generated C++ fails loudly at compile time
// (documented in the MethodMap.Debug.cs header; closing task = runtime-side).
//
// Known-but-unwireable overloads are LOUD (NotSupportedException, wrapped as
// "<owner>::<name>: ..." by BclResolve):
//   * Debug.Assert(bool, string, string) -- no detail-message parameter
//   * Debug.WriteLine() / Fail()         -- stubs require a text argument
//   * WriteLine(object | numeric)        -- no boxed/numeric formatter
//
// Signature blobs are the raw bytes RawSignature consumes (0x08=i4, 0x0E=
// string, 0x01=void, 0x02=bool).
public class MethodMapDebugTests
{
    //--------------------------------------------------------------------------
    // Debug.Assert -- bool / (bool, string) -> assert entry (static, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debug_AssertBool_MapsToAssertIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "Assert", Sig(0x01, 0x02)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_assert", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "b" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_AssertBoolString_MapsToAssertIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "Assert", Sig(0x01, 0x02, 0x0E)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_assert", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_AssertBoolStringString_MapsToAssertIcallDroppingDetail()
    {
        // Debug.Assert(bool, string, string): the net48 detail-message
        // overload. The runtime stub takes (condition, message) only -- the
        // detail message is dropped at emission ("drop" cast); the stub never
        // renders a detail message, so the drop is observable-exact.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "Assert", Sig(0x01, 0x02, 0x0E, 0x0E)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_assert", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_AssertBoolStringStringArgs_MapsToAssertIcallDroppingAll()
    {
        // Debug.Assert(bool, string, string, object[]): the detail message +
        // format-args overload. The runtime stub takes (condition, message)
        // only -- both the detail message and the args array are dropped.
        // The object[] param is an SZARRAY (0x1D) + element byte (0x05) --
        // hand-built blob.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "Assert",
                new byte[] { 0x00, 0x04, 0x01, 0x02, 0x0E, 0x0E, 0x1D, 0x05 }), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_assert", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s", "drop", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Debug.WriteLine(string) -> write_line entry (static, void, "s" cast)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debug_WriteLineString_MapsToWriteLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteLineObject_MapsToWriteLineObjIcall()
    {
        // Debug.WriteLine(object): the object's ToString text
        // (il2cpp_debug_write_line_obj -- net48 semantics).
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x1C)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_obj", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteLineNoArgs_ThrowsLoud()
    {
        // The stub requires a text argument -- a zero-argument write has no
        // runtime counterpart -> LOUD.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01)), "WriteLine"));

        Assert.Contains("System.Diagnostics.Debug::WriteLine", ex.Message);
    }

    [Fact]
    public void Resolve_Debug_WriteLineInt_ThrowsLoud()
    {
        // No numeric formatter in the debug module -> LOUD.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x08)), "WriteLine"));

        Assert.Contains("System.Diagnostics.Debug::WriteLine", ex.Message);
    }

    [Fact]
    public void Resolve_Debug_WriteLineObject_MapsToWriteLineObjIcall_Existing()
    {
        // Debug.WriteLine(object): now wired to the object-ToString icall
        // (il2cpp_debug_write_line_obj -- icall_object_to_string).
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x1C)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_obj", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Debug.Fail(string) -> fail entry (static, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debug_FailString_MapsToFailIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "Fail", Sig(0x01, 0x0E)), "Fail");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_fail", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Trace.Write / Trace.WriteLine(string) -> trace / write_line entries
    // (System.Diagnostics.Trace routes to TryMapDebug via the stacked
    // dispatch case in MethodRefResolver.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Trace_WriteString_MapsToTraceWriteIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteLineString_MapsToWriteLineIcall()
    {
        // debugext.h: Trace.WriteLine IS il2cpp_debug_write_line.
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "WriteLine", Sig(0x01, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteObject_MapsToTraceWriteObjIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x1C)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_obj", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteLineObject_MapsToWriteLineObjIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "WriteLine", Sig(0x01, 0x1C)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_obj", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteInt_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x08)), "Write"));

        Assert.Contains("System.Diagnostics.Trace::Write", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Out-of-surface members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debug_AutoFlushGetter_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debug", "get_AutoFlush", Sig(0x02)), "get_AutoFlush");

        Assert.StartsWith("System_Diagnostics_Debug_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    [Fact]
    public void Resolve_Trace_Fail_FallsBackToGeneratedName()
    {
        // Trace.Fail is outside the mapped surface (Debug.Fail is mapped) --
        // the fallback keeps it loud at C++ link time.
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "Fail", Sig(0x01, 0x0E)), "Fail");

        Assert.StartsWith("System_Diagnostics_Trace_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.VersionUri.cs)
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
