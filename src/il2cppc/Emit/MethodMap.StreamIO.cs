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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
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
                if ((owner == "System.IO.StreamWriter" || receiverType == "System.IO.StreamWriter")
                    && ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stream_write_line", 1, false, false, true);
                throw new NotSupportedException($"WriteLine with {count} arg(s) not wired: the runtime icall takes exactly (string)");
            // ---- shared Stream-base members ----------------------------------
            case "Flush": return I("il2cpp::runtime::icall_stream_flush", 0, false, false, true);
            case "Close": return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
            case "Dispose":
                if (count == 0) return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
                // Dispose(bool) -- batch 150f: the flag is dropped (Close is
                // idempotent; disposing=false is a no-op either way).
                if (count == 1) return I("il2cpp::runtime::icall_stream_close", 1, false, false, true, new[] { "drop" });
                throw new NotSupportedException("Dispose with an unsupported arity not wired");
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
                throw new NotSupportedException($"Seek with {count} arg(s) not wired: the runtime icall takes (long offset, SeekOrigin origin)");
            case "get_CanSeek":
                return I("il2cpp::runtime::icall_stream_get_can_seek", 0, true, false, true);
            case "SetLength":
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_stream_set_length", 1, false, false, true,
                        new[] { "", "l" });
                throw new NotSupportedException($"SetLength with {count} arg(s) not wired: the runtime icall takes (long value)");
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
                throw new NotSupportedException($"Read with {count} arg(s) not wired: the runtime managed-array icall takes (byte[], int32, int32) or the 0-arg text read");
            case "Peek":
                // StreamReader.Peek() (batch 2026-10-05, iosymprobe P1 /
                // iopairprobe T_case): the next UTF-16 unit WITHOUT consuming
                // (-1 at end-of-stream, net48).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_stream_peek", 0, true, false, true);
                throw new NotSupportedException($"Peek with {count} arg(s) not wired: the runtime icall takes no arguments");
            case "get_Capacity":
                // MemoryStream.Capacity (batch 2026-10-05): the memory buffer
                // allocation size (int32).
                if (owner == "System.IO.MemoryStream" || receiverType == "System.IO.MemoryStream")
                    return I("il2cpp::runtime::icall_stream_memory_get_capacity", 0, true, false, true);
                return null;
            case "ReadByte":
                // D-STP-3 (2026-08-12): ReadByte() -- the runtime gained the
                // single-byte icall icall_stream_read_byte (streamio.cpp),
                // returning int (0..255) with -1 at end-of-stream (net48).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_stream_read_byte", 0, true, false, true);
                throw new NotSupportedException($"ReadByte with {count} arg(s) not wired: the runtime icall takes no arguments");
            case "Write":
                // WAVE2: TextWriter/StreamWriter.Write(string) -- UTF-8 text
                // WITHOUT the line terminator (net48 Write vs WriteLine). The
                // MemberRef owner is the TextWriter BASE for both concrete and
                // base-typed stream-writer receivers; the RawSignature byte
                // 0x0E separates the string form from the still-LOUD
                // Write(char)(0x03)/Write(object) overloads. StringWriter
                // receivers route to StringIO before this arm (receiverType
                // dispatch in MethodRefResolver).
                if ((owner is "System.IO.StreamWriter" or "System.IO.TextWriter"
                        || receiverType is "System.IO.StreamWriter")
                    && ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_stream_write_text", 1, false, false, true);
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stream_write_bytes", 3, false, false, true,
                        new[] { "", "a", "", "" });
                throw new NotSupportedException($"Write with {count} arg(s) not wired: the runtime managed-array icall takes (byte[], int32, int32)");
            case "WriteByte":
                // D-STP-3 (2026-08-12): WriteByte(byte) -- the runtime gained
                // the single-byte icall icall_stream_write_byte (streamio.cpp).
                // byte is ELEMENT_TYPE_U1 (0x05) -- an int32 slot, "i" cast.
                if (ps.Length == 1 && ps[0] == 0x05)
                    return I("il2cpp::runtime::icall_stream_write_byte", 1, false, false, true,
                        new[] { "", "i" });
                throw new NotSupportedException($"WriteByte with {count} arg(s) not wired: the runtime icall takes (byte value)");
            default: return null;
        }
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
                throw new NotSupportedException(
                    $"FileStream ctor with {count} arg(s) not wired: the object-typed icalls take exactly " +
                    "(string path, int32 FileMode) or (string path, int32 FileMode, int32 FileAccess) -- " +
                    "the other overloads (FileShare/bufferSize/FileOptions forms) need extra icall variants");
              case "System.IO.MemoryStream":
                  if (ps.Length == 0)
                      return I("il2cpp::runtime::icall_stream_memory_ctor", 0, false, false, true);
                  // P2 IO-symmetry (2026-09-19): the 1-arg ctors -- byte[]
                  // (0x1D SZARRAY -> "a" cast) seeds the buffer; int32 sets the
                  // initial capacity. Both were LOUD before.
                  if (ps.Length == 1 && ps[0] == 0x1D)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_data", 1, false, false, true,
                          new[] { "", "a" });
                  if (ps.Length == 1 && ps[0] == 0x08)
                      return I("il2cpp::runtime::icall_stream_memory_ctor_capacity", 1, false, false, true,
                          new[] { "", "i" });
                  throw new NotSupportedException($"MemoryStream ctor with {count} arg(s) not wired (wired: (), (byte[]), (int))");
              case "System.IO.StreamReader":
                  if (ps.Length == 1 && ps[0] == 0x12)
                      return I("il2cpp::runtime::icall_stream_reader_ctor", 1, false, false, true);
                  // P2 IO-symmetry (2026-09-19): StreamReader(string path) --
                  // the runtime opens "rb" and BOM-detects (net48 contract).
                  if (ps.Length == 1 && ps[0] == 0x0E)
                      return I("il2cpp::runtime::icall_stream_reader_path_ctor", 1, false, false, true,
                          new[] { "", "" });
                  throw new NotSupportedException(
                      $"StreamReader ctor with {count} arg(s) not wired: the object-typed icalls take exactly (Stream) or (string path) -- " +
                      "the encoding overloads need extra icall variants");
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
