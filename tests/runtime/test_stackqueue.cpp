//------------------------------------------------------------------------------
// test_stackqueue.cpp -- System.Collections.Stack / Queue runtime unit tests
// (stackqueue.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. create: count starts at 0; pop/peek on an empty stack and
//      dequeue/peek on an empty queue raise InvalidOperationException.
//   2. Stack LIFO order: push 1,2,3 -> pop 3,2,1.
//   3. Stack null element: push(nullptr) -> pop returns nullptr.
//   4. Stack growth: the items array doubles exactly when the stack fills
//      (4 -> 8 -> 16); a 10-push stack pops back in exact reverse order.
//   5. Stack clear: count -> 0 and the stack is reusable afterwards (a
//      subsequent pop on the cleared stack raises).
//   6. Queue FIFO order: enqueue 1,2,3 -> dequeue 1,2,3.
//   7. Queue ring wrap: with the 4-slot ring, dequeue from the front,
//      re-enqueue, and verify the front-to-back order survives the wrap.
//   8. Queue growth with re-layout: growing a wrapped queue re-lays the ring
//      with head = 0 while preserving the front-to-back order.
//   9. Queue null element: enqueue(nullptr) -> dequeue returns nullptr.
//  10. Queue clear: count -> 0 and the queue is reusable afterwards.
//  11. Stack to_array: top-of-stack FIRST (net48 Stack.ToArray order).
//  12. Stack to_array: empty stack -> empty (length-0) array.
//  13. Queue to_array: front (head) FIRST across a ring wrap (net48
//      Queue.ToArray order).
//  14. Queue to_array: empty queue -> empty (length-0) array.
//  15. Enumerator VERSION CHECK (D-STK-1): every structural mutation
//      (push/pop/clear, enqueue/dequeue/clear) bumps the container version;
//      a snapshot enumerator whose captured version no longer matches throws
//      InvalidOperationException on MoveNext / Reset -- the net48
//      InvalidOperation_EnumFailedVersion semantics. Read-only operations
//      (peek / get_Count / ToArray) never bump the version, so a stable
//      container re-enumerates freely.
//
// Plain executable + minimal assertion harness (see test_hashtable.cpp).
//------------------------------------------------------------------------------

#include "stackqueue.h"

#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "stackqueue test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// The exception-factory chain (see il2cpp_exception_ext.h): the type_name()
// of a caught DotNetException is the whole chain as thrown.
const char* kInvalidOperationChain =
    "System.InvalidOperationException,System.InvalidOperationException,System.SystemException,System.Exception";

// Raise-and-catch helper: run 'fn', capture the managed exception carrier.
// Returns true when a DotNetException with the expected chain was thrown.
template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedChain)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
}

