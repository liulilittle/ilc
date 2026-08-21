//------------------------------------------------------------------------------
// sortedlist.cpp -- non-generic sorted-list runtime (see sortedlist.h for the
// full layout, GC bitmap, sorting and growth contracts).
//
// Data structure:
//   Il2CppSortedList { items: Il2CppArray* (reference array), count, capacity }
//   items[0, capacity)        = keys, in ascending order (the sort contract)
//   items[capacity, 2*cap)    = values, index-aligned with their keys
//   The items array is a REFERENCE array, so the collector scans every key
//   and every value; a rooted sorted list keeps its whole contents alive.
//
// Sort contract (implemented here, documented in the header):
//   * boxed System.Int32 vs boxed System.Int32 -- by VALUE (ascending);
//   * System.String vs System.String -- ordinal comparison of UTF-16 code
//     units (unit-by-unit -- never bytewise, see the header's note; the
//     shorter string sorts first when it is a prefix);
//   * boxed Int32 vs string -- the Int32 sorts first (deterministic total
//     order for the mixed case);
//   * any other key kind -- address order (ascending pointer; reference
//     identity), keeping the total order the binary search requires.
//   All comparisons use explicit relational tests, never subtraction (no
//   signed-overflow UB).
//
// Growth: when an insert would exceed capacity, ONE new array at double
// capacity is allocated and every pair is copied with pure pointer writes
// (no allocation between the array's birth and its publication, so no
// collection can reclaim it); the old array pointer is re-read from the
// struct AFTER the allocation (Phase C may have moved it). See the header's
// GROWTH CONTRACT for the full safety argument.
//
// GC-safety notes for the code below:
//   * All reference stores go through il2cpp_write_barrier first (old ->
//     young card marking, same convention as hashtable.cpp).
//   * Bulk shifts (insert/remove) use memmove, then a per-slot barrier pass
//     over the moved region (the barrier is keyed on the SLOT address, so
//     per-slot calls after the move are exactly equivalent to per-store
//     barriers).
//   * The trailing slots left by a removal are cleared to null (with
//     barriers) so the collector never scans stale references.
//   * Exception factories come from il2cpp_exception_ext.h (chains carry the
//     type + base classes, so generated catch clauses follow .NET
//     inheritance semantics).
//------------------------------------------------------------------------------

#include "sortedlist.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

//------------------------------------------------------------------------------
// The System.Collections.SortedList runtime descriptor. GLOBAL scope (external
// linkage, OUTSIDE namespace il2cpp): the transpiler emits
// &System_Collections_SortedList_class for isinst/castclass and the newobj
// fallback (the same contract as System_Collections_Stack_class in
// stackqueue.cpp / System_Version_class in versionuri.cpp), while newobj
// routes through the *_class() accessor (the BclAllocExpr whitelist). Both
// point at this same descriptor, so isinst identity and newobj allocation
// never diverge. Registered lazily (constraint #12: no global-constructor
// allocation).
//   instanceSize 24 (obj 8 + items 8 + count 4 + capacity 4), refFieldBitmap
//   bit 1: 'items' is a GC ref.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Collections_SortedList_class = {
    "System.Collections.SortedList", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppSortedList)), // instanceSize
    0b00010u,                       // refFieldBitmap: bit 1 = items
    1,                              // refFieldCount
    nullptr,                        // baseClass
    nullptr,                        // vtable
    0,                              // vtableSize
    nullptr,                        // interfaces
    0,                              // interfaceCount
};

//------------------------------------------------------------------------------
// The snapshot enumerator runtime descriptor (D-SL-3, sldeep). GLOBAL scope
// (external linkage, OUTSIDE namespace il2cpp) like the SortedList descriptor:
// the enumerator is a managed object (Il2CppArrayEnumerator) allocated through
// il2cpp_object_new by the get_enumerator icall. instanceSize 24 (obj 8 +
// array 8 + index 4, padded to alignment), refFieldBitmap bit 1: 'array' is a
// GC ref (the snapshot array the foreach iterates must stay reachable).
// Registered lazily alongside the SortedList descriptor.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Collections_SortedList_Enumerator_class = {
    "System.Collections.SortedList.Enumerator", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppArrayEnumerator)), // instanceSize
    0b00010u,                       // refFieldBitmap: bit 1 = array
    1,                              // refFieldCount
    nullptr,                        // baseClass
    nullptr,                        // vtable
    0,                              // vtableSize
    nullptr,                        // interfaces
    0,                              // interfaceCount
};

