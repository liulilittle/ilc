//------------------------------------------------------------------------------
// test_tp_stubs.cpp -- ThreadPool internal-call STUB unit tests
// (src/runtime/src/stubs.cpp, declared in stubs.h).
//
// These icalls are BCL-infrastructure no-op stubs; the contract under test is
// exactly that: callable without crashing (they must never throw, abort or
// dereference -- they take no object state) and the hosted-pool probe reports
// "not hosted" (0), matching the simplified runtime's self-hosted pool.
//
// Coverage:
//   1. notify_work_item_complete          : single + repeated call, no crash.
//   2. notify_work_item_progress_native   : single + repeated call, no crash.
//   3. report_thread_status               : called across the int32 domain
//      edges (0, 1, -1, INT32_MAX, INT32_MIN), no crash for any status.
//   4. is_thread_pool_hosted              : returns 0 (pool is not externally
//      hosted) and is stable across repeated calls.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal. A stub
// that crashes fails harder: the process terminates and the exit code is
// observed as failure regardless of the harness.
//------------------------------------------------------------------------------

// stubs.h relies on the including TU to define the managed object types
// (its "../il2cpp_api.h" bridge does not declare them) -- pull the internal
// type headers first, exactly like stubs.cpp itself does.
#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "stubs.h"

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

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // 1. Work-item completion notification: a void no-op that must return
    //    normally; repeated calls exercise the same path (no state to corrupt).
    {
        icall_thread_pool_notify_work_item_complete();
        Expect(true, "notify_work_item_complete returned (single call)");
        icall_thread_pool_notify_work_item_complete();
        icall_thread_pool_notify_work_item_complete();
        Expect(true, "notify_work_item_complete returned (repeated calls)");
    }

    // 2. Work-item progress notification: same no-op contract.
    {
        icall_thread_pool_notify_work_item_progress_native();
        Expect(true, "notify_work_item_progress_native returned (single call)");
        icall_thread_pool_notify_work_item_progress_native();
        icall_thread_pool_notify_work_item_progress_native();
        Expect(true, "notify_work_item_progress_native returned (repeated calls)");
    }

    // 3. Thread-status report: consumes any int32 status without effect --
    //    probe the domain edges (zero, sign boundaries, extremes).
    {
        static const int32_t kStatuses[] = { 0, 1, -1, INT32_MAX, INT32_MIN };
        for (const int32_t status : kStatuses)
        {
            icall_thread_pool_report_thread_status(status);
        }
        Expect(true, "report_thread_status returned for all edge statuses");
    }

    // 4. Hosted-pool probe: the simplified runtime hosts its own pool, so the
    //    stub always answers "not hosted" (0); stable across calls.
    {
        Expect(icall_thread_pool_is_thread_pool_hosted() == 0,
               "is_thread_pool_hosted == 0");
        Expect(icall_thread_pool_is_thread_pool_hosted()
                   == icall_thread_pool_is_thread_pool_hosted(),
               "is_thread_pool_hosted stable across calls");
    }

    if (g_failures == 0)
    {
        std::printf("all threadpool stub tests passed\n");
        return 0;
    }
    std::printf("%d threadpool stub test(s) FAILED\n", g_failures);
    return 1;
}
