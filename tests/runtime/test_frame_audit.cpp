//------------------------------------------------------------------------------
// test_frame_audit.cpp -- frame-stack pairing-invariant audit test.
//
// Complements test_frame_deep.cpp (GC-root behaviour) by pinning the 1:1
// pairing contract itself (il2cpp_frame.h / framestack.cpp):
//
//   1. Empty-stack invariants: balance 0, top null, dump reports no frames.
//   2. Push/pop round-trip: top tracks the innermost frame, balance counts.
//   3. LIFO chain: 3 frames walk in reverse-push order; prev links form the
//      exact caller chain; balance == walked chain length.
//   4. Idempotent double-pop: a second pop of an already-popped frame is a
//      silent no-op (balance stays 0) -- the RAII destructor path.
//   5. RAII scope exit pops (no explicit pop); destructor after an explicit
//      pop is also an idempotent no-op.
//   6. Re-pushing a popped frame's storage slot is a FRESH pairing (legal) --
//      the push-side double-push guard must not false-positive on it.
//   7. Exception unwind: a throw across nested frames pops every frame LIFO
//      through the destructor; balance returns to 0, stack empty.
//   8. il2cpp_frame_dump diagnostics: label, balance, method names, innermost
//      first order; empty dump reports "(no managed frames)".
//   9. Crash probes (child process -- abort is loud): push(nullptr) aborts,
//      pop(nullptr) aborts, pop of a frame on the chain but not the top
//      aborts, and a DOUBLE push of the same frame aborts (1:1 pairing on
//      the push side; without it the chain becomes an unbreakable cycle).
//  10. Multi-threaded isolation: 4 threads push/pop 1000 frames each
//      concurrently; every thread sees exactly its own chain (top, balance,
//      walked depth all per-thread) and the main thread's stack is untouched.
//      Regresses the pre-fix shared global head (conccases C21 abort).
//
// Plain executable + minimal assertion harness (see test_frame_deep.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_frame.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
#include <thread>

#if defined(_WIN32)
#include <process.h>
#else
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

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

// Walked chain length (chain view of the balance -- must match the counters).
int ChainLength()
{
    int n = 0;
    for (il2cpp::Il2CppFrame* f = il2cpp::il2cpp_frame_top(); f != nullptr; f = f->prev)
    {
        ++n;
    }
    return n;
}

// Read the whole FILE* back after a dump (fflush + rewind) into a buffer.
bool ReadAll(FILE* f, char* buf, int bufSize)
{
    std::fflush(f);
    std::rewind(f);
    const size_t n = std::fread(buf, 1, static_cast<size_t>(bufSize - 1), f);
    buf[n] = '\0';
    return n > 0;
}

//------------------------------------------------------------------------------
// Crash-probe child process. The abort paths (null push/pop, non-top pop,
// double push) kill the process -- the only way to observe them is a child
// process whose non-zero exit code proves the abort fired. argv0 is the test
// executable; 'probe' selects the branch (see RunProbe).
//------------------------------------------------------------------------------
int RunCrashProbe(const char* argv0, const char* probe)
{
#if defined(_WIN32)
    const int code = _spawnl(_P_WAIT, argv0, argv0, probe, static_cast<char*>(nullptr));
    return code; // child exit code (3 on abort via MSVCRT); -1 if the spawn itself failed
#else
    const pid_t pid = fork();
    if (pid == 0)
    {
        execl(argv0, argv0, probe, static_cast<char*>(nullptr));
        _exit(127); // exec failed
    }
    int status = 0;
    waitpid(pid, &status, 0);
    if (WIFSIGNALED(status))
    {
        return 128 + WTERMSIG(status); // abort() -> SIGABRT (6) -> 134
    }
    if (WIFEXITED(status))
    {
        return WEXITSTATUS(status);
    }
    return 1;
#endif
}

