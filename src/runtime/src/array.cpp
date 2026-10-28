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

#include "decimal.h" // b5-gaps 13: the 16-byte value-struct cell families
#include "guid.h"
#include "il2cpp_dtoffset.h"
#include "il2cpp_exception_ext.h" // il2cpp_throw_index_out_of_range_exception / _null_reference_
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h" // nb9 D10: primitive element descriptors (elemKind)
#include "il2cpp_runtime.h"
#include "il2cpp_time.h"
#include "il2cpp_array_md_vt.h" // the value-element multidim accessors defined below

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
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

// A3: DateTime[]/TimeSpan[] keep their OWN array class descriptor (name
// "[System.DateTime" / "[System.TimeSpan"). The compiler-emitted castclass
// for `(DateTime[])o` resolves the target class by that name, so the allocator
// must stamp exactly this class -- degrading to the int32 array class would
// make a valid downcast throw InvalidCastException while `(int[])o` passed.
// Only the NAME is needed here: ArrayClassForElement synthesizes (and
// registers) "[System.DateTime" from it; the element descriptor itself never
// enters the registry (no boxed DateTime exists in this runtime).
Il2CppClass g_datetimeElemClass = {
    "System.DateTime",
    16,
    0u,
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

Il2CppClass g_timespanElemClass = {
    "System.TimeSpan",
    16,
    0u,
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
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
    bool        isRef;
    Il2CppClass klass;
};

ElementArrayClassSlot g_elementArrayClasses[kMaxElementArrayClasses];
int32_t g_elementArrayClassCount = 0;
std::mutex g_elementArrayMutex; // guards the pool (array alloc is multi-threaded)

// Look up the synthesized array class for an element class, creating and
// registering it on first use. Null element -> the shared object-array class.
Il2CppClass* ArrayClassForElement(Il2CppClass* elemClass, bool isRef = true)
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
        if (g_elementArrayClasses[i].isRef == isRef &&
            std::strcmp(g_elementArrayClasses[i].klass.name + 1, elemClass->name) == 0)
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
    // nb9 E3: a reference-element array is assignable to object[] (and via
    // object[]'s own base chain to Array), exactly like net48; the shared
    // object-array descriptor is the base of every synthesized ref array.
    slot.isRef = isRef;
    // nb9 F1: value-element arrays are NOT assignable to object[]; reference
    // arrays derive from the shared object-array descriptor (E3).
    slot.klass.baseClass = isRef ? &g_objectArrayClass : nullptr;
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
    // nb9 F1/typed-cast: value arrays with a known element descriptor get
    // their own array class ("[System.Int64" etc.) so Array -> T[] casts
    // conform; int32[] keeps the fixed descriptor.
    if (!isRefElement && elemClass != nullptr && elemClass != &System_Int32_class)
        arrayClass = ArrayClassForElement(elemClass, false);
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
    // nb9 D10: record the primitive element kind from the descriptor the
    // transpiler passes for value-element arrays (int32/int64/single/double).
    arr->elemKind = kElemKindNone;
    if (!isRefElement)
    {
        if (elemClass == &System_Int32_class) arr->elemKind = kElemKindI4;
        else if (elemClass == &System_Int64_class) arr->elemKind = kElemKindI8;
        else if (elemClass == &System_Single_class) arr->elemKind = kElemKindF;
        else if (elemClass == &System_Double_class) arr->elemKind = kElemKindD;
        else if (elemClass == &System_UInt32_class) arr->elemKind = kElemKindU4;
    }
    arr->rank = 1; // one-dimensional scope; multi-dimensional support is future work
    arr->dims[0] = length; // single-dim fills dims[0] (rank-aware GetLength/GetUpperBound parity)
    return arr;
}

