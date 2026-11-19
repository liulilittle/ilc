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
#include "arraysort.h" // il2cpp_ienum_* (ArrayList(ICollection) enumeration)
#include "arrayextra.h" // il2cpp_array_get_value (array-element boxing)
#include "listi4.h" // il2cpp_list_i4_* (List<int> source)
#include "listobj.h" // il2cpp_list_obj_* (reference List<T> source)

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
static_assert(offsetof(Il2CppArrayList, wrapperKind) == 3 * sizeof(void*) - 4, "Il2CppArrayList: wrapperKind follows count (compact, NOT in the bitmap)");

// Minimum items-array length: avoids resizing on the very first add (and
// matches the .NET Framework's first grow target of 4).
constexpr int32_t kMinItems = 4;

// Doubling cap: keeps the doubling loop inside int32 (no overflow).
constexpr int32_t kMaxItems = 1 << 30;

// The .NET Framework 4.8 ArrayList out-of-range message (used for get/set/
// remove_at alike).
constexpr const char* kIndexOutOfRangeMessage =
    "Index was out of range. Must be non-negative and less than the size of the collection.";

// net48 ArgumentOutOfRangeException message for a negative index/count
// (ArgumentOutOfRange_NeedNonNegNum).
constexpr const char* kNeedNonNegMessage = "Non-negative number required.";

// net48 ArgumentException message for an out-of-bounds offset/length pair
// (Argument_InvalidOffLen).
constexpr const char* kOffLenMessage =
    "Offset and length were out of bounds for the array or count is greater "
    "than the number of elements from index to the end of the source collection.";

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

// The wrapper mutation guard: a ReadOnly wrapper rejects EVERY mutation; a
// FixedSize wrapper rejects every SIZE-CHANGING mutation but allows an
// existing-index overwrite (net48: FixedSize.Count is fixed, the elements are
// writable). Adapter / Synchronized wrappers are transparent.
void ThrowIfWrapperRejectsMutation(Il2CppObject* self, bool sizeChanging)
{
    const int32_t kind = AsList(self)->wrapperKind;
    if (kind == kArrayListReadOnly)
    {
        il2cpp_throw_not_supported_exception(
            "Collection is read-only.");
    }
    if (kind == kArrayListFixedSize && sizeChanging)
    {
        il2cpp_throw_not_supported_exception(
            "Collection was of a fixed size.");
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
        // The kMinItems ensure IS this grow pass: falling through would
        // double it to 8 on the very first Add (net48 Capacity == 4 after
        // the first Add from an empty list).
        return;
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
    // net48 ArrayList() keeps emptyArray: items stays null and Capacity reads
    // 0; the first Add goes through GrowList's null-items branch (kMinItems).
    list->items = nullptr;
    list->count = 0;
    return reinterpret_cast<Il2CppObject*>(list);
}

void il2cpp_arraylist_create_capacity(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity",
            "capacity is less than zero.");
    }
    // The receiver is the newobj-allocated list (the emitter's
    // il2cpp_object_new placeholder); the ctor pre-sizes its items array in
    // place. AllocItems can compact the container: root 'list' across the
    // allocation and re-read before the publication store (the GrowList /
    // set_capacity discipline).
    Il2CppArrayList* list = AsList(self);
    list->count = 0;
    if (capacity > 0)
    {
        GcRoot<Il2CppArrayList*> root(list, "icall_arraylist_create_capacity");
        Il2CppArray* arr = AllocItems(capacity);
        list = root.Get();
        list->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(list->items));
    }
    else
    {
        // Capacity 0 keeps items null (the first Add grows to the default 4),
        // matching the parameterless ctor arm above.
        list->items = nullptr;
    }
}

