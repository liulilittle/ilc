//------------------------------------------------------------------------------
// stackqueue.cpp -- non-generic System.Collections.Stack / Queue runtime (see
// stackqueue.h for the layout, GC bitmap and growth contracts).
//
// Data structures:
//   Il2CppStack { items: Il2CppArray* (reference array), count }
//     LIFO over a plain dynamic array: slot 0 = bottom, count-1 = top. The
//     array is a REFERENCE array, so the collector scans every element; a
//     rooted stack therefore keeps its whole contents alive.
//   Il2CppQueue { items: Il2CppArray* (reference array), head, count }
//     FIFO over a ring buffer: element i sits at items[(head + i) % length].
//     Enqueue appends at the back, dequeue reads the front and advances head;
//     the ring wraps with a simple modulo, so no element ever needs shifting.
//
// GC-safety notes for the code below (same conventions as hashtable.cpp):
//   * All reference stores go through il2cpp_write_barrier first (old ->
//     young tracking).
//   * Growth allocates ONE new array, then performs pure pointer writes (no
//     further allocation), so no collection can run between the array's
//     creation and its publication into items.
//   * pop/dequeue null the vacated slot (with a write barrier) so the
//     collector never rescans a stale reference past the logical end.
//   * Empty pop/peek/dequeue raise InvalidOperationException through the
//     il2cpp_exception_ext.h factories (chains carry type + base classes,
//     so generated catch clauses follow .NET inheritance semantics).
//------------------------------------------------------------------------------

#include "stackqueue.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "string_compare.h" // il2cpp_string_equals_ordinal
#include "il2cpp_value_semantics.h" // runtime::icall_object_equals (Contains fallback)

#include <cstddef>
#include <cstdint>
#include <cstring>

//------------------------------------------------------------------------------
// The System.Collections.Stack / System.Collections.Queue runtime descriptors.
// GLOBAL scope (external linkage, OUTSIDE namespace il2cpp): the transpiler
// emits &System_Collections_Stack_class / &System_Collections_Queue_class for
// isinst/castclass (the same contract as System_WeakReference_class in
// gcext.cpp / System_Version_class in versionuri.cpp), while newobj routes
// through the *_class() accessors (FIX-3). Both point at these same
// descriptors, so isinst identity and newobj allocation never diverge.
// Registered lazily (constraint #12: no global-constructor allocation).
//   Stack: instanceSize 24 (obj 8 + items 8 + count 4 + version 4),
//     refFieldBitmap bit 1: 'items' is a GC ref (count/version are not).
//   Queue: instanceSize 32 (obj 8 + items 8 + head 4 + count 4 + version 4,
//     padded to alignment), refFieldBitmap bit 1: 'items' is a GC ref.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Collections_Stack_class = {
    "System.Collections.Stack", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStack)), // instanceSize
    0b00010u,                   // refFieldBitmap: bit 1 = items
    1,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

il2cpp::Il2CppClass System_Collections_Queue_class = {
    "System.Collections.Queue", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppQueue)), // instanceSize
    0b00010u,                   // refFieldBitmap: bit 1 = items
    1,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

//------------------------------------------------------------------------------
// The snapshot enumerator runtime descriptors (C42 sqenum-b23 + D-STK-1).
// GLOBAL scope (external linkage, OUTSIDE namespace il2cpp) like the container
// descriptors: the enumerator is a managed object (Il2CppStackQueueEnumerator)
// allocated through il2cpp_object_new by the get_enumerator icalls.
// instanceSize sizeof(Il2CppStackQueueEnumerator) (obj 8 + container 8 + items
// 8 + version/head/count/index 16 = 40), refFieldBitmap bits 1+2: 'container'
// and 'items' are GC refs (the live container whose version MoveNext re-checks,
// and the frozen array the foreach iterates, must both stay reachable). One
// descriptor per container kind (klass name matches the .NET nested
// Enumerator type names). Registered lazily alongside the container
// descriptors.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Collections_Stack_Enumerator_class = {
    "System.Collections.Stack.Enumerator", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStackQueueEnumerator)), // instanceSize
    0b00110u,                   // refFieldBitmap: bits 1+2 = container + items
    2,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