//------------------------------------------------------------------------------
// System.IDisposable interface descriptor (D-SL-3, sldeep). The foreach over a
// non-generic collection lowers to a try/finally that emits
// `isinst System.IDisposable; if (d != null) d.Dispose();` over the
// enumerator. The snapshot enumerator does NOT implement IDisposable, so the
// isinst guard always yields null and Dispose is never invoked at runtime --
// but the generated C++ still references BOTH symbols, so the descriptor must
// exist to link. instanceSize 0 (interface: never allocated), no interfaces.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_IDisposable_class = {
    "System.IDisposable",           // name
    0,                              // instanceSize (interface: never allocated)
    0u,                             // refFieldBitmap
    0,                              // refFieldCount
    nullptr,                        // baseClass
    nullptr,                        // vtable
    0,                              // vtableSize
    nullptr,                        // interfaces
    0,                              // interfaceCount
};

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Layout verification: the class descriptor's refFieldBitmap bits below are
// literal constants; these compile-time checks pin them to the struct
// layout, so a layout change with a stale bitmap cannot link silently.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppSortedList, items) == 1 * sizeof(void*), "Il2CppSortedList: items must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppSortedList, count) == 2 * sizeof(void*), "Il2CppSortedList: count sits at slot 2 (NOT in the bitmap)");
static_assert(offsetof(Il2CppSortedList, capacity) == 2 * sizeof(void*) + sizeof(int32_t), "Il2CppSortedList: capacity sits at slot 3 (NOT in the bitmap)");

// Minimum capacity: avoids a degenerate 1-slot table for tiny capacities.
constexpr int32_t kMinCapacity = 4;

// The doubling cap: capacity * 2 must stay inside int32, and the items array
// holds 2 * capacity slots (this keeps the array length below 2^30).
constexpr int32_t kMaxCapacity = 1 << 29;

bool g_sortedListRegistered = false;

void EnsureSortedListRegistered()
{
    if (g_sortedListRegistered) return;
    il2cpp_class_register(&System_Collections_SortedList_class);
    // D-SL-3 (sldeep): register the snapshot-enumerator descriptor (GC must
    // scan its array field) and the System.IDisposable interface descriptor
    // (the foreach finally's isinst references the global symbol; class_by_name
    // lookups on "System.IDisposable" resolve once registered).
    il2cpp_class_register(&System_Collections_SortedList_Enumerator_class);
    il2cpp_class_register(&System_IDisposable_class);
    g_sortedListRegistered = true;
}

//------------------------------------------------------------------------------
// Small helpers.
//------------------------------------------------------------------------------

Il2CppSortedList* AsSortedList(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppSortedList*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors hashtable.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// Allocate the items array: 2 * capacity reference slots (Il2CppObject*
// elements, isRefElem == true -> the collector scans every element).
Il2CppArray* AllocItems(int32_t capacity)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, capacity * 2);
}

// Defensive: a sorted list whose ctor icall was skipped has a null items
// array; lazily allocate the minimum table so no icall dereferences null
// (generated code always calls the ctor -- this is misuse hardening only).
// NOTE: the allocation here can compact the container, so this MUST be
// inlined at a call site that has rooted its 'self' (GcRoot) and re-read the
// container BEFORE the store -- a by-value helper's store would go to the
// moved-out old address and be lost (stackqueue2 e2e, 2026-08-13).