//------------------------------------------------------------------------------
// ArrayList(ICollection c) -- net48 arraylist.cs:86. Two-pass enumeration:
// the first pass counts the elements (exact capacity), the second fills the
// freshly allocated items array. The receiver, the collection and the items
// array are GC-rooted across the (possibly allocating) enumerator calls.
//------------------------------------------------------------------------------
void il2cpp_arraylist_create_from_collection(Il2CppObject* self, Il2CppObject* collection)
{
    ThrowIfNullReceiver(self);
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("c");
    }
    GcRoot<Il2CppObject*> rootSelf(self, "icall_arraylist_create_from_collection_self");
    GcRoot<Il2CppObject*> rootCollection(collection, "icall_arraylist_create_from_collection_c");

    // ARRAY source: a raw array is NOT a reference-slot sequence (an int[] cell
    // is not an Il2CppObject*), so the generic snapshot enumerator cannot be
    // used. Copy the elements directly, boxing value cells through the
    // arrayextra element core (byte/int32/int64 stride; net48 boxes the exact
    // primitive type -- the same helper contract as Array.GetValue).
    {
        Il2CppObject* c = rootCollection.Get();
        if (c->klass != nullptr && c->klass->name != nullptr && c->klass->name[0] == '[')
        {
            Il2CppArray* src = reinterpret_cast<Il2CppArray*>(c);
            const int32_t count = src->length;
            Il2CppArrayList* list0 = AsList(rootSelf.Get());
            if (count == 0)
            {
                list0->items = nullptr;
                list0->count = 0;
                return;
            }
            Il2CppArray* arr = AllocItems(count);
            GcRoot<Il2CppArray*> rootItems(arr, "icall_arraylist_create_from_collection_arr");
            int32_t filled = 0;
            for (int32_t i = 0; i < count; ++i)
            {
                Il2CppObject* item;
                src = reinterpret_cast<Il2CppArray*>(rootCollection.Get());
                if (src->isRefElem)
                {
                    item = reinterpret_cast<Il2CppObject**>(src->data)[i];
                }
                else
                {
                    item = il2cpp_array_get_value(src, i);
                }
                Il2CppArray* live = rootItems.Get();
                Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(live->data);
                dst[i] = item;
                il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), item);
                ++filled;
            }
            list0 = AsList(rootSelf.Get());
            arr = rootItems.Get();
            list0->items = arr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&list0->items),
                                 reinterpret_cast<Il2CppObject*>(list0->items));
            list0->count = filled;
            return;
        }
    }

    // ArrayList source: copy the reference slots directly.
    {
        Il2CppObject* c = rootCollection.Get();
        if (c->klass == il2cpp_arraylist_class())
        {
            Il2CppArrayList* src = AsList(c);
            const int32_t count = src->count;
            Il2CppArrayList* list0 = AsList(rootSelf.Get());
            if (count == 0)
            {
                list0->items = nullptr;
                list0->count = 0;
                return;
            }
            Il2CppArray* arr = AllocItems(count);
            GcRoot<Il2CppArray*> rootItems(arr, "icall_arraylist_create_from_collection_al");
            Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(arr->data);
            Il2CppObject** srcSlots = src->items != nullptr
                ? reinterpret_cast<Il2CppObject**>(src->items->data)
                : nullptr;
            for (int32_t i = 0; i < count; ++i)
            {
                Il2CppObject* item = srcSlots != nullptr ? srcSlots[i] : nullptr;
                Il2CppArray* live = rootItems.Get();
                dst = reinterpret_cast<Il2CppObject**>(live->data);
                dst[i] = item;
                il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), item);
            }
            list0 = AsList(rootSelf.Get());
            arr = rootItems.Get();
            list0->items = arr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&list0->items),
                                 reinterpret_cast<Il2CppObject*>(list0->items));
            list0->count = count;
            return;
        }
    }

    // List<int> source: the listi4 domain exposes Count/get_Item; box each
    // int32 element into the reference-slot store.
    {
        Il2CppObject* c = rootCollection.Get();
        if (c->klass == il2cpp_list_i4_class())
        {
            const int32_t count = il2cpp_list_i4_get_count(c);
            Il2CppArrayList* list0 = AsList(rootSelf.Get());
            if (count == 0)
            {
                list0->items = nullptr;
                list0->count = 0;
                return;
            }
            Il2CppArray* arr = AllocItems(count);
            GcRoot<Il2CppArray*> rootItems(arr, "icall_arraylist_create_from_collection_l4");
            for (int32_t i = 0; i < count; ++i)
            {
                c = rootCollection.Get();
                Il2CppObject* item = il2cpp_box_int32(nullptr, il2cpp_list_i4_get_item(c, i));
                Il2CppArray* live = rootItems.Get();
                Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(live->data);
                dst[i] = item;
                il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), item);
            }
            list0 = AsList(rootSelf.Get());
            arr = rootItems.Get();
            list0->items = arr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&list0->items),
                                 reinterpret_cast<Il2CppObject*>(list0->items));
            list0->count = count;
            return;
        }
    }

    // Pass 1: count the elements.
    int32_t count = 0;
    {
        Il2CppObject* en = il2cpp_ienum_get_enumerator_nongeneric(rootCollection.Get());
        GcRoot<Il2CppObject*> rootEnum(en, "icall_arraylist_create_from_collection_enum1");
        while (il2cpp_ienum_move_next(en))
        {
            if (count == INT32_MAX)
            {
                il2cpp_throw_argument_out_of_range_exception(
                    "c", "The collection is too large.");
            }
            ++count;
            en = rootEnum.Get();
        }
        il2cpp_ienum_dispose(en);
    }

    Il2CppArrayList* list = AsList(rootSelf.Get());
    if (count == 0)
    {
        // net48 keeps the empty array: Capacity 0, Count 0.
        list->items = nullptr;
        list->count = 0;
        return;
    }

    Il2CppArray* arr = AllocItems(count);
    GcRoot<Il2CppArray*> rootItems(arr, "icall_arraylist_create_from_collection_items");
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(arr->data);

    // Pass 2: fill (a fresh enumerator; the collection is rooted).
    int32_t filled = 0;
    {
        Il2CppObject* en = il2cpp_ienum_get_enumerator_nongeneric(rootCollection.Get());
        GcRoot<Il2CppObject*> rootEnum(en, "icall_arraylist_create_from_collection_enum2");
        while (filled < count && il2cpp_ienum_move_next(en))
        {
            Il2CppObject* item = il2cpp_ienum_get_current_obj(en);
            Il2CppArray* live = rootItems.Get();
            dst = reinterpret_cast<Il2CppObject**>(live->data);
            dst[filled] = item;
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[filled]), item);
            ++filled;
            en = rootEnum.Get();
        }
        il2cpp_ienum_dispose(en);
    }

    list = AsList(rootSelf.Get());
    arr = rootItems.Get();
    list->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(list->items));
    list->count = filled;
}

