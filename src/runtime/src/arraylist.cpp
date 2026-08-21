//------------------------------------------------------------------------------
// arraylist.cpp -- non-generic System.Collections.ArrayList runtime (see
// arraylist.h for the layout, GC bitmap and growth contracts).
//
// Data structure:
//   Il2CppArrayList { items: Il2CppArray* (REFERENCE array), count }
//     Plain dynamic array: slot i holds element i. The array is a REFERENCE
//     array (isRefElement true), so the collector scans every element as an
//     Il2CppObject*; a rooted list therefore keeps its whole contents alive.
//
// GC-safety notes for the code below (same conventions as stackqueue.cpp):
//   * All reference stores (element writes, the items pointer itself) go
//     through il2cpp_write_barrier first (old -> young tracking).
//   * Growth allocates ONE new array, then performs pure pointer writes (no
//     further allocation), so no collection can run between the array's
//     creation and its publication into items.
//   * remove_at / remove / clear null the vacated slots (with a write
//     barrier) so the collector never rescans a stale reference past the
//     logical end.
//   * Out-of-range get/set/remove_at raise ArgumentOutOfRangeException
//     through the il2cpp_exception_ext.h factory (chains carry type + base
//     classes, so generated catch clauses follow .NET inheritance semantics).
//
// Equality authority for index_of / contains / remove:
//   * runtime::icall_object_equals -- static Object.Equals (valuesemantics.cpp):
//     reference identity first (null == null); two strings -> ordinal content;
//     two boxes with the same klass name -> value equality (a boxed Int32 30
//     equals ANY boxed Int32 30, net48 ArrayList.IndexOf semantics); every
//     other pair -> false. This is the same contract the BCL maps IndexOf and
//     Remove to, so a value-equal-but-not-identical element is found here
//     exactly when .NET would find it.
//------------------------------------------------------------------------------

#include "arraylist.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "il2cpp_value_semantics.h" // runtime::icall_object_equals

#include <cstddef>
#include <cstdint>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Layout verification: the class descriptor's refFieldBitmap bits below are
// literal constants; these compile-time checks pin them to the struct
// layout, so a layout change with a stale bitmap cannot link silently.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppArrayList, items) == 1 * sizeof(void*), "Il2CppArrayList: items must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppArrayList, count) == 2 * sizeof(void*), "Il2CppArrayList: count sits at slot 2 (NOT in the bitmap)");

// Minimum items-array length: avoids resizing on the very first add (and
// matches the .NET Framework's first grow target of 4).
constexpr int32_t kMinItems = 4;

// Doubling cap: keeps the doubling loop inside int32 (no overflow).
constexpr int32_t kMaxItems = 1 << 30;

// The .NET Framework 4.8 ArrayList out-of-range message (used for get/set/
// remove_at alike).
constexpr const char* kIndexOutOfRangeMessage =
    "Index was out of range. Must be non-negative and less than the size of the collection.";

// The System.Collections.ArrayList runtime descriptor. Registered lazily
// (constraint #12: no global-constructor allocation), exactly like the
// stack/queue/hashtable classes.
//   instanceSize 24 (3 slots), refFieldBitmap bit 1: 'items' is a GC ref.
Il2CppClass g_arrayListClass = {
    "System.Collections.ArrayList", // name
    static_cast<int32_t>(sizeof(Il2CppArrayList)), // instanceSize
    0b00010u,                   // refFieldBitmap: bit 1 = items
    1,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

bool g_arrayListRegistered = false;

void EnsureArrayListRegistered()
{
    if (g_arrayListRegistered) return;
    il2cpp_class_register(&g_arrayListClass);
    g_arrayListRegistered = true;
}

//------------------------------------------------------------------------------
// Small helpers.
//------------------------------------------------------------------------------

Il2CppArrayList* AsList(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppArrayList*>(self);
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

// Allocate a reference items array (Il2CppObject* elements, isRefElem =
// true -> the collector scans every element as a GC reference).
Il2CppArray* AllocItems(int32_t itemCount)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, itemCount);
}

