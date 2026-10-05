#pragma once
//------------------------------------------------------------------------------
// stackqueue.h -- managed non-generic System.Collections.Stack / Queue
// runtime (icall domain), modeled on the il2cpp_hashtable.h conventions
// (same layout / GC-bitmap / reference-array rules).
//
// Layouts (aligned with the object-model convention; every field below is a
// GC-scanned slot in the class descriptor's refFieldBitmap):
//
//   struct Il2CppStack
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* items;    // reference array (Il2CppObject* elements):
//                              //   slot 0 = bottom, slot count-1 = top
//       int32_t      count;    // number of elements (NOT a reference)
//       int32_t      version;  // mutation counter (NOT a reference; D-STK-1)
//   };
//
//   struct Il2CppQueue
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* items;    // reference array (Il2CppObject* elements)
//       int32_t      head;     // ring index of the FRONT element
//       int32_t      count;    // number of elements (NOT a reference)
//       int32_t      version;  // mutation counter (NOT a reference; D-STK-1)
//   };
//
//   The queue's 'items' array is a RING BUFFER: element i sits at
//   items[(head + i) % items->length]. Growth re-layouts the ring so head
//   returns to 0 and the front-to-back order is preserved.
//
//   GC SAFETY (refFieldBitmap contract):
//     * Il2CppStack -- instanceSize 24, refFieldBitmap bit 1 (slot 1 = the
//       items array; the int32 count slot is NOT a reference). The collector
//       keeps the array alive whenever the stack is reachable.
//     * Il2CppQueue -- instanceSize 32, refFieldBitmap bit 1 (slot 1 = the
//       items array; head/count are NOT references).
//     * Both items arrays are REFERENCE arrays (il2cpp_array_new with
//       isRefElement == true): the collector scans every element as an
//       Il2CppObject*, so a rooted stack/queue keeps its whole contents
//       alive.
//
// .NET SEMANTICS BASELINE (matches System.Collections.Stack / Queue on .NET
// Framework 4.8):
//   * Stack: push/pop/peek, LIFO order. Pop/peek on an EMPTY stack ->
//     InvalidOperationException("Stack empty.").
//   * Queue: enqueue/dequeue/peek, FIFO order. Dequeue/peek on an EMPTY
//     queue -> InvalidOperationException("Queue empty.").
//   * Null elements are legal everywhere (push/enqueue store them; the
//     matching pop/dequeue returns null).
//   * pop/dequeue VACATE their slot (store null) before returning -- the
//     collector must never rescan a stale reference past the logical end of
//     the container (mirrors .NET's _array[_size] = null).
//   * Growth: when the container is full, the items array doubles (min 4,
//     cap 2^30). Clear() drops every element and resets count/head.
//
//   VERSION COUNTER (D-STK-1): every STRUCTURAL mutation -- push / pop /
//   clear (Stack), enqueue / dequeue / clear (Queue) -- increments the
//   container's 'version' (an int32 monotonic counter; wraps after 2^31
//   mutations, exactly like net48's internal _version). Read-only operations
//   (peek / get_Count / ToArray / GetEnumerator) never bump it. The snapshot
//   enumerators capture 'version' at GetEnumerator time and re-check it on
//   every MoveNext / Reset: a mismatch means the container changed under the
//   enumerator, and the icall throws InvalidOperationException("Collection was
//   modified; enumeration operation may not execute.") -- the net48
//   InvalidOperation_EnumFailedVersion semantics (see the enumerator section
//   below).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed non-generic stack (see header comment for layout + GC bitmap).
struct Il2CppStack
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* items;    // reference array: slot 0 = bottom, count-1 = top
    int32_t      count;    // number of elements
    int32_t      version;  // mutation counter (D-STK-1)
};

// Managed non-generic queue: ring buffer over 'items' (see header comment).
struct Il2CppQueue
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* items;    // reference array: ring storage
    int32_t      head;     // ring index of the FRONT element
    int32_t      count;    // number of elements
    int32_t      version;  // mutation counter (D-STK-1)
};

