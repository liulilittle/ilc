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

#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32 (boxed sources)
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "arraysort.h"
#include "hashset.h"    // il2cpp_hashset_class + Il2CppHashSetObject (ICollection<int> source)
#include "stackqueue.h" // il2cpp_stack_class/il2cpp_queue_class + typed enumerators

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

//--------------------------------------------------------------------------
// COLLECTION-SOURCE NORMALIZATION.
//
// net48's List<T> accepts ANY IEnumerable<T> at the three collection-source
// entry points (the ctor, AddRange and InsertRange). The reference source
// (`collections_generic_list.cs`) splits the sources in two:
//
//   * an ICollection<T> source is BULK-COPIED: its Count pre-sizes the items
//     array EXACTLY (EnsureCapacity(_size + c.Count)) and the cells are copied
//     (or enumerated once for a set, whose internal slot order is the only
//     observable order the runtime has);
//   * every other IEnumerable<T> is enumerated element by element through
//     Add/Insert, so the capacity grows the ordinary way (0 -> 4 -> doubling).
//
// The runtime used to accept a one-dimensional int32 array ONLY and raise
// ArgumentException("Object is not an array.") for every other shape, so
// List<int>/yield/LINQ/HashSet/Queue sources threw on code that runs on net48.
//
// Normalization (net48 order: the null check precedes the index check):
//   1. int32[]        -> ICollection<int> bulk copy (memcpy fast path)
//   2. List<int>      -> ICollection<int> bulk copy; a SELF source is
//                        snapshotted first (the cells are read before the
//                        growth the copy itself requires)
//   3. HashSet<int>   -> ICollection<int>: live Count pre-sizes, then the
//                        shared snapshot enumerator supplies the cells
//   4. other enumerables (yield / LINQ / Queue<int> / Stack<int> / user
//      IEnumerable<int>) -> GetEnumerator / MoveNext / get_Current via the
//                        i4 enumerator kernel, Dispose exactly once (also on
//                        the throwing path), get_Current NEVER read after
//                        MoveNext returns false.
//--------------------------------------------------------------------------

// The ICollection<int> element count of 'source', or -1 when the source has
// no int-collection carrier and must be enumerated.
constexpr int32_t kNotIntCollection = -1;

int32_t IntCollectionCount(Il2CppObject* source)
{
    Il2CppClass* klass = source->klass;
    if (klass == nullptr || klass->name == nullptr)
    {
        return kNotIntCollection;
    }
    if (klass->name[0] == '[')
    {
        Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(source);
        if (arr->isRefElem) return kNotIntCollection;
        if (arr->elemSize != static_cast<int32_t>(sizeof(int32_t))) return kNotIntCollection;
        if (arr->elemKind != kElemKindI4 && arr->elemKind != kElemKindNone)
            return kNotIntCollection;
        return arr->length;
    }
    if (klass == il2cpp_list_i4_class())
    {
        return reinterpret_cast<Il2CppListI4*>(source)->count;
    }
    if (klass == il2cpp_hashset_class())
    {
        // One shared descriptor backs EVERY HashSet<T> family; only the int32
        // family is an ICollection<int> at this surface. The live count
        // (slot count minus the free-list holes) is the net48 Count.
        Il2CppHashSetObject* set = reinterpret_cast<Il2CppHashSetObject*>(source);
        if (set->family != 1) return kNotIntCollection;
        return set->count - set->freeCount;
    }
    return kNotIntCollection;
}

// The int32 cells of an ICollection<int> source, or nullptr when the source
// has no contiguous int32 storage (the HashSet family). MUST be called AFTER
// every allocation of the operation: a Gen0 compaction may move the source
// array/list object, and the caller re-derives it from the rooted slot.
const int32_t* IntCollectionCells(Il2CppObject* source)
{
    Il2CppClass* klass = source->klass;
    if (klass == nullptr || klass->name == nullptr) return nullptr;
    if (klass->name[0] == '[')
    {
        return reinterpret_cast<const int32_t*>(reinterpret_cast<Il2CppArray*>(source)->data);
    }
    if (klass == il2cpp_list_i4_class())
    {
        Il2CppListI4* list = reinterpret_cast<Il2CppListI4*>(source);
        return list->items != nullptr ? reinterpret_cast<const int32_t*>(list->items->data)
                                      : nullptr;
    }
    return nullptr;
}

