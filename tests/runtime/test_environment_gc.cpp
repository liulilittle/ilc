//------------------------------------------------------------------------------
// test_environment_gc.cpp -- Unit tests for P6 environment + GC icalls
// implemented during the MethodInternalCall audit campaign.
//
// Tests: TickCount64, CurrentManagedThreadId, FailFast (existence check),
// String.IsInterned, GC.GetTotalAllocatedBytes, GC.GetSegmentSize.
//------------------------------------------------------------------------------
#include "il2cpp_environment.h"
#include "il2cpp_gc.h"
#include "il2cpp_gc_api.h"
#include "il2cpp_string_intern.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>

// environment: il2cpp::runtime; gc: il2cpp; string: il2cpp::runtime
using namespace il2cpp::runtime;
using namespace il2cpp;

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

} // anonymous namespace

int main()
{
    // --- P6: Environment.TickCount64 ---
    {
        const int64_t tc = icall_environment_get_tick_count64();
        Expect(tc > 0, "TickCount64 > 0");
    }

    // --- P6: Environment.CurrentManagedThreadId ---
    {
        const int32_t tid = icall_environment_get_current_managed_thread_id();
        Expect(tid >= 0, "CurrentManagedThreadId >= 0");
    }

    // --- P6: GC.GetTotalAllocatedBytes ---
    {
        const int64_t bytes = il2cpp_gc_get_total_allocated_bytes_approximate();
        Expect(bytes > 0, "GetTotalAllocatedBytes > 0");
    }

    // --- P6: GC.GetSegmentSize ---
    {
        const uint64_t segSize = il2cpp_gc_get_segment_size();
        Expect(segSize > 0, "GetSegmentSize > 0");
    }

    // --- P6: GC.GetMaxGeneration ---
    {
        const int32_t maxGen = il2cpp_gc_get_max_generation();
        Expect(maxGen >= 0, "GetMaxGeneration >= 0");
    }

    // --- P6: String.IsInterned ---
    {
        Il2CppString* str = il2cpp_string_new_from_cs("P6_test_string_not_interned");
        Il2CppString* interned = reinterpret_cast<Il2CppString*>(
            il2cpp_string_is_interned(reinterpret_cast<Il2CppObject*>(str)));
        Expect(interned == nullptr, "IsInterned returns nullptr for non-interned string");

        Il2CppString* s = il2cpp_string_new_from_cs("P6_test_string_to_intern");
        il2cpp_string_intern(reinterpret_cast<Il2CppObject*>(s));
        Il2CppString* found = reinterpret_cast<Il2CppString*>(
            il2cpp_string_is_interned(reinterpret_cast<Il2CppObject*>(s)));
        Expect(found != nullptr, "IsInterned returns non-null for interned string");
    }

    // --- P6: Environment.FailFast (existence check only -- cannot test abort) ---
    {
        Expect(icall_environment_fail_fast != nullptr, "FailFast function pointer exists");
    }

    if (g_failures != 0)
    {
        std::printf("%d environment/gc assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime environment/gc tests passed\n");
    return 0;
}
