using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Environment/Random domain dispatch tests (MethodMap.Environment.cs +
// MethodMap.EnvironmentExtra.cs, MethodMap.Random.cs + MethodMap.RandomExtra.cs).
//
// Coverage contract (2026-08-03, difference-set analysis): the runtime
// surfaces (il2cpp_environment.h icalls incl. the M7 env-audit ExitCode pair,
// il2cpp_random.h 7/7 icalls) are FULLY wired by the base files -- so the Extra files carry no new
// icall mappings and act as LOUD rejection gates for known-but-unwireable
// net48 members (Environment.Exit / get_Version / ..., Random.Sample).
// These tests pin: (a) the base-file mappings route through the BCL domain
// dispatch, (b) the Extra gates reject loudly with the "{owner}::{name}"
// prefix (see BclResolve's wrap), (c) unknown members fall back to the
// generated-name path.
//
// Same synthetic-PE technique as MethodRefResolverDispatchTests.cs: a real
// ECMA-335 assembly whose only MemberReference is the member under test,
// read through the transpiler's own PEReader/MetadataReader pipeline.
public class MethodMapTestsEnvRandom
{
    //--------------------------------------------------------------------------
    // Environment domain -- base file positives (MethodMap.Environment.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Environment_GetNewLine_MapsToNewLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "get_NewLine", Sig(0x0E)), "get_NewLine");

        Assert.Equal("il2cpp::runtime::icall_environment_get_newline", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Environment_GetFolderPath_MapsToSpecialFolderIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "GetFolderPath", Sig(0x0E, 0x08)), "GetFolderPath");

        Assert.Equal("il2cpp::runtime::icall_environment_get_special_folder", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Environment_GetCommandLine_MapsToCommandLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "GetCommandLine", Sig(0x0E)), "GetCommandLine");

        Assert.Equal("il2cpp::runtime::icall_environment_get_command_line", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Environment_GetEnvironmentVariable_MapsToEnvVarIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "GetEnvironmentVariable", Sig(0x0E, 0x0E)),
            "GetEnvironmentVariable");

        Assert.Equal("il2cpp::runtime::icall_environment_get_environment_variable", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // M7 env (2026-08-08): ExitCode get/set -- the env audit added
    // icall_environment_get_exit_code / icall_environment_set_exit_code
    // (il2cpp_environment.h, process-lifetime int32). get returns the stored
    // int; set stores and returns a status the .NET void setter discards.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Environment_GetExitCode_MapsToExitCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "get_ExitCode", Sig(0x08)), "get_ExitCode");

        Assert.Equal("il2cpp::runtime::icall_environment_get_exit_code", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Environment_SetExitCode_MapsToExitCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "set_ExitCode", Sig(0x01, 0x08)), "set_ExitCode");

        Assert.Equal("il2cpp::runtime::icall_environment_set_exit_code", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Random domain -- base file positives (MethodMap.Random.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Random_NextDouble_MapsToDoubleIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Random", "NextDouble", Sig(0x0D)), "NextDouble");

        Assert.Equal("il2cpp::runtime::icall_random_next_double", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Random_NextBytes_MapsToBytesIcall()
    {
        // void NextBytes(byte[]): 0x1D SZARRAY consumes its element byte
        // (0x08), so the count byte is 1 for 2 param bytes
        var resolved = ResolveMember(Synthesize("System", "Random", "NextBytes",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x08 }), "NextBytes");

        Assert.Equal("il2cpp::runtime::icall_random_next_bytes", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Environment Extra gate -- known-unwireable members reject LOUD
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Environment_Exit_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Environment", "Exit", Sig(0x01, 0x08)), "Exit"));

        Assert.Contains("System.Environment::Exit", ex.Message);
    }

    [Fact]
    public void Resolve_Environment_GetVersion_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Environment", "get_Version", Sig(0x1C)), "get_Version"));

        Assert.Contains("System.Environment::get_Version", ex.Message);
    }

    [Fact]
    public void Resolve_Environment_GetCurrentManagedThreadId_MapsToThreadIdIcall()
    {
        // P6 wiring: Environment.CurrentManagedThreadId resolves to the
        // runtime thread-id icall (int32 OS thread id -- value-returning,
        // zero params, no ref return).
        var resolved = ResolveMember(Synthesize("System", "Environment", "get_CurrentManagedThreadId", Sig(0x08)),
            "get_CurrentManagedThreadId");

        Assert.Equal("il2cpp::runtime::icall_environment_get_current_managed_thread_id", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Environment_GetEnvironmentVariable_WithTarget_ThrowsLoud()
    {
        // Arity-guard pin (2026-08-22 surface sweep): the
        // EnvironmentVariableTarget overload GetEnvironmentVariable(string,
        // EnvironmentVariableTarget) (2 explicit args, target enum = 0x08)
        // has no runtime icall -- MethodMap.Environment.cs returns null for
        // the uncovered arity and the shape lands in
        // TryMapEnvironmentExtra's LOUD gate (BclResolve prefixes
        // "{owner}::{name}") instead of silently dropping the target
        // argument on the 1-param icall.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Environment", "GetEnvironmentVariable", Sig(0x0E, 0x0E, 0x08)),
                "GetEnvironmentVariable"));

        Assert.Contains("System.Environment::GetEnvironmentVariable", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Random Extra gate -- the protected sample hook rejects LOUD
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Random_Sample_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Random", "Sample", Sig(0x0D)), "Sample"));

        Assert.Contains("System.Random::Sample", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Unknown members -- both domains fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Environment_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "Frobnicate", Sig(0x08, 0x08)), "Frobnicate");

        Assert.StartsWith("System_Environment_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Random_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Random", "Frobnicate", Sig(0x08, 0x08)), "Frobnicate");

        Assert.StartsWith("System_Random_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same technique as
    // MethodRefResolverDispatchTests.cs)
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