// IEnumerable<int>.GetEnumerator for the shapes that have NO generic
// interface dispatch in this runtime: the int32-typed Queue/Stack expansions
// ride the shared non-generic container descriptors, whose snapshot
// enumerators are addressed directly (the transpiler's foreach does the same
// in MethodMap.Collections).
Il2CppObject* GetIntSourceEnumerator(Il2CppObject* source)
{
    Il2CppClass* klass = source->klass;
    if (klass == il2cpp_queue_class()) return il2cpp_queue_get_enumerator(source);
    if (klass == il2cpp_stack_class()) return il2cpp_stack_get_enumerator(source);
    if (klass != nullptr && klass->name != nullptr && klass->name[0] == '[')
    {
        // net48's `foreach (int x in source)` casts EVERY element; a value
        // array of another width (long[]/double[]/DateTime[]/...) can never
        // yield an int32, so it is an InvalidCastException, never a silent
        // numeric conversion.
        Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(source);
        if (!arr->isRefElem &&
            (arr->elemSize != static_cast<int32_t>(sizeof(int32_t)) ||
             (arr->elemKind != kElemKindI4 && arr->elemKind != kElemKindNone)))
        {
            il2cpp_throw_new(
                "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                "Specified cast is not valid.");
        }
    }
    return il2cpp_ienum_get_enumerator(source);
}

int32_t IntSourceMoveNext(Il2CppObject* en)
{
    if (en == nullptr)
    {
        // The source's GetEnumerator returned null: net48's foreach reads
        // en.MoveNext() and raises NullReferenceException.
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    if (en->klass == &System_Collections_Queue_Enumerator_class)
        return il2cpp_queue_enumerator_move_next(en);
    if (en->klass == &System_Collections_Stack_Enumerator_class)
        return il2cpp_stack_enumerator_move_next(en);
    return il2cpp_ienum_move_next(en);
}

int32_t IntSourceCurrent(Il2CppObject* en)
{
    if (en->klass == &System_Collections_Queue_Enumerator_class)
        return runtime::icall_unbox_int32(il2cpp_queue_enumerator_get_current(en));
    if (en->klass == &System_Collections_Stack_Enumerator_class)
        return runtime::icall_unbox_int32(il2cpp_stack_enumerator_get_current(en));
    return il2cpp_ienum_get_current_i4(en);
}

// The `foreach` finally arm: Dispose runs exactly once on EVERY exit path
// (normal completion and a MoveNext/Add exception both). A null enumerator
// (the source's GetEnumerator returned null) is left alone -- net48's using
// skips the null Dispose and the MoveNext below raises NRE.
class IntSourceEnumeratorGuard
{
public:
    IntSourceEnumeratorGuard() : m_slot(nullptr) {}
    ~IntSourceEnumeratorGuard()
    {
        if (m_slot != nullptr && *m_slot != nullptr)
        {
            il2cpp_ienum_dispose(*m_slot);
        }
    }
    void Attach(Il2CppObject** slot) { m_slot = slot; }

private:
    Il2CppObject** m_slot;
};

// Grow the items array so at least 'needed' elements fit, following the net48
// EnsureCapacity shape: double (floor kMinItems at zero), then raise to
// 'needed' when the doubling is still too small (the bulk-copy branch).
void EnsureCapacityAtLeast(Il2CppObject** selfSlot, int32_t needed)
{
    EnsureItems(selfSlot);
    Il2CppListI4* list = AsList(*selfSlot);
    if (list->items->length >= needed) return;
    if (needed > kMaxItems)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Array dimensions exceeded supported range.");
    }
    const int32_t oldLength = list->items->length;
    int32_t newLength = NextLength(oldLength);
    if (newLength < needed) newLength = needed;
    if (newLength <= oldLength) return; // already at the cap
    Il2CppArray* newItems = AllocItems(newLength);
    list = AsList(*selfSlot); // re-derive after the (possibly collecting) allocation
    int32_t* dst = reinterpret_cast<int32_t*>(newItems->data);
    const int32_t* src = Slots(list);
    for (int32_t i = 0; i < oldLength; ++i)
    {
        dst[i] = src[i];
    }
    il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                         reinterpret_cast<Il2CppObject*>(newItems));
    list->items = newItems;
}