// Defensive: a container whose create icall was skipped has a null items
// array; lazily allocate the minimum array so no icall dereferences null
// (generated code always calls create -- this is misuse hardening only).
// NOTE: the allocation here can compact the container, so this MUST be
// inlined at a call site that has rooted its 'self' (GcRoot), and the
// container pointer must be re-read BEFORE the store (see GrowList). A
// by-value helper cannot do this safely -- the store would go to the
// moved-out old address and be lost (stackqueue2 e2e, 2026-08-13).

// Double the items-array length, capped at kMaxItems. At the cap the array
// is rebuilt at the same length (an add at count == 2^30 is the documented
// physical limit -- an 8 GiB pointer array -- and cannot occur in AOT
// output).
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
// that allocation runs before the array exists); the copy loop below is
// pure pointer reads/writes with no allocation, so no collection can run
// between the array's birth and its publication into 'items'. The old array
// is still reachable through the container while the loop reads it.
// 'list' is re-read through the rooted slot after EVERY allocation: a
// compacting collection moves the container, and the re-read is what keeps
// the 'items' store on the post-compaction address.
//------------------------------------------------------------------------------

void GrowList(Il2CppArrayList* list)
{
    GcRoot<Il2CppArrayList*> root(list, "icall_arraylist_grow");
    if (list->items == nullptr)
    {
        // Defensive ensure, inlined: allocate, re-read the (possibly moved)
        // container, then publish.
        Il2CppArray* arr = AllocItems(kMinItems);
        list = root.Get();
        list->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(list->items));
    }
    const int32_t oldLength = list->items->length;
    const int32_t newLength = NextLength(oldLength);
    if (newLength != oldLength)
    {
        Il2CppArray* newItems = AllocItems(newLength);
        list = root.Get();
        Il2CppObject** newSlots = reinterpret_cast<Il2CppObject**>(newItems->data);
        Il2CppObject* const* oldSlots = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < oldLength; ++i)
        {
            newSlots[i] = oldSlots[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&newSlots[i]), newSlots[i]);
        }
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(newItems));
        list->items = newItems;
    }
}

//------------------------------------------------------------------------------
// Shared bounds check: get/set/remove_at use the element range [0, count-1].
// Raises ArgumentOutOfRangeException with the exact .NET Framework 4.8
// message.
//------------------------------------------------------------------------------

void ThrowIfElementIndexOutOfRange(int32_t index, int32_t count)
{
    if (index < 0 || index >= count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_arraylist_class -- the managed descriptor (lazily registered on
// first use).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_arraylist_class()
{
    EnsureArrayListRegistered();
    return &g_arrayListClass;
}

//------------------------------------------------------------------------------
// ArrayList icalls.
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_arraylist_create()
{
    EnsureArrayListRegistered();
    Il2CppArrayList* list = reinterpret_cast<Il2CppArrayList*>(il2cpp_object_new(&g_arrayListClass));
    // AllocItems below may compact and MOVE the fresh object (only reachable
    // through this icall's local until it is returned): re-read before
    // publishing (stackqueue.cpp create precedent).
    GcRoot<Il2CppArrayList*> root(list, "icall_arraylist_create");
    Il2CppArray* arr = AllocItems(kMinItems);
    list = root.Get();
    list->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(list->items));
    list->count = 0;
    return reinterpret_cast<Il2CppObject*>(list);
}

void il2cpp_arraylist_add(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    // GrowList below can allocate (defensive ensure + growth) and compact:
    // root 'self' for the whole body and re-read it after the call.
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_add");
    Il2CppArrayList* list = AsList(self);
    if (list->items == nullptr || list->count == list->items->length)
    {
        GrowList(list);
        self = root.Get();
        list = AsList(self);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    slots[list->count] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[list->count]), value);
    ++list->count;
}

Il2CppObject* il2cpp_arraylist_get_item(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    return slots[index];
}

void il2cpp_arraylist_set_item(Il2CppObject* self, int32_t index, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    slots[index] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), value);
}