//------------------------------------------------------------------------------
// il2cpp_array_new_typed -- the runtime's own value-array builders (List<T>
// ToArray, LINQ materializers, Queue/Stack snapshots) and the transpiler's
// DateTime[]/TimeSpan[] newarr. Same allocation as il2cpp_array_new, plus the
// caller-supplied element kind: without it an 8-byte cell is ambiguous
// (int64/double vs a DateTime/user struct) and the element-kind Array scans
// have to reject it. Kind 6/7 also supplies the element NAME so the array
// keeps its "[System.DateTime"/"[System.TimeSpan" class identity.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_array_new_typed(int32_t elemSize, int32_t elemKind, int32_t length)
{
    Il2CppClass* elemClass = elemKind == 6 ? &g_datetimeElemClass
                          : elemKind == 7 ? &g_timespanElemClass
                                          : nullptr;
    Il2CppArray* arr = il2cpp_array_new(elemClass, elemSize, false, length);
    arr->elemKind = elemKind;
    return arr;
}

//------------------------------------------------------------------------------
// il2cpp_array_new_multi -- multi-dimensional array allocation (GN-3).
// Per-dimension sizes are stored in the header (dims[0..rank-1]); the element
// payload is ROW-MAJOR FLAT: element (i0, i1, ..., iN-1) sits at flat offset
//   i0*(d1*d2*...*dN-1) + i1*(d2*...*dN-1) + ... + iN-1
// (row-major = last dimension varies fastest, net48 Array layout). 'length'
// carries the TOTAL element count (product of dims), so:
//   * GC scan (gc.cpp:619-631) reads arr->data for arr->length slots via
//     FIELD access -- the multidim payload is scanned as one flat run (the
//     dims header field shifts offsetof(data) but every consumer uses the
//     field / offsetof, so the scan and the flat-stelem initializer fold
//     behave exactly as a 1-D array);
//   * Array.Length returns the total count (net48 parity);
//   * Array.GetLength(dim)/GetUpperBound(dim) read dims[dim] / dims[dim]-1
//     (rank-aware; the single-dim path reads dims[0] == length).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_array_new_multi(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement,
                                    int32_t rank, const int32_t* dims)
{
    EnsureArraysRegistered();
    if (rank < 1 || rank > IL2CPP_MAX_ARRAY_RANK)
    {
        // Loud, non-catchable caller-bug guard (the transpiler only emits the
        // rank-fixed allocators it sees; a rank beyond the compile-time ceiling
        // is a programming error, not a runtime condition).
        std::fprintf(stderr, "il2cpp: unsupported array rank %d (max %d)\n", rank,
                     static_cast<int>(IL2CPP_MAX_ARRAY_RANK));
        std::abort();
    }
    int64_t total = 1;
    for (int32_t d = 0; d < rank; ++d)
    {
        const int32_t size = dims[d] < 0 ? 0 : dims[d]; // defensive: negative size is a caller bug
        total *= static_cast<int64_t>(size);
        if (total > static_cast<int64_t>(std::numeric_limits<int32_t>::max()))
        {
            std::fprintf(stderr, "il2cpp: multidim array length overflow (product of dims)\n");
            std::abort();
        }
    }
    const int32_t length = static_cast<int32_t>(total);
    Il2CppClass* arrayClass = isRefElement ? ArrayClassForElement(elemClass) : &g_int32ArrayClass;
    // nb9 F1/typed-cast: value arrays with a known element descriptor get
    // their own array class ("[System.Int64" etc.) so Array -> T[] casts
    // conform; int32[] keeps the fixed descriptor.
    if (!isRefElement && elemClass != nullptr && elemClass != &System_Int32_class)
        arrayClass = ArrayClassForElement(elemClass, false);
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(
        il2cpp_gc_alloc_size(arrayClass, ArrayAllocSize(elemSize, length)));
    arr->length = length;
    arr->elemSize = elemSize;
    arr->elemClass = isRefElement ? elemClass : nullptr;
    arr->isRefElem = isRefElement;
    arr->rank = rank;
    for (int32_t d = 0; d < rank; ++d)
    {
        arr->dims[d] = dims[d] < 0 ? 0 : dims[d];
    }
    return arr;
}

