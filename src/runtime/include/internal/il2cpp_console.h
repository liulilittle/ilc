#pragma once
//------------------------------------------------------------------------------
// il2cpp_console.h -- System.Console icall extension module.
//
// Scope: the Console icalls BEYOND the M1 core set, which lives in runtime.cpp
// (icall_console_write_line_i / _s / _n and icall_console_write_i / _s -- not
// redeclared here). This module owns:
//
//   icall_console_write_line_b / _c / _d / _u64
//                                    -- WriteLine(bool | char | double | ulong)
//   icall_console_write_line_object  -- WriteLine(object): the BOXED ToString
//                                       path (hashtable2/hashtablecase
//                                       untracked-box unblock) -- a null value
//                                       prints only the line terminator (net48
//                                       TextWriter.WriteLine(object) null
//                                       semantics), anything else renders via
//                                       icall_object_to_string and the M1
//                                       string printer
//   icall_console_write_b / _c / _d  -- Write(bool | char | double), no newline
//   icall_console_write_u32          -- Write(uint32), no newline (the
//                                       write_line_u32 variant: full-range
//                                       unsigned decimal, 0xFFFFFFFF never -1)
//   icall_console_read_line               -- ReadLine(): one line from stdin as a
//                                            managed string; nullptr on EOF
//   icall_console_read_key                -- ReadKey(bool intercept): blocking
//                                            single-key read; returns the pressed
//                                            character as an int32 code
//
// Value-type marshalling convention (matches the generated-code ABI):
//   bool    -> int32_t (0 / 1)
//   char    -> int32_t (the 16-bit code-unit value; emitted in the active
//              console output encoding -- UTF-8 under a 65001 console, the
//              console code page otherwise (net48 Console.OutputEncoding
//              default); a lone surrogate has no mapping -> '?' outside
//              UTF-8, U+FFFD inside it -- same policy as the M1 string icalls)
//   double  -> double
//   ulong   -> uint64_t (the 8-byte slot value reinterpreted bit-preservingly
//              by the generated "q" cast -- full-range decimal, never routed
//              through the int64 formatter)
//
// The numeric formatting helpers are shared by the whole Write/WriteLine
// family and are exposed here for the unit tests:
//   console_format_int32  -- decimal rendering ("0", "-12345", "2147483647")
//   console_format_u64    -- full-range uint64 decimal rendering
//                            ("0", "18446744073709551615" -- the UInt64ToString
//                            digit loop, valuesemantics.cpp)
//   console_format_double -- .NET-style shortest round-trip rendering
//                            ("1.5", "0.1", "150", "1E+21", "NaN", "Infinity")
//                            -- the net48 reference is .NET's ToString().
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio> // FILE (console_write_units target stream)
#include <string>

// System.IO.TextWriter::Null -- the BCL static-field storage slot (the
// stopwatch.h System_Diagnostics_Stopwatch_sf_Frequency precedent: the
// runtime owns static storage for mscorlib types, which have no TypeDef).
// In the ILC object model TextWriter.Null is the NULL (discarding) console
// stream object -- a REAL object (filled by EnsureConsoleStreams), never
// nullptr once the console streams exist: SetOut/SetError swap it in as the
// discarding stream (net48 semantics), while a null MANAGED argument
// (SetOut(null)) throws ArgumentNullException. Writing to the null stream is
// a no-op. The transpiler emits ldsfld System.IO.TextWriter::Null as a direct
// read of {TypeCppName}_sf_{FieldName}; generated.h includes this header, so
// the declaration is visible to every generated translation unit.
extern il2cpp::Il2CppObject* System_IO_TextWriter_sf_Null;

