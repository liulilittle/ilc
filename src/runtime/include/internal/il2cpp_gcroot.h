#pragma once
//------------------------------------------------------------------------------
// il2cpp_gcroot.h -- RAII rooting of a raw icall parameter across an
// allocation (shared by stackqueue.cpp / arraylist.cpp / sortedlist.cpp).
//
// WHY: a compacting collection (triggered by any allocation when the heap is
// under pressure) MOVES every surviving Gen0 object. The collector rewrites
// every GC-managed reference (frame slots, object fields) to the new address,
// but a raw C++ parameter of an icall is NOT a GC-managed slot: if the icall
// allocates (e.g. a growth path) and keeps using its parameter afterwards,
// the parameter still points at the OLD address. The old memory is never
// freed (the collector leaks old Gen0 addresses by design), so the writes
// "work", but the CALLER's frame slot was rewritten to the moved copy --
// reads and writes silently diverge (stackqueue2 e2e, 2026-08-13: a Queue
// with 20000 elements lost exactly one ++count this way).
//
// FIX: root the parameter's own stack slot in a temporary Il2CppFrame for
// the duration of the allocation. The collector then rewrites the SLOT
// through the frame refs, and Get() re-reads the moved address -- exactly
// the same contract the transpiler relies on for managed locals.
//
// Frame protocol (see gc.cpp root scan): frame->refs[i] is the ADDRESS of a
// reference slot; the collector marks *refs[i] and rewrites it in place. So
// refs_[0] must hold &ref (the variable's own address) -- never an extra
// indirection level, or the collector would treat the variable ADDRESS as
// the object reference and Get() would read garbage.
//------------------------------------------------------------------------------

#include "il2cpp_frame.h"

namespace il2cpp {

template <typename T>
struct GcRoot
{
    Il2CppFrame frame_;
    void*       refs_[1];

    GcRoot(T& ref, const char* name)
    {
        refs_[0] = &ref;
        frame_.methodName = name;
        frame_.refs = refs_;
        frame_.refCount = 1;
        il2cpp_frame_push(&frame_);
    }
    // Il2CppFrame's RAII destructor pops the frame on every exit path (pop is
    // idempotent for an already-popped frame), so no explicit pop is needed.
    // Re-read the (possibly rewritten) rooted variable.
    T& Get()
    {
        return *reinterpret_cast<T*>(refs_[0]);
    }
};

} // namespace il2cpp