void il2cpp_arraylist_add(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfWrapperRejectsMutation(self, true);
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
    ThrowIfWrapperRejectsMutation(self, false);
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
    ThrowIfWrapperRejectsMutation(self, true);
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
    ThrowIfWrapperRejectsMutation(self, true);
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
    ThrowIfWrapperRejectsMutation(self, true);
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
    ThrowIfWrapperRejectsMutation(self, true);
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

// ArrayList A-class: capacity read (fresh newobj lists have a null items
// array and report 0), the three Is-* flags (always false for a plain
// ArrayList -- only wrappers change them, and this runtime has no wrapper
// type), and SyncRoot (the list itself, matching the lock(self) idiom).
int32_t il2cpp_arraylist_get_capacity(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    return list->items != nullptr ? list->items->length : 0;
}

bool il2cpp_arraylist_get_is_fixed_size(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsList(self)->wrapperKind == kArrayListFixedSize;
}

bool il2cpp_arraylist_get_is_read_only(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsList(self)->wrapperKind == kArrayListReadOnly;
}

bool il2cpp_arraylist_get_is_synchronized(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsList(self)->wrapperKind == kArrayListSynchronized;
}

Il2CppObject* il2cpp_arraylist_get_sync_root(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // net48's ArrayList.SyncRoot is a fresh System.Object per access (it is
    // NOT the list itself -- probe-alextra pins ReferenceEquals(root, list)
    // == False for a plain list, and the wrapper SyncRoot is a distinct
    // object too).
    return il2cpp_object_new(&System_Object_class);
}

// ArrayList B-class: capacity/reshape/clone/copy/reverse/remove-range.
// Allocation paths root 'self' across AllocItems (compacting collection).
void il2cpp_arraylist_set_capacity(Il2CppObject* self, int32_t value)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (value < list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("value", "Capacity was less than Count.");
    }
    int32_t cur = list->items != nullptr ? list->items->length : 0;
    if (value == cur) return;
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_set_capacity");
    Il2CppArray* fresh = value > 0 ? AllocItems(value) : nullptr;
    self = root.Get();
    list = AsList(self);
    if (list->items != nullptr && list->count > 0)
    {
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(fresh->data);
        Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            dst[i] = src[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
    }
    list->items = fresh;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(list->items));
}

Il2CppObject* il2cpp_arraylist_clone(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_clone");
    Il2CppArrayList* list = AsList(self);
    Il2CppObject* fresh = il2cpp_arraylist_create();
    self = root.Get();
    list = AsList(self);
    Il2CppArrayList* dst = AsList(fresh);
    GcRoot<Il2CppObject*> rootDst(fresh, "icall_arraylist_clone_dst");
    Il2CppArray* arr = list->count > 0 ? AllocItems(list->count) : nullptr;
    fresh = rootDst.Get();
    dst = AsList(fresh);
    if (list->items != nullptr && list->count > 0)
    {
        Il2CppObject** d = reinterpret_cast<Il2CppObject**>(arr->data);
        Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            d[i] = s[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&d[i]), d[i]);
        }
    }
    dst->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&dst->items),
                         reinterpret_cast<Il2CppObject*>(dst->items));
    dst->count = list->count;
    return fresh;
}

void il2cpp_arraylist_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    Il2CppArrayList* list = AsList(self);
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (arrayIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("arrayIndex", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(dst->length) - arrayIndex < list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    if (list->items == nullptr || list->count == 0) return;
    Il2CppObject** d = reinterpret_cast<Il2CppObject**>(dst->data);
    Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
    for (int32_t i = 0; i < list->count; ++i)
    {
        d[arrayIndex + i] = s[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&d[arrayIndex + i]), d[arrayIndex + i]);
    }
}

// ArrayList.CopyTo(Array) -- net48 arraylist.cs:655. Copies every element
// into the destination starting at index 0 (the 1-arg ICollection shape).
void il2cpp_arraylist_copy_to_array(Il2CppObject* self, Il2CppObject* array)
{
    ThrowIfNullReceiver(self);
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    il2cpp_arraylist_copy_to(self, array, 0);
}

// ArrayList.CopyTo(int, Array, int, int) -- net48 arraylist.cs:674. The
// source window [index, index+count) copies into dst[arrayIndex..]. The
// bounds contract (probe-alextra + net48 host measured):
//   index < 0 / arrayIndex < 0 / count < 0 -> ArgumentOutOfRangeException
//   index + count > Count / arrayIndex + count > dst.Length -> ArgumentException
//   array == null -> ArgumentNullException
void il2cpp_arraylist_copy_to_range(Il2CppObject* self, int32_t index, Il2CppObject* array,
                                    int32_t arrayIndex, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (arrayIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    Il2CppArrayList* list = AsList(self);
    if (static_cast<int64_t>(index) + count > list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (static_cast<int64_t>(arrayIndex) + count > dst->length)
    {
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, "
            "and the array's minimum length.");
    }
    if (count == 0 || list->items == nullptr) return;
    Il2CppObject** d = reinterpret_cast<Il2CppObject**>(dst->data);
    Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
    for (int32_t i = 0; i < count; ++i)
    {
        d[arrayIndex + i] = s[index + i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&d[arrayIndex + i]), d[arrayIndex + i]);
    }
}

