//------------------------------------------------------------------------------
// framestack.cpp -- thread-local managed frame stack (see il2cpp_frame.h).
//
// The frame stack is a PER-THREAD linked list (thread_local head + counters).
// Each thread pushes/pops its OWN method frames: a frame record is a C++
// stack local of one method invocation, so its whole lifetime is on the
// thread that created it -- sharing the chain across threads would corrupt
// it (a worker's pop would "fix" the top the owner just pushed, and the
// owner's next pop aborts "frame ... is not the current top"). The collector
// walks the CALLING thread's chain as a GC root set (each frame's refs array
// holds live reference slots).
//
// KNOWN SCOPE (M-stage, single-threaded collector): il2cpp_frame_top() walks
// only the current thread's chain, so the collector misses frame roots on
// OTHER threads. The M-stage allocator is single-threaded (only the main
// thread allocates and triggers collections; workers never allocate), and
// cross-thread-referenced objects (e.g. a shared lock gate) stay rooted via
// the static-field/global root set -- so no live object is reclaimed in the
// current model. A multi-threaded collector must walk every thread's chain;
// that is a separate threading-collector task, not this fix.
//
// Invariant (1:1 pairing -- distributed-laws): every il2cpp_frame_push has
// EXACTLY ONE matching il2cpp_frame_pop; the popped frame MUST be the current
// top. A violated pairing aborts loudly (a corrupted frame stack would
// produce garbage roots -- far worse than a crash).
//
// The pairing is enforced on BOTH sides: pushing a frame that is already on
// the chain (a second push before the matching pop) is the same violation --
// it would splice the frame in twice and create a cycle (frame->prev points
// at itself once the old link is overwritten) that no pop can break and that
// every chain walk (GC root scan, exception report) would loop on forever.
// il2cpp_frame_push therefore aborts loudly too, so a chain cycle can never
// be constructed in the first place.
//------------------------------------------------------------------------------

#include "il2cpp_frame.h"

#include <cstdio>
#include <cstdlib>

