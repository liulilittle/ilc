#pragma once
//------------------------------------------------------------------------------
// buffer.h -- System.Buffer method implementations (icall layer).
//
// Implements the System.Buffer surface: raw BYTE-level access to arrays,
// independent of element types. All offsets and lengths are in BYTES, not
// elements (unlike Array.Copy, whose units are elements).
//
//   il2cpp_buffer_block_copy     -- Buffer.BlockCopy(Array, int, Array, int, int)
//   il2cpp_buffer_byte_length    -- Buffer.ByteLength(Array)
//   il2cpp_buffer_memory_copy    -- raw memmove primitive (dst, src, count)
//   il2cpp_buffer_memory_copy_sized -- Buffer.MemoryCopy(void*, void*, long, long)
//
// Byte-length contract: an array's byte length is elemSize * length, where
// elemSize comes from the array's OWN type information (Il2CppArray::elemSize,
// il2cpp_array.h) -- the caller never supplies it. This keeps the signature
// aligned with .NET's Buffer.ByteLength(Array) single-argument form.
//
// BlockCopy element-kind contract (matches array.cpp's M3 model):
//   - Value-element arrays (isRefElem == false) of ANY elemSize may be
//     copied into each other BYTE-wise: int32[] -> byte[], byte[] -> byte[],
//     byte[] -> int32[], etc. The source and destination element sizes need
//     NOT match (the copy is a flat byte stream).
//   - Reference-element arrays (isRefElem == true) are REJECTED
//     (ArgumentException, .NET: "Only array of primitive types can be
//     copied.") -- a reference array is a sequence of GC roots, not bytes;
//     byte-copying it would corrupt the card table's view of live objects.
//
// Exception mapping (managed semantics, thrown through il2cpp_throw_new with
// the full base-chain string):
//   null argument                    -> ArgumentNullException
//   negative offset / count          -> ArgumentOutOfRangeException
//   reference-element array          -> ArgumentException
//   offset + count beyond the array  -> ArgumentException
//   byte length overflowing int32    -> OverflowException (ByteLength only)
//   non-primitive array (ByteLength) -> ArgumentException (net48 rejects
//     reference-element arrays: Arg_MustBePrimArray)
//   MemoryCopy: sourceBytesToCopy >  -> ArgumentOutOfRangeException
//     destinationSizeInBytes
//   MemoryCopy: negative count       -> OverflowException (net48's checked
//     conversion of a negative long to nuint)
//
// MemoryCopy has two runtime forms:
//   - il2cpp_buffer_memory_copy is the raw C++-level memmove primitive: a
//     checked-free overlap-safe copy; the caller guarantees the buffers.
//   - il2cpp_buffer_memory_copy_sized is the net48 public surface: it
//     validates the size arguments (see above) before delegating to the raw
//     primitive. It does NOT verify the actual pointed-to buffer sizes --
//     matching .NET, which trusts the caller-supplied size parameters.
//
// All array parameters are Il2CppObject* (the generated code's reference
// type); the array layout is validated internally (klass name '[' prefix)
// before any array field is dereferenced -- a non-array object is a caller
// bug and raises ArgumentException instead of undefined behavior.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {
namespace runtime {

// Buffer.BlockCopy: copy 'count' BYTES from src[srcOffset] into
// dst[dstOffset] (byte offsets into each array's payload). Source and
// destination arrays may have different element kinds (both must be
// value-element arrays); overlapping ranges (src == dst) copy as if through
// a temporary buffer (memmove semantics). count == 0 is a validated no-op.
void il2cpp_buffer_block_copy(Il2CppObject* src, int32_t srcOffset,
                              Il2CppObject* dst, int32_t dstOffset,
                              int32_t count);

// Buffer.ByteLength: the total payload size of 'arr' in bytes
// (elemSize * length, read from the array's own type information). Null
// throws ArgumentNullException; a non-array object throws ArgumentException;
// a reference-element array throws ArgumentException (net48 rejects
// non-primitive arrays); a byte length beyond int32 throws OverflowException.
int32_t il2cpp_buffer_byte_length(Il2CppObject* arr);

// icall_buffer_get_byte -- Buffer.GetByte(Array, int) -> the array's payload
// byte at raw-data offset 'index' (int32 carrier, the UNSIGNED byte value
// 0..255), with the net48 validation (see buffer.cpp). Defined here because
// the MethodMap Buffer surface is buffer.h (il2cpp_buffer_*).
int32_t icall_buffer_get_byte(Il2CppObject* arr, int32_t index);

// icall_buffer_set_byte -- Buffer.SetByte(Array, int, byte): store ONE payload
// byte at the raw-data offset 'index' (the value arrives in an int32 carrier;
// only the low byte is written). The net48 validation mirrors GetByte exactly:
// null/not-array, reference-element array (ArgumentException), index outside
// [0, byteLength) -> ArgumentOutOfRangeException.
void icall_buffer_set_byte(Il2CppObject* arr, int32_t index, int32_t value);

// Buffer.MemoryCopy (raw pointer form): copy 'count' raw bytes from 'src' to
// 'dst' with memmove overlap safety (forward and backward overlap both behave
// as if a temporary buffer were used). No bounds checking -- this is the raw
// primitive; the caller guarantees valid buffers.
void il2cpp_buffer_memory_copy(void* dst, const void* src, size_t count);

// Buffer.MemoryCopy (net48 sized form): copy 'sourceBytesToCopy' bytes from
// 'src' into 'dst'. Validates the size arguments exactly as net48 does:
//   sourceBytesToCopy > destinationSizeInBytes -> ArgumentOutOfRangeException
//   sourceBytesToCopy < 0                      -> OverflowException
// Then delegates to il2cpp_buffer_memory_copy (memmove, overlap-safe). The
// pointed-to buffers themselves are trusted (net48 semantics: only the size
// parameters are checked, never the actual allocation extents).
void il2cpp_buffer_memory_copy_sized(void* dst, const void* src,
                                     int64_t destinationSizeInBytes,
                                     int64_t sourceBytesToCopy);

} // namespace runtime
} // namespace il2cpp
