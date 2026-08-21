using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Debugger dispatch tests: prove the System.Diagnostics.Debugger
// domain (TryMapDebugger in MethodMap.Debugger.cs) routes each member to the
// matching debuggerext.h entry point. All members are STATIC, so IsInstance
// must stay false -- the generated-name fallback classifies static members
// INSTANCE and StackFold pops this from an empty stack (the standard
// unwired-gap failure shape).
//
//   get_IsAttached   -> il2cpp::runtime::il2cpp_debugger_is_attached (bool)
//   IsLogging()      -> il2cpp::runtime::il2cpp_debugger_is_logging  (bool)
//   Break()          -> il2cpp::runtime::il2cpp_debugger_break        (void)
//   Log(int, string, string) -> il2cpp::runtime::il2cpp_debugger_log
//                               (casts ["i","s","s"] -- level int32, the two
//                               strings as Il2CppString*)
//
// Known-but-unwireable shapes are LOUD (NotSupportedException, wrapped as
// "<owner>::<name>: ..." by BclResolve); out-of-surface members fall back to
// the generated-name path (loud link error).
//
// Signature blobs are the raw bytes RawSignature consumes (0x08=i4, 0x0E=
// string, 0x01=void, 0x02=bool).
public class MethodMapDebuggerTests
{
    //--------------------------------------------------------------------------
    // get_IsAttached -- static, returns a bool value, no params
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debugger_IsAttached_MapsToAttachedIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debugger", "get_IsAttached", Sig(0x02)), "get_IsAttached");

        Assert.Equal("il2cpp::runtime::il2cpp_debugger_is_attached", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IsLogging -- static, returns a bool value, no params
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debugger_IsLogging_MapsToLoggingIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debugger", "IsLogging", Sig(0x02)), "IsLogging");

        Assert.Equal("il2cpp::runtime::il2cpp_debugger_is_logging", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Break -- static, void, no params -> no-op
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debugger_Break_MapsToBreakIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debugger", "Break", Sig(0x01)), "Break");

        Assert.Equal("il2cpp::runtime::il2cpp_debugger_break", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Log(int, string, string) -> log entry (static, void, i/s/s casts)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debugger_Log_MapsToLogIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debugger", "Log", Sig(0x01, 0x08, 0x0E, 0x0E)), "Log");

        Assert.Equal("il2cpp::runtime::il2cpp_debugger_log", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "i", "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Debugger_LogWrongArity_ThrowsLoud()
    {
        // The stub takes exactly (int level, string category, string message) --
        // any other shape is a wiring error -> LOUD.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember(Synthesize("System.Diagnostics", "Debugger", "Log", Sig(0x01, 0x08, 0x0E)), "Log"));

        Assert.Contains("System.Diagnostics.Debugger::Log", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Out-of-surface members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Debugger_Launch_FallsBackToGeneratedName()
    {
        // Launch() is outside the mapped surface (a debugger can never be
        // launched in ILC) -- the fallback keeps it loud at C++ link time.
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Debugger", "Launch", Sig(0x02)), "Launch");

        Assert.StartsWith("System_Diagnostics_Debugger_", resolved.CppName);
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
