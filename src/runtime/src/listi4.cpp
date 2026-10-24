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

#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "arraysort.h"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>

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
        // nb9 H-B1: guard the fixed slot table (an overflow corrupts m_count).
        if (m_count >= static_cast<int32_t>(sizeof(m_slots) / sizeof(m_slots[0])))
        {
            std::abort();
        }
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
    // An empty list (the net48 default Capacity 0) grows straight to the
    // minimum 4-slot array on the first add; doubling from 0 would stay 0.
    if (oldLength == 0)
    {
        return kMinItems;
    }
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

//--------------------------------------------------------------------------
// listb1: the predicate / comparison callback ABIs and validation for the
// delegate surface. The ABI is the single-cast static methodPtr (an
// instance-bound leaf receives its target in the `this` slot); the delegate and
// the list are GC-rooted across a callback loop.
//--------------------------------------------------------------------------
typedef bool (*PredI4Fn)(int32_t);
typedef bool (*PredI4ThisFn)(Il2CppObject*, int32_t);
typedef int32_t (*CmpI4Fn)(int32_t, int32_t);
typedef int32_t (*CmpI4ThisFn)(Il2CppObject*, int32_t, int32_t);

bool InvokePredI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredI4Fn>(d->methodPtr)(v);
}

int32_t InvokeCmpI4(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<CmpI4ThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<CmpI4Fn>(d->methodPtr)(a, b);
}

// RequireListCallback -- the single-cast delegate validation (the arrayext2.cpp
// RequireArrayCallback contract).
Il2CppDelegate* RequireListCallback(Il2CppObject* cb, const char* paramName)
{
    if (cb == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
        return nullptr;
    }
    const char* name = cb->klass != nullptr ? cb->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, "System.MulticastDelegate") == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a List callback.");
        return nullptr;
    }
    if (std::strcmp(name, "System.Delegate") != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
        return nullptr;
    }
    return reinterpret_cast<Il2CppDelegate*>(cb);
}

// RequireInt32ArrayArg -- validate an IEnumerable<int> argument (the wired
// surface passes a one-dimensional int32 array): null -> ArgumentNullException;
// a non-array -> ArgumentException; a non-int32 array -> NotSupportedException.
Il2CppArray* RequireInt32ArrayArg(Il2CppObject* obj, const char* paramName)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
    if (arr->obj.klass == nullptr || arr->obj.klass->name == nullptr ||
        arr->obj.klass->name[0] != '[')
    {
        il2cpp_throw_argument_exception("Object is not an array.");
    }
    if (arr->isRefElem || arr->elemSize != sizeof(int32_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int32 arrays are supported.");
    }
    return arr;
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
    // net48: the default Capacity is 0 -- the first Add grows to the 4-slot
    // minimum (NextLength(0) == kMinItems). The array length IS the Capacity.
    Il2CppArray* items = AllocItems(0);
    Il2CppListI4* list = AsList(self);
    list->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(items));
    list->count = 0;
    return self;
}

void il2cpp_list_i4_create_with_capacity(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    // net48 List<T>(int capacity): a negative capacity raises
    // ArgumentOutOfRangeException (ArgumentOutOfRange_MustBeNonNegNum).
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", kNonNegativeMessage);
    }
    // The items allocation may compact and MOVE 'self' (only reachable through
    // the caller's frame slot, which is rewritten): root it and re-read the
    // (possibly moved) container before writing its fields.
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    // The items-array length IS the Capacity (net48 observable): a zero
    // capacity keeps a zero-length array (the first Add grows to the 4-slot
    // minimum through NextLength(0)).
    Il2CppArray* items = AllocItems(capacity);
    Il2CppListI4* list = AsList(self);
    list->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(items));
    list->count = 0;
}

void il2cpp_list_i4_create_with_collection(Il2CppObject* self, Il2CppObject* source)
{
    ThrowIfNullReceiver(self);
    Il2CppArray* src = RequireInt32ArrayArg(source, "collection");
    // Root both the shell and the source across the items allocation (either
    // may be compacted/moved).
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&source));
    // The initial Capacity equals the source count (net48); the items allocation
    // may compact the freshly created list, so the pointer is re-derived after.
    Il2CppArray* items = AllocItems(src->length);
    Il2CppListI4* list = AsList(self);
    list->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(items));
    list->count = src->length;
    if (src->length > 0)
    {
        std::memcpy(items->data, src->data,
                    static_cast<size_t>(src->length) * sizeof(int32_t));
    }
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

int32_t il2cpp_list_i4_get_capacity(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    return list->items != nullptr ? list->items->length : 0;
}

