using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Encoding dispatch tests: prove the System.Text.Encoding domain
// (TryMapEncoding in MethodMap.Encoding.cs) resolves each member with real
// behavior:
//
//   Encoding.get_UTF8 / get_Unicode / get_ASCII -> fold to null (::null:: --
//     the runtime has no Encoding object) and record the concrete encoding
//     type (ReturnTypeName) so the folded NullExpr carries the receiver hint
//     for the GetBytes/GetString page dispatch
//   Encoding.GetBytes(string) -> byte[] / GetString(byte[]) -> string ->
//     the allocating per-page icalls (il2cpp_encoding_utf8_get_bytes etc.),
//     dispatched by the receiver's tracked encoding type; the receiver is
//     dropped, the string/byte[] argument carries the "s"/"a" cast
//   Encoding.GetBytes/GetString WITHOUT a receiver hint -> generated-name
//     fallback (the fold's first resolution defers to the receiver-aware
//     re-resolve; the bare generated name means the emitted call fails LOUD
//     only when the receiver stays untracked)
//   Encoding.GetChars / GetBytes(5-arg) / GetString(3-arg) -> LOUD
//     NotSupportedException
//   out-of-scope members (GetHashCode)          -> generated-name fallback
//
// The PEs are synthesized with System.Reflection.Metadata's own writer (the
// same pipeline as MethodMapTests.Guid): signature blobs are REAL ECMA-335
// shapes -- [0x00 header][param count][return byte][return payload][params]
// (0x08=i4, 0x0E=string, 0x05=byte, 0x03=char, 0x1D=SZARRAY whose element
// byte follows). The return payload is consumed by BOTH decoders
// (MethodSignatureCoder.Decode and RawSignature): CLASS (0x12) carries the
// TypeDefOrRef coded index -- the synthetic PE's one TypeRef,
// System.Text.Encoding, is row 1, and the TypeRef tag is 1, so
// (1 << 2) | 1 = 0x05 -- and SZARRAY
// (0x1D) carries its element byte. The param count must therefore be the
// TRUE parameter count, NOT the blob-tail length: counting return payload
// bytes as parameters reads out of bounds in the parameter loop (the
// original five failures). The mapping dispatches on NAME only, so the
// decode payloads are otherwise inert.
public class MethodMapEncodingTests
{
    //--------------------------------------------------------------------------
    // Static encoding getters -- fold to null + record the concrete type
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_UTF8", "System.Text.UTF8Encoding")]
    [InlineData("get_Unicode", "System.Text.UnicodeEncoding")]
    [InlineData("get_ASCII", "System.Text.ASCIIEncoding")]
    public void Resolve_Encoding_StaticGetter_FoldsToNullWithType(string name, string typeName)
    {
        // static Encoding get_X(): ret class Encoding (0x12 + TypeRef coded
        // index 0x05 -- TypeDefOrRef tag TypeRef=1, row 1 -> (1 << 2) | 1),
        // zero params.
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", name, SigEx(0x12, new byte[] { 0x05 }, 0)), name);

        Assert.Equal(ResolvedMethod.NullMarker, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        // The concrete encoding type feeds the folded null's receiver hint.
        Assert.Equal(typeName, resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // GetBytes(string) / GetString(byte[]) -- allocating per-page icalls,
    // dispatched by the receiver's tracked encoding type
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Text.UTF8Encoding", "il2cpp::runtime::il2cpp_encoding_utf8_get_bytes")]
    [InlineData("System.Text.UnicodeEncoding", "il2cpp::runtime::il2cpp_encoding_unicode_get_bytes")]
    [InlineData("System.Text.ASCIIEncoding", "il2cpp::runtime::il2cpp_encoding_ascii_get_bytes")]
    public void Resolve_Encoding_GetBytes_WithEncodingReceiver_MapsToPageIcall(string receiverType, string icall)
    {
        // instance byte[] GetBytes(string): ret SZARRAY byte (element byte
        // 0x05 is the return payload), 1 param string.
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetBytes",
            SigEx(0x1D, new byte[] { 0x05 }, 1, 0x0E)), "GetBytes", receiverType);

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
        // The null-folded receiver is dropped; the string takes the "s" cast.
        Assert.Equal(new[] { "drop", "s" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("System.Text.UTF8Encoding", "il2cpp::runtime::il2cpp_encoding_utf8_get_string")]
    [InlineData("System.Text.UnicodeEncoding", "il2cpp::runtime::il2cpp_encoding_unicode_get_string")]
    [InlineData("System.Text.ASCIIEncoding", "il2cpp::runtime::il2cpp_encoding_ascii_get_string")]
    public void Resolve_Encoding_GetString_WithEncodingReceiver_MapsToPageIcall(string receiverType, string icall)
    {
        // instance string GetString(byte[]): ret string, 1 param SZARRAY byte
        // (marker 0x1D + element 0x05).
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetString",
            SigEx(0x0E, Array.Empty<byte>(), 1, 0x1D, 0x05)), "GetString", receiverType);

        Assert.Equal(icall, resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        // The byte[] argument takes the "a" cast (Il2CppObject* -> Il2CppArray*).
        Assert.Equal(new[] { "drop", "a" }, resolved.ParamTypeNames);
    }

    // The derived-owner route is the same map (UTF8Encoding-typed receivers
    // may arrive via a UTF8Encoding-declared MemberRef).
    [Fact]
    public void Resolve_UTF8Encoding_GetBytes_WithReceiver_MapsToUtf8Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "UTF8Encoding", "GetBytes",
            SigEx(0x1D, new byte[] { 0x05 }, 1, 0x0E)), "GetBytes", "System.Text.UTF8Encoding");

