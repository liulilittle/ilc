//------------------------------------------------------------------------------
// threadext.cpp -- System.Threading.Thread icall extensions (see threadext.h
// for the contract): Join / Priority set / IsAlive.
//
// These complement thread.cpp (il2cpp_thread.h, icall_thread_* names) which
// already implements sleep / yield / current-thread-id / get-priority. The
// il2cpp_thread_* names avoid collisions with the existing icall_thread_*
// entries; threadext.cpp depends on icall_thread_get_current_thread_id() for
// the is-alive identity comparison.
//
// M-stage model: SINGLE-THREADED. There is exactly one thread -- the current
// one -- so join never blocks, priority is accepted-and-ignored, and only the
// current thread id reports alive. All three entries are platform-neutral
// (no OS calls required); see threadext.h for the net48 deviations.
//------------------------------------------------------------------------------

#include "threadext.h"

#include "il2cpp_thread.h" // icall_thread_get_current_thread_id

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_thread_join -- Thread.Join(int): SIMPLIFIED, never blocks.
//------------------------------------------------------------------------------
void il2cpp_thread_join(int32_t thread_id)
{
    (void)thread_id; // single-threaded model: nothing to wait for
}

//------------------------------------------------------------------------------
// il2cpp_thread_set_priority -- Thread.Priority setter: accepted, ignored.
//------------------------------------------------------------------------------
void il2cpp_thread_set_priority(int32_t priority)
{
    (void)priority; // single-threaded model: priority never changes
}

//------------------------------------------------------------------------------
// il2cpp_thread_get_is_alive -- Thread.IsAlive: only the current thread
// exists in the model, so only its id reports alive.
//------------------------------------------------------------------------------
bool il2cpp_thread_get_is_alive(int32_t thread_id)
{
    return thread_id == icall_thread_get_current_thread_id();
}

}} // namespace il2cpp::runtime