void il2cpp_list_i4_set_capacity(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    if (value < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("value", kNonNegativeMessage);
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppListI4* list = AsList(self);
    if (value < list->count)
    {
        // net48: a capacity below Count is rejected (ArgumentOutOfRangeException).
        il2cpp_throw_argument_out_of_range_exception("value",
            "value was less than the current Count.");
    }
    const int32_t oldLength = list->items != nullptr ? list->items->length : 0;
    if (value == oldLength)
    {
        return; // net48: an equal capacity is a no-op
    }
    Il2CppArray* fresh = AllocItems(value);
    // Re-derive after the allocation (a collection may have compacted/moved the
    // list; only the rooted slot was rewritten).
    list = AsList(self);
    const int32_t copyLen = list->count < value ? list->count : value;
    if (copyLen > 0 && list->items != nullptr)
    {
        std::memcpy(fresh->data, Slots(list),
                    static_cast<size_t>(copyLen) * sizeof(int32_t));
    }
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(fresh));
    list->items = fresh;
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
// listb1: range operations.
//------------------------------------------------------------------------------

void il2cpp_list_i4_add_range(Il2CppObject* self, Il2CppObject* collection)
{
    ThrowIfNullReceiver(self);
    Il2CppArray* src = RequireInt32ArrayArg(collection, "collection");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&collection));
    EnsureItems(&self);
    const int32_t srcLen = reinterpret_cast<Il2CppArray*>(collection)->length;
    const int32_t need = AsList(self)->count + srcLen;
    while (AsList(self)->items->length < need)
    {
        GrowList(&self);
    }
    Il2CppListI4* list = AsList(self);
    const int32_t* srcSlots = reinterpret_cast<const int32_t*>(src->data);
    std::memcpy(Slots(list) + list->count, srcSlots,
                static_cast<size_t>(srcLen) * sizeof(int32_t));
    list->count += srcLen;
}

void il2cpp_list_i4_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection)
{
    ThrowIfNullReceiver(self);
    Il2CppArray* src = RequireInt32ArrayArg(collection, "collection");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&collection));
    Il2CppListI4* list = AsList(self);
    ThrowIfInsertIndexOutOfRange(index, list->count);
    const int32_t srcLen = reinterpret_cast<Il2CppArray*>(collection)->length;
    const int32_t need = list->count + srcLen;
    while (AsList(self)->items->length < need)
    {
        GrowList(&self);
    }
    list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = list->count; i > index; --i)
    {
        slots[i + srcLen - 1] = slots[i - 1];
    }
    const int32_t* srcSlots = reinterpret_cast<const int32_t*>(src->data);
    std::memcpy(slots + index, srcSlots,
                static_cast<size_t>(srcLen) * sizeof(int32_t));
    list->count += srcLen;
}

void il2cpp_list_i4_remove_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    }
    Il2CppListI4* list = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > list->count)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to end.");
    }
    int32_t* slots = Slots(list);
    for (int32_t i = index; i < list->count - count; ++i)
    {
        slots[i] = slots[i + count];
    }
    for (int32_t i = list->count - count; i < list->count; ++i)
    {
        slots[i] = 0;
    }
    list->count -= count;
}

Il2CppObject* il2cpp_list_i4_get_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    }
    Il2CppListI4* list = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > list->count)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to end.");
    }
    Il2CppObject* result = il2cpp_object_new(&g_listI4Class);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&result));
    il2cpp_list_i4_create_with_capacity(result, count);
    Il2CppListI4* dst = AsList(result);
    list = AsList(self);
    const int32_t* srcSlots = Slots(list);
    std::memcpy(Slots(dst), srcSlots + index,
                static_cast<size_t>(count) * sizeof(int32_t));
    dst->count = count;
    return result;
}

int32_t il2cpp_list_i4_remove_all(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    int32_t wi = 0;
    for (int32_t i = 0; i < list->count; ++i)
    {
        const int32_t v = slots[i];
        if (!InvokePredI4(d, v))
        {
            slots[wi] = v;
            ++wi;
        }
    }
    for (int32_t i = wi; i < list->count; ++i)
    {
        slots[i] = 0;
    }
    const int32_t removed = list->count - wi;
    list->count = wi;
    return removed;
}