int32_t il2cpp_arraylist_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsList(self)->count;
}

void il2cpp_arraylist_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        const int32_t itemCount = list->items->length;
        for (int32_t i = 0; i < itemCount; ++i)
        {
            if (slots[i] != nullptr)
            {
                slots[i] = nullptr;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
            }
        }
    }
    list->count = 0;
}

void il2cpp_arraylist_remove_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    ThrowIfElementIndexOutOfRange(index, list->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    // Shift [index+1, count) left by one, then vacate the tail slot (null
    // with a write barrier): the collector must never rescan a stale
    // reference past the logical end (mirrors .NET's _items[_size] = null).
    for (int32_t i = index; i < list->count - 1; ++i)
    {
        slots[i] = slots[i + 1];
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
    }
    const int32_t tail = list->count - 1;
    slots[tail] = nullptr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[tail]), nullptr);
    --list->count;
}

// Insert(index, value) -- shift [index, count) RIGHT by one and write
// 'value' at index (net48 ArrayList.Insert; reverse of remove_at). An index
// outside [0, count] raises ArgumentOutOfRangeException. The array grows on
// demand (the add path's GrowList); every moved slot + the inserted element
// go through the write barrier so the collector never scans stale pointers.
void il2cpp_arraylist_insert(Il2CppObject* self, int32_t index, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    // GrowList below can allocate (defensive ensure + growth) and compact:
    // root 'self' for the whole body and re-read it after the call.
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_insert");
    Il2CppArrayList* list = AsList(self);
    if (index < 0 || index > list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    if (list->items == nullptr || list->count == list->items->length)
    {
        GrowList(list);
        self = root.Get();
        list = AsList(self);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    for (int32_t i = list->count; i > index; --i)
    {
        slots[i] = slots[i - 1];
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
    }
    slots[index] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), value);
    ++list->count;
}

void il2cpp_arraylist_remove(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items == nullptr || list->count == 0)
    {
        return; // empty list: nothing to remove (silent no-op)
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    for (int32_t i = 0; i < list->count; ++i)
    {
        // Value equality -- the same contract as index_of (static
        // Object.Equals: null matches null; boxed values by payload; strings
        // by ordinal content; reference identity for everything else).
        // Remove the FIRST match, then shift the followers left and vacate
        // the tail exactly like remove_at.
        if (runtime::icall_object_equals(slots[i], value))
        {
            for (int32_t j = i; j < list->count - 1; ++j)
            {
                slots[j] = slots[j + 1];
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[j]), slots[j]);
            }
            const int32_t tail = list->count - 1;
            slots[tail] = nullptr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[tail]), nullptr);
            --list->count;
            return;
        }
    }
    // Absent value: .NET ArrayList.Remove is a silent success -- nothing to do.
}

int32_t il2cpp_arraylist_index_of(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            // Value equality -- static Object.Equals (net48 ArrayList.IndexOf
            // semantics): reference identity first (null matches null);
            // boxed values compare by payload; strings by ordinal content.
            if (runtime::icall_object_equals(slots[i], value))
            {
                return i;
            }
        }
    }
    return -1;
}

bool il2cpp_arraylist_contains(Il2CppObject* self, Il2CppObject* value)
{
    return il2cpp_arraylist_index_of(self, value) >= 0;
}

Il2CppObject* il2cpp_arraylist_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // AllocItems can trigger a compacting collection: root 'self' and re-read
    // it afterwards (the items array and count are read from the rooted copy).
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_to_array");
    Il2CppArrayList* list = AsList(self);
    Il2CppArray* result = AllocItems(list->count);
    self = root.Get();
    list = AsList(self);
    // Pure pointer copy (no allocation inside); 'list' stays reachable
    // through the rooted slot while the copy runs.
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    Il2CppObject* const* src = list->items != nullptr
        ? reinterpret_cast<Il2CppObject* const*>(list->items->data) : nullptr;
    for (int32_t i = 0; i < list->count; ++i)
    {
        dst[i] = src != nullptr ? src[i] : nullptr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

} // namespace il2cpp