void il2cpp_arraylist_trim_to_size(Il2CppObject* self)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    int32_t cur = list->items != nullptr ? list->items->length : 0;
    if (list->count == cur) return;
    il2cpp_arraylist_set_capacity(self, list->count);
}

void il2cpp_arraylist_reverse(Il2CppObject* self)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    il2cpp_arraylist_reverse_range(self, 0, list->count);
}

void il2cpp_arraylist_reverse_range(Il2CppObject* self, int32_t index, int32_t length)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(list->count) - index < length)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    if (list->items == nullptr) return;
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    for (int32_t i = 0; i < length / 2; ++i)
    {
        Il2CppObject* tmp = slots[index + i];
        slots[index + i] = slots[index + length - 1 - i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index + i]), slots[index + i]);
        slots[index + length - 1 - i] = tmp;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index + length - 1 - i]), tmp);
    }
}

void il2cpp_arraylist_remove_range(Il2CppObject* self, int32_t index, int32_t length)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(list->count) - index < length)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    if (length == 0) return;
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    for (int32_t i = index + length; i < list->count; ++i)
    {
        slots[i - length] = slots[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i - length]), slots[i - length]);
    }
    for (int32_t i = list->count - length; i < list->count; ++i)
    {
        slots[i] = nullptr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
    }
    list->count -= length;
}

//------------------------------------------------------------------------------
// ArrayList C-class: enumeration. GetEnumerator() / GetEnumerator(index,count)
// wrap a SNAPSHOT of the current elements in the shared Il2CppArrayEnumerator
// (the same carrier Array.GetEnumerator uses, so MoveNext/Reset/Dispose ride
// the existing il2cpp_sortedlist_enumerator_* icalls and get_Current boxes a
// value element through ArrayEnumRef).
//
// net48's ArrayList enumerator is a LIVE view: a structural change after the
// first MoveNext makes MoveNext throw InvalidOperationException ("Collection
// was modified..."). A snapshot makes the whole contents frozen, so the
// "modified" path is unreachable -- the frozen array IS the net48 enumerator's
// state at GetEnumerator time for every read that does not race a mutation.
// The range overload bounds the window: index < 0 -> ArgumentOutOfRangeException;
// index > Count or count > Count - index -> ArgumentException (net48 host
// measured: g31/g40 -> ArgumentException, g2n -> ArgumentOutOfRangeException).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_arraylist_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    // Snapshot the current elements into a reference array of exactly count
    // (a compacting allocation can move the container: root and re-read).
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_get_enumerator");
    Il2CppArray* snap = AllocItems(list->count);
    self = root.Get();
    list = AsList(self);
    if (list->items != nullptr && list->count > 0)
    {
        Il2CppObject** d = reinterpret_cast<Il2CppObject**>(snap->data);
        Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            d[i] = s[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&d[i]), d[i]);
        }
    }
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
        il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
    en->array = snap;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->array),
                         reinterpret_cast<Il2CppObject*>(en->array));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

Il2CppObject* il2cpp_arraylist_get_enumerator_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    Il2CppArrayList* list = AsList(self);
    if (index > list->count || static_cast<int64_t>(list->count) - index < count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_get_enumerator_range");
    Il2CppArray* snap = AllocItems(count);
    self = root.Get();
    list = AsList(self);
    if (list->items != nullptr && count > 0)
    {
        Il2CppObject** d = reinterpret_cast<Il2CppObject**>(snap->data);
        Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < count; ++i)
        {
            d[i] = s[index + i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&d[i]), d[i]);
        }
    }
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
        il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
    en->array = snap;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->array),
                         reinterpret_cast<Il2CppObject*>(en->array));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

//------------------------------------------------------------------------------
// ArrayList D-class: GetRange / AddRange / InsertRange / SetRange.
//
// The ICollection source is enumerated through the non-generic IEnumerator
// surface. A null source raises ArgumentNullException("c"). GetRange returns
// a NEW ArrayList whose Capacity equals the SOURCE list's current Capacity
// (net48 copies the whole backing array; an empty source answers Capacity 0).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_arraylist_get_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_get_range");
    Il2CppArrayList* list = AsList(self);
    if (static_cast<int64_t>(index) + count > list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    const int32_t srcCapacity = list->items != nullptr ? list->items->length : 0;
    Il2CppObject* fresh = il2cpp_arraylist_create();
    self = root.Get();
    Il2CppArrayList* dst = AsList(fresh);
    GcRoot<Il2CppObject*> rootDst(fresh, "icall_arraylist_get_range_dst");
    if (count > 0)
    {
        // The copy is a fresh backing array sized to the source capacity (the
        // net48 GetRange contract), then only 'count' slots are filled.
        Il2CppArray* arr = AllocItems(srcCapacity > 0 ? srcCapacity : count);
        fresh = rootDst.Get();
        dst = AsList(fresh);
        Il2CppObject** d = reinterpret_cast<Il2CppObject**>(arr->data);
        Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < count; ++i)
        {
            d[i] = s[index + i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&d[i]), d[i]);
        }
        dst->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst->items),
                             reinterpret_cast<Il2CppObject*>(dst->items));
    }
    else if (srcCapacity > 0)
    {
        // A zero-count range over a non-empty list keeps the source capacity
        // (net48: the backing array is still copied).
        Il2CppArray* arr = AllocItems(srcCapacity);
        fresh = rootDst.Get();
        dst = AsList(fresh);
        dst->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst->items),
                             reinterpret_cast<Il2CppObject*>(dst->items));
    }
    dst->count = count;
    return fresh;
}

