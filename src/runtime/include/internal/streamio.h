#pragma once
//------------------------------------------------------------------------------
// streamio.h -- System.IO stream icalls (FileStream / StreamReader /
// StreamWriter / MemoryStream subset).
//
// This header declares the runtime icall surface for stream-based I/O that
// generated BCL code and hand-written tests link against. It complements
// il2cpp_io.h (File / Path / Directory whole-content helpers): the stream
// layer exposes a seekable, incremental read/write handle that maps onto the
// .NET Stream / StreamReader.ReadLine / StreamWriter.WriteLine surface.
//
// Handle model:
//   * An opaque stream handle (il2cpp_stream_t) is obtained from
//     il2cpp_stream_file_open / il2cpp_stream_memory_create and passed to
//     every other function. The handle is owned by the caller and MUST be
//     released with il2cpp_stream_close exactly once -- after close the
//     handle is dangling and must not be used again.
//   * THREADING: a handle (and the StreamState behind it, which a
//     StreamReader/StreamWriter wrapper SHARES with its underlying stream) is
//     NOT thread-safe. There is no lock and no atomic in the backend -- the
//     pushback ring, the decoder stash and the stdio mode bookkeeping are
//     plain fields. Concurrent use of one handle from several threads is a
//     data race; callers must serialize access (one owner thread, or an
//     external lock covering the whole handle). Distinct handles over the
//     same file are independent (each owns its own FILE*).
//   * The handle wraps one of two backends:
//       - File: a FILE* opened with _wfopen (Windows, UTF-16 path taken
//         directly from the managed string) or fopen (other platforms,
//         UTF-8 path). Position/length are queried through 64-bit fseek.
//       - Memory: a malloc/realloc buffer (StreamState::buf) with explicit
//         position, length (bytes written) and capacity. The buffer grows
//         by doubling and is freed by close.
//
// Result conventions (M3 runtime: failures are return values, not C++
// throws -- aligned with il2cpp_io.h). The ONE exception is a backend
// reposition failure on the read path (see FilePrepareRead in streamio.cpp):
// no return channel can express "the FILE* could not be put back into a legal
// read mode", so it raises IOException. net48 has no counterpart for that
// condition -- a Win32 handle lets WriteFile/ReadFile alternate freely, and an
// unseekable stream raises NotSupportedException from Seek/Position, never
// from Read -- so the IOException is an implementation-side failure signal,
// not modelled net48 behaviour.
//   * Concretely: a reposition failure (a backend I/O error, not a capability
//     check) raises IOException (kStreamIOExceptionChain); the normal paths
//     never throw. That branch is also production-unreachable today -- the
//     only FILE* this layer creates comes from _wfopen/fopen (there is no
//     popen/fdopen/_fdopen anywhere in the file), i.e. always a seekable
//     regular file, so the forced-failure path exists only for the tests.
//   * Open functions return nullptr on failure (unopenable path, null path,
//     invalid mode).
//   * Read/write functions return the number of bytes transferred; 0 means
//     end-of-stream (read) or a zero-length request (write); -1 signals a
//     null stream, a negative count, or a capability violation (read on a
//     write-only stream, write on a read-only stream).
//   * Status functions (flush / close / set_position / write_line) return 0
//     on success and non-zero on failure.
//   * Boolean functions (can_read / can_write) return 1/0; null streams
//     report 0.
//   * Value-returning functions (get_position / get_length) return -1 on
//     failure (null stream, or a stream backend that cannot answer).
//   * read_line returns a managed string WITHOUT the line terminator
//     (\r\n and \n both stripped); nullptr signals end-of-stream. An empty
//     line ("\n") returns an empty string, NOT nullptr.
//
// File open modes (int32_t):
//   0 = read        ("rb",  read-only)
//   1 = write       ("wb",  write-only, create/truncate)
//   2 = append      ("ab",  write-only, writes go to end-of-file)
//   3 = read-write  ("r+b" or "w+b" when the file does not exist yet,
//                    both directions, no truncation of existing content)
//   4 = read-write  ("w+b", both directions, ALWAYS truncate -- the net48
//                    FileMode.Create / Truncate with ReadWrite access target,
//                    the object-layer translation picks this mode)
//
// Text encoding contract (StreamReader/StreamWriter subset): read_line
// decodes the line as UTF-8; write_line encodes the managed string as UTF-8
// and appends CRLF (the .NET Framework default on Windows, aligned with the
// WriteAllLines convention in io.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// Opaque stream backend (defined in streamio.cpp; never dereferenced by
// callers). The handle is owned by the caller and freed by close.
struct StreamState;

typedef StreamState* il2cpp_stream_t;

//------------------------------------------------------------------------------
// FileStream.
//------------------------------------------------------------------------------

// FileStream open: creates a handle over the file named by 'path' in the
// given 'mode' (0=read, 1=write, 2=append, 3=read-write, 4=read-write-truncate;
// see header). Returns nullptr for a null path, an invalid mode, or an
// unopenable target (missing file in read mode, missing directory in write
// mode, ...).
il2cpp_stream_t il2cpp_stream_file_open(Il2CppObject* path, int32_t mode);

