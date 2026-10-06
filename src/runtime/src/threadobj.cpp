//------------------------------------------------------------------------------
// threadobj.cpp -- System.Threading.Thread OBJECT icall implementations (see
// threadobj.h for the contract).
//
// The statics (sleep/yield/critical regions) live in thread.cpp; the
// single-threaded id-based extensions (threadext.h) are a separate model.
// THIS domain owns the managed Thread object: a real OS thread is spawned
// through the platform layer (plat_thread_spawn) when Start() runs the
// ThreadStart callback, and Join() waits for it through plat_thread_join.
//
// Cross-thread state: the RUNNING -> FINISHED transition is a std::atomic in
// the native handle (the child writes it with release, the parent reads it
// with acquire; the monotonic 1 -> 2 ordering makes every interleaving
// converge). The managed object's own state/tid fields are written and read
// ONLY by the creating thread (single-caller model, see the header).
//
// net48 deviations are documented in threadobj.h; the notable ones:
//   * the child drops exceptions escaping its callback (the thread still
//     terminates, Join still returns);
//   * a self-join throws ThreadStateException immediately (never blocks);
//   * priority is pinned to Normal (setter accepted-and-ignored);
//   * ManagedThreadId is the OS thread id, 0 until Start.
//------------------------------------------------------------------------------

#include "threadobj.h"

#include "il2cpp_delegate_ext.h" // icall_delegate_get_fn_ptr (validation)
#include "il2cpp_exception.h"    // il2cpp_throw_new (carrier)
#include "il2cpp_exception_ext.h"// il2cpp_throw_argument_null_exception
#include "il2cpp_runtime.h"      // il2cpp_object_new / il2cpp_class_register
#include "platform.h"            // plat_thread_spawn / join / current_id
#include "il2cpp_time.h"       // Il2CppTimeSpan

#include <atomic>
#include <cstdint>
#include <new>

//------------------------------------------------------------------------------
// System.Threading.Thread runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Threading_Thread_class for newobj/isinst/castclass, the same
// contract as System_Version_class in versionuri.cpp). instanceSize covers
// header + callback pointer + handle + state + tid + name. There is exactly
// ONE reference field -- the Thread.Name string at slot 5 (refFieldBitmap
// bit 5, refFieldCount 1); the callback is a code pointer and the handle is
// opaque native state, neither ever scanned.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Threading_Thread_class = {
    "System.Threading.Thread",                   // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppThreadObject)), // instanceSize
    0b1100000u,                                  // refFieldBitmap: bit 5 = name, bit 6 = callbackTarget
    2,                                           // refFieldCount: name + callbackTarget
    &System_Object_class,                        // baseClass (a Thread IS-A object)
    nullptr,                                     // vtable
    0,                                           // vtableSize
    nullptr,                                     // interfaces
    0,                                           // interfaceCount
};

