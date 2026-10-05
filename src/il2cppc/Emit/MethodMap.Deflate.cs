namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Deflate -- System.IO.Compression.DeflateStream icall mappings
// (il2cpp_deflate.h, implementation deflate.cpp).
// Owners: System.IO.Compression.DeflateStream
//
// OBJECT MODEL: a DeflateStream is a managed object (Il2CppDeflateStreamObject
// -- header + ONE native state pointer) allocated through its GLOBAL class
// descriptor System_IO_Compression_DeflateStream_class -- the transpiler's
// BclAllocExpr fallback emits il2cpp_object_new(&System_IO_Compression_
// DeflateStream_class) for `new DeflateStream(...)` (the System.Version
// contract). The native state holds the underlying stream object + a byte
// buffer + the compression mode; it is NOT a GC reference (refFieldBitmap 0).
//
// Every icall takes the managed receiver (Il2CppObject* self) as its first
// parameter -- the receiver passes through the "" passthrough cast. The
// underlying Stream argument passes as "" (the object IS the pointer); the
// CompressionMode enum (0x11 VALUETYPE) passes as "i" (int32 carrier); the
// byte[] buffer passes as "a" (Il2CppObject* -> Il2CppArray*); offset/count
// pass as "" (int32 values).
//
// Wired surface:
//   * .ctor(Stream, CompressionMode)
//   * Write(byte[], int, int) -- buffers (Compress mode)
//   * Read(byte[], int, int) -- inflates (Decompress mode)
//   * Close() / Dispose() -- writes the deflated stream (Compress mode)
//
// Known-unwireable members return null (the generated-name path):
//   * Flush / Seek / Position / Length / CanRead / CanWrite -- the
//     Stream-base surface is out of scope for this domain.
//   * Write(byte[]) / Read(byte[]) -- the single-array overloads are out of
//     scope (the runtime has the (byte[], int, int) forms only).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDeflate(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // .ctor(Stream stream, CompressionMode mode): the receiver (self)
            // is arg[0] (""), the stream is arg[1] ("" -- the object IS the
            // pointer), the mode is arg[2] ("i" -- the enum int32 carrier).
            case ".ctor":
                if (owner == "System.IO.Compression.DeflateStream" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_deflate_stream_ctor", 2, false, false, true, new[] { "", "", "i" });
                return null;
            // void Write(byte[] buffer, int offset, int count): buffers the
            // bytes (Compress mode). The byte[] is "a", offset/count "".
            // The MemberRef declares Write on the Stream base (the resolver's
            // Stream case routes a DeflateStream receiver here), so no owner
            // check.
            case "Write":
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_deflate_stream_write", 3, false, false, true, new[] { "", "a", "", "" });
                return null;
            // int Read(byte[] buffer, int offset, int count): inflates from
            // the underlying stream; returns the bytes read (0 at EOF).
            case "Read":
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_deflate_stream_read", 3, true, false, true, new[] { "", "a", "", "" });
                return null;
            // void Close() / Dispose(): writes the deflated stream (Compress
            // mode). Dispose(bool) drops the flag (Close is idempotent).
            case "Close":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_deflate_stream_close", 0, false, false, true);
                return null;
            case "Dispose":
                if (count == 0)
                    return I("il2cpp::runtime::icall_deflate_stream_close", 0, false, false, true);
                if (count == 1)
                    return I("il2cpp::runtime::icall_deflate_stream_close", 1, false, false, true, new[] { "drop" });
                return null;
            default: return null;
        }
    }
}
