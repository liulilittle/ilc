#pragma once
//------------------------------------------------------------------------------
// debuggerext.h -- System.Diagnostics.Debugger stub module
// (src/runtime/src/debuggerext.cpp; the System.Diagnostics.Debug/Trace stubs
// live in debugext.h).
//
// Scope: ILC has NO debugger. The attached/logging state is constant and
// Break is a no-op; Log writes to the platform-independent stderr so AOT
// diagnostics survive. net48's Log is a silent no-op when no debugger is
// attached -- the stderr write is a deliberate, audit-sanctioned deviation
// (docs/audit/icall-inventory-net48.md §5 item 7).
//
// STRING ABI: managed strings cross as Il2CppString* (the emitter's "s" cast)
// and are converted to UTF-8 internally -- the console module convention
// (icall_console_write_line_s, runtime.cpp), NOT the const char* convention
// of debugext.h (which has a documented generated-code ABI gap).
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// System.Diagnostics.Debugger.get_IsAttached -- always false: ILC runs
// without a debugger. The bool result crosses in the int32 ABI slot.
int32_t il2cpp_debugger_is_attached();

// System.Diagnostics.Debugger.IsLogging() -- always false: no logging channel.
int32_t il2cpp_debugger_is_logging();

// System.Diagnostics.Debugger.Break() -- no-op: no debugger to break into.
void il2cpp_debugger_break();

// System.Diagnostics.Debugger.Log(int level, string category, string message)
// -- writes the message to stderr (platform-independent). A non-empty
// category prefixes "<category>: ". A null message prints "(null)" (defensive
// -- net48's managed wrapper throws ArgumentNullException before reaching the
// icall, but ILC calls the icall directly). The level is accepted for
// signature compatibility but not printed (it is a debugger filter hint).
void il2cpp_debugger_log(int32_t level, Il2CppString* category, Il2CppString* message);

}} // namespace il2cpp::runtime