// One crash probe run. Reached only when the expected abort did NOT fire.
int RunProbe(const char* probe)
{
    il2cpp::Il2CppFrame a = { nullptr, "probe.A", nullptr, 0 };
    il2cpp::Il2CppFrame b = { nullptr, "probe.B", nullptr, 0 };
    if (std::strcmp(probe, "probe-push-null") == 0)
    {
        il2cpp::il2cpp_frame_push(nullptr); // abort: null frame
        return 0;
    }
    if (std::strcmp(probe, "probe-pop-null") == 0)
    {
        il2cpp::il2cpp_frame_pop(nullptr); // abort: null frame
        return 0;
    }
    if (std::strcmp(probe, "probe-pop-nontop") == 0)
    {
        il2cpp::il2cpp_frame_push(&a);
        il2cpp::il2cpp_frame_push(&b);
        il2cpp::il2cpp_frame_pop(&a); // a is on the chain but not the top -> abort
        return 0;
    }
    if (std::strcmp(probe, "probe-push-twice") == 0)
    {
        il2cpp::il2cpp_frame_push(&a);
        il2cpp::il2cpp_frame_push(&a); // already on the chain -> abort (1:1 pairing)
        return 0;
    }
    return 0;
}

//------------------------------------------------------------------------------
// Audit sections (all clean up so the balance returns to 0).
//------------------------------------------------------------------------------

// 7. Exception unwind: every nested frame pops through its destructor.
void ThrowWithFrame(int n)
{
    il2cpp::Il2CppFrame frame = { nullptr, "test_audit_throw", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&frame);
    if (n == 0)
    {
        throw std::runtime_error("frame audit unwind");
    }
    ThrowWithFrame(n - 1);
    il2cpp::il2cpp_frame_pop(&frame); // never reached on the throw path (RAII pops)
}

void ExceptionUnwindAudit()
{
    bool caught = false;
    try
    {
        ThrowWithFrame(5);
    }
    catch (const std::runtime_error&)
    {
        caught = true;
    }
    Expect(caught, "exception propagates through nested audit frames");
    Expect(il2cpp::il2cpp_frame_balance() == 0, "RAII unwind restores the balance to 0");
    Expect(il2cpp::il2cpp_frame_top() == nullptr, "frame stack is empty after the unwind");
}

// 5. RAII scope exit and destructor-after-explicit-pop idempotency.
void RaiiScopedUnwind()
{
    {
        il2cpp::Il2CppFrame f = { nullptr, "test_audit_raii", nullptr, 0 };
        il2cpp::il2cpp_frame_push(&f);
        Expect(il2cpp::il2cpp_frame_balance() == 1 && il2cpp::il2cpp_frame_top() == &f,
               "frame is live inside the RAII scope");
    } // destructor pops it -- no explicit pop
    Expect(il2cpp::il2cpp_frame_balance() == 0 && il2cpp::il2cpp_frame_top() == nullptr,
           "RAII scope exit pops the frame");

    il2cpp::Il2CppFrame f = { nullptr, "test_audit_popdtor", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&f);
    il2cpp::il2cpp_frame_pop(&f); // explicit pop
    Expect(il2cpp::il2cpp_frame_balance() == 0, "explicit pop empties the stack");
    // f goes out of scope here -> destructor pops again -> idempotent no-op.
    Expect(il2cpp::il2cpp_frame_balance() == 0 && il2cpp::il2cpp_frame_top() == nullptr,
           "destructor after an explicit pop is an idempotent no-op");
}

// 10. Multi-threaded isolation: each thread owns its OWN frame chain. 4
// threads push/pop 1000 frames each, concurrently. With the pre-fix shared
// GLOBAL head the chains interleave (a worker's push splices into the chain
// of whichever thread happens to be pushing at that moment), and the next
// pop aborts "frame ... is not the current top" -- a process kill. The
// thread_local head makes each chain private: every thread sees exactly its
// own 1000 frames (top == innermost, balance == 1000 == walked depth), and
// the main thread's stack stays untouched throughout.
void ThreadWorker(int id, int* failures)
{
    il2cpp::Il2CppFrame frames[1000] = {};
    for (int i = 0; i < 1000; ++i)
    {
        frames[i].methodName = "test_audit_thread";
        il2cpp::il2cpp_frame_push(&frames[i]);
        if (il2cpp::il2cpp_frame_top() != &frames[i])
        {
            std::printf("FAIL: thread %d: pushed frame is the per-thread top\n", id);
            ++*failures;
        }
    }
    int depth = 0;
    for (il2cpp::Il2CppFrame* f = il2cpp::il2cpp_frame_top(); f != nullptr; f = f->prev)
    {
        ++depth;
    }
    if (il2cpp::il2cpp_frame_balance() != 1000)
    {
        std::printf("FAIL: thread %d: balance is 1000 after 1000 pushes\n", id);
        ++*failures;
    }
    if (depth != 1000)
    {
        std::printf("FAIL: thread %d: per-thread chain length is 1000\n", id);
        ++*failures;
    }
    for (int i = 999; i >= 0; --i)
    {
        il2cpp::il2cpp_frame_pop(&frames[i]);
    }
    if (il2cpp::il2cpp_frame_balance() != 0 || il2cpp::il2cpp_frame_top() != nullptr)
    {
        std::printf("FAIL: thread %d: per-thread stack empties after 1000 pops\n", id);
        ++*failures;
    }
}