il2cpp::Il2CppClass System_Collections_Queue_Enumerator_class = {
    "System.Collections.Queue.Enumerator", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStackQueueEnumerator)), // instanceSize
    0b00110u,                   // refFieldBitmap: bits 1+2 = container + items
    2,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Layout verification: the class descriptors' refFieldBitmap bits below are
// literal constants; these compile-time checks pin them to the struct
// layout, so a layout change with a stale bitmap cannot link silently.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppStack, items) == 1 * sizeof(void*), "Il2CppStack: items must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppStack, count) == 2 * sizeof(void*), "Il2CppStack: count sits at slot 2 (NOT in the bitmap)");
static_assert(offsetof(Il2CppStack, version) == 2 * sizeof(void*) + sizeof(int32_t), "Il2CppStack: version follows count (NOT in the bitmap)");
static_assert(offsetof(Il2CppQueue, items) == 1 * sizeof(void*), "Il2CppQueue: items must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppQueue, head) == 2 * sizeof(void*), "Il2CppQueue: head sits at slot 2 (NOT in the bitmap)");
// Both int32 fields (head, count) occupy offsets 16..23 -- the low half of
// slot 2 only; the version counter sits at offset 24 (inside slot 3, which is
// NOT in the bitmap), so the bitmap's single bit 1 (items) is the only
// reference slot.
static_assert(offsetof(Il2CppQueue, count) == 2 * sizeof(void*) + sizeof(int32_t), "Il2CppQueue: count follows head inside slot 2");
static_assert(offsetof(Il2CppQueue, version) == 2 * sizeof(void*) + 2 * sizeof(int32_t), "Il2CppQueue: version follows count (NOT in the bitmap)");
// C42 sqenum-b23 + D-STK-1: the shared enumerator layout -- container must sit
// at slot 1 (bitmap bit 1) and items at slot 2 (bitmap bit 2); version/head/
// count/index are ints (NOT in the bitmap).
static_assert(offsetof(Il2CppStackQueueEnumerator, container) == 1 * sizeof(void*), "Il2CppStackQueueEnumerator: container must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppStackQueueEnumerator, items) == 2 * sizeof(void*), "Il2CppStackQueueEnumerator: items must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppStackQueueEnumerator, version) == 2 * sizeof(void*) + 2 * sizeof(int32_t), "Il2CppStackQueueEnumerator: version follows items");
static_assert(offsetof(Il2CppStackQueueEnumerator, head) == 2 * sizeof(void*) + 3 * sizeof(int32_t), "Il2CppStackQueueEnumerator: head follows version");
static_assert(offsetof(Il2CppStackQueueEnumerator, count) == 2 * sizeof(void*) + 4 * sizeof(int32_t), "Il2CppStackQueueEnumerator: count follows head");
static_assert(offsetof(Il2CppStackQueueEnumerator, index) == 2 * sizeof(void*) + 5 * sizeof(int32_t), "Il2CppStackQueueEnumerator: index follows count");

// Minimum items-array length: avoids resizing on the very first push (and
// matches the .NET Framework's first grow target of 4).
constexpr int32_t kMinItems = 4;

// Doubling cap: keeps the doubling loop inside int32 (no overflow) and the
// index arithmetic well below 2^31 (the ring modulo stays exact).
constexpr int32_t kMaxItems = 1 << 30;

bool g_stackRegistered = false;
bool g_queueRegistered = false;

void EnsureStackRegistered()
{
    if (g_stackRegistered) return;
    il2cpp_class_register(&System_Collections_Stack_class);
    // C42 sqenum-b23: register the Stack snapshot-enumerator descriptor (GC
    // must scan its frozen items field).
    il2cpp_class_register(&System_Collections_Stack_Enumerator_class);
    g_stackRegistered = true;
}

void EnsureQueueRegistered()
{
    if (g_queueRegistered) return;
    il2cpp_class_register(&System_Collections_Queue_class);
    // C42 sqenum-b23: register the Queue snapshot-enumerator descriptor (GC
    // must scan its frozen items field).
    il2cpp_class_register(&System_Collections_Queue_Enumerator_class);
    g_queueRegistered = true;
}

//------------------------------------------------------------------------------
// Small helpers.
//------------------------------------------------------------------------------

Il2CppStack* AsStack(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppStack*>(self);
}

