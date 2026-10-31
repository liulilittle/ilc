#pragma once
//------------------------------------------------------------------------------
// il2cpp_pinvoke.h -- P/Invoke dynamic resolution (M4 Task 2, P-D4).
//
//   void* il2cpp_pinvoke_resolve(const char* dll, const char* entry);
//
//   Resolves a native symbol at runtime: LoadLibrary + GetProcAddress on
//   Windows, dlopen + dlsym on POSIX. Results are cached (a flat table of
//   {dll, entry, symbol}) so repeated calls to the same P/Invoke entry are
//   a table lookup after the first resolution.
//
//   The transpiler emits calls as:
//     ((int32_t(*)(int32_t,int32_t))il2cpp_pinvoke_resolve("mylib","add_ints"))(a, b)
//   for DYNAMIC binding. STATIC binding (M4 Task 1) links the library at
//   build time and calls the symbol directly -- both paths are supported;
//   the transpiler chooses per-library (a library present in the build as a
//   static lib uses static binding; everything else -- an out-of-manifest or
//   SHARED library -- resolves dynamically, with the extern "C" declaration
//   suppressed so the unlinked symbol never reaches the linker).
//
//   Failure semantics (P-D4): a library that cannot be loaded throws the
//   managed DllNotFoundException carrier, a symbol that cannot be found the
//   EntryPointNotFoundException carrier (both catchable through the generated
//   try/catch via the type chain, matching net48). NULL arguments and table
//   OVERFLOW remain an abort -- internal/programming errors with no net48
//   counterpart.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

// Resolve a native symbol (see header). Returns a function/object pointer.
//
// This is the ORIGINAL two-argument form: it resolves `entry` verbatim (no
// A/W suffix probing) and keeps every pre-existing call site compiling and
// behaving exactly as before. New call sites that carry the [DllImport]
// CharSet/ExactSpelling flags use il2cpp_pinvoke_resolve_probe below.
void* il2cpp_pinvoke_resolve(const char* dll, const char* entry);

//------------------------------------------------------------------------------
// Entry-point suffix probing (net48 parity, CharSet A/W).
//
//   void* il2cpp_pinvoke_resolve_probe(const char* dll, const char* entry,
//                                      int32_t charSet, int32_t exactSpelling);
//
// net48 never binds a [DllImport] at link time: it LoadLibrary's the module and
// probes GetProcAddress with a candidate list derived from the import
// attributes (measured against Framework64 v4.0.30319):
//
//   exactSpelling != 0                      -> [entry]
//   charSet in {0x00 None, 0x02 Ansi}       -> [entry, entry+"A", entry+"W"]
//   charSet in {0x04 Unicode, 0x06 Auto}    -> [entry, entry+"W"]
//
// The first candidate that resolves wins and is cached. When every candidate
// misses, the thrown EntryPointNotFoundException quotes the ORIGINAL `entry`
// (measured: the net48 message names "MessageBox", never "MessageBoxW").
//
// On POSIX the suffix candidates are still probed (the bare name is tried
// first, so a library that exports the exact name resolves as before).
//
// The cache is keyed on (dll, entry, charSet, exactSpelling): two imports that
// share dll+entry but differ in ExactSpelling/CharSet resolve INDEPENDENTLY, so
// the result never depends on which import runs first.
//
// A/W PROBE ORDER: not verified on this machine (the audit measured 0 `only-A`
// exports, so the order is unobservable with the shipped system DLLs). The
// implementation tries "A" before "W"; see .opencode/tmp/impl-entrypoint-suffix/
// NOTES.md for the verification command.
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_resolve_probe(const char* dll, const char* entry,
                                   int32_t charSet, int32_t exactSpelling);

//------------------------------------------------------------------------------
// String marshalling (M4 Task 3).
//
//   const char* il2cpp_pinvoke_string_to_utf8(Il2CppString* s)
//     Marshals a managed string to a null-terminated UTF-8 buffer (CharSet.
//     Ansi). The buffer is owned by the runtime (a small per-thread rotating
//     slot -- valid until the NEXT call on the SAME thread; P/Invoke
//     marshalling is copy-in/copy-out, so the callee consumes it before any
//     further P/Invoke). The encoder is full UTF-8 (1/2/3-byte BMP plus
//     4-byte astral surrogate PAIRS -- encoding each surrogate separately
//     would emit ill-formed UTF-8); lone unpaired surrogates encode as the
//     replacement character U+FFFD, matching .NET marshalling semantics.
//
//   Il2CppString* il2cpp_pinvoke_string_from_utf8(const char* utf8)
//     Marshals a null-terminated UTF-8 buffer back into a managed string
//     (Ansi return values).
//------------------------------------------------------------------------------
const char* il2cpp_pinvoke_string_to_utf8(Il2CppObject* s);
Il2CppObject* il2cpp_pinvoke_string_from_utf8(const char* utf8);

//------------------------------------------------------------------------------
// SetLastError support (M4 Task 4).
//
// .NET's [DllImport(SetLastError=true)] captures the OS error code after the
// call (GetLastError/errno) into a per-thread slot; Marshal.GetLastWin32Error
// reads it. The transpiler emits il2cpp_pinvoke_capture_last_error() after
// calls marked SetLastError, and maps Marshal.GetLastWin32Error to
// il2cpp_pinvoke_get_last_error.
//------------------------------------------------------------------------------
void il2cpp_pinvoke_capture_last_error();
int32_t il2cpp_pinvoke_get_last_error();

} // namespace il2cpp