namespace il2cpp { namespace runtime {

// .NET bool.ToString() semantics: true -> "True", false -> "False".
void icall_console_write_line_b(int32_t value);

// .NET char.ToString() semantics: the single character; code units >= 0x80
// print as '?'. A trailing newline is written.
void icall_console_write_line_c(int32_t value);

// .NET double.ToString() semantics (shortest round-trip, see
// console_format_double). A trailing newline is written.
void icall_console_write_line_d(double value);

// Full-range uint64 decimal rendering (console_format_u64: the UInt64ToString
// digit loop) -- UInt64.MaxValue prints "18446744073709551615", never the
// int64 interpretation -1. A trailing newline is written.
void icall_console_write_line_u64(uint64_t value);

// Full-range uint32 decimal rendering (same console_format_u64 digit loop;
// the uint32 promotes exactly) -- 0xFFFFFFFF prints "4294967295", never the
// signed interpretation -1 (convcase L60/L61). A trailing newline is written.
void icall_console_write_line_u32(uint32_t value);

// .NET WriteLine(object) semantics -- the BOXED ToString path (hashtable2 /
// hashtablecase untracked-box unblock; MethodMap.Console.cs LOUD arm):
//   null                     -> an empty line (net48 TextWriter.WriteLine
//                                (object) prints just the line terminator);
//   anything else            -> icall_object_to_string (boxed primitives print
//                                their VALUE text, strings print themselves,
//                                other objects print the type's full name) via
//                                the M1 string printer (full UTF-8) + newline.
// This is the runtime object printer the transpiler needs for WriteLine(object)
// with a box type that StackFold cannot track statically (Hashtable get_Item
// results etc.).
void icall_console_write_line_object(Il2CppObject* value);

// No-newline variants of the above (System.Console.Write(bool | char | double)).
void icall_console_write_b(int32_t value);
void icall_console_write_c(int32_t value);
void icall_console_write_d(double value);

// No-newline variant of icall_console_write_line_u32 (System.Console.Write
// (uint32)): full-range unsigned decimal via console_format_u64 --
// 0xFFFFFFFF prints "4294967295", never the signed interpretation -1 (the
// convcase Write probe).
void icall_console_write_u32(uint32_t value);

// System.Console.ReadLine(): reads one line from stdin (terminated by '\n';
// a trailing '\r' is stripped so CRLF input yields the same managed string on
// every platform). Returns a managed string, or nullptr on EOF (no characters
// read before end-of-input).
Il2CppObject* icall_console_read_line();

// System.Console.ReadKey(bool intercept): blocking single-key read; returns
// the pressed character as an int32 code (M4 simplification: the intercept
// flag is accepted for signature compatibility but the read is always
// no-echo). -1 (EOF) when no key is available. This is a console-only call
// and does not read redirected input.
int32_t icall_console_read_key(int32_t intercept);

// Shared numeric formatting helpers (see header comment).
std::string console_format_int32(int32_t value);
std::string console_format_u64(uint64_t value);
std::string console_format_double(double value);

// Console line terminator (net48 semantics). On Windows stdout is switched to
// binary mode at startup, so this writes the explicit "\r\n" byte pair; on
// POSIX it writes '\n'. Text mode would translate the '\n' into "\r\n" again,
// corrupting strings that already contain "\r\n" (e.g. Environment.NewLine)
// into "\r\r\n".
void console_write_newline();

// Active console output code page (net48 Console.OutputEncoding default):
// the Win32 console output CP (GetConsoleOutputCP); 65001 == UTF-8 mode,
// 0 (no console) maps to 65001. Other platforms always report 65001.
unsigned console_output_code_page();

// Write UTF-16 code units in the active console output encoding to 'stream'
// (net48 Console.OutputEncoding default -- the console output code page, for
// both the terminal and the redirected channel). CP 65001 keeps the exact
// legacy pair-aware UTF-8 policy (valid pair -> one 4-byte sequence,
// unpaired -> U+FFFD); other code pages convert pair-aware, unmappable input
// becoming '?' (net48 replacement fallback).
void console_write_units(const char16_t* units, int32_t len, FILE* stream);

//------------------------------------------------------------------------------
// Console STREAM surface (console3 work order, 2026-08-09): Console.Out /
// Console.Error are CONSOLE STREAM OBJECTS (header + an int32 kind: Out /
// Error / Null). get_Out / get_Error return the CURRENT stream (swapped by
// SetOut / SetError); every write on a stream object dispatches on its kind.
//
// Semantics (net48 csc-probe pinned):
//   * SetOut(TextWriter.Null) / SetError(TextWriter.Null) make the respective
//     stream a DISCARDING stream -- and redirect the underlying C stdout /
//     stderr to the null device, so even the M1 core icalls (runtime.cpp,
//     which print straight to stdout) are swallowed afterwards (net48:
//     Console.WriteLine writes to Console.Out, the swapped stream).
//   * SetOut(null) / SetError(null) throw ArgumentNullException (paramName
//     "newOut" / "newError", net48 csc-probe) and leave the stream untouched.
//   * The stream objects are GC roots (il2cpp_gc_add_static_root) -- they
//     live for the whole process and must never be collected.
//   * TextWriter.Null is the NULL CONSOLE STREAM OBJECT (the
//     System_IO_TextWriter_sf_Null slot, filled by EnsureConsoleStreams) --
//     distinguishable from a null managed argument.
//------------------------------------------------------------------------------

// Console.Out: the current Out stream object (default: the stdout stream).
Il2CppObject* icall_console_get_out();

// Console.Error: the current Error stream object (default: the stderr stream).
Il2CppObject* icall_console_get_error();

// Console.SetOut(writer) / Console.SetError(writer): swap the respective
// stream. A NULL writer throws ArgumentNullException (paramName "newOut" /
// "newError", net48 csc-probe) and leaves the stream untouched. TextWriter.Null
// (the null stream object, kind kConsoleStreamNull) makes the stream discard
// subsequent output (net48). Any other console stream object is stored as the
// new stream (its kind selects the target).
void icall_console_set_out(Il2CppObject* writer);
void icall_console_set_error(Il2CppObject* writer);

// TextWriter.Write(text) on a console stream object: emits the text (active
// console output encoding, no newline) to the stream's target (stdout /
// stderr / discard). A null text is a no-op (net48 TextWriter.Write(null)).
void icall_console_stream_write(Il2CppObject* self, Il2CppObject* text);

// TextWriter.WriteLine(text) on a console stream object: emits the text
// (active console output encoding) followed by the line terminator. A null
// text (or the DEFAULT -- the transpiler's 0-arg WriteLine() emits just the
// receiver) writes only the terminator (net48 TextWriter.WriteLine(null)).
void icall_console_stream_write_line(Il2CppObject* self, Il2CppObject* text = nullptr);

}} // namespace il2cpp::runtime
