//------------------------------------------------------------------------------
// test_g4slvals_ienum.cpp -- g4sortedlist2vals regression spec.
//
// The P3-extra SortedList<string,int> Values view is the list object itself;
// a generic `foreach (int v in sl.Values)` reaches the runtime GENERIC
// interface dispatch (il2cpp_ienum_get_enumerator with the SortedList klass)
// and the i4 carrier. Before the fix that dispatch throws
// NotSupportedException ("no reflected virtual member"); the fix forwards:
//   il2cpp_ienum_get_enumerator(SortedList klass)
//     -> icall_sortedlist_si_values_get_enumerator
//   il2cpp_ienum_get_current_i4(SortedList snapshot enumerator klass)
//     -> icall_sortedlist_si_enumerator_get_current
//
// This executable fails on the pre-fix runtime and passes on the fixed one.
//------------------------------------------------------------------------------
#include "arraysort.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "sortedlist.h"
#include "sortedlistint_obj.h"

using il2cpp::Il2CppObject;

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

il2cpp::Il2CppObject* S(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

} // namespace

int main()
{
    il2cpp::Il2CppObject* list = il2cpp::il2cpp_object_new(il2cpp::il2cpp_sortedlist_class());
    il2cpp::runtime::il2cpp_sortedlist_create(list, 0);
    il2cpp::runtime::icall_sortedlist_si_add(list, S("b"), 2);
    il2cpp::runtime::icall_sortedlist_si_add(list, S("a"), 1);
    il2cpp::runtime::icall_sortedlist_si_add(list, S("c"), 3);

    // 1. Generic IEnumerable<int>.GetEnumerator dispatch on the Values view
    //    (the receiver is the list object itself).
    Il2CppObject* e = nullptr;
    try
    {
        e = il2cpp_ienum_get_enumerator(list);
    }
    catch (const il2cpp::DotNetException& ex)
    {
        std::printf("FAIL: generic GetEnumerator dispatch threw %s\n", ex.type_name());
        ++g_failures;
    }
    if (e != nullptr)
    {
        Expect(il2cpp_ienum_move_next(e), "move_next #1 returns true");
        ExpectI32(il2cpp_ienum_get_current_i4(e), 1, "values enumerate ascending (1)");
        Expect(il2cpp_ienum_move_next(e), "move_next #2 returns true");
        ExpectI32(il2cpp_ienum_get_current_i4(e), 2, "values enumerate ascending (2)");
        Expect(il2cpp_ienum_move_next(e), "move_next #3 returns true");
        ExpectI32(il2cpp_ienum_get_current_i4(e), 3, "values enumerate ascending (3)");
        Expect(!il2cpp_ienum_move_next(e), "move_next #4 returns false");
        il2cpp_ienum_reset(e);
        Expect(il2cpp_ienum_move_next(e), "reset restarts the enumeration");
        ExpectI32(il2cpp_ienum_get_current_i4(e), 1, "values enumerate ascending after reset");
        il2cpp_ienum_dispose(e); // no-op (not IDisposable)
    }

    // 2. The concrete wrapper path still works (regression).
    Il2CppObject* e2 = il2cpp::runtime::icall_sortedlist_si_values_get_enumerator(list);
    Expect(e2 != nullptr, "concrete values enumerator is non-null");
    if (e2 != nullptr)
    {
        Expect(il2cpp_ienum_move_next(e2), "concrete move_next #1");
        ExpectI32(il2cpp::runtime::icall_sortedlist_si_enumerator_get_current(e2), 1,
                  "concrete get_current #1");
        il2cpp_ienum_dispose(e2);
    }

    // 3. The non-generic Snapshot path (S() keys) is untouched: the same
    //    enumerator klass via the array route keeps yielding refs.
    Il2CppObject* keys = il2cpp::runtime::icall_sortedlist_si_get_keys(list);
    Il2CppObject* ek = il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(keys);
    Expect(il2cpp_ienum_move_next(ek), "keys snapshot move_next");
    Expect(il2cpp_ienum_get_current_ref(ek) != nullptr, "keys snapshot get_current_ref non-null");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("FAILURES: %d\n", g_failures);
    return 1;
}
