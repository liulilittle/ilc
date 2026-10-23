using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Console dispatch tests: prove the System.Console domain
// (TryMapConsole in MethodMap.Console.cs) routes each WriteLine overload to
// the matching entry point.
//
//   WriteLine()                        -> icall_console_write_line_n
//   WriteLine(int)                     -> icall_console_write_line_i
//   WriteLine(uint)                    -> icall_console_write_line_u32
//                                        (full-range unsigned decimal; the
//                                        signed printer renders 0xFFFFFFFF as
//                                        -1 -- convcase L60/L61)
//   WriteLine(string)                  -> icall_console_write_line_s
//   WriteLine(bool)                    -> icall_console_write_line_b
//   WriteLine(char)                    -> icall_console_write_line_c
//   WriteLine(double | float)          -> icall_console_write_line_d
//   WriteLine(long)                    -> il2cpp_console_write_line_l
//                                        (generated-header inline helper:
//                                        icall_int64_to_string + write_line_s)
//   WriteLine(ulong)                   -> icall_console_write_line_u64
//                                        (runtime icall, console_format_u64:
//                                        the UInt64ToString digit loop)
//   WriteLine(object) [tracked box]    -> unboxed matching icall per box type
//   WriteLine(object) [boxed enum]     -> write_line_s(il2cpp_enum_to_string)
//                                        ("enum:l" cast: member name once the
//                                        enum metadata is registered, numeric
//                                        decimal otherwise -- box milestone)
//   Write(int)                         -> icall_console_write_i
//   Write(uint)                        -> icall_console_write_u32
//                                        (no-newline write_line_u32 variant:
//                                        full-range unsigned decimal, never
//                                        the signed printer's -1)
//
// UInt64 surface (box diagnostics work order 4): WriteLine(ulong) maps to the runtime
// icall so UInt64.MaxValue prints "18446744073709551615" instead of the
// int64 helper's -1; the "q" cast reinterprets the 8-byte slot. The
// BOXED-object path (WriteLine(object) with a UInt64 box) stays LOUD: the
// arg0Type switch has no System.UInt64 case (an unbox:q cast does not exist
// in the CodeGenerator cast table).
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x08=i4, 0x0A=i8, 0x0B=u8, 0x1C=object).
public class MethodMapConsoleTests
{
    //--------------------------------------------------------------------------
    // WriteLine(long) -> runtime int64 icall (0x0A = ELEMENT_TYPE_I8). misc3
    // moved the int64 arm off the generated-header inline helper onto the
    // runtime entry so every integer width shares one console formatter
    // family (console.cpp icall_console_write_line_l).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineInt64_MapsToInt64Icall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x0A)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_l", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
        Assert.True(resolved.IsConsoleWriteLine);
    }

    //--------------------------------------------------------------------------
    // WriteLine(ulong) -> runtime u64 icall (0x0B = ELEMENT_TYPE_U8): the "q"
    // cast reinterprets the 8-byte slot bit-preservingly, so UInt64.MaxValue
    // prints the full decimal -- never the int64 helper's -1.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineUInt64_MapsToUInt64Icall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x0B)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_u64", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "q" }, resolved.ParamTypeNames);
        Assert.True(resolved.IsConsoleWriteLine);
    }

    //--------------------------------------------------------------------------
    // WriteLine(uint) -> runtime u32 icall (0x09 = ELEMENT_TYPE_U4): the
    // full-range unsigned decimal (console_format_u64 via the uint32 printer)
    // so 0xFFFFFFFF prints "4294967295" -- never the signed printer's -1
    // (convcase L60/L61 regression).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineUInt32_MapsToUInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x09)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_u32", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.IsConsoleWriteLine);
    }

    //--------------------------------------------------------------------------
    // Write(uint) -> runtime no-newline u32 icall (0x09 = ELEMENT_TYPE_U4):
    // the write_line_u32 variant -- full-range unsigned decimal (console_
    // format_u64 via the uint32 printer) so 0xFFFFFFFF prints "4294967295",
    // never the signed printer's -1 (convcase Write probe); no trailing
    // newline (System.Console.Write semantics).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteUInt32_MapsToUInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "Write", Sig(0x01, 0x09)), "Write");

        Assert.Equal("il2cpp::runtime::icall_console_write_u32", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.IsConsoleWriteLine);
    }

    //--------------------------------------------------------------------------
    // WriteLine(object) with a tracked UInt64 box -> the u64 icall through the
    // "unbox:ul" cast. misc3 wired the 8-byte unsigned carrier, so the boxed
    // surface no longer has to stay LOUD: UInt64.MaxValue prints its full
    // decimal instead of the int64 helper's -1.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineUInt64Boxed_MapsToUInt64Icall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine",
            arg0Type: "System.UInt64");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_u64", resolved.CppName);
        Assert.Equal(new[] { "unbox:ul" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // WriteLine(object) with a TRACKED boxed enum (timecase work order 2, box
    // milestone) -> write_line_s with the "enum:l" cast: the emitted argument is
    // il2cpp_enum_to_string(nullptr, unbox_long(box)) -- the member name when
    // the enum metadata is registered + a name-bearing descriptor lands, the
    // Int32 no-metadata numeric path otherwise (documented forward ABI).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineBoxedEnum_MapsToEnumToStringViaWriteLineS()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine",
            arg0Type: "System.DayOfWeek");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_s", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "enum:l" }, resolved.ParamTypeNames);
        Assert.True(resolved.IsConsoleWriteLine);
    }

    //--------------------------------------------------------------------------
    // Boxed prims that have no unbox cast still render correctly: Int64 and
    // UInt32 go through their matching icall; Byte/SByte/Int16/UInt16 have no
    // cast in the CodeGenerator table but the runtime object printer
    // (valuesemantics.cpp icall_object_to_string) masks/sign-extends them
    // correctly, so they route to icall_console_write_line_object. Only a
    // WRONG path would be a runtime InvalidCast (unbox_long klass-checks).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Int64", "il2cpp::runtime::icall_console_write_line_l")]
    [InlineData("System.UInt32", "il2cpp::runtime::icall_console_write_line_u32")]
    [InlineData("System.Byte", "il2cpp::runtime::icall_console_write_line_object")]
    [InlineData("System.SByte", "il2cpp::runtime::icall_console_write_line_object")]
    [InlineData("System.Int16", "il2cpp::runtime::icall_console_write_line_object")]
    [InlineData("System.UInt16", "il2cpp::runtime::icall_console_write_line_object")]
    public void Resolve_Console_WriteLineBoxedPrim_MapsToCarrierOrObjectPrinter(string boxType, string expected)
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine",
            arg0Type: boxType);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // WriteLine(object) with an UNTRACKED box (hashtable2/hashtablecase gap:
    // a Hashtable get_Item result has no tracked box type, so arg0Type is
    // null) -> the runtime object printer icall_console_write_line_object
    // (il2cpp_console.h:89, M7 2026-08-09): wraps icall_object_to_string and
    // writes (boxed primitives print their VALUE text, strings print
    // themselves, other objects the type name, null prints only the line
    // terminator).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineUntrackedObject_MapsToObjectPrinter()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_object", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Debug.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? arg0Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, arg0Type);
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
