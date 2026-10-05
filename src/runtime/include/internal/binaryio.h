#pragma once
//------------------------------------------------------------------------------
// binaryio.h -- System.IO.BinaryReader / BinaryWriter byte-stream domain.
//
// The managed BinaryReader/BinaryWriter speak a compact, little-endian
// binary protocol. This module implements that protocol over an in-memory
// byte stream so generated BCL code and the unit tests can reproduce it
// byte-for-byte:
//
//   * Fixed-width values (int32 / int64 / uint32 / double / single / bool)
//     are little-endian IEEE-754 for the floating-point types -- bool is
//     exactly 1 byte (0x00 false, 0x01 true), matching BinaryWriter.Write.
//   * Strings are a 7-bit-encoded UTF-8 BYTE length prefix followed by the
//     UTF-8 content (the BinaryWriter.Write(string) convention). The
//     length is the byte count of the UTF-8 encoding, NOT the UTF-16 unit
//     count; the 7-bit encoding stores 7 bits per byte with the high bit
//     marking a continuation byte (up to 5 bytes for an int32 length). The
//     UTF-8 codec follows net48 Encoding.UTF8: surrogate pairs encode as
//     4-byte sequences, lone surrogates and invalid input decode/encode as
//     the replacement character U+FFFD (invalid input consumes bytes per
//     the net48 replacement-fallback counts).
//
// Lifecycle: the writer accumulates bytes in an internal growable buffer;
// il2cpp_binary_writer_get_bytes snapshots them into a managed byte[].
// The reader COPIES the byte range passed to il2cpp_binary_reader_create,
// so the source buffer can be released as soon as the call returns.
//
// Failure conventions (M3 runtime: the managed exception system is a later
// milestone, so failures are return values, never C++ throws):
//   * A read needing more bytes than remain returns the zero value
//     (0 / 0.0 / false / null) and latches the reader's failed flag
//     (il2cpp_binary_reader_eof -> 1). A failed reader stays failed: every
//     later read also returns the zero value and the position never
//     advances. il2cpp_binary_seek still reports -1 for out-of-range
//     targets (no read occurs, so the failed flag is untouched by seeks).
//   * il2cpp_binary_write_string with a null string writes the empty
//     string (a single 0x00 length prefix) -- no null handling at this
//     layer (ArgumentNullException is a later milestone).
//
// The implementations are fully portable C++ (explicit byte shifts -- the
// emitted layout is little-endian regardless of the host byte order) and
// compile under MSVC and MinGW alike.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// Opaque stream handles (implemented in binaryio.cpp; never dereference).
struct Il2CppBinaryWriter;
struct Il2CppBinaryReader;

//------------------------------------------------------------------------------
// System.IO.BinaryWriter side.
//------------------------------------------------------------------------------

// Create a writer with an empty buffer (must be released with
// il2cpp_binary_writer_destroy).
Il2CppBinaryWriter* il2cpp_binary_writer_create();

// Release a writer and its buffer. A null handle is ignored.
void il2cpp_binary_writer_destroy(Il2CppBinaryWriter* writer);

// Snapshot the bytes accumulated so far as a managed byte[] (length == the
// number of bytes written). The array is a copy: the writer may keep
// accumulating (or be destroyed) without affecting it. Null -> null.
Il2CppArray* il2cpp_binary_writer_get_bytes(Il2CppBinaryWriter* writer);

// Append 'value' little-endian, 4 bytes.
void il2cpp_binary_write_int32(Il2CppBinaryWriter* writer, int32_t value);

// Append 'value' little-endian, 8 bytes.
void il2cpp_binary_write_int64(Il2CppBinaryWriter* writer, int64_t value);

// Append 'value' little-endian, 4 bytes.
void il2cpp_binary_write_uint32(Il2CppBinaryWriter* writer, uint32_t value);

// Append 'value' little-endian IEEE-754 binary64, 8 bytes.
void il2cpp_binary_write_double(Il2CppBinaryWriter* writer, double value);

// Append 'value' little-endian IEEE-754 binary32, 4 bytes.
void il2cpp_binary_write_single(Il2CppBinaryWriter* writer, float value);

// Append 'value' as 1 byte: 0x01 when non-zero, 0x00 when zero.
void il2cpp_binary_write_bool(Il2CppBinaryWriter* writer, int32_t value);

// Append 'value' as a 7-bit-encoded UTF-8 byte length + UTF-8 content
// (the BinaryWriter.Write(string) convention). A null 'value' writes the
// empty string (a single 0x00 length byte).
void il2cpp_binary_write_string(Il2CppBinaryWriter* writer, Il2CppObject* value);

//------------------------------------------------------------------------------
// System.IO.BinaryReader side.
//------------------------------------------------------------------------------

// Create a reader over a COPY of 'data'[0..length). 'data' may be null
// only when length == 0. Must be released with il2cpp_binary_reader_destroy.
Il2CppBinaryReader* il2cpp_binary_reader_create(const uint8_t* data, size_t length);

// Release a reader. A null handle is ignored.
void il2cpp_binary_reader_destroy(Il2CppBinaryReader* reader);

// Read 4 little-endian bytes as int32. Truncated: 0 + failed flag.
int32_t il2cpp_binary_read_int32(Il2CppBinaryReader* reader);

// Read 8 little-endian bytes as int64. Truncated: 0 + failed flag.
int64_t il2cpp_binary_read_int64(Il2CppBinaryReader* reader);

// Read 4 little-endian bytes as uint32. Truncated: 0 + failed flag.
uint32_t il2cpp_binary_read_uint32(Il2CppBinaryReader* reader);

// Read 8 little-endian bytes as IEEE-754 binary64. Truncated: 0.0 + failed.
double il2cpp_binary_read_double(Il2CppBinaryReader* reader);

// Read 4 little-endian bytes as IEEE-754 binary32. Truncated: 0.0f + failed.
float il2cpp_binary_read_single(Il2CppBinaryReader* reader);

// Read 1 byte as 0/1. Truncated: 0 + failed flag.
int32_t il2cpp_binary_read_bool(Il2CppBinaryReader* reader);

// Read a 7-bit-encoded byte length + UTF-8 content as a managed string.
// Truncated or malformed (length > int32, content cut short): null +
// failed flag. An empty string reads back as an empty string, never null.
Il2CppObject* il2cpp_binary_read_string(Il2CppBinaryReader* reader);

// Move the read position. 'origin': 0 = begin (offset >= 0), 1 = current,
// 2 = end (offset <= 0); any other origin fails. Returns the new position
// on success, -1 when the target would fall outside [0, length]. Seeking
// never clears the failed flag and never performs a read.
int32_t il2cpp_binary_seek(Il2CppBinaryReader* reader, int64_t offset, int32_t origin);

// The current read position (byte offset from the buffer start).
int32_t il2cpp_binary_reader_position(Il2CppBinaryReader* reader);

// 1 once any read failed (truncated/malformed); the flag latches -- a
// failed reader stays failed. 0 while every read succeeded.
int32_t il2cpp_binary_reader_eof(Il2CppBinaryReader* reader);

}} // namespace il2cpp::runtime