Il2CppQueue* AsQueue(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppQueue*>(self);
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

//------------------------------------------------------------------------------
// GcRoot (RAII rooting of a raw icall parameter across an allocation) lives
// in the shared header il2cpp_gcroot.h -- see there for the WHY and the
// frame-refs protocol. In this file it is used by push/enqueue (ensure +
// grow), to_array and get_enumerator, plus the two create icalls (whose
// freshly allocated object is only reachable through the icall's own local
// until it is returned).
//
// The defensive ensure pattern (create-skipped container with a null items
// array) is INLINED at each call site -- never factored into a helper that
// takes the container BY VALUE: the allocation inside can move the
// container, and a by-value helper would then write the moved-out old
// address (the store is silently lost, stackqueue2 e2e, 2026-08-13).
//------------------------------------------------------------------------------

// Ring index of the element 'offset' positions behind the front: element i
// (0-based from the front) sits at (head + i) % length. 'length' is always
// >= kMinItems, so the modulo is exact.
int32_t RingIndex(int32_t head, int32_t offset, int32_t length)
{
    return (head + offset) % length;
}

// Double the items-array length, capped at kMaxItems. At the cap the array
// is rebuilt at the same length (an insert at count == 2^30 is the
// documented physical limit -- an 8 GiB pointer array -- and cannot occur in
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
// that allocation runs before the array exists); the copy loop below is
// pure pointer reads/writes with no allocation, so no collection can run
// between the array's birth and its publication into 'items'. The old array
// is still reachable through the container while the loop reads it.
//------------------------------------------------------------------------------

void GrowStack(Il2CppStack* st)
{
    // AllocItems below can trigger a compacting collection, which MOVES 'st'
    // (a Gen0 survivor). 'st' is a raw parameter -- root its slot for the
    // allocation and re-read the (possibly moved) address afterwards; the old
    // items array is re-read from the rooted copy too (Phase C rewrote it).
    GcRoot<Il2CppStack*> root(st, "icall_stack_grow");
    const int32_t oldLength = st->items->length;
    const int32_t newLength = NextLength(oldLength);
    if (newLength != oldLength)
    {
        Il2CppArray* newItems = AllocItems(newLength);
        st = root.Get();
        Il2CppObject** newSlots = reinterpret_cast<Il2CppObject**>(newItems->data);
        Il2CppObject* const* oldSlots = reinterpret_cast<Il2CppObject* const*>(st->items->data);
        for (int32_t i = 0; i < oldLength; ++i)
        {
            newSlots[i] = oldSlots[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&newSlots[i]), newSlots[i]);
        }
        il2cpp_write_barrier(reinterpret_cast<void**>(&st->items),
                             reinterpret_cast<Il2CppObject*>(newItems));
        st->items = newItems;
    }
}

void GrowQueue(Il2CppQueue* q)
{
    // Same rooting discipline as GrowStack: the growth allocation can trigger
    // a compacting collection that moves 'q'; re-read it from the rooted
    // slot afterwards.
    GcRoot<Il2CppQueue*> root(q, "icall_queue_grow");
    const int32_t oldLength = q->items->length;
    const int32_t newLength = NextLength(oldLength);
    if (newLength != oldLength)
    {
        Il2CppArray* newItems = AllocItems(newLength);
        q = root.Get();
        Il2CppObject** newSlots = reinterpret_cast<Il2CppObject**>(newItems->data);
        Il2CppObject* const* oldSlots = reinterpret_cast<Il2CppObject* const*>(q->items->data);
        // Re-layout the ring: element i (from the front) moves to slot i, so
        // head resets to 0 and the front-to-back order is preserved.
        for (int32_t i = 0; i < q->count; ++i)
        {
            newSlots[i] = oldSlots[RingIndex(q->head, i, oldLength)];
            il2cpp_write_barrier(reinterpret_cast<void**>(&newSlots[i]), newSlots[i]);
        }
        il2cpp_write_barrier(reinterpret_cast<void**>(&q->items),
                             reinterpret_cast<Il2CppObject*>(newItems));
        q->items = newItems;
        q->head = 0; // pure int store; no allocation between publish and this
    }
}

//------------------------------------------------------------------------------
// Shared empty checks: pop/peek/dequeue raise InvalidOperationException with
// the exact .NET Framework 4.8 messages.
//------------------------------------------------------------------------------

void ThrowIfStackEmpty(int32_t count)
{
    if (count <= 0)
    {
        il2cpp_throw_invalid_operation_exception("Stack empty.");
    }
}