        Assert.Equal("il2cpp::runtime::il2cpp_encoding_utf8_get_bytes", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // First resolution (no receiver hint) defers to the receiver-aware
    // re-resolve -- the generated-name fallback, never a LOUD throw.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Encoding_GetBytes_NoReceiverHint_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetBytes",
            SigEx(0x1D, new byte[] { 0x05 }, 1, 0x0E)), "GetBytes");

        Assert.StartsWith("System_Text_Encoding_GetBytes_", resolved.CppName);
    }

    [Fact]
    public void Resolve_Encoding_GetString_NoReceiverHint_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetString",
            SigEx(0x0E, Array.Empty<byte>(), 1, 0x1D, 0x05)), "GetString");

        Assert.StartsWith("System_Text_Encoding_GetString_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Unwired shapes -- LOUD with the {owner}::{name} prefix from BclResolve's
    // wrap
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Encoding_GetBytes_UnknownReceiver_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "Encoding", "GetBytes",
                SigEx(0x1D, new byte[] { 0x05 }, 1, 0x0E)), "GetBytes", "System.Text.Latin1Encoding"));

        Assert.Contains("System.Text.Encoding::GetBytes", ex.Message);
        Assert.Contains("Latin1Encoding", ex.Message);
    }

    [Fact]
    public void Resolve_Encoding_GetBytes_FiveArgs_ThrowsLoud()
    {
        // instance int GetBytes(string, int, int, byte[], int): ret i4,
        // params string + 2x i4 + SZARRAY byte + i4.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "Encoding", "GetBytes",
                SigEx(0x08, Array.Empty<byte>(), 5, 0x0E, 0x08, 0x08, 0x1D, 0x05, 0x08)), "GetBytes"));

        Assert.Contains("System.Text.Encoding::GetBytes", ex.Message);
        Assert.Contains("overload", ex.Message);
    }

    [Fact]
    public void Resolve_Encoding_GetString_ThreeArgs_ThrowsLoud()
    {
        // instance string GetString(byte[], int, int): ret string,
        // params SZARRAY byte + 2x i4.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "Encoding", "GetString",
                SigEx(0x0E, Array.Empty<byte>(), 3, 0x1D, 0x05, 0x08, 0x08)), "GetString"));

        Assert.Contains("System.Text.Encoding::GetString", ex.Message);
        Assert.Contains("overload", ex.Message);
    }

    //--------------------------------------------------------------------------
    // GetChars(byte[]) -> char[] -- the M5 UTF8 page is wired
    // (il2cpp_encoding_utf8_get_chars); the no-hint first resolution defers
    // to the receiver-aware re-resolve like GetBytes/GetString.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Encoding_GetChars_UTF8Receiver_MapsToUtf8Icall()
    {
        // instance char[] GetChars(byte[]): ret SZARRAY char (element byte
        // 0x03 is the return payload), 1 param SZARRAY byte.
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetChars",
            SigEx(0x1D, new byte[] { 0x03 }, 1, 0x1D, 0x05)), "GetChars", "System.Text.UTF8Encoding");

        Assert.Equal("il2cpp::runtime::il2cpp_encoding_utf8_get_chars", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Char[]", resolved.ReturnTypeName);
        // The null-folded receiver is dropped; the byte[] argument carries "a".
        Assert.Equal(new[] { "drop", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Encoding_GetChars_NoReceiverHint_FallsBackToGeneratedName()
    {
        // No receiver hint -> the map returns null (defer to the receiver-aware
        // re-resolve) and the generated-name fallback is kept, never a LOUD
        // throw -- the GetBytes/GetString no-hint convention.
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetChars",
            SigEx(0x1D, new byte[] { 0x03 }, 1, 0x1D, 0x05)), "GetChars");

        Assert.StartsWith("System_Text_Encoding_GetChars_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // enc-b23: the ranged GetChars(byte[], int, int) allocating form on ALL
    // three pages (il2cpp_encoding_<page>_get_chars 3-arg overloads).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Text.UTF8Encoding", "utf8")]
    [InlineData("System.Text.UnicodeEncoding", "unicode")]
    [InlineData("System.Text.ASCIIEncoding", "ascii")]
    public void Resolve_Encoding_GetChars_Ranged_WithReceiver_MapsToPageIcall(string receiverType, string page)
    {
        // instance char[] GetChars(byte[] bytes, int32 index, int32 count):
        // ret SZARRAY char, 3 params byte[] + int32 + int32.
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetChars",
            SigEx(0x1D, new byte[] { 0x03 }, 3, 0x1D, 0x05, 0x08, 0x08)), "GetChars", receiverType);

        Assert.Equal($"il2cpp::runtime::il2cpp_encoding_{page}_get_chars", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Char[]", resolved.ReturnTypeName);
        // Null-folded receiver dropped; byte[] "a"; index/count int32 "".
        Assert.Equal(new[] { "drop", "a", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Encoding_GetChars_Ranged_NoReceiverHint_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetChars",
            SigEx(0x1D, new byte[] { 0x03 }, 3, 0x1D, 0x05, 0x08, 0x08)), "GetChars");

        Assert.StartsWith("System_Text_Encoding_GetChars_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Out-of-scope members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Encoding_GetHashCode_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "Encoding", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.StartsWith("System_Text_Encoding_GetHashCode_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Guid)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return receiverType == null
            ? MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>())
            : MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
                receiverType, false, null);
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

    // Signature blob with a RETURN PAYLOAD (the bytes both decoders consume
    // after the return byte, before the params): the TypeDefOrRef coded index
    // for CLASS (0x12)/VALUETYPE (0x11) returns, the element byte for SZARRAY
    // (0x1D) returns. count is the TRUE parameter count -- the decoder reads
    // exactly that many parameters from body, and a SZARRAY parameter spans
    // marker + element bytes, so the body length is NOT the count (counting
    // body bytes as parameters reads out of bounds in the parameter loop).
    private static byte[] SigEx(byte ret, byte[] retPayload, int count, params byte[] body)
    {
        var bytes = new byte[3 + retPayload.Length + body.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        Array.Copy(retPayload, 0, bytes, 3, retPayload.Length);
        Array.Copy(body, 0, bytes, 3 + retPayload.Length, body.Length);
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
