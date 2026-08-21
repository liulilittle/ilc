//------------------------------------------------------------------------------
// test_listi4.cpp -- System.Collections.Generic.List<int32> runtime unit
// tests (listi4.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. create: count starts at 0; get/set/remove_at/insert out of range on
//      the empty list raise ArgumentOutOfRangeException -- with the net48
//      MESSAGE pinned: element ops carry ArgumentOutOfRange_Index, insert
//      carries the distinct ArgumentOutOfRange_ListInsert text.
//   2. add + get_item order: append 1,2,3 -> items 1,2,3, count 3.
//   3. set_item overwrite: item 1 -> 42.
//   4. Growth: the items array doubles exactly when the list fills
//      (4 -> 8 -> 16); a 100-add list reads back every element in order.
//   5. insert: front, middle and end (end == append) placements.
//   6. insert bounds: -1 and count+1 raise ArgumentOutOfRangeException (with
//      the net48 INSERT message pinned).
//   7. remove_at: head, middle and tail; count shrinks; out-of-range raises.
//   8. remove: first-equal removal (duplicates keep the later copy);
//      absent value leaves the list unchanged and returns false.
//   9. index_of / contains: hits, misses and duplicate handling.
//  10. clear: count -> 0 and the list is reusable afterwards.
//  11. to_array: exact-length independent copy (mutating the returned array
//      does not change the list).
//  12. create_with_capacity: exact-capacity pre-size + growth (10 -> 20,
//      3 -> 6), the zero-capacity minimum-array fallback, and the negative
//      capacity ArgumentOutOfRangeException with the net48 message pinned.
//
// Plain executable + minimal assertion harness (see test_stackqueue.cpp).
//------------------------------------------------------------------------------

#include "listi4.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "listi4 test fixture assumes 8-byte pointers");

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
const char* kArgOutOfRangeChain =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";

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

// Raise-and-catch helper that ALSO pins the exception MESSAGE (net48 resource
// text). Returns true when the chain AND the message match.
template <typename Fn>
bool ThrowsDotNetWithMessage(Fn fn, const char* expectedChain, const char* expectedMessage)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0 &&
               std::strcmp(e.message_text(), expectedMessage) == 0;
    }
}

// net48 resource texts (see listi4.cpp): get/set/remove_at use
// ArgumentOutOfRange_Index; insert uses ArgumentOutOfRange_ListInsert (a
// DIFFERENT string -- List<T> documents distinct text for each).
const char* kIndexOutOfRangeMessage =
    "Index was out of range. Must be non-negative and less than the size of the collection.";
const char* kInsertOutOfRangeMessage =
    "Index must be within the bounds of the List.";
const char* kNonNegativeMessage = "Non-negative number required.";

// GC-during-growth fixture: a plain 32-byte reference-free class whose
// instances fill the current Gen0 segment (see probe 0). File-scope like the
// linq_sort fixtures: the class descriptor address must stay stable while the
// collector scans instances across a collection.
il2cpp::Il2CppClass g_listI4JunkClass = {
    "Test.ListI4Junk", // name
    32,                // instanceSize (a clean 32-byte block)
    0u,                // refFieldBitmap: no reference fields
    0,                 // refFieldCount
    nullptr,           // baseClass
    nullptr,           // vtable
    0,                 // vtableSize
    nullptr,           // interfaces
    0,                 // interfaceCount
};

} // namespace