void ThrowIfQueueEmpty(int32_t count)
{
    if (count <= 0)
    {
        il2cpp_throw_invalid_operation_exception("Queue empty.");
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_stack_class / il2cpp_queue_class -- the managed descriptors
// (lazily registered on first use).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_stack_class()
{
    EnsureStackRegistered();
    return &System_Collections_Stack_class;
}

Il2CppClass* il2cpp_queue_class()
{
    EnsureQueueRegistered();
    return &System_Collections_Queue_class;
}

//------------------------------------------------------------------------------
// Stack icalls.
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_stack_create()
{
    EnsureStackRegistered();
    Il2CppStack* st = reinterpret_cast<Il2CppStack*>(il2cpp_object_new(&System_Collections_Stack_class));
    // AllocItems below may compact and MOVE the fresh object (only reachable
    // through this icall's local until it is returned): root the local and
    // re-read it before publishing 'items', so the returned address is the
    // post-compaction one the caller's frame will be rewritten to.
    GcRoot<Il2CppStack*> root(st, "icall_stack_create");
    Il2CppArray* arr = AllocItems(kMinItems);
    st = root.Get();
    st->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&st->items),
                         reinterpret_cast<Il2CppObject*>(st->items));
    st->count = 0;
    st->version = 0;
    return reinterpret_cast<Il2CppObject*>(st);
}

void il2cpp_stack_push(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppStack* st = AsStack(self);
    if (st->items == nullptr)
    {
        // Defensive: a create-skipped stack has a null items array. AllocItems
        // below may trigger a compacting collection that MOVES 'self': root
        // its slot, then re-read the moved address BEFORE publishing 'items'
        // (writing through the stale address would lose the store -- the
        // caller's slot was rewritten to the moved copy).
        GcRoot<Il2CppObject*> root(self, "icall_stack_push_ensure");
        Il2CppArray* arr = AllocItems(kMinItems);
        self = root.Get();
        st = AsStack(self);
        st->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&st->items),
                             reinterpret_cast<Il2CppObject*>(st->items));
    }
    if (st->count == st->items->length)
    {
        // GrowStack allocates and may compact: same rooting discipline.
        GcRoot<Il2CppObject*> root(self, "icall_stack_push_grow");
        GrowStack(AsStack(self));
        self = root.Get();
        st = AsStack(self);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(st->items->data);
    slots[st->count] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[st->count]), value);
    ++st->count;
    ++st->version; // D-STK-1: structural mutation (net48 Push bumps _version)
}

Il2CppObject* il2cpp_stack_pop(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStack* st = AsStack(self);
    ThrowIfStackEmpty(st->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(st->items->data);
    --st->count;
    Il2CppObject* result = slots[st->count];
    slots[st->count] = nullptr; // vacate: the collector must never rescan it
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[st->count]), nullptr);
    ++st->version; // D-STK-1: structural mutation (net48 Pop bumps _version)
    return result;
}

Il2CppObject* il2cpp_stack_peek(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStack* st = AsStack(self);
    ThrowIfStackEmpty(st->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(st->items->data);
    return slots[st->count - 1];
}

int32_t il2cpp_stack_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsStack(self)->count;
}

void il2cpp_stack_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStack* st = AsStack(self);
    if (st->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(st->items->data);
        const int32_t itemCount = st->items->length;
        for (int32_t i = 0; i < itemCount; ++i)
        {
            if (slots[i] != nullptr)
            {
                slots[i] = nullptr;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
            }
        }
    }
    st->count = 0;
    ++st->version; // D-STK-1: structural mutation (net48 Clear bumps _version)
}

