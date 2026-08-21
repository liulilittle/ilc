#pragma once
//------------------------------------------------------------------------------
// threadobj.h -- System.Threading.Thread OBJECT model (threadobj domain:
// new Thread / Start / Join / IsAlive / ManagedThreadId / Priority /
// CurrentThread). See threadobj.cpp for the implementation.
//
// This complements the statics in il2cpp_thread.h (sleep / yield /
// current-thread-id / critical regions) and the single-threaded extensions
// in threadext.h (id-based join / priority / is-alive). Unlike threadext's
// SIMPLIFIED single-threaded model, the Thread OBJECT model runs REAL OS
// threads: icall_threadobj_start spawns one through the platform layer
// (plat_thread_spawn) and the callback runs on it.
//
// REPRESENTATION
// --------------
//   struct Il2CppThreadObject        -- the managed Thread object, allocated
//      through the GLOBAL System_Threading_Thread_class descriptor (the same
//      contract as System_Version_class in versionuri.h -- generated newobj
//      emits il2cpp_object_new(&System_Threading_Thread_class)):
//        obj         object header (klass pointer)
//        callbackFn  the ThreadStart CODE pointer, extracted at ctor time
//                    from the delegate's methodPtr (never a managed ref --
//                    the delegate itself is not rooted, see below)
//        handle      opaque native thread state (il2cpp_thread_handle*,
//                    allocated at Start, freed by the first Join-after-run;
//                    null before Start and after Join)
//        state       0 = NEW (created, not started)
//                    1 = STARTED (Start called; running or finished)
//                    2 = CURRENT (the CurrentThread singleton object)
//        tid         OS thread id (only for the CURRENT object; a spawned
//                    thread's id lives in its handle)
//        isBackground  the IsBackground flag (plain stored value, no
//                    process-lifetime coupling in the M-stage model)
//        name        the Thread.Name string (a managed GC reference, null
//                    until set)
//   GC reference fields: exactly ONE -- the `name` string, stored at slot 5
//   (refFieldBitmap bit 5, refFieldCount 1). The collector scans and moves it
//   with the object (compaction rewrites the slot, gc.cpp bitmap scan). The
//   delegate is NOT a reference: callbackFn is the extracted ThreadStart CODE
//   pointer, never rooted -- the M-stage contract keeps the caller's delegate
//   reference alive (same policy as timer.h), so it is deliberately absent
//   from the bitmap.
//
// DELEGATE CONTRACT (ThreadStart)
// -------------------------------
// The ctor extracts the delegate's methodPtr through
// icall_delegate_get_fn_ptr, which enforces the delegatecb domain policy:
//   * null            -> ArgumentNullException (checked BEFORE the extract)
//   * multicast       -> InvalidOperationException (only one code pointer
//                        exists; .NET would run the whole invocation list --
//                        documented deviation)
//   * instance-bound  -> InvalidOperationException (the pointer carries no
//                        'this'; unreachable through the transpiler, which
//                        only builds static-method delegates -- documented)
// The extracted pointer is the generated zero-argument ABI void(*)() --
// ThreadStart takes no parameters -- and the spawned thread calls it
// directly. If the callback throws (a DotNetException), the thread still
// terminates and Join still returns (the exception is dropped; .NET would
// route it to the AppDomain unhandled-exception machinery -- documented
// deviation). The callback MUST NOT allocate: the M-stage allocator is
// single-threaded by contract (il2cpp_allocator.h), so a child thread
// touching the managed heap is a caller bug.
//
// STATE MACHINE (single-caller model)
// -----------------------------------
// The managed object's state field is written/read ONLY by the creating
// thread; the cross-thread flag (RUNNING -> FINISHED) lives in the native
// handle as a std::atomic. Members are NOT safe to call concurrently from
// multiple threads on the same Thread object (caller contract, like .NET's
// own race-prone Join/IsAlive pair).
//
// net48 deviations (documented next to each entry):
//   * ManagedThreadId is the OS thread id (like the statics' current-id
//     probe), 0 until Start -- net48 assigns sequential managed ids at
//     construction.
//   * Joining the CURRENT thread throws ThreadStateException immediately --
//     net48 would block forever on a self-join.
//   * Priority is pinned to Normal (2 -- the net48 enum value): the setter
//     is accepted and ignored, the getter always reports 2.
//   * IsBackground is a plain stored flag with the net48 STOPPED-thread
//     guard: setting it on a thread whose callback has FINISHED throws
//     ThreadStateException ("Thread is dead; state cannot be accessed." --
//     net48 SetBackgroundNative behavior, live-probe verified). NEW /
//     RUNNING / CURRENT threads accept the set; the getter reports the
//     stored value. The value has no process-lifetime coupling.
//   * CurrentThread returns a FRESH object per call (net48 returns the same
//     per-thread instance); identity comparisons across calls are not
//     modeled.
//   * A thread whose object is garbage-collected before Join leaks its
//     native handle (no finalizers in the M-stage model).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_string.h"   // Il2CppString (the Thread.Name GC reference)

// System.Threading.Thread runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Threading_Thread_class for newobj/isinst/castclass; defined
// in threadobj.cpp, registered lazily through il2cpp_threadobj_class()).
extern il2cpp::Il2CppClass System_Threading_Thread_class;