namespace {

// Flat row-major offset for a multidim element index set. The caller passes
// the receiver array (null -> NullReferenceException via the COLD failure
// path) and the per-dim indices; bounds are checked per-dimension and an
// out-of-range index throws IndexOutOfRangeException (net48 semantics). The
// per-dim lengths come from the header dims field.
int32_t MultidimFlatOffset(Il2CppArray* arr, int32_t rank, const int32_t* idx)
{
    // Null receiver -> NullReferenceException (never touches arr fields).
    if (arr == nullptr)
    {
        il2cpp_array_fail(arr, idx[0]);
    }
    int32_t flat = 0;
    for (int32_t d = 0; d < rank; ++d)
    {
        if (idx[d] < 0 || idx[d] >= arr->dims[d])
        {
            il2cpp_array_fail(arr, idx[d]);
        }
        flat = flat * arr->dims[d] + idx[d];
    }
    return flat;
}

} // namespace

int32_t il2cpp_array_get_i4_2d(Il2CppArray* arr, int32_t i, int32_t j)
{
    const int32_t idx[2] = { i, j };
    const int32_t flat = MultidimFlatOffset(arr, 2, idx);
    return *reinterpret_cast<const int32_t*>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));
}

int32_t il2cpp_array_get_i4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k)
{
    const int32_t idx[3] = { i, j, k };
    const int32_t flat = MultidimFlatOffset(arr, 3, idx);
    return *reinterpret_cast<const int32_t*>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));
}

void il2cpp_array_set_i4_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value)
{
    const int32_t idx[2] = { i, j };
    const int32_t flat = MultidimFlatOffset(arr, 2, idx);
    *reinterpret_cast<int32_t*>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value;
}

void il2cpp_array_set_i4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value)
{
    const int32_t idx[3] = { i, j, k };
    const int32_t flat = MultidimFlatOffset(arr, 3, idx);
    *reinterpret_cast<int32_t*>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value;
}

//------------------------------------------------------------------------------
// b5-gaps 13: the VALUE-ELEMENT multidim accessor family. The allocation
// layer (il2cpp_array_new_multi) already carries the element stride and the
// zeroed-payload semantics for any elemSize; what was missing is the
// width/kind-correct ELEMENT ACCESS for the value families (the int32-only
// i4 accessors above read 4 bytes of every cell regardless of the real
// stride). The cell families below follow the SZARRAY token-accessor
// conventions:
//   * narrow cells (u1/i1/u2/i2/u4) widen to the int32 carrier on read and
//     narrow the int32 value on write (the enum int32 carrier model);
//   * i8/f4/f8 keep their native cell width;
//   * DateTime/TimeSpan cells are the 8-byte packed image (kind in the top
//     bits -- the il2cpp_datetime_from/to_array_bits converters);
//   * Guid/Decimal/DateTimeOffset cells move the full 16-byte runtime value
//     struct (the same POD the SZARRAY guid/dec/dto arms assign).
//------------------------------------------------------------------------------
namespace {

// Native-width cells: the accessor signature IS the cell type (i8/f4/f8).
#define IL2CPP_MD_CELL_NATIVE_ACCESSORS_2D(SUFFIX, CELL)                                       \
    CELL il2cpp_array_get_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j)                \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        return *reinterpret_cast<const CELL*>(                                                 \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j, CELL value)    \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        *reinterpret_cast<CELL*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value; \
    }

#define IL2CPP_MD_CELL_NATIVE_ACCESSORS_3D(SUFFIX, CELL)                                       \
    CELL il2cpp_array_get_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k)     \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        return *reinterpret_cast<const CELL*>(                                                 \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, CELL value) \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        *reinterpret_cast<CELL*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value; \
    }

// Narrow cells: the carrier is int32 on both sides (the enum int32 model) --
// the read widens the raw cell, the write narrows the int32 value.
#define IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(SUFFIX, CELL)                                       \
    int32_t il2cpp_array_get_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j)             \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        return static_cast<int32_t>(*reinterpret_cast<const CELL*>(                            \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)));      \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value) \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        *reinterpret_cast<CELL*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) =      \
            static_cast<CELL>(value);                                                          \
    }