// Shared ICollection drain: append every element of 'collection' to 'dst'
// (the AddRange / InsertRange core). The collection is enumerated through the
// non-generic IEnumerator; a null raises ArgumentNullException("c").
void ArrayListAppendCollection(Il2CppArrayList* dst, Il2CppObject* collection, const char* rootName)
{
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("c");
    }
    GcRoot<Il2CppObject*> rootColl(collection, rootName);
    Il2CppObject* en = il2cpp_ienum_get_enumerator_nongeneric(rootColl.Get());
    GcRoot<Il2CppObject*> rootEnum(en, "icall_arraylist_append_enum");
    while (il2cpp_ienum_move_next(en))
    {
        Il2CppObject* item = il2cpp_ienum_get_current_obj(en);
        // The append grows on demand (the add path's GrowList): the dst
        // container and its items pointer are re-read after every allocation.
        if (dst->items == nullptr || dst->count == dst->items->length)
        {
            GrowList(dst);
        }
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(dst->items->data);
        slots[dst->count] = item;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[dst->count]), item);
        ++dst->count;
        en = rootEnum.Get();
    }
    il2cpp_ienum_dispose(en);
}

void il2cpp_arraylist_add_range(Il2CppObject* self, Il2CppObject* collection)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("c");
    }
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_add_range");
    Il2CppArrayList* list = AsList(self);
    ArrayListAppendCollection(list, collection, "icall_arraylist_add_range_coll");
    (void)root;
}

void il2cpp_arraylist_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("c");
    }
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_insert_range");
    Il2CppArrayList* list = AsList(self);
    if (index < 0 || index > list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    // Drain into a temporary list, then splice it in at 'index' (the shift is
    // the Insert loop reversed). The temporary keeps the enumeration source
    // independent of the receiver's own growth.
    Il2CppObject* tmp = il2cpp_arraylist_create();
    self = root.Get();
    list = AsList(self);
    ArrayListAppendCollection(AsList(tmp), collection, "icall_arraylist_insert_range_tmp");
    Il2CppArrayList* src = AsList(tmp);
    if (src->count > 0)
    {
        if (list->items == nullptr || list->count + src->count > list->items->length)
        {
            // Grow to fit (the doubling rule), then re-read the container.
            const int32_t need = list->count + src->count;
            while (list->items != nullptr && list->items->length < need)
            {
                GrowList(list);
                self = root.Get();
                list = AsList(self);
            }
            if (list->items == nullptr || list->items->length < need)
            {
                GrowList(list);
                self = root.Get();
                list = AsList(self);
            }
        }
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        for (int32_t i = list->count - 1; i >= index; --i)
        {
            slots[i + src->count] = slots[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i + src->count]), slots[i + src->count]);
        }
        Il2CppObject* const* s = reinterpret_cast<Il2CppObject* const*>(src->items->data);
        for (int32_t i = 0; i < src->count; ++i)
        {
            slots[index + i] = s[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index + i]), slots[index + i]);
        }
        list->count += src->count;
    }
}

void il2cpp_arraylist_set_range(Il2CppObject* self, int32_t index, Il2CppObject* collection)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("c");
    }
    GcRoot<Il2CppObject*> root(self, "icall_arraylist_set_range");
    GcRoot<Il2CppObject*> rootColl(collection, "icall_arraylist_set_range_coll");
    Il2CppArrayList* list = AsList(self);
    // Count the source first (the net48 ArgumentException contract: the range
    // must fit inside the receiver).
    int32_t srcCount = 0;
    {
        Il2CppObject* probe = il2cpp_ienum_get_enumerator_nongeneric(rootColl.Get());
        GcRoot<Il2CppObject*> rootProbe(probe, "icall_arraylist_set_range_probe");
        while (il2cpp_ienum_move_next(probe))
        {
            ++srcCount;
            probe = rootProbe.Get();
        }
        il2cpp_ienum_dispose(probe);
    }
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(index) + srcCount > list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    Il2CppObject* en = il2cpp_ienum_get_enumerator_nongeneric(rootColl.Get());
    GcRoot<Il2CppObject*> rootEnum(en, "icall_arraylist_set_range_enum");
    int32_t at = index;
    while (il2cpp_ienum_move_next(en))
    {
        Il2CppObject* item = il2cpp_ienum_get_current_obj(en);
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        slots[at] = item;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[at]), item);
        ++at;
        en = rootEnum.Get();
    }
    il2cpp_ienum_dispose(en);
}