void ThrowIfIndexOutOfRange(Il2CppSortedList* sl, int32_t index)
{
    if (index < 0 || index >= sl->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
}

Il2CppObject* KeyAt(Il2CppSortedList* sl, int32_t index)
{
    return reinterpret_cast<Il2CppObject* const*>(sl->items->data)[index];
}

Il2CppObject* ValueAt(Il2CppSortedList* sl, int32_t index)
{
    return reinterpret_cast<Il2CppObject* const*>(sl->items->data)[sl->capacity + index];
}

//------------------------------------------------------------------------------
// Key classification (drives the sort contract). The runtime's own
// descriptors own these names ("System.String" from string.cpp, "System.
// Int32" from boxing.cpp), so klass-name comparison is stable for every
// managed string / boxed int32 the runtime produces; the transpiled BCL's
// descriptors for the same types carry the same names.
//------------------------------------------------------------------------------

bool IsStringKey(const Il2CppObject* key)
{
    return key->klass != nullptr && std::strcmp(key->klass->name, "System.String") == 0;
}

bool IsBoxedInt32Key(const Il2CppObject* key)
{
    return key->klass != nullptr && std::strcmp(key->klass->name, "System.Int32") == 0;
}

// Read the boxed int32 payload at byte offset 8 -- the documented M2 box
// layout (klass at 0, aligned value slot at 8, see boxing.cpp). Only called
// after IsBoxedInt32Key, so the klass is the Int32 box class.
int32_t BoxedInt32Value(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(boxed) + 8);
}

//------------------------------------------------------------------------------
// CompareStringOrdinal -- ordinal comparison of two managed strings by UTF-16
// code unit (.NET Framework semantics). Unit-by-unit, NOT bytewise: memcmp
// would impose little-endian byte order and disagree with .NET for
// non-ASCII keys. The shorter string sorts first when one is a prefix.
//------------------------------------------------------------------------------
int32_t CompareStringOrdinal(const Il2CppString* a, const Il2CppString* b)
{
    const int32_t n = (a->length < b->length) ? a->length : b->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const char16_t ca = a->chars[i];
        const char16_t cb = b->chars[i];
        if (ca < cb) return -1;
        if (ca > cb) return 1;
    }
    if (a->length < b->length) return -1;
    if (a->length > b->length) return 1;
    return 0;
}

//------------------------------------------------------------------------------
// CompareKeys -- the sort contract (see the header). Returns <0 when 'a'
// sorts before 'b', 0 when equal (same key), >0 otherwise. Explicit
// relational tests only -- never subtraction (no signed-overflow UB).
//------------------------------------------------------------------------------
int32_t CompareKeys(const Il2CppObject* a, const Il2CppObject* b)
{
    const bool aInt = IsBoxedInt32Key(a);
    const bool bInt = IsBoxedInt32Key(b);
    const bool aStr = IsStringKey(a);
    const bool bStr = IsStringKey(b);
    if (aInt && bInt)
    {
        const int32_t av = BoxedInt32Value(a);
        const int32_t bv = BoxedInt32Value(b);
        return av < bv ? -1 : av > bv ? 1 : 0;
    }
    if (aStr && bStr)
    {
        return CompareStringOrdinal(reinterpret_cast<const Il2CppString*>(a),
                                    reinterpret_cast<const Il2CppString*>(b));
    }
    if (aInt && bStr) return -1; // documented total order: Int32 sorts first
    if (aStr && bInt) return 1;
    // Unknown key kinds: address order (reference identity; the total order
    // keeps the binary search sound -- see the header's sorting contract).
    const uintptr_t pa = reinterpret_cast<uintptr_t>(a);
    const uintptr_t pb = reinterpret_cast<uintptr_t>(b);
    return pa < pb ? -1 : pa > pb ? 1 : 0;
}

//------------------------------------------------------------------------------
// FindKey -- binary search over the sorted key half. *found is set when the
// key is present (compare == 0); the returned index is the insertion point
// otherwise. No allocation, so no collection can run while it executes.
//------------------------------------------------------------------------------
int32_t FindKey(const Il2CppSortedList* sl, const Il2CppObject* key, bool* found)
{
    const Il2CppObject* const* keys = reinterpret_cast<Il2CppObject* const*>(sl->items->data);
    int32_t lo = 0;
    int32_t hi = sl->count;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareKeys(keys[mid], key);
        if (c < 0) lo = mid + 1;
        else if (c > 0) hi = mid;
        else
        {
            *found = true;
            return mid;
        }
    }
    *found = false;
    return lo;
}

