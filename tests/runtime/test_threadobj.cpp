//------------------------------------------------------------------------------
// test_threadobj.cpp -- System.Threading.Thread OBJECT model runtime tests
// (see threadobj.h for the contract under test).
//
// Coverage (normal + edge + error paths), net48 oracle expectations
// (tests/cases/threadcase/expected.txt -- the e2e probes print True):
//   1. Descriptor identity/layout: il2cpp_threadobj_class() ==
//      &System_Threading_Thread_class, instanceSize == sizeof(
//      Il2CppThreadObject), by-name resolution, IS-A System.Object.
//   2. ctor validation: null callback -> ArgumentNullException; a multicast
//      delegate -> InvalidOperationException (delegatecb policy).
//   3. NEW-state probes: IsAlive false, ManagedThreadId 0, Join ->
//      ThreadStateException.
//   4. Start -> the child runs on a REAL OS thread (the callback writes a
//      static counter, sleeps 50 ms, writes again); IsAlive true while it
//      runs; Join blocks >= 45 ms (the sleep floor), the second write is
//      visible after Join (happens-before through plat_thread_join); the
//      child's OS id differs from the main thread's and is nonzero.
//   5. Lifecycle errors: a second Start -> ThreadStateException; Start
//      after Join -> ThreadStateException; Join on the current thread ->
//      ThreadStateException.
//   6. CurrentThread: Priority == 0 (Normal), ManagedThreadId == the
//      current OS id, IsAlive true.
//   7. set_Priority accepts ANY value; the getter stays 0.
//   8. IsBackground: default false; set/get round-trips (NEW, CURRENT
//      objects); the net48 STOPPED-thread guard (rtaud11 probe): the setter
//      on a FINISHED thread (joined OR merely completed) throws
//      ThreadStateException ("Thread is dead; state cannot be accessed."),
//      while NEW / RUNNING / CURRENT threads accept the set.
//   9. Thread.Name (D-THR-1): descriptor refFieldBitmap bit 5 roots the name;
//      null default; set/get content round-trip; a second non-empty set
//      throws InvalidOperationException (net48: name may only be set once);
//      null/empty CLEARS (rename possible after a clear); the name survives
//      Start/Join; CurrentThread object defaults null and round-trips.
//
// Plain executable + minimal assertion harness (see test_versionobj.cpp).
//------------------------------------------------------------------------------

#include "threadobj.h"

#include "il2cpp_delegate.h"   // il2cpp_delegate_new
#include "il2cpp_delegate_ext.h" // icall_delegate_combine (multicast probe)
#include "il2cpp_exception.h"  // DotNetException carrier
#include "il2cpp_runtime.h"    // il2cpp_is_instance / il2cpp_class_by_name
#include "il2cpp_string.h"     // il2cpp_string_new_from_cs (Name round-trip)
#include "il2cpp_thread.h"     // icall_thread_sleep (child callback)
#include "platform.h"          // plat_thread_current_id

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>

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

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedChain'.
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedChain)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

const char* kArgNullExceptionChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char* kInvalidOperationChain =
    "System.InvalidOperationException,System.Exception";
const char* kThreadStateChain =
    "System.Threading.ThreadStateException,System.Threading.ThreadStateException,System.SystemException,System.Exception";
// il2cpp_throw_invalid_operation_exception (exceptionext.cpp) carries the
// FULL chain -- unlike delegatecb's raw short chain (kInvalidOperationChain
// above, which only the ctor multicast probe matches).
const char* kInvalidOpFullChain =
    "System.InvalidOperationException,System.InvalidOperationException,System.SystemException,System.Exception";