// ArrayList E-class: ToArray. ToArray() is the reference-slot copy (to_array).
// ToArray(Type) builds a typed array: the Type value IS the type-name string
// (typecheck.cpp), so the element name selects the element family. A null
// Type raises ArgumentNullException; an unknown/value-mismatched element type
// raises InvalidCastException (net48: "The value of type X cannot be
// converted to Y" / a value element that does not match the target primitive
// is an InvalidCastException). A null element into a value element array is
// an InvalidCastException too.
Il2CppObject* il2cpp_arraylist_to_array_typed(Il2CppObject* self, Il2CppObject* type)
{
    ThrowIfNullReceiver(self);
    if (type == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    auto s = reinterpret_cast<Il2CppString*>(type);
    char buf[128];
    if (s->length < 0 || s->length >= static_cast<int32_t>(sizeof(buf)))
    {
        il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
        return nullptr;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] > 0x7F)
        {
            il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
            return nullptr;
        }
        buf[i] = static_cast<char>(s->chars[i]);
    }
    buf[s->length] = '\0';

    // The element family table: the value families carry their C++ width and
    // the box klass name used for the element cast check; the reference
    // families (object/string) accept any reference element (null included).
    const char* elem = buf;
    int32_t width = 0;
    const char* boxName = nullptr;
    if (std::strcmp(elem, "System.Object") == 0 || std::strcmp(elem, "System.String") == 0)
    {
        width = static_cast<int32_t>(sizeof(void*));
        boxName = nullptr;
    }
    else if (std::strcmp(elem, "System.Int32") == 0) { width = 4; boxName = "System.Int32"; }
    else if (std::strcmp(elem, "System.Int64") == 0) { width = 8; boxName = "System.Int64"; }
    else if (std::strcmp(elem, "System.Double") == 0) { width = 8; boxName = "System.Double"; }
    else if (std::strcmp(elem, "System.Boolean") == 0) { width = 4; boxName = "System.Boolean"; }
    else if (std::strcmp(elem, "System.Char") == 0) { width = 4; boxName = "System.Char"; }
    else if (std::strcmp(elem, "System.Byte") == 0) { width = 4; boxName = "System.Byte"; }
    else if (std::strcmp(elem, "System.SByte") == 0) { width = 4; boxName = "System.SByte"; }
    else if (std::strcmp(elem, "System.Int16") == 0) { width = 4; boxName = "System.Int16"; }
    else if (std::strcmp(elem, "System.UInt16") == 0) { width = 4; boxName = "System.UInt16"; }
    else if (std::strcmp(elem, "System.UInt32") == 0) { width = 4; boxName = "System.UInt32"; }
    else if (std::strcmp(elem, "System.UInt64") == 0) { width = 8; boxName = "System.UInt64"; }
    else if (std::strcmp(elem, "System.Single") == 0) { width = 8; boxName = "System.Single"; }
    else if (std::strcmp(elem, "System.IntPtr") == 0) { width = static_cast<int32_t>(sizeof(void*)); boxName = nullptr; }
    else
    {
        // An element type this build has no descriptor for: net48 raises
        // InvalidCastException for the element conversion (probe-alextra pins
        // int[] -> string[] / long[] / int[][] all InvalidCastException).
        il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
        return nullptr;
    }

    GcRoot<Il2CppObject*> root(self, "icall_arraylist_to_array_typed");
    Il2CppArrayList* list = AsList(self);
    const bool isRefElem = boxName == nullptr;
    Il2CppArray* result = il2cpp_array_new(nullptr, width, isRefElem, list->count);
    self = root.Get();
    list = AsList(self);
    if (list->items != nullptr && list->count > 0)
    {
        char* dst = result->data;
        Il2CppObject* const* s2 = reinterpret_cast<Il2CppObject* const*>(list->items->data);
        for (int32_t i = 0; i < list->count; ++i)
        {
            Il2CppObject* item = s2[i];
            if (isRefElem)
            {
                if (std::strcmp(elem, "System.String") == 0)
                {
                    // A string[] element must be a string or null.
                    if (item != nullptr && (item->klass == nullptr || item->klass->name == nullptr
                        || std::strcmp(item->klass->name, "System.String") != 0))
                    {
                        il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
                        return nullptr;
                    }
                }
                else if (item != nullptr && std::strcmp(elem, "System.Object") == 0)
                {
                    // object[] accepts anything.
                }
                // IntPtr[] rides the reference-slot ABI for this build.
                reinterpret_cast<Il2CppObject**>(dst)[i] = item;
                il2cpp_write_barrier(reinterpret_cast<void**>(&reinterpret_cast<Il2CppObject**>(dst)[i]), item);
            }
            else
            {
                // A value element must be a box of EXACTLY the target type
                // (net48: an int element into a long[] is an InvalidCastException).
                if (item == nullptr || item->klass == nullptr
                    || item->klass->name == nullptr
                    || std::strcmp(item->klass->name, boxName) != 0)
                {
                    il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
                    return nullptr;
                }
                if (width == 8)
                {
                    const int64_t v = *reinterpret_cast<const int64_t*>(
                        reinterpret_cast<const char*>(item) + 8);
                    *reinterpret_cast<int64_t*>(dst + static_cast<size_t>(i) * 8) = v;
                }
                else
                {
                    const int32_t v = *reinterpret_cast<const int32_t*>(
                        reinterpret_cast<const char*>(item) + 8);
                    *reinterpret_cast<int32_t*>(dst + static_cast<size_t>(i) * 4) = v;
                }
            }
        }
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// ArrayList F-class: LastIndexOf / Sort / BinarySearch.
//------------------------------------------------------------------------------

int32_t il2cpp_arraylist_last_index_of(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        for (int32_t i = list->count - 1; i >= 0; --i)
        {
            if (runtime::icall_object_equals(slots[i], value)) return i;
        }
    }
    return -1;
}

