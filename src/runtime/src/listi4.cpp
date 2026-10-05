//------------------------------------------------------------------------------
// listi4.cpp -- System.Collections.Generic.List<int32> runtime (see listi4.h
// for the layout, GC bitmap and growth contracts).
//
// Data structure:
//   Il2CppListI4 { items: Il2CppArray* (INT32 value array), count }
//     Plain dynamic array: slot i holds element i. The array is a VALUE array
//     (elemSize 4, isRefElement false), so the collector never scans the
//     elements -- only the array object itself is a GC reference.
//
// GC-safety notes for the code below (same conventions as stackqueue.cpp):
//   * The items POINTER is a GC reference: every publication of a new array
//     goes through il2cpp_write_barrier (old -> young tracking).
//   * Element slots hold int32 VALUES (not references), so element writes
//     need no write barrier and no vacate-null contract -- the collector can
//     never rescan a stale reference past the logical end.
//   * Growth allocates ONE new array, then performs a pure value copy (no
//     further allocation), so no collection can run between the array's
//     creation and its publication into items.
//   * The Gen0 collection COMPACTS the young generation (M3): a surviving
//     list object is copied to a fresh Gen1 segment and its old address is
//     reclaimed. Phase C rewrites ONLY registered roots -- the icall's 'self'
//     is a by-value C++ local, so an allocating call (create/EnsureItems/
//     GrowList/to_array) can leave 'self' stale. Every entry icall therefore
//     roots 'self' through a RootScope and RE-DERIVES the list pointer from
//     the rooted slot after any allocation (stringbuilder.cpp / linq_map.cpp
//     precedent). The old items array is still reachable through the rooted
//     list while the pure-value copy runs.
//   * Out-of-range get/set/insert/remove_at raise ArgumentOutOfRangeException
//     through the il2cpp_exception_ext.h factory (chains carry type + base
//     classes, so generated catch clauses follow .NET inheritance semantics).
//------------------------------------------------------------------------------

#include "listi4.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h"
#include "arraysort.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Layout verification: the class descriptor's refFieldBitmap bits below are
// literal constants; these compile-time checks pin them to the struct
// layout, so a layout change with a stale bitmap cannot link silently.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppListI4, items) == 1 * sizeof(void*), "Il2CppListI4: items must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppListI4, count) == 2 * sizeof(void*), "Il2CppListI4: count sits at slot 2 (NOT in the bitmap)");

// Minimum items-array length: avoids resizing on the very first add (and
// matches the .NET Framework's first grow target of 4).
constexpr int32_t kMinItems = 4;

// Doubling cap: keeps the doubling loop inside int32 (no overflow).
constexpr int32_t kMaxItems = 1 << 30;

// The .NET Framework 4.8 List<T> out-of-range message for get/set/remove_at
// (ArgumentOutOfRange_Index). Insert uses a SEPARATE resource message (see
// kInsertOutOfRangeMessage below) -- net48 has different text for each.
constexpr const char* kIndexOutOfRangeMessage =
    "Index was out of range. Must be non-negative and less than the size of the collection.";

// The .NET Framework 4.8 capacity-ctor message (ArgumentOutOfRange_
// MustBeNonNegNum), thrown by new List<int>(negativeCapacity).
constexpr const char* kNonNegativeMessage = "Non-negative number required.";

// The .NET Framework 4.8 List<T>.Insert out-of-range message
// (ArgumentOutOfRange_ListInsert): insertion at index == count is legal, so
// the text differs from the element-range message above.
constexpr const char* kInsertOutOfRangeMessage =
    "Index must be within the bounds of the List.";

