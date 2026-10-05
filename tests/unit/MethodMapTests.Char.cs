using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Char.cs dispatch tests -- the System.Char STATIC surface
// (D-CHA-2, charstr GAPS.md): IsDigit/IsLetter/IsLetterOrDigit/IsLower/
// IsUpper/IsWhiteSpace/ToUpper/ToUpperInvariant/ToLower/ToLowerInvariant.
//
// Coverage contract: every char static MUST resolve to its il2cpp_char.h
// icall (char.cpp) with the single-char "c" argument cast, a value return and
// NO receiver (static). The char parameter's element byte is 0x03
// (SignatureTypeCode.Char -- MethodSignatureCoder.cs:99); the Is* methods
// return bool (0x02), the To* methods return char (0x03).
//
// Same synthetic-PE pipeline as MethodMapTests.Math.cs: a real ECMA-335
// assembly whose only MemberReference is the member under test, resolved
// through the transpiler's public Resolve entry point. The System.Char
// INSTANCE surface (ToString()/Equals(object)) must still fall through to
// TryMapValueType (MethodMap.ValueType.cs) -- the dispatch-chain regression
// probe at the bottom.
public class MethodMapTestsChar
{
    [Theory]
    [InlineData("IsDigit", 0x02, "il2cpp::runtime::icall_char_is_digit")]
    [InlineData("IsLetter", 0x02, "il2cpp::runtime::icall_char_is_letter")]
    [InlineData("IsLetterOrDigit", 0x02, "il2cpp::runtime::icall_char_is_letter_or_digit")]
    [InlineData("IsLower", 0x02, "il2cpp::runtime::icall_char_is_lower")]
    [InlineData("IsUpper", 0x02, "il2cpp::runtime::icall_char_is_upper")]
    [InlineData("IsWhiteSpace", 0x02, "il2cpp::runtime::icall_char_is_whitespace")]
    public void Resolve_CharStatic_Is_OneCharBool_MapsToIcall(string name, byte ret, string icall)
    {
        var resolved = ResolveMember(Synthesize("System", "Char", name, Sig(ret, 0x03)), name);

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.IsInstance);
        Assert.Null(resolved.ResultKind);
        Assert.False(resolved.ParamIsRef[0]);
        Assert.Equal("c", resolved.ParamTypeNames![0]);
    }

    [Theory]
    [InlineData("ToUpper", 0x03, "il2cpp::runtime::icall_char_to_upper")]
    [InlineData("ToUpperInvariant", 0x03, "il2cpp::runtime::icall_char_to_upper")]
    [InlineData("ToLower", 0x03, "il2cpp::runtime::icall_char_to_lower")]
    [InlineData("ToLowerInvariant", 0x03, "il2cpp::runtime::icall_char_to_lower")]
    public void Resolve_CharStatic_To_OneCharChar_MapsToIcall(string name, byte ret, string icall)
    {
        var resolved = ResolveMember(Synthesize("System", "Char", name, Sig(ret, 0x03)), name);

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.IsInstance);
        Assert.Null(resolved.ResultKind); // char16_t result widens into the int32 slot
        Assert.False(resolved.ParamIsRef[0]);
        Assert.Equal("c", resolved.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // Dispatch-chain regression: a System.Char INSTANCE member (ToString())
    // must still resolve through TryMapValueType (MethodMap.ValueType.cs) --
    // the new System.Char case line runs TryMapChar BEFORE TryMapValueType and
    // must NOT shadow the instance surface.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Char_ToString_Instance_FallsThroughToValueType()
    {
        var resolved = ResolveMember(Synthesize("System", "Char", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_char_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Math.cs).
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