int32_t il2cpp_arraylist_last_index_of_start(Il2CppObject* self, Il2CppObject* value, int32_t startIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items == nullptr && startIndex == 0)
    {
        return -1; // net48: an empty list accepts startIndex 0 -> -1
    }
    if (startIndex < 0 || startIndex >= list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", kNeedNonNegMessage);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    for (int32_t i = startIndex; i >= 0; --i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

int32_t il2cpp_arraylist_last_index_of_range(Il2CppObject* self, Il2CppObject* value,
                                             int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->items == nullptr && startIndex == 0)
    {
        if (count < 0)
        {
            il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
        }
        return -1;
    }
    if (startIndex < 0 || startIndex >= list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", kNeedNonNegMessage);
    }
    if (count < 0 || static_cast<int64_t>(count) > static_cast<int64_t>(startIndex) + 1)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

//------------------------------------------------------------------------------
// ArrayList.Sort -- an in-place quicksort over the reference slots. The
// default order is Comparer.Default (ComparerCompareCore: null first, string
// pairs culture, builtin boxes by value); a user comparer (Sort(IComparer) /
// Sort(index,count,IComparer)) dispatches through il2cpp_comparer_compare.
// A null comparer means Comparer.Default. The mixed-type failure is
// InvalidOperationException (net48).
//------------------------------------------------------------------------------
namespace {

int32_t ArrayListCompare(Il2CppObject* a, Il2CppObject* b, Il2CppObject* comparer)
{
    // A null comparer means Comparer.Default (net48): the singleton's
    // Compare(object,object) is the default order. The singleton accessor
    // registers the Comparer class on first use.
    return il2cpp_comparer_compare(comparer != nullptr ? comparer : il2cpp_comparer_default(), a, b);
}

void ArrayListQuickSort(Il2CppObject** slots, int32_t lo, int32_t hi, Il2CppObject* comparer)
{
    while (lo < hi)
    {
        const int32_t pivot = lo + (hi - lo) / 2;
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (ArrayListCompare(slots[i], slots[pivot], comparer) < 0) ++i;
            while (ArrayListCompare(slots[j], slots[pivot], comparer) > 0) --j;
            if (i <= j)
            {
                Il2CppObject* tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            ArrayListQuickSort(slots, lo, j, comparer);
            lo = i;
        }
        else
        {
            ArrayListQuickSort(slots, i, hi, comparer);
            hi = j;
        }
    }
}

} // namespace

void il2cpp_arraylist_sort(Il2CppObject* self)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->count > 1 && list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        try
        {
            ArrayListQuickSort(slots, 0, list->count - 1, nullptr);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(
                "Failed to compare two elements in the array.");
        }
        // Re-publish the shuffled slots through the write barrier (the sort
        // moved references without it).
        for (int32_t i = 0; i < list->count; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
}

void il2cpp_arraylist_sort_comparer(Il2CppObject* self, Il2CppObject* comparer)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->count > 1 && list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        try
        {
            ArrayListQuickSort(slots, 0, list->count - 1, comparer);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(
                "Failed to compare two elements in the array.");
        }
        for (int32_t i = 0; i < list->count; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
}

void il2cpp_arraylist_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count, Il2CppObject* comparer)
{
    ThrowIfWrapperRejectsMutation(self, true);
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(index) + count > list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    if (count > 1 && list->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(list->items->data);
        try
        {
            ArrayListQuickSort(slots, index, index + count - 1, comparer);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(
                "Failed to compare two elements in the array.");
        }
        for (int32_t i = index; i < index + count; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
}

//------------------------------------------------------------------------------
// ArrayList.BinarySearch -- a lower-bound binary search over the ASCENDING
// window. A found element returns its index; a missing one the bitwise
// complement of the insertion point. A null / absent window yields -1 (= ~0
// on an empty window). The comparison uses Comparer.Default or the user
// comparer (net48).
//------------------------------------------------------------------------------
namespace {

int32_t ArrayListSearchCore(Il2CppObject* const* slots, int32_t lo, int32_t hi,
                            Il2CppObject* value, Il2CppObject* comparer)
{
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t cmp = ArrayListCompare(slots[mid], value, comparer);
        if (cmp == 0) return mid;
        if (cmp < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

} // namespace

int32_t il2cpp_arraylist_binary_search(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->count == 0 || list->items == nullptr) return -1;
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(list->items->data);
    try
    {
        return ArrayListSearchCore(slots, 0, list->count - 1, value, nullptr);
    }
    catch (const DotNetException&)
    {
        il2cpp_throw_invalid_operation_exception(
            "Failed to compare two elements in the array.");
        return -1;
    }
}

int32_t il2cpp_arraylist_binary_search_comparer(Il2CppObject* self, Il2CppObject* value, Il2CppObject* comparer)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (list->count == 0 || list->items == nullptr) return -1;
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(list->items->data);
    try
    {
        return ArrayListSearchCore(slots, 0, list->count - 1, value, comparer);
    }
    catch (const DotNetException&)
    {
        il2cpp_throw_invalid_operation_exception(
            "Failed to compare two elements in the array.");
        return -1;
    }
}

int32_t il2cpp_arraylist_binary_search_range_comparer(Il2CppObject* self, int32_t index, int32_t count,
                                                      Il2CppObject* value, Il2CppObject* comparer)
{
    ThrowIfNullReceiver(self);
    Il2CppArrayList* list = AsList(self);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kNeedNonNegMessage);
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    if (static_cast<int64_t>(index) + count > list->count)
    {
        il2cpp_throw_argument_exception(kOffLenMessage);
    }
    if (count == 0 || list->items == nullptr) return ~index;
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(list->items->data);
    try
    {
        const int32_t found = ArrayListSearchCore(slots, index, index + count - 1, value, comparer);
        return found >= 0 ? found : found;
    }
    catch (const DotNetException&)
    {
        il2cpp_throw_invalid_operation_exception(
            "Failed to compare two elements in the array.");
        return -1;
    }
}

//------------------------------------------------------------------------------
// ArrayList G-class: Repeat + the static wrappers.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_arraylist_repeat(Il2CppObject* value, int32_t count)
{
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", kNeedNonNegMessage);
    }
    Il2CppObject* fresh = il2cpp_arraylist_create();
    if (count == 0)
    {
        // net48 Repeat(value, 0) reports Capacity 4 (the default empty list
        // grows to 4 on the first add -- but an explicit 0 keeps the empty
        // backing array). Host-measured: rep0_cnt=0 rep0_cap=4.
        Il2CppArrayList* list = AsList(fresh);
        Il2CppArray* arr = AllocItems(kMinItems);
        list->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(list->items));
        return fresh;
    }
    Il2CppArrayList* list = AsList(fresh);
    Il2CppArray* arr = AllocItems(count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        slots[i] = value;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), value);
    }
    list->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(list->items));
    list->count = count;
    return fresh;
}