namespace il2cpp {

namespace {

constexpr size_t AlignUp(size_t value, size_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}

// Layout pin: the icall accessors read the fields at fixed offsets. callbackFn
// and handle are plain pointer slots (width-stable); name is the GC-reference
// slot immediately after managedId, and callbackTarget the one after name --
// both shift index with pointer width, so they are pinned relationally.
static_assert(offsetof(Il2CppThreadObject, callbackFn) == 1 * sizeof(void*),
              "Il2CppThreadObject: callbackFn must sit at slot 1 (offset 8)");
static_assert(offsetof(Il2CppThreadObject, handle) == 2 * sizeof(void*),
              "Il2CppThreadObject: handle must sit at slot 2 (offset 16)");
static_assert(offsetof(Il2CppThreadObject, name) ==
                  AlignUp(offsetof(Il2CppThreadObject, managedId) + sizeof(int32_t),
                          alignof(Il2CppString*)),
              "Il2CppThreadObject: name must sit in the first pointer-aligned slot after managedId "
              "(the refFieldBitmap GC reference slot, x64 slot 5 / x86 slot 7)");
static_assert(offsetof(Il2CppThreadObject, managedId) ==
                  offsetof(Il2CppThreadObject, name) - sizeof(int32_t),
              "Il2CppThreadObject: managedId must sit immediately before the name slot");
static_assert(offsetof(Il2CppThreadObject, callbackTarget) ==
                  offsetof(Il2CppThreadObject, name) + sizeof(il2cpp::Il2CppString*),
              "Il2CppThreadObject: callbackTarget must sit in the slot after name "
              "(the refFieldBitmap GC reference slot, x64 slot 6 / x86 slot 8)");

bool g_threadClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_threadobj_class -- the System.Threading.Thread descriptor (lazily
// registered on first use; idempotent -- il2cpp_class_register dedupes by
// pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_threadobj_class()
{
    if (!g_threadClassRegistered)
    {
        il2cpp_class_register(&System_Threading_Thread_class);
        g_threadClassRegistered = true;
    }
    return &System_Threading_Thread_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Native thread state. Allocated by Start, freed by the first Join after
// the thread finished. The child writes state (1 -> 2, release) and tid at
// entry; the parent reads state/tid (acquire) and joins.
//------------------------------------------------------------------------------
struct il2cpp_thread_handle
{
    std::atomic<int32_t> state; // 1 = RUNNING, 2 = FINISHED (monotonic)
    plat_thread_t native;       // the spawned platform thread (joined once)
    int32_t tid;                // OS thread id, set by the child at entry
    int32_t managedId;          // ctor-assigned managed id (net48 MTID) -- the
                                // child publishes it to its TLS at entry so
                                // CurrentThread.ManagedThreadId matches the
                                // owning Thread object inside the callback
    void (*fn)();               // the ThreadStart callback (zero-arg ABI)
    Il2CppObject* target;       // ThreadStart delegate target (null = static);
                                // passed as `this` to the instance ABI when set
    Il2CppObject* parameter;    // ParameterizedThreadStart arg (nullptr for ThreadStart)
    Il2CppString* name;         // owner's Thread.Name at Start (raw pointer --
                                // the owning Thread object roots the string via
                                // refFieldBitmap bit 5 and outlives the thread,
                                // so the child TLS copy below stays valid)
};

//------------------------------------------------------------------------------
// ManagedThreadId allocator (net48 contract, A1 MTID-BOUNDARY-SPEC F50B348A):
// a PROCESS-WIDE monotonic counter. The main thread is 1; id 2 is reserved for
// the runtime, so the FIRST user Thread gets 3 and each subsequent one +1,
// never reused. The id is assigned at CONSTRUCTION (net48 assigns even for a
// never-started Thread) and stays stable across Start/Join.
//------------------------------------------------------------------------------
std::atomic<int32_t> g_nextManagedId{3};

// The managed id of the thread currently executing (TLS). Defaults to 1 (the
// main thread); a spawned thread's trampoline sets it to its object's
// ctor-assigned id before running the callback, so
// Thread.CurrentThread.ManagedThreadId == the instance's id inside the body.
thread_local int32_t t_currentManagedId = 1;

// The Thread.Name visible to Thread.CurrentThread inside a spawned thread
// (TLS). Published from the owner's name at Start (the child never touches
// the managed owner object); the main thread's entry stays null until set.
thread_local Il2CppString* t_currentThreadName = nullptr;

// Per-thread "already registered as a pool worker" flag (TLS). A pool worker
// registers its managed id exactly ONCE at thread start; the flag makes a
// second registration on the same thread a no-op (the worker keeps its id
// across every work item it serves -- net48 MTID contract item 5).
thread_local bool t_poolWorkerRegistered = false;

// ThreadTrampolineParameterized -- ParameterizedThreadStart runner: passes
// the stored parameter to the callback, then marks the handle FINISHED. An
// instance-bound callback (target != null, a closure over captured state) uses
// the generated instance ABI `void M(Il2CppObject* this, Il2CppObject* param)`
// -- the target rides the `this` slot; a static callback uses
// `void M(Il2CppObject* param)`.
void ThreadTrampolineParameterized(void* arg)
{
    il2cpp_thread_handle* h = static_cast<il2cpp_thread_handle*>(arg);
    h->tid = plat_thread_current_id();
    t_currentManagedId = h->managedId; // CurrentThread.ManagedThreadId == owner
    t_currentThreadName = h->name;     // CurrentThread.Name == owner's name
    try
    {
        if (h->target != nullptr)
        {
            typedef void (*InstanceParameterizedFn)(Il2CppObject*, Il2CppObject*);
            InstanceParameterizedFn pfn = reinterpret_cast<InstanceParameterizedFn>(h->fn);
            pfn(h->target, h->parameter);
        }
        else
        {
            typedef void (*ParameterizedFn)(Il2CppObject*);
            ParameterizedFn pfn = reinterpret_cast<ParameterizedFn>(h->fn);
            pfn(h->parameter);
        }
    }
    catch (...)
    {
        // Dropped per the header contract (no delivery path in M-stage).
    }
    h->state.store(2, std::memory_order_release);
}

// The platform layer spawns void(*)(void*) trampolines. The trampoline
// records the child's OS id, runs the ThreadStart callback, and marks the
// handle FINISHED -- even when the callback throws (the exception is
// dropped per the header contract; the thread must terminate and Join must
// return, otherwise Join would block forever).
void ThreadTrampoline(void* arg)
{
    il2cpp_thread_handle* h = static_cast<il2cpp_thread_handle*>(arg);
    h->tid = plat_thread_current_id();
    t_currentManagedId = h->managedId; // CurrentThread.ManagedThreadId == owner
    t_currentThreadName = h->name;     // CurrentThread.Name == owner's name
    try
    {
        // An instance-method ThreadStart (closure/lambda) takes the target in
        // the `this` slot (generated instance ABI void M(Il2CppObject* this));
        // a static one is the plain void M().
        if (h->target != nullptr)
        {
            typedef void (*InstanceFn)(Il2CppObject*);
            InstanceFn pfn = reinterpret_cast<InstanceFn>(h->fn);
            pfn(h->target);
        }
        else
        {
            h->fn();
        }
    }
    catch (...)
    {
        // Dropped: an exception escaping a thread's callback has no
        // delivery path in the M-stage model (documented in threadobj.h).
    }
    h->state.store(2, std::memory_order_release);
}

Il2CppThreadObject* AsThread(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppThreadObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors versionuri.cpp / arraylist.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// ThreadStateException carrier (net48 chain: ThreadStateException ->
// SystemException -> Exception).
void ThrowThreadState(const char* message)
{
    il2cpp_throw_new(
        "System.Threading.ThreadStateException,"
        "System.Threading.ThreadStateException,System.SystemException,System.Exception",
        message);
}

constexpr const char* kOutOfMemoryChain =
    "System.OutOfMemoryException,System.OutOfMemoryException,"
    "System.SystemException,System.Exception";

// Reap a spawned thread's native handle: wait for the child to exit when it
// is still running (the FINISHED store happens-before the child exits, and
// plat_thread_join synchronizes with that exit -- so after the join returns
// the child can never touch h again and the handle is safe to free) and
// release the handle. The object's managed id (t->tid, assigned at ctor) is
// NEVER overwritten here -- net48 keeps ManagedThreadId stable for the whole
// lifetime of the Thread object. Called ONLY by the Join family (single-caller
// model -- t->handle is parent-only state, race-free).
void ReapThread(Il2CppThreadObject* t, il2cpp_thread_handle* h)
{
    if (h->state.load(std::memory_order_acquire) != 2)
    {
        plat_thread_join(h->native);
    }
    t->handle = nullptr;
    delete h;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_threadobj_register_pool_worker -- ThreadPool worker managed-id
// registration (net48 MTID contract, A1 F50B348A item 5). A pool worker
// shares the SAME process-wide monotonic id space as explicit Thread objects
// and REUSES its id across every work item it serves. Called ONCE at worker
// thread start (threadpool.cpp mark_pool_worker): allocates the next id from
// the shared allocator and publishes it to the calling thread's TLS, so
// Thread.CurrentThread.ManagedThreadId inside a work item reports the
// worker's stable id. Idempotent per thread (t_poolWorkerRegistered): a
// second call on the same thread is a no-op, so the worker keeps its id.
// EXTERNAL linkage (outside the anonymous namespace): threadpool.cpp calls it.
//------------------------------------------------------------------------------
void il2cpp_threadobj_register_pool_worker()
{
    if (t_poolWorkerRegistered)
    {
        return; // already registered: the worker keeps its id (reuse contract)
    }
    t_poolWorkerRegistered = true;
    t_currentManagedId = g_nextManagedId.fetch_add(1, std::memory_order_relaxed);
}

//------------------------------------------------------------------------------
// icall_threadobj_ctor -- new Thread(ThreadStart): validate the delegate
// (null -> ArgumentNullException; multicast / instance-bound ->
// InvalidOperationException through icall_delegate_get_fn_ptr) and extract
// its code pointer. The thread is NEW until Start.
//------------------------------------------------------------------------------
void icall_threadobj_ctor(Il2CppObject* self, Il2CppObject* callback)
{
    ThrowIfNullReceiver(self);
    if (callback == nullptr)
    {
        il2cpp_throw_argument_null_exception("start");
    }
    // Instance-method delegates (closures/lambdas) ARE valid ThreadStart
    // callbacks: extract (methodPtr, target) and keep the target as a GC
    // reference (callbackTarget) so the spawned thread can invoke the
    // generated instance ABI with it as `this`. Still rejects multicast
    // (InvalidOperationException) and null.
    Il2CppObject* target = nullptr;
    void* fn = icall_delegate_get_fn_ptr_target(callback, &target);
    il2cpp_threadobj_class(); // register the descriptor (tooling/type probes)
    Il2CppThreadObject* t = AsThread(self);
    t->callbackFn = fn;
    t->callbackTarget = target;
    t->handle = nullptr;
    t->state = 0; // NEW
    t->tid = 0;
    // net48 ManagedThreadId: assigned at CONSTRUCTION (even for a never-started
    // Thread), monotonic and never reused. The first user Thread gets 3 (id 2
    // is the runtime-reserved slot).
    t->managedId = g_nextManagedId.fetch_add(1, std::memory_order_relaxed);
    t->isBackground = false; // net48: new threads are foreground
    t->name = nullptr;       // Thread.Name: unset until icall_threadobj_set_name
}

//------------------------------------------------------------------------------
// icall_threadobj_start -- Thread.Start(): spawn a REAL OS thread running
// the ThreadStart callback (zero-arg ABI). State NEW is required: starting
// twice (running OR finished) raises ThreadStateException (net48).
//------------------------------------------------------------------------------
void icall_threadobj_start(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppThreadObject* t = AsThread(self);
    if (t->state != 0)
    {
        ThrowThreadState("Thread has already been started.");
    }
    il2cpp_thread_handle* h = new il2cpp_thread_handle();
    h->state.store(1, std::memory_order_relaxed);
    h->tid = 0;
    h->managedId = t->managedId; // publish the ctor-assigned id to the child TLS
    h->name = t->name;             // publish the owner's name to the child TLS
    h->fn = reinterpret_cast<void (*)()>(t->callbackFn);
    h->target = t->callbackTarget; // instance ThreadStart target (null = static)
    h->native = plat_thread_spawn(&ThreadTrampoline, h);
    if (h->native == nullptr)
    {
        delete h;
        il2cpp_throw_new(kOutOfMemoryChain,
                         "Insufficient memory to create a new thread.");
    }
    // The child never touches the managed object -- it only reads its own
    // handle argument -- so these parent-only writes are race-free (the
    // child may finish before they land; Join/IsAlive consult the handle).
    t->handle = h;
    t->state = 1; // STARTED
}

//------------------------------------------------------------------------------
// icall_threadobj_start_parameterized -- Thread.Start(object): spawn a REAL
// OS thread running the ParameterizedThreadStart callback with the supplied
// parameter.
//------------------------------------------------------------------------------
void icall_threadobj_start_parameterized(Il2CppObject* self, Il2CppObject* parameter)
{
    ThrowIfNullReceiver(self);
    Il2CppThreadObject* t = AsThread(self);
    if (t->state != 0)
    {
        ThrowThreadState("Thread has already been started.");
    }
    il2cpp_thread_handle* h = new il2cpp_thread_handle();
    h->state.store(1, std::memory_order_relaxed);
    h->tid = 0;
    h->managedId = t->managedId; // publish the ctor-assigned id to the child TLS
    h->name = t->name;             // publish the owner's name to the child TLS
    h->fn = reinterpret_cast<void (*)()>(t->callbackFn);
    h->target = t->callbackTarget; // instance ParameterizedThreadStart target
    h->parameter = parameter;
    h->native = plat_thread_spawn(&ThreadTrampolineParameterized, h);
    if (h->native == nullptr)
    {
        delete h;
        il2cpp_throw_new(kOutOfMemoryChain,
                         "Insufficient memory to create a new thread.");
    }
    // Parent-only writes (the child reads only its handle arg) -- race-free.
    t->handle = h;
    t->state = 1; // STARTED
}

//------------------------------------------------------------------------------
// icall_threadobj_unsafe_start(_parameterized) -- no ExecutionContext in the
// M-stage model: delegate to Start / Start(object).
//------------------------------------------------------------------------------
void icall_threadobj_unsafe_start(Il2CppObject* self)
{
    icall_threadobj_start(self);
}

void icall_threadobj_unsafe_start_parameterized(Il2CppObject* self, Il2CppObject* parameter)
{
    icall_threadobj_start_parameterized(self, parameter);
}

//------------------------------------------------------------------------------
// icall_threadobj_get_thread_state -- net48 ThreadState flags: Running=0,
// Unstarted=8, Stopped=16 (object states: 0=NEW, 1=STARTED, 2=CURRENT).
//------------------------------------------------------------------------------
int32_t icall_threadobj_get_thread_state(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppThreadObject* t = AsThread(self);
    if (t->state == 2)
        return 0; // CURRENT: Running
    if (t->state == 0)
        return 8; // NEW: Unstarted
    const il2cpp_thread_handle* h = static_cast<const il2cpp_thread_handle*>(t->handle);
    if (h == nullptr)
        return 16; // handle released by Join: Stopped
    if (h->state.load(std::memory_order_acquire) == 2)
        return 16; // FINISHED: Stopped
    return 0; // RUNNING
}

//------------------------------------------------------------------------------
// icall_threadobj_join -- Thread.Join(): block until the spawned thread
// exits, then release the native handle (a second Join returns
// immediately -- net48 Join on a finished thread). Joining a NEW thread
// or the CURRENT thread raises ThreadStateException (self-join: net48
// would block forever -- this contract throws instead, see threadobj.h).
//------------------------------------------------------------------------------
void icall_threadobj_join(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppThreadObject* t = AsThread(self);
    if (t->state == 0)
    {
        ThrowThreadState("Thread has not been started.");
    }
    if (t->state == 2)
    {
        ThrowThreadState("A thread cannot join itself.");
    }
    il2cpp_thread_handle* h = static_cast<il2cpp_thread_handle*>(t->handle);
    if (h == nullptr)
    {
        return; // already joined (the thread finished)
    }
    ReapThread(t, h);
}

//------------------------------------------------------------------------------
// icall_threadobj_join_timeout -- Thread.Join(int32): the net48 Join(int)
// form. Validation order matches net48 JoinInternal: the timeout argument
// (< -1 -> ArgumentOutOfRangeException) is checked FIRST, then the
// thread-state guards (self-join then never-started ThreadStateException --
// same messages as Join()). Returns:
//   * true  -- the thread terminated within the timeout (or was already
//     terminated: a JOINED thread and a NEW-not-yet-reaped finished thread
//     both read the FINISHED/nulled handle);
//   * false -- the timeout expired while the thread still runs. The native
//     handle is NOT released: a later Join / Join(int) still waits.
// timeoutMs == -1 waits forever (Timeout.Infinite), 0 is a single
// non-blocking probe (true iff already terminated). Waiting is implemented
// by POLLING the handle's FINISHED atomic at a small sleep granularity (the
// same polling pattern as monitor.cpp's wait) -- the wake latency after the
// child exits is bounded by the poll interval.
//------------------------------------------------------------------------------
bool icall_threadobj_join_timeout(Il2CppObject* self, int32_t millisecondsTimeout)
{
    ThrowIfNullReceiver(self);
    if (millisecondsTimeout < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("millisecondsTimeout", nullptr);
    }
    Il2CppThreadObject* t = AsThread(self);
    if (t->state == 2)
    {
        ThrowThreadState("A thread cannot join itself.");
    }
    if (t->state == 0)
    {
        ThrowThreadState("Thread has not been started.");
    }
    il2cpp_thread_handle* h = static_cast<il2cpp_thread_handle*>(t->handle);
    if (h == nullptr)
    {
        return true; // already joined: the thread is dead (net48 Join returns true)
    }
    const int64_t budgetNs = millisecondsTimeout < 0
        ? -1 // infinite wait (Timeout.Infinite)
        : static_cast<int64_t>(millisecondsTimeout) * 1000000LL;
    const int64_t startNs = plat_clock_monotonic_ns();
    for (;;)
    {
        if (h->state.load(std::memory_order_acquire) == 2)
        {
            break; // the child finished: reap and report success
        }
        if (budgetNs >= 0 && plat_clock_monotonic_ns() - startNs >= budgetNs)
        {
            return false; // timed out: the thread still runs, handle stays
        }
        plat_thread_sleep(2);
    }
    ReapThread(t, h);
    return true;
}

//------------------------------------------------------------------------------
// icall_threadobj_get_is_alive -- Thread.IsAlive: true exactly while the
// thread runs (the atomic handle state reads 1 = RUNNING). NEW threads and
// joined/finished threads report false; the CURRENT object always reports
// true.
//------------------------------------------------------------------------------
bool icall_threadobj_get_is_alive(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppThreadObject* t = AsThread(self);
    if (t->state == 2)
    {
        return true; // the CURRENT thread object is always alive
    }
    if (t->state == 0)
    {
        return false; // NEW: nothing started yet
    }
    const il2cpp_thread_handle* h = static_cast<const il2cpp_thread_handle*>(t->handle);
    return h != nullptr && h->state.load(std::memory_order_acquire) == 1;
}

//------------------------------------------------------------------------------
// icall_threadobj_get_managed_thread_id -- Thread.ManagedThreadId: the
// ctor-assigned managed id (net48 semantics -- assigned at CONSTRUCTION even
// for a never-started Thread, monotonic, never reused, stable across
// Start/Join). The CURRENT object carries the running thread's TLS id (see
// get_current_thread).
//------------------------------------------------------------------------------
int32_t icall_threadobj_get_managed_thread_id(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppThreadObject* t = AsThread(self);
    return t->managedId;
}

//------------------------------------------------------------------------------
// icall_threadobj_get_priority -- Thread.Priority getter: always 2 =
// ThreadPriority.Normal (the net48 enum value; the model pins priority).
//------------------------------------------------------------------------------
int32_t icall_threadobj_get_priority(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 2; // ThreadPriority.Normal (pinned; net48 enum value)
}

//------------------------------------------------------------------------------
// icall_threadobj_set_priority -- Thread.Priority setter: accepted and
// IGNORED (mirrors threadext's il2cpp_thread_set_priority; the getter keeps
// reporting Normal -- no thread ever changes priority in the model).
//------------------------------------------------------------------------------
void icall_threadobj_set_priority(Il2CppObject* self, int32_t priority)
{
    ThrowIfNullReceiver(self);
    (void)priority; // pinned to Normal (documented deviation)
}

//------------------------------------------------------------------------------
// icall_threadobj_get_is_background -- Thread.IsBackground getter: the
// stored flag (default false = foreground).
//------------------------------------------------------------------------------
bool icall_threadobj_get_is_background(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsThread(self)->isBackground;
}

//------------------------------------------------------------------------------
// icall_threadobj_set_is_background -- Thread.IsBackground setter (net48
// semantics, verified by the rtaud11 live probe): plain flag storage, with
// the net48 STOPPED-thread guard -- setting IsBackground on a thread whose
// callback has FINISHED throws ThreadStateException ("Thread is dead; state
// cannot be accessed."). A NEW (unstarted) thread, a RUNNING thread, or the
// CURRENT thread all accept the set (probe: unstarted_set=True /
// running_set=True / current_set ok; finished_set=THROWS /
// bg_set_after_join=THROWS). The FINISHED state is read from the handle's
// atomic (1 = RUNNING, 2 = FINISHED -- the same acquire load the IsAlive
// probe uses); a null handle means Join already released it (the thread
// finished). The value stays informational in the M-stage model (no
// process-lifetime coupling).
//------------------------------------------------------------------------------
void icall_threadobj_set_is_background(Il2CppObject* self, bool isBackground)
{
    ThrowIfNullReceiver(self);
    Il2CppThreadObject* t = AsThread(self);
    if (t->state == 1) // STARTED: running or finished (single-caller model)
    {
        const il2cpp_thread_handle* h = static_cast<const il2cpp_thread_handle*>(t->handle);
        if (h == nullptr || h->state.load(std::memory_order_acquire) == 2)
        {
            ThrowThreadState("Thread is dead; state cannot be accessed.");
        }
    }
    t->isBackground = isBackground;
}

//------------------------------------------------------------------------------
// icall_threadobj_get_name -- Thread.Name getter: the stored name string
// (null when never set). The name is a managed string GC reference kept
// alive by the object (refFieldBitmap bit 5, see the descriptor).
//------------------------------------------------------------------------------
Il2CppString* icall_threadobj_get_name(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsThread(self)->name;
}

//------------------------------------------------------------------------------
// icall_threadobj_set_name -- Thread.Name setter (net48 reference semantics,
// Thread.cs Name setter):
//   * a null or EMPTY value CLEARS the name (InternalClearName -- m_Name =
//     null) and never throws, even on an already-named thread (so a thread
//     can be renamed after a clear);
//   * a non-null, non-empty value names the thread, but setting one on an
//     ALREADY-NAMED thread (m_Name != null && m_Name.Length != 0) throws
//     InvalidOperationException (net48: "A thread's Name may only be set
//     once." -- InternalSetName). The name is a managed string GC reference
//     stored on the object (refFieldBitmap bit 5).
//------------------------------------------------------------------------------
void icall_threadobj_set_name(Il2CppObject* self, Il2CppString* name)
{
    ThrowIfNullReceiver(self);
    Il2CppThreadObject* t = AsThread(self);
    if (name == nullptr || name->length == 0)
    {
        t->name = nullptr; // clear (net48 InternalClearName -- no exception)
        if (t->state == 2)
            t_currentThreadName = nullptr; // CURRENT wrapper clear publishes too
        return;
    }
    if (t->name != nullptr)
    {
        il2cpp_throw_invalid_operation_exception("A thread's Name may only be set once.");
    }
    t->name = name;
    if (t->state == 2)
    {
        // Naming the CURRENT wrapper (Thread.CurrentThread.Name = x) names
        // this thread: publish to TLS so later CurrentThread reads see it
        // (the wrapper itself is ephemeral -- a fresh object per call).
        t_currentThreadName = name;
    }
}

//------------------------------------------------------------------------------
// icall_threadobj_get_current_thread -- Thread.CurrentThread (static): a
// fresh Thread object wrapping the CURRENT thread (state CURRENT,
// ManagedThreadId = the current thread's managed id from TLS -- main = 1,
// a spawned thread = its ctor-assigned id, IsAlive true, self-join throws).
//------------------------------------------------------------------------------
Il2CppObject* icall_threadobj_get_current_thread()
{
    il2cpp_threadobj_class(); // register the descriptor
    Il2CppThreadObject* t = reinterpret_cast<Il2CppThreadObject*>(
        il2cpp_object_new(&System_Threading_Thread_class));
    t->callbackFn = nullptr;
    t->callbackTarget = nullptr; // no ThreadStart callback (CURRENT object)
    t->handle = nullptr;
    t->state = 2; // CURRENT
    t->tid = plat_thread_current_id();
    t->managedId = t_currentManagedId; // main = 1; spawned = ctor id (TLS)
    t->isBackground = false; // net48: threads start foreground
    t->name = t_currentThreadName; // CurrentThread.Name: owner's name in a
                                   // spawned thread, null on main until set
    return reinterpret_cast<Il2CppObject*>(t);
}

// P6: Thread legacy stubs (.NET Framework 4.8 AppDomain/context/abort methods).

void icall_threadobj_set_app_domain_stack(Il2CppObject* self, int32_t stackSize)
{
    (void)self; (void)stackSize;
    // no-op: AppDomain not supported in ILC model
}

void icall_threadobj_restore_app_domain_stack(Il2CppObject* self)
{
    (void)self;
    // no-op: AppDomain not supported in ILC model
}

Il2CppObject* icall_threadobj_internal_get_current_thread()
{
    return nullptr; // no current thread in simplified model
}

Il2CppObject* icall_threadobj_get_context_internal(Il2CppObject* self)
{
    (void)self;
    return nullptr; // no context in simplified model
}

Il2CppObject* icall_threadobj_internal_cross_context_callback(int32_t contextId, Il2CppObject* callback)
{
    (void)contextId; (void)callback;
    return nullptr; // no cross-context callback in simplified model
}

void icall_threadobj_set_abort_reason(Il2CppObject* self, Il2CppObject* reason)
{
    (void)self; (void)reason;
    // no-op: abort not supported in simplified model
}

Il2CppObject* icall_threadobj_get_abort_reason(Il2CppObject* self)
{
    (void)self;
    return nullptr; // no abort reason in simplified model
}

void icall_threadobj_clear_abort_reason(Il2CppObject* self)
{
    (void)self;
    // no-op: abort not supported in simplified model
}

// P6: Thread internal helpers -- stubs for simplified model.

void icall_threadobj_set_is_thread_pool_thread(Il2CppObject* self, bool value)
{
    (void)self; (void)value;
    // no-op: simplified model
}

int32_t icall_threadobj_get_apartment_state_native(Il2CppObject* self)
{
    (void)self;
    return 0; // MTA
}

bool icall_threadobj_set_apartment_state_native(Il2CppObject* self, int32_t state)
{
    (void)self; (void)state;
    return true; // always succeeds
}

void icall_threadobj_interrupt(Il2CppObject* self)
{
    (void)self;
    // no-op: simplified model, no thread interruption
}

int32_t icall_threadobj_get_optimal_max_spin_waits(Il2CppObject* self)
{
    (void)self;
    return 1; // default spin count
}

//------------------------------------------------------------------------------
// icall_threadobj_join_timespan -- Thread.Join(TimeSpan): convert TimeSpan to
// milliseconds and delegate to icall_threadobj_join_timeout.
//------------------------------------------------------------------------------
bool icall_threadobj_join_timespan(Il2CppObject* self, Il2CppTimeSpan timeout)
{
    // Timeout.Infinite (-10000 ticks) maps to -1 (infinite wait).
    if (timeout.ticks == -10000LL)
    {
        return icall_threadobj_join_timeout(self, -1);
    }
    // Convert ticks (100ns units) to milliseconds, checking overflow.
    int64_t ms = timeout.ticks / 10000LL; // 10000 ticks per millisecond
    if (ms > static_cast<int64_t>(INT32_MAX) || ms < static_cast<int64_t>(INT32_MIN))
    {
        il2cpp_throw_argument_out_of_range_exception("timeout", nullptr);
    }
    return icall_threadobj_join_timeout(self, static_cast<int32_t>(ms));
}

//------------------------------------------------------------------------------
// icall_thread_sleep_timespan -- Thread.Sleep(TimeSpan): convert TimeSpan to
// milliseconds and delegate to plat_thread_sleep.
//------------------------------------------------------------------------------
void icall_thread_sleep_timespan(Il2CppTimeSpan ts)
{
    // Timeout.Infinite (-10000 ticks) maps to -1 (infinite sleep).
    if (ts.ticks == -10000LL)
    {
        plat_thread_sleep(-1);
        return;
    }
    // Convert ticks (100ns units) to milliseconds, checking overflow.
    int64_t ms = ts.ticks / 10000LL; // 10000 ticks per millisecond
    if (ms > static_cast<int64_t>(INT32_MAX) || ms < static_cast<int64_t>(INT32_MIN))
    {
        il2cpp_throw_argument_out_of_range_exception("timeout", nullptr);
    }
    plat_thread_sleep(static_cast<int32_t>(ms));
}

}} // namespace il2cpp::runtime
