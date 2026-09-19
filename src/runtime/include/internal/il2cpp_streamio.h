#pragma once
//------------------------------------------------------------------------------
// il2cpp_streamio.h -- System.IO stream OBJECT model (the class descriptors +
// object-typed icall surface for FileStream / StreamReader / StreamWriter /
// MemoryStream). See streamio.h for the underlying HANDLE layer (the
// StreamState factories and raw handle functions this layer delegates to).
//
// OBJECT MODEL (the System_WeakReference_class / System_StringReader_class
// precedent -- the transpiler emits il2cpp_object_new(&System_IO_*_class) for
// newobj/isinst/castclass on these BCL types):
//
//   * Four GLOBAL-scope runtime descriptors -- System_IO_FileStream_class /
//     System_IO_StreamReader_class / System_IO_StreamWriter_class /
//     System_IO_MemoryStream_class -- all over the SAME C++ layout:
//
//         struct Il2CppStreamObject { Il2CppObject obj; int32_t handle; }
//
//     object header + an int32 HANDLE. There are NO GC reference fields
//     (refFieldBitmap 0), so the objects move freely under the Gen0
//     compaction (gc.cpp Phase A) -- the handle is copied verbatim and stays
//     valid, exactly like the gcext weak-target handle.
//   * The handle indexes the stream HANDLE REGISTRY (a static C++ array of
//     StreamState* -- NON-MOVING memory owned by streamio.cpp, the same
//     contract as the gcext weak-target table). The registry IS the
//     object -> FILE* mapping: every object icall reads the receiver's
//     int32 handle and looks up the backend state (StreamStateOf).
//   * StreamReader / StreamWriter SHARE the wrapped stream's backend state
//     (net48 ownership: a wrapper takes over the wrapped stream -- closing
//     the wrapper closes the underlying file). StreamState refCounts its
//     live handles: close closes the underlying FILE* once and frees the
//     state when the LAST handle closes (idempotent, no double-fclose).
//   * Handle lifetime: allocated by a ctor icall, released by Close. A
//     fresh object's ZERO-INITIALIZED handle is overwritten by the ctor --
//     never released as a stale index (the WeakReference ctor precedent:
//     handle == 0 is also the first valid index once the registry arms).
//   * After Close the object's handle is -1: instance calls return
//     -1 / nullptr / 0 (M3 result conventions -- the documented divergence
//     from net48's ObjectDisposedException, consistent with stringrw.h).
//   * Ctor failures THROW (net48 semantics): null path -> ArgumentNullException,
//     invalid FileMode/FileAccess -> ArgumentException, unopenable file ->
//     FileNotFoundException; a null receiver -> NullReferenceException.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"     // Il2CppArray (byte[] Read/Write icalls)
#include "il2cpp_object.h"    // Il2CppObject
#include "il2cpp_metadata.h"  // Il2CppClass descriptor type; System_Object_class
#include "il2cpp_com.h"       // stage 12/13: the COM icalls (CreateStreamOnHGlobal / BSTR bridge) the generated code calls

#include <cstdint>

// System.IO stream runtime descriptors (GLOBAL scope -- the transpiler emits
// &System_IO_*_class for newobj/isinst/castclass; defined in streamio.cpp,
// registered lazily through il2cpp_stream_register_classes()).
extern il2cpp::Il2CppClass System_IO_FileStream_class;
extern il2cpp::Il2CppClass System_IO_StreamReader_class;
extern il2cpp::Il2CppClass System_IO_StreamWriter_class;
extern il2cpp::Il2CppClass System_IO_MemoryStream_class;

// System.IO.BinaryReader / BinaryWriter runtime descriptors (the binrw-b23
// object closure -- the C56 protocol over a WRAPPED stream). Same layout
// contract as the four stream classes: the shared Il2CppStreamObject shape
// (object header + int32 handle into the streamio handle registry). The
// descriptor allocates through the transpiler's newobj fallback
// (il2cpp_object_new(&System_IO_BinaryReader_class)) and registers through
// il2cpp_stream_register_classes() (the wrapper ctors call it).
extern il2cpp::Il2CppClass System_IO_BinaryReader_class;
extern il2cpp::Il2CppClass System_IO_BinaryWriter_class;

