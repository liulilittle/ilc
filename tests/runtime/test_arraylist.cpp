//------------------------------------------------------------------------------
// test_arraylist.cpp -- System.Collections.ArrayList runtime unit tests
// (arraylist.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. create: count starts at 0; get/set/remove_at out of range on the
//      empty list raise ArgumentOutOfRangeException.
//   2. add + get_item order: append three strings -> items in order, count 3.
//   3. set_item overwrite: element 1 -> a different object; count unchanged.
//   4. Growth: the items array doubles exactly when the list fills
//      (4 -> 8 -> 16); a 100-add list reads back every element in order.
//   5. remove_at: head, middle and tail; count shrinks; out-of-range raises.
//   5b. remove(value): by-value removal of the FIRST reference-identical
//       element (head/middle/tail); absent value and empty list are silent
//       no-ops; a drained list stays usable.
//   5c. remove(value) removes only ONE occurrence (duplicates survive).
//   5d. remove(value) matches a stored null (null == null).
//   6. index_of / contains: hits, misses and null matching (reference
//      identity for interned strings).
//   6b. index_of / contains use VALUE equality (net48 ArrayList.IndexOf
//       Object.Equals semantics): a fresh boxed int with the same payload as
//       a stored boxed int is found; RemoveAt(IndexOf(boxed)) never throws
//       (the e2e listops tail-OOB regression).
//   7. clear: count -> 0, elements gone, reusable afterwards.
//   8. Null element: add(nullptr) counts, get_item returns null, index_of
//      finds it, to_array copies it.
//   9. to_array: exact-length independent object[] copy (mutating the
//      returned array does not change the list).
//  10. GC survival: a rooted list whose elements are reachable ONLY through
//      the list survives a full collection with every element still
//      readable -- proves the refFieldBitmap (items slot) and the
//      reference-array scan keep the whole contents alive. NOTE: the
//      collector COMPACTS Gen0 (survivors move; references are rewritten),
//      so post-collect assertions use fresh lookups + move-safe CONTENT
//      equality, never stale local pointers.
//
// Plain executable + minimal assertion harness (see test_stackqueue.cpp).
//------------------------------------------------------------------------------

#include "arraylist.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "arraylist test fixture assumes 8-byte pointers");

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

