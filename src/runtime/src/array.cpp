//------------------------------------------------------------------------------
// array.cpp -- managed array creation and bounds-checked element access.
//
// The array class descriptor is a per-element-type runtime descriptor whose
// name follows the .NET convention (a leading '[' marks an array class). The
// collector identifies arrays by that prefix and scans the payload as element
// references when isRefElem is set (see gc.cpp ScanObject). Arrays are
// variable-size GC objects: allocation goes through il2cpp_gc_alloc_size
// with the header + length*elemSize extent.
//
// M3 scope: one-dimensional arrays; int32 and reference elements. Bounds
// checking throws a catchable .NET exception (IndexOutOfRangeException; a
// null receiver is a NullReferenceException), so managed try/catch can
// observe out-of-range access -- the old loud abort was not catchable. The
// helper's contract is stable: out-of-range access never touches memory
// outside the array.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_index_out_of_range_exception / _null_reference_
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

namespace il2cpp {

namespace {

// The int32 array class descriptor (the only primitive array M3 supports):
//   name      "[System.Int32"  -- '[' prefix marks an array class (GC scan)
//   instanceSize: header + 1-element stub (payload is size-extended)
//   refFieldBitmap: 0 (no fixed reference fields -- the payload is scanned
//                   as VALUE data; reference arrays have their own class)
Il2CppClass g_int32ArrayClass = {
    "[System.Int32",              // name: array-class marker prefix '['
    static_cast<int32_t>(sizeof(Il2CppArray)), // instanceSize (header + stub)
    0u,                           // refFieldBitmap
    0,                            // refFieldCount
    nullptr,                      // baseClass
    nullptr,                      // vtable
    0,                            // vtableSize
    nullptr,                      // interfaces
    0,                            // interfaceCount
};

// The OBJECT array class descriptor (reference elements): the payload is
// scanned as GC references by the collector.
Il2CppClass g_objectArrayClass = {
    "[System.Object",             // name: array-class marker prefix '['
    static_cast<int32_t>(sizeof(Il2CppArray)),
    0u,                           // refFieldBitmap (payload scanned dynamically)
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

//------------------------------------------------------------------------------
// D-TYPG-1: element-type-aware array class descriptors.
//
// The transpiler passes the ELEMENT class at newarr (a reference-element
// array knows its element type at compile time), so the runtime can stamp the
// array with an element-aware descriptor name "[<element full name>" instead
// of the shared "[System.Object". GetType() then reports the ELEMENT short
// name (string[] -> "[System.String" -> "String[]"; net48 parity). Descriptors
// are synthesized on first use into a small fixed pool (a program's distinct
// reference element types are bounded) and registered in the type registry so
// il2cpp_class_by_name / isinst lookups resolve them like any class.
//------------------------------------------------------------------------------

// Upper bound on distinct reference element types a program may allocate.
constexpr int32_t kMaxElementArrayClasses = 64;
constexpr size_t  kElementArrayNameMax = 96;

struct ElementArrayClassSlot
{
    char        name[kElementArrayNameMax]; // "[<element full name>"
    Il2CppClass klass;
};

ElementArrayClassSlot g_elementArrayClasses[kMaxElementArrayClasses];
int32_t g_elementArrayClassCount = 0;
std::mutex g_elementArrayMutex; // guards the pool (array alloc is multi-threaded)

// Look up the synthesized array class for an element class, creating and
// registering it on first use. Null element -> the shared object-array class.
Il2CppClass* ArrayClassForElement(Il2CppClass* elemClass)
{
    if (elemClass == nullptr || elemClass->name == nullptr)
    {
        return &g_objectArrayClass;
    }
    // System.Object elements reuse the shared object-array descriptor: its
    // name "[System.Object" is already the element-aware form.
    if (std::strcmp(elemClass->name, "System.Object") == 0)
    {
        return &g_objectArrayClass;
    }
    std::lock_guard<std::mutex> lock(g_elementArrayMutex);
    for (int32_t i = 0; i < g_elementArrayClassCount; ++i)
    {
        if (std::strcmp(g_elementArrayClasses[i].klass.name + 1, elemClass->name) == 0)
        {
            return &g_elementArrayClasses[i].klass;
        }
    }
    if (g_elementArrayClassCount >= kMaxElementArrayClasses)
    {
        std::fprintf(stderr,
                     "il2cpp: element-array class pool exhausted (%d element types)\n",
                     kMaxElementArrayClasses);
        std::abort();
    }
    ElementArrayClassSlot& slot = g_elementArrayClasses[g_elementArrayClassCount];
    std::snprintf(slot.name, kElementArrayNameMax, "[%s", elemClass->name);
    slot.klass.name = slot.name;
    slot.klass.instanceSize = static_cast<int32_t>(sizeof(Il2CppArray));
    slot.klass.refFieldBitmap = 0u; // payload scanned dynamically via isRefElem
    slot.klass.refFieldCount = 0;
    slot.klass.baseClass = nullptr;
    slot.klass.vtable = nullptr;
    slot.klass.vtableSize = 0;
    slot.klass.interfaces = nullptr;
    slot.klass.interfaceCount = 0;
    il2cpp_class_register(&slot.klass);
    ++g_elementArrayClassCount;
    return &slot.klass;
}

bool g_arraysRegistered = false;

void EnsureArraysRegistered()
{
    if (g_arraysRegistered) return;
    il2cpp_class_register(&g_int32ArrayClass);
    il2cpp_class_register(&g_objectArrayClass);
    g_arraysRegistered = true;
}

// Total allocation size: the true payload extent -- offsetof(data) + length *
// elemSize. NOTE: sizeof(Il2CppArray) must NOT be used as the base here -- it
// rounds up to the struct's alignment (the char data[1] stub sits at a
// non-zero offset), which under-allocates by (alignment - dataOffset) bytes.
// A reference-array scan (8 bytes per slot) would then read past the real
// allocation boundary into the next block's first bytes, and the GC would
// treat garbage as an object pointer (audit C4 / gcstress crash).
size_t ArrayAllocSize(int32_t elemSize, int32_t length)
{
    const size_t base = offsetof(Il2CppArray, data);
    const size_t extra = static_cast<size_t>(length) * static_cast<size_t>(elemSize);
    return base + extra;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_array_new -- zero-initialized array allocation (see header).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_array_new(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t length)
{
    EnsureArraysRegistered();
    if (length < 0)
    {
        length = 0; // defensive: negative length is a caller bug
    }
    Il2CppClass* arrayClass = isRefElement ? ArrayClassForElement(elemClass) : &g_int32ArrayClass;
    // For reference arrays, the element descriptor is recorded on the array
    // (used by future cast checks); the class itself is the element-aware
    // object array class (D-TYPG-1: string[] stamps "[System.String", so
    // GetType().Name reports "String[]" -- net48 parity).
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(
        il2cpp_gc_alloc_size(arrayClass, ArrayAllocSize(elemSize, length)));
    arr->length = length;
    arr->elemSize = elemSize;
    arr->elemClass = isRefElement ? elemClass : nullptr;
    arr->isRefElem = isRefElement;
    arr->rank = 1; // one-dimensional scope; multi-dimensional support is future work
    return arr;
}

//------------------------------------------------------------------------------
// il2cpp_array_fail -- COLD failure path for the inline element access.
// The inline hot path in il2cpp_array.h performs the null / bounds test and
// reaches this function only on failure; keeping it out-of-line keeps the
// throw machinery out of every hot loop. The failure throws a catchable
// managed exception (NullReferenceException for a null array, otherwise
// IndexOutOfRangeException) -- never a process abort, so generated
// try/catch can handle out-of-range access (.NET semantics).
//------------------------------------------------------------------------------
void il2cpp_array_fail(Il2CppArray* arr, int32_t index)
{
    if (arr == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    else
    {
        // The index is diagnostic-only here (the managed exception OBJECT
        // task carries full detail); the parameter stays for the API shape.
        (void)index;
        il2cpp_throw_index_out_of_range_exception(nullptr);
    }
    // il2cpp_throw_* throws a C++ exception and never returns; the abort is
    // a defensive fallback that also silences -Winvalid-noreturn (the same
    // pattern as monitor.cpp ThrowSynchronizationLockException).
    std::abort();
}

} // namespace il2cpp
