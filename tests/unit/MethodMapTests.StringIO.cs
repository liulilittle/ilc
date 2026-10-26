using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.StringIO dispatch tests: prove the System.IO.StringReader /
// System.IO.StringWriter domain (TryMapStringIO in MethodMap.StringIO.cs).
// The domain is CLOSED (M6 object closure): the runtime defines the
// System_IO_StringReader_class / System_IO_StringWriter_class descriptors
// (stringrw.h) and the object icalls, so every mapped member resolves to its
// icall -- the .ctor / Read / Peek / ReadLine / ReadToEnd / Write / WriteLine
// / ToString / Close mappings assert the exact C++ name + instance/static +
// return shape:
//
//   StringReader.ctor(string)     -> icall_stringreader_ctor (instance, void)
//   StringReader.Read()           -> icall_stringreader_read (int32, -1 EOF)
//   StringReader.Peek()           -> icall_stringreader_peek (int32, -1 EOF)
//   StringReader.ReadLine()       -> icall_stringreader_read_line (string)
//   StringReader.ReadToEnd()      -> icall_stringreader_read_to_end (string)
//   StringReader.Close()/Dispose()-> icall_stringreader_close
//   StringWriter.ctor()           -> icall_stringwriter_ctor (instance, void)
//   StringWriter.Write(string)    -> icall_stringwriter_write (void)
//   StringWriter.WriteLine(string)-> icall_stringwriter_write_line (void)
//   StringWriter.ToString()       -> icall_stringwriter_to_string (string)
//   StringWriter.Close()/Dispose()-> icall_stringwriter_close
//
// Cross-domain route (G6, same as Version/Uri in MethodMap.Object.cs):
// Roslyn emits callvirt System.Object::ToString for StringWriter.ToString
// (net48 IL, stringrwcase e2e), so TryMapObject routes the Object-owner
// memberref to icall_stringwriter_to_string via the receiver hint:
//
//   Object::ToString on a StringWriter receiver -> icall_stringwriter_to_string
//
// Unwireable members throw NotSupportedException:
//   StringReader.Read(char[],int,int) -> needs managed-array icall
//   StringWriter.Write(char)/Write(char[])/WriteLine(other arities) -> icall
//     takes exactly (self, string)
//   StringWriter.ctor(IFormatProvider) -> no provider-taking ctor icall
//   Dispose(bool) -> no disposing flag
//   StringReader.GetHashCode etc.  -> default case: member not wired
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x02=bool, 0x08=i4, 0x0E=string, 0x12=class) -- see
// MethodMapTests.VersionUri.cs for the conventions.
public class MethodMapStringIOTests
{
    //--------------------------------------------------------------------------
    // StringReader ctor(string) -> icall_stringreader_ctor (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringReader_CtorString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", ".ctor", Sig(0x01, 0x0E)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stringreader_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringReader.Read()/Peek() -> icall_stringreader_read/peek (int32)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Read", "il2cpp::runtime::icall_stringreader_read")]
    [InlineData("Peek", "il2cpp::runtime::icall_stringreader_peek")]
    public void Resolve_StringReader_ReadPeek_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", name, Sig(0x08)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringReader.ReadLine()/ReadToEnd() -> string-returning icalls
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ReadLine", "il2cpp::runtime::icall_stringreader_read_line")]
    [InlineData("ReadToEnd", "il2cpp::runtime::icall_stringreader_read_to_end")]
    public void Resolve_StringReader_ReadLineReadToEnd_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", name, Sig(0x0E)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringReader.Close()/Dispose() -> icall_stringreader_close (void)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Close")]
    [InlineData("Dispose")]
    public void Resolve_StringReader_CloseDispose_MapsToIcall(string name)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", name, Sig(0x01)), name);

        Assert.Equal("il2cpp::runtime::icall_stringreader_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringReader.Read(char[], int, int) -- LOUD (managed-array icall gap)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringReader_ReadArray_MapsToReadChars()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", "Read",
            new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x03, 0x08, 0x08 }), "Read");

        Assert.Equal("il2cpp::runtime::icall_stringreader_read_chars", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal(new[] { "", "a", "i", "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Cross-domain G6: Object::ToString on a StringWriter receiver (Roslyn
    // emits callvirt Object::ToString for w.ToString() -- stringrwcase e2e)
    // -> icall_stringwriter_to_string via the receiver hint (TryMapObject).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_ToString_OnStringWriterReceiver_RoutesToStringWriterIcall()
    {
        var m = ResolveMember(Synthesize("System", "Object", "ToString", Sig(0x0E)), "ToString",
            receiverType: "System.IO.StringWriter");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_to_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringWriter ctor() -> icall_stringwriter_ctor (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringWriter_CtorDefault_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringWriter.Write(string)/WriteLine(string) -> void icalls
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Write", "il2cpp::runtime::icall_stringwriter_write")]
    [InlineData("WriteLine", "il2cpp::runtime::icall_stringwriter_write_line")]
    public void Resolve_StringWriter_WriteWriteLineString_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", name, Sig(0x01, 0x0E)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringWriter.ToString() -> icall_stringwriter_to_string (string)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringWriter_ToString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_to_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // StringWriter.Close()/Dispose() -> icall_stringwriter_close (void)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("Close")]
    [InlineData("Dispose")]
    public void Resolve_StringWriter_CloseDispose_MapsToIcall(string name)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", name, Sig(0x01)), name);

        Assert.Equal("il2cpp::runtime::icall_stringwriter_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Unwireable members -- LOUD
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringWriter_CtorProvider_MapsToProviderCtor()
    {
        // StringWriter(IFormatProvider): the provider is stored and exposed
        // through FormatProvider (no longer LOUD).
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_ctor_provider", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_StringWriter_CtorStringBuilder_MapsToBuilderCtor()
    {
        // StringBuilder is also a 0x12 CLASS: the tracked argument type
        // selects the builder-backed ctor.
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x12, 0x05 }), ".ctor",
            receiverType: "System.IO.StringWriter", arg0Type: "System.Text.StringBuilder");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_ctor_sb", m.CppName);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_StringWriter_GetStringBuilder_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "GetStringBuilder", Sig(0x1C)), "GetStringBuilder");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_get_string_builder", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal("System.Text.StringBuilder", m.ReturnTypeName);
    }

    [Theory]
    [InlineData("Write", 0x08, "il2cpp::runtime::icall_stringwriter_write_i", "i")]
    [InlineData("Write", 0x09, "il2cpp::runtime::icall_stringwriter_write_u4", "u4")]
    [InlineData("Write", 0x0A, "il2cpp::runtime::icall_stringwriter_write_l", "l")]
    [InlineData("Write", 0x0B, "il2cpp::runtime::icall_stringwriter_write_u8", "q")]
    [InlineData("Write", 0x0D, "il2cpp::runtime::icall_stringwriter_write_d", "d")]
    [InlineData("Write", 0x02, "il2cpp::runtime::icall_stringwriter_write_b", "i")]
    [InlineData("Write", 0x1C, "il2cpp::runtime::icall_stringwriter_write_o", "")]
    [InlineData("WriteLine", 0x08, "il2cpp::runtime::icall_stringwriter_write_line_i", "i")]
    [InlineData("WriteLine", 0x09, "il2cpp::runtime::icall_stringwriter_write_line_u4", "u4")]
    [InlineData("WriteLine", 0x0A, "il2cpp::runtime::icall_stringwriter_write_line_l", "l")]
    [InlineData("WriteLine", 0x0B, "il2cpp::runtime::icall_stringwriter_write_line_u8", "q")]
    [InlineData("WriteLine", 0x0D, "il2cpp::runtime::icall_stringwriter_write_line_d", "d")]
    [InlineData("WriteLine", 0x02, "il2cpp::runtime::icall_stringwriter_write_line_b", "i")]
    [InlineData("WriteLine", 0x1C, "il2cpp::runtime::icall_stringwriter_write_line_o", "")]
    public void Resolve_StringWriter_ValueWrites_MapToTypedIcalls(string name, byte param, string cppName, string cast)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", name, Sig(0x01, param)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", cast }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StringWriter_WriteRange_MapsToArrayRangeIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "Write",
            new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x03, 0x08, 0x08 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_cs_range", m.CppName);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal(new[] { "", "a", "i", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StringWriter_WriteFormat_MapsToFormatIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "Write",
            new byte[] { 0x00, 0x02, 0x01, 0x0E, 0x1C }), "Write");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_format_1", m.CppName);
        Assert.Equal(2, m.ParamCount);
    }

    [Fact]
    public void Resolve_StringWriter_WriteChar_MapsToWriteC()
    {
        // Write(char) -- batch 150e: the "c" char16_t cast.
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "Write", Sig(0x01, 0x03)), "Write");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_c", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", "c" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StringWriter_WriteLineDefault_MapsToNewLineIcall()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteLine", Sig(0x01)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_line_default", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
    }

    [Fact]
    public void Resolve_StringWriter_DisposeBool_MapsToClose()
    {
        // Dispose(bool) -- batch 150e: the flag is dropped (Close).
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "Dispose", Sig(0x01, 0x02)), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "drop" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StringReader_GetHashCode_ThrowsLoudNotWired()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "StringReader", "GetHashCode", Sig(0x08)), "GetHashCode"));

        Assert.StartsWith("System.IO.StringReader::GetHashCode: ", ex.Message);
        Assert.Contains("member not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // The net48 async surface (synchronous-completion model): every returned
    // task is already completed. The reader entries wrap the synchronous
    // result in a completed Task<T>; the writer entries run the write and
    // return a completed (non-generic) Task.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ReadToEndAsync", "il2cpp::runtime::icall_stringreader_read_to_end_async")]
    [InlineData("ReadLineAsync", "il2cpp::runtime::icall_stringreader_read_line_async")]
    public void Resolve_StringReader_AsyncZeroArg_MapsToCompletedTask(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", name,
            Blob(0x20, 0x00, 0x13, 0x00)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.Threading.Tasks.Task`1", m.ReturnTypeName);
    }

    [Theory]
    [InlineData("ReadAsync", "il2cpp::runtime::icall_stringreader_read_chars_async")]
    [InlineData("ReadBlockAsync", "il2cpp::runtime::icall_stringreader_read_block_async")]
    public void Resolve_StringReader_AsyncArray_MapsToCompletedTask(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringReader", name,
            Blob(0x20, 0x03, 0x13, 0x00, 0x1D, 0x03, 0x08, 0x08)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal(new[] { "", "a", "i", "i" }, m.ParamTypeNames);
        Assert.Equal("System.Threading.Tasks.Task`1", m.ReturnTypeName);
    }

    [Theory]
    [InlineData("WriteAsync", 0x0E, "il2cpp::runtime::icall_stringwriter_write_async", "")]
    [InlineData("WriteAsync", 0x03, "il2cpp::runtime::icall_stringwriter_write_async_c", "c")]
    [InlineData("WriteAsync", 0x1D, "il2cpp::runtime::icall_stringwriter_write_async_cs", "a")]
    public void Resolve_StringWriter_WriteAsync_MapsToCompletedTask(string name, byte param, string cppName, string cast)
    {
        var sig = param == 0x1D
            ? Blob(0x20, 0x01, 0x13, 0x00, 0x1D, 0x03)
            : Blob(0x20, 0x01, 0x13, 0x00, param);
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", name, sig), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", cast }, m.ParamTypeNames);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringWriter_WriteAsyncRange_MapsToCompletedTask()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteAsync",
            Blob(0x20, 0x03, 0x13, 0x00, 0x1D, 0x03, 0x08, 0x08)), "WriteAsync");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_async_cs_range", m.CppName);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringWriter_WriteLineAsyncDefault_MapsToCompletedTask()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteLineAsync",
            Blob(0x20, 0x00, 0x13, 0x00)), "WriteLineAsync");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_line_async_default", m.CppName);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Theory]
    [InlineData(0x0E, "il2cpp::runtime::icall_stringwriter_write_line_async")]
    [InlineData(0x03, "il2cpp::runtime::icall_stringwriter_write_line_async_c")]
    public void Resolve_StringWriter_WriteLineAsyncScalar_MapsToCompletedTask(byte param, string cppName)
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteLineAsync",
            Blob(0x20, 0x01, 0x13, 0x00, param)), "WriteLineAsync");

        Assert.Equal(cppName, m.CppName);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringWriter_WriteLineAsyncChars_MapsToCompletedTask()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteLineAsync",
            Blob(0x20, 0x01, 0x13, 0x00, 0x1D, 0x03)), "WriteLineAsync");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_line_async_cs", m.CppName);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringWriter_WriteLineAsyncRange_MapsToCompletedTask()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "WriteLineAsync",
            Blob(0x20, 0x03, 0x13, 0x00, 0x1D, 0x03, 0x08, 0x08)), "WriteLineAsync");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_write_line_async_cs_range", m.CppName);
        Assert.Equal(3, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringWriter_FlushAsync_MapsToCompletedTask()
    {
        var m = ResolveMember(Synthesize("System.IO", "StringWriter", "FlushAsync",
            Blob(0x20, 0x00, 0x13, 0x00)), "FlushAsync");

        Assert.Equal("il2cpp::runtime::icall_stringwriter_flush_async", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal("System.Threading.Tasks.Task", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // LOUD-whitelist self-identification (2026-10-26). Every net48
    // StringReader/StringWriter member is wired (see MethodMap.StringIO.cs);
    // the surviving catch-alls are shapes net48 does not define. Each must
    // announce its whitelist category + the net48 Reference Source line it was
    // checked against.
    //--------------------------------------------------------------------------
    public static IEnumerable<object[]> LoudWhitelistShapes() => new[]
    {
        new object[] { "Read",      new byte[] { 0x08 } },                     // StringReader.Read(int) -- no such overload
        new object[] { "ReadBlock", new byte[] { 0x08 } },                     // StringReader.ReadBlock(int) -- no such overload
        new object[] { "Write",     new byte[] { 0x0E, 0x1C, 0x1C, 0x1C, 0x1C } }, // 5 args
        new object[] { "WriteLine", new byte[] { 0x0E, 0x1C, 0x1C, 0x1C, 0x1C } }, // 5 args
        new object[] { "Dispose",   new byte[] { 0x02, 0x02 } },               // 2 args
    };

    [Theory]
    [MemberData(nameof(LoudWhitelistShapes))]
    public void Resolve_CatchAlls_AreSelfIdentifyingLoudWhitelists(string name, byte[] ps)
    {
        var owner = name == "Read" || name == "ReadBlock" ? "StringReader" : "StringWriter";
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", owner, name, Sig(0x01, ps)), name));

        Assert.Contains("LOUD whitelist (", ex.Message);
        Assert.Contains("net48 RefSrc mscorlib/system/io/", ex.Message);
    }

    [Fact]
    public void Resolve_DefaultMember_IsSelfIdentifyingLoudWhitelist()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "StringReader", "GetHashCode", Sig(0x08)), "GetHashCode"));

        Assert.Contains("LOUD whitelist (", ex.Message);
        Assert.Contains("net48 RefSrc mscorlib/system/io/", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.VersionUri.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null,
        string? arg0Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return receiverType == null
            ? MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
                null, false, arg0Type)
            : MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
                receiverType, false, arg0Type);
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

    private static byte[] Blob(params byte[] bytes) => bytes;

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