Il2CppObject* il2cpp_stack_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // AllocItems can trigger a compacting collection: root 'self' and re-read
    // it afterwards (the items array and count are read from the rooted copy).
    GcRoot<Il2CppObject*> root(self, "icall_stack_to_array");
    Il2CppStack* st = AsStack(self);
    // Exact-length reference array; an empty stack yields an empty (length-0)
    // array -- matching .NET Framework Stack.ToArray().
    Il2CppArray* result = AllocItems(st->count);
    self = root.Get();
    st = AsStack(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    if (st->items != nullptr && st->count > 0)
    {
        // .NET Framework Stack.ToArray(): copies top-of-stack FIRST (the same
        // order successive Pop calls return), so result[0] is the top element.
        Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(st->items->data);
        for (int32_t i = 0; i < st->count; ++i)
        {
            dst[i] = slots[st->count - 1 - i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// Queue icalls.
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_queue_create()
{
    EnsureQueueRegistered();
    Il2CppQueue* q = reinterpret_cast<Il2CppQueue*>(il2cpp_object_new(&System_Collections_Queue_class));
    // Same re-read-before-publish discipline as the Stack create (see there).
    GcRoot<Il2CppQueue*> root(q, "icall_queue_create");
    Il2CppArray* arr = AllocItems(kMinItems);
    q = root.Get();
    q->items = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&q->items),
                         reinterpret_cast<Il2CppObject*>(q->items));
    q->head = 0;
    q->count = 0;
    q->version = 0;
    return reinterpret_cast<Il2CppObject*>(q);
}

void il2cpp_queue_enqueue(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppQueue* q = AsQueue(self);
    if (q->items == nullptr)
    {
        // Defensive: a create-skipped queue has a null items array. Same
        // re-read-before-publish discipline as the Stack push (see there).
        GcRoot<Il2CppObject*> root(self, "icall_queue_enqueue_ensure");
        Il2CppArray* arr = AllocItems(kMinItems);
        self = root.Get();
        q = AsQueue(self);
        q->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&q->items),
                             reinterpret_cast<Il2CppObject*>(q->items));
    }
    if (q->count == q->items->length)
    {
        // GrowQueue allocates and may compact: root 'self' (see push).
        GcRoot<Il2CppObject*> root(self, "icall_queue_enqueue_grow");
        GrowQueue(AsQueue(self));
        self = root.Get();
        q = AsQueue(self);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(q->items->data);
    const int32_t index = RingIndex(q->head, q->count, q->items->length);
    slots[index] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), value);
    ++q->count;
    ++q->version; // D-STK-1: structural mutation (net48 Enqueue bumps _version)
}

Il2CppObject* il2cpp_queue_dequeue(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppQueue* q = AsQueue(self);
    ThrowIfQueueEmpty(q->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(q->items->data);
    Il2CppObject* result = slots[q->head];
    slots[q->head] = nullptr; // vacate: the collector must never rescan it
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[q->head]), nullptr);
    q->head = RingIndex(q->head, 1, q->items->length);
    --q->count;
    ++q->version; // D-STK-1: structural mutation (net48 Dequeue bumps _version)
    return result;
}

Il2CppObject* il2cpp_queue_peek(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppQueue* q = AsQueue(self);
    ThrowIfQueueEmpty(q->count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(q->items->data);
    return slots[q->head];
}

int32_t il2cpp_queue_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsQueue(self)->count;
}

void il2cpp_queue_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppQueue* q = AsQueue(self);
    if (q->items != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(q->items->data);
        const int32_t itemCount = q->items->length;
        for (int32_t i = 0; i < itemCount; ++i)
        {
            if (slots[i] != nullptr)
            {
                slots[i] = nullptr;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
            }
        }
    }
    q->head = 0;
    q->count = 0;
    ++q->version; // D-STK-1: structural mutation (net48 Clear bumps _version)
}

