using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Interlocked dispatch tests (System.Threading.Interlocked wiring --
// il2cpp_interlocked.h, 10 icalls: increment/decrement/exchange/add/
// compare_exchange x int32/int64 + pointer forms).
//
// Coverage contract: every overload family in il2cpp_interlocked.h gets at
// least one dispatch assertion. RawSignature's ReadParamByte consumes the
// BYREF (0x10) element byte (ps carries the ELEMENT type), so:
//   * Increment/Decrement (single byref param): ps[0] is the element byte
//     (0x08=i4 -> int32 icall, 0x0A=i8 -> int64 icall) -- both families
//     wire exactly.
//   * Add/Exchange/CompareExchange (2+ params): ps[0] carries the byref
//     element, ps[1] (and ps[2]) the VALUE-param type -- both families are
//     wired exactly.
//   * Exchange(ref object, object) / CompareExchange(ref object, object,
//     object) -- the pointer icalls need a void** location cast the emitter
//     has no code for ("p" does not exist in the ParamTypeNames table):
//     LOUD-rejected.
//
// Same synthetic-PE pipeline as MethodMapTests.MathExtra: a real ECMA-335
// assembly whose only MemberReference is the member under test, resolved
// through the transpiler's public Resolve entry point.
public class MethodMapTestsInterlocked
{
    //--------------------------------------------------------------------------
    // Increment (single byref param -- int32/int64 indistinguishable in ps;
    // both map to the int32 icall, documented gap)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Increment_RefInt32_MapsToIncrementIcall()
    {
        var resolved = ResolveMember(Synthesize("Increment", Sig(1, 0x08, 0x10, 0x08)), "Increment");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_increment", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Null(resolved.ResultKind); // int32 result
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames); // address passes through
    }

    [Fact]
    public void Resolve_Increment_RefInt64_MapsToIncrement64Icall()
    {
        // G5: ReadParamByte now consumes the BYREF element byte, so ps[0]
        // carries the element type (0x0A = i8) for the ref-long overload --
        // it resolves to the int64 icall exactly (no longer ambiguous).
        var resolved = ResolveMember(Synthesize("Increment", Sig(1, 0x0A, 0x10, 0x0A)), "Increment");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_increment64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // Decrement (same BYREF-element discrimination as Increment)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decrement_RefInt32_MapsToDecrementIcall()
    {
        var resolved = ResolveMember(Synthesize("Decrement", Sig(1, 0x08, 0x10, 0x08)), "Decrement");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_decrement", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Decrement_RefInt64_MapsToDecrement64Icall()
    {
        var resolved = ResolveMember(Synthesize("Decrement", Sig(1, 0x0A, 0x10, 0x0A)), "Decrement");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_decrement64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // Add -- ps[1] carries the value type: int32 vs int64 wired exactly
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Add_RefInt32_MapsToAddIcall()
    {
        var resolved = ResolveMember(Synthesize("Add", Sig(2, 0x08, 0x10, 0x08, 0x08)), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_add", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Add_RefInt64_MapsToAdd64Icall()
    {
        var resolved = ResolveMember(Synthesize("Add", Sig(2, 0x0A, 0x10, 0x0A, 0x0A)), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_add64", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("l", resolved.ResultKind); // int64 result
        Assert.Equal(new[] { "", "l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Exchange -- same ps[1] discrimination; object form LOUD
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Exchange_RefInt32_MapsToExchangeIcall()
    {
        var resolved = ResolveMember(Synthesize("Exchange", Sig(2, 0x08, 0x10, 0x08, 0x08)), "Exchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_exchange", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Exchange_RefInt64_MapsToExchange64Icall()
    {
        var resolved = ResolveMember(Synthesize("Exchange", Sig(2, 0x0A, 0x10, 0x0A, 0x0A)), "Exchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_exchange64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(new[] { "", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Exchange_RefObject_MapsToPointerExchange()
    {
        // batch 149: the "pobj" emitter cast (void** location) wires the
        // ref-object form to the pointer atomic.
        var resolved = ResolveMember(Synthesize("Exchange", Sig(2, 0x1C, 0x10, 0x1C, 0x1C)), "Exchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_exchange_pointer", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "pobj", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // CompareExchange -- three params; int32/int64 exact, object form LOUD
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_CompareExchange_RefInt32_MapsToCompareExchangeIcall()
    {
        var resolved = ResolveMember(Synthesize("CompareExchange", Sig(3, 0x08, 0x10, 0x08, 0x08, 0x08)), "CompareExchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_compare_exchange", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(new[] { "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_CompareExchange_RefInt64_MapsToCompareExchange64Icall()
    {
        var resolved = ResolveMember(Synthesize("CompareExchange", Sig(3, 0x0A, 0x10, 0x0A, 0x0A, 0x0A)), "CompareExchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_compare_exchange64", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(new[] { "", "l", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_CompareExchange_RefObject_MapsToPointerCompareExchange()
    {
        // batch 149: the ref-object form maps to the pointer atomic.
        var resolved = ResolveMember(Synthesize("CompareExchange", Sig(3, 0x1C, 0x10, 0x1C, 0x1C, 0x1C)), "CompareExchange");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_compare_exchange_pointer", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "pobj", "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Unmapped members fall through to the generated-name path (null)
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // net48 audit §3 (2026-08-07): the floating-point atomics have no runtime
    // icall -- LOUD, self-identifying (they must never fall through to the
    // generated-name undefined symbol). SpeculationBarrier, by contrast, is
    // WIRED (ICALL-C): the runtime ships the empty
    // il2cpp_interlocked_speculation_barrier (a hardware speculation fence --
    // semantically faithful because every ILC atomic is seq_cst).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Exchange_RefDouble_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Exchange", Sig(2, 0x08, 0x10, 0x0D, 0x0D)), "Exchange"));

        Assert.Contains("System.Threading.Interlocked::Exchange", ex.Message);
        Assert.Contains("floating-point", ex.Message);
    }

    [Fact]
    public void Resolve_CompareExchange_RefDouble_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("CompareExchange", Sig(3, 0x08, 0x10, 0x0D, 0x0D, 0x0D)), "CompareExchange"));

        Assert.Contains("System.Threading.Interlocked::CompareExchange", ex.Message);
        Assert.Contains("floating-point", ex.Message);
    }

    [Fact]
    public void Resolve_SpeculationBarrier_MapsToBarrierIcall()
    {
        var resolved = ResolveMember(Synthesize("SpeculationBarrier", Sig(0, 0x01)), "SpeculationBarrier");

        Assert.Equal("il2cpp::runtime::il2cpp_interlocked_speculation_barrier", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Null(resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_UnknownMember_FallsThroughToGeneratedName()
    {
        // No map branch matches a 1-arity CompareExchange -- falls through to
        // the generated-name path (a by-value CompareExchange(int,int,int)
        // blob would now MATCH the ref-int branch: ps carries the BYREF
        // element byte, indistinguishable from a by-value int param -- the
        // emitter distinguishes them by the ldloca expression shape instead).
        var resolved = ResolveMember(Synthesize("CompareExchange", Sig(1, 0x08, 0x08)), "CompareExchange");

        Assert.StartsWith("System_Threading_Interlocked_CompareExchange_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.MathExtra)
    //--------------------------------------------------------------------------

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), MANAGED param count,
    // return byte, then the raw parameter bytes (a BYREF param is 0x10 + its
    // element byte -- matching real net48 blobs: Add(ref long, long) =
    // 00 02 0A 10 0A 0A is count=2 with 3 raw param bytes). count must be
    // explicit: RawSignature's ReadParamByte drops the BYREF element byte
    // (ps=[0x10, 0x0A] for Add64) while MethodSignatureCoder.Decode consumes
    // it -- a count derived from the byte count would mis-decode.
    private static byte[] Sig(int count, byte ret, params byte[] paramBytes)
    {
        var bytes = new byte[3 + paramBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        Array.Copy(paramBytes, 0, bytes, 3, paramBytes.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Threading"), mdb.GetOrAddString("Interlocked"));
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
