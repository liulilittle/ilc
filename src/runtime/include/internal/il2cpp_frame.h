#pragma once
//------------------------------------------------------------------------------
// il2cpp_frame.h -- managed frame records (M3 Task 9: frame stack = GC safe
// point = root scanning; the same mechanism serves StackTrace later).
//
//   struct Il2CppFrame
//   {
//       Il2CppFrame* prev;        // previous frame (caller) -- set by push
//       const char*  methodName;  // the transpiled method's C++ name (diagnostics)
//       void**       refs;        // array of reference-slot ADDRESSES
//       int32_t      refCount;    // number of reference slots
//   };
//
//   Generated method prologue (every method with reference locals):
//       Il2CppFrame __frame = { nullptr, "Foo_Bar_hash", refsArray, N };
//       il2cpp_frame_push(&__frame);
//   and every 'return' (and the method exit) pops:
//       il2cpp_frame_pop(&__frame);
//
//   The frame stack is a per-thread linked list. The collector walks it as a
//   GC root set: every refs[i] address holds a live object reference while
//   the method is on the call stack. The M2 per-method add/remove root range
//   protocol is upgraded to this unified frame model (the API is additive:
//   add_root_range/remove_root_range remain for non-frame roots).
//
//   M3 scope: root scanning + method-name diagnostics. Full StackTrace
//   reconstruction (exception-time frame dump) is the Task 10 extension.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdio>
#include <cstdint>

namespace il2cpp {

// Forward declaration of the frame struct (the pop declaration below refers
// to it; the full definition follows).
struct Il2CppFrame;

// Forward declaration (defined below): pop a frame. The frame struct's RAII
// destructor calls this, so the declaration must precede the struct.
void il2cpp_frame_pop(Il2CppFrame* frame);

// Managed frame record (see header comment).
struct Il2CppFrame
{
    Il2CppFrame* prev;       // caller's frame (linked list)
    const char*  methodName; // diagnostics: the transpiled method name
    void**       refs;       // reference-slot addresses (GC roots)
    int32_t      refCount;   // number of slots in 'refs'

    // RAII pop (M3 Task 10): the frame pops itself when the method's stack
    // storage goes out of scope -- on BOTH the normal return path and the
    // exception-unwind path (C++ guarantees destructor execution during
    // unwinding). il2cpp_frame_pop is idempotent for a frame that is no
    // longer on the stack (already popped), so the destructor can always
    // call it. This is what keeps the frame stack consistent across
    // exceptions. NOTE: the pop guard must NOT test prev (the outermost
    // frame's prev is null when the stack was empty at push -- testing it
    // would skip the pop).
    ~Il2CppFrame()
    {
        il2cpp_frame_pop(this);
    }
};

// Push a frame onto the thread-local frame stack. The frame storage is owned
// by the generated method (a local variable) -- it must outlive the push
// (until the matching pop). Frames are pushed in call order; the collector
// walks the chain as a root set. 1:1 pairing is enforced on the push side
// too: pushing a frame that is already on the chain (a second push before the
// matching pop) is a programming error and aborts loudly (it would create an
// unbreakable chain cycle).
void il2cpp_frame_push(Il2CppFrame* frame);

// Pop a frame (must be the CURRENT top -- the method's own frame). Paired
// with push (1:1); popping a frame that is not the top is a programming
// error and aborts loudly. IDEMPOTENT for a frame that is no longer on the
// stack (already popped) -- the RAII destructor can always call it. null is
// never a valid argument (symmetric with push) and aborts loudly.
void il2cpp_frame_pop(Il2CppFrame* frame);

// The current top frame (null when the stack is empty). Diagnostic/scanning.
Il2CppFrame* il2cpp_frame_top();

// Diagnostic: push/pop balance of the CALLING thread (0 when every push was
// popped). The counters are per-thread (thread_local, race-free), matching
// the per-thread chain -- the value is the calling thread's own chain length.
// Test helper.
int il2cpp_frame_balance();

// Diagnostic dump: write the frame chain to 'stream' (stderr when null) --
// label, counter balance, every frame innermost-first (index / method name /
// address), and the walked chain length. Cross-checks the counter balance
// against the walked length and aborts loudly on a mismatch, a chain longer
// than the depth cap, or (by construction, see framestack.cpp) a cycle.
// Crash/exception diagnostics and tests.
void il2cpp_frame_dump(FILE* stream, const char* label);

} // namespace il2cpp

