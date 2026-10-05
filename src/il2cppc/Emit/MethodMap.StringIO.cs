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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                return MapStringIoCtor(owner, ps, count);
            case "Read":
                if ((owner == "System.IO.StringReader" || owner == "System.IO.TextReader") && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read", 0, true, false, true);
                if ((owner == "System.IO.StringReader" || owner == "System.IO.TextReader") && count == 3)
                    // Read(char[], int, int) -- batch 150e: the managed-array
                    // shaped icall (buffer "a", offset/count "i").
                    return I("il2cpp::runtime::icall_stringreader_read_chars", 3, true, false, true,
                        new[] { "", "a", "i", "i" });
                if (owner == "System.IO.StringReader" || owner == "System.IO.TextReader")
                    throw new NotSupportedException(
                        "StringReader.Read with an unsupported arity not wired");
                return null;
            case "Peek":
                if ((owner == "System.IO.StringReader" || owner == "System.IO.TextReader") && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_peek", 0, true, false, true);
                return null;
            case "ReadLine":
                if ((owner == "System.IO.StringReader" || owner == "System.IO.TextReader") && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_line", 0, true, true, true);
                return null;
            case "ReadToEnd":
                if ((owner == "System.IO.StringReader" || owner == "System.IO.TextReader") && count == 0)
                    return I("il2cpp::runtime::icall_stringreader_read_to_end", 0, true, true, true);
                return null;
            case "Write":
                if ((owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter") && count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stringwriter_write", 1, false, false, true);
                if ((owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter") && count == 1 && ps[0] == 0x03)
                    // Write(char) -- batch 150e: the "c" char16_t cast.
                    return I("il2cpp::runtime::icall_stringwriter_write_c", 1, false, false, true,
                        new[] { "", "c" });
                if ((owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter") && count == 1 && ps[0] == 0x1D)
                    // Write(char[]) -- batch 150e: the "a" array cast.
                    return I("il2cpp::runtime::icall_stringwriter_write_cs", 1, false, false, true,
                        new[] { "", "a" });
                if (owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter")
                    throw new NotSupportedException(
                        "StringWriter.Write with an unsupported arity/type not wired");
                return null;
            case "WriteLine":
                if ((owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter") && count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stringwriter_write_line", 1, false, false, true);
                if (owner == "System.IO.StringWriter" || owner == "System.IO.TextWriter")
                    throw new NotSupportedException(
                        "StringWriter.WriteLine with this arity not wired: the runtime icall takes exactly (self, string); the 0-arg form would need a null-text variant");
                return null;
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
                throw new NotSupportedException("Dispose with an unsupported arity not wired");
            default:
                throw new NotSupportedException(
                    "member not wired: the object icalls cover " +
                    "ctor/Read/Peek/ReadLine/ReadToEnd/Write/WriteLine/ToString/Close only " +
                    "(see stringrw.h)");
        }
    }

    // Constructors: the runtime icalls are OBJECT-TYPED (self first), so the
    // emitter's (receiver, args...) call shape matches exactly -- unlike the
    // stream domain, whose open/create icalls are handle factories.
    private static ResolvedMethod? MapStringIoCtor(string owner, byte[] ps, int count)
    {
        if (owner == "System.IO.StringReader")
        {
            if (count == 1 && ps[0] == 0x0E)
                return I("il2cpp::runtime::icall_stringreader_ctor", 1, false, false, true);
            throw new NotSupportedException($"StringReader ctor with {count} param(s) not wired (expected exactly (string))");
        }
        if (owner == "System.IO.StringWriter")
        {
            if (count == 0)
                return I("il2cpp::runtime::icall_stringwriter_ctor", 0, false, false, true);
            throw new NotSupportedException(
                "StringWriter.ctor(IFormatProvider) not wired: the provider is ignored by this domain and no provider-taking ctor icall exists");
        }
        return null;
    }
}