namespace il2cpp {

// Managed System.IO stream layout (shared by all four stream classes): object
// header + an int32 handle into the streamio handle registry. The handle is
// -1 after Close (no live handle); the registry entry lives in non-moving
// memory so the object's compaction copies never invalidate it.
struct Il2CppStreamObject
{
    Il2CppObject obj;   // object header (klass pointer, offset 0)
    int32_t handle;     // registry index (offset 8)
};

// Register the four System_IO_*_class descriptors (idempotent --
// il2cpp_class_register dedupes by pointer). Call before any newobj /
// isinst / castclass that may target a stream type.
void il2cpp_stream_register_classes();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// A read-only MemoryStream object seeded with a COPY of 'data' (position 0,
// length = len). Used by Assembly.GetManifestResourceStream -- the e2e
// contract is `Stream s = ...; s.Read(buf, 0, n)` (a raw byte[] receiver
// would not be a stream object). The handle registry keeps the state alive.
Il2CppObject* il2cpp_stream_memory_from_bytes(const uint8_t* data, int32_t len);

//------------------------------------------------------------------------------
// COM stream bridge (stage 12 -- Marshal.CreateStreamOnHGlobal, the com.cpp
// consumer). A ComStream object is a streamio handle-registry state of kind
// kCom whose read/write/position/length/seek/set_length delegate to the
// wrapped native IStream through the ABI-safe icalls (il2cpp_com.h). The
// object is NOT an RCW (IsComObject false) and is never transpiler-emitted
// (the RcwClass precedent -- a runtime-only descriptor).
//------------------------------------------------------------------------------

// Wrap a native IStream* (an OWNED reference -- the runtime holds it) behind
// a ComStream stream object. 'deleteOnRelease' records CreateStreamOnHGlobal's
// fDeleteOnRelease flag; closing the stream releases the IStream reference
// (the HGLOBAL lifetime is the native stream's own -- Release frees it only
// when fDeleteOnRelease was set). Windows: full implementation. Non-Windows:
// returns nullptr (the runtime's create icall throws NotSupportedException
// before ever calling this).
Il2CppObject* il2cpp_stream_com_create(intptr_t iStream, int32_t deleteOnRelease);

// The wrapped IStream* of a ComStream object (as the intptr carrier), or 0
// when 'stream' is not a live COM stream (null, a different stream class, or
// a closed ComStream).
intptr_t il2cpp_stream_com_get_iunknown(Il2CppObject* stream);

//------------------------------------------------------------------------------
// Object ctors (each arms the receiver's handle; failures throw the M3
// exception carrier, matching net48's throwing ctors).
//------------------------------------------------------------------------------

// FileStream(string path, FileMode fileMode, FileAccess fileAccess): opens
// 'path' with the .NET FileMode (1..6) / FileAccess (1..3) constants
// translated to the runtime mode (0..4, see streamio.h). Null path ->
// ArgumentNullException; invalid mode/access -> ArgumentException; an
// incompatible FileMode/FileAccess combination (FileMode.Create/CreateNew/
// Truncate without Write, or FileMode.Append with anything but Write -- net48
// matrix) -> ArgumentException; an unopenable target (missing file in read
// mode, missing directory in write mode, ...) -> FileNotFoundException. Null
// receiver -> NullReferenceException.
void icall_stream_file_ctor(Il2CppObject* self, Il2CppObject* path,
                            int32_t fileMode, int32_t fileAccess);

// FileStream(string path, FileMode fileMode): the 2-arg net48 ctor. The
// access default is the net48 rule -- FileMode.Append -> FileAccess.Write,
// every other mode -> FileAccess.ReadWrite (FileShare.Read is not modeled,
// same as the 3-arg ctor). Errors as icall_stream_file_ctor.
void icall_stream_file_ctor2(Il2CppObject* self, Il2CppObject* path,
                             int32_t fileMode);

// MemoryStream(): an in-memory read-write stream (malloc-backed buffer).
// Null receiver -> NullReferenceException.
void icall_stream_memory_ctor(Il2CppObject* self);

// P2 IO-symmetry (2026-09-19): the MemoryStream 1-arg ctors.
//   MemoryStream(byte[] buffer): seeds the stream with a COPY of the array
//   (pos 0). MemoryStream(int capacity): empty stream with that initial
//   buffer capacity (length stays 0).
void icall_stream_memory_ctor_data(Il2CppObject* self, Il2CppArray* data);
void icall_stream_memory_ctor_capacity(Il2CppObject* self, int32_t capacity);

// StreamReader(Stream stream): wraps an EXISTING stream object -- the reader
// SHARES its backend state (refCounted; net48 ownership: closing the reader
// closes the shared file). Null stream -> ArgumentNullException; a
// non-FileStream/MemoryStream receiver -> ArgumentException. Null receiver ->
// NullReferenceException.
void icall_stream_reader_ctor(Il2CppObject* self, Il2CppObject* stream);

// P2 IO-symmetry (2026-09-19): StreamReader(string path) -- opens the file
// read-only ("rb") and runs the same 3-byte BOM detection as the (Stream) ctor.
void icall_stream_reader_path_ctor(Il2CppObject* self, Il2CppObject* path);

// StreamWriter(Stream stream): wraps an EXISTING stream object -- the writer
// SHARES its backend state (refCounted; net48 ownership: closing the writer
// closes the shared file). Errors as icall_stream_reader_ctor.
void icall_stream_writer_ctor(Il2CppObject* self, Il2CppObject* stream);

// StreamWriter(string path): opens 'path' with FileMode.Create + FileAccess.Write
// (runtime "wb" mode 1 -- create/truncate) and stores the handle on the writer
// object. Null path -> ArgumentNullException; unopenable target ->
// FileNotFoundException. Null receiver -> NullReferenceException. net48: the
// single-arg string-path writer ctor (streamops e2e `new StreamWriter(file)`).
void icall_stream_writer_path_ctor(Il2CppObject* self, Il2CppObject* path);

// P2(5) IO-symmetry (2026-09-19): StreamWriter(string path, bool append) --
// append=false is FileMode.Create (truncate), append=true is FileMode.Append;
// the text encoder is UTF-8 (no preamble).
void icall_stream_writer_path_append_ctor(Il2CppObject* self, Il2CppObject* path,
                                          int32_t append);

// StreamWriter(string path, bool append, Encoding encoding): the same open
// modes plus the text encoder selected by the managed encoding object's
// runtime class (UTF-8 / UTF-16LE / ASCII). A UTF-16 buffer preamble is
// written before the first text write when the target starts empty -- the
// net48 StreamWriter preamble rule.
void icall_stream_writer_path_encoding_ctor(Il2CppObject* self, Il2CppObject* path,
                                            int32_t append, Il2CppObject* encoding);

// StreamWriter(string path, bool append, Encoding encoding, int bufferSize):
// identical byte output; net48 validates bufferSize > 0
// (ArgumentOutOfRangeException otherwise) and otherwise the size only affects
// the internal buffer, never the written bytes.
void icall_stream_writer_path_encoding_ctor4(Il2CppObject* self, Il2CppObject* path,
                                             int32_t append, Il2CppObject* encoding,
                                             int32_t bufferSize);

//------------------------------------------------------------------------------
// Object instance members (the receiver is Il2CppObject* self; the backend
// state is resolved through the handle registry). Result conventions mirror
// the handle layer (streamio.h): read/write return byte counts or -1,
// positions/lengths return values or -1, status functions return 0/non-zero,
// booleans return 1/0, string/array returns are nullptr on failure.
//------------------------------------------------------------------------------

// Stream.Read(byte[] buffer, int offset, int count): reads up to 'count'
// bytes into the array at 'offset'. Returns bytes read (0 at end-of-stream;
// -1 for a capability violation or a closed stream). Null buffer ->
// ArgumentNullException; an invalid (offset, count) range ->
// ArgumentOutOfRangeException; a read on a write-only stream ->
// NotSupportedException (net48). Null receiver -> NullReferenceException.
int32_t icall_stream_read_bytes(Il2CppObject* self, Il2CppArray* buffer,
                                int32_t offset, int32_t count);

// Stream.Write(byte[] buffer, int offset, int count): writes 'count' bytes
// from the array at 'offset'. Null buffer / invalid range / write on a
// read-only stream as icall_stream_read_bytes (NotSupportedException).
void icall_stream_write_bytes(Il2CppObject* self, Il2CppArray* buffer,
                              int32_t offset, int32_t count);

// Stream.Flush(): pushes buffered file data to the OS (a no-op for memory
// streams). 0 on success, non-zero on failure (null/closed receiver).
int32_t icall_stream_flush(Il2CppObject* self);

// Stream.Close(): closes the underlying file/buffer and releases the handle
// (the object's handle becomes -1). IDEMPOTENT: a second Close is a no-op
// returning 0. On a shared (wrapped) state it closes the file once and frees
// the state when the last handle closes. 0 on success.
int32_t icall_stream_close(Il2CppObject* self);

// Stream.Position / Stream.Length: the current byte offset / stream size in
// bytes. -1 on a closed receiver or a backend error. set_Position with a
// NEGATIVE value throws ArgumentOutOfRangeException (net48 -- argument
// validation, not the closed-receiver -1 convention).
int64_t icall_stream_get_position(Il2CppObject* self);
int32_t icall_stream_set_position(Il2CppObject* self, int64_t position);
int64_t icall_stream_get_length(Il2CppObject* self);

// Stream.CanRead / Stream.CanWrite: 1/0 (0 on a closed receiver).
int32_t icall_stream_get_can_read(Il2CppObject* self);
int32_t icall_stream_get_can_write(Il2CppObject* self);

// Stream.CanSeek: 1 for memory streams and every file-stream mode (net48
// probe: an append-mode FileStream still reports CanSeek=true -- only write
// placement differs), 0 for closed receivers.
int32_t icall_stream_get_can_seek(Il2CppObject* self);

// Stream.Seek(long offset, SeekOrigin origin): moves to 'offset' relative to
// 'origin' (0=Begin, 1=Current, 2=End) and returns the NEW position.
// An invalid origin -> ArgumentException; a seek before the beginning of the
// stream -> IOException (net48); a non-seekable stream ->
// NotSupportedException; -1 for a closed receiver (M3). Null receiver ->
// NullReferenceException.
int64_t icall_stream_seek(Il2CppObject* self, int64_t offset, int32_t origin);

// Stream.SetLength(long value): truncates or extends the stream to 'value'
// bytes (extensions are zero-filled; a position past the new end clamps
// back). A NEGATIVE value -> ArgumentOutOfRangeException (argument
// validation); a non-seekable stream -> NotSupportedException; a closed
// receiver is a silent no-op (M3). Null receiver -> NullReferenceException.
void icall_stream_set_length(Il2CppObject* self, int64_t value);

// Stream.ReadByte(): the next byte as an int (0..255), or -1 at end-of-stream
// (clean EOF, NOT an exception -- net48). A read on a write-only stream ->
// NotSupportedException; -1 for a closed receiver. Null receiver ->
// NullReferenceException.
int32_t icall_stream_read_byte(Il2CppObject* self);

// Stream.WriteByte(byte value): writes the single byte at the current
// position (extending the stream when past the end). A write on a read-only
// stream -> NotSupportedException; a closed receiver is a silent no-op (M3).
// Null receiver -> NullReferenceException.
void icall_stream_write_byte(Il2CppObject* self, int32_t value);

// StreamReader.ReadLine(): the next line as a managed string with the
// terminator stripped (CRLF / LF; UTF-8 decoded). nullptr at end-of-stream
// or on a closed receiver; an empty line returns an empty string.
Il2CppObject* icall_stream_read_line(Il2CppObject* self);

// StreamReader.ReadToEnd(): the remainder from the current position to EOF as
// one UTF-8 managed string ("" at end-of-stream, NOT null -- net48). nullptr
// for a closed receiver or a read-capability violation.
Il2CppObject* icall_stream_read_to_end(Il2CppObject* self);

// StreamWriter.WriteLine(string): writes the line as UTF-8 followed by CRLF.
// 0 on success, non-zero on failure (closed receiver, capability violation,
// short write).
int32_t icall_stream_write_line(Il2CppObject* self, Il2CppObject* line);

// TextWriter/StreamWriter.Write(string): writes the text as UTF-8 WITHOUT
// the line terminator (net48 TextWriter.Write). A null string emits nothing.
// 0 on success, -1 on a closed receiver (the M3 silent-failure convention).
int32_t icall_stream_write_text(Il2CppObject* self, Il2CppObject* text);

// MemoryStream.ToArray(): a managed byte[] COPY of the stream content (bytes
// written so far, regardless of the current position). nullptr for a closed
// receiver or a non-memory stream.
Il2CppArray* icall_stream_memory_to_array(Il2CppObject* self);

//------------------------------------------------------------------------------
// Timeout surface (System.IO.Stream). FileStream and MemoryStream never
// support timeouts: CanTimeout is always false, and the ReadTimeout /
// WriteTimeout accessors (get AND set) throw InvalidOperationException
// ("Timeouts are not supported on this stream.") -- net48 behavior. A null
// receiver -> NullReferenceException (before the throw).
//------------------------------------------------------------------------------
int32_t icall_stream_get_can_timeout(Il2CppObject* self);
int32_t icall_stream_get_read_timeout(Il2CppObject* self);
int32_t icall_stream_set_read_timeout(Il2CppObject* self, int32_t value);
int32_t icall_stream_get_write_timeout(Il2CppObject* self);
int32_t icall_stream_set_write_timeout(Il2CppObject* self, int32_t value);

//------------------------------------------------------------------------------
// System.IO.BinaryReader / BinaryWriter OBJECT surface (binrw-b23).
//
// The managed BinaryReader/BinaryWriter are STREAM WRAPPERS (net48 ownership:
// they share the wrapped stream's backend state -- see the StreamReader/Writer
// wrapper ctors above -- and read/write the C56 binary protocol through it):
//   * fixed-width values little-endian; bool exactly 1 byte; floats IEEE-754
//     bit patterns (the BinaryWriter.Write protocol)
//   * strings are a 7-bit-encoded UTF-8 BYTE length prefix + UTF-8 content
//     (net48 Encoding.UTF8: surrogate pairs 4-byte, lone surrogates U+FFFD)
//
// The reads/writes operate at the wrapped stream's CURRENT position, advancing
// it and extending the stream length -- the underlying MemoryStream/FileStream
// position stays in sync (net48 semantics). Failure conventions follow the
// stream layer (M3): a read that cannot satisfy its full width returns the
// zero value / null (no managed exception; a partial read advances the stream
// position by whatever was available -- net48 throws EndOfStreamException, a
// documented divergence the probe never exercises), a write on a closed
// receiver is a silent no-op, a write on a non-writable stream throws
// NotSupportedException. Closing the wrapper closes the shared stream once
// (refCounted -- the StreamReader/Writer ownership rule).
//------------------------------------------------------------------------------

// BinaryReader(Stream stream) / BinaryWriter(Stream stream): wrap an EXISTING
// stream object -- SHARES its backend state (refCounted close). Errors as
// icall_stream_reader_ctor (null stream -> ArgumentNullException,
// non-FileStream/MemoryStream -> ArgumentException). Null receiver ->
// NullReferenceException.
void icall_binary_reader_ctor(Il2CppObject* self, Il2CppObject* stream);
void icall_binary_writer_ctor(Il2CppObject* self, Il2CppObject* stream);

// BinaryWriter.Write(...): append the fixed-width little-endian encoding /
// the 7-bit-length-prefixed string to the wrapped stream at its current
// position. A null string writes the empty string (a single 0x00 length
// byte). A closed receiver is a silent no-op; a write on a non-writable
// stream throws NotSupportedException.
void icall_binary_write_int32(Il2CppObject* self, int32_t value);
void icall_binary_write_int64(Il2CppObject* self, int64_t value);
void icall_binary_write_uint32(Il2CppObject* self, uint32_t value);
void icall_binary_write_double(Il2CppObject* self, double value);
void icall_binary_write_single(Il2CppObject* self, float value);
void icall_binary_write_bool(Il2CppObject* self, int32_t value);
void icall_binary_write_string(Il2CppObject* self, Il2CppObject* value);

// BinaryReader.ReadInt32/... : decode the next value from the wrapped stream
// at its current position. Truncated / malformed (string length > remaining,
// or a 7-bit length prefix longer than 5 bytes): the zero value / null.
int32_t      icall_binary_read_int32(Il2CppObject* self);
int64_t      icall_binary_read_int64(Il2CppObject* self);
uint32_t     icall_binary_read_uint32(Il2CppObject* self);
double       icall_binary_read_double(Il2CppObject* self);
float        icall_binary_read_single(Il2CppObject* self);
int32_t      icall_binary_read_bool(Il2CppObject* self);
Il2CppObject* icall_binary_read_string(Il2CppObject* self);

// BinaryReader.ReadByte: the RAW next byte (0..255 -- net48). Separate from
// icall_binary_read_bool on purpose: the bool icall NORMALIZES to 0/1 (right
// for ReadBoolean, wrong for ReadByte). Truncated: 0 (the M3 zero-value
// convention -- see the EndOfStreamException divergence note above).
int32_t      icall_binary_read_byte(Il2CppObject* self);

}} // namespace il2cpp::runtime