#define IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(SUFFIX, CELL)                                       \
    int32_t il2cpp_array_get_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k)  \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        return static_cast<int32_t>(*reinterpret_cast<const CELL*>(                            \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)));      \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value) \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        *reinterpret_cast<CELL*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) =      \
            static_cast<CELL>(value);                                                          \
    }

#define IL2CPP_MD_BITS_ACCESSORS_2D(SUFFIX, STRUCT, FROM_BITS, TO_BITS)                        \
    runtime::STRUCT il2cpp_array_get_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j)     \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        const int64_t bits = *reinterpret_cast<const int64_t*>(                                \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
        return FROM_BITS(bits);                                                                \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j, runtime::STRUCT value) \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        *reinterpret_cast<int64_t*>(                                                           \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = TO_BITS(value); \
    }

#define IL2CPP_MD_BITS_ACCESSORS_3D(SUFFIX, STRUCT, FROM_BITS, TO_BITS)                        \
    runtime::STRUCT il2cpp_array_get_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k) \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        const int64_t bits = *reinterpret_cast<const int64_t*>(                                \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
        return FROM_BITS(bits);                                                                \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, runtime::STRUCT value) \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        *reinterpret_cast<int64_t*>(                                                           \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = TO_BITS(value); \
    }

// The 16-byte struct-cell families: TYPE is the fully qualified runtime
// value struct (Il2CppCodeGen lives in il2cpp::runtime, Il2CppGuid in
// il2cpp directly -- the caller passes the qualified name).
#define IL2CPP_MD_STRUCT_ACCESSORS_2D(SUFFIX, TYPE)                                            \
    TYPE il2cpp_array_get_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j)                \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        return *reinterpret_cast<const TYPE*>(                                                 \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_2d(Il2CppArray* arr, int32_t i, int32_t j, TYPE value)    \
    {                                                                                          \
        const int32_t idx[2] = { i, j };                                                       \
        const int32_t flat = MultidimFlatOffset(arr, 2, idx);                                  \
        *reinterpret_cast<TYPE*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value; \
    }

#define IL2CPP_MD_STRUCT_ACCESSORS_3D(SUFFIX, TYPE)                                            \
    TYPE il2cpp_array_get_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k)     \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        return *reinterpret_cast<const TYPE*>(                                                 \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));       \
    }                                                                                          \
    void il2cpp_array_set_##SUFFIX##_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, TYPE value) \
    {                                                                                          \
        const int32_t idx[3] = { i, j, k };                                                    \
        const int32_t flat = MultidimFlatOffset(arr, 3, idx);                                  \
        *reinterpret_cast<TYPE*>(                                                              \
            arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize)) = value; \
    }

} // namespace

