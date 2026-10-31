//------------------------------------------------------------------------------
// rwlock.cpp -- System.Threading.ReaderWriterLock (the LEGACY lock; the Slim
// variant lives in rwls.cpp). Contract + measured net48 semantics in
// il2cpp_rwlock.h.
//
// The state is the current thread's hold levels plus the writer sequence
// number. Deterministic single-threaded model: no blocking, no cross-thread
// waiting (documented in the header).
//------------------------------------------------------------------------------

#include "il2cpp_rwlock.h"

#include "il2cpp_exception.h"       // il2cpp_throw_new (ApplicationException carrier)
#include "il2cpp_exception_ext.h"   // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_metadata.h"        // System_Object_class
#include "il2cpp_runtime.h"         // il2cpp_class_register

#include <cstdint>
#include <cstdlib>

//------------------------------------------------------------------------------
// Class descriptor (GLOBAL scope -- the transpiler emits
// &System_Threading_ReaderWriterLock_class for newobj). Header + native state
// pointer; NO GC reference fields (refFieldBitmap 0).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Threading_ReaderWriterLock_class = {
    "System.Threading.ReaderWriterLock",
    static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppReaderWriterLockObject)),
    0u,
    0,
    &System_Object_class,
    nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

namespace {

// Native state. writerSeqNum mirrors net48's _seqNum: starts at 1 and
// increments on every writer acquisition (measured: AnyWritersSince(n) is
// exactly `writerSeqNum > n`).
struct RwLockState
{
    int32_t readLevel;   // current thread's reader-lock recursion level
    int32_t writeLevel;  // current thread's writer-lock recursion level
    int32_t writerSeqNum;
};

bool g_rwlockClassRegistered = false;

RwLockState* AsState(Il2CppObject* self)
{
    return static_cast<RwLockState*>(
        reinterpret_cast<Il2CppReaderWriterLockObject*>(self)->state);
}

[[noreturn]] void ThrowNotHeld(const char* what)
{
    il2cpp_throw_new(
        "System.ApplicationException,"
        "System.ApplicationException,"
        "System.SystemException,System.Object,System.Exception",
        what);
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

void ValidateTimeout(int32_t timeout)
{
    // net48: -1 means infinite; anything below -1 is out of range.
    if (timeout < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("timeout", nullptr);
    }
}

} // namespace

void icall_rwl_ctor(Il2CppObject* self)
{
    if (!g_rwlockClassRegistered)
    {
        il2cpp_class_register(&System_Threading_ReaderWriterLock_class);
        g_rwlockClassRegistered = true;
    }
    auto* state = new RwLockState();
    state->readLevel = 0;
    state->writeLevel = 0;
    state->writerSeqNum = 1; // measured net48: the initial sequence is 1
    reinterpret_cast<Il2CppReaderWriterLockObject*>(self)->state = state;
}

void icall_rwl_acquire_reader(Il2CppObject* self, int32_t timeout)
{
    ValidateTimeout(timeout);
    // A reader acquisition never advances the writer sequence (measured).
    ++AsState(self)->readLevel;
}

void icall_rwl_release_reader(Il2CppObject* self)
{
    RwLockState* state = AsState(self);
    if (state->readLevel <= 0)
    {
        ThrowNotHeld("The reader lock is not held by the current thread.");
    }
    --state->readLevel;
}

void icall_rwl_acquire_writer(Il2CppObject* self, int32_t timeout)
{
    ValidateTimeout(timeout);
    RwLockState* state = AsState(self);
    ++state->writeLevel;
    // Every acquisition (recursive included) advances the sequence.
    ++state->writerSeqNum;
}

void icall_rwl_release_writer(Il2CppObject* self)
{
    RwLockState* state = AsState(self);
    if (state->writeLevel <= 0)
    {
        ThrowNotHeld("The writer lock is not held by the current thread.");
    }
    --state->writeLevel;
}

bool icall_rwl_get_is_reader_lock_held(Il2CppObject* self)
{
    return AsState(self)->readLevel > 0;
}

bool icall_rwl_get_is_writer_lock_held(Il2CppObject* self)
{
    return AsState(self)->writeLevel > 0;
}

int32_t icall_rwl_get_writer_seq_num(Il2CppObject* self)
{
    return AsState(self)->writerSeqNum;
}

bool icall_rwl_any_writers_since(Il2CppObject* self, int32_t seqNum)
{
    // measured net48: exactly `WriterSeqNum > seqNum`.
    return AsState(self)->writerSeqNum > seqNum;
}

}} // namespace il2cpp::runtime
