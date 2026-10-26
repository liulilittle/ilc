using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Math.cs dispatch tests -- System.Math.Round arity dispatch.
//
// Coverage contract: Math.Round MUST dispatch on the parameter signature. The
// old single-arity mapping sent EVERY overload to the 1-param
// icall_math_round_d and dropped the first argument of Round(double, int)
// (math2 generated `round_d(2LL)` -- printed 2 instead of 3.14/2.68). The
// 2-param (double, int32 digits) form maps to icall_math_round_digits_d; the
// MidpointRounding overloads are LOUD-rejected (the runtime has only the
// default ToEven path).
//
// Same synthetic-PE pipeline as MethodMapTests.MathExtra.cs: a real ECMA-335
// assembly whose only MemberReference is the member under test, resolved
// through the transpiler's public Resolve entry point.
public class MethodMapTestsMath
{
    //--------------------------------------------------------------------------
    // Round(double) -- the 1-param banker's icall (unchanged behaviour).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_Round1Arg_MapsToRoundD()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Round", Sig(0x0D, 0x0D)), "Round");

        Assert.Equal("il2cpp::runtime::icall_math_round_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Round(double, int digits) -- MUST resolve to the 2-param digits icall
    // with ParamCount 2 (the fold pops BOTH stack args; ParamCount 1 dropped
    // the first argument and generated round_d(2LL)).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_Round2ArgInt_MapsToRoundDigitsD()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Round", Sig(0x0D, 0x0D, 0x08)), "Round");

        Assert.Equal("il2cpp::runtime::icall_math_round_digits_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Round(double, MidpointRounding) -- WIRED (batch 149): the mode select
    // (0 ToEven / 1 AwayFromZero). MidpointRounding is an enum (a VALUETYPE),
    // which the signature encoder emits as 0x11 (followed by its TypeDefOrRef
    // coded index); a CLASS (0x12) second argument is NOT the enum overload
    // and stays LOUD below.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_RoundMidpointRounding2Arg_MapsToRoundModeD()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Round",
            new byte[] { 0x00, 0x02, 0x0D, 0x0D, 0x11, 0x05 }), "Round");

        Assert.Equal("il2cpp::runtime::icall_math_round_mode_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "d", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Round(double, CLASS) -- a non-enum reference second argument has no
    // mapping; MUST be LOUD instead of silently mapping to a 1-param icall
    // and dropping an argument.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Math_RoundClass2Arg_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Math", "Round",
                new byte[] { 0x00, 0x02, 0x0D, 0x0D, 0x12, 0x05 }), "Round"));

        Assert.Contains("System.Math::Round", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.MathExtra.cs).
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