int32_t il2cpp_list_i4_last_index_of(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = list->count - 1; i >= 0; --i)
    {
        if (slots[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// listb1: predicate family.
//------------------------------------------------------------------------------

int32_t il2cpp_list_i4_find(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0; i < list->count; ++i)
    {
        const int32_t v = slots[i];
        if (InvokePredI4(d, v))
        {
            return v;
        }
    }
    return 0; // default(int)
}

int32_t il2cpp_list_i4_find_last(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = list->count - 1; i >= 0; --i)
    {
        const int32_t v = slots[i];
        if (InvokePredI4(d, v))
        {
            return v;
        }
    }
    return 0; // default(int)
}

int32_t il2cpp_list_i4_find_index(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0; i < list->count; ++i)
    {
        if (InvokePredI4(d, slots[i]))
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_list_i4_find_last_index(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = list->count - 1; i >= 0; --i)
    {
        if (InvokePredI4(d, slots[i]))
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_list_i4_exists(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0; i < list->count; ++i)
    {
        if (InvokePredI4(d, slots[i]))
        {
            return 1;
        }
    }
    return 0;
}

int32_t il2cpp_list_i4_true_for_all(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0; i < list->count; ++i)
    {
        if (!InvokePredI4(d, slots[i]))
        {
            return 0;
        }
    }
    return 1; // an empty list is vacuously true
}

void il2cpp_list_i4_for_each(Il2CppObject* self, Il2CppObject* action)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(action, "action");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0; i < list->count; ++i)
    {
        InvokePredI4(d, slots[i]);
    }
}

Il2CppObject* il2cpp_list_i4_find_all(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* d = RequireListCallback(match, "match");
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&d));
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    Il2CppObject* result = il2cpp_object_new(&g_listI4Class);
    il2cpp_list_i4_create_with_capacity(result, list->count);
    scope.Add(reinterpret_cast<void**>(&result));
    Il2CppListI4* dst = AsList(result);
    int32_t wi = 0;
    for (int32_t i = 0; i < list->count; ++i)
    {
        const int32_t v = slots[i];
        if (InvokePredI4(d, v))
        {
            Slots(dst)[wi] = v;
            ++wi;
        }
    }
    dst->count = wi;
    return result;
}

//------------------------------------------------------------------------------
// listb1: reverse / binary search / sort overloads.
//------------------------------------------------------------------------------

void il2cpp_list_i4_reverse(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    int32_t* slots = Slots(list);
    for (int32_t i = 0, j = list->count - 1; i < j; ++i, --j)
    {
        const int32_t t = slots[i];
        slots[i] = slots[j];
        slots[j] = t;
    }
}

void il2cpp_list_i4_reverse_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    }
    Il2CppListI4* list = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > list->count)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to end.");
    }
    int32_t* slots = Slots(list);
    for (int32_t i = index, j = index + count - 1; i < j; ++i, --j)
    {
        const int32_t t = slots[i];
        slots[i] = slots[j];
        slots[j] = t;
    }
}

int32_t il2cpp_list_i4_binary_search(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppListI4* list = AsList(self);
    const int32_t* slots = Slots(list);
    int32_t lo = 0;
    int32_t hi = list->count - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + ((hi - lo) >> 1);
        if (slots[mid] == value)
        {
            return mid;
        }
        if (slots[mid] < value)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid - 1;
        }
    }
    return ~lo; // net48: the complement of the insertion point
}

void il2cpp_list_i4_sort_comparison(Il2CppObject* self, Il2CppObject* comparison)
{
    ThrowIfNullReceiver(self);
    if (comparison == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    Il2CppListI4* list = AsList(self);
    if (list->count <= 1)
    {
        return;
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppArray* temp = AllocItems(list->count);
    scope.Add(reinterpret_cast<void**>(&temp));
    list = AsList(self);
    std::memcpy(temp->data, Slots(list),
                static_cast<size_t>(list->count) * sizeof(int32_t));
    // The int32 array-sort domain invokes the managed Comparison<int> delegate
    // (a pure CPU sort -- no allocation, so no collection can interleave).
    il2cpp_array_sort_int32_comparison(temp, comparison);
    list = AsList(self);
    std::memcpy(Slots(list), temp->data,
                static_cast<size_t>(list->count) * sizeof(int32_t));
}

void il2cpp_list_i4_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count, Il2CppObject* comparer)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    }
    Il2CppListI4* list = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > list->count)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to end.");
    }
    if (count <= 1)
    {
        return;
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppArray* temp = AllocItems(count);
    scope.Add(reinterpret_cast<void**>(&temp));
    list = AsList(self);
    std::memcpy(temp->data, Slots(list) + index,
                static_cast<size_t>(count) * sizeof(int32_t));
    il2cpp_array_sort_icomparer_range(temp, 0, count, comparer);
    list = AsList(self);
    std::memcpy(Slots(list) + index, temp->data,
                static_cast<size_t>(count) * sizeof(int32_t));
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