//------------------------------------------------------------------------------
// Grow -- rebuild the items array at double capacity (capped) and copy every
// pair with pure pointer writes.
//
// GC safety: the ONE new array is allocated first (any collection triggered
// by that allocation runs before the array exists); the container itself is
// re-read through the rooted slot AFTER the allocation (a compacting
// collection moves it -- the old slot address would lose the 'items' store);
// the copy loop performs no allocation, so no collection can run between
// the array's birth and its publication into sl->items.
//------------------------------------------------------------------------------
void Grow(Il2CppSortedList* sl)
{
    GcRoot<Il2CppSortedList*> root(sl, "icall_sortedlist_grow");
    const int32_t oldCapacity = sl->capacity;
    int32_t newCapacity = oldCapacity * 2;
    if (newCapacity > kMaxCapacity)
    {
        std::fprintf(stderr,
                     "il2cpp sorted list capacity cap (%d) reached with %d pairs\n",
                     kMaxCapacity, sl->count);
        std::abort();
    }
    Il2CppArray* fresh = AllocItems(newCapacity);
    sl = root.Get();
    Il2CppArray* old = sl->items; // re-read from the re-rooted slot AFTER the allocation
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(fresh->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(old->data);
    for (int32_t i = 0; i < sl->count; ++i)
    {
        dst[i] = src[i];
        dst[newCapacity + i] = src[oldCapacity + i];
    }
    for (int32_t i = 0; i < sl->count; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[newCapacity + i]),
                             dst[newCapacity + i]);
    }
    il2cpp_write_barrier(reinterpret_cast<void**>(&sl->items),
                         reinterpret_cast<Il2CppObject*>(fresh));
    sl->items = fresh;
    sl->capacity = newCapacity;
}

//------------------------------------------------------------------------------
// ShiftRight -- make room at sorted index 'pos' (insert): move the key and
// value tails one slot to the right. memmove first, then a per-slot barrier
// pass over the moved region (the barrier is keyed on the SLOT address, so
// this is exactly equivalent to per-store barriers). The slots at 'pos'
// still hold duplicated tail values until the caller overwrites them.
//------------------------------------------------------------------------------
void ShiftRight(Il2CppSortedList* sl, int32_t pos)
{
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
    const int32_t cap = sl->capacity;
    const int32_t tail = sl->count - pos;
    std::memmove(slots + pos + 1, slots + pos, static_cast<size_t>(tail) * sizeof(void*));
    std::memmove(slots + cap + pos + 1, slots + cap + pos,
                 static_cast<size_t>(tail) * sizeof(void*));
    for (int32_t i = pos; i <= sl->count; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[cap + i]), slots[cap + i]);
    }
}

//------------------------------------------------------------------------------
// ShiftLeft -- close the gap at sorted index 'pos' (remove): move the key
// and value tails one slot left, then CLEAR the trailing slots (the GC must
// never scan stale references left by a shrink).
//------------------------------------------------------------------------------
void ShiftLeft(Il2CppSortedList* sl, int32_t pos)
{
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
    const int32_t cap = sl->capacity;
    const int32_t tail = sl->count - pos - 1;
    std::memmove(slots + pos, slots + pos + 1, static_cast<size_t>(tail) * sizeof(void*));
    std::memmove(slots + cap + pos, slots + cap + pos + 1,
                 static_cast<size_t>(tail) * sizeof(void*));
    for (int32_t i = pos; i < sl->count; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[cap + i]), slots[cap + i]);
    }
    slots[sl->count - 1] = nullptr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[sl->count - 1]), nullptr);
    slots[cap + sl->count - 1] = nullptr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[cap + sl->count - 1]), nullptr);
}