// The System.Collections.Generic.List`1 (int32 instantiation) runtime
// descriptor. Registered lazily (constraint #12: no global-constructor
// allocation), exactly like the stack/queue/hashtable classes.
//   instanceSize 24 (3 slots), refFieldBitmap bit 1: 'items' is a GC ref.
Il2CppClass g_listI4Class = {
    "System.Collections.Generic.List`1", // name
    static_cast<int32_t>(sizeof(Il2CppListI4)), // instanceSize
    0b00010u,                   // refFieldBitmap: bit 1 = items
    1,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

bool g_listI4Registered = false;

void EnsureListI4Registered()
{
    if (g_listI4Registered) return;
    il2cpp_class_register(&g_listI4Class);
    g_listI4Registered = true;
}

//------------------------------------------------------------------------------
// Small helpers.
//------------------------------------------------------------------------------

Il2CppListI4* AsList(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppListI4*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors stringext.cpp / hashtable.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// Allocate an int32 VALUE items array (elemSize 4, isRefElement false ->
// the collector never scans the elements).
Il2CppArray* AllocItems(int32_t itemCount)
{
    return il2cpp_array_new(nullptr, sizeof(int32_t), false, itemCount);
}

// Defensive: a container whose create icall was skipped has a null items
// array; lazily allocate the minimum array so no icall dereferences null
// (generated code always calls create -- this is misuse hardening only).
// 'selfSlot' is the rooted list-pointer address (see the RootScope contract):
// the allocation may compact the list, so the pointer is re-derived after.
void EnsureItems(Il2CppObject** selfSlot)
{
    Il2CppListI4* list = AsList(*selfSlot);
    if (list->items == nullptr)
    {
        Il2CppArray* items = AllocItems(kMinItems);
        list = AsList(*selfSlot); // re-derive after the (possibly collecting) allocation
        list->items = items;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(items));
    }
}

int32_t* Slots(Il2CppListI4* list)
{
    return reinterpret_cast<int32_t*>(list->items->data);
}

//------------------------------------------------------------------------------
// RootScope -- GC rooting guard (stringbuilder.cpp:267 / linq_map.cpp:151 /
// ipaddr.cpp:80 precedent). The runtime stack is NOT a GC root set: a managed
// pointer held only in a C++ local is invisible to the collector. Registering
// the local's ADDRESS as a static root keeps the object alive AND lets the
// collector's Phase C rewrite the slot after a Gen0 compaction (which copies
// the list object to a fresh Gen1 segment). Callers MUST re-derive the list
// pointer from the rooted slot after any allocation. RAII removal makes the
// ArgumentOutOfRangeException throwing paths leak-free.
//------------------------------------------------------------------------------
class RootScope
{
public:
    RootScope() : m_count(0) {}
    ~RootScope()
    {
        while (m_count > 0)
        {
            il2cpp_gc_remove_static_root(m_slots[m_count - 1]);
            --m_count;
        }
    }
    void Add(void** slot)
    {
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[4]; // max 1 used today (self); headroom for nested helpers
    int32_t m_count;
};

// Double the items-array length, capped at kMaxItems. At the cap the array
// is rebuilt at the same length (an insert at count == 2^30 is the
// documented physical limit -- a 4 GiB int32 array -- and cannot occur in
// AOT output).
int32_t NextLength(int32_t oldLength)
{
    int32_t newLength = oldLength * 2;
    if (newLength < oldLength || newLength > kMaxItems)
    {
        newLength = oldLength;
    }
    return newLength;
}

//------------------------------------------------------------------------------
// Growth.
//
// GC safety: the new array is allocated first (any collection triggered by
// that allocation runs before the array exists); the copy loop below is pure
// int32 value reads/writes with no allocation, so no collection can run
// between the array's birth and its publication into 'items'. The Gen0
// compaction can COPY the list object itself during the allocation, so the
// pointer is re-derived from the rooted slot ('selfSlot') after the
// allocation; the old array is still reachable through the rooted list while
// the loop reads it. Value elements need no per-slot write barrier.
//------------------------------------------------------------------------------

void GrowList(Il2CppObject** selfSlot)
{
    Il2CppListI4* list = AsList(*selfSlot);
    const int32_t oldLength = list->items->length;
    const int32_t newLength = NextLength(oldLength);
    if (newLength != oldLength)
    {
        Il2CppArray* newItems = AllocItems(newLength);
        list = AsList(*selfSlot); // re-derive after the (possibly collecting) allocation
        int32_t* newSlots = reinterpret_cast<int32_t*>(newItems->data);
        const int32_t* oldSlots = Slots(list);
        for (int32_t i = 0; i < oldLength; ++i)
        {
            newSlots[i] = oldSlots[i];
        }
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(newItems));
        list->items = newItems;
    }
}

//------------------------------------------------------------------------------
// Shared bounds checks: get/set/remove_at use the element range [0, count-1];
// insert uses the insertion range [0, count]. Both raise
// ArgumentOutOfRangeException with the exact .NET Framework 4.8 message.
//------------------------------------------------------------------------------

void ThrowIfElementIndexOutOfRange(int32_t index, int32_t count)
{
    if (index < 0 || index >= count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
    }
}

void ThrowIfInsertIndexOutOfRange(int32_t index, int32_t count)
{
    if (index < 0 || index > count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kInsertOutOfRangeMessage);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_list_i4_class -- the managed descriptor (lazily registered on
// first use).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_list_i4_class()
{
    EnsureListI4Registered();
    return &g_listI4Class;
}

//------------------------------------------------------------------------------
// List<int> icalls.
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_list_i4_create()
{
    EnsureListI4Registered();
    Il2CppObject* self = il2cpp_object_new(&g_listI4Class);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    // The items allocation may trigger a Gen0 compaction that COPIES the
    // freshly created list to a Gen1 segment; 'self' is rooted and the list
    // pointer is re-derived after the allocation (see the RootScope contract).
    Il2CppArray* items = AllocItems(kMinItems);
    Il2CppListI4* list = AsList(self);
    list->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(items));
    list->count = 0;
    return self;
}

Il2CppObject* il2cpp_list_i4_create_with_capacity(int32_t capacity)
{
    // net48 List<T>(int capacity): a negative capacity raises
    // ArgumentOutOfRangeException (ArgumentOutOfRange_MustBeNonNegNum).
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", kNonNegativeMessage);
    }
    EnsureListI4Registered();
    Il2CppObject* self = il2cpp_object_new(&g_listI4Class);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    // A length-0 items array would never grow (NextLength(0) == 0), so a
    // zero capacity falls back to the default minimum array -- observably
    // identical to net48's empty-array state on every wired member (the
    // internal length is visible only through the unmapped Capacity property).
    const int32_t itemCount = capacity > 0 ? capacity : kMinItems;
    Il2CppArray* items = AllocItems(itemCount);
    Il2CppListI4* list = AsList(self);
    list->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(items));
    list->count = 0;
    return self;
}