// The System.Collections.Stack / Queue runtime descriptors (registered
// lazily on first use; instanceSize + refFieldBitmap per the header
// comment). Create the managed objects through the *_create icalls below.
Il2CppClass* il2cpp_stack_class();
Il2CppClass* il2cpp_queue_class();

//------------------------------------------------------------------------------
// Stack (System.Collections.Stack).
//------------------------------------------------------------------------------

// Stack() -- allocate the managed stack with an empty 4-slot items array.
// Returns the new object (never null).
Il2CppObject* il2cpp_stack_create();

// Push(value) -- push onto the top. Null is legal. Grows (doubles the items
// array, min 4) when the stack is full.
void il2cpp_stack_push(Il2CppObject* self, Il2CppObject* value);

// Pop() -- the top element (LIFO). An empty stack raises
// InvalidOperationException("Stack empty."); the vacated slot is nulled.
Il2CppObject* il2cpp_stack_pop(Il2CppObject* self);

// Peek() -- the top element WITHOUT removing it. An empty stack raises
// InvalidOperationException("Stack empty.").
Il2CppObject* il2cpp_stack_peek(Il2CppObject* self);

// Count -- the number of elements.
int32_t il2cpp_stack_get_count(Il2CppObject* self);

// Clear() -- drop every element (all slots nulled). Count -> 0.
void il2cpp_stack_clear(Il2CppObject* self);

// ToArray() -- a new exact-length object[] with TOP-OF-STACK FIRST (the
// order successive Pop calls return; matches .NET Framework
// Stack.ToArray()). An empty stack yields an empty (length-0) array.
// The result is an independent copy; the stack is unchanged.
Il2CppObject* il2cpp_stack_to_array(Il2CppObject* self);

//------------------------------------------------------------------------------
// Queue (System.Collections.Queue).
//------------------------------------------------------------------------------

// Queue() -- allocate the managed queue with an empty 4-slot ring array.
// Returns the new object (never null).
Il2CppObject* il2cpp_queue_create();

// Enqueue(value) -- append at the back. Null is legal. Grows (doubles the
// ring array, min 4, re-laying the ring with head = 0) when the queue is
// full.
void il2cpp_queue_enqueue(Il2CppObject* self, Il2CppObject* value);

// Dequeue() -- the front element (FIFO). An empty queue raises
// InvalidOperationException("Queue empty."); the vacated slot is nulled.
Il2CppObject* il2cpp_queue_dequeue(Il2CppObject* self);

// Peek() -- the front element WITHOUT removing it. An empty queue raises
// InvalidOperationException("Queue empty.").
Il2CppObject* il2cpp_queue_peek(Il2CppObject* self);

// Count -- the number of elements.
int32_t il2cpp_queue_get_count(Il2CppObject* self);

// Clear() -- drop every element (all slots nulled). Count -> 0.
void il2cpp_queue_clear(Il2CppObject* self);

// ToArray() -- a new exact-length object[] with FRONT (head) FIRST (the
// order successive Dequeue calls return, walking the ring; matches .NET
// Framework Queue.ToArray()). An empty queue yields an empty (length-0)
// array. The result is an independent copy; the queue is unchanged.
Il2CppObject* il2cpp_queue_to_array(Il2CppObject* self);

//------------------------------------------------------------------------------
// G4: generic-domain membership (Queue<T>/Stack<T>.Contains). Equality over
// the element slots is Object.Equals value semantics for the AOT domains in
// use (the ArrayList.Contains convention): pointer identity, ordinal string
// content, boxed Int32/Int64/Double payload. The scan order matches the
// enumeration contract (Queue front-first, Stack top-first); membership is
// order-independent.
//------------------------------------------------------------------------------
bool il2cpp_queue_contains(Il2CppObject* self, Il2CppObject* item);
bool il2cpp_stack_contains(Il2CppObject* self, Il2CppObject* item);

