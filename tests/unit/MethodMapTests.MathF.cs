using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.MathF dispatch tests: prove the System.MathF domain
// (MethodMap.MathF.cs -> TryMapMathF) maps the corert float-semantics math
// surface to the mathf.cpp icalls (il2cpp_mathf.h). float params decode as
// 0x0C (R4); results ride the "d" double carrier (the float-in-double
// M-stage model -- float widens to double losslessly).
//
//   Sqrt/Sin/... (float)->float      -> icall_mathf_<name>_f ("d" result)
//   Atan2/Pow (float,float)->float   -> icall_mathf_<name>_f
//   Log(x, y) (float,float)->float   -> icall_mathf_log_base_f
//   FusedMultiplyAdd (x,y,z)         -> icall_mathf_fma_f
//   Round(x, digits) (float,int)     -> icall_mathf_round_digits_f
//   ScaleB(x, n) (float,int)         -> icall_mathf_scalb_f
//   Sign(float)/ILogB(float) -> int  -> icall_mathf_sign_i / ilogb_i
//   ModF(float, out float)           -> icall_mathf_modf_f ("addr:d" out cast)
//   SinCos(float) -> (float, float)  -> icall_mathf_sincos_f (TupleReturnFields)
public class MethodMapMathFTests
{
    [Theory]
    [InlineData("Sqrt", "sqrt")]
    [InlineData("Abs", "abs")]
    [InlineData("Sin", "sin")]
    [InlineData("Cos", "cos")]
    [InlineData("Tan", "tan")]
    [InlineData("Asin", "asin")]
    [InlineData("Acos", "acos")]
    [InlineData("Atan", "atan")]
    [InlineData("Exp", "exp")]
    [InlineData("Log", "log")]
    [InlineData("Log2", "log2")]
    [InlineData("Log10", "log10")]
    [InlineData("Ceiling", "ceiling")]
    [InlineData("Floor", "floor")]
    [InlineData("Round", "round")]
    [InlineData("Truncate", "truncate")]
    [InlineData("Cosh", "cosh")]
    [InlineData("Sinh", "sinh")]
    [InlineData("Tanh", "tanh")]
    [InlineData("Acosh", "acosh")]
    [InlineData("Asinh", "asinh")]
    [InlineData("Atanh", "atanh")]
    [InlineData("Cbrt", "cbrt")]
    public void Resolve_MathF_SingleFloat_MapsToFloatIcall(string name, string stem)
    {
        // static float Name(float): ret R4 (0x0C), one R4 param.
        var m = ResolveMember(Synthesize("System", "MathF", name, Sig(0x0C, 0x0C)), name);

        Assert.Equal($"il2cpp::runtime::icall_mathf_{stem}_f", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("d", m.ResultKind); // float widens into the double carrier
    }

    [Theory]
    [InlineData("Atan2", "atan2")]
    [InlineData("Pow", "pow")]
    [InlineData("Max", "max")]
    [InlineData("Min", "min")]
    [InlineData("MaxMagnitude", "max_magnitude")]
    [InlineData("MinMagnitude", "min_magnitude")]
    [InlineData("IEEERemainder", "ieee_remainder")]
    public void Resolve_MathF_TwoFloats_MapsToFloatIcall(string name, string stem)
    {
        var m = ResolveMember(Synthesize("System", "MathF", name, Sig(0x0C, 0x0C, 0x0C)), name);

        Assert.Equal($"il2cpp::runtime::icall_mathf_{stem}_f", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal("d", m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_LogTwoFloats_MapsToLogBaseIcall()
    {
        // Log(float, float) with TWO params routes to log_base_f (not log_f).
        var m = ResolveMember(Synthesize("System", "MathF", "Log", Sig(0x0C, 0x0C, 0x0C)), "Log");

        Assert.Equal("il2cpp::runtime::icall_mathf_log_base_f", m.CppName);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal("d", m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_FusedMultiplyAdd_ThreeFloats_MapsToFmaIcall()
    {
        var m = ResolveMember(Synthesize("System", "MathF", "FusedMultiplyAdd", Sig(0x0C, 0x0C, 0x0C, 0x0C)), "FusedMultiplyAdd");

        Assert.Equal("il2cpp::runtime::icall_mathf_fma_f", m.CppName);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal("d", m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_RoundDigits_MapsToRoundDigitsIcall()
    {
        // Round(float, int): float + int32 params.
        var m = ResolveMember(Synthesize("System", "MathF", "Round", Sig(0x0C, 0x0C, 0x08)), "Round");

        Assert.Equal("il2cpp::runtime::icall_mathf_round_digits_f", m.CppName);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "i" }, m.ParamTypeNames);
        Assert.Equal("d", m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_ScaleB_MapsToScalbIcall()
    {
        var m = ResolveMember(Synthesize("System", "MathF", "ScaleB", Sig(0x0C, 0x0C, 0x08)), "ScaleB");

        Assert.Equal("il2cpp::runtime::icall_mathf_scalb_f", m.CppName);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "i" }, m.ParamTypeNames);
        Assert.Equal("d", m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_Sign_MapsToSignIcall()
    {
        // int Sign(float): ret int32, one float param -- the default int32
        // result kind.
        var m = ResolveMember(Synthesize("System", "MathF", "Sign", Sig(0x08, 0x0C)), "Sign");

        Assert.Equal("il2cpp::runtime::icall_mathf_sign_i", m.CppName);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Null(m.ResultKind);
    }

    [Fact]
    public void Resolve_MathF_ILogB_MapsToIlogbIcall()
    {
        var m = ResolveMember(Synthesize("System", "MathF", "ILogB", Sig(0x08, 0x0C)), "ILogB");

        Assert.Equal("il2cpp::runtime::icall_mathf_ilogb_i", m.CppName);
        Assert.Equal(1, m.ParamCount);
        Assert.Null(m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ModF -- P5: float ModF(float x, out float intPart) RESOLVES now: normal
    // call emission, the BYREF out param decodes to its element byte
    // (0x10 + 0x0C -> ps[1] == 0x0C) and the "addr:d" cast passes the ldloca
    // intPart slot as a typed pointer (float-in-double model); NOT a
    // ValueTuple return (TupleReturnFields null).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MathF_ModF_MapsToModFIcall()
    {
        var m = ResolveMember(Synthesize("System", "MathF", "ModF", Sig(0x0C, 0x0C, 0x10, 0x0C)), "ModF");

        Assert.Equal("il2cpp::runtime::icall_mathf_modf_f", m.CppName);
        Assert.True(m.ReturnsValue);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "d", "addr:d" }, m.ParamTypeNames);
        Assert.Equal("d", m.ResultKind);
        Assert.Null(m.TupleReturnFields);
    }

    //--------------------------------------------------------------------------
    // SinCos -- P5: the netcore public API returns ValueTuple<float,float>
    // (GENERICINST 0x15 return: marker + VALUETYPE token + arity 2 + two R4
    // elements). Resolves to icall_mathf_sincos_f with TupleReturnFields
    // ["d","d"] (float-in-double slot model) -- the CodeGenerator AssignStmt
    // handler emits fn(arg, &t.Item1, &t.Item2).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MathF_SinCos_MapsToSincosTupleIcall()
    {
        var m = ResolveMember(Synthesize("System", "MathF", "SinCos",
            new byte[] { 0x00, 0x01, 0x15, 0x11, 0x05, 0x02, 0x0C, 0x0C, 0x0C }), "SinCos");

        Assert.Equal("il2cpp::runtime::icall_mathf_sincos_f", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.NotNull(m.TupleReturnFields);
        Assert.Equal(2, m.TupleReturnFields!.Length);
        Assert.Equal(new[] { "d", "d" }, m.TupleReturnFields);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (MethodMapTests.IntPtr.cs pipeline)
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