namespace il2cpp {

namespace {

// Thread-local frame stack head. A shared (non-thread-local) head is a
// cross-thread data race: worker threads push/pop onto ONE chain and corrupt
// each other's frames -- the worker's pop aborts "frame ... is not the current
// top" (conccases C21 lock/Monitor/ThreadPool sections). thread_local gives
// every OS thread its own chain (C++11, all platforms; same mechanism as
// pinvoke.cpp's thread_local g_lastError).
thread_local Il2CppFrame* g_frameHead = nullptr;

// Diagnostic depth cap for il2cpp_frame_dump: a valid single-threaded chain
// is bounded by the C++ call stack (each frame record is >= 32 bytes; the
// default 1MB stack cannot host 65536 frames), so a chain longer than this is
// corrupt (cycle / dangling prev) -- the dump aborts loudly instead of
// walking it forever.
const int kMaxDumpDepth = 65536;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_frame_push -- push a frame (see header).
//------------------------------------------------------------------------------
// Push/pop counters are PER-THREAD (thread_local), like the chain they count.
// A global counter would mix threads: the balance of the CALLING thread is
// exactly its own chain length, which is what the dump cross-check verifies
// (walked depth == balance). A global sum has no single chain to validate
// against and would spuriously abort the dump whenever another thread has
// frames live. See also il2cpp_frame_balance in the header.
static thread_local int g_pushCount = 0;
static thread_local int g_popCount = 0;

void il2cpp_frame_push(Il2CppFrame* frame)
{
    if (frame == nullptr)
    {
        std::fprintf(stderr, "il2cpp_frame_push: null frame\n");
        std::abort();
    }
    // 1:1 pairing (push side): a frame already on the chain was pushed without
    // its matching pop. Pushing it again would overwrite prev and insert the
    // frame a second time -- a cycle (see the file comment) that no subsequent
    // pop can untangle and every chain walk would hang on. Abort loudly.
    for (Il2CppFrame* cur = g_frameHead; cur != nullptr; cur = cur->prev)
    {
        if (cur == frame)
        {
            std::fprintf(stderr,
                         "il2cpp_frame_push: frame %p is already on the frame stack (1:1 pairing violated)\n",
                         static_cast<void*>(frame));
            std::abort();
        }
    }
    ++g_pushCount;
    frame->prev = g_frameHead;
    g_frameHead = frame;
}

//------------------------------------------------------------------------------
// il2cpp_frame_pop -- pop the current top frame (see header).
// IDEMPOTENT: popping a frame that is no longer on the stack (already popped)
// is a silent no-op -- the RAII destructor calls this unconditionally, and a
// frame's destructor may run after an explicit pop. Popping a frame that is
// on the stack but NOT the top is a programming error (corrupted pairing)
// and aborts loudly. null is never a valid frame argument (symmetric with
// il2cpp_frame_push) and also aborts loudly.
//------------------------------------------------------------------------------
void il2cpp_frame_pop(Il2CppFrame* frame)
{
    if (frame == nullptr)
    {
        std::fprintf(stderr, "il2cpp_frame_pop: null frame\n");
        std::abort();
    }
    if (g_frameHead == nullptr)
    {
        return; // stack already empty: this frame was popped earlier
    }
    if (g_frameHead != frame)
    {
        // Not the top: either already popped (walk the chain to confirm) or
        // a corrupted pairing. Confirm by searching the chain.
        for (Il2CppFrame* cur = g_frameHead; cur != nullptr; cur = cur->prev)
        {
            if (cur == frame)
            {
                // Found on the stack but not at the top: corrupted pairing.
                std::fprintf(stderr, "il2cpp_frame_pop: frame %p is not the current top (%p)\n",
                             static_cast<void*>(frame), static_cast<void*>(g_frameHead));
                std::abort();
            }
        }
        return; // not on the stack: already popped (idempotent)
    }
    ++g_popCount;
    g_frameHead = frame->prev;
    frame->prev = nullptr; // detached: no dangling link
}

//------------------------------------------------------------------------------
// il2cpp_frame_top -- the current top frame (see header).
//------------------------------------------------------------------------------
// Per-thread balance: number of frames the CALLING thread currently has on its
// own stack (counters are thread_local, so this is race-free). The dump
// cross-checks this against the walked chain length of the same thread.
int il2cpp_frame_balance() { return g_pushCount - g_popCount; }

Il2CppFrame* il2cpp_frame_top()
{
    return g_frameHead;
}

//------------------------------------------------------------------------------
// il2cpp_frame_dump -- diagnostic dump of the frame chain (see header).
//
// Writes the label, the push/pop balance (counter view), every frame in
// innermost-first order (index, method name, address), and the walked chain
// length -- then CROSS-CHECKS the two length views: balance (counters) must
// equal the walked depth (chain view). A mismatch or a chain that exceeds the
// depth cap is corruption (cycle / dangling prev) and aborts loudly -- a
// diagnostic must never silently walk a corrupt chain.
//------------------------------------------------------------------------------
void il2cpp_frame_dump(FILE* stream, const char* label)
{
    if (stream == nullptr)
    {
        stream = stderr;
    }
    std::fprintf(stream, "%s: managed frame stack (balance %d)\n",
                 label != nullptr ? label : "frame dump",
                 il2cpp_frame_balance());
    int depth = 0;
    for (Il2CppFrame* frame = g_frameHead; frame != nullptr; frame = frame->prev)
    {
        if (depth >= kMaxDumpDepth)
        {
            // A valid chain is bounded by the C++ call stack (see the cap
            // comment above); exceeding it means a cycle -- abort, never hang.
            std::fprintf(stream,
                         "  [corrupt] frame chain exceeds %d frames (cycle or dangling prev) at depth %d (%p)\n",
                         kMaxDumpDepth, depth, static_cast<void*>(frame));
            std::abort();
        }
        std::fprintf(stream, "  [%d] %s (%p)\n",
                     depth,
                     frame->methodName != nullptr ? frame->methodName : "(unknown frame)",
                     static_cast<void*>(frame));
        ++depth;
    }
    if (depth == 0)
    {
        std::fprintf(stream, "  (no managed frames on the stack)\n");
    }
    if (depth != il2cpp_frame_balance())
    {
        std::fprintf(stream,
                     "  [corrupt] counter balance %d does not match walked chain length %d\n",
                     il2cpp_frame_balance(), depth);
        std::abort();
    }
}

} // namespace il2cpp



