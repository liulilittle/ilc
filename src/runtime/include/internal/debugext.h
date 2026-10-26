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
#include "il2cpp_array.h"         // Il2CppArray (the composite-format args)
#include "il2cpp_value_semantics.h" // icall_object_to_string (the object forms)

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

// System.Diagnostics.Debug.WriteLine(object) / Trace.WriteLine(object) /
// Trace.Write(object): the object's ToString text (net48 semantics -- a null
// value raises NullReferenceException through icall_object_to_string, and a
// boxed primitive formats its value). WriteLine appends the newline, Write
// does not.
void il2cpp_debug_write_line_obj(Il2CppObject* value);
void il2cpp_trace_write_obj(Il2CppObject* value);

//--------------------------------------------------------------------------
// D-DEBUG-REST (2026-10-27): the remaining net48 Debug/Trace overloads.
//
// net48 (System_compmod_system_diagnostics_Debug.cs) has, in addition to
// the string/object forms above:
//   * the CATEGORY forms -- Write/WriteLine(string message, string category)
//     and Write/WriteLine(object value, string category). The Trace class
//     implementation (reference lines 497/507/522/532) renders
//     `category + ":" + (value/message ?? "")` -- a null category degrades
//     to the category-less form, a null message/value to the empty string.
//   * the COMPOSITE-FORMAT form -- WriteLine(string format, object[] args):
//     String.Format(CultureInfo.InvariantCulture, format, args) then the
//     line writer (reference lines 240/502). The formatting authority is
//     icall_string_format (the same kernel the console module's fmt_array
//     icalls use).
//   * Debug.Fail(string message, string detailMessage) / Trace.Fail(string)
//     / Trace.Fail(string,string): the assertion-failure forms (reference
//     lines 141/384/389) -- "Assertion Failed: <message>" and, for the
//     detail form, the detail message on the following line.
//   * Trace.Assert(bool) / (bool,string) / (bool,string,string): the
//     Trace-owned assertion forms (reference lines 355/360/366) -- the same
//     print-and-continue contract as il2cpp_debug_assert.
//
// All of these are [Conditional("DEBUG")] on the managed side, so a Release
// build strips the call sites; the mappings exist so a DEBUG-defined build
// resolves instead of failing LOUD.
//--------------------------------------------------------------------------

// Category forms (Debug -> stdout, Trace -> stdout; WriteLine appends \n).
void il2cpp_debug_write_line_category(Il2CppString* message, Il2CppString* category);
void il2cpp_debug_write_category(Il2CppString* message, Il2CppString* category);
void il2cpp_debug_write_line_obj_category(Il2CppObject* value, Il2CppString* category);
void il2cpp_debug_write_obj_category(Il2CppObject* value, Il2CppString* category);

void il2cpp_trace_write_line_category(Il2CppString* message, Il2CppString* category);
void il2cpp_trace_write_category(Il2CppString* message, Il2CppString* category);
void il2cpp_trace_write_line_obj_category(Il2CppObject* value, Il2CppString* category);
void il2cpp_trace_write_obj_category(Il2CppObject* value, Il2CppString* category);

// Composite-format forms (Debug -> stdout, Trace -> stdout).
void il2cpp_debug_write_line_fmt(Il2CppObject* format, Il2CppArray* args);
void il2cpp_trace_write_line_fmt(Il2CppObject* format, Il2CppArray* args);

// Fail / Assert forms (stderr for the failure text; stdout for the Trace
// write forms above).
void il2cpp_debug_fail_detail(Il2CppString* message, Il2CppString* detailMessage);
void il2cpp_trace_fail(Il2CppString* message);
void il2cpp_trace_fail_detail(Il2CppString* message, Il2CppString* detailMessage);
void il2cpp_trace_assert(int32_t condition);
void il2cpp_trace_assert_msg(int32_t condition, Il2CppString* message);
void il2cpp_trace_assert_detail(int32_t condition, Il2CppString* message, Il2CppString* detailMessage);
// Trace.Assert(bool, string, string detailFormat, object[] args): the
// detail is a composite-format string (reference line 447).
void il2cpp_trace_assert_fmt(int32_t condition, Il2CppString* message,
                             Il2CppObject* detailFormat, Il2CppArray* args);

}} // namespace il2cpp::runtime
