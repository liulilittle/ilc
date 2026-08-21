//------------------------------------------------------------------------------
// buffer.cpp -- System.Buffer method implementations (icall layer).
//
// See buffer.h for the full contract: byte-unit offsets, cross-element-kind
// byte copies, validation order and exception mapping.
//
// Implementation notes:
//   - Every public entry point receives Il2CppObject* (the generated code's
//     reference type) and validates the array layout before dereferencing
//     any array field: the klass name MUST carry the '[' array-class marker
//     (array.cpp convention), otherwise the object is not an array and the
//     call raises ArgumentException instead of undefined behavior.
//   - Static-method null arguments are ArgumentNullException -- .NET static
//     semantics (no instance receiver exists on this surface).
//   - Range checks use int64 arithmetic (offset + count computed in int64),
//     so the check itself can never overflow for non-negative int32 inputs;
//     only the true range violation can fail the check.
//   - Overlapping copies (src == dst) go through std::memmove so forward
//     and backward overlap both behave as if a temporary buffer were used.
//   - ByteLength computes elemSize * length in int64 and rejects results
//     beyond int32 with OverflowException, matching the .NET checked
//     conversion. Array fields are internal invariants (never negative), so
//     no defensive checks are needed there -- same convention as arrayext.
//   - ByteLength rejects reference-element arrays with ArgumentException,
//     matching net48 (IsPrimitiveTypeArray / Arg_MustBePrimArray): a
//     reference array is a sequence of GC roots, not a raw byte payload.
//   - MemoryCopy has two forms: the raw memmove primitive
//     (il2cpp_buffer_memory_copy, checked-free, overlap-safe) and the net48
//     sized entry (il2cpp_buffer_memory_copy_sized) that validates the size
//     arguments -- sourceBytesToCopy beyond destinationSizeInBytes raises
//     ArgumentOutOfRangeException, a negative count raises
//     OverflowException (net48's checked long->nuint conversion) -- before
//     delegating to the primitive.
//------------------------------------------------------------------------------

#include "buffer.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Managed exception type chains (type + base classes, the exception.cpp
// catch-matcher walks the chain after the first comma).
//------------------------------------------------------------------------------
const char kArgNullChain[]       = "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgOutOfRangeChain[] = "System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgumentChain[]      = "System.ArgumentException,System.SystemException,System.Exception";
const char kOverflowChain[]      = "System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception";

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

//------------------------------------------------------------------------------
// RequireArray -- validate a static-method array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
// (Duplicated from arrayext.cpp's anonymous namespace: extracting it into a
// shared header would modify an existing file, which this task forbids; an
// anonymous-namespace copy cannot collide at link time.)
//------------------------------------------------------------------------------
Il2CppArray* RequireArray(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_new(kArgNullChain, "Value cannot be null.");
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr ||
        obj->klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_new(kArgumentChain, "Object is not an array.");
    }
    return reinterpret_cast<Il2CppArray*>(obj);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_buffer_block_copy -- Buffer.BlockCopy (see the header contract).
//   Validation order: null args, negative offsets/count, element-kind
//   (reference arrays rejected), byte-range overflow -- then the copy.
//   count == 0 is a validated no-op (offsets may sit one past the end,
//   matching .NET's > check).
//------------------------------------------------------------------------------
void il2cpp_buffer_block_copy(Il2CppObject* src, int32_t srcOffset,
                              Il2CppObject* dst, int32_t dstOffset,
                              int32_t count)
{
    Il2CppArray* s = RequireArray(src);
    Il2CppArray* d = RequireArray(dst);

    if (srcOffset < 0 || dstOffset < 0 || count < 0)
    {
        il2cpp_throw_new(kArgOutOfRangeChain, "Index or length is less than zero.");
    }
    if (s->isRefElem || d->isRefElem)
    {
        il2cpp_throw_new(kArgumentChain,
                         "Only array of primitive types can be copied.");
    }
    const int64_t srcBytes = static_cast<int64_t>(s->length) * s->elemSize;
    const int64_t dstBytes = static_cast<int64_t>(d->length) * d->elemSize;
    if (static_cast<int64_t>(srcOffset) + count > srcBytes)
    {
        il2cpp_throw_new(kArgumentChain, "Source array was not long enough.");
    }
    if (static_cast<int64_t>(dstOffset) + count > dstBytes)
    {
        il2cpp_throw_new(kArgumentChain,
                         "Destination array was not long enough.");
    }
    if (count == 0)
    {
        return; // validated no-op (offsets may sit one past the end)
    }

    std::memmove(d->data + dstOffset, s->data + srcOffset,
                 static_cast<size_t>(count));
}

//------------------------------------------------------------------------------
// il2cpp_buffer_byte_length -- Buffer.ByteLength (see the header contract).
//   elemSize * length in int64 (the check itself cannot overflow); a result
//   beyond int32 raises OverflowException, matching .NET's checked int
//   conversion. Reference-element arrays are rejected with ArgumentException
//   (net48 Arg_MustBePrimArray) -- a reference array is a sequence of GC
//   roots, not a raw byte payload.
//------------------------------------------------------------------------------
int32_t il2cpp_buffer_byte_length(Il2CppObject* arr)
{
    Il2CppArray* a = RequireArray(arr);
    if (a->isRefElem)
    {
        il2cpp_throw_new(kArgumentChain, "Array is not a primitive type.");
    }
    const int64_t bytes =
        static_cast<int64_t>(a->length) * static_cast<int64_t>(a->elemSize);
    if (bytes > INT32_MAX)
    {
        il2cpp_throw_new(kOverflowChain, "Array is too large.");
    }
    return static_cast<int32_t>(bytes);
}

//------------------------------------------------------------------------------
// il2cpp_buffer_memory_copy -- raw memmove primitive (see the header
//   contract). std::memmove is overlap-safe by definition.
//------------------------------------------------------------------------------
void il2cpp_buffer_memory_copy(void* dst, const void* src, size_t count)
{
    std::memmove(dst, src, count);
}

//------------------------------------------------------------------------------
// il2cpp_buffer_memory_copy_sized -- Buffer.MemoryCopy (net48 sized form, see
//   the header contract). Validates the size arguments exactly as net48 does:
//     sourceBytesToCopy > destinationSizeInBytes -> ArgumentOutOfRangeException
//     sourceBytesToCopy < 0                      -> OverflowException (net48's
//                                                   checked long->nuint cast)
//   then delegates to the raw memmove primitive (overlap-safe). The pointed-to
//   buffers are trusted, matching net48 (only the size parameters are checked).
//------------------------------------------------------------------------------
void il2cpp_buffer_memory_copy_sized(void* dst, const void* src,
                                     int64_t destinationSizeInBytes,
                                     int64_t sourceBytesToCopy)
{
    if (sourceBytesToCopy > destinationSizeInBytes)
    {
        il2cpp_throw_new(kArgOutOfRangeChain,
                         "sourceBytesToCopy exceeds the destination buffer size.");
    }
    if (sourceBytesToCopy < 0)
    {
        il2cpp_throw_new(kOverflowChain,
                         "sourceBytesToCopy must not be negative.");
    }
    il2cpp_buffer_memory_copy(dst, src,
                              static_cast<size_t>(sourceBytesToCopy));
}

} // namespace runtime
} // namespace il2cpp
