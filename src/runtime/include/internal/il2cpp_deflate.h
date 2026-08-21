#pragma once
//------------------------------------------------------------------------------
// il2cpp_deflate.h -- System.IO.Compression.DeflateStream runtime support
// (deflate.cpp).
//
// SCOPE (the taskcard T2xx deflate): the DEFLATE (RFC 1951) subset the
// runtime needs for a DeflateStream round-trip:
//   * DEFLATE (compress): STORED blocks only (BTYPE=00) -- the compressed
//     output is a sequence of stored blocks (no Huffman coding on the
//     compress side).
//   * INFLATE (decompress): STORED blocks (BTYPE=00) AND FIXED-HUFFMAN
//     blocks (BTYPE=01) -- the RFC 1951 fixed literal/length + distance
//     tables. Dynamic Huffman blocks (BTYPE=10) are out of scope (a
//     DeflateStream produced by this runtime never emits them; a foreign
//     dynamic-Huffman stream is rejected with an error).
//
// The raw primitives operate on byte buffers (no managed objects):
//   * il2cpp_deflate_compress(src, srcLen, dst, dstCapacity): writes the
//     stored-block DEFLATE stream and returns the bytes written (or the
//     required capacity when dst is null).
//   * il2cpp_deflate_decompress(src, srcLen, dst, dstCapacity): decodes the
//     stored/fixed-Huffman stream and returns the bytes written (or the
//     required capacity when dst is null). Malformed input returns -1.
//
// OBJECT MODEL: DeflateStream is a managed object (Il2CppDeflateStreamObject
// -- header + a native state pointer) allocated through the GLOBAL class
// descriptor System_IO_Compression_DeflateStream_class (the transpiler's
// BclAllocExpr fallback emits &System_IO_Compression_DeflateStream_class for
// `new DeflateStream(...)`). The native state holds the underlying stream
// handle + a byte buffer + the compression mode; it is NOT a GC reference
// (refFieldBitmap 0).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"     // Il2CppArray (byte[] Read/Write buffers)
#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.IO.Compression.DeflateStream runtime descriptor (GLOBAL scope -- the
// transpiler emits &System_IO_Compression_DeflateStream_class for
// newobj/isinst/castclass; defined in deflate.cpp).
extern il2cpp::Il2CppClass System_IO_Compression_DeflateStream_class;

namespace il2cpp { namespace runtime {

// Native DeflateStream state (forward; defined in deflate.cpp).
struct DeflateStreamState;

// Managed DeflateStream layout: object header + ONE native state pointer.
// The pointer is NOT a GC reference (refFieldBitmap 0).
struct Il2CppDeflateStreamObject
{
    Il2CppObject obj;            // object header (klass pointer, offset 0)
    DeflateStreamState* state;   // native state (offset 8) -- not a GC reference
};

// Register the DeflateStream descriptor once (idempotent).
void il2cpp_deflate_register_classes();

// --- raw primitives ---------------------------------------------------------

// DEFLATE (stored blocks only): writes the stored-block stream for 'srcLen'
// bytes of 'src' into 'dst' (at most 'dstCapacity' bytes). Returns the bytes
// written, or the exact required capacity when 'dst' is null. A null 'src'
// with srcLen > 0 is an error (returns -1).
int32_t il2cpp_deflate_compress(const uint8_t* src, int32_t srcLen,
                                uint8_t* dst, int32_t dstCapacity);

// INFLATE (stored + fixed Huffman): decodes the DEFLATE stream 'src' (of
// 'srcLen' bytes) into 'dst' (at most 'dstCapacity' bytes). Returns the bytes
// written, or the exact required capacity when 'dst' is null. Malformed input
// (bad block header, truncated data, a dynamic-Huffman block, an invalid
// back-reference) returns -1.
int32_t il2cpp_deflate_decompress(const uint8_t* src, int32_t srcLen,
                                  uint8_t* dst, int32_t dstCapacity);

// --- DeflateStream object icalls -------------------------------------------

// DeflateStream(Stream stream, CompressionMode mode): mode 0 = Compress,
// 1 = Decompress (net48 CompressionMode enum); otherwise
// ArgumentOutOfRangeException. The underlying stream is the managed stream
// object (read/written through the streamio object icalls). Allocates the
// native state.
void icall_deflate_stream_ctor(Il2CppObject* self, Il2CppObject* stream,
                               int32_t mode);

// Write(byte[] buffer, int offset, int count): buffers the bytes (Compress
// mode). The deflated output is written to the underlying stream on Close.
void icall_deflate_stream_write(Il2CppObject* self, Il2CppArray* buffer,
                                int32_t offset, int32_t count);

// Read(byte[] buffer, int offset, int count): inflates from the underlying
// stream (Decompress mode) and fills the buffer; returns the bytes read (0 at
// end of stream).
int32_t icall_deflate_stream_read(Il2CppObject* self, Il2CppArray* buffer,
                                  int32_t offset, int32_t count);

// Close(): Compress mode -- writes the stored-block DEFLATE stream of the
// buffered bytes to the underlying stream; Decompress mode -- no-op (the
// inflate is pull-based). Idempotent.
void icall_deflate_stream_close(Il2CppObject* self);

}} // namespace il2cpp::runtime