il2cpp::Il2CppObject* Str(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Move-safe content check: does 'obj' hold a managed string whose UTF-16
// units equal the ASCII text? Used after collections, where local pointers
// are STALE (the compacting collector moves Gen0 survivors) -- content
// equality is the only comparison that stays valid across a move.
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
    // --- 1. create: count 0; out-of-range access on the empty list raises
    //    ArgumentOutOfRangeException ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 0, "fresh list: count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_get_item(list, 0); }, kArgOutOfRangeChain),
               "get_item(0) on an empty list raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_set_item(list, 0, Str("x")); }, kArgOutOfRangeChain),
               "set_item(0) on an empty list raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_remove_at(list, 0); }, kArgOutOfRangeChain),
               "remove_at(0) on an empty list raises ArgumentOutOfRangeException");
    }

    // --- 2. add + get_item order: append strings -> items in order ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("one"));
        il2cpp::il2cpp_arraylist_add(list, Str("two"));
        il2cpp::il2cpp_arraylist_add(list, Str("three"));
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 3, "three adds -> count 3");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 0), "one"), "item 0 == one");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 1), "two"), "item 1 == two");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 2), "three"), "item 2 == three");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_get_item(list, 3); }, kArgOutOfRangeChain),
               "get_item(count) raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_get_item(list, -1); }, kArgOutOfRangeChain),
               "get_item(-1) raises ArgumentOutOfRangeException");
    }

    // --- 3. set_item overwrite: element 1 -> a different object ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("alpha"));
        il2cpp::il2cpp_arraylist_add(list, Str("beta"));
        il2cpp::il2cpp_arraylist_set_item(list, 1, Str("gamma"));
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 0), "alpha"), "set_item keeps untouched items");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 1), "gamma"), "set_item overwrites item 1");
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "set_item does not change count");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_set_item(list, 2, Str("x")); }, kArgOutOfRangeChain),
               "set_item(count) raises ArgumentOutOfRangeException");
    }

    // --- 4. Growth: the items array doubles exactly when the list fills
    //    (4 -> 8 -> 16); a 100-add list reads back every element in order ---
    {
        il2cpp::Il2CppArrayList* list = reinterpret_cast<il2cpp::Il2CppArrayList*>(il2cpp::il2cpp_arraylist_create());
        il2cpp::Il2CppObject* listObj = reinterpret_cast<il2cpp::Il2CppObject*>(list);
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::il2cpp_arraylist_add(listObj, Str("g-00"));
        }
        Expect(list->items->length == 4, "list array starts at 4 slots");
        il2cpp::il2cpp_arraylist_add(listObj, Str("g-00"));
        Expect(list->items->length == 8, "5th add doubles the array to 8");
        for (int32_t i = 5; i < 8; ++i)
        {
            il2cpp::il2cpp_arraylist_add(listObj, Str("g-00"));
        }
        Expect(list->items->length == 8, "8 elements still fit in 8 slots");
        il2cpp::il2cpp_arraylist_add(listObj, Str("g-00"));
        Expect(list->items->length == 16, "9th add doubles the array to 16");

        il2cpp::Il2CppObject* big = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* expected[100];
        for (int32_t i = 0; i < 100; ++i)
        {
            char buf[16];
            std::snprintf(buf, sizeof(buf), "v%d", i);
            // Interned canonical instances: identity comparison stays valid
            // (the intern pool roots them across any implicit collection).
            expected[i] = il2cpp::il2cpp_string_intern(Str(buf));
            il2cpp::il2cpp_arraylist_add(big, expected[i]);
        }
        Expect(il2cpp::il2cpp_arraylist_get_count(big) == 100, "100 adds -> count 100 (multiple doublings)");
        bool orderOk = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            if (il2cpp::il2cpp_arraylist_get_item(big, i) != expected[i]) orderOk = false;
        }
        Expect(orderOk, "100 reads return the exact add order");
    }

    // --- 5. remove_at: head, middle and tail; out-of-range raises ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* e[5];
        for (int32_t i = 0; i < 5; ++i)
        {
            char buf[8];
            std::snprintf(buf, sizeof(buf), "e%d", i);
            // Interned canonical instances make the post-remove identity
            // lookups meaningful (a vacated slot must NOT be findable).
            e[i] = il2cpp::il2cpp_string_intern(Str(buf));
            il2cpp::il2cpp_arraylist_add(list, e[i]);
        }
        il2cpp::il2cpp_arraylist_remove_at(list, 2); // middle: e0 e1 e2 e3 e4 -> e0 e1 e3 e4
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 4, "remove_at middle -> count 4");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 2) == e[3], "remove_at middle shifts followers left");
        il2cpp::il2cpp_arraylist_remove_at(list, 0); // head: e1 e3 e4
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 0) == e[1], "remove_at head -> item 0 == e1");
        il2cpp::il2cpp_arraylist_remove_at(list, 2); // tail: e1 e3
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "remove_at tail -> count 2");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, e[4]) == -1, "removed tail element is gone (vacated)");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_remove_at(list, 2); }, kArgOutOfRangeChain),
               "remove_at(count) raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_remove_at(list, -1); }, kArgOutOfRangeChain),
               "remove_at(-1) raises ArgumentOutOfRangeException");
    }

    // --- 5b. remove(value): by-value removal of the first reference-identical
    //    element; absent value / empty list are silent no-ops ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* r[5];
        for (int32_t i = 0; i < 5; ++i)
        {
            char buf[8];
            std::snprintf(buf, sizeof(buf), "r%d", i);
            r[i] = il2cpp::il2cpp_string_intern(Str(buf));
            il2cpp::il2cpp_arraylist_add(list, r[i]);
        }
        il2cpp::il2cpp_arraylist_remove(list, r[2]); // middle: r0 r1 r2 r3 r4 -> r0 r1 r3 r4
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 4, "remove(value) middle -> count 4");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 2) == r[3], "remove(value) middle shifts followers left");
        il2cpp::il2cpp_arraylist_remove(list, r[0]); // head: r1 r3 r4
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 0) == r[1], "remove(value) head -> item 0 == r1");
        il2cpp::il2cpp_arraylist_remove(list, r[4]); // tail: r1 r3
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "remove(value) tail -> count 2");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, r[4]) == -1, "removed tail element is gone (vacated)");
        il2cpp::il2cpp_arraylist_remove(list, il2cpp::il2cpp_string_intern(Str("ghost")));
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "remove(value) absent value is a silent no-op");
        il2cpp::il2cpp_arraylist_remove(list, r[1]);
        il2cpp::il2cpp_arraylist_remove(list, r[3]);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 0, "drain via remove(value) -> count 0");
        il2cpp::il2cpp_arraylist_remove(list, r[0]);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 0, "remove(value) on empty list is a silent no-op");
    }

    // --- 5c. remove(value) removes only the FIRST identical element ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* dup = il2cpp::il2cpp_string_intern(Str("dup"));
        il2cpp::il2cpp_arraylist_add(list, Str("x"));
        il2cpp::il2cpp_arraylist_add(list, dup);
        il2cpp::il2cpp_arraylist_add(list, Str("y"));
        il2cpp::il2cpp_arraylist_add(list, dup);
        il2cpp::il2cpp_arraylist_remove(list, dup);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 3, "remove(value) removes ONE occurrence");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, dup) == 2, "the SECOND occurrence survives at index 2");
        Expect(il2cpp::il2cpp_arraylist_contains(list, dup), "contains still true after one removal");
    }

    // --- 5d. remove(value) matches a stored null (null == null) ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* real = Str("real");
        il2cpp::il2cpp_arraylist_add(list, real);
        il2cpp::il2cpp_arraylist_add(list, nullptr);
        il2cpp::il2cpp_arraylist_add(list, Str("after"));
        il2cpp::il2cpp_arraylist_remove(list, nullptr);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "remove(null) drops the stored null");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 0) == real, "remove(null) keeps the element before the null");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, nullptr) == -1, "no null remains after remove(null)");
    }

    // --- 6. index_of / contains: hits, misses and null matching ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::Il2CppObject* s10 = Str("ten");
        il2cpp::Il2CppObject* s20 = Str("twenty");
        il2cpp::il2cpp_arraylist_add(list, s10);
        il2cpp::il2cpp_arraylist_add(list, s20);
        il2cpp::il2cpp_arraylist_add(list, s10); // duplicate instance
        Expect(il2cpp::il2cpp_arraylist_index_of(list, s10) == 0, "index_of returns the FIRST identical index");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, s20) == 1, "index_of hit at index 1");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, Str("absent")) == -1, "index_of miss returns -1");
        Expect(il2cpp::il2cpp_arraylist_contains(list, s10), "contains(ten) is true");
        Expect(il2cpp::il2cpp_arraylist_contains(list, Str("absent")) == false, "contains(absent) is false");
        il2cpp::il2cpp_arraylist_add(list, nullptr);
        Expect(il2cpp::il2cpp_arraylist_index_of(list, nullptr) == 3, "index_of finds a stored null");
    }

    // --- 6b. index_of / contains use VALUE equality (net48
    //    ArrayList.IndexOf Object.Equals semantics): two distinct boxed ints
    //    with the same payload compare equal ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        // Each box is a DISTINCT managed object with the same payload.
        il2cpp::Il2CppObject* box10a = il2cpp::il2cpp_box_int32(nullptr, 10);
        il2cpp::Il2CppObject* box30a = il2cpp::il2cpp_box_int32(nullptr, 30);
        il2cpp::Il2CppObject* box10b = il2cpp::il2cpp_box_int32(nullptr, 10);
        il2cpp::Il2CppObject* box30b = il2cpp::il2cpp_box_int32(nullptr, 30);
        il2cpp::il2cpp_arraylist_add(list, box10a);
        il2cpp::il2cpp_arraylist_add(list, box30a);
        // Sanity: the "b" boxes are genuinely distinct instances (not the
        // stored ones) -- identity alone would MISS them.
        Expect(box10a != box10b, "box10b is a distinct instance from box10a");
        Expect(box30a != box30b, "box30b is a distinct instance from box30a");
        // Value equality: a distinct boxed int with the same payload matches.
        Expect(il2cpp::il2cpp_arraylist_index_of(list, box30b) == 1,
               "index_of finds a boxed int by VALUE (distinct instance)");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, box10b) == 0,
               "index_of finds boxed 10 by VALUE");
        Expect(il2cpp::il2cpp_arraylist_contains(list, box10b),
               "contains finds a boxed int by VALUE");
        Expect(il2cpp::il2cpp_arraylist_contains(list, il2cpp::il2cpp_box_int32(nullptr, 99)) == false,
               "contains misses an absent boxed int");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, il2cpp::il2cpp_box_int32(nullptr, 99)) == -1,
               "index_of misses an absent boxed int");
        // Value-equal remove: Remove(box30b) must drop the stored box30a.
        il2cpp::il2cpp_arraylist_remove(list, box30b);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 1,
               "remove removes a boxed int by VALUE");
        Expect(il2cpp::il2cpp_arraylist_contains(list, box30b) == false,
               "value-removed boxed int is gone");
        // e2e listops tail-OOB regression: RemoveAt(IndexOf(boxed)) -- the
        // old reference-identity IndexOf returned -1 here, so RemoveAt(-1)
        // raised "Index was out of range".
        il2cpp::Il2CppObject* b = il2cpp::il2cpp_arraylist_create();
        for (int32_t i = 0; i < 5; ++i)
        {
            il2cpp::il2cpp_arraylist_add(b, il2cpp::il2cpp_box_int32(nullptr, i * 10));
        }
        int32_t idx = il2cpp::il2cpp_arraylist_index_of(b, il2cpp::il2cpp_box_int32(nullptr, 30));
        Expect(idx == 3, "interleaved IndexOf(boxed 30) == 3 (not -1)");
        Expect(!ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_remove_at(b, idx); }, kArgOutOfRangeChain),
               "RemoveAt(IndexOf(boxed 30)) does NOT throw (no OOB)");
        Expect(il2cpp::il2cpp_arraylist_get_count(b) == 4, "interleaved RemoveAt leaves count 4");
        Expect(il2cpp::il2cpp_arraylist_index_of(b, il2cpp::il2cpp_box_int32(nullptr, 40)) == 3,
               "elements shift correctly after the interleaved RemoveAt");
    }

    // --- 7. clear: count -> 0, elements gone, reusable afterwards ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("a"));
        il2cpp::il2cpp_arraylist_add(list, Str("b"));
        il2cpp::il2cpp_arraylist_clear(list);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 0, "clear -> count 0");
        Expect(il2cpp::il2cpp_arraylist_index_of(list, Str("a")) == -1, "cleared elements are gone");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_arraylist_get_item(list, 0); }, kArgOutOfRangeChain),
               "get_item after clear raises (elements dropped)");
        il2cpp::il2cpp_arraylist_add(list, Str("again"));
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 0), "again"), "list is reusable after clear");
    }

    // --- 8. Null element: add(nullptr) counts, get returns null, to_array
    //    copies it ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("real"));
        il2cpp::il2cpp_arraylist_add(list, nullptr);
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 2, "null add still counts");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 0), "real"), "null does not disturb order");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 1) == nullptr, "null element reads back as null");
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_arraylist_to_array(list));
        Expect(arr->length == 2, "to_array length == count");
        il2cpp::Il2CppObject** elems = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
        Expect(IsContent(elems[0], "real") && elems[1] == nullptr, "to_array copies null elements");
    }

    // --- 9. to_array: exact-length independent object[] copy ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("x"));
        il2cpp::il2cpp_arraylist_add(list, Str("y"));
        il2cpp::il2cpp_arraylist_add(list, Str("z"));
        il2cpp::Il2CppArray* arr =
            reinterpret_cast<il2cpp::Il2CppArray*>(il2cpp::il2cpp_arraylist_to_array(list));
        Expect(arr->length == 3, "to_array length == count");
        Expect(arr->elemSize == static_cast<int32_t>(sizeof(void*)) && arr->isRefElem,
               "to_array is a reference (object[]) array");
        il2cpp::Il2CppObject** elems = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
        Expect(IsContent(elems[0], "x") && IsContent(elems[1], "y") && IsContent(elems[2], "z"),
               "to_array copies the elements in order");
        elems[1] = Str("mutated");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 1), "y"),
               "to_array result is an independent copy");
    }

    // --- 10. GC survival: elements reachable ONLY through the rooted list
    //    survive a full collection ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_add(list, Str("gc-one"));
        il2cpp::il2cpp_arraylist_add(list, Str("gc-two"));
        il2cpp::il2cpp_arraylist_add(list, il2cpp::il2cpp_box_int32(nullptr, 99));
        il2cpp::il2cpp_arraylist_add(list, nullptr);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        il2cpp::il2cpp_gc_collect();
        // NOTE: the compacting collector MOVES Gen0 survivors and rewrites
        // every reference, so pre-collect local element pointers are STALE
        // afterwards; every check below uses FRESH lookups and move-safe
        // CONTENT equality instead of pointer comparison.
        Expect(il2cpp::il2cpp_gc_get_generation(list) >= il2cpp::kGcGeneration1,
               "rooted list survives collection (promoted)");
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 4,
               "count intact after collection");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 0), "gc-one"),
               "first element survived via the items-array bitmap scan");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, 1), "gc-two"),
               "second element survived (reference-array scan)");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 2) != nullptr &&
               il2cpp::il2cpp_arraylist_get_item(list, 2)->klass != nullptr &&
               std::strcmp(il2cpp::il2cpp_arraylist_get_item(list, 2)->klass->name, "System.Int32") == 0,
               "boxed-int element survived (reference-array scan)");
        Expect(il2cpp::il2cpp_arraylist_get_item(list, 3) == nullptr,
               "stored null still null after collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 10b. D-AL-2 regression: Insert into a FRESH list (create icall
    //    skipped -> null items array) must NOT crash. The e2e newobj path
    //    allocates via il2cpp_object_new(il2cpp_arraylist_class()) -- a
    //    zero-initialized list with items == nullptr -- and GrowList used to
    //    dereference items->length before any lazy allocation (crash). Now
    //    GrowList runs EnsureItems first, so a fresh list's Insert works. ---
    {
        // Exact newobj path: zero-initialized object, items array absent.
        il2cpp::Il2CppObject* fresh =
            il2cpp::il2cpp_object_new(il2cpp::il2cpp_arraylist_class());
        il2cpp::Il2CppArrayList* freshList = reinterpret_cast<il2cpp::Il2CppArrayList*>(fresh);
        Expect(freshList->items == nullptr, "newobj path: fresh list starts with a null items array");
        il2cpp::il2cpp_arraylist_insert(fresh, 0, Str("i0"));
        Expect(il2cpp::il2cpp_arraylist_get_count(fresh) == 1, "fresh Insert(0) -> count 1");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(fresh, 0), "i0"),
               "fresh Insert(0) stores the element");
        il2cpp::il2cpp_arraylist_insert(fresh, 0, Str("i1")); // head into the fresh list
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(fresh, 0), "i1") &&
               IsContent(il2cpp::il2cpp_arraylist_get_item(fresh, 1), "i0"),
               "fresh head Insert shifts the survivor right");
        Expect(il2cpp::il2cpp_arraylist_get_count(fresh) == 2, "fresh list: 2 inserts -> count 2");
        // Insert(index == count) appends without shifting.
        il2cpp::il2cpp_arraylist_insert(fresh, 2, Str("i2"));
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(fresh, 2), "i2"),
               "fresh Insert(index==count) appends");
        // The lazy allocation also must not break growth on the same list.
        for (int32_t i = 3; i < 9; ++i)
        {
            il2cpp::il2cpp_arraylist_insert(fresh, i, Str("grow"));
        }
        Expect(il2cpp::il2cpp_arraylist_get_count(fresh) == 9, "fresh list grows through Insert");
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(fresh, 2), "i2"),
               "elements survive the fresh list's growth");
        // Fresh list via the create icall (items allocated) Insert(0) too.
        il2cpp::Il2CppObject* c = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_arraylist_insert(c, 0, Str("c0"));
        Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(c, 0), "c0"),
               "create-icall list Insert(0) works");
        Expect(il2cpp::il2cpp_arraylist_get_count(c) == 1, "create-icall list: count 1 after Insert");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arraylist assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arraylist tests passed\n");
    return 0;
}