void il2cpp_list_i4_add(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    EnsureItems(&self);
    Il2CppListI4* list = AsList(self);
    if (list->count == list->items->length)
    {
        GrowList(&self);
    }
    // Re-derive after the growth: the allocation may have compacted and moved
    // the list (only the rooted slot was rewritten).
    list = AsList(self);
    Slots(list)[list->count] = value;
    ++list->count;
}

void il2cpp_list_i4_insert(Il2CppObject* self, int32_t index, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppListI4* list = AsList(self);
    ThrowIfInsertIndexOutOfRange(index, list->count);
    EnsureItems(&self);
    list = AsList(self); // re-derive after EnsureItems (may have collected)
    if (list->count == list->items->length)
    {
        GrowList(&self);
        list = AsList(self); // re-derive after the growth (may have collected)
    }
    // Shift [index, count) right by one; the range is valid (index <= count),
    // so the write stays inside the grown array.
    int32_t* slots = Slots(list);
    for (int32_t i = list->count; i > index; --i)
    {
        slots[i] = slots[i - 1];
    }
    slots[index] = value;
    ++list->count;
}

void il2cpp_list_i4_remove_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    int32_t* slots = Slots(list);
    // Shift [index+1, count) left by one, then zero the vacated tail slot
    // (value hygiene; mirrors the stack/queue vacate contract).
    for (int32_t i = index; i < list->count - 1; ++i)
    {
        slots[i] = slots[i + 1];
    }
    slots[list->count - 1] = 0;
    --list->count;
}

bool il2cpp_list_i4_remove(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    const int32_t index = il2cpp_list_i4_index_of(self, value);
    if (index < 0)
    {
        return false;
    }
    il2cpp_list_i4_remove_at(self, index);
    return true;
}

int32_t il2cpp_list_i4_get_item(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    return Slots(list)[index];
}

void il2cpp_list_i4_set_item(Il2CppObject* self, int32_t index, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    Slots(list)[index] = value;
}

int32_t il2cpp_list_i4_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsList(self)->count;
}

void il2cpp_list_i4_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    if (list->items != nullptr)
    {
        int32_t* slots = Slots(list);
        for (int32_t i = 0; i < list->count; ++i)
        {
            slots[i] = 0;
        }
    }
    list->count = 0;
}

int32_t il2cpp_list_i4_index_of(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    if (list->items != nullptr)
    {
        const int32_t* slots = Slots(list);
        for (int32_t i = 0; i < list->count; ++i)
        {
            if (slots[i] == value)
            {
                return i;
            }
        }
    }
    return -1;
}

bool il2cpp_list_i4_contains(Il2CppObject* self, int32_t value)
{
    return il2cpp_list_i4_index_of(self, value) >= 0;
}