// Managed string <-> ASCII comparison helper (the Name round-trip compares
// content, not identity -- each get returns the stored reference).
bool StrEquals(il2cpp::Il2CppString* str, const char* ascii)
{
    if (str == nullptr)
    {
        return false;
    }
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// The ThreadStart callback: writes a static counter, sleeps 50 ms (the
// join-wait floor), writes again. Allocation-free -- the M-stage allocator
// is single-threaded, so the child must not touch the managed heap.
int g_childRan = 0;

void ChildCallback()
{
    g_childRan = 1;
    il2cpp::runtime::icall_thread_sleep(50);
    g_childRan = 2;
}

template <typename Fn>
int64_t ElapsedMs(Fn&& fn)
{
    const auto start = std::chrono::steady_clock::now();
    fn();
    const auto end = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
}

// A Thread object in the NEW state with a valid single-cast static-method
// delegate (mirrors the generated `new Thread(Worker)` shape).
il2cpp::Il2CppObject* NewThreadObject()
{
    using namespace il2cpp;
    Il2CppObject* t = il2cpp_object_new(&System_Threading_Thread_class);
    Il2CppObject* d = reinterpret_cast<Il2CppObject*>(il2cpp_delegate_new(
        reinterpret_cast<void*>(&ChildCallback)));
    runtime::icall_threadobj_ctor(t, d);
    return t;
}

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. descriptor identity + layout ---
    Expect(il2cpp_threadobj_class() == &System_Threading_Thread_class,
           "il2cpp_threadobj_class() returns &System_Threading_Thread_class");
    Expect(System_Threading_Thread_class.instanceSize == sizeof(Il2CppThreadObject),
           "System_Threading_Thread_class.instanceSize == sizeof(Il2CppThreadObject)");
    Expect(il2cpp_class_by_name("System.Threading.Thread") == &System_Threading_Thread_class,
           "System.Threading.Thread resolves by name (registered)");
    Il2CppObject* cur = icall_threadobj_get_current_thread();
    Expect(cur->klass == &System_Threading_Thread_class,
           "CurrentThread object klass stamped");
    Expect(il2cpp_is_instance(cur, &System_Object_class),
           "a Thread IS-A System.Object (baseClass chain)");

    // --- 2. ctor validation ---
    Expect(ThrowsDotNet([&] {
               icall_threadobj_ctor(reinterpret_cast<Il2CppObject*>(
                                        il2cpp_object_new(&System_Threading_Thread_class)),
                                    nullptr);
           }, kArgNullExceptionChain),
           "ctor(null) throws ArgumentNullException");
    {
        Il2CppObject* d1 = reinterpret_cast<Il2CppObject*>(il2cpp_delegate_new(
            reinterpret_cast<void*>(&ChildCallback)));
        Il2CppObject* d2 = reinterpret_cast<Il2CppObject*>(il2cpp_delegate_new(
            reinterpret_cast<void*>(&ChildCallback)));
        Il2CppObject* multi = icall_delegate_combine(d1, d2);
        Expect(ThrowsDotNet([&] {
                   icall_threadobj_ctor(reinterpret_cast<Il2CppObject*>(
                                            il2cpp_object_new(&System_Threading_Thread_class)),
                                        multi);
               }, kInvalidOperationChain),
               "ctor(multicast delegate) throws InvalidOperationException");
    }

    // --- 3. NEW-state probes ---
    Il2CppObject* t = NewThreadObject();
    Expect(!icall_threadobj_get_is_alive(t), "IsAlive before Start == false");
    Expect(icall_threadobj_get_managed_thread_id(t) != 0,
           "ManagedThreadId assigned at construction (net48: never 0, even before Start)");
    Expect(ThrowsDotNet([&] { icall_threadobj_join(t); }, kThreadStateChain),
           "Join before Start throws ThreadStateException");

    // --- 4. Start -> real thread -> Join ---
    const int32_t mainId = plat_thread_current_id();
    Expect(mainId != 0, "main OS thread id is nonzero");
    icall_threadobj_start(t);
    Expect(icall_threadobj_get_is_alive(t), "IsAlive after Start == true");
    // ManagedThreadId is set by the CHILD at its entry (0 until then --
    // documented in threadobj.h), so the nonzero probes below run after
    // Join where the value is deterministic.
    const int64_t joinMs = ElapsedMs([&] { icall_threadobj_join(t); });
    Expect(joinMs >= 45, "Join waited >= 45 ms (child slept 50)");
    Expect(g_childRan == 2, "child callback ran to completion (counter 2)");
    Expect(!icall_threadobj_get_is_alive(t), "IsAlive after Join == false");
    // net48: ManagedThreadId stays queryable AFTER the thread ends (Join
    // preserves the child's OS id on the object).
    Expect(icall_threadobj_get_managed_thread_id(t) != 0,
           "child ManagedThreadId stays nonzero after Join");
    Expect(icall_threadobj_get_managed_thread_id(t) != mainId,
           "child OS id differs from the main thread's id");

    // --- 5. lifecycle errors ---
    Expect(ThrowsDotNet([&] { icall_threadobj_start(t); }, kThreadStateChain),
           "second Start throws ThreadStateException");
    Expect(ThrowsDotNet([&] { icall_threadobj_join(icall_threadobj_get_current_thread()); }, kThreadStateChain),
           "joining the current thread throws ThreadStateException (self-join)");

    // A second Join after finish returns immediately (net48).
    Expect(ElapsedMs([&] { icall_threadobj_join(t); }) < 45,
           "second Join after finish returns immediately");

    // Start after Join throws ThreadStateException (the thread ran once).
    Il2CppObject* t2 = NewThreadObject();
    icall_threadobj_start(t2);
    icall_threadobj_join(t2);
    Expect(ThrowsDotNet([&] { icall_threadobj_start(t2); }, kThreadStateChain),
           "Start after Join throws ThreadStateException");

    // --- 6. CurrentThread object ---
    // ThreadPriority.Normal == 2 (net48 enum value), not 0.
    Expect(icall_threadobj_get_priority(cur) == 2,
           "CurrentThread.Priority == 2 (Normal)");
    Expect(icall_threadobj_get_managed_thread_id(cur) == 1,
           "CurrentThread.ManagedThreadId == 1 (main thread managed id; net48 contract)");
    Expect(icall_threadobj_get_is_alive(cur), "CurrentThread.IsAlive == true");

    // --- 7. priority setter is accepted and ignored ---
    icall_threadobj_set_priority(cur, 0);
    icall_threadobj_set_priority(cur, 4);    // net48 ThreadPriority.Highest
    icall_threadobj_set_priority(cur, -1);   // out of net48 range (accepted)
    icall_threadobj_set_priority(cur, INT32_MAX);
    Expect(icall_threadobj_get_priority(cur) == 2,
           "priority stays 2 (Normal) after set_priority");

    // --- 8. IsBackground flag round-trip + net48 STOPPED-thread guard ---
    // NEW thread: default false (foreground); set/get round-trips. NEW
    // threads accept the set (net48: unstarted_set=True).
    Il2CppObject* bg = NewThreadObject();
    Expect(!icall_threadobj_get_is_background(bg),
           "IsBackground before set == false (foreground default)");
    icall_threadobj_set_is_background(bg, true);
    Expect(icall_threadobj_get_is_background(bg),
           "IsBackground == true after set(true)");
    icall_threadobj_set_is_background(bg, false);
    Expect(!icall_threadobj_get_is_background(bg),
           "IsBackground == false after set(false)");
    // The CURRENT thread object: same default + round-trip (net48:
    // current_set ok -- the current thread always accepts the set).
    Expect(!icall_threadobj_get_is_background(cur),
           "CurrentThread.IsBackground == false by default");
    icall_threadobj_set_is_background(cur, true);
    Expect(icall_threadobj_get_is_background(cur),
           "CurrentThread.IsBackground == true after set(true)");
    // net48 STOPPED guard, FINISHED-but-not-joined path: a thread whose
    // callback has completed (handle atomic state == 2) but that has NOT
    // been joined yet rejects the setter (probe: finished_set=THROWS).
    {
        Il2CppObject* done = NewThreadObject();
        icall_threadobj_start(done);
        const auto deadline = std::chrono::steady_clock::now()
                              + std::chrono::seconds(5);
        bool finished = false;
        while (!finished && std::chrono::steady_clock::now() < deadline)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            finished = !icall_threadobj_get_is_alive(done);
        }
        Expect(finished, "finished-not-joined child reports IsAlive false");
        Expect(ThrowsDotNet([&] {
                   icall_threadobj_set_is_background(done, true);
               }, kThreadStateChain),
               "set_IsBackground on a finished-not-joined thread throws ThreadStateException");
        icall_threadobj_join(done); // clean up the handle
    }
    // net48 STOPPED guard, JOINED path (probe: bg_set_after_join=THROWS):
    // setting IsBackground on a thread whose handle Join already released
    // throws ThreadStateException ("Thread is dead; state cannot be
    // accessed.").
    Il2CppObject* bg2 = NewThreadObject();
    icall_threadobj_start(bg2);
    icall_threadobj_join(bg2);
    Expect(ThrowsDotNet([&] {
               icall_threadobj_set_is_background(bg2, true);
           }, kThreadStateChain),
           "set_IsBackground after Join throws ThreadStateException (stopped thread)");

    // --- 9. Thread.Name (D-THR-1 closure) ---
    // The descriptor's refFieldBitmap bit 5 roots the name string (the GC
    // scans and moves it with the object).
    Expect((System_Threading_Thread_class.refFieldBitmap & (1u << 5)) != 0,
           "Thread descriptor refFieldBitmap bit 5 marks the name GC ref");
    // NEW thread: name is null by default; set/get round-trips content.
    Il2CppObject* named = NewThreadObject();
    Expect(icall_threadobj_get_name(named) == nullptr,
           "Name before set == null");
    icall_threadobj_set_name(named, il2cpp_string_new_from_cs("worker"));
    Expect(StrEquals(icall_threadobj_get_name(named), "worker"),
           "Name == \"worker\" after set");
    // net48: a thread's Name may only be set once (non-null non-empty).
    Expect(ThrowsDotNet([&] {
               icall_threadobj_set_name(named, il2cpp_string_new_from_cs("second"));
           }, kInvalidOpFullChain),
           "set_Name again throws InvalidOperationException");
    // net48: setting null or empty CLEARS the name (never throws), so a
    // thread can be renamed after a clear.
    icall_threadobj_set_name(named, nullptr);
    Expect(icall_threadobj_get_name(named) == nullptr,
           "set_Name(null) clears the name");
    icall_threadobj_set_name(named, il2cpp_string_new_from_cs(""));
    Expect(icall_threadobj_get_name(named) == nullptr,
           "set_Name(empty) clears the name");
    icall_threadobj_set_name(named, il2cpp_string_new_from_cs("retry"));
    Expect(StrEquals(icall_threadobj_get_name(named), "retry"),
           "rename works after a clear");
    // Start/Join leaves the name intact (simple storage on the object).
    icall_threadobj_start(named);
    icall_threadobj_join(named);
    Expect(StrEquals(icall_threadobj_get_name(named), "retry"),
           "Name survives Start/Join");
    // The CURRENT thread object: null default + round-trip.
    Expect(icall_threadobj_get_name(cur) == nullptr,
           "CurrentThread.Name == null by default");
    icall_threadobj_set_name(cur, il2cpp_string_new_from_cs("main"));
    Expect(StrEquals(icall_threadobj_get_name(cur), "main"),
           "CurrentThread.Name round-trips after set");
    // Setting the same name (non-null non-empty) on an already-named thread
    // throws -- even the identical value.
    Expect(ThrowsDotNet([&] {
               icall_threadobj_set_name(cur, il2cpp_string_new_from_cs("main"));
           }, kInvalidOpFullChain),
           "set_Name(same value) still throws InvalidOperationException");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
    }
    return g_failures == 0 ? 0 : 1;
}