// The narrow-cell families (the enum underlying widths): int32 carrier both
// sides, the raw cell is the underlying width.
IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(u1, uint8_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(i1, int8_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(u2, uint16_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(i2, int16_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_2D(u4, uint32_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(u1, uint8_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(i1, int8_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(u2, uint16_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(i2, int16_t)
IL2CPP_MD_CELL_NARROW_ACCESSORS_3D(u4, uint32_t)

// The native-width cells (the non-int32 primitives).
IL2CPP_MD_CELL_NATIVE_ACCESSORS_2D(i8, int64_t)
IL2CPP_MD_CELL_NATIVE_ACCESSORS_2D(f4, float)
IL2CPP_MD_CELL_NATIVE_ACCESSORS_2D(f8, double)
IL2CPP_MD_CELL_NATIVE_ACCESSORS_3D(i8, int64_t)
IL2CPP_MD_CELL_NATIVE_ACCESSORS_3D(f4, float)
IL2CPP_MD_CELL_NATIVE_ACCESSORS_3D(f8, double)

// The 8-byte packed-image families (DateTime keeps its 2-bit kind in the top
// of the word; TimeSpan is the raw ticks).
IL2CPP_MD_BITS_ACCESSORS_2D(dt, Il2CppDateTime, runtime::il2cpp_datetime_from_array_bits,
                            runtime::il2cpp_datetime_to_array_bits)
IL2CPP_MD_BITS_ACCESSORS_2D(ts, Il2CppTimeSpan, [](int64_t bits) { return runtime::Il2CppTimeSpan(bits); },
                            [](const runtime::Il2CppTimeSpan& v) { return v.ticks; })
IL2CPP_MD_BITS_ACCESSORS_3D(dt, Il2CppDateTime, runtime::il2cpp_datetime_from_array_bits,
                            runtime::il2cpp_datetime_to_array_bits)
IL2CPP_MD_BITS_ACCESSORS_3D(ts, Il2CppTimeSpan, [](int64_t bits) { return runtime::Il2CppTimeSpan(bits); },
                            [](const runtime::Il2CppTimeSpan& v) { return v.ticks; })

// The 16-byte value-struct families (the same POD assignment the SZARRAY
// dec/guid/dto arms use).
IL2CPP_MD_STRUCT_ACCESSORS_2D(dec, runtime::Il2CppDecimal)
IL2CPP_MD_STRUCT_ACCESSORS_2D(guid, Il2CppGuid)
IL2CPP_MD_STRUCT_ACCESSORS_2D(dto, runtime::Il2CppDateTimeOffset)
IL2CPP_MD_STRUCT_ACCESSORS_3D(dec, runtime::Il2CppDecimal)
IL2CPP_MD_STRUCT_ACCESSORS_3D(guid, Il2CppGuid)
IL2CPP_MD_STRUCT_ACCESSORS_3D(dto, runtime::Il2CppDateTimeOffset)

// The element-ADDRESS form (the multidim ldelema: `arr[i,j] = new T(...)`
// lowers to ldelema + the in-place ctor writing through the address). The
// per-dim bounds checks are identical to the typed accessors; the result is
// the raw cell pointer the 1-D il2cpp_array_get_elem_ptr yields.
intptr_t il2cpp_array_md_elem_ptr_2d(Il2CppArray* arr, int32_t i, int32_t j)
{
    const int32_t idx[2] = { i, j };
    const int32_t flat = MultidimFlatOffset(arr, 2, idx);
    return reinterpret_cast<intptr_t>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));
}

intptr_t il2cpp_array_md_elem_ptr_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k)
{
    const int32_t idx[3] = { i, j, k };
    const int32_t flat = MultidimFlatOffset(arr, 3, idx);
    return reinterpret_cast<intptr_t>(arr->data + static_cast<size_t>(flat) * static_cast<size_t>(arr->elemSize));
}

//------------------------------------------------------------------------------
// il2cpp_array_new_multi_{v|ref}{2|3}d -- rank-fixed wrappers over the
// generic allocator (the transpiler emits these exact names; the {v|ref} tag
// mirrors the isRefElement argument the codegen derives from the element
// suffix, and <N>d is the rank).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_array_new_multi_v2d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1)
{
    const int32_t dims[2] = { d0, d1 };
    return il2cpp_array_new_multi(elemClass, elemSize, isRefElement, 2, dims);
}

Il2CppArray* il2cpp_array_new_multi_v3d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1, int32_t d2)
{
    const int32_t dims[3] = { d0, d1, d2 };
    return il2cpp_array_new_multi(elemClass, elemSize, isRefElement, 3, dims);
}

Il2CppArray* il2cpp_array_new_multi_ref2d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1)
{
    const int32_t dims[2] = { d0, d1 };
    return il2cpp_array_new_multi(elemClass, elemSize, isRefElement, 2, dims);
}

Il2CppArray* il2cpp_array_new_multi_ref3d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1, int32_t d2)
{
    const int32_t dims[3] = { d0, d1, d2 };
    return il2cpp_array_new_multi(elemClass, elemSize, isRefElement, 3, dims);
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
