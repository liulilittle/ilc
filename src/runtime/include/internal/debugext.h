#pragma once
//------------------------------------------------------------------------------
// debugext.h -- System.Diagnostics.Debug / System.Diagnostics.Trace /
// System.Console.WriteLine stub module.
//
// Scope: non-fatal diagnostic stubs for the generated-code pipeline. These
// guarantee the program runs to completion instead of crashing: a failed
// Debug.Assert prints a message and CONTINUES (the .NET default Debug.Assert
// shows a message box in interactive hosts; here the simplified contract is
// "print to stderr and continue"). The real .NET Debug.Assert would terminate
// the process when assertions are enabled -- the stub deliberately does NOT,
// so AOT programs with debug leftovers stay runnable.
//
// Output policy (matches the console module): everything goes through the C
// stdio streams (stdout for diagnostics, stderr for assertions) so the
// generated-code output pipeline stays byte-ordered with these calls.
//
// STRING ABI: managed strings cross as Il2CppString* (the emitter's "s" cast)
// and are converted to UTF-8 internally -- the console/debuggerext convention
// (icall_console_write_line_s, debuggerext.cpp), NOT the const char* form of
// the earlier M1 string icalls. The conversion uses the pinvokeext encoder
// (full UTF-8 incl. astral pairs, U+FFFD for lone surrogates -- .NET
// marshalling semantics) and the buffer is freed after the write.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// System.Diagnostics.Debug.Assert(bool, string) stub: when condition == 0
// (the bool->int32_t ABI form), prints "Assertion Failed: <message>" to
// stderr and CONTINUES -- never aborts the process. A null message prints as
// "(null)".
//
// The message parameter is optional (default nullptr): the transpiler emits
// the one-argument Debug.Assert(bool) call against this same entry point, and
// a false condition without a message prints "Assertion Failed: (null)".
void il2cpp_debug_assert(int32_t condition, Il2CppString* message = nullptr);

// System.Diagnostics.Debug.WriteLine(string) / Console.WriteLine(string)
// stub: prints the text plus a trailing newline to stdout.
void il2cpp_debug_write_line(Il2CppString* text);

// System.Diagnostics.Debug.Fail(string) stub: always prints
// "Assertion Failed: <message>" to stderr and CONTINUES -- never aborts.
void il2cpp_debug_fail(Il2CppString* message);

// System.Diagnostics.Trace.Write(string) stub: prints the text to stdout
// WITHOUT a trailing newline (.NET Trace.Write semantics; Trace.WriteLine
// is il2cpp_debug_write_line).
void il2cpp_trace_write(Il2CppString* text);

}} // namespace il2cpp::runtime