int main()
{
    // --- 0. GC-during-growth probe (FIRST: a clean heap makes the segment
    //    math exact). A collection fired by the GROWTH allocation (segment
    //    full + free list empty) compacts Gen0 and MOVES the list object; the
    //    icall must keep 'self' valid across the growth (root + re-read), or
    //    the stale pointer corrupts the list. Sizing: list object 24 bytes,
    //    items(4) array = 32+16 = 48; filler int32[16350] = 32+65400 =
    //    65432 -> used = 65504. The grow to 8 needs 64 more -> 65568 > 65536
    //    -> the growth allocation fires a collection. ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::il2cpp_list_i4_add(list, i);
        }
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 4, "pre-probe: list holds 4 elements");

        // Rooted filler fills the current Gen0 segment so the NEXT allocation
        // (the grow's new items array) overflows and triggers a collection.
        // MANY small objects, NOT one oversized array (the oversized block
        // trips a pre-existing collector promote-path issue -- reproduces with
        // ORIGINAL code too, outside the listi4 domain).
        il2cpp::Il2CppObject* junkA[2044] = { nullptr };
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(&junkA[0]), 2044);
        for (int32_t i = 0; i < 2044; ++i)
        {
            junkA[i] = il2cpp::il2cpp_gc_alloc(&g_listI4JunkClass);
        }

        const int64_t before = il2cpp::il2cpp_gc_get_collection_count(0);
        try
        {
            il2cpp::il2cpp_list_i4_add(list, 99); // 5th add: grows 4 -> 8
        }
        catch (...)
        {
            // original code's stale-pointer corruption may throw; swallow so the
            // process reaches scope exit and the CRT heap check runs.
        }
        const int64_t after = il2cpp::il2cpp_gc_get_collection_count(0);
        Expect(after > before, "probe precondition: the growth allocation triggered a collection");

        // Read count defensively (may be corrupt on the unpatched build).
        bool countOk = true;
        try
        {
            countOk = il2cpp::il2cpp_list_i4_get_count(list) == 5;
        }
        catch (...)
        {
            countOk = false;
        }
        Expect(countOk, "list count intact across a growth-triggered collection");
        bool itemOk = false;
        try
        {
            itemOk = il2cpp::il2cpp_list_i4_get_item(list, 4) == 99;
        }
        catch (...)
        {
            itemOk = false;
        }
        Expect(itemOk, "the element that forced the grow survives the mid-growth collection");
        bool keep = true;
        for (int32_t i = 0; i < 4 && keep; ++i)
        {
            try
            {
                keep = il2cpp::il2cpp_list_i4_get_item(list, i) == i;
            }
            catch (...)
            {
                keep = false;
            }
        }
        Expect(keep, "pre-growth elements survive the mid-growth collection");

        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(&junkA[0]));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 1. create: count 0; out-of-range access on the empty list raises
    //    ArgumentOutOfRangeException ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 0, "fresh list: count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_get_item(list, 0); }, kArgOutOfRangeChain),
               "get_item(0) on an empty list raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_get_item(list, 0); },
                                       kArgOutOfRangeChain, kIndexOutOfRangeMessage),
               "get_item out-of-range carries the net48 element message");
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_remove_at(list, 0); },
                                       kArgOutOfRangeChain, kIndexOutOfRangeMessage),
               "remove_at out-of-range carries the net48 element message");
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_insert(list, 1, 7); },
                                       kArgOutOfRangeChain, kInsertOutOfRangeMessage),
               "insert out-of-range carries the net48 INSERT message (distinct)");
    }

    // --- 2. add + get_item order: append 1,2,3 -> items 1,2,3 ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 1);
        il2cpp::il2cpp_list_i4_add(list, 2);
        il2cpp::il2cpp_list_i4_add(list, 3);
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 3, "three adds -> count 3");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 1, "item 0 == 1");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 1) == 2, "item 1 == 2");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 2) == 3, "item 2 == 3");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_get_item(list, 3); }, kArgOutOfRangeChain),
               "get_item(count) raises ArgumentOutOfRangeException");
    }

    // --- 3. set_item overwrite: item 1 -> 42 ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 10);
        il2cpp::il2cpp_list_i4_add(list, 20);
        il2cpp::il2cpp_list_i4_set_item(list, 1, 42);
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 10, "set_item keeps untouched items");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 1) == 42, "set_item overwrites item 1");
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 2, "set_item does not change count");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_set_item(list, 2, 1); }, kArgOutOfRangeChain),
               "set_item(count) raises ArgumentOutOfRangeException");
    }

    // --- 4. Growth: the items array doubles exactly when the list fills
    //    (4 -> 8 -> 16); a 100-add list reads back every element in order ---
    {
        // The growth allocations below can trigger a Gen0 compaction that
        // MOVES the list object (Phase C rewrites only registered roots), so
        // the raw pointer must be rooted and re-derived after each allocating
        // icall -- the same contract section 0 exercises (test_hashtable.cpp
        // bigObj precedent). Without it the caller-side pointer goes stale and
        // the length reads hit freed memory (0xC0000374 heap-corruption class).
        il2cpp::Il2CppObject* listObj = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&listObj));
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::il2cpp_list_i4_add(listObj, i);
        }
        il2cpp::Il2CppListI4* list = reinterpret_cast<il2cpp::Il2CppListI4*>(listObj);
        Expect(list->items->length == 4, "list array starts at 4 slots");
        il2cpp::il2cpp_list_i4_add(listObj, 4);
        list = reinterpret_cast<il2cpp::Il2CppListI4*>(listObj); // re-derive: the grow may have compacted the list
        Expect(list->items->length == 8, "5th add doubles the array to 8");
        for (int32_t i = 5; i < 8; ++i)
        {
            il2cpp::il2cpp_list_i4_add(listObj, i);
        }
        list = reinterpret_cast<il2cpp::Il2CppListI4*>(listObj); // re-derive (another growth may have run)
        Expect(list->items->length == 8, "8 elements still fit in 8 slots");
        il2cpp::il2cpp_list_i4_add(listObj, 8);
        list = reinterpret_cast<il2cpp::Il2CppListI4*>(listObj); // re-derive: 9th add doubles 8 -> 16
        Expect(list->items->length == 16, "9th add doubles the array to 16");

        il2cpp::Il2CppObject* big = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&big));
        for (int32_t i = 0; i < 100; ++i)
        {
            il2cpp::il2cpp_list_i4_add(big, i * 3);
        }
        Expect(il2cpp::il2cpp_list_i4_get_count(big) == 100, "100 adds -> count 100 (multiple doublings)");
        bool orderOk = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            if (il2cpp::il2cpp_list_i4_get_item(big, i) != i * 3) orderOk = false;
        }
        Expect(orderOk, "100 reads return the exact add order");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&big));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&listObj));
    }

    // --- 5. insert: front, middle and end (end == append) ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        il2cpp::il2cpp_list_i4_add(list, 2);
        il2cpp::il2cpp_list_i4_add(list, 3);
        il2cpp::il2cpp_list_i4_insert(list, 0, 1); // front
        il2cpp::il2cpp_list_i4_insert(list, 3, 4); // end (index == count)
        il2cpp::il2cpp_list_i4_insert(list, 2, 99); // middle (grows 4 -> 8: allocating, so 'list' is rooted)
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 5, "three inserts -> count 5");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 1, "insert at front -> item 0 == 1");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 1) == 2, "insert shifts followers right (item 1)");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 2) == 99, "middle insert lands at index 2");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 3) == 3, "middle insert shifts followers right (item 3)");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 4) == 4, "insert at index == count appends");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 6. insert bounds: -1 and count+1 raise ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 5);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_insert(list, -1, 1); }, kArgOutOfRangeChain),
               "insert(-1) raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_insert(list, -1, 1); },
                                       kArgOutOfRangeChain, kInsertOutOfRangeMessage),
               "insert(-1) carries the net48 INSERT message");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_insert(list, 2, 1); }, kArgOutOfRangeChain),
               "insert(count+1) raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_insert(list, 2, 1); },
                                       kArgOutOfRangeChain, kInsertOutOfRangeMessage),
               "insert(count+1) carries the net48 INSERT message");
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 1, "failed inserts leave the list unchanged");
    }

    // --- 7. remove_at: head, middle and tail; out-of-range raises ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        for (int32_t i = 0; i < 5; ++i)
        {
            il2cpp::il2cpp_list_i4_add(list, i);
        }
        il2cpp::il2cpp_list_i4_remove_at(list, 2); // middle: 0 1 2 3 4 -> 0 1 3 4
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 4, "remove_at middle -> count 4");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 2) == 3, "remove_at middle shifts followers left");
        il2cpp::il2cpp_list_i4_remove_at(list, 0); // head: 1 3 4
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 1, "remove_at head -> item 0 == 1");
        il2cpp::il2cpp_list_i4_remove_at(list, 2); // tail: 1 3
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 2, "remove_at tail -> count 2");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_remove_at(list, 2); }, kArgOutOfRangeChain),
               "remove_at(count) raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_remove_at(list, -1); }, kArgOutOfRangeChain),
               "remove_at(-1) raises ArgumentOutOfRangeException");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 8. remove: first-equal removal; absent value leaves the list
    //    unchanged and returns false ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 7);
        il2cpp::il2cpp_list_i4_add(list, 3);
        il2cpp::il2cpp_list_i4_add(list, 7);
        Expect(il2cpp::il2cpp_list_i4_remove(list, 7), "remove(7) returns true on a hit");
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 2, "remove hit -> count 2");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 3, "remove keeps the later duplicate");
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 1) == 7, "remove keeps the second copy at index 1");
        Expect(!il2cpp::il2cpp_list_i4_remove(list, 99), "remove(99) returns false when absent");
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 2, "absent remove leaves the list unchanged");
    }

    // --- 9. index_of / contains: hits, misses and duplicates ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 10);
        il2cpp::il2cpp_list_i4_add(list, 20);
        il2cpp::il2cpp_list_i4_add(list, 10);
        Expect(il2cpp::il2cpp_list_i4_index_of(list, 10) == 0, "index_of returns the FIRST equal index");
        Expect(il2cpp::il2cpp_list_i4_index_of(list, 20) == 1, "index_of hit at index 1");
        Expect(il2cpp::il2cpp_list_i4_index_of(list, 30) == -1, "index_of miss returns -1");
        Expect(il2cpp::il2cpp_list_i4_contains(list, 10), "contains(10) is true");
        Expect(il2cpp::il2cpp_list_i4_contains(list, 30) == false, "contains(30) is false");
    }

    // --- 10. clear: count -> 0, elements gone, reusable afterwards ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_list_i4_add(list, 1);
        il2cpp::il2cpp_list_i4_add(list, 2);
        il2cpp::il2cpp_list_i4_clear(list);
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 0, "clear -> count 0");
        Expect(il2cpp::il2cpp_list_i4_index_of(list, 1) == -1, "cleared elements are gone");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_get_item(list, 0); }, kArgOutOfRangeChain),
               "get_item after clear raises (elements dropped)");
        il2cpp::il2cpp_list_i4_add(list, 9);
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 0) == 9, "list is reusable after clear");
    }

    // --- 11. to_array: exact-length independent copy ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_list_i4_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        il2cpp::il2cpp_list_i4_add(list, 5);
        il2cpp::il2cpp_list_i4_add(list, 6);
        il2cpp::il2cpp_list_i4_add(list, 7);
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_list_i4_to_array(list));
        Expect(arr->length == 3, "to_array length == count");
        Expect(arr->elemSize == 4 && !arr->isRefElem, "to_array is an int32 value array");
        const int32_t* elems = reinterpret_cast<const int32_t*>(arr->data);
        Expect(elems[0] == 5 && elems[1] == 6 && elems[2] == 7, "to_array copies the elements in order");
        const_cast<int32_t*>(elems)[1] = 999;
        Expect(il2cpp::il2cpp_list_i4_get_item(list, 1) == 6, "to_array result is an independent copy");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 12. create_with_capacity: pre-sized growth + bounds (D-LISTQ-5) ---
    {
        // Positive capacity: the items array starts at exactly 'capacity'
        // (net48 List<T>(int)); the first Add past it doubles (10 -> 20).
        il2cpp::Il2CppObject* cap = il2cpp::il2cpp_list_i4_create_with_capacity(10);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&cap));
        Expect(il2cpp::il2cpp_list_i4_get_count(cap) == 0, "capacity ctor: fresh count 0");
        for (int32_t i = 0; i < 11; ++i)
        {
            il2cpp::il2cpp_list_i4_add(cap, i * 7);
        }
        Expect(il2cpp::il2cpp_list_i4_get_count(cap) == 11, "capacity ctor: 11 adds -> count 11");
        Expect(reinterpret_cast<il2cpp::Il2CppListI4*>(cap)->items->length == 20,
               "capacity ctor: 11th add doubles 10 -> 20 (exact-capacity pre-size)");
        bool capOrder = true;
        for (int32_t i = 0; i < 11; ++i)
        {
            if (il2cpp::il2cpp_list_i4_get_item(cap, i) != i * 7) capOrder = false;
        }
        Expect(capOrder, "capacity ctor: elements survive the 10 -> 20 growth");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&cap));

        // Small positive capacity below the default minimum (3 < 4): the
        // array is exactly 3 and grows 3 -> 6 on the 4th add.
        il2cpp::Il2CppObject* small = il2cpp::il2cpp_list_i4_create_with_capacity(3);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&small));
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::il2cpp_list_i4_add(small, i);
        }
        Expect(reinterpret_cast<il2cpp::Il2CppListI4*>(small)->items->length == 6,
               "capacity ctor(3): 4th add grows 3 -> 6");
        Expect(il2cpp::il2cpp_list_i4_get_item(small, 3) == 3, "capacity ctor(3): element after growth");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&small));

        // Zero capacity: net48's empty-array state is observably equivalent
        // to the default minimum array on the wired surface -- adds work.
        il2cpp::Il2CppObject* zero = il2cpp::il2cpp_list_i4_create_with_capacity(0);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&zero));
        Expect(il2cpp::il2cpp_list_i4_get_count(zero) == 0, "capacity ctor(0): fresh count 0");
        il2cpp::il2cpp_list_i4_add(zero, 42);
        Expect(il2cpp::il2cpp_list_i4_get_count(zero) == 1 && il2cpp::il2cpp_list_i4_get_item(zero, 0) == 42,
               "capacity ctor(0): first add works (minimum-array fallback)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&zero));

        // Negative capacity: ArgumentOutOfRangeException, chain + message.
        Expect(ThrowsDotNetWithMessage([&]() { il2cpp::il2cpp_list_i4_create_with_capacity(-1); },
                                       kArgOutOfRangeChain, kNonNegativeMessage),
               "capacity ctor(-1) raises ArgumentOutOfRangeException with the net48 message");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_list_i4_create_with_capacity(-5); }, kArgOutOfRangeChain),
               "capacity ctor(-5) raises ArgumentOutOfRangeException");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d listi4 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime listi4 tests passed\n");
    return 0;
}
