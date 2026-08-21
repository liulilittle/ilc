//------------------------------------------------------------------------------
// test_threadext.cpp -- System.Threading.Thread icall EXTENSIONS runtime
// unit tests (see threadext.h for the contract under test).
//
// Coverage (normal + edge + error paths):
//   1. join(current id)   : returns immediately (elapsed stays far below the
//      sleep floor; the single-threaded model never blocks).
//   2. join(arbitrary id) : also returns immediately (thread 123456 does not
//      exist, nothing to wait for).
//   3. set_priority       : any value (0, 4, -1, INT32_MAX) is accepted
//      without crashing and without changing the reported priority --
//      get_priority stays 0 (ThreadPriority.Normal).
//   4. is_alive(current)  : true (the running thread reports alive).
//   5. is_alive(other)    : false (no such thread exists in the model).
//   6. is_alive stability : two consecutive calls on the current id agree.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "threadext.h"

#include "il2cpp_thread.h" // icall_thread_get_current_thread_id/get_priority

#include <chrono>
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

// Elapsed milliseconds of 'fn' (steady clock; coarse floors only, so timer
// noise is irrelevant).
int64_t ElapsedMs(void (*fn)())
{
    const auto start = std::chrono::steady_clock::now();
    fn();
    const auto end = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    const int32_t self_id = icall_thread_get_current_thread_id();

    // --- 1. join(current id) returns immediately (never blocks) ---
    Expect(ElapsedMs([]() {
               il2cpp_thread_join(icall_thread_get_current_thread_id());
           }) < 45,
           "join(current id) does not block (elapsed < 45 ms)");

    // --- 2. join(arbitrary id) also returns immediately ---
    Expect(ElapsedMs([]() { il2cpp_thread_join(123456); }) < 45,
           "join(123456) does not block (elapsed < 45 ms)");

    // --- 3. set_priority accepts any value without crashing; get stays 0 ---
    il2cpp_thread_set_priority(0);
    il2cpp_thread_set_priority(4);   // net48 ThreadPriority.Highest
    il2cpp_thread_set_priority(-1);  // out of net48 enum range (accepted)
    il2cpp_thread_set_priority(INT32_MAX);
    Expect(icall_thread_get_priority() == 0,
           "priority stays 0 (ThreadPriority.Normal) after set_priority");

    // --- 4. is_alive: the current thread is alive ---
    Expect(il2cpp_thread_get_is_alive(self_id),
           "is_alive(current id) == true");

    // --- 5. is_alive: any other id is not alive (no such thread) ---
    Expect(!il2cpp_thread_get_is_alive(123456),
           "is_alive(123456) == false");
    Expect(!il2cpp_thread_get_is_alive(0),
           "is_alive(0) == false");

    // --- 6. is_alive stability for the current id ---
    Expect(il2cpp_thread_get_is_alive(self_id) ==
               il2cpp_thread_get_is_alive(self_id),
           "is_alive(current id) is stable across calls");

    if (g_failures == 0)
    {
        std::printf("all runtime threadext tests passed\n");
        return 0;
    }
    std::printf("%d threadext test(s) FAILED\n", g_failures);
    return 1;
}