//------------------------------------------------------------------------------
// Snapshot enumerator surface (C42 sqenum-b23 -- GetEnumerator / foreach;
// D-STK-1 -- version-checked like net48).
// The enumerator object (Il2CppStackQueueEnumerator) wraps the CONTAINER (a GC
// ref: its live 'version' is re-read on every MoveNext / Reset) plus the
// container's items array by reference with the count/head FROZEN at
// GetEnumerator time: it is a single allocation and its container + items
// fields are GC refs (bitmap bits 1 and 2), so both the frozen array and the
// container stay reachable even if the container later grows (growth allocates
// a NEW array; the old one survives through the items ref). The enumeration
// order matches ToArray: Stack TOP-of-stack first, Queue FRONT first
// (ring-aware). foreach semantics:
//   create  -> container + version snapshotted, items/count (and head for the
//              queue) snapshotted, index = -1
//   MoveNext-> VERSION CHECK first: if container->version != snapshot version
//              throw InvalidOperationException("Collection was modified;
//              enumeration operation may not execute.") -- net48 semantics
//              (D-STK-1). Then ++index; returns 1 while index < count, else 0
//   get_Current -> the element at the cursor (caller reads only after a
//                  successful MoveNext)
//   Reset   -> VERSION CHECK first (net48 throws here too), then index = -1
//   Dispose -> no-op (the enumerator is not IDisposable; the foreach finally's
//              isinst guard never reaches it -- the IDisposable::Dispose arm
//              in MethodRefResolver.cs maps the call to the shared no-op).
//
//   IEnumerator::MoveNext / get_Current / Reset on a Stack-Enumerator or
//   Queue-Enumerator receiver dispatch to the matching icalls below
//   (MethodMap.Collections.cs).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_stack_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_queue_get_enumerator(Il2CppObject* self);
int32_t il2cpp_stack_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_stack_enumerator_get_current(Il2CppObject* self);
void il2cpp_stack_enumerator_reset(Il2CppObject* self);
int32_t il2cpp_queue_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_queue_enumerator_get_current(Il2CppObject* self);
void il2cpp_queue_enumerator_reset(Il2CppObject* self);

//------------------------------------------------------------------------------
// Snapshot enumerator layout (C42 sqenum-b23 + D-STK-1): header + the
// CONTAINER reference (a GC ref, bitmap bit 1 -- its live 'version' is
// re-read on every MoveNext / Reset) + the frozen items array (a GC ref,
// bitmap bit 2) + the version snapshot + the frozen ring head / count + the
// cursor. version/head/count/index are NOT references. The SAME struct backs
// both the Stack and the Queue enumerator descriptors (the queue uses 'head',
// the stack leaves it 0).
//------------------------------------------------------------------------------
struct Il2CppStackQueueEnumerator
{
    Il2CppObject obj;        // object header (klass pointer)
    Il2CppObject* container; // the container at snapshot time (GC ref; bit 1)
    Il2CppArray* items;      // the container's items array at snapshot time
    int32_t      version;    // container version at snapshot time
    int32_t      head;       // queue: ring head at snapshot time (stack: unused)
    int32_t      count;      // element count at snapshot time
    int32_t      index;      // cursor: -1 = before the first element
};

} // namespace il2cpp

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptors -- the transpiler emits
// &System_Collections_Stack_class / &System_Collections_Queue_class for
// isinst/castclass (the same contract as System_WeakReference_class in
// il2cpp_gc_ext.h / System_Version_class in versionuri.cpp). Defined in
// stackqueue.cpp at GLOBAL scope with external linkage; registered lazily
// through the *_class() accessors, which return their addresses.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Collections_Stack_class;
extern il2cpp::Il2CppClass System_Collections_Queue_class;

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptors for the snapshot enumerators (C42
// sqenum-b23 + D-STK-1). Same contract as the container descriptors above:
// defined at global scope in stackqueue.cpp with external linkage, registered
// lazily alongside the container in Ensure*Registered. Both describe the shared
// Il2CppStackQueueEnumerator layout (bitmap bits 1+2 = the container ref + the
// frozen items array).
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Collections_Stack_Enumerator_class;
extern il2cpp::Il2CppClass System_Collections_Queue_Enumerator_class;
