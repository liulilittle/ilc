using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// LOUD P0-19: Math decimal overload dispatch.
//
// System.Math carries Abs/Max/Min/Sign/Floor/Ceiling/Round/Truncate overloads
// for decimal. A decimal parameter is a VALUETYPE (0x11) in the signature --
// the SAME byte DateTime / TimeSpan / user structs / enums use -- so the byte
// alone cannot select the decimal arm. The dispatch key here is the tracked
// managed type (arg0Type == "System.Decimal"), exactly like the Write(strcut)
// decimal arm. Every decimal branch must resolve to the decimal.h icall family
// (raw 16-byte value slots: args "dec", decimal result "dec"), NEVER the
// double/long fallbacks (which produced `abs_d(<decimal slot>)` and the
// C2664 e2e failure).
//
// The no-misfire assertions pin the pre-existing int/long/double paths.
public class MethodMapTestsMathDecimal
{
    private const string Dec = "System.Decimal";

    [Fact]
    public void Resolve_Math_AbsDecimal_MapsToDecimalAbs()
    {
        var resolved = ResolveMember(Synthesize("Math", "Abs",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Abs", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_abs", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Math_MaxDecimal_MapsToDecimalMax()
    {
        var resolved = ResolveMember(Synthesize("Math", "Max",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "Max", Dec, Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_max", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec", "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_MinDecimal_MapsToDecimalMin()
    {
        var resolved = ResolveMember(Synthesize("Math", "Min",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "Min", Dec, Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_min", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec", "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_SignDecimal_MapsToDecimalSign()
    {
        var resolved = ResolveMember(Synthesize("Math", "Sign",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "Sign", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_sign", resolved.CppName);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_FloorDecimal_MapsToDecimalFloor()
    {
        var resolved = ResolveMember(Synthesize("Math", "Floor",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Floor", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_floor", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_CeilingDecimal_MapsToDecimalCeiling()
    {
        var resolved = ResolveMember(Synthesize("Math", "Ceiling",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Ceiling", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_ceiling", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_TruncateDecimal_MapsToDecimalTruncate()
    {
        var resolved = ResolveMember(Synthesize("Math", "Truncate",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Truncate", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_truncate", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Math_RoundDecimal_MapsToDecimalRoundToEven()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Round", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_round", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Math_RoundDecimalDigits_MapsToDecimalRound()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x08 }), "Round", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_round", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Math_RoundDecimalMode_MapsToDecimalRoundMode()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "Round", Dec,
            "System.MidpointRounding");

        Assert.Equal("il2cpp::runtime::icall_decimal_round_mode", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec", "i" }, resolved.ParamTypeNames);
        Assert.Null(resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Math_RoundDecimalDigitsMode_MapsToDecimalRound()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x03, 0x11, 0x05, 0x11, 0x05, 0x08, 0x11, 0x05 }), "Round", Dec);

        Assert.Equal("il2cpp::runtime::icall_decimal_round", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
        Assert.Equal(new[] { "dec", "i", "i" }, resolved.ParamTypeNames);
        Assert.Null(resolved.AppendLiterals);
    }

    //--------------------------------------------------------------------------
    // No-misfire: the primitives keep their original icalls. A valuetype byte
    // WITHOUT the tracked decimal type (e.g. DateTime 0x11) must NOT take the
    // decimal arm.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_AbsInt_StaysInt()
    {
        var resolved = ResolveMember(Synthesize("Math", "Abs",
            new byte[] { 0x00, 0x01, 0x08, 0x08 }), "Abs", null);

        Assert.Equal("il2cpp::runtime::icall_math_abs_i", resolved.CppName);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_AbsLong_StaysLong()
    {
        var resolved = ResolveMember(Synthesize("Math", "Abs",
            new byte[] { 0x00, 0x01, 0x0A, 0x0A }), "Abs", null);

        Assert.Equal("il2cpp::runtime::icall_math_abs_l", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_AbsDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Abs",
            new byte[] { 0x00, 0x01, 0x0D, 0x0D }), "Abs", null);

        Assert.Equal("il2cpp::runtime::icall_math_abs_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_AbsOtherValueType_StaysDoubleFallback()
    {
        // 0x11 is NOT enough: without the System.Decimal hint this stays on the
        // historical double fallback (DateTime/enum/user-struct territory is
        // out of scope for this batch).
        var resolved = ResolveMember(Synthesize("Math", "Abs",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "Abs", "System.DateTime");

        Assert.Equal("il2cpp::runtime::icall_math_abs_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_MaxInt_StaysInt()
    {
        var resolved = ResolveMember(Synthesize("Math", "Max",
            Sig(0x08, 0x08, 0x08)), "Max", null);

        Assert.Equal("il2cpp::runtime::icall_math_max_i", resolved.CppName);
    }

    [Fact]
    public void Resolve_Math_MinDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Min",
            Sig(0x0D, 0x0D, 0x0D)), "Min", null);

        Assert.Equal("il2cpp::runtime::icall_math_min_d", resolved.CppName);
    }

    [Fact]
    public void Resolve_Math_SignDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Sign", Sig(0x08, 0x0D)), "Sign", null);

        Assert.Equal("il2cpp::runtime::icall_math_sign_d", resolved.CppName);
    }

    [Fact]
    public void Resolve_Math_FloorDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Floor", Sig(0x0D, 0x0D)), "Floor", null);

        Assert.Equal("il2cpp::runtime::icall_math_floor_d", resolved.CppName);
    }

    [Fact]
    public void Resolve_Math_RoundDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round", Sig(0x0D, 0x0D)), "Round", null);

        Assert.Equal("il2cpp::runtime::icall_math_round_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_RoundDoubleMode_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x02, 0x0D, 0x0D, 0x11, 0x05 }), "Round", null, "System.MidpointRounding");

        Assert.Equal("il2cpp::runtime::icall_math_round_mode_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_RoundDoubleDigitsMode_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Round",
            new byte[] { 0x00, 0x03, 0x0D, 0x0D, 0x08, 0x11, 0x05 }), "Round", null, "System.MidpointRounding");

        Assert.Equal("il2cpp::runtime::icall_math_round_digits_mode_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Math_TruncateDouble_StaysDouble()
    {
        var resolved = ResolveMember(Synthesize("Math", "Truncate", Sig(0x0D, 0x0D)), "Truncate", null);

        Assert.Equal("il2cpp::runtime::icall_math_truncate_d", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Synthetic PE helpers (same pipeline as MethodMapTests.Math.cs).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? arg0Type,
        string? arg1Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, arg0Type, arg1Type);
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

    private static MemoryStream Synthesize(string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString(typeName));
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