void MultiThreadedIsolation()
{
    int workerFailures[4] = { 0, 0, 0, 0 };
    std::thread t0(ThreadWorker, 0, &workerFailures[0]);
    std::thread t1(ThreadWorker, 1, &workerFailures[1]);
    std::thread t2(ThreadWorker, 2, &workerFailures[2]);
    std::thread t3(ThreadWorker, 3, &workerFailures[3]);
    t0.join();
    t1.join();
    t2.join();
    t3.join();
    for (int i = 0; i < 4; ++i)
    {
        g_failures += workerFailures[i];
    }
    // The main thread never pushed: its own chain must be untouched by the
    // workers' concurrent push/pop (proves the chains are thread-local).
    Expect(il2cpp::il2cpp_frame_balance() == 0 && il2cpp::il2cpp_frame_top() == nullptr,
           "main thread's frame stack is isolated from worker threads");
}

// 8. il2cpp_frame_dump diagnostics.
void DumpCapture()
{
    char buf[4096];
#if defined(_MSC_VER)
    FILE* f = nullptr;
    ::tmpfile_s(&f);
#else
    FILE* f = std::tmpfile();
#endif
    Expect(f != nullptr, "tmpfile for dump capture");
    if (f == nullptr)
    {
        return;
    }
    il2cpp::Il2CppFrame a = { nullptr, "test_audit_a", nullptr, 0 };
    il2cpp::Il2CppFrame b = { nullptr, "test_audit_b", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&a);
    il2cpp::il2cpp_frame_push(&b);
    il2cpp::il2cpp_frame_dump(f, "audit-dump");
    Expect(ReadAll(f, buf, sizeof(buf)), "dump output is non-empty");
    Expect(std::strstr(buf, "audit-dump") != nullptr, "dump contains the label");
    Expect(std::strstr(buf, "balance 2") != nullptr, "dump contains the counter balance");
    const char* pB = std::strstr(buf, "test_audit_b");
    const char* pA = std::strstr(buf, "test_audit_a");
    Expect(pB != nullptr && pA != nullptr, "dump lists both frame method names");
    Expect(pB != nullptr && pA != nullptr && pB < pA,
           "dump lists the innermost frame before the outer frame");
    il2cpp::il2cpp_frame_pop(&b);
    il2cpp::il2cpp_frame_pop(&a);

    std::rewind(f);
    il2cpp::il2cpp_frame_dump(f, "empty-dump");
    Expect(ReadAll(f, buf, sizeof(buf)), "empty dump output is non-empty");
    Expect(std::strstr(buf, "no managed frames") != nullptr,
           "empty dump reports no managed frames");
    Expect(std::strstr(buf, "balance 0") != nullptr, "empty dump reports balance 0");
    std::fclose(f);
}

} // namespace