Il2CppObject* il2cpp_list_i4_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppListI4* list = AsList(self);
    Il2CppArray* result = AllocItems(list->count);
    // Re-derive after the allocation: the copy may have compacted and moved
    // the list (only the rooted slot was rewritten); 'result' is freshly
    // allocated post-collection, so it cannot be moved by the value copy.
    list = AsList(self);
    // Pure int32 value copy (no allocation inside); 'list' stays reachable
    // through the rooted slot while the copy runs.
    int32_t* dst = reinterpret_cast<int32_t*>(result->data);
    const int32_t* src = list->items != nullptr ? Slots(list) : nullptr;
    for (int32_t i = 0; i < list->count; ++i)
    {
        dst[i] = src != nullptr ? src[i] : 0;
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// GetEnumerator -- return a snapshot enumerator over the list's items
// array [0, count). The enumerator is a managed Il2CppArrayEnumerator (re-used
// from sortedlist.h) holding a FRESH exact-length copy of the items array and
// a cursor starting at -1. A fresh copy (not the live items array) is used
// because the list may grow (and replace its items array) during enumeration;
// the snapshot keeps the foreach stable (same semantics as SortedList's
// snapshot enumerator).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_list_i4_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureListI4Registered();
    // Ensure the enumerator class is registered (it is defined in
    // sortedlist.cpp; register here too -- il2cpp_class_register dedupes by
    // pointer).
    il2cpp_class_register(&System_Collections_SortedList_Enumerator_class);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppListI4* list = AsList(self);
    // Allocate a snapshot array of exactly 'count' elements.
    Il2CppArray* snap = AllocItems(list->count);
    // Re-derive after the allocation (may have compacted/moved the list).
    list = AsList(self);
    // Copy [0, count) into the snapshot.
    if (list->count > 0 && list->items != nullptr)
    {
        const int32_t* src = Slots(list);
        int32_t* dst = reinterpret_cast<int32_t*>(snap->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            dst[i] = src[i];
        }
    }
    // Allocate the enumerator object (re-uses Il2CppArrayEnumerator from
    // sortedlist.h -- the struct is { obj, array, index }).
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
        il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
    en->array = snap;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->array),
                         reinterpret_cast<Il2CppObject*>(snap));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

//------------------------------------------------------------------------------
// Sort -- ascending in-place sort of the elements [0, count).
// The items array may be longer than count (capacity > count), so a temporary
// exact-length copy is sorted and copied back.
//------------------------------------------------------------------------------
void il2cpp_list_i4_sort(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppListI4* list = AsList(self);
    if (list->count <= 1)
    {
        return; // 0 or 1 elements: already sorted
    }
    // Allocate a temporary array of exactly 'count' elements.
    Il2CppArray* temp = AllocItems(list->count);
    // Re-derive after the allocation.
    list = AsList(self);
    // Copy [0, count) into temp.
    {
        const int32_t* src = Slots(list);
        int32_t* dst = reinterpret_cast<int32_t*>(temp->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            dst[i] = src[i];
        }
    }
    // Sort the temp array in-place.
    il2cpp_array_sort_int32(temp);
    // Copy back into the list's items array (no allocation, no collection).
    list = AsList(self); // defensive re-derive (temp alloc may have moved list)
    {
        int32_t* dst = Slots(list);
        const int32_t* src = reinterpret_cast<int32_t*>(temp->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            dst[i] = src[i];
        }
    }
}

//------------------------------------------------------------------------------
// CopyTo(T[] array, int32_t arrayIndex) -- copy elements [0, count) into
// 'array' starting at 'arrayIndex'. Validates: null array
// (ArgumentNullException), negative index (ArgumentOutOfRangeException),
// insufficient space (ArgumentException).
//------------------------------------------------------------------------------
void il2cpp_list_i4_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (arrayIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("arrayIndex", kNonNegativeMessage);
    }
    Il2CppListI4* list = AsList(self);
    // Bounds: arrayIndex + count must not exceed dst->length.
    if (static_cast<int64_t>(arrayIndex) + list->count > dst->length)
    {
        il2cpp_throw_argument_exception("Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    }
    if (list->count > 0 && list->items != nullptr)
    {
        const int32_t* src = Slots(list);
        int32_t* dstSlots = reinterpret_cast<int32_t*>(dst->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            dstSlots[arrayIndex + i] = src[i];
        }
    }
}

//------------------------------------------------------------------------------
// Enumerator members -- the generated code stores the enumerator object in a
// reference slot (Il2CppObject*) and passes (&slot) with a "deref:r" cast,
// so the icall receives the Il2CppObject* (the enumerator). The Il2CppArrayEnumerator
// struct is { Il2CppObject obj; Il2CppArray* array; int32_t index; }.
//------------------------------------------------------------------------------

int32_t il2cpp_list_i4_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(self);
    ++en->index;
    return en->index < en->array->length ? 1 : 0;
}

int32_t il2cpp_list_i4_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(self);
    return reinterpret_cast<int32_t*>(en->array->data)[en->index];
}

void il2cpp_list_i4_enumerator_dispose(Il2CppObject* self)
{
    (void)self;
}

} // namespace il2cpp
