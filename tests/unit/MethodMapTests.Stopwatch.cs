using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Stopwatch dispatch tests: prove the BCL domain dispatch routes
// the System.Diagnostics.Stopwatch surface to TryMapStopwatch
// (MethodMap.Stopwatch.cs) and lock the static/instance classification
// contract against the runtime surface (stopwatch.h -- 5 static primitives +
// the object-state icall family).
//
// Coverage contract (M6 object closure -- swcase #35):
//   * StartNew MUST resolve STATIC (the fallback mangled path would classify
//     the non-get_/.ctor name as INSTANCE and "pop this" from an empty stack
//     -- the standard unwired-gap shape). The mapping folds it to
//     icall_stopwatch_start_new: the runtime allocates a Stopwatch object
//     through System_Stopwatch_class, starts it and returns it; the result is
//     tracked as "System.Diagnostics.Stopwatch" (ReturnTypeName) so later
//     instance-call resolution sees the receiver type.
//   * GetTimestamp / get_Frequency -- the other static primitives.
//   * The instance members (Start/Stop/Restart/Reset/get_IsRunning/
//     get_ElapsedTicks/get_ElapsedMilliseconds/get_ElapsedMicroseconds/
//     get_Elapsed) map to the object-state icalls (Il2CppObject* self):
//     void mutators, bool IsRunning, int64 tick/ms/us getters, and the
//     TimeSpan-returning get_Elapsed ("ts" ResultKind + "System.TimeSpan"
//     ReturnTypeName for chained member resolution).
//   * .ctor maps to icall_stopwatch_ctor_default (zero state).
//   * Unwired names fall back to the generated-name path (mangled).
//
// Same synthetic-PE pipeline as MethodRefResolverDispatchTests: a real
// ECMA-335 assembly whose only MemberReference is the member under test,
// resolved through the transpiler's public Resolve entry point. Signature
// blobs use ret 0x1C (object) -- no coded-index consumption (the 0x12 ret
// would require a TypeDefOrRef row; see MethodMapTests.Attribute.cs).
public class MethodMapStopwatchTests
{
    //--------------------------------------------------------------------------
    // Static surface -- StartNew (object factory) + the primitives
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stopwatch_StartNew_IsStatic_AndReturnsStopwatchObject()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", "StartNew", Sig(0x1C, 0)), "StartNew");

        Assert.Equal("il2cpp::runtime::icall_stopwatch_start_new", resolved.CppName);
        Assert.False(resolved.IsInstance); // MUST be static: the factory takes no receiver
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef); // the result is a managed object reference
        Assert.Equal(0, resolved.ParamCount);
        Assert.Null(resolved.ResultKind); // reference result (Il2CppObject* slot)
        Assert.Equal("System.Diagnostics.Stopwatch", resolved.ReturnTypeName); // receiver-type tracking
    }

    [Fact]
    public void Resolve_Stopwatch_Ctor_MapsToDefaultCtorIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", ".ctor", Sig(0x01, 0)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stopwatch_ctor_default", resolved.CppName);
        Assert.True(resolved.IsInstance); // mutates the allocated receiver
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stopwatch_GetTimestamp_IsStatic_MapsToTimestampIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", "GetTimestamp", Sig(0x0A, 0)), "GetTimestamp");

        Assert.Equal("il2cpp::runtime::il2cpp_stopwatch_get_timestamp", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stopwatch_GetFrequency_IsStatic_MapsToFrequencyIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", "get_Frequency", Sig(0x0A, 0)), "get_Frequency");

        Assert.Equal("il2cpp::runtime::il2cpp_stopwatch_get_frequency", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Instance surface -- the object-state icall family (Il2CppObject* self)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Start", "il2cpp::runtime::icall_stopwatch_start", false, null)]
    [InlineData("Stop", "il2cpp::runtime::icall_stopwatch_stop", false, null)]
    [InlineData("Restart", "il2cpp::runtime::icall_stopwatch_restart", false, null)]
    [InlineData("Reset", "il2cpp::runtime::icall_stopwatch_reset", false, null)]
    [InlineData("get_IsRunning", "il2cpp::runtime::icall_stopwatch_get_is_running", true, null)]
    [InlineData("get_ElapsedTicks", "il2cpp::runtime::icall_stopwatch_get_elapsed_ticks", true, "l")]
    [InlineData("get_ElapsedMilliseconds", "il2cpp::runtime::icall_stopwatch_get_elapsed_milliseconds", true, "l")]
    [InlineData("get_ElapsedMicroseconds", "il2cpp::runtime::icall_stopwatch_get_elapsed_microseconds", true, "l")]
    public void Resolve_Stopwatch_InstanceMembers_MapToObjectIcalls(string member, string cpp, bool returnsValue, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", member, Sig(0x1C, 0)), member);

        Assert.Equal(cpp, resolved.CppName);
        Assert.True(resolved.IsInstance); // receives the Stopwatch object as 'self'
        Assert.Equal(returnsValue, resolved.ReturnsValue);
        Assert.Equal(resultKind, resolved.ResultKind);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stopwatch_GetElapsed_ReturnsTimeSpanForChainedMembers()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", "get_Elapsed", Sig(0x0A, 0)), "get_Elapsed");

        Assert.Equal("il2cpp::runtime::icall_stopwatch_get_elapsed", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal("ts", resolved.ResultKind); // Il2CppTimeSpan result slot
        Assert.Equal("System.TimeSpan", resolved.ReturnTypeName); // chains (sw.Elapsed.TotalMilliseconds) resolve
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stopwatch_UnwiredMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Diagnostics", "Stopwatch", "Frobnicate", Sig(0x1C, 0)), "Frobnicate");

        Assert.StartsWith("System_Diagnostics_Stopwatch_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as
    // MethodRefResolverDispatchTests)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT), param COUNT, return byte, then
    // the raw param bytes RawSignature decodes.
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
