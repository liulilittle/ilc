//------------------------------------------------------------------------------
// marshalext.cpp -- P/Invoke struct marshalling primitives (M4, see
// il2cpp_marshal_ext.h for the full contract).
//
// This layer implements the memory-block primitives behind the future
// struct-parameter support: an M4 struct is a byte buffer of consecutive
// int32 slots, allocated with calloc (the icall_marshal_alloc_hglobal
// mechanism) and returned across the boundary as an int64 pointer TOKEN
// carrying the FULL native address -- the runtime never truncates (the M4
// int32-ABI truncation is a transpiler-side decision, see the header).
//
// Every allocation is tracked in a bounded flat registry ({ptr, size},
// single-threaded by contract, pinvokeext callback-table style). Field
// reads/writes and array conversions bound-check the byte range against the
// registered size, so out-of-range indices raise ArgumentOutOfRangeException
// and foreign/double-freed pointers abort loudly (fail loud, never silent).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp {
namespace runtime {

namespace {

// Bounded allocation registry: every live struct buffer plus its byte size
// (the bounds-defense source of truth). Flat static storage (constraint #12
// -- no global-constructor allocation); overflow aborts loudly (the
// transpiler emits a bounded set of live struct buffers per call frame).
constexpr int32_t kMaxStructs = 256;

struct StructEntry
{
    void*   ptr;  // the native buffer (registered at alloc_struct)
    int32_t size; // byte size (always > 0)
};

StructEntry g_structs[kMaxStructs];
int32_t     g_structCount = 0;

// Look up 'ptr' in the registry; returns the registered byte size, or -1
// when the pointer is unknown (foreign buffer / already freed).
int32_t FindStructSize(void* ptr)
{
    for (int32_t i = 0; i < g_structCount; ++i)
    {
        if (g_structs[i].ptr == ptr)
        {
            return g_structs[i].size;
        }
    }
    return -1;
}

// The int32 array class descriptor used for struct -> int32[] conversion
// (mirrors array.cpp's internal descriptor; passed to il2cpp_array_new).
Il2CppClass g_int32ArrayClass = {
    "[System.Int32",
    static_cast<int32_t>(sizeof(Il2CppArray)),
    0u,   // refFieldBitmap
    0,    // refFieldCount
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

} // namespace

//------------------------------------------------------------------------------
// icall_marshal_size_of_struct -- M4 all-int32 struct layout size (see
// header). Size = round_up(fieldCount * 4, 8); fieldSizes, when provided,
// must consist entirely of 4-byte entries (the M4 contract).
//------------------------------------------------------------------------------
int32_t icall_marshal_size_of_struct(int32_t fieldCount, int32_t* fieldSizes)
{
    if (fieldCount < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("fieldCount", nullptr);
    }
    if (fieldCount > (INT32_MAX - 7) / 4)
    {
        // fieldCount*4 + alignment would overflow int32 -- the layout cannot
        // be represented in this ABI (mirrors the checked-arithmetic policy).
        il2cpp_throw_argument_out_of_range_exception("fieldCount", nullptr);
    }
    if (fieldSizes != nullptr)
    {
        for (int32_t i = 0; i < fieldCount; ++i)
        {
            if (fieldSizes[i] != 4)
            {
                il2cpp_throw_not_supported_exception(
                    "M4 struct fields must all be int32 (4 bytes)");
            }
        }
    }
    const int32_t raw = fieldCount * 4;
    return (raw + 7) & ~7;
}

//------------------------------------------------------------------------------
// icall_marshal_alloc_struct -- allocate a struct buffer (calloc-backed,
// zeroed) and return its int64 pointer token (full address, see header).
//------------------------------------------------------------------------------
int64_t icall_marshal_alloc_struct(int32_t size)
{
    if (size <= 0)
    {
        return 0; // "no buffer" token (mirrors alloc_hglobal's null on negative size)
    }
    void* ptr = std::calloc(1, static_cast<size_t>(size));
    if (ptr == nullptr)
    {
        std::fprintf(stderr, "icall_marshal_alloc_struct: out of memory\n");
        std::abort();
    }
    if (g_structCount >= kMaxStructs)
    {
        std::fprintf(stderr, "icall_marshal_alloc_struct: registry overflow\n");
        std::free(ptr);
        std::abort();
    }
    g_structs[g_structCount].ptr = ptr;
    g_structs[g_structCount].size = size;
    ++g_structCount;
    return static_cast<int64_t>(reinterpret_cast<uintptr_t>(ptr));
}

//------------------------------------------------------------------------------
// Resolve a struct pointer: null -> ArgumentNullException; unknown pointer
// -> abort loudly (foreign buffer / already freed = memory corruption).
// Returns the registered byte size.
//------------------------------------------------------------------------------
int32_t ResolveStruct(void* structPtr)
{
    if (structPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("structPtr");
    }
    const int32_t size = FindStructSize(structPtr);
    if (size < 0)
    {
        std::fprintf(stderr, "marshal struct icall: pointer not tracked by "
                             "icall_marshal_alloc_struct (foreign buffer or double free)\n");
        std::abort();
    }
    return size;
}

//------------------------------------------------------------------------------
// Bounds defense shared by the field icalls: the byte range of field
// 'fieldIndex' must fit inside the registered buffer (see header).
//------------------------------------------------------------------------------
void CheckFieldRange(int32_t fieldIndex, int32_t size)
{
    if (fieldIndex < 0 ||
        static_cast<int64_t>(fieldIndex) * 4 + 4 > static_cast<int64_t>(size))
    {
        il2cpp_throw_argument_out_of_range_exception("fieldIndex", nullptr);
    }
}

//------------------------------------------------------------------------------
// icall_marshal_write_int32_field / read -- slot access at offset i*4.
//------------------------------------------------------------------------------
void icall_marshal_write_int32_field(void* structPtr, int32_t fieldIndex, int32_t value)
{
    const int32_t size = ResolveStruct(structPtr);
    CheckFieldRange(fieldIndex, size);
    reinterpret_cast<int32_t*>(structPtr)[fieldIndex] = value;
}

int32_t icall_marshal_read_int32_field(void* structPtr, int32_t fieldIndex)
{
    const int32_t size = ResolveStruct(structPtr);
    CheckFieldRange(fieldIndex, size);
    return reinterpret_cast<int32_t*>(structPtr)[fieldIndex];
}

//------------------------------------------------------------------------------
// icall_marshal_struct_to_int32_array -- copy slots 0..fieldCount-1 into a
// new managed int32[] (GC allocation through il2cpp_array_new).
//------------------------------------------------------------------------------
Il2CppObject* icall_marshal_struct_to_int32_array(void* structPtr, int32_t fieldCount)
{
    const int32_t size = ResolveStruct(structPtr);
    if (fieldCount < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("fieldCount", nullptr);
    }
    if (static_cast<int64_t>(fieldCount) * 4 > static_cast<int64_t>(size))
    {
        il2cpp_throw_argument_out_of_range_exception("fieldCount", nullptr);
    }
    Il2CppArray* arr = il2cpp_array_new(&g_int32ArrayClass, 4, false, fieldCount);
    if (fieldCount > 0)
    {
        std::memcpy(arr->data, structPtr, static_cast<size_t>(fieldCount) * 4);
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// icall_marshal_int32_array_to_struct -- copy a managed int32[] into the
// struct buffer (elements 0..length-1 -> slots 0..length-1). The array
// layout is validated (array-class prefix, int32 element kind) before any
// array field is dereferenced (arrayext contract).
//------------------------------------------------------------------------------
void icall_marshal_int32_array_to_struct(Il2CppObject* array, void* structPtr)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    const int32_t size = ResolveStruct(structPtr);
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(array);
    if (arr->obj.klass == nullptr || arr->obj.klass->name == nullptr ||
        arr->obj.klass->name[0] != '[')
    {
        il2cpp_throw_argument_exception("not an array object");
    }
    if (arr->elemSize != 4 || arr->isRefElem)
    {
        il2cpp_throw_not_supported_exception("int32 arrays only");
    }
    if (static_cast<int64_t>(arr->length) * 4 > static_cast<int64_t>(size))
    {
        il2cpp_throw_argument_out_of_range_exception("array", nullptr);
    }
    if (arr->length > 0)
    {
        std::memcpy(structPtr, arr->data, static_cast<size_t>(arr->length) * 4);
    }
}

//------------------------------------------------------------------------------
// icall_marshal_free_struct -- release a tracked buffer (null -> no-op; an
// untracked pointer aborts loudly -- double free must never be silent).
//------------------------------------------------------------------------------
void icall_marshal_free_struct(void* ptr)
{
    if (ptr == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_structCount; ++i)
    {
        if (g_structs[i].ptr == ptr)
        {
            std::free(ptr);
            // Remove the entry (swap-with-last: table order is irrelevant).
            g_structs[i] = g_structs[g_structCount - 1];
            --g_structCount;
            return;
        }
    }
    std::fprintf(stderr, "icall_marshal_free_struct: pointer not tracked "
                         "(foreign buffer or double free)\n");
    std::abort();
}

//------------------------------------------------------------------------------
// D-HG-1 (2026-08-12): Marshal.ReAllocHGlobal(IntPtr pv, IntPtr cb) -- the
// realloc half of the hglobal alloc family (see il2cpp_marshal_ext.h).
//
// net48 realloc semantics:
//   * the OLD pointer is INVALID after the call (the block may MOVE -- the
//     net48 oracle confirmed ReAllocHGlobal returns a different address on
//     this dev box), and the NEW pointer must be used for every later access
//     and for FreeHGlobal;
//   * the first min(oldSize, newSize) bytes are preserved;
//   * realloc failure throws OutOfMemoryException (net48).
//
// Heap pairing: the hglobal alloc/free side (runtime.cpp icall_marshal_
// alloc_hglobal / free_hglobal) uses GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT) /
// GlobalFree on Windows and calloc/free on POSIX. Realloc pairs the same way:
// GlobalReAlloc with GMEM_MOVEABLE (lets the heap move the block -- the
// net48 LocalReAlloc equivalent; on Windows Local/Global heaps are the same
// family) / std::realloc on POSIX. GMEM_MOVEABLE returns the new handle when
// the block moves, otherwise the same pointer -- both are valid new pointers.
//------------------------------------------------------------------------------
intptr_t icall_marshal_realloc_hglobal(intptr_t ptr, intptr_t size)
{
    if (size < 0)
    {
        return 0;
    }
    void* pNew;
#if defined(_WIN32)
    pNew = GlobalReAlloc(reinterpret_cast<void*>(ptr), static_cast<SIZE_T>(size), GMEM_MOVEABLE);
#else
    pNew = std::realloc(reinterpret_cast<void*>(ptr), static_cast<size_t>(size));
#endif
    if (pNew == nullptr)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Insufficient memory to continue the execution of the program.");
    }
    return reinterpret_cast<intptr_t>(pNew);
}

} // namespace runtime
} // namespace il2cpp