//------------------------------------------------------------------------------
// Insert -- the shared body of Add (replaceExisting == false: a duplicate
// key raises ArgumentException and the list stays unchanged) and the indexer
// SET by key (replaceExisting == true: an existing key's value is replaced in
// place, and a missing key is inserted at its sorted position).
//------------------------------------------------------------------------------
void Insert(Il2CppSortedList* sl, Il2CppObject* key, Il2CppObject* value, bool replaceExisting)
{
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    // The defensive ensure and Grow below can allocate and compact: root the
    // container and re-read it after every allocation (Grow re-roots its own
    // parameter, but THIS function's 'sl' must be re-derived through its own
    // rooted slot).
    GcRoot<Il2CppSortedList*> root(sl, "icall_sortedlist_insert");
    if (sl->items == nullptr)
    {
        // Defensive ensure, inlined: allocate, re-read the (possibly moved)
        // container, then publish.
        Il2CppArray* arr = AllocItems(kMinCapacity);
        sl = root.Get();
        sl->capacity = kMinCapacity;
        sl->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&sl->items),
                             reinterpret_cast<Il2CppObject*>(sl->items));
    }
    bool found = false;
    const int32_t pos = FindKey(sl, key, &found);
    if (found)
    {
        if (!replaceExisting)
        {
            il2cpp_throw_argument_exception("Item has already been added.");
        }
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
        slots[sl->capacity + pos] = value;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[sl->capacity + pos]), value);
        return;
    }
    if (sl->count == sl->capacity)
    {
        Grow(sl);
        sl = root.Get(); // re-derive after the (possibly collecting) growth
    }
    ShiftRight(sl, pos);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
    slots[pos] = key;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[pos]), key);
    slots[sl->capacity + pos] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[sl->capacity + pos]), value);
    ++sl->count;
}

//------------------------------------------------------------------------------
// SnapshotKeysOrValues -- build the Keys / Values snapshot array. 'collectValues'
// selects the values region (index-aligned with the sorted keys) instead of
// the keys region. Both snapshots share the list's ascending order, so the
// i-th key and the i-th value always describe the same pair.
//
// GC safety: exactly ONE allocation (the snapshot), then a pure pointer-write
// fill with no further allocation, so no collection can reclaim the snapshot
// between its birth and the return. 'sl' stays reachable through the caller's
// 'self', and sl->items is re-read AFTER the allocation (a compacting
// collection may have moved it -- same convention as Grow).
//------------------------------------------------------------------------------
Il2CppArray* SnapshotKeysOrValues(Il2CppSortedList* sl, bool collectValues)
{
    // The snapshot allocation can compact and MOVE the container: root it and
    // re-read the rooted slot before touching any of its fields.
    GcRoot<Il2CppSortedList*> root(sl, "icall_sortedlist_snapshot");
    const int32_t n = sl->count;
    Il2CppArray* result = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, n);
    sl = root.Get();
    if (sl->items == nullptr)
    {
        return result; // empty list (ctor skipped defensively): all-null snapshot
    }
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(sl->items->data);
    const int32_t cap = sl->capacity;
    for (int32_t i = 0; i < n; ++i)
    {
        dst[i] = collectValues ? src[cap + i] : src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    return result;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_sortedlist_class -- the System.Collections.SortedList descriptor
// (registered lazily).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_sortedlist_class()
{
    EnsureSortedListRegistered();
    return &System_Collections_SortedList_class;
}

namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_sortedlist_create -- SortedList(int capacity) (see header).
//------------------------------------------------------------------------------
void il2cpp_sortedlist_create(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", nullptr);
    }
    Il2CppSortedList* sl = AsSortedList(self);
    int32_t cap = (capacity < kMinCapacity) ? kMinCapacity : capacity;
    if (cap > kMaxCapacity)
    {
        cap = kMaxCapacity;
    }
    int32_t p = 1;
    while (p < cap)
    {
        p <<= 1;
    }
    // AllocItems below may compact and MOVE 'self' (only reachable through
    // the caller's frame slot, which is rewritten): root it and re-read the
    // (possibly moved) container before writing its fields.
    GcRoot<Il2CppObject*> root(self, "icall_sortedlist_create");
    Il2CppArray* arr = AllocItems(p);
    self = root.Get();
    sl = AsSortedList(self);
    sl->capacity = p;
    sl->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&sl->items),
                         reinterpret_cast<Il2CppObject*>(sl->items));
    sl->count = 0;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_add -- Add(key, value) (see header).