//------------------------------------------------------------------------------
// ArrayList wrappers. A wrapper SHARES the source list's items array and
// count (the net48 wrapper is a live view over the same storage) and carries
// its own kind tag. The flags answer from the tag:
//   IsFixedSize    == (kind == FixedSize)
//   IsReadOnly     == (kind == ReadOnly)
//   IsSynchronized == (kind == Synchronized)
//   SyncRoot       == a fresh System.Object (net48's wrapper SyncRoot is a new
//                    object, never the wrapper itself)
// The mutating calls on a ReadOnly / FixedSize wrapper raise
// NotSupportedException (net48: ReadOnly rejects every mutation; FixedSize
// rejects size-changing mutations but allows an existing-index overwrite).
//----------------------------------------------------------------------------//
Il2CppObject* ArrayListMakeWrapper(Il2CppObject* list, int32_t kind)
{
    if (list == nullptr)
    {
        il2cpp_throw_argument_null_exception("list");
    }
    EnsureArrayListRegistered();
    Il2CppArrayList* src = AsList(list);
    Il2CppObject* fresh = il2cpp_arraylist_create();
    Il2CppArrayList* w = AsList(fresh);
    // Share the source storage. The source is GC-rooted through the wrapper's
    // own reference field (slot 3 is NOT a GC ref -- the kind tag -- so the
    // wrapper keeps the array alive directly; the source list object itself is
    // reachable from the caller's frame in every wrapper call).
    w->items = src->items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->items),
                         reinterpret_cast<Il2CppObject*>(w->items));
    w->count = src->count;
    w->wrapperKind = kind;
    return fresh;
}

Il2CppObject* il2cpp_arraylist_adapter(Il2CppObject* list)
{
    return ArrayListMakeWrapper(list, kArrayListAdapter);
}

Il2CppObject* il2cpp_arraylist_fixed_size(Il2CppObject* list)
{
    return ArrayListMakeWrapper(list, kArrayListFixedSize);
}

Il2CppObject* il2cpp_arraylist_read_only(Il2CppObject* list)
{
    return ArrayListMakeWrapper(list, kArrayListReadOnly);
}

Il2CppObject* il2cpp_arraylist_synchronized(Il2CppObject* list)
{
    return ArrayListMakeWrapper(list, kArrayListSynchronized);
}

} // namespace il2cpp