// Bulk copy of an ICollection<int> source into the range [index, index+srcLen).
// 'capacityReady' says the caller already ensured the room (the ctor pre-sizes
// its own storage and never shifts); otherwise the net48 EnsureCapacity shape
// is applied here. 'sourceSlot' is the caller's ROOTED source slot: the growth
// and the enumerator allocation can compact the source object, so it is
// re-read after every allocation instead of trusting a stale by-value copy.
void InsertCollectionCells(Il2CppObject** selfSlot, int32_t index,
                           Il2CppObject** sourceSlot, int32_t srcLen, bool capacityReady)
{
    if (srcLen <= 0) return;
    const int32_t oldCount = AsList(*selfSlot)->count;
    if (!capacityReady) EnsureCapacityAtLeast(selfSlot, oldCount + srcLen);
    {
        Il2CppListI4* list = AsList(*selfSlot);
        int32_t* slots = Slots(list);
        for (int32_t i = oldCount - 1; i >= index; --i)
        {
            slots[i + srcLen] = slots[i];
        }
    }
    const int32_t* src = IntCollectionCells(*sourceSlot);
    if (src != nullptr)
    {
        int32_t* slots = Slots(AsList(*selfSlot));
        for (int32_t i = 0; i < srcLen; ++i)
        {
            slots[index + i] = src[i];
        }
    }
    else
    {
        // HashSet<int>: its internal slot order IS the net48 CopyTo order, so
        // the shared snapshot enumerator supplies the cells. The root scope is
        // declared BEFORE the guard so the guard (which calls Dispose) is torn
        // down while the enumerator is still rooted.
        Il2CppObject* en = GetIntSourceEnumerator(*sourceSlot);
        RootScope scope;
        scope.Add(reinterpret_cast<void**>(&en));
        IntSourceEnumeratorGuard guard;
        guard.Attach(&en);
        int32_t w = 0;
        while (w < srcLen && IntSourceMoveNext(en) != 0)
        {
            const int32_t value = IntSourceCurrent(en);
            int32_t* slots = Slots(AsList(*selfSlot)); // re-derive (get_Current may box)
            slots[index + w] = value;
            ++w;
        }
    }
    AsList(*selfSlot)->count = oldCount + srcLen;
}

// Enumerate 'source' through the int32 enumerator kernel, inserting each
// element at a RUNNING index (net48's `while (en.MoveNext()) Insert(index++,
// en.Current)`), so an exception mid-way keeps the prefix that was already
// inserted and the net48 capacity growth is reproduced exactly.
void InsertEnumerableCells(Il2CppObject** selfSlot, int32_t index, Il2CppObject* source)
{
    Il2CppObject* en = GetIntSourceEnumerator(source);
    IntSourceEnumeratorGuard guard;
    guard.Attach(&en);
    // The enumerator is a GC reference held only in this frame: root the slot
    // (the per-element Insert can allocate and compact).
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&en));
    while (IntSourceMoveNext(en) != 0)
    {
        const int32_t value = IntSourceCurrent(en);
        il2cpp_list_i4_insert(*selfSlot, index, value);
        ++index;
    }
}

