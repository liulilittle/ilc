using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.MathExtra dispatch tests (System.Math wiring leftovers).
//
// Coverage contract: every member in the MathExtra difference set gets at
// least one dispatch assertion. The difference set -- reconciled against
// il2cpp_math.h + il2cpp_math_ext.h (both headers read for this task) is:
//   * Clamp  -- icall_math_clamp_i (il2cpp_math.h:57, implemented in
//               math.cpp:88): the ONLY icall in either header that
//               MethodMap.Math.cs does not map; wired here in MathExtra.
//   * DivRem -- the corert Math.DivModInt family wired result-first via the
//               M7 ref/out support (icall_math_divrem_i/_l): the net48 PUBLIC
//               int/long forms now RESOLVE (was a LOUD reject before the
//               icall + addr:* wiring landed).
//   * Acosh/Asinh/Atanh/Cbrt/FusedMultiplyAdd/Log2 -- the .NET Core
//               gMathFuncs super-functions (net48 member scan: MISSING all
//               eight, so DORMANT mappings): single/3-arg double icalls.
//   * SinCos -- P5: the netcore (double,double) ValueTuple shape RESOLVES to
//               icall_math_sincos via TupleReturnFields (tuple-return wiring).
//   * ModF -- STILL LOUD: the ValueTuple/out-param public shape has no Math
//               mapping (rejected by name in TryMapMathExtra).
//   * BigMul -- the only net48 System.Math member MethodMap.Math.cs does not
//               map at all; no runtime icall exists in either header:
//               LOUD-rejected.
//   * Truncate/Cosh/Sinh/Tanh/IEEERemainder/Log(double,double) -- the
//               mathext icalls the task card expected here; they were
//               already wired in MethodMap.Math.cs (lines 43, 52-59), so the
//               assertions below prove those resolve through the Math chain.
//
// Same synthetic-PE pipeline as MethodRefResolverDispatchTests: a real
// ECMA-335 assembly whose only MemberReference is the member under test,
// resolved through the transpiler's public Resolve entry point.
public class MethodMapTestsMathExtra
{
    //--------------------------------------------------------------------------
    // MathExtra difference set -- Clamp (real mapping, via TryMapMathExtra)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_Clamp_MapsToClampIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Clamp", Sig(0x08, 0x08, 0x08, 0x08)), "Clamp");