Il2CppObject* il2cpp_queue_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // Same rooting discipline as the Stack overload (allocation may compact).
    GcRoot<Il2CppObject*> root(self, "icall_queue_to_array");
    Il2CppQueue* q = AsQueue(self);
    // Exact-length reference array; an empty queue yields an empty (length-0)
    // array -- matching .NET Framework Queue.ToArray().
    Il2CppArray* result = AllocItems(q->count);
    self = root.Get();
    q = AsQueue(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    if (q->items != nullptr && q->count > 0)
    {
        // .NET Framework Queue.ToArray(): copies FRONT (head) first -- the
        // same order successive Dequeue calls return -- walking the ring.
        const int32_t length = q->items->length;
        Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(q->items->data);
        for (int32_t i = 0; i < q->count; ++i)
        {
            dst[i] = slots[RingIndex(q->head, i, length)];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// C42 sqenum-b23 + D-STK-1 -- version-checked snapshot enumerator icalls.
// Stack / Queue GetEnumerator (IEnumerable) route here; the IEnumerator::
// MoveNext / get_Current / Reset interface members dispatch to the matching
// icalls by receiver pseudo-type (MethodMap.Collections.cs). The enumerator
// wraps the CONTAINER (a GC ref, bitmap bit 1) plus the container's items
// array by reference (a GC ref, bitmap bit 2) and the count/head/version
// FROZEN at GetEnumerator time:
//   create  -> container + items/count (and head for the queue) + version
//              snapshotted, index = -1
//   MoveNext-> VERSION CHECK (D-STK-1): if container->version != snapshot
//              version, throw InvalidOperationException("Collection was
//              modified; enumeration operation may not execute.") -- the net48
//              InvalidOperation_EnumFailedVersion behavior. Then ++index;
//              returns 1 while index < count, else 0
//   get_Current -> the element at the cursor (caller reads only after a
//                  successful MoveNext)
//   Reset   -> VERSION CHECK first (net48 throws here too), then index = -1
//   Dispose -> no-op (the IDisposable::Dispose arm in MethodRefResolver.cs
//              maps the foreach-finally call to the shared no-op; the
//              enumerator is not IDisposable, so the isinst guard never
//              reaches it at runtime -- no icall is needed here).
//
// GC SAFETY: the enumerator is a SINGLE allocation. Its container and items
// fields are GC refs (write barriers + descriptor bitmap bits 1+2), so the
// container (whose live version MoveNext re-reads) and the frozen array both
// stay reachable even if the container later grows (growth allocates a NEW
// array; the old one survives through the items reference).
//
// ENUMERATION ORDER matches ToArray (net48): Stack iterates TOP-of-stack
// first (slots[count-1-i]), Queue iterates FRONT (head) first, ring-aware
// (slots[RingIndex(head, i, items->length)]).
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_stack_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureStackRegistered();
    // Two allocations below (the defensive items array + the enumerator
    // object) can each trigger a compacting collection: root 'self' for the
    // whole body and re-read it (and 'st' derived from it) after each
    // allocation.
    GcRoot<Il2CppObject*> root(self, "icall_stack_get_enumerator");
    Il2CppStack* st = AsStack(self);
    if (st->items == nullptr)
    {
        // Defensive: a create-skipped stack has a null items array. Allocate
        // and re-read the rooted 'self' before publishing (see push).
        Il2CppArray* arr = AllocItems(kMinItems);
        self = root.Get();
        st = AsStack(self);
        st->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&st->items),
                             reinterpret_cast<Il2CppObject*>(st->items));
    }
    self = root.Get();
    st = AsStack(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(
        il2cpp_object_new(&System_Collections_Stack_Enumerator_class));
    self = root.Get();
    st = AsStack(self);
    en->container = self;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->container),
                         reinterpret_cast<Il2CppObject*>(en->container));
    en->items = st->items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->items),
                         reinterpret_cast<Il2CppObject*>(en->items));
    en->version = st->version;
    en->head = 0;
    en->count = st->count;
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

Il2CppObject* il2cpp_queue_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureQueueRegistered();
    // Same rooting discipline as the Stack overload (two allocation points).
    GcRoot<Il2CppObject*> root(self, "icall_queue_get_enumerator");
    Il2CppQueue* q = AsQueue(self);
    if (q->items == nullptr)
    {
        // Defensive: a create-skipped queue has a null items array. Allocate
        // and re-read the rooted 'self' before publishing (see enqueue).
        Il2CppArray* arr = AllocItems(kMinItems);
        self = root.Get();
        q = AsQueue(self);
        q->items = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&q->items),
                             reinterpret_cast<Il2CppObject*>(q->items));
    }
    self = root.Get();
    q = AsQueue(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(
        il2cpp_object_new(&System_Collections_Queue_Enumerator_class));
    self = root.Get();
    q = AsQueue(self);
    en->container = self;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->container),
                         reinterpret_cast<Il2CppObject*>(en->container));
    en->items = q->items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->items),
                         reinterpret_cast<Il2CppObject*>(en->items));
    en->version = q->version;
    en->head = q->head;
    en->count = q->count;
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

// D-STK-1: the net48 InvalidOperation_EnumFailedVersion message. The container
// 'version' bumped on every structural mutation; a snapshot enumerator whose
// captured version no longer matches must refuse to advance.
constexpr const char* kEnumFailedVersionMessage =
    "Collection was modified; enumeration operation may not execute.";