// Snapshot the int32 cells of a List<int> source into a fresh exact-length
// value array. Used for SELF references (AddRange(this) / InsertRange(i,this)):
// the growth the copy needs would otherwise replace the very array the cells
// are being read from.
Il2CppArray* SnapshotListCells(Il2CppObject** sourceSlot, int32_t count)
{
    Il2CppArray* snap = AllocItems(count);
    Il2CppListI4* src = AsList(*sourceSlot); // re-derive after the allocation
    const int32_t* cells = src->items != nullptr ? Slots(src) : nullptr;
    int32_t* dst = reinterpret_cast<int32_t*>(snap->data);
    for (int32_t i = 0; i < count; ++i)
    {
        dst[i] = cells != nullptr ? cells[i] : 0;
    }
    return snap;
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
    // net48: a null source raises ArgumentNullException("collection") BEFORE
    // any other validation (the ctor body's own null guard).
    if (source == nullptr)
    {
        il2cpp_throw_argument_null_exception("collection");
    }
    // Root both the shell and the source across every allocation (either may
    // be compacted/moved).
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&source));

    // ICollection<int> (int32[] / List<int> / HashSet<int>): the initial
    // Capacity equals the source Count (net48; an empty source stays 0), then
    // the cells are copied. A SELF source cannot occur here (the shell is
    // fresh), so the source cells need no snapshot.
    const int32_t n = IntCollectionCount(source);
    if (n != kNotIntCollection)
    {
        Il2CppArray* items = AllocItems(n);
        Il2CppListI4* list = AsList(self); // re-derive after the allocation
        list->items = items;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(items));
        list->count = 0;
        if (n > 0)
        {
            InsertCollectionCells(&self, 0, &source, n, /*capacityReady=*/true);
        }
        return;
    }

    // Every other IEnumerable<int>: net48 starts with the default Capacity (0)
    // and appends element by element, so the growth is the ordinary 0 -> 4 ->
    // doubling walk (never the source's Count -- it has none).
    {
        Il2CppArray* items = AllocItems(0);
        Il2CppListI4* list = AsList(self);
        list->items = items;
        il2cpp_write_barrier(reinterpret_cast<void**>(&list->items),
                             reinterpret_cast<Il2CppObject*>(items));
        list->count = 0;
    }
    InsertEnumerableCells(&self, 0, source);
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
    // net48 AddRange == InsertRange(_size, collection): the null check runs
    // before anything else (ArgumentNullException("collection")), then the
    // whole source-shape matrix is normalized (arrays/lists bulk copy,
    // everything else enumerates through Add).
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("collection");
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&collection));
    const int32_t n = IntCollectionCount(collection);
    if (n != kNotIntCollection)
    {
        // ICollection<int>: bulk copy (an empty source changes nothing).
        // EnsureCapacity(_size + Count) is the net48 growth shape.
        InsertCollectionCells(&self, AsList(self)->count, &collection, n,
                              /*capacityReady=*/false);
        return;
    }
    // Any other IEnumerable<int>: net48 appends one element at a time (the
    // Add growth walk), keeping the already-appended prefix on an exception.
    InsertEnumerableCells(&self, AsList(self)->count, collection);
}

void il2cpp_list_i4_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection)
{
    ThrowIfNullReceiver(self);
    // net48 order: the null check FIRST, then the index bounds.
    if (collection == nullptr)
    {
        il2cpp_throw_argument_null_exception("collection");
    }
    Il2CppListI4* list = AsList(self);
    // net48 InsertRange raises ArgumentOutOfRange_Index (the ELEMENT-range
    // message), not the ArgumentOutOfRange_ListInsert text of Insert(int,T).
    if (index < 0 || index > list->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&collection));
    const int32_t n = IntCollectionCount(collection);
    if (n != kNotIntCollection)
    {
        if (collection == self)
        {
            // SELF reference: the growth below replaces the items array the
            // copy still has to read, so the cells are snapshotted first.
            Il2CppArray* snap = SnapshotListCells(&self, n);
            Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
            RootScope snapScope;
            snapScope.Add(reinterpret_cast<void**>(&snapObj));
            InsertCollectionCells(&self, index, &snapObj, n, /*capacityReady=*/false);
            return;
        }
        InsertCollectionCells(&self, index, &collection, n, /*capacityReady=*/false);
        return;
    }
    // Any other IEnumerable<int>: net48 inserts one element at a time at the
    // running index (the prefix survives a mid-enumeration exception).
    InsertEnumerableCells(&self, index, collection);
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