//------------------------------------------------------------------------------
void il2cpp_sortedlist_add(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Insert(AsSortedList(self), key, value, false);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_by_index -- GetByIndex(index): the value at the
// given sorted index (see header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_by_index(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    ThrowIfIndexOutOfRange(sl, index);
    return ValueAt(sl, index);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_by_key -- indexer GET by key (see header): the
// stored value, or null when the key is absent (a stored null value also
// yields null -- indistinguishable, exactly like .NET).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_by_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppSortedList* sl = AsSortedList(self);
    if (sl->items == nullptr)
    {
        return nullptr; // empty list (ctor skipped defensively)
    }
    bool found = false;
    const int32_t pos = FindKey(sl, key, &found);
    return found ? ValueAt(sl, pos) : nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_set_at -- SetAt(index, value) (see header): replace the
// value at the given sorted index.
//------------------------------------------------------------------------------
void il2cpp_sortedlist_set_at(Il2CppObject* self, int32_t index, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    ThrowIfIndexOutOfRange(sl, index);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
    slots[sl->capacity + index] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[sl->capacity + index]), value);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_set_by_key -- indexer SET by key (see header): replace
// the value of an existing key, or insert at the sorted position.
//------------------------------------------------------------------------------
void il2cpp_sortedlist_set_by_key(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Insert(AsSortedList(self), key, value, true);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_keys -- Keys (see header): a snapshot of every key
// in ascending (sorted) order.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(SnapshotKeysOrValues(AsSortedList(self), false));
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_values -- Values (see header): a snapshot of every
// value (stored nulls included), index-aligned with the sorted Keys.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_values(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(SnapshotKeysOrValues(AsSortedList(self), true));
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_remove_at -- RemoveAt(index) (see header): drop the pair
// at the given sorted index; the array stays sorted.
//------------------------------------------------------------------------------
void il2cpp_sortedlist_remove_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    ThrowIfIndexOutOfRange(sl, index);
    ShiftLeft(sl, index);
    --sl->count;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_count -- Count (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_sortedlist_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsSortedList(self)->count;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_key_at -- GetKey(index) (see header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_key_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    ThrowIfIndexOutOfRange(sl, index);
    return KeyAt(sl, index);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_value_at -- GetValueAt(index) (see header): the
// value at the given sorted index (GetByIndex semantics).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_sortedlist_get_value_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    ThrowIfIndexOutOfRange(sl, index);
    return ValueAt(sl, index);
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_index_of_key -- IndexOfKey(key) (see header): the sorted
// index of the key, or -1 when absent.
//------------------------------------------------------------------------------
int32_t il2cpp_sortedlist_index_of_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppSortedList* sl = AsSortedList(self);
    if (sl->items == nullptr)
    {
        return -1; // empty list (ctor skipped defensively)
    }
    bool found = false;
    const int32_t pos = FindKey(sl, key, &found);
    return found ? pos : -1;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_contains_key -- ContainsKey(key) (see header): 1 when the
// key is present (a stored null value still counts as present), 0 when absent.
//------------------------------------------------------------------------------
int32_t il2cpp_sortedlist_contains_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppSortedList* sl = AsSortedList(self);
    if (sl->items == nullptr)
    {
        return 0; // empty list (ctor skipped defensively)
    }
    bool found = false;
    FindKey(sl, key, &found);
    return found ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_remove_by_key -- Remove(key) (see header): drop the pair
// with the given key; a missing key is a no-op (unlike RemoveAt).
//------------------------------------------------------------------------------
void il2cpp_sortedlist_remove_by_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppSortedList* sl = AsSortedList(self);
    if (sl->items == nullptr)
    {
        return; // empty list (ctor skipped defensively)
    }
    bool found = false;
    const int32_t pos = FindKey(sl, key, &found);
    if (!found)
    {
        return; // absent key: no-op
    }
    ShiftLeft(sl, pos);
    --sl->count;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_get_capacity -- Capacity (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_sortedlist_get_capacity(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsSortedList(self)->capacity;
}

//------------------------------------------------------------------------------
// il2cpp_sortedlist_clear -- Clear() (see header): drop every pair.
//------------------------------------------------------------------------------
void il2cpp_sortedlist_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppSortedList* sl = AsSortedList(self);
    if (sl->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(sl->items->data);
        for (int32_t i = 0; i < 2 * sl->capacity; ++i)
        {
            if (slots[i] != nullptr)
            {
                slots[i] = nullptr;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
            }
        }
    }
    sl->count = 0;
}

//------------------------------------------------------------------------------
// D-SL-3 (sldeep) -- snapshot enumerator icalls. The Keys / Values snapshots
// are reference arrays (Il2CppArray*); the ICollection/IEnumerable/IEnumerator
// interface methods route here (see sortedlist.h). The enumerator wraps the
// snapshot array + a next-index cursor; foreach semantics:
//   create  -> index = -1 (before the first element)
//   MoveNext-> ++index; returns 1 while index < array->length, else 0
//   get_Current -> array[index] (caller only reads after a successful MoveNext)
//   Reset   -> index = -1
//   Dispose -> no-op (the enumerator is not IDisposable; the foreach finally's
//              isinst guard never reaches it, but the symbol must link).
//
// VERSION-CHECK DIVERGENCE (recorded, D-STK-1 family; see sortedlist.h): the
// enumerator wraps a FROZEN snapshot and holds NO list reference (GetEnumerator
// is routed on the snapshot array itself), so list mutations after
// GetEnumerator cannot be detected -- the walk continues over the frozen copy
// instead of net48's InvalidOperationException. The snapshot semantics are
// documented and pinned by test_sortedlist 16d.
//------------------------------------------------------------------------------

// ICollection.get_Count on a Keys / Values snapshot: the snapshot array length
// equals the list count at snapshot time (net48's live view agrees while the
// list is not mutated -- the sldeep probes snapshot once).
int32_t il2cpp_sortedlist_snapshot_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppArray*>(self)->length;
}

// IEnumerable.GetEnumerator on a Keys / Values snapshot: wrap the array in a
// fresh enumerator object (index -1 = before the first element). The enumerator
// is a managed object (registered klass, array field scanned), so the snapshot
// stays reachable while the foreach runs.
Il2CppObject* il2cpp_sortedlist_snapshot_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureSortedListRegistered();
    // il2cpp_object_new below may compact and MOVE 'self' (the snapshot
    // array): root it and re-read the (possibly moved) address before
    // storing it into the enumerator's GC-scanned array field.
    GcRoot<Il2CppObject*> root(self, "icall_sortedlist_snapshot_enumerator");
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
        il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
    self = root.Get();
    en->array = reinterpret_cast<Il2CppArray*>(self);
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->array),
                         reinterpret_cast<Il2CppObject*>(en->array));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

// IEnumerator.MoveNext: advance the cursor; 1 while in bounds, 0 when exhausted.
int32_t il2cpp_sortedlist_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(self);
    ++en->index;
    return en->index < en->array->length ? 1 : 0;
}

// IEnumerator.get_Current: the element at the cursor (valid after MoveNext).
Il2CppObject* il2cpp_sortedlist_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(self);
    return reinterpret_cast<Il2CppObject* const*>(en->array->data)[en->index];
}

// IEnumerator.Reset: back to before the first element.
void il2cpp_sortedlist_enumerator_reset(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    reinterpret_cast<Il2CppArrayEnumerator*>(self)->index = -1;
}

// IDisposable.Dispose on the enumerator: no-op (see the descriptor comment).
void il2cpp_sortedlist_enumerator_dispose(Il2CppObject* self)
{
    (void)self;
}

} // namespace runtime
} // namespace il2cpp