namespace il2cpp {

// Managed System.Threading.Thread layout (see the header comment): object
// header + the extracted ThreadStart code pointer + the opaque native
// handle + the parent-only state/tid fields. NO reference fields.
struct Il2CppThreadObject
{
    Il2CppObject obj;       // object header (klass pointer, offset 0)
    void* callbackFn;       // ThreadStart code pointer (null for CURRENT)
    void* handle;           // native il2cpp_thread_handle* (null until Start)
    int32_t state;          // 0 NEW / 1 STARTED / 2 CURRENT
    int32_t tid;            // OS thread id (CURRENT object only)
    bool isBackground;      // IsBackground flag (default false = foreground)
    Il2CppString* name;     // Thread.Name (GC reference -- see the header)
};

// The System.Threading.Thread descriptor (lazily registered on first use;
// generated code allocates through &System_Threading_Thread_class).
Il2CppClass* il2cpp_threadobj_class();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// new Thread(ThreadStart): validates the delegate (null ->
// ArgumentNullException; multicast / instance-bound ->
// InvalidOperationException) and extracts its code pointer. The thread is
// created in the NEW state -- nothing runs until Start.
void icall_threadobj_ctor(Il2CppObject* self, Il2CppObject* callback);

// Thread.Start(): spawns a REAL OS thread running the ThreadStart callback.
// A ThreadStateException is raised when the thread was already started
// (running or finished).
void icall_threadobj_start(Il2CppObject* self);

// Thread.Join(): blocks until the spawned thread exits, then releases its
// native handle (a later Join returns immediately). A ThreadStateException
// is raised when the thread was never started or is the CURRENT thread
// (self-join -- net48 would block forever; this contract throws instead).
void icall_threadobj_join(Il2CppObject* self);

// Thread.Join(int32 millisecondsTimeout) -- the net48 Join(int) form (wired
// by MethodMap.Thread.cs, D-THR3-1): blocks until the thread terminates or
// the timeout expires. Returns true when the thread ended within the timeout
// (or was already terminated), false on timeout. millisecondsTimeout < -1
// throws ArgumentOutOfRangeException BEFORE the thread-state guards (net48
// JoinInternal validation order); -1 (Timeout.Infinite) waits forever; 0 is a
// single non-blocking probe (true iff already terminated). NEW (never-started)
// and CURRENT (self-join) threads throw ThreadStateException like Join(). On
// a timeout return (false) the native handle is NOT released: a later
// Join / Join(int) still waits for the running thread.
bool icall_threadobj_join_timeout(Il2CppObject* self, int32_t millisecondsTimeout);

// Thread.IsAlive: true exactly while the thread is running (NEW and
// finished threads report false; the CURRENT thread reports true).
bool icall_threadobj_get_is_alive(Il2CppObject* self);

// Thread.ManagedThreadId (SIMPLIFIED): the OS thread id (see the header
// comment) -- 0 until Start for a spawned thread, the current OS id for the
// CURRENT object.
int32_t icall_threadobj_get_managed_thread_id(Il2CppObject* self);

// Thread.Priority getter: always 2 = ThreadPriority.Normal (the net48 enum
// value; the model pins priority -- no thread ever changes it).
int32_t icall_threadobj_get_priority(Il2CppObject* self);

// Thread.Priority setter: accepted and IGNORED (the getter keeps reporting
// Normal -- no thread ever changes priority in the model).
void icall_threadobj_set_priority(Il2CppObject* self, int32_t priority);

// Thread.IsBackground getter: the stored flag (default false = foreground;
// set through icall_threadobj_set_is_background). Unlike net48, the value
// has no process-lifetime coupling -- it is purely informational.
bool icall_threadobj_get_is_background(Il2CppObject* self);

// Thread.IsBackground setter: plain flag storage with the net48
// STOPPED-thread guard -- setting it on a thread whose callback has FINISHED
// throws ThreadStateException (net48 "Thread is dead; state cannot be
// accessed."); NEW / RUNNING / CURRENT threads accept the set.
void icall_threadobj_set_is_background(Il2CppObject* self, bool isBackground);

// Thread.Name getter: the stored name string (null when never set). The
// name is a managed string (GC reference) kept alive through the object.
Il2CppString* icall_threadobj_get_name(Il2CppObject* self);

// Thread.Name setter (net48 semantics): a null value is ignored (the name
// stays as-is); a NON-null value names the thread -- setting a name on an
// ALREADY-NAMED thread throws InvalidOperationException (net48: a thread's
// name may only be set once). The name is a managed string reference stored
// on the object (GC-scanned through refFieldBitmap bit 5).
void icall_threadobj_set_name(Il2CppObject* self, Il2CppString* name);

// Thread.CurrentThread (static): a Thread object wrapping the CURRENT
// thread (state CURRENT, ManagedThreadId = the current OS id, IsAlive
// true). Returns a FRESH object per call (documented deviation).
Il2CppObject* icall_threadobj_get_current_thread();

// P6: Thread internal helpers -- the BCL calls these but the ILC has a
// simplified single-threaded model. Stubs return sensible defaults.

// Thread.IsThreadPoolThread setter: no-op (simplified model).
void icall_threadobj_set_is_thread_pool_thread(Il2CppObject* self, bool value);

// Thread.GetApartmentStateNative: always returns 0 (MTA).
int32_t icall_threadobj_get_apartment_state_native(Il2CppObject* self);

// Thread.SetApartmentStateNative: always returns true (no-op).
bool icall_threadobj_set_apartment_state_native(Il2CppObject* self, int32_t state);

// Thread.Interrupt: no-op (simplified model, no thread interruption).
void icall_threadobj_interrupt(Il2CppObject* self);

// Thread.get_OptimalMaxSpinWaitsPerSpinIteration: returns 1 (default).
int32_t icall_threadobj_get_optimal_max_spin_waits(Il2CppObject* self);

}} // namespace il2cpp::runtime