//------------------------------------------------------------------------------
// main -- child-probe mode (argc > 1) or the audit suite.
//------------------------------------------------------------------------------
int main(int argc, char** argv)
{
    // Unbuffered stdio: a crash (or redirected output) must not swallow the
    // diagnostics -- setvbuf(_IONBF) keeps every checkpoint visible.
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    std::setvbuf(stderr, nullptr, _IONBF, 0);

    if (argc > 1)
    {
        return RunProbe(argv[1]); // crash-probe child mode
    }

    // --- 1. Empty-stack invariants ---
    Expect(il2cpp::il2cpp_frame_balance() == 0, "balance is 0 before any push");
    Expect(il2cpp::il2cpp_frame_top() == nullptr, "top is null before any push");

    // --- 2. Push/pop round-trip ---
    il2cpp::Il2CppFrame a = { nullptr, "test_audit_a", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&a);
    Expect(il2cpp::il2cpp_frame_top() == &a, "pushed frame becomes the top");
    Expect(il2cpp::il2cpp_frame_balance() == 1, "balance counts the pushed frame");
    il2cpp::il2cpp_frame_pop(&a);
    Expect(il2cpp::il2cpp_frame_top() == nullptr, "pop returns the stack to empty");
    Expect(il2cpp::il2cpp_frame_balance() == 0, "pop restores the balance to 0");

    // --- 3. LIFO chain: order, prev links, balance == walked length ---
    il2cpp::Il2CppFrame f1 = { nullptr, "test_audit_f1", nullptr, 0 };
    il2cpp::Il2CppFrame f2 = { nullptr, "test_audit_f2", nullptr, 0 };
    il2cpp::Il2CppFrame f3 = { nullptr, "test_audit_f3", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&f1);
    il2cpp::il2cpp_frame_push(&f2);
    il2cpp::il2cpp_frame_push(&f3);
    Expect(il2cpp::il2cpp_frame_top() == &f3, "innermost frame is the top");
    Expect(f3.prev == &f2 && f2.prev == &f1 && f1.prev == nullptr,
           "prev links form the exact caller chain");
    Expect(il2cpp::il2cpp_frame_balance() == 3 && ChainLength() == 3,
           "balance matches the walked chain length (3 frames)");
    il2cpp::il2cpp_frame_pop(&f3);
    Expect(il2cpp::il2cpp_frame_top() == &f2 && il2cpp::il2cpp_frame_balance() == 2,
           "pop removes only the top (f2 exposed)");
    il2cpp::il2cpp_frame_pop(&f2);
    Expect(il2cpp::il2cpp_frame_balance() == 1 && ChainLength() == 1,
           "balance matches the walked chain length (1 frame)");
    il2cpp::il2cpp_frame_pop(&f1);
    Expect(il2cpp::il2cpp_frame_balance() == 0 && ChainLength() == 0,
           "all frames popped: balance 0, chain empty");

    // --- 4. Idempotent double-pop ---
    il2cpp::Il2CppFrame d = { nullptr, "test_audit_d", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&d);
    il2cpp::il2cpp_frame_pop(&d);
    il2cpp::il2cpp_frame_pop(&d); // already popped: silent no-op
    Expect(il2cpp::il2cpp_frame_balance() == 0 && il2cpp::il2cpp_frame_top() == nullptr,
           "second pop of an already-popped frame is an idempotent no-op");

    // --- 5. RAII scope exit + destructor-after-explicit-pop ---
    RaiiScopedUnwind();

    // --- 6. Re-pushing a popped frame's storage slot (fresh pairing) ---
    for (int i = 0; i < 3; ++i)
    {
        il2cpp::Il2CppFrame r = { nullptr, "test_audit_reuse", nullptr, 0 };
        il2cpp::il2cpp_frame_push(&r);
        il2cpp::il2cpp_frame_pop(&r);
    }
    Expect(il2cpp::il2cpp_frame_balance() == 0 && il2cpp::il2cpp_frame_top() == nullptr,
           "re-pushing a popped frame slot is a fresh, balanced pairing");

    // --- 7. Exception unwind ---
    ExceptionUnwindAudit();

    // --- 8. Diagnostic dump ---
    DumpCapture();

    // --- 10. Multi-threaded isolation (per-thread chains) ---
    MultiThreadedIsolation();

    // --- 9. Crash probes (child processes) ---
    // Every probe must exit NON-zero (abort fired). A zero exit means the
    // probe ran to completion and the expected abort silently did not fire.
    Expect(RunCrashProbe(argv[0], "probe-push-null") != 0,
           "push(null) aborts loudly (child exits non-zero)");
    Expect(RunCrashProbe(argv[0], "probe-pop-null") != 0,
           "pop(null) aborts loudly (child exits non-zero)");
    Expect(RunCrashProbe(argv[0], "probe-pop-nontop") != 0,
           "pop of a non-top frame aborts loudly (child exits non-zero)");
    Expect(RunCrashProbe(argv[0], "probe-push-twice") != 0,
           "double push of the same frame aborts loudly (child exits non-zero)");

    // --- Report ---
    Expect(il2cpp::il2cpp_frame_balance() == 0, "audit leaves a balanced frame stack");
    if (g_failures != 0)
    {
        std::printf("%d frame-audit assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime frame-audit tests passed\n");
    return 0;
}
