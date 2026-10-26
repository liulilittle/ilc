namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StreamIO -- System.IO stream icall mappings (streamio.h).
// Owners: System.IO.Stream / FileStream / MemoryStream / StreamReader / StreamWriter
//
// OBJECT MODEL (stream-b23 runtime closure): the runtime reshaped the
// stream layer to OBJECT-typed icalls (il2cpp_streamio.h) -- 4 stream class
// descriptors (System_IO_FileStream_class / MemoryStream / StreamReader /
// StreamWriter, shared Il2CppStreamObject { obj; int32_t handle; } layout)
// backed by a non-moving handle registry (g_streamSlots[256] -> StreamState*,
// WeakRef-table model). Generated newobj allocates through the BclAllocExpr
// fallback il2cpp_object_new(&System_IO_FileStream_class) (TypeCppName
// sanitizes "System.IO.FileStream" -> System_IO_FileStream -- no explicit
// BclAllocExpr entry needed).
//
// Wired surface:
//   .ctor(string, FileMode, FileAccess) -> icall_stream_file_ctor
//   .ctor(string, FileMode)              -> icall_stream_file_ctor2 (WAVE2:
//     runtime derives the access default Append -> Write, else ReadWrite)
//   .ctor()                              -> icall_stream_memory_ctor (MemoryStream)
//   .ctor(Stream)                        -> icall_stream_reader_ctor / writer_ctor
//   Flush/Close/Dispose()                -> icall_stream_{flush,close}
//   Position (get/set)                   -> icall_stream_{get,set}_position
//   Length / CanRead / CanWrite          -> icall_stream_get_{length,can_read,can_write}
//   CanTimeout / ReadTimeout / WriteTimeout -> icall_stream_get_can_timeout /
//     {get,set}_{read,write}_timeout (D-STO-1: the timeout surface -- net48
//     "timeouts not supported" shape: CanTimeout=false, the accessors throw)
//   StreamReader.ReadLine()              -> icall_stream_read_line (string)
//   StreamReader.ReadToEnd()             -> icall_stream_read_to_end (string;
//     D-STO-1 -- was the last unwired reader text member)
//   StreamWriter.WriteLine(string)       -> icall_stream_write_line
//   StreamWriter/TextWriter.Write(string) -> icall_stream_write_text (WAVE2:
//     UTF-8 text without the line terminator; null emits nothing)
//   MemoryStream.ToArray()               -> icall_stream_memory_to_array (byte[])
//   Read(byte[],int,int)                 -> icall_stream_read_bytes (int32 count)
//   Write(byte[],int,int)                -> icall_stream_write_bytes
//   ReadByte() / WriteByte(byte)         -> icall_stream_{read,write}_byte
//     (D-STP-3: the single-byte surface -- ReadByte returns -1 at EOF, net48)
//   Seek(long, SeekOrigin)               -> icall_stream_seek (returns new pos)
//   CanSeek / SetLength(long)            -> icall_stream_get_can_seek /
//     icall_stream_set_length (D-STP-2: the origin-aware seek + truncate/
//     extend surface -- was LOUD-unwired next to the absolute Position)
//
// All instance icalls take (Il2CppObject* self, ...) -- the managed receiver
// passes through as arg[0] with cast "" (the object IS the stream). byte[]
// params use the "a" cast (Il2CppArray*). The wrapper ctors share the
// wrapped stream's StreamState (refCount++) -- closing the wrapper closes
// the underlying file once (net48 semantics).
//
// Remaining LOUD rejections:
//   * StreamWriter(string, Encoding) / (Stream, Encoding) -- encoding ctors
//     need extra icall variants.
//   * Dispose(bool) -- the runtime has only Close (no disposing flag).
//   * WriteLine(non-string) -- the icall takes exactly (self, string).
//   * FileStream(FileShare/bufferSize/FileOptions) forms -- need extra icall
//     variants.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStreamIO(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? paramTypes = null)
    {
        // nb8 STDIN-WRITE: Process.StandardInput is a PID, not a genuine
        // StreamWriter (its get_StandardInput identity fold tracks the pseudo-type
        // "System.Diagnostics.Process.Stdin"). Defer to the Process domain
        // (MethodMap.Process.cs), which owns WriteLine/Write/Flush/Close/AutoFlush
        // for that receiver; the generic stream icalls would write to the wrong
        // (nonexistent) stream handle otherwise.
        if (receiverType == "System.Diagnostics.Process.Stdin")
        {
            return null;
        }
        switch (name)
        {
            // ---- constructors (owner-specific) --------------------------------
            case ".ctor":
                return MapStreamCtor(owner, ps, count);
            // ---- owner-specific members --------------------------------------
            case "ToArray":
                if (owner == "System.IO.MemoryStream")
                    return I("il2cpp::runtime::icall_stream_memory_to_array", 0, true, true, true, null, null, returnTypeName: "System.Byte[]");
                return null;
            case "ReadLine":
                // StreamReader.ReadLine(): the MemberRef may declare on the
                // TextReader base (csc emits the declaring type); the receiver
                // tracking disambiguates the stream wrapper.
                if (owner == "System.IO.StreamReader" || receiverType == "System.IO.StreamReader")
                    return I("il2cpp::runtime::icall_stream_read_line", 0, true, true, true, null, null, returnTypeName: "System.String");
                return null;
            case "ReadToEnd":
                // D-STO-1 (2026-08-11): StreamReader.ReadToEnd() -- the last
                // unwired reader text member. The runtime icall
                // icall_stream_read_to_end exists (streamio.cpp 1085) and
                // returns the remainder from the current position to EOF as a
                // UTF-8 managed string ("" at end-of-stream, NOT null -- net48).
                // Same owner/receiver disambiguation as ReadLine: the MemberRef
                // may declare on TextReader (ReadToEnd is inherited), so the
                // receiver tracking picks the stream wrapper.
                // NOTE: the System.Diagnostics.Process receiver shape
                // (Process.StandardOutput -- a PID, not a genuine StreamReader)
                // owns ReadToEnd in MethodMap.Process.cs, whose dispatch line
                // runs AFTER StreamIO -- return null here so the stdout-line
                // icall wins for that receiver.
                // nb7 PROC-STARTINFO: the StandardError shape carries the
                // distinct "System.Diagnostics.Process.Stderr" pseudo-type (the
                // get_StandardError identity fold), also deferred to the
                // Process domain's stderr drain.
                if (receiverType is "System.Diagnostics.Process" or "System.Diagnostics.Process.Stderr") return null;
                if (owner == "System.IO.StreamReader" || receiverType == "System.IO.StreamReader")
                    return I("il2cpp::runtime::icall_stream_read_to_end", 0, true, true, true, null, null, returnTypeName: "System.String");
                return null;
            case "WriteLine":
                // net48 TextWriter.WriteLine overload family. The value forms
                // render the value's net48 ToString text + CRLF (the runtime
                // icall_stream_write_line_* surface); the composite forms
                // expand through String.Format first. The char[] forms are
                // told apart from byte[] by the decoded parameter-type text
                // (the raw 0x1D byte carries no element type).
                if (owner is "System.IO.StreamWriter" or "System.IO.TextWriter"
                    || receiverType is "System.IO.StreamWriter")
                {
                    if (count == 0)
                        return I("il2cpp::runtime::icall_stream_write_line_default", 0, false, false, true);
                    if (count == 1)
                    {
                        switch (ps[0])
                        {
                            case 0x0E: return I("il2cpp::runtime::icall_stream_write_line", 1, false, false, true);
                            case 0x03: return I("il2cpp::runtime::icall_stream_write_line_c", 1, false, false, true, new[] { "", "i" });
                            case 0x1D when StreamFirstParamIsCharArray(arg0Type, paramTypes):
                                return I("il2cpp::runtime::icall_stream_write_line_cs", 1, false, false, true, new[] { "", "a" });
                            case 0x02: return I("il2cpp::runtime::icall_stream_write_line_b", 1, false, false, true, new[] { "", "i" });
                            case 0x08: return I("il2cpp::runtime::icall_stream_write_line_i", 1, false, false, true, new[] { "", "i" });
                            case 0x09: return I("il2cpp::runtime::icall_stream_write_line_u4", 1, false, false, true, new[] { "", "u4" });
                            case 0x0A: return I("il2cpp::runtime::icall_stream_write_line_l", 1, false, false, true, new[] { "", "l" });
                            case 0x0B: return I("il2cpp::runtime::icall_stream_write_line_u8", 1, false, false, true, new[] { "", "q" });
                            case 0x0C: return I("il2cpp::runtime::icall_stream_write_line_f", 1, false, false, true, new[] { "", "d" });
                            case 0x0D: return I("il2cpp::runtime::icall_stream_write_line_d", 1, false, false, true, new[] { "", "d" });
                            case 0x11 when StreamFirstParamIsDecimal(paramTypes):
                                return I("il2cpp::runtime::icall_stream_write_line_dec", 1, false, false, true, new[] { "", "dec" });
                            case 0x1C: return I("il2cpp::runtime::icall_stream_write_line_o", 1, false, false, true);
                        }
                    }
                    if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                        && StreamFirstParamIsCharArray(arg0Type, paramTypes))
                        return I("il2cpp::runtime::icall_stream_write_line_cs_range", 3, false, false, true,
                            new[] { "", "a", "i", "i" });
                    if (count == 2 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_line_fmt1", 2, false, false, true);
                    if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                        return I("il2cpp::runtime::icall_stream_write_line_fmt_array", 2, false, false, true,
                            new[] { "", "", "a" });
                    if (count == 3 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_line_fmt2", 3, false, false, true);
                    if (count == 4 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12 && ps[3] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_line_fmt3", 4, false, false, true);
                }
                throw new NotSupportedException($"WriteLine with {count} arg(s) not wired: the runtime icalls take the net48 TextWriter.WriteLine family (string/value/char[]/composite)");
            // ---- shared Stream-base members ----------------------------------
            case "Flush": return I("il2cpp::runtime::icall_stream_flush", 0, false, false, true);
            case "Close": return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
            case "Dispose":
                if (count == 0) return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
                // Dispose(bool) -- batch 150f: the flag is dropped (Close is
                // idempotent; disposing=false is a no-op either way).
                if (count == 1) return I("il2cpp::runtime::icall_stream_close", 1, false, false, true, new[] { "drop" });
                // net48 Stream has exactly Dispose() and the protected
                // Dispose(bool) (Reference Source mscorlib/system/io/stream.cs:252)
                // -- any other arity is not a net48 overload (whitelist
                // category 4), so this guard is unreachable from net48 IL.
                throw new NotSupportedException("Dispose with an unsupported arity not wired: net48 has only Dispose() and the protected Dispose(bool) (stream.cs:252)");
            case "get_Position": return I("il2cpp::runtime::icall_stream_get_position", 0, true, false, true, null, "l");
            case "set_Position": return I("il2cpp::runtime::icall_stream_set_position", 1, false, false, true, new[] { "", "l" });
            case "get_Length": return I("il2cpp::runtime::icall_stream_get_length", 0, true, false, true, null, "l");
            case "get_CanRead": return I("il2cpp::runtime::icall_stream_get_can_read", 0, true, false, true);
            case "get_CanWrite": return I("il2cpp::runtime::icall_stream_get_can_write", 0, true, false, true);
            // D-STO-1 (2026-08-11): the timeout surface -- FileStream and
            // MemoryStream never support timeouts. CanTimeout is false; the
            // ReadTimeout / WriteTimeout accessors (get AND set) throw
            // InvalidOperationException (net48 probe-verified). The runtime
            // icalls icall_stream_get_can_timeout / {get,set}_{read,write}_timeout
            // exist (streamio.cpp 1111-1145); the transpiler map stopped at
            // CanRead/CanWrite, so the timeout shape was unreachable from C#.
            // All five return int32_t carriers (bool/int). Setters take the
            // int32 value with the "i" cast (receiver "" passthrough).
            case "get_CanTimeout": return I("il2cpp::runtime::icall_stream_get_can_timeout", 0, true, false, true);
            case "get_ReadTimeout": return I("il2cpp::runtime::icall_stream_get_read_timeout", 0, true, false, true);
            case "set_ReadTimeout": return I("il2cpp::runtime::icall_stream_set_read_timeout", 1, false, false, true, new[] { "", "i" });
            case "get_WriteTimeout": return I("il2cpp::runtime::icall_stream_get_write_timeout", 0, true, false, true);
            case "set_WriteTimeout": return I("il2cpp::runtime::icall_stream_set_write_timeout", 1, false, false, true, new[] { "", "i" });
            // D-STP-2 (2026-08-12): Seek / CanSeek / SetLength now wired -- the
            // runtime gained icall_stream_seek / get_can_seek / set_length
            // (streamio.cpp). SeekOrigin is an enum (VALUETYPE 0x11) carried
            // as int32 through the "i" cast; the offset is long ("l"); Seek
            // returns the new position (int64, "l" result kind).
            case "Seek":
                if (ps.Length == 2 && ps[0] == 0x0A && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_stream_seek", 2, true, false, true,
                        new[] { "", "l", "i" }, "l");
                // net48 has exactly Stream.Seek(long, SeekOrigin) (Reference
                // Source mscorlib/system/io/stream.cs:735) -- any other shape is
                // not a net48 overload (whitelist category 4).
                throw new NotSupportedException($"Seek with {count} arg(s) not wired: net48 has only (long, SeekOrigin) (stream.cs:735)");
            case "get_CanSeek":
                return I("il2cpp::runtime::icall_stream_get_can_seek", 0, true, false, true);
            case "SetLength":
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_stream_set_length", 1, false, false, true,
                        new[] { "", "l" });
                // net48 has exactly Stream.SetLength(long) (Reference Source
                // mscorlib/system/io/stream.cs:737) -- any other shape is not a
                // net48 overload (whitelist category 4).
                throw new NotSupportedException($"SetLength with {count} arg(s) not wired: net48 has only (long) (stream.cs:737)");
            case "Read":
                // TextReader.Read() (batch 2026-10-05, iosymprobe P1): the
                // 0-arg form consumes the next UTF-16 unit (or -1 at EOF).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_stream_read_char", 0, true, false, true);
                // managed: int Read(byte[] buffer, int offset, int count) --
                // the runtime managed-array icall (byte[] + offset/count,
                // returns the bytes read; 0 at EOF). The buffer is an
                // Il2CppArray* (the "a" cast); offset/count are int32.
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stream_read_bytes", 3, true, false, true,
                        new[] { "", "a", "", "" });
                // net48 has exactly Stream.Read(byte[],int,int) and the
                // TextReader.Read() 0-arg form (Reference Source
                // mscorlib/system/io/stream.cs:739, textreader.cs) -- any other
                // shape is not a net48 overload (whitelist category 4).
                throw new NotSupportedException($"Read with {count} arg(s) not wired: net48 has only (byte[],int,int) and the 0-arg text read (stream.cs:739)");
            case "Peek":
                // StreamReader.Peek() (batch 2026-10-05, iosymprobe P1 /
                // iopairprobe T_case): the next UTF-16 unit WITHOUT consuming
                // (-1 at end-of-stream, net48).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_stream_peek", 0, true, false, true);
                // net48 has exactly StreamReader.Peek() (Reference Source
                // mscorlib/system/io/streamreader.cs) -- any other shape is not a
                // net48 overload (whitelist category 4).
                throw new NotSupportedException($"Peek with {count} arg(s) not wired: net48 has only the 0-arg form (streamreader.cs)");
            case "get_Capacity":
                // MemoryStream.Capacity (batch 2026-10-05): the memory buffer
                // allocation size (int32).
                if (owner == "System.IO.MemoryStream" || receiverType == "System.IO.MemoryStream")
                    return I("il2cpp::runtime::icall_stream_memory_get_capacity", 0, true, false, true);
                return null;
            // io2 B1 (streammem2): MemoryStream.Capacity setter -- the runtime
            // icall validates (<Length -> AOOR, non-expandable -> NotSupported)
            // and resizes the owned buffer.
            case "set_Capacity":
                if (owner == "System.IO.MemoryStream" || receiverType == "System.IO.MemoryStream")
                    return I("il2cpp::runtime::icall_stream_memory_set_capacity", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            // io2 B1 (streammem2): MemoryStream.GetBuffer -- the underlying
            // buffer as byte[]. Only legal for the exposable ctors (otherwise
            // UnauthorizedAccessException, raised by the runtime icall). The
            // 1-arg CopyTo form appends the net48 default buffer size (4096).
            case "GetBuffer":
                if (owner == "System.IO.MemoryStream" || receiverType == "System.IO.MemoryStream")
                    return I("il2cpp::runtime::icall_stream_memory_get_buffer", 0, true, true, true,
                        returnTypeName: "System.Byte[]");
                return null;
            case "CopyTo":
                // Stream.CopyTo(Stream) -- the net48 default bufferSize is 4096
                // (appended as a literal; the buffer only affects chunking, not
                // the copied bytes).
                if (ps.Length == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_stream_copy_to", 1, false, false, true,
                        new[] { "", "" }, appendLiterals: new[] { "4096" });
                // Stream.CopyTo(Stream, int bufferSize).
                if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_stream_copy_to", 2, false, false, true,
                        new[] { "", "", "i" });
                // net48 has exactly Stream.CopyTo(Stream) and
                // CopyTo(Stream,int) (Reference Source
                // mscorlib/system/io/stream.cs:181,198) -- any other shape is not
                // a net48 overload (whitelist category 4).
                throw new NotSupportedException($"Stream.CopyTo with {count} arg(s) not wired: net48 has only (Stream) and (Stream,int) (stream.cs:181,198)");
            case "ReadByte":
                // D-STP-3 (2026-08-12): ReadByte() -- the runtime gained the
                // single-byte icall icall_stream_read_byte (streamio.cpp),
                // returning int (0..255) with -1 at end-of-stream (net48).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_stream_read_byte", 0, true, false, true);
                // net48 has exactly Stream.ReadByte() (Reference Source
                // mscorlib/system/io/stream.cs:747) -- any other shape is not a
                // net48 overload (whitelist category 4).
                throw new NotSupportedException($"ReadByte with {count} arg(s) not wired: net48 has only the 0-arg form (stream.cs:747)");
            case "Write":
                // WAVE2 + the net48 TextWriter.Write value family: the string
                // form is UTF-8 text WITHOUT the line terminator (net48 Write
                // vs WriteLine); the value forms render the value's ToString
                // text (the icall_stream_write_value_* surface); the composite
                // forms expand through String.Format. The char[] forms are
                // told apart from byte[] by the decoded parameter-type text.
                // StringWriter receivers route to StringIO before this arm
                // (receiverType dispatch in MethodRefResolver).
                if (owner is "System.IO.StreamWriter" or "System.IO.TextWriter"
                    || receiverType is "System.IO.StreamWriter")
                {
                    if (count == 1 && ps[0] == 0x0E)
                        return I("il2cpp::runtime::icall_stream_write_text", 1, false, false, true);
                    if (count == 1)
                    {
                        switch (ps[0])
                        {
                            case 0x03: return I("il2cpp::runtime::icall_stream_write_value_c", 1, false, false, true, new[] { "", "i" });
                            case 0x1D when StreamFirstParamIsCharArray(arg0Type, paramTypes):
                                return I("il2cpp::runtime::icall_stream_write_value_cs", 1, false, false, true, new[] { "", "a" });
                            case 0x02: return I("il2cpp::runtime::icall_stream_write_value_b", 1, false, false, true, new[] { "", "i" });
                            case 0x08: return I("il2cpp::runtime::icall_stream_write_value_i", 1, false, false, true, new[] { "", "i" });
                            case 0x09: return I("il2cpp::runtime::icall_stream_write_value_u4", 1, false, false, true, new[] { "", "u4" });
                            case 0x0A: return I("il2cpp::runtime::icall_stream_write_value_l", 1, false, false, true, new[] { "", "l" });
                            case 0x0B: return I("il2cpp::runtime::icall_stream_write_value_u8", 1, false, false, true, new[] { "", "q" });
                            case 0x0C: return I("il2cpp::runtime::icall_stream_write_value_f", 1, false, false, true, new[] { "", "d" });
                            case 0x0D: return I("il2cpp::runtime::icall_stream_write_value_d", 1, false, false, true, new[] { "", "d" });
                            case 0x11 when StreamFirstParamIsDecimal(paramTypes):
                                return I("il2cpp::runtime::icall_stream_write_value_dec", 1, false, false, true, new[] { "", "dec" });
                            case 0x1C: return I("il2cpp::runtime::icall_stream_write_value_o", 1, false, false, true);
                        }
                    }
                    if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                        && StreamFirstParamIsCharArray(arg0Type, paramTypes))
                        return I("il2cpp::runtime::icall_stream_write_value_cs_range", 3, false, false, true,
                            new[] { "", "a", "i", "i" });
                    if (count == 2 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_value_fmt1", 2, false, false, true);
                    if (count == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                        return I("il2cpp::runtime::icall_stream_write_value_fmt_array", 2, false, false, true,
                            new[] { "", "", "a" });
                    if (count == 3 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_value_fmt2", 3, false, false, true);
                    if (count == 4 && ps[0] == 0x0E && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12 && ps[3] is 0x1C or 0x12)
                        return I("il2cpp::runtime::icall_stream_write_value_fmt3", 4, false, false, true);
                }
                // Stream.Write(byte[], int, int) -- the managed-array byte
                // write (the Stream base form; any stream receiver). A char[]
                // first argument is the TextWriter form handled above.
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    && !StreamFirstParamIsCharArray(arg0Type, paramTypes))
                    return I("il2cpp::runtime::icall_stream_write_bytes", 3, false, false, true,
                        new[] { "", "a", "", "" });
                throw new NotSupportedException($"Write with {count} arg(s) not wired: the runtime icalls take the net48 TextWriter.Write family or Stream.Write(byte[], int32, int32)");
            case "WriteByte":
                // D-STP-3 (2026-08-12): WriteByte(byte) -- the runtime gained
                // the single-byte icall icall_stream_write_byte (streamio.cpp).
                // byte is ELEMENT_TYPE_U1 (0x05) -- an int32 slot, "i" cast.
                if (ps.Length == 1 && ps[0] == 0x05)
                    return I("il2cpp::runtime::icall_stream_write_byte", 1, false, false, true,
                        new[] { "", "i" });
                // net48 has exactly Stream.WriteByte(byte) (Reference Source
                // mscorlib/system/io/stream.cs:766) -- any other shape is not a
                // net48 overload (whitelist category 4).
                throw new NotSupportedException($"WriteByte with {count} arg(s) not wired: net48 has only (byte) (stream.cs:766)");
            default: return null;
        }
    }

    // The SZARRAY element type is not carried by the raw signature byte (0x1D
    // for both byte[] and char[]); the decoded parameter-type text is the
    // authority. char[] encodes as "r:c[]" (element byte 0x03), byte[] as
    // "r:u1[]". The tracked arg0Type hint is the fallback for a hint-less
    // signature.
    private static bool StreamFirstParamIsCharArray(string? arg0Type, string? paramTypes)
    {
        if (arg0Type == "System.Char[]") return true;
        if (paramTypes == null) return false;
        var first = paramTypes.Split(',')[0].Trim();
        return first is "r:c[]" or "r:System.Char[]";
    }

    // VALUETYPE first parameter: only System.Decimal has a Write/WriteLine
    // overload in the net48 TextWriter family.
    private static bool StreamFirstParamIsDecimal(string? paramTypes)
    {
        if (paramTypes == null) return false;
        return paramTypes.Split(',')[0].Trim() is "V:System.Decimal" or "r:System.Decimal";
    }

    // Object-typed ctor icalls: each stores the factory's handle into the
    // managed stream object (the emitter passes the receiver as arg[0] -- the
    // runtime signature is (Il2CppObject* self, ...), so the receiver rides in
    // the "" passthrough and pc counts the args AFTER self).
    private static ResolvedMethod? MapStreamCtor(string owner, byte[] ps, int count)
    {
        switch (owner)
        {
            case "System.IO.FileStream":
                // .ctor(string, FileMode, FileAccess): the runtime translates
                // the .NET FileMode (1..6) / FileAccess (1..3) ints to the
                // runtime mode 0..3 and opens via the platform dual path
                // (_wfopen / UTF-8 fopen). Missing file -> FileNotFoundException.
                // FileMode/FileAccess are enums -- RawSignature decodes them as
                // VALUETYPE (0x11 + TypeHandle); the runtime takes int32, so
                // the enum carriers pass through the "i" cast.
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11 && ps[2] is 0x08 or 0x11)
                    return I("il2cpp::runtime::icall_stream_file_ctor", 3, false, false, true,
                        new[] { "", "", "i", "i" });
                // WAVE2: FileStream(string path, FileMode fileMode) -- the
                // runtime derives the net48 access default (Append -> Write,
                // otherwise ReadWrite).
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11)
                    return I("il2cpp::runtime::icall_stream_file_ctor2", 2, false, false, true,
                        new[] { "", "", "i" });
                // batch 2026-10-05 (iosymprobe G11): FileStream(string,
                // FileMode, FileAccess, FileShare) -- the 4th arg (FileShare)
                // does not change the bytes / open mode on this runtime, so it
                // is dropped at the call site (the IL arity is preserved).
                if (ps.Length == 4 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11
                    && ps[2] is 0x08 or 0x11 && ps[3] is 0x08 or 0x11)
                    return I("il2cpp::runtime::icall_stream_file_ctor", 4, false, false, true,
                        new[] { "", "", "i", "i", "drop" });
                // binrw3/StreamIO: FileStream(string, FileMode, FileAccess,
                // FileShare, int bufferSize) -- the bufferSize only affects the
                // internal buffer, never the bytes / open mode -> dropped.
                if (ps.Length == 5 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11
                    && ps[2] is 0x08 or 0x11 && ps[3] is 0x08 or 0x11 && ps[4] == 0x08)
                    return I("il2cpp::runtime::icall_stream_file_ctor", 5, false, false, true,
                        new[] { "", "", "i", "i", "drop", "drop" });
                // FileStream(string, FileMode, FileAccess, FileShare, int,
                // FileOptions) and (..., bool useAsync): the 6th arg does not
                // change the bytes / open mode -> dropped.
                if (ps.Length == 6 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11
                    && ps[2] is 0x08 or 0x11 && ps[3] is 0x08 or 0x11 && ps[4] == 0x08
                    && ps[5] is 0x08 or 0x11 or 0x02)
                    return I("il2cpp::runtime::icall_stream_file_ctor", 6, false, false, true,
                        new[] { "", "", "i", "i", "drop", "drop", "drop" });
                throw new NotSupportedException(
                    $"FileStream ctor with {count} arg(s) not wired: the object-typed icalls take the " +
                    "(string path, FileMode[, FileAccess[, FileShare[, bufferSize[, FileOptions/useAsync]]]]) " +
                    "family -- the IntPtr/SafeFileHandle/FileSystemRights forms wrap an OS handle (machine state) " +
                    "and stay LOUD");
              case "System.IO.MemoryStream":
                  if (ps.Length == 0)
                      return I("il2cpp::runtime::icall_stream_memory_ctor", 0, false, false, true);
                  // P2 IO-symmetry (2026-09-19): the 1-arg ctors -- byte[]
                  // (0x1D SZARRAY -> "a" cast) ADOPTS the array; int32 sets the
                  // initial capacity. Both were LOUD before.
                  if (ps.Length == 1 && ps[0] == 0x1D)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_data", 1, false, false, true,
                          new[] { "", "a" });
                  if (ps.Length == 1 && ps[0] == 0x08)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_capacity", 1, false, false, true,
                          new[] { "", "i" });
                  // io2 B1 (streammem2): the rest of the net48 ctor family. The
                  // byte[] ctors (0x1D) adopt the caller's array (fixed size,
                  // non-exposable unless publiclyVisible); bool is 0x02, the
                  // window index/count are int32 (0x08).
                  if (ps.Length == 2 && ps[0] == 0x1D && ps[1] == 0x02)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_data_writable", 2, false, false, true,
                          new[] { "", "a", "i" });
                  if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_window", 3, false, false, true,
                          new[] { "", "a", "i", "i" });
                  if (ps.Length == 4 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x02)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_window_writable", 4, false, false, true,
                          new[] { "", "a", "i", "i", "i" });
                  if (ps.Length == 5 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x02 && ps[4] == 0x02)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_window_writable_public", 5, false, false, true,
                          new[] { "", "a", "i", "i", "i", "i" });
                   // net48 has exactly MemoryStream(), (int), (byte[]),
                   // (byte[],bool), (byte[],int,int), (byte[],int,int,bool) and
                   // (byte[],int,int,bool,bool) (Reference Source
                   // mscorlib/system/io/memorystream.cs:66-108) -- any other
                   // shape is not a net48 overload (whitelist category 4).
                   throw new NotSupportedException($"MemoryStream ctor with {count} arg(s) not wired: net48 has only (), (int), (byte[]), (byte[],bool), (byte[],int,int)[,bool[,bool]] (memorystream.cs:66-108)");
              case "System.IO.StreamReader":
                  if (ps.Length == 1 && ps[0] == 0x12)
                      return I("il2cpp::runtime::icall_stream_reader_ctor", 1, false, false, true);
                  // P2 IO-symmetry (2026-09-19): StreamReader(string path) --
                  // the runtime opens "rb" and BOM-detects (net48 contract).
                  if (ps.Length == 1 && ps[0] == 0x0E)
                      return I("il2cpp::runtime::icall_stream_reader_path_ctor", 1, false, false, true,
                          new[] { "", "" });
                  // binrw3/StreamIO: StreamReader(Stream, bool
                  // detectEncodingFromByteOrderMarks) / (string, bool) -- the
                  // flag is dropped (the runtime BOM-detects unconditionally,
                  // the net48 default).
                  if (ps.Length == 2 && ps[1] == 0x02)
                  {
                      if (ps[0] == 0x12)
                          return I("il2cpp::runtime::icall_stream_reader_ctor", 2, false, false, true,
                              new[] { "", "", "drop" });
                      if (ps[0] == 0x0E)
                          return I("il2cpp::runtime::icall_stream_reader_path_ctor", 2, false, false, true,
                              new[] { "", "", "drop" });
                  }
                  throw new NotSupportedException(
                      $"StreamReader ctor with {count} arg(s) not wired: the object-typed icalls take " +
                      "(Stream[, bool]), (string path[, bool]) -- the Encoding overloads need a runtime " +
                      "reader ctor that honours an explicit encoding (the BOM-only ctor cannot)");
            case "System.IO.StreamWriter":
                if (ps.Length == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_stream_writer_ctor", 1, false, false, true);
                // streamops: `new StreamWriter(file)` -- the runtime path ctor
                // opens FileMode.Create + FileAccess.Write ("wb" create/
                // truncate) and stores the handle on the writer object. The
                // path is a managed string (Il2CppObject*) -- passthrough "".
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stream_writer_path_ctor", 1, false, false, true,
                        new[] { "", "" });
                // P2(5) IO-symmetry (2026-09-19): the append / encoding path
                // ctors -- (string,bool) opens Create/Append, (string,bool,
                // Encoding) and (string,bool,Encoding,int) additionally select
                // the text encoder + its preamble. The runtime reads the
                // encoding's class name; bufferSize is validated (net48:
                // <= 0 -> ArgumentOutOfRangeException) and otherwise ignored
                // (it never changes the written bytes).
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                    return I("il2cpp::runtime::icall_stream_writer_path_append_ctor", 2, false, false, true,
                        new[] { "", "", "i" });
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x02 && ps[2] == 0x12)
                    return I("il2cpp::runtime::icall_stream_writer_path_encoding_ctor", 3, false, false, true,
                        new[] { "", "", "i", "" });
                if (ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x02 && ps[2] == 0x12 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stream_writer_path_encoding_ctor4", 4, false, false, true,
                        new[] { "", "", "i", "", "i" });
                throw new NotSupportedException(
                    $"StreamWriter ctor with {count} arg(s) not wired: the object-typed icall takes exactly " +
                    "(Stream), (string path), (string, bool append) or (string, bool, Encoding[, int bufferSize])");
            default: return null;
        }
    }
}
