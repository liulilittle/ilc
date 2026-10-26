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

    //--------------------------------------------------------------------------
    // D-DEBUG-REST: the net48 category / composite-format / Fail / Assert
    // forms (debugext.h il2cpp_debug_*_category / il2cpp_*_fmt /
    // il2cpp_*_fail_detail / il2cpp_trace_assert*).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debug_WriteLineStringCategory_MapsToCategoryIcall()
    {
        // Debug.WriteLine(string message, string category) -- net48 :217.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x0E, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteLineObjectCategory_MapsToObjCategoryIcall()
    {
        // Debug.WriteLine(object value, string category) -- net48 :228.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x1C, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_obj_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteLineFormatArray_MapsToFmtIcall()
    {
        // Debug.WriteLine(string format, params object[] args) -- net48 :239.
        // The args ride an SZARRAY (0x1D) + element byte (0x05).
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "WriteLine",
                new byte[] { 0x00, 0x02, 0x01, 0x0E, 0x1D, 0x05 }), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_line_fmt", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteStringCategory_MapsToCategoryIcall()
    {
        // Debug.Write(string message, string category) -- net48 :177.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "Write", Sig(0x01, 0x0E, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteObjectCategory_MapsToObjCategoryIcall()
    {
        // Debug.Write(object value, string category) -- net48 :186.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "Write", Sig(0x01, 0x1C, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_write_obj_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_FailStringString_MapsToFailDetailIcall()
    {
        // Debug.Fail(string message, string detailMessage) -- net48 :141.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Debug", "Fail", Sig(0x01, 0x0E, 0x0E)), "Fail");

        Assert.Equal("il2cpp::runtime::il2cpp_debug_fail_detail", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteStringCategory_MapsToCategoryIcall()
    {
        // Trace.Write(string message, string category) -- net48 :522.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x0E, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteObjectCategory_MapsToObjCategoryIcall()
    {
        // Trace.Write(object value, string category) -- net48 :532.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x1C, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_obj_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteLineStringCategory_MapsToCategoryIcall()
    {
        // Trace.WriteLine(string message, string category) -- net48 :507.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "WriteLine", Sig(0x01, 0x0E, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_line_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteLineObjectCategory_MapsToObjCategoryIcall()
    {
        // Trace.WriteLine(object value, string category) -- net48 :497.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "WriteLine", Sig(0x01, 0x1C, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_line_obj_category", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_WriteLineFormatArray_MapsToFmtIcall()
    {
        // Trace.WriteLine(string format, params object[] args) -- net48 :502.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "WriteLine",
                new byte[] { 0x00, 0x02, 0x01, 0x0E, 0x1D, 0x05 }), "WriteLine");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_write_line_fmt", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_FailString_MapsToTraceFailIcall()
    {
        // Trace.Fail(string) -- net48 :384 (debugext.h il2cpp_trace_fail).
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Trace", "Fail", Sig(0x01, 0x0E)), "Fail");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_fail", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_FailStringString_MapsToTraceFailDetailIcall()
    {
        // Trace.Fail(string message, string detailMessage) -- net48 :389.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Fail", Sig(0x01, 0x0E, 0x0E)), "Fail");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_fail_detail", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_AssertBool_MapsToTraceAssertIcall()
    {
        // Trace.Assert(bool) -- net48 :355.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Assert", Sig(0x01, 0x02)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_assert", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "b" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_AssertBoolString_MapsToTraceAssertMsgIcall()
    {
        // Trace.Assert(bool, string) -- net48 :360.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Assert", Sig(0x01, 0x02, 0x0E)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_assert_msg", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_AssertBoolStringString_MapsToTraceAssertDetailIcall()
    {
        // Trace.Assert(bool, string, string) -- net48 :366.
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Assert", Sig(0x01, 0x02, 0x0E, 0x0E)), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_assert_detail", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Trace_AssertBoolStringStringArgs_MapsToTraceAssertFmtIcall()
    {
        // Trace.Assert(bool, string, string detailFormat, object[] args) --
        // net48 :447 (the detail is a composite-format string).
        var resolved = ResolveMember(
            Synthesize("System.Diagnostics", "Trace", "Assert",
                new byte[] { 0x00, 0x04, 0x01, 0x02, 0x0E, 0x0E, 0x1D, 0x05 }), "Assert");

        Assert.Equal("il2cpp::runtime::il2cpp_trace_assert_fmt", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "b", "s", "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debug_WriteLineNoArgs_ThrowsWhitelistLoud()
    {
        // The stub requires a text argument -- a zero-argument write has no
        // net48 counterpart -> LOUD whitelist (self-identifying).
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01)), "WriteLine"));

        Assert.Contains("System.Diagnostics.Debug::WriteLine", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
        Assert.Contains("System_compmod_system_diagnostics_Debug.cs", ex.Message);
    }

    [Fact]
    public void Resolve_Debug_WriteLineInt_ThrowsWhitelistLoud()
    {
        // No numeric formatter in the debug module -> LOUD whitelist.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Debug", "WriteLine", Sig(0x01, 0x08)), "WriteLine"));

        Assert.Contains("System.Diagnostics.Debug::WriteLine", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
    }

    [Fact]
    public void Resolve_Trace_WriteInt_ThrowsWhitelistLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Trace", "Write", Sig(0x01, 0x08)), "Write"));

        Assert.Contains("System.Diagnostics.Trace::Write", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
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