int32_t il2cpp_stack_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    if (AsStack(en->container)->version != en->version)
    {
        il2cpp_throw_invalid_operation_exception(kEnumFailedVersionMessage);
    }
    ++en->index;
    return en->index < en->count ? 1 : 0;
}

Il2CppObject* il2cpp_stack_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(en->items->data);
    return slots[en->count - 1 - en->index];
}

void il2cpp_stack_enumerator_reset(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    if (AsStack(en->container)->version != en->version)
    {
        il2cpp_throw_invalid_operation_exception(kEnumFailedVersionMessage);
    }
    en->index = -1;
}

int32_t il2cpp_queue_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    if (AsQueue(en->container)->version != en->version)
    {
        il2cpp_throw_invalid_operation_exception(kEnumFailedVersionMessage);
    }
    ++en->index;
    return en->index < en->count ? 1 : 0;
}

Il2CppObject* il2cpp_queue_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(en->items->data);
    return slots[RingIndex(en->head, en->index, en->items->length)];
}

void il2cpp_queue_enumerator_reset(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStackQueueEnumerator* en = reinterpret_cast<Il2CppStackQueueEnumerator*>(self);
    if (AsQueue(en->container)->version != en->version)
    {
        il2cpp_throw_invalid_operation_exception(kEnumFailedVersionMessage);
    }
    en->index = -1;
}

//------------------------------------------------------------------------------
// G4: generic-domain membership -- Object.Equals value semantics over the
// element slots (pointer, ordinal string content, boxed Int32/Int64/Double
// payload; the ArrayList.Contains convention). The boxed payload sits at
// offset 8 (header + klass), the runtime-wide kBoxPayloadOffset. Every
// other shape (the remaining primitive boxes, user types) falls back to
// runtime::icall_object_equals -- the runtime-wide equality choke point.
//------------------------------------------------------------------------------
static bool SeqElementEquals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return true;
    if (a == nullptr || b == nullptr) return false;
    const Il2CppClass* ka = a->klass;
    const Il2CppClass* kb = b->klass;
    if (ka == nullptr || kb == nullptr || ka->name == nullptr || kb->name == nullptr) return false;
    if (std::strcmp(ka->name, "System.String") == 0 && std::strcmp(kb->name, "System.String") == 0)
        return il2cpp_string_equals_ordinal(reinterpret_cast<const Il2CppString*>(a),
                                            reinterpret_cast<const Il2CppString*>(b));
    constexpr int32_t kPayload = 8;
    if (std::strcmp(ka->name, "System.Int32") == 0 && std::strcmp(kb->name, "System.Int32") == 0)
        return *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(a) + kPayload)
            == *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(b) + kPayload);
    if (std::strcmp(ka->name, "System.Int64") == 0 && std::strcmp(kb->name, "System.Int64") == 0)
        return *reinterpret_cast<const int64_t*>(reinterpret_cast<const char*>(a) + kPayload)
            == *reinterpret_cast<const int64_t*>(reinterpret_cast<const char*>(b) + kPayload);
    if (std::strcmp(ka->name, "System.Double") == 0 && std::strcmp(kb->name, "System.Double") == 0)
        return *reinterpret_cast<const double*>(reinterpret_cast<const char*>(a) + kPayload)
            == *reinterpret_cast<const double*>(reinterpret_cast<const char*>(b) + kPayload);
    // G4 B8 (b2b8): every other element shape (the remaining boxed primitive
    // klasses, two boxes of a user type; null was handled above) rides the
    // runtime-wide Object.Equals choke point -- BoxedValueEquals covers all
    // primitive boxes, and the user virtual-Equals fallback lands with
    // PATCH-object-equals (P1.6).
    return runtime::icall_object_equals(a, b);
}

bool il2cpp_stack_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    Il2CppStack* st = AsStack(self);
    if (st->items == nullptr || st->count <= 0) return false;
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(st->items->data);
    for (int32_t i = 0; i < st->count; ++i)
    {
        if (SeqElementEquals(slots[i], item)) return true;
    }
    return false;
}

bool il2cpp_queue_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    Il2CppQueue* q = AsQueue(self);
    if (q->items == nullptr || q->count <= 0) return false;
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(q->items->data);
    const int32_t length = q->items->length;
    for (int32_t i = 0; i < q->count; ++i)
    {
        if (SeqElementEquals(slots[RingIndex(q->head, i, length)], item)) return true;
    }
    return false;
}

} // namespace il2cpp