        Assert.Equal("il2cpp::runtime::icall_math_clamp_i", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // DivRem(int, int, out int) -- the net48 PUBLIC form now resolves to the
    // result-first icall_math_divrem_i via the M7 addr:* wiring (the old
    // blanket LOUD reject is gone). The BYREF out param decodes to its element
    // byte (0x10 + 0x08 -> ps[2] == 0x08); the last arg cast is "addr:i" so the
    // ldloca remainder slot passes through as a typed int32_t*.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_DivRemInt_MapsToDivRemI()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "DivRem",
            new byte[] { 0x00, 0x03, 0x08, 0x08, 0x08, 0x10, 0x08 }), "DivRem");

        Assert.Equal("il2cpp::runtime::icall_math_divrem_i", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "i", "i", "addr:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // DivRem(long, long, out long) -- the long form resolves to the int64
    // result-first icall with the "l"/"addr:l" arg casts + "l" result kind.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_DivRemLong_MapsToDivRemL()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "DivRem",
            new byte[] { 0x00, 0x03, 0x0A, 0x0A, 0x0A, 0x10, 0x0A }), "DivRem");

        Assert.Equal("il2cpp::runtime::icall_math_divrem_l", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "l", "l", "addr:l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // MathExtra difference set -- BigMul (D-ME2-1, 2026-08-12): now WIRED.
    // Math.BigMul(int,int) -> icall_math_bigmul (il2cpp_math.h), result kind
    // "l" (int64). The old LOUD reject (MethodMap.MathExtra.cs) is bypassed
    // because TryMapMath (MethodMap.Math.cs) maps "BigMul" FIRST.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_BigMul_MapsToBigMulIcall()
    {
        // long BigMul(int, int): Sig(ret=0x0A i8, params 0x08 i4, 0x08 i4)
        var resolved = ResolveMember(Synthesize("System", "Math", "BigMul", Sig(0x0A, 0x08, 0x08)), "BigMul");

        Assert.Equal("il2cpp::runtime::icall_math_bigmul", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // .NET Core gMathFuncs super-functions -- the 6 single-value double icalls
    // (dormant mappings: net48 has no such members, semantics = netcore libm).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Acosh", "il2cpp::runtime::icall_math_acosh_d")]
    [InlineData("Asinh", "il2cpp::runtime::icall_math_asinh_d")]
    [InlineData("Atanh", "il2cpp::runtime::icall_math_atanh_d")]
    [InlineData("Cbrt", "il2cpp::runtime::icall_math_cbrt_d")]
    [InlineData("Log2", "il2cpp::runtime::icall_math_log2_d")]
    public void Resolve_Math_SuperFunction1Arg_MapsToDoubleIcall(string name, string cppName)
    {
        var resolved = ResolveMember(Synthesize("System", "Math", name, Sig(0x0D, 0x0D)), name);

        Assert.Equal(cppName, resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_FusedMultiplyAdd_MapsToFmaIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "FusedMultiplyAdd", Sig(0x0D, 0x0D, 0x0D, 0x0D)), "FusedMultiplyAdd");

        Assert.Equal("il2cpp::runtime::icall_math_fma_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // SinCos -- P5: the netcore public API returns a (double,double)
    // ValueTuple (GENERICINST 0x15 return) and RESOLVES now:
    // icall_math_sincos with TupleReturnFields ["d","d"] -- the CodeGenerator
    // AssignStmt handler emits fn(arg, &t.Item1, &t.Item2).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_SinCos_MapsToSincosTupleIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "SinCos",
            new byte[] { 0x00, 0x01, 0x15, 0x11, 0x05, 0x02, 0x0D, 0x0D, 0x0D }), "SinCos");

        Assert.Equal("il2cpp::runtime::icall_math_sincos", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
        Assert.NotNull(resolved.TupleReturnFields);
        Assert.Equal(2, resolved.TupleReturnFields!.Length);
        Assert.Equal(new[] { "d", "d" }, resolved.TupleReturnFields);
    }

    //--------------------------------------------------------------------------
    // ModF -- STILL LOUD: the netcore public API returns a ValueTuple (double
    // frac, out double intPart) and TryMapMathExtra rejects it by NAME
    // regardless of ps (no Math.ModF mapping exists anywhere).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_ModF_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Math", "ModF", Sig(0x0D, 0x0D)), "ModF"));

        Assert.Contains("Math.ModF", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
        Assert.Contains("net48 has no such overload", ex.Message);
    }

    //--------------------------------------------------------------------------
    // mathext members wired in MethodMap.Math.cs (dispatch proof: the task's
    // expected difference set resolves end-to-end through the Math chain)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_Truncate_MapsToTruncateIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Truncate", Sig(0x0D, 0x0D)), "Truncate");

        Assert.Equal("il2cpp::runtime::icall_math_truncate_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_Cosh_MapsToCoshIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Cosh", Sig(0x0D, 0x0D)), "Cosh");

        Assert.Equal("il2cpp::runtime::icall_math_cosh_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_Sinh_MapsToSinhIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Sinh", Sig(0x0D, 0x0D)), "Sinh");

        Assert.Equal("il2cpp::runtime::icall_math_sinh_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_Tanh_MapsToTanhIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Tanh", Sig(0x0D, 0x0D)), "Tanh");

        Assert.Equal("il2cpp::runtime::icall_math_tanh_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_Ieeeremainder_MapsToRemainderIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "IEEERemainder", Sig(0x0D, 0x0D, 0x0D)), "IEEERemainder");

        Assert.Equal("il2cpp::runtime::icall_math_ieee_remainder_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_LogTwoArgs_MapsToLogBaseIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Log", Sig(0x0D, 0x0D, 0x0D)), "Log");

        Assert.Equal("il2cpp::runtime::icall_math_log_base_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests)
    //--------------------------------------------------------------------------

    // Resolves the single MemberReference named `memberName` through the same
    // pipeline the transpiler uses.
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
