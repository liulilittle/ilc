using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.StreamIO dispatch tests: prove the System.IO stream domain
// (TryMapStreamIO in MethodMap.StreamIO.cs) routes each member to the
// matching streamio.h icall with the right arity, ref-ness and cast codes,
// and that the unwireable surface rejects LOUDLY (NotSupportedException with
// the "{owner}::{name}" prefix -- see BclResolve's wrap):
//   * the shared Stream-base surface (Flush/Close/Dispose/Position/Length/
//     CanRead/CanWrite) maps to the il2cpp_stream_* icalls -- Position and
//     Length carry int64 result kinds, set_Position casts the value arg "l"
//   * owner-specific members (StreamReader.ReadLine / StreamWriter.WriteLine
//     (string) / MemoryStream.ToArray) map to their streamio.h icalls with
//     the correct managed return types
//   * ALL four ctors reject LOUDLY: the runtime open/create icalls are
//     handle FACTORIES, not object-typed ctor icalls (the emitted ref-type
//     ctor call is (receiver, args...) and discards the return)
//   * Read/Write(byte[], int, int) map to the managed-array read/write icalls
//     (icall_stream_{read,write}_bytes); D-STP-2/3 (batch 134) added Seek /
//     get_CanSeek / SetLength / ReadByte / WriteByte to their single-byte /
//     long streamio.h icalls. LOUD: Dispose(bool) and WriteLine(non-string)
//   * out-of-scope members fall back to the generated-name path
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (MetadataBuilder + ManagedPEBuilder): a real ECMA-335 assembly whose only
// MemberReference is the member under test. Signatures are built with
// Sig(ret, params...) -- the raw bytes RawSignature consumes; class-typed
// returns/params use 0x12 followed by a compressed TypeDefOrRef coded index
// (the synthesized PE holds exactly one TypeReference, so 0x05). byte[]
// params/returns are 0x1D followed by the element byte 0x05.
public class MethodMapStreamIOTests
{
    //--------------------------------------------------------------------------
    // Shared Stream-base surface
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stream_Flush_MapsToFlushIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "Flush", Sig(0x01)), "Flush");

        Assert.Equal("il2cpp::runtime::icall_stream_flush", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stream_Close_MapsToCloseIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "Close", Sig(0x01)), "Close");

        Assert.Equal("il2cpp::runtime::icall_stream_close", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_FileStream_Dispose_NoArgs_MapsToCloseIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", "Dispose", Sig(0x01)), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_stream_close", resolved.CppName);
        Assert.True(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_FileStream_GetPosition_MapsToGetPositionIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", "get_Position", Sig(0x0A)), "get_Position");

        Assert.Equal("il2cpp::runtime::icall_stream_get_position", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("l", resolved.ResultKind); // int64 carrier
    }

    [Fact]
    public void Resolve_FileStream_SetPosition_MapsToSetPositionIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", "set_Position", Sig(0x01, 0x0A)), "set_Position");

        Assert.Equal("il2cpp::runtime::icall_stream_set_position", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "l" }, resolved.ParamTypeNames); // receiver + int64 value
    }

    [Fact]
    public void Resolve_MemoryStream_GetLength_MapsToGetLengthIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "MemoryStream", "get_Length", Sig(0x0A)), "get_Length");

        Assert.Equal("il2cpp::runtime::icall_stream_get_length", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Stream_GetCanRead_MapsToGetCanReadIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_CanRead", Sig(0x02)), "get_CanRead");

        Assert.Equal("il2cpp::runtime::icall_stream_get_can_read", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Stream_GetCanWrite_MapsToGetCanWriteIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_CanWrite", Sig(0x02)), "get_CanWrite");

        Assert.Equal("il2cpp::runtime::icall_stream_get_can_write", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Owner-specific members
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StreamReader_ReadLine_MapsToReadLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadLine", Sig(0x0E)), "ReadLine");

        Assert.Equal("il2cpp::runtime::icall_stream_read_line", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_StreamWriter_WriteLineString_MapsToWriteLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "StreamWriter", "WriteLine", Sig(0x01, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_stream_write_line", resolved.CppName);
        Assert.False(resolved.ReturnsValue); // the runtime returns int32, .NET is void
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_MemoryStream_ToArray_MapsToMemoryToArrayIcall()
    {
        // ret byte[] = 0x1D SZARRAY + element byte 0x05 (byte), 0 params.
        var resolved = ResolveMember(Synthesize("System.IO", "MemoryStream", "ToArray",
            new byte[] { 0x00, 0x00, 0x1D, 0x05 }), "ToArray");

        Assert.Equal("il2cpp::runtime::icall_stream_memory_to_array", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Constructors -- object-typed ctor icalls (the stream-b23 object layer)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_FileStream_Ctor_ThreeParams_MapsToFileCtorIcall()
    {
        // FileStream(string, FileMode, FileAccess): 3 params, void.
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", ".ctor",
            Sig(0x01, 0x0E, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stream_file_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_FileStream_Ctor_TwoParams_ThrowsLoud()
    {
        // FileStream(string, FileMode): 2 params -- no literal-injection
        // mechanism for the mode constant, LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "FileStream", ".ctor", Sig(0x01, 0x0E, 0x08)), ".ctor"));

        Assert.Contains("System.IO.FileStream::.ctor", ex.Message);
    }

    [Fact]
    public void Resolve_MemoryStream_Ctor_MapsToMemoryCtorIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "MemoryStream", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stream_memory_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_StreamReader_Ctor_Stream_MapsToReaderCtorIcall()
    {
        // StreamReader(Stream): the wrapper shares the wrapped stream's state.
        var resolved = ResolveMember(Synthesize("System.IO", "StreamReader", ".ctor",
            Sig(0x01, 0x12, 0x05)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stream_reader_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_StreamReader_Ctor_String_ThrowsLoud()
    {
        // StreamReader(string): the object icall takes only (Stream), LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "StreamReader", ".ctor", Sig(0x01, 0x0E)), ".ctor"));

        Assert.Contains("System.IO.StreamReader::.ctor", ex.Message);
    }

    [Fact]
    public void Resolve_StreamWriter_Ctor_Stream_MapsToWriterCtorIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "StreamWriter", ".ctor",
            Sig(0x01, 0x12, 0x05)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stream_writer_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_StreamWriter_Ctor_String_MapsToPathCtorIcall()
    {
        // StreamWriter(string path): the runtime string-path ctor
        // (icall_stream_writer_path_ctor) opens FileMode.Create +
        // FileAccess.Write and stores the handle on the writer object
        // (streamops `using (StreamWriter usw = new StreamWriter(file))`).
        var resolved = ResolveMember(Synthesize("System.IO", "StreamWriter", ".ctor", Sig(0x01, 0x0E)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stream_writer_path_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // LOUD rejection of close-but-wrong / unwired members
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stream_ReadByteArray_MapsToReadBytesIcall()
    {
        // int Read(byte[] buffer, int offset, int count) -- ret int32 (0x08),
        // params byte[] (0x1D 0x05), int, int.
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "Read",
            new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x05, 0x08, 0x08 }), "Read");

        Assert.Equal("il2cpp::runtime::icall_stream_read_bytes", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "a", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_FileStream_WriteByteArray_MapsToWriteBytesIcall()
    {
        // void Write(byte[] buffer, int offset, int count) -- ret void (0x01).
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", "Write",
            new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x05, 0x08, 0x08 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_stream_write_bytes", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "a", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stream_ReadByte_MapsToReadByteIcall()
    {
        // D-STP-3 (2026-08-12, batch 134): ReadByte() is now wired to the
        // single-byte icall icall_stream_read_byte (int 0..255, -1 at EOF).
        // (The old "_ThrowsLoud" assertion was a batch-134 leftover -- the map
        // gained the icall but the test was not updated; fixed in D-LINQC-1.)
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "ReadByte", Sig(0x08)), "ReadByte");

        Assert.Equal("il2cpp::runtime::icall_stream_read_byte", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stream_WriteByte_MapsToWriteByteIcall()
    {
        // D-STP-3: WriteByte(byte) -> icall_stream_write_byte (byte rides the
        // "i" int32-slot cast). (See Resolve_Stream_ReadByte_MapsToReadByteIcall
        // for the batch-134 stale-test note.)
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "WriteByte", Sig(0x01, 0x05)), "WriteByte");

        Assert.Equal("il2cpp::runtime::icall_stream_write_byte", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stream_DisposeBool_MapsToClose()
    {
        // Dispose(bool) -- batch 150f: the flag is dropped (Close is
        // idempotent; disposing=false is a no-op either way).
        var m = ResolveMember(Synthesize("System.IO", "Stream", "Dispose", Sig(0x01, 0x02)), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_stream_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "drop" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamWriter_WriteLineInt_ThrowsLoud()
    {
        // WriteLine(int) must NOT route through write_line(string).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "StreamWriter", "WriteLine", Sig(0x01, 0x08)), "WriteLine"));

        Assert.Contains("System.IO.StreamWriter::WriteLine", ex.Message);
    }

    //--------------------------------------------------------------------------
    // D-STO-1 (2026-08-11): timeout surface + ReadToEnd wiring
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stream_GetCanTimeout_MapsToCanTimeoutIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_CanTimeout", Sig(0x02)), "get_CanTimeout");

        Assert.Equal("il2cpp::runtime::icall_stream_get_can_timeout", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stream_GetReadTimeout_MapsToReadTimeoutIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_ReadTimeout", Sig(0x08)), "get_ReadTimeout");

        Assert.Equal("il2cpp::runtime::icall_stream_get_read_timeout", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stream_SetReadTimeout_MapsToSetReadTimeoutIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "set_ReadTimeout", Sig(0x01, 0x08)), "set_ReadTimeout");

        Assert.Equal("il2cpp::runtime::icall_stream_set_read_timeout", resolved.CppName);
        Assert.False(resolved.ReturnsValue); // the runtime returns int32, .NET is void
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stream_GetWriteTimeout_MapsToWriteTimeoutIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_WriteTimeout", Sig(0x08)), "get_WriteTimeout");

        Assert.Equal("il2cpp::runtime::icall_stream_get_write_timeout", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stream_SetWriteTimeout_MapsToSetWriteTimeoutIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "set_WriteTimeout", Sig(0x01, 0x08)), "set_WriteTimeout");

        Assert.Equal("il2cpp::runtime::icall_stream_set_write_timeout", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamReader_ReadToEnd_MapsToReadToEndIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadToEnd", Sig(0x0E)), "ReadToEnd");

        Assert.Equal("il2cpp::runtime::icall_stream_read_to_end", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_FileStream_Seek_ThrowsLoud()
    {
        // D-STO-1: Seek has NO runtime icall (grep-confirmed) -- the runtime
        // exposes only get/set_position; a relative-origin seek would need a
        // dedicated icall variant. LOUD, not the generated-name fallback (a
        // probe would emit a mangled symbol and fail as a C++ link error).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "FileStream", "Seek",
                new byte[] { 0x20, 0x02, 0x0A, 0x0A, 0x08 }), "Seek"));

        Assert.Contains("System.IO.FileStream::Seek", ex.Message);
    }

    [Fact]
    public void Resolve_Stream_GetCanSeek_MapsToGetCanSeekIcall()
    {
        // D-STP-2 (batch 134): get_CanSeek now maps to
        // icall_stream_get_can_seek. (Stale-test note: see
        // Resolve_Stream_ReadByte_MapsToReadByteIcall.)
        var resolved = ResolveMember(Synthesize("System.IO", "Stream", "get_CanSeek", Sig(0x02)), "get_CanSeek");

        Assert.Equal("il2cpp::runtime::icall_stream_get_can_seek", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_FileStream_SetLength_MapsToSetLengthIcall()
    {
        // D-STP-2 (batch 134): SetLength(long) -> icall_stream_set_length
        // (void, the long value rides the "l" cast). (Stale-test note: see
        // Resolve_Stream_ReadByte_MapsToReadByteIcall.)
        var resolved = ResolveMember(Synthesize("System.IO", "FileStream", "SetLength", Sig(0x01, 0x0A)), "SetLength");

        Assert.Equal("il2cpp::runtime::icall_stream_set_length", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.BinaryIO)
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
    // The count is derived by WALKING the parameter section -- CLASS/VALUETYPE
    // (0x11/0x12/0x40), SZARRAY (0x1D/0x1F) and BYREF (0x10) params carry a
    // trailing byte (coded index / element byte) that must be consumed; the
    // return byte may carry the same trailing for a CLASS/VALUETYPE return.
    private static byte[] Sig(byte ret, params byte[] ps)
    {
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
