#pragma once
//------------------------------------------------------------------------------
// threadext.h -- System.Threading.Thread icall EXTENSIONS (thread-domain
// completion: Join / Priority set / IsAlive).
//
// The sleep / yield / current-thread-id / get-priority entries already live
// in il2cpp_thread.h (icall_thread_* names, thread.cpp). This header adds
// the remaining Thread surface with an il2cpp_ prefix so the two translation
// units (thread.cpp / threadext.cpp) do not collide; threadext.cpp relies on
// icall_thread_get_current_thread_id() for the is-alive comparison.
//
// M-stage model: the runtime is SINGLE-THREADED -- there is exactly one
// thread, the current one. The three simplified contracts:
//
//   * il2cpp_thread_join(thread_id) -- Thread.Join() semantics WITHOUT the
//     wait: every thread other than the caller is by definition terminated
//     or nonexistent, and the caller cannot join itself, so joining NEVER
//     blocks; the call returns immediately regardless of the id.
//   * il2cpp_thread_set_priority(priority) -- accepted and IGNORED: the
//     priority never changes (icall_thread_get_priority stays 0 =
//     ThreadPriority.Normal, see il2cpp_thread.h). net48 throws
//     ArgumentException for out-of-range enum values; the simplified
//     contract accepts ANY value without validation.
//   * il2cpp_thread_get_is_alive(thread_id) -- true if and only if
//     thread_id equals icall_thread_get_current_thread_id(): the single
//     live thread reports alive; every other id reports false because no
//     such thread exists in the model.
//
// net48 reference semantics (Thread.Join blocks until the target thread
// terminates; Thread.IsAlive is true while the thread runs; Thread.Priority
// validates the enum range) are intentionally simplified per the model
// above; the deviations are documented next to each entry.
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// Thread.Join(int) -- SIMPLIFIED -> void. In the single-threaded model there
// is nothing to wait for (any thread other than the caller is terminated or
// nonexistent), so the call returns immediately and never blocks. net48
// would block until the target thread terminates.
void il2cpp_thread_join(int32_t thread_id);

// Thread.Priority setter (int32) -- SIMPLIFIED -> void. Accepted and IGNORED
// (the single-threaded model never changes priority; icall_thread_get_priority
// always reports 0 = ThreadPriority.Normal). net48 throws
// ArgumentException for out-of-range enum values; the simplified contract
// accepts ANY value without validation.
void il2cpp_thread_set_priority(int32_t priority);

// Thread.IsAlive (int32 thread_id) -- SIMPLIFIED -> bool. Returns true if and
// only if thread_id is the CURRENT thread id (icall_thread_get_current_thread_id):
// the one live thread reports alive, every other id reports false (no such
// thread exists in the model).
bool il2cpp_thread_get_is_alive(int32_t thread_id);

}} // namespace il2cpp::runtime