// Stream.Read(buffer, count): reads up to 'count' bytes into 'buffer'.
// Returns bytes read; 0 at end-of-stream; -1 for a null stream, a negative
// count, or a read on a write-only stream.
int32_t il2cpp_stream_read(il2cpp_stream_t stream, void* buffer, int32_t count);

// Stream.Write(buffer, count): writes 'count' bytes from 'buffer' at the
// current position (append-mode writes land at end-of-file). Returns bytes
// written; 0 for a zero-length request; -1 for a null stream, a negative
// count, or a write on a read-only stream.
int32_t il2cpp_stream_write(il2cpp_stream_t stream, const void* buffer, int32_t count);

// Stream.Flush(): pushes buffered file data to the OS (a no-op for memory
// streams). 0 on success, non-zero on failure (null stream, fflush error).
int32_t il2cpp_stream_flush(il2cpp_stream_t stream);

// Stream.Close(): flushes and releases the handle (fclose + buffer free).
// The handle is dangling afterwards and must not be used again. 0 on
// success, non-zero for a null stream.
int32_t il2cpp_stream_close(il2cpp_stream_t stream);

// Stream.Position: the current byte offset from the start of the stream.
// -1 for a null stream or a backend error (ftell failure).
int64_t il2cpp_stream_get_position(il2cpp_stream_t stream);

// Stream.Position = value: seeks to 'value' bytes from the start of the
// stream. Seeking past the end is allowed (a later write extends the
// stream). 0 on success, -1 for a null stream or a negative position.
int32_t il2cpp_stream_set_position(il2cpp_stream_t stream, int64_t position);

// StreamReader.ReadLine: the next line as a managed string with the
// terminator stripped (CRLF and LF both accepted; a lone CR is content).
// nullptr at end-of-stream; an empty line returns an empty string. UTF-8
// decoded. -1-style errors surface as nullptr too (null stream, read-only
// violation).
Il2CppObject* il2cpp_stream_read_line(il2cpp_stream_t stream);

// StreamReader.ReadToEnd: everything from the current position to the end as
// one UTF-8 decoded managed string. An empty remainder at end-of-stream
// returns an EMPTY string ("" -- NOT nullptr, matching net48). nullptr for a
// null/closed stream or a read-capability violation.
Il2CppObject* il2cpp_stream_read_to_end(il2cpp_stream_t stream);

// StreamWriter.WriteLine(line): writes 'line' as UTF-8 followed by CRLF.
// 0 on success, non-zero on failure (null stream, write-only violation,
// short write).
int32_t il2cpp_stream_write_line(il2cpp_stream_t stream, Il2CppObject* line);

// StreamWriter.Write(text): writes 'text' as UTF-8 WITHOUT a line terminator
// (net48 TextWriter.Write). A null string emits nothing. 0 on success,
// non-zero on failure (null/closed stream, write-only violation, short
// write).
int32_t il2cpp_stream_write_text(il2cpp_stream_t stream, Il2CppObject* text);

// Stream.Length: the stream size in bytes (bytes written so far). -1 for a
// null stream or a backend error.
int64_t il2cpp_stream_get_length(il2cpp_stream_t stream);

// Stream.CanRead: 1 when the stream supports reading (read / read-write
// modes, memory streams), 0 otherwise (and for null streams).
int32_t il2cpp_stream_get_can_read(il2cpp_stream_t stream);

// Stream.CanWrite: 1 when the stream supports writing (write / append /
// read-write modes, memory streams), 0 otherwise (and for null streams).
int32_t il2cpp_stream_get_can_write(il2cpp_stream_t stream);

// Stream.CanSeek: 1 when the stream supports seeking (memory streams and
// every file-stream mode -- net48 probe: an append-mode FileStream still
// reports CanSeek=true; only write placement differs), 0 for null/closed
// streams.
int32_t il2cpp_stream_get_can_seek(il2cpp_stream_t stream);

// Stream.Seek(offset, origin): moves to 'offset' relative to 'origin'
// (0=Begin, 1=Current, 2=End) and returns the NEW position. Seeking past the
// end is allowed (a later write extends the stream). Returns -1 for a null or
// closed stream, an invalid origin, or a resulting NEGATIVE position (a seek
// before the beginning -- callers validate and throw). The position is only
// moved when the target is valid (verify-then-mutate).
int64_t il2cpp_stream_seek(il2cpp_stream_t stream, int64_t offset, int32_t origin);

// Stream.SetLength(value): truncates or extends the stream to 'value' bytes
// (extensions are zero-filled -- the exposed bytes read 0, net48). A position
// past the new end clamps back to 'value'. 0 on success, -1 for a null or
// closed stream, a negative value, or a backend error (the state is left
// untouched on failure).
int32_t il2cpp_stream_set_length(il2cpp_stream_t stream, int64_t value);

//------------------------------------------------------------------------------
// MemoryStream.
//------------------------------------------------------------------------------

// MemoryStream(): a read-write stream over a malloc-backed buffer (no
// managed object; the handle is released with close). Returns nullptr on
// allocation failure.
il2cpp_stream_t il2cpp_stream_memory_create(void);

// MemoryStream.ToArray(): a managed byte[] copy of the stream content
// (bytes written so far, regardless of the current position). nullptr for a
// null stream or a non-memory stream.
Il2CppObject* il2cpp_stream_memory_to_array(il2cpp_stream_t stream);

}} // namespace il2cpp::runtime
