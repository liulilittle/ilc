namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StringIO -- System.IO.StringReader / StringWriter mappings
// (src/runtime/include/internal/stringrw.h -- il2cpp_stringreader_* /
// il2cpp_stringwriter_* entry points). Owners: System.IO.StringReader,
// System.IO.StringWriter
//
// RUNTIME OBJECT CLOSURE (M6): StringReader / StringWriter are managed
// OBJECTS -- the runtime defines the System_IO_StringReader_class /
// System_IO_StringWriter_class descriptors (Il2CppStringReaderObject: header
// + wrapped-string ref (bitmap bit 1) + backend handle; Il2CppStringWriterObject:
// header + backend handle, stringrw.h) and the object icalls below own the
// backend handle. Generated newobj allocates through
// il2cpp_object_new(&System_IO_StringReader_class) -- the BclAllocExpr
// fallback in CodeGenerator.cs emits exactly this mangled global symbol, so
// NO BclAllocExpr entry is required (the same contract as System.Version).
//
//   StringReader.ctor(string)    -> icall_stringreader_ctor (null text ->
//                                   ArgumentNullException)
//   StringReader.Read()          -> icall_stringreader_read (int32, -1 at EOF)
//   StringReader.Peek()          -> icall_stringreader_peek (int32, -1 at EOF)
//   StringReader.ReadLine()      -> icall_stringreader_read_line (string)
//   StringReader.ReadToEnd()     -> icall_stringreader_read_to_end (string)
//   StringReader.Close()/Dispose()-> icall_stringreader_close
//   StringWriter.ctor()          -> icall_stringwriter_ctor
//   StringWriter.Write(string)   -> icall_stringwriter_write (null is no-op)
//   StringWriter.WriteLine(string)-> icall_stringwriter_write_line (+CRLF)
//   StringWriter.ToString()      -> icall_stringwriter_to_string (copy)
//   StringWriter.Close()/Dispose()-> icall_stringwriter_close
//
// Members without an object icall stay LOUD -- wiring manual convention,
// MethodRefResolver.cs: "known but unwireable members MUST throw":
//   * StringReader.Read(char[], int, int): needs a managed-array shaped
//     icall (buffer + offset + count), like the stream Read(void*, count).
//   * StringWriter.Write(char) / Write(char[]) / WriteLine(char) / ...
//     the icalls take exactly (self, string).
//   * StringWriter.WriteLine(): the icall takes (self, string); the 0-arg
//     form would need a null-text variant.
//   * StringWriter.ctor(IFormatProvider): the provider is ignored by this
//     domain; a provider-taking ctor icall does not exist.
//   * Dispose(bool): the runtime has only Close (no disposing flag).
//   * Use-after-close: net48 throws ObjectDisposedException; this runtime
//     follows the M3 result conventions (return values: -1 / null) -- a
//     documented divergence, see stringrw.h.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStringIO(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? paramTypes = null)
    {
        var reader = owner == "System.IO.StringReader" || owner == "System.IO.TextReader";
        var writer = owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter";
        switch (name)
        {
            case ".ctor":
                return MapStringIoCtor(owner, ps, count, arg0Type, paramTypes);
            case "Read":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read", 0, true, false, true);
                if (reader && count == 3)
                    // Read(char[], int, int) -- batch 150e: the managed-array
                    // shaped icall (buffer "a", offset/count "i").
                    return I("il2cpp::runtime::icall_stringreader_read_chars", 3, true, false, true,
                        new[] { "", "a", "i", "i" });
                if (reader)
                    throw new NotSupportedException(
                        "StringReader.Read with an unsupported arity not wired -- LOUD whitelist (net48 StringReader.Read has only () and (char[],int,int)); net48 RefSrc mscorlib/system/io/stringreader.cs:73,85");
                return null;
            // ReadBlock(char[], int, int): the same buffer shape as Read, but
            // the runtime loops until the window is full or EOF (net48).
            case "ReadBlock":
                if (reader && count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringreader_read_block", 3, true, false, true,
                        new[] { "", "a", "i", "i" });
                if (reader)
                    throw new NotSupportedException(
                        "StringReader.ReadBlock with an unsupported arity not wired -- LOUD whitelist (net48 TextReader.ReadBlock has only (char[],int,int)); net48 RefSrc mscorlib/system/io/textreader.cs:159");
                return null;
            case "Peek":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_peek", 0, true, false, true);
                return null;
            case "ReadLine":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_line", 0, true, true, true);
                return null;
            case "ReadToEnd":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_to_end", 0, true, true, true);
                return null;
            case "Write":
                if (!writer) return null;
                // Write(value) -- the TextWriter convenience overloads all
                // render the value's net48 ToString text into the builder.
                if (count == 1)
                {
                    switch (ps[0])
                    {
                        case 0x0E: return I("il2cpp::runtime::icall_stringwriter_write", 1, false, false, true);
                        case 0x03: return I("il2cpp::runtime::icall_stringwriter_write_c", 1, false, false, true, new[] { "", "c" });
                        case 0x1D: return I("il2cpp::runtime::icall_stringwriter_write_cs", 1, false, false, true, new[] { "", "a" });
                        case 0x02: return I("il2cpp::runtime::icall_stringwriter_write_b", 1, false, false, true, new[] { "", "i" });
                        case 0x04 or 0x05 or 0x06 or 0x07 or 0x08:
                            return I("il2cpp::runtime::icall_stringwriter_write_i", 1, false, false, true, new[] { "", "i" });
                        case 0x09: return I("il2cpp::runtime::icall_stringwriter_write_u4", 1, false, false, true, new[] { "", "u4" });
                        case 0x0A: return I("il2cpp::runtime::icall_stringwriter_write_l", 1, false, false, true, new[] { "", "l" });
                        case 0x0B: return I("il2cpp::runtime::icall_stringwriter_write_u8", 1, false, false, true, new[] { "", "q" });
                        case 0x0C or 0x0D: return I("il2cpp::runtime::icall_stringwriter_write_d", 1, false, false, true, new[] { "", "d" });
                        case 0x11: return I("il2cpp::runtime::icall_stringwriter_write_o", 1, false, false, true, new[] { "", "box:dec" });
                        case 0x1C: return I("il2cpp::runtime::icall_stringwriter_write_o", 1, false, false, true, new[] { "", "" });
                    }
                }
                // Write(char[]/string, int, int) -- the window forms.
                if (count == 3 && ps[1] == 0x08 && ps[2] == 0x08)
                {
                    if (ps[0] == 0x1D)
                        return I("il2cpp::runtime::icall_stringwriter_write_cs_range", 3, false, false, true,
                            new[] { "", "a", "i", "i" });
                    if (ps[0] == 0x0E)
                        return I("il2cpp::runtime::icall_stringwriter_write_s_range", 3, false, false, true,
                            new[] { "", "", "i", "i" });
                }
                // Write(string, object) / (string, object, object) /
                // (string, object, object, object) / (string, params object[]).
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_format_1", 2, false, false, true);
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_stringwriter_write_format", 2, false, false, true, new[] { "", "", "a" });
                if (count == 3 && ps[0] == 0x0E && ps[1] == 0x1C && ps[2] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_format_2", 3, false, false, true);
                if (count == 4 && ps[0] == 0x0E && ps[1] == 0x1C && ps[2] == 0x1C && ps[3] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_format_3", 4, false, false, true);
                throw new NotSupportedException(
                    "StringWriter.Write with an unsupported arity/type not wired -- LOUD whitelist (net48 TextWriter.Write families are bool/char/char[]/decimal/double/short/int/long/float/sbyte/string/uint/ulong/ushort/object plus the (char[],int,int) and composite-format forms); net48 RefSrc mscorlib/system/io/textwriter.cs:193-350");
            case "WriteLine":
                if (!writer) return null;
                if (count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_default", 0, false, false, true);
                if (count == 1)
                {
                    switch (ps[0])
                    {
                        case 0x0E: return I("il2cpp::runtime::icall_stringwriter_write_line", 1, false, false, true);
                        case 0x03: return I("il2cpp::runtime::icall_stringwriter_write_line_c", 1, false, false, true, new[] { "", "c" });
                        case 0x1D: return I("il2cpp::runtime::icall_stringwriter_write_line_cs", 1, false, false, true, new[] { "", "a" });
                        case 0x02: return I("il2cpp::runtime::icall_stringwriter_write_line_b", 1, false, false, true, new[] { "", "i" });
                        case 0x04 or 0x05 or 0x06 or 0x07 or 0x08:
                            return I("il2cpp::runtime::icall_stringwriter_write_line_i", 1, false, false, true, new[] { "", "i" });
                        case 0x09: return I("il2cpp::runtime::icall_stringwriter_write_line_u4", 1, false, false, true, new[] { "", "u4" });
                        case 0x0A: return I("il2cpp::runtime::icall_stringwriter_write_line_l", 1, false, false, true, new[] { "", "l" });
                        case 0x0B: return I("il2cpp::runtime::icall_stringwriter_write_line_u8", 1, false, false, true, new[] { "", "q" });
                        case 0x0C or 0x0D: return I("il2cpp::runtime::icall_stringwriter_write_line_d", 1, false, false, true, new[] { "", "d" });
                        case 0x11: return I("il2cpp::runtime::icall_stringwriter_write_line_o", 1, false, false, true, new[] { "", "box:dec" });
                        case 0x1C: return I("il2cpp::runtime::icall_stringwriter_write_line_o", 1, false, false, true, new[] { "", "" });
                    }
                }
                if (count == 3 && ps[1] == 0x08 && ps[2] == 0x08)
                {
                    if (ps[0] == 0x1D)
                        return I("il2cpp::runtime::icall_stringwriter_write_line_cs_range", 3, false, false, true,
                            new[] { "", "a", "i", "i" });
                    if (ps[0] == 0x0E)
                        return I("il2cpp::runtime::icall_stringwriter_write_line_s_range", 3, false, false, true,
                            new[] { "", "", "i", "i" });
                }
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_format_1", 2, false, false, true);
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_format", 2, false, false, true, new[] { "", "", "a" });
                if (count == 3 && ps[0] == 0x0E && ps[1] == 0x1C && ps[2] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_format_2", 3, false, false, true);
                if (count == 4 && ps[0] == 0x0E && ps[1] == 0x1C && ps[2] == 0x1C && ps[3] == 0x1C)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_format_3", 4, false, false, true);
                throw new NotSupportedException(
                    "StringWriter.WriteLine with an unsupported arity/type not wired -- LOUD whitelist (net48 TextWriter.WriteLine families are the Write value families plus () and the composite-format forms); net48 RefSrc mscorlib/system/io/textwriter.cs:360-532");
            case "ToString":
                if (owner == "System.IO.StringWriter" && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_to_string", 0, true, true, true);
                return null;
            case "Close":
                if (count == 0)
                    // receiverType (the tracked concrete wrapper) decides the
                    // reader/writer side -- the owner is the shared
                    // TextReader/TextWriter base for the inherited members.
                    return I(owner == "System.IO.StringReader" || receiverType == "System.IO.StringReader"
                        ? "il2cpp::runtime::icall_stringreader_close"
                        : "il2cpp::runtime::icall_stringwriter_close", 0, false, false, true);
                return null;
            case "Dispose":
                if (count == 0) return I(owner == "System.IO.StringReader" || receiverType == "System.IO.StringReader"
                    ? "il2cpp::runtime::icall_stringreader_close"
                    : "il2cpp::runtime::icall_stringwriter_close", 0, false, false, true);
                // Dispose(bool) -- batch 150e: the flag is dropped (Close is
                // idempotent; disposing=false is a no-op either way).
                if (count == 1) return I(owner == "System.IO.StringReader" || receiverType == "System.IO.StringReader"
                    ? "il2cpp::runtime::icall_stringreader_close"
                    : "il2cpp::runtime::icall_stringwriter_close", 1, false, false, true, new[] { "drop" });
                throw new NotSupportedException("Dispose with an unsupported arity not wired -- LOUD whitelist (net48 TextReader/TextWriter.Dispose has only () and (bool)); net48 RefSrc mscorlib/system/io/textreader.cs:76");
            // StringWriter.GetStringBuilder(): the backing builder instance
            // (the net48 identity contract).
            case "GetStringBuilder":
                if (owner == "System.IO.StringWriter" && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_get_string_builder", 0, true, true, true,
                        returnTypeName: "System.Text.StringBuilder");
                return null;
            // StringWriter.Encoding: net48 returns Encoding.Unicode (UTF-16).
            case "get_Encoding":
                if (writer && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_get_encoding", 0, true, true, true,
                        returnTypeName: "System.Text.UnicodeEncoding");
                return null;
            // StringWriter.FormatProvider: the ctor provider, or
            // CurrentCulture when the provider was omitted.
            case "get_FormatProvider":
                if (writer && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_get_format_provider", 0, true, true, true,
                        returnTypeName: "System.IFormatProvider");
                return null;
            // NewLine get/set: the WriteLine terminator.
            case "get_NewLine":
                if (writer && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_get_newline", 0, true, true, true,
                        returnTypeName: "System.String");
                return null;
            case "set_NewLine":
                if (writer && count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stringwriter_set_newline", 1, false, false, true);
                return null;
            // Flush(): a no-op for the in-memory writer.
            case "Flush":
                if (writer && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_flush", 0, false, false, true);
                return null;
            // The net48 async surface: every returned task is COMPLETED (the
            // synchronous-completion model), so the entries run the
            // synchronous operation and wrap the result.
            case "ReadToEndAsync":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_to_end_async", 0, true, true, true,
                        returnTypeName: "System.Threading.Tasks.Task`1");
                return null;
            case "ReadLineAsync":
                if (reader && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_line_async", 0, true, true, true,
                        returnTypeName: "System.Threading.Tasks.Task`1");
                return null;
            case "ReadAsync":
                if (reader && count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringreader_read_chars_async", 3, true, true, true,
                        new[] { "", "a", "i", "i" },
                        returnTypeName: "System.Threading.Tasks.Task`1");
                return null;
            case "ReadBlockAsync":
                if (reader && count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringreader_read_block_async", 3, true, true, true,
                        new[] { "", "a", "i", "i" },
                        returnTypeName: "System.Threading.Tasks.Task`1");
                return null;
            case "WriteAsync":
                if (!writer) return null;
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stringwriter_write_async", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.Threading.Tasks.Task");
                if (count == 1 && ps[0] == 0x03)
                    return I("il2cpp::runtime::icall_stringwriter_write_async_c", 1, true, true, true,
                        new[] { "", "c" }, returnTypeName: "System.Threading.Tasks.Task");
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_stringwriter_write_async_cs", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.Threading.Tasks.Task");
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringwriter_write_async_cs_range", 3, true, true, true,
                        new[] { "", "a", "i", "i" },
                        returnTypeName: "System.Threading.Tasks.Task");
                return null;
            case "WriteLineAsync":
                if (!writer) return null;
                if (count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_async_default", 0, true, true, true,
                        returnTypeName: "System.Threading.Tasks.Task");
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_async", 1, true, true, true,
                        returnTypeName: "System.Threading.Tasks.Task");
                if (count == 1 && ps[0] == 0x03)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_async_c", 1, true, true, true,
                        new[] { "", "c" }, returnTypeName: "System.Threading.Tasks.Task");
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_async_cs", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.Threading.Tasks.Task");
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringwriter_write_line_async_cs_range", 3, true, true, true,
                        new[] { "", "a", "i", "i" },
                        returnTypeName: "System.Threading.Tasks.Task");
                return null;
            case "FlushAsync":
                if (writer && count == 0)
                    return I("il2cpp::runtime::icall_stringwriter_flush_async", 0, true, true, true,
                        returnTypeName: "System.Threading.Tasks.Task");
                return null;
            default:
                throw new NotSupportedException(
                    "member not wired: the object icalls cover " +
                    "ctor/Read/ReadBlock/Peek/ReadLine/ReadToEnd/Write/WriteLine/ToString/" +
                    "GetStringBuilder/Encoding/FormatProvider/NewLine/Flush/Close only " +
                    "(see stringrw.h) -- LOUD whitelist (the member is not part of the net48 StringReader/StringWriter object-icall surface); net48 RefSrc mscorlib/system/io/stringreader.cs:46-178, mscorlib/system/io/stringwriter.cs:65-198");
        }
    }

    // Constructors: the runtime icalls are OBJECT-TYPED (self first), so the
    // emitter's (receiver, args...) shape matches exactly -- unlike the
    // stream domain, whose open/create icalls are handle factories. The two
    // 0x12 CLASS shapes (IFormatProvider vs StringBuilder) are told apart by
    // the tracked argument type (arg0Type): a StringBuilder argument selects
    // the builder-backed ctor; everything else is the provider ctor.
    private static ResolvedMethod? MapStringIoCtor(string owner, byte[] ps, int count, string? arg0Type, string? paramTypes)
    {
        if (owner == "System.IO.StringReader")
        {
            if (count == 1 && ps[0] == 0x0E)
                return I("il2cpp::runtime::icall_stringreader_ctor", 1, false, false, true);
            throw new NotSupportedException($"StringReader ctor with {count} param(s) not wired (expected exactly (string)) -- LOUD whitelist (net48 StringReader has only the (string) ctor); net48 RefSrc mscorlib/system/io/stringreader.cs:36");
        }
        if (owner == "System.IO.StringWriter")
        {
            if (count == 0)
                return I("il2cpp::runtime::icall_stringwriter_ctor", 0, false, false, true);
            // The declared parameter type is authoritative (the arg0Type
            // hint is often absent for ctor arguments); the tracked argument
            // type is the fallback for a signature walk that omits it.
            var builderArg = (paramTypes != null && paramTypes.Contains("System.Text.StringBuilder"))
                || arg0Type == "System.Text.StringBuilder";
            if (count == 1 && ps[0] == 0x12)
            {
                if (builderArg)
                    return I("il2cpp::runtime::icall_stringwriter_ctor_sb", 1, false, false, true);
                // IFormatProvider (or a concrete culture): the provider is
                // stored and exposed through FormatProvider. The decoded
                // parameter type settles the shape; a hint-less synthetic
                // signature is genuinely ambiguous and stays LOUD.
                if (paramTypes == null && arg0Type == null)
                    throw new NotSupportedException(
                        "StringWriter ctor class argument is ambiguous (StringBuilder vs IFormatProvider) without a declared/tracked type -- LOUD whitelist (the declared parameter type is unavailable, so StringBuilder vs IFormatProvider cannot be distinguished); net48 RefSrc mscorlib/system/io/stringwriter.cs:48,54,57");
                return I("il2cpp::runtime::icall_stringwriter_ctor_provider", 1, false, false, true);
            }
            if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
            {
                if (!builderArg)
                    throw new NotSupportedException(
                        "StringWriter(StringBuilder, IFormatProvider) requires a tracked StringBuilder first argument -- LOUD whitelist (the StringBuilder first argument's tracked type is unavailable); net48 RefSrc mscorlib/system/io/stringwriter.cs:57");
                return I("il2cpp::runtime::icall_stringwriter_ctor_sb_provider", 2, false, false, true);
            }
            throw new NotSupportedException(
                $"StringWriter ctor with {count} param(s) not wired (expected (), (IFormatProvider), (StringBuilder) or (StringBuilder, IFormatProvider)) -- LOUD whitelist (net48 StringWriter ctors are only (), (IFormatProvider), (StringBuilder) and (StringBuilder,IFormatProvider)); net48 RefSrc mscorlib/system/io/stringwriter.cs:43-57");
        }
        return null;
    }
}