il2cpp::Il2CppObject* Str(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Move-safe content check: does 'obj' hold a managed string whose UTF-16
// units equal the ASCII text? Content equality stays valid across a
// compacting collection (local pointers go stale; contents do not).
bool IsContent(il2cpp::Il2CppObject* obj, const char* ascii)
{
    if (obj == nullptr || obj->klass == nullptr ||
        std::strcmp(obj->klass->name, "System.String") != 0)
    {
        return false;
    }
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. create: count 0; empty-container pops/peeks/dequeues raise
    //    InvalidOperationException ("Stack empty." / "Queue empty.") ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        Expect(il2cpp::il2cpp_stack_get_count(st) == 0, "fresh stack: count 0");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 0, "fresh queue: count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_pop(st); }, kInvalidOperationChain),
               "pop on an empty stack raises InvalidOperationException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_peek(st); }, kInvalidOperationChain),
               "peek on an empty stack raises InvalidOperationException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_dequeue(q); }, kInvalidOperationChain),
               "dequeue on an empty queue raises InvalidOperationException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_peek(q); }, kInvalidOperationChain),
               "peek on an empty queue raises InvalidOperationException");
    }

    // --- 2. Stack LIFO order: push 1,2,3 -> pop 3,2,1 ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st, Str("one"));
        il2cpp::il2cpp_stack_push(st, Str("two"));
        il2cpp::il2cpp_stack_push(st, Str("three"));
        Expect(il2cpp::il2cpp_stack_get_count(st) == 3, "three pushes -> count 3");
        Expect(IsContent(il2cpp::il2cpp_stack_peek(st), "three"), "peek returns the top without removing");
        Expect(il2cpp::il2cpp_stack_get_count(st) == 3, "peek does not change count");
        Expect(IsContent(il2cpp::il2cpp_stack_pop(st), "three"), "pop returns the last pushed (LIFO 1)");
        Expect(IsContent(il2cpp::il2cpp_stack_pop(st), "two"), "pop returns the second pushed (LIFO 2)");
        Expect(IsContent(il2cpp::il2cpp_stack_pop(st), "one"), "pop returns the first pushed (LIFO 3)");
        Expect(il2cpp::il2cpp_stack_get_count(st) == 0, "stack empty after popping all");
    }

    // --- 3. Stack null element: push(nullptr) -> pop returns nullptr ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st, nullptr);
        Expect(il2cpp::il2cpp_stack_get_count(st) == 1, "null push still counts");
        Expect(il2cpp::il2cpp_stack_pop(st) == nullptr, "null element pops back as null");
        Expect(il2cpp::il2cpp_stack_get_count(st) == 0, "count back to 0 after null pop");
    }

    // --- 4. Stack growth: items array doubles exactly when the stack fills
    //    (4 -> 8 -> 16); a 10-push stack pops back in exact reverse order ---
    {
        il2cpp::Il2CppStack* st = reinterpret_cast<il2cpp::Il2CppStack*>(il2cpp::il2cpp_stack_create());
        il2cpp::Il2CppObject* stObj = reinterpret_cast<il2cpp::Il2CppObject*>(st);
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::il2cpp_stack_push(stObj, Str("s-00"));
        }
        Expect(st->items->length == 4, "stack array starts at 4 slots");
        il2cpp::il2cpp_stack_push(stObj, Str("s-00"));
        Expect(st->items->length == 8, "5th push doubles the array to 8");
        for (int32_t i = 0; i < 3; ++i)
        {
            il2cpp::il2cpp_stack_push(stObj, Str("s-00"));
        }
        Expect(st->items->length == 8, "8 elements still fit in 8 slots");
        il2cpp::il2cpp_stack_push(stObj, Str("s-00"));
        Expect(st->items->length == 16, "9th push doubles the array to 16");

        il2cpp::Il2CppObject* st2 = il2cpp::il2cpp_stack_create();
        const char* values[10] = { "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9" };
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_stack_push(st2, Str(values[i]));
        }
        Expect(il2cpp::il2cpp_stack_get_count(st2) == 10, "10 pushes -> count 10 (multiple doublings)");
        bool orderOk = true;
        for (int32_t i = 9; i >= 0; --i)
        {
            if (!IsContent(il2cpp::il2cpp_stack_pop(st2), values[i])) orderOk = false;
        }
        Expect(orderOk, "10 pops return the exact reverse push order");
    }

    // --- 5. Stack clear: count -> 0, elements gone, reusable afterwards ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st, Str("a"));
        il2cpp::il2cpp_stack_push(st, Str("b"));
        il2cpp::il2cpp_stack_clear(st);
        Expect(il2cpp::il2cpp_stack_get_count(st) == 0, "clear -> count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_pop(st); }, kInvalidOperationChain),
               "pop after clear raises (elements dropped)");
        il2cpp::il2cpp_stack_push(st, Str("again"));
        Expect(IsContent(il2cpp::il2cpp_stack_pop(st), "again"), "stack is reusable after clear");
    }

    // --- 6. Queue FIFO order: enqueue 1,2,3 -> dequeue 1,2,3 ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q, Str("first"));
        il2cpp::il2cpp_queue_enqueue(q, Str("second"));
        il2cpp::il2cpp_queue_enqueue(q, Str("third"));
        Expect(il2cpp::il2cpp_queue_get_count(q) == 3, "three enqueues -> count 3");
        Expect(IsContent(il2cpp::il2cpp_queue_peek(q), "first"), "peek returns the front without removing");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 3, "peek does not change count");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "first"), "dequeue returns the first enqueued (FIFO 1)");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "second"), "dequeue returns the second enqueued (FIFO 2)");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "third"), "dequeue returns the third enqueued (FIFO 3)");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 0, "queue empty after dequeuing all");
    }

    // --- 7. Queue ring wrap: fill the 4-slot ring, dequeue from the front,
    //    re-enqueue (wraps), and verify the front-to-back order survives ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        // a b c d fill the ring (head 0, count 4)
        il2cpp::il2cpp_queue_enqueue(q, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q, Str("b"));
        il2cpp::il2cpp_queue_enqueue(q, Str("c"));
        il2cpp::il2cpp_queue_enqueue(q, Str("d"));
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "a"), "wrap: first dequeue is a");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "b"), "wrap: second dequeue is b (head now 2)");
        // e f wrap into the vacated slots 0,1; order is now c d e f
        il2cpp::il2cpp_queue_enqueue(q, Str("e"));
        il2cpp::il2cpp_queue_enqueue(q, Str("f"));
        Expect(il2cpp::il2cpp_queue_get_count(q) == 4, "wrap: count 4 with two wrapped slots");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "c"), "wrap: front stayed at c");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "d"), "wrap: then d");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "e"), "wrap: wrapped element e dequeued");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "f"), "wrap: wrapped element f dequeued");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 0, "wrap: queue empty after the wrapped drain");
    }

    // --- 8. Queue growth with re-layout: growing a WRAPPED ring re-lays it
    //    with head = 0 while preserving the front-to-back order ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        // a b c d fill the ring; dequeue a b (head -> 2, count 2)
        il2cpp::il2cpp_queue_enqueue(q, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q, Str("b"));
        il2cpp::il2cpp_queue_enqueue(q, Str("c"));
        il2cpp::il2cpp_queue_enqueue(q, Str("d"));
        il2cpp::il2cpp_queue_dequeue(q);
        il2cpp::il2cpp_queue_dequeue(q);
        // e f wrap into slots 0,1 (ring c d e f, head 2, count 4)
        il2cpp::il2cpp_queue_enqueue(q, Str("e"));
        il2cpp::il2cpp_queue_enqueue(q, Str("f"));
        // g triggers the grow (count == length): 4 -> 8, the wrapped ring is
        // re-laid head-first (c d e f g), head resets to 0
        il2cpp::il2cpp_queue_enqueue(q, Str("g"));
        il2cpp::Il2CppQueue* qq = reinterpret_cast<il2cpp::Il2CppQueue*>(q);
        Expect(qq->items->length == 8, "growth: 5th enqueue doubles the wrapped ring to 8");
        Expect(qq->head == 0, "growth: re-laid ring starts at head 0");
        // h i j fill the 8-slot ring (count 8); k triggers the 8 -> 16 grow
        il2cpp::il2cpp_queue_enqueue(q, Str("h"));
        il2cpp::il2cpp_queue_enqueue(q, Str("i"));
        il2cpp::il2cpp_queue_enqueue(q, Str("j"));
        Expect(il2cpp::il2cpp_queue_get_count(q) == 8, "growth: 8 elements queued");
        il2cpp::il2cpp_queue_enqueue(q, Str("k"));
        Expect(qq->items->length == 16, "growth: 9th enqueue doubles the ring to 16");
        il2cpp::il2cpp_queue_enqueue(q, Str("l"));
        const char* expect[10] = { "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" };
        bool orderOk = true;
        for (int32_t i = 0; i < 10; ++i)
        {
            if (!IsContent(il2cpp::il2cpp_queue_dequeue(q), expect[i])) orderOk = false;
        }
        Expect(orderOk, "growth: front-to-back order preserved across re-layout");
    }

    // --- 9. Queue null element: enqueue(nullptr) -> dequeue returns nullptr ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q, Str("real"));
        il2cpp::il2cpp_queue_enqueue(q, nullptr);
        Expect(il2cpp::il2cpp_queue_get_count(q) == 2, "null enqueue still counts");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "real"), "null does not disturb FIFO order");
        Expect(il2cpp::il2cpp_queue_dequeue(q) == nullptr, "null element dequeues as null");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 0, "count back to 0 after null dequeue");
    }

    // --- 10. Queue clear: count -> 0, elements gone, reusable afterwards ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q, Str("x"));
        il2cpp::il2cpp_queue_enqueue(q, Str("y"));
        il2cpp::il2cpp_queue_clear(q);
        Expect(il2cpp::il2cpp_queue_get_count(q) == 0, "clear -> count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_dequeue(q); }, kInvalidOperationChain),
               "dequeue after clear raises (elements dropped)");
        il2cpp::il2cpp_queue_enqueue(q, Str("again"));
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "again"), "queue is reusable after clear");
    }

    // --- 11. Stack ToArray: TOP-OF-STACK FIRST (net48 Stack.ToArray order) ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st, Str("bottom"));
        il2cpp::il2cpp_stack_push(st, Str("middle"));
        il2cpp::il2cpp_stack_push(st, Str("top"));
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_stack_to_array(st));
        Expect(arr->length == 3, "stack to_array length == count");
        Expect(arr->elemSize == static_cast<int32_t>(sizeof(void*)) && arr->isRefElem,
               "stack to_array is a reference (object[]) array");
        il2cpp::Il2CppObject** elems = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
        Expect(IsContent(elems[0], "top") && IsContent(elems[1], "middle") && IsContent(elems[2], "bottom"),
               "stack to_array: top-of-stack first (net48 order)");
        Expect(il2cpp::il2cpp_stack_get_count(st) == 3, "stack to_array does not mutate the stack");
        Expect(IsContent(il2cpp::il2cpp_stack_pop(st), "top"), "stack unchanged after to_array");
    }

    // --- 12. Stack ToArray: empty stack -> empty (length-0) array ---
    {
        il2cpp::Il2CppObject* st = il2cpp::il2cpp_stack_create();
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_stack_to_array(st));
        Expect(arr->length == 0, "empty stack to_array length 0");
    }

    // --- 13. Queue ToArray: FRONT (head) FIRST, ring-aware (net48
    //    Queue.ToArray order) ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        // a b c d fill the ring; dequeue a b (head -> 2, count 2)
        il2cpp::il2cpp_queue_enqueue(q, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q, Str("b"));
        il2cpp::il2cpp_queue_enqueue(q, Str("c"));
        il2cpp::il2cpp_queue_enqueue(q, Str("d"));
        il2cpp::il2cpp_queue_dequeue(q);
        il2cpp::il2cpp_queue_dequeue(q);
        // e f wrap into slots 0,1 (ring c d e f, head 2, count 4)
        il2cpp::il2cpp_queue_enqueue(q, Str("e"));
        il2cpp::il2cpp_queue_enqueue(q, Str("f"));
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_queue_to_array(q));
        Expect(arr->length == 4, "queue to_array length == count (wrapped ring)");
        Expect(arr->elemSize == static_cast<int32_t>(sizeof(void*)) && arr->isRefElem,
               "queue to_array is a reference (object[]) array");
        il2cpp::Il2CppObject** elems = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
        Expect(IsContent(elems[0], "c") && IsContent(elems[1], "d") && IsContent(elems[2], "e") && IsContent(elems[3], "f"),
               "queue to_array: front (head) first across the ring wrap (net48 order)");
        Expect(il2cpp::il2cpp_queue_get_count(q) == 4, "queue to_array does not mutate the queue");
        Expect(IsContent(il2cpp::il2cpp_queue_dequeue(q), "c"), "queue unchanged after to_array");
    }

    // --- 14. Queue ToArray: empty queue -> empty (length-0) array ---
    {
        il2cpp::Il2CppObject* q = il2cpp::il2cpp_queue_create();
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_queue_to_array(q));
        Expect(arr->length == 0, "empty queue to_array length 0");
    }

    // --- 15. Enumerator VERSION CHECK (D-STK-1): a structural mutation after
    //    GetEnumerator makes the snapshot enumerator throw InvalidOperation-
    //    Exception on MoveNext / Reset (net48 InvalidOperation_EnumFailedVersion).
    //    Read-only operations never bump the version. ---
    {
        // 15a. version counter: push/pop/clear bump, peek/count/to_array do not.
        il2cpp::Il2CppStack* st = reinterpret_cast<il2cpp::Il2CppStack*>(il2cpp::il2cpp_stack_create());
        il2cpp::Il2CppObject* stObj = reinterpret_cast<il2cpp::Il2CppObject*>(st);
        Expect(st->version == 0, "fresh stack version 0");
        il2cpp::il2cpp_stack_push(stObj, Str("a"));
        il2cpp::il2cpp_stack_push(stObj, Str("b"));
        Expect(st->version == 2, "two pushes bump the version to 2");
        il2cpp::il2cpp_stack_peek(stObj);
        il2cpp::il2cpp_stack_get_count(stObj);
        il2cpp::il2cpp_stack_to_array(stObj);
        Expect(st->version == 2, "peek / get_count / to_array do not bump the version");
        il2cpp::il2cpp_stack_pop(stObj);
        Expect(st->version == 3, "pop bumps the version");
        il2cpp::il2cpp_stack_clear(stObj);
        Expect(st->version == 4, "clear bumps the version");

        il2cpp::Il2CppQueue* q = reinterpret_cast<il2cpp::Il2CppQueue*>(il2cpp::il2cpp_queue_create());
        il2cpp::Il2CppObject* qObj = reinterpret_cast<il2cpp::Il2CppObject*>(q);
        Expect(q->version == 0, "fresh queue version 0");
        il2cpp::il2cpp_queue_enqueue(qObj, Str("x"));
        Expect(q->version == 1, "enqueue bumps the queue version");
        il2cpp::il2cpp_queue_peek(qObj);
        il2cpp::il2cpp_queue_get_count(qObj);
        il2cpp::il2cpp_queue_to_array(qObj);
        Expect(q->version == 1, "queue peek / get_count / to_array do not bump the version");
        il2cpp::il2cpp_queue_dequeue(qObj);
        Expect(q->version == 2, "dequeue bumps the queue version");
        il2cpp::il2cpp_queue_clear(qObj);
        Expect(q->version == 3, "queue clear bumps the version");

        // 15b. Stack: mutation after GetEnumerator throws on MoveNext.
        il2cpp::Il2CppObject* st1 = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st1, Str("one"));
        il2cpp::il2cpp_stack_push(st1, Str("two"));
        il2cpp::Il2CppObject* en1 = il2cpp::il2cpp_stack_get_enumerator(st1);
        Expect(il2cpp::il2cpp_stack_enumerator_move_next(en1) == 1 &&
               IsContent(il2cpp::il2cpp_stack_enumerator_get_current(en1), "two"),
               "enumerator starts at the top before any mutation");
        il2cpp::il2cpp_stack_push(st1, Str("three"));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_enumerator_move_next(en1); }, kInvalidOperationChain),
               "MoveNext after Push throws InvalidOperationException (D-STK-1)");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_enumerator_reset(en1); }, kInvalidOperationChain),
               "Reset after Push throws InvalidOperationException (D-STK-1)");

        // 15c. Stack: Pop / Clear after GetEnumerator also throw on MoveNext.
        il2cpp::Il2CppObject* st2 = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st2, Str("a"));
        il2cpp::il2cpp_stack_push(st2, Str("b"));
        il2cpp::Il2CppObject* en2 = il2cpp::il2cpp_stack_get_enumerator(st2);
        il2cpp::il2cpp_stack_pop(st2);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_enumerator_move_next(en2); }, kInvalidOperationChain),
               "MoveNext after Pop throws InvalidOperationException (D-STK-1)");
        il2cpp::Il2CppObject* st3 = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st3, Str("a"));
        il2cpp::Il2CppObject* en3 = il2cpp::il2cpp_stack_get_enumerator(st3);
        il2cpp::il2cpp_stack_clear(st3);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_stack_enumerator_move_next(en3); }, kInvalidOperationChain),
               "MoveNext after Clear throws InvalidOperationException (D-STK-1)");

        // 15d. Stack: a STABLE container re-enumerates freely (no false throw).
        il2cpp::Il2CppObject* st4 = il2cpp::il2cpp_stack_create();
        il2cpp::il2cpp_stack_push(st4, Str("x"));
        il2cpp::il2cpp_stack_push(st4, Str("y"));
        il2cpp::Il2CppObject* en4 = il2cpp::il2cpp_stack_get_enumerator(st4);
        Expect(il2cpp::il2cpp_stack_enumerator_move_next(en4) == 1, "stable stack: first MoveNext ok");
        il2cpp::il2cpp_stack_enumerator_reset(en4);
        Expect(il2cpp::il2cpp_stack_enumerator_move_next(en4) == 1 &&
               IsContent(il2cpp::il2cpp_stack_enumerator_get_current(en4), "y"),
               "stable stack: Reset then MoveNext ok (version unchanged)");

        // 15e. Queue: Enqueue / Dequeue / Clear after GetEnumerator throw on
        //      MoveNext; a stable queue re-enumerates freely.
        il2cpp::Il2CppObject* q1 = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q1, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q1, Str("b"));
        il2cpp::Il2CppObject* eq1 = il2cpp::il2cpp_queue_get_enumerator(q1);
        il2cpp::il2cpp_queue_enqueue(q1, Str("c"));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_enumerator_move_next(eq1); }, kInvalidOperationChain),
               "queue MoveNext after Enqueue throws InvalidOperationException (D-STK-1)");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_enumerator_reset(eq1); }, kInvalidOperationChain),
               "queue Reset after Enqueue throws InvalidOperationException (D-STK-1)");

        il2cpp::Il2CppObject* q2 = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q2, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q2, Str("b"));
        il2cpp::Il2CppObject* eq2 = il2cpp::il2cpp_queue_get_enumerator(q2);
        il2cpp::il2cpp_queue_dequeue(q2);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_enumerator_move_next(eq2); }, kInvalidOperationChain),
               "queue MoveNext after Dequeue throws InvalidOperationException (D-STK-1)");

        il2cpp::Il2CppObject* q3 = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q3, Str("a"));
        il2cpp::Il2CppObject* eq3 = il2cpp::il2cpp_queue_get_enumerator(q3);
        il2cpp::il2cpp_queue_clear(q3);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_queue_enumerator_move_next(eq3); }, kInvalidOperationChain),
               "queue MoveNext after Clear throws InvalidOperationException (D-STK-1)");

        il2cpp::Il2CppObject* q4 = il2cpp::il2cpp_queue_create();
        il2cpp::il2cpp_queue_enqueue(q4, Str("a"));
        il2cpp::il2cpp_queue_enqueue(q4, Str("b"));
        il2cpp::Il2CppObject* eq4 = il2cpp::il2cpp_queue_get_enumerator(q4);
        Expect(il2cpp::il2cpp_queue_enumerator_move_next(eq4) == 1 &&
               IsContent(il2cpp::il2cpp_queue_enumerator_get_current(eq4), "a"),
               "stable queue: first MoveNext ok");
        il2cpp::il2cpp_queue_enumerator_reset(eq4);
        Expect(il2cpp::il2cpp_queue_enumerator_move_next(eq4) == 1 &&
               IsContent(il2cpp::il2cpp_queue_enumerator_get_current(eq4), "a"),
               "stable queue: Reset then MoveNext ok (version unchanged)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d stackqueue assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stackqueue tests passed\n");
    return 0;
}
