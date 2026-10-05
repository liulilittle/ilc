#pragma once
//------------------------------------------------------------------------------
// il2cpp_pinvoke_ext.h -- P/Invoke extension layer (M4 parallel, pinvokeext).
//
// Complements il2cpp_pinvoke.h (which is read-only and owned elsewhere).
// The extension adds:
//
//   1. Heap-allocated UTF-8 string marshalling (argument direction):
//
//        char* il2cpp_pinvoke_string_to_utf8_alloc(Il2CppString* s)
//
//      Unlike the existing static rotating buffer (il2cpp_pinvoke.h), this
//      variant ALLOCATES the output buffer (std::malloc) so the length is
//      unbounded and the buffer stays valid until the caller releases it.
//      The generated P/Invoke call site marshals the argument, passes the
//      buffer to the native callee, then frees it:
//
//        char* utf8 = il2cpp_pinvoke_string_to_utf8_alloc(str);
//        result = native_fn(utf8);
//        il2cpp_pinvoke_utf8_free(utf8);
//
//      Ownership contract: RUNTIME allocates, CALLER frees. The encoder is
//      full UTF-8 (2/3/4-byte sequences incl. astral surrogate pairs);
//      unpaired surrogates encode as the replacement character U+FFFD
//      (.NET marshalling semantics). A null string marshals as an empty
//      allocated buffer (the caller's free path stays uniform).
//
//   1b. UTF-16 (WCHAR*) string marshalling (WinForms stage-1 gap #6:
//      CharSet.Auto 479 P/Invokes all call the W-entry variants):
//
//        char16_t* il2cpp_pinvoke_string_to_utf16_alloc(Il2CppString* s)
//        void      il2cpp_pinvoke_utf16_free(char16_t* utf16)
//
//      The Unicode twin of the UTF-8 alloc pair. The copy is LENGTH-based
//      (s->length units), so embedded NUL units are preserved verbatim --
//      matching .NET's StringToHGlobalUni / CharSet.Unicode argument
//      marshalling. char16_t is explicit (NOT wchar_t -- its width differs
//      between Windows 2 and POSIX 4), so the buffer is UTF-16 on every
//      platform. Ownership: RUNTIME allocates, CALLER frees. A null string
//      marshals as an empty allocated buffer (the caller's free path stays
//      uniform).
//
//      Decode side (PtrToStringUni):
//
//        Il2CppObject* il2cpp_pinvoke_string_from_utf16(const char16_t* utf16)
//        Il2CppObject* il2cpp_pinvoke_string_from_utf16_len(
//            const char16_t* utf16, int32_t length)
//
//      The null-terminated form scans to the first NUL unit
//      (PtrToStringUni(IntPtr) semantics); the explicit-length form copies
//      exactly 'length' units (PtrToStringUni(IntPtr, int) semantics --
//      embedded NULs preserved; negative length clamps to 0). Null input
//      decodes as the empty string (uniform with the UTF-8 decode layer).
//
//   2. Return-value direction (UTF-8 -> managed string) is ALREADY provided
//      by il2cpp_pinvoke_string_from_utf8 in il2cpp_pinvoke.h; this layer
//      reuses it -- nothing is re-implemented here. The UTF-16 decode
//      helpers above are this layer's own return-value/icall direction.
//
//   1c. Byte-array BUFFER parameters (WinForms stage-1 gap #9:
//       GetKeyboardState(byte[256]) form). A byte[] argument marshals as a
//       pointer to the array's payload:
//
//         uint8_t* il2cpp_pinvoke_byte_array_data(Il2CppArray* array)
//
//       The generated P/Invoke call site emits the payload ADDRESS of the
//       managed byte[] and passes it to the native callee, which reads
//       and/or writes the buffer in place (GetKeyboardState writes 256
//       bytes through it). No copy-back is needed: the native callee
//       touches the array's own memory, so the managed side observes the
//       writes after the call. The helper is the byte[] analogue of the
//       existing string marshallers -- a runtime-owned address, validated
//       against the array's actual element kind before it leaves the layer.
//
//   3. Callback registration (delegate -> native callback bridge, paving the
//      way for the delegate milestone):
//
//        void icall_pinvoke_register_callback(const char* name, void* fn)
//        void* il2cpp_pinvoke_find_callback(const char* name)
//
//      Generated code registers a managed static method's address under a
//      stable name; the native side looks it up by name and calls it.
//      Registration is idempotent: re-registering the same name REPLACES
//      the entry (last registration wins). Lookup of an unknown name
//      returns nullptr (never a default). Storage is a flat static table
//      (constraint #12 -- no global-constructor allocation); overflow and
//      null arguments abort loudly, matching il2cpp_pinvoke.h's policy.
//
//   4. SetLastError pre-call cleanup:
//
//        void il2cpp_pinvoke_clear_last_error()
//
//      .NET clears the thread's OS error slot BEFORE invoking a P/Invoke
//      marked SetLastError=true, so the post-call capture reflects only
//      what the callee set. This function is that pre-call reset: it zeroes
//      GetLastError (Windows) / errno (POSIX). The captured slot
//      (il2cpp_pinvoke_get_last_error) is overwritten by the capture that
//      follows the call, so no reset of it is needed here.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

// Marshal a managed string to a heap-allocated null-terminated UTF-8 buffer
// (full UTF-8 incl. astral pairs; lone surrogates -> U+FFFD; null input ->
// empty buffer). The RUNTIME allocates; the caller MUST release the result
// with il2cpp_pinvoke_utf8_free. Allocation failure aborts loudly.
char* il2cpp_pinvoke_string_to_utf8_alloc(Il2CppString* s);

// Release a buffer returned by il2cpp_pinvoke_string_to_utf8_alloc.
// Null-safe (matching std::free semantics).
void il2cpp_pinvoke_utf8_free(char* utf8);

// Marshal a managed string to a NEW heap-allocated null-terminated UTF-16
// (char16_t) buffer. The copy is LENGTH-based (s->length units) -- embedded
// NUL units are preserved verbatim, matching .NET's Unicode marshalling. The
// RUNTIME allocates; the caller MUST release the result with
// il2cpp_pinvoke_utf16_free. Null input -> an empty allocated buffer.
// Allocation failure aborts loudly.
char16_t* il2cpp_pinvoke_string_to_utf16_alloc(Il2CppString* s);

// Release a buffer returned by il2cpp_pinvoke_string_to_utf16_alloc.
// Null-safe (matching std::free semantics).
void il2cpp_pinvoke_utf16_free(char16_t* utf16);

// Decode a null-terminated UTF-16 (char16_t) buffer back into a managed
// string (PtrToStringUni(IntPtr) semantics -- stops at the first NUL unit).
// Null input -> the empty string.
Il2CppObject* il2cpp_pinvoke_string_from_utf16(const char16_t* utf16);

// Decode exactly 'length' units from a UTF-16 buffer into a managed string
// (PtrToStringUni(IntPtr, int) semantics -- embedded NUL units preserved).
// Negative length clamps to 0; null input or length 0 -> the empty string.
Il2CppObject* il2cpp_pinvoke_string_from_utf16_len(const char16_t* utf16, int32_t length);

//------------------------------------------------------------------------------
// Byte-array buffer parameters (WinForms stage-1 gap #9 -- GetKeyboardState
// (byte[256]) form). A byte[] P/Invoke argument marshals as a pointer to the
// array's inline payload:
//
//   uint8_t* il2cpp_pinvoke_byte_array_data(Il2CppArray* array)
//
// The generated call site emits this for every byte[] argument and passes the
// result to the native callee, which reads and/or writes the buffer in place
// (copy-in/copy-out is direct -- the native callee touches the array's own
// memory, so the managed side observes the writes after the call).
//
// VALIDATION: a null array returns nullptr (.NET marshals a null byte[]
// argument as a NULL pointer to the native callee -- pass-through, not a
// crash; the callee's null contract decides the outcome). An array whose
// element size is not 1 (elemSize != 1 -- NOT a byte-like array) aborts
// loudly: the generated code emits the helper only for byte[] parameters,
// so any other element kind is a transpiler-emission bug. sbyte[]/bool[]
// (also elemSize 1) are accepted and marshal identically. The length is not
// checked here: the callee's buffer size is the marshalling contract (.NET
// semantics -- GetKeyboardState documents 256 bytes).
//
// PIN LIMITATION (documented divergence, identical to
// icall_marshal_unsafe_addr_of_pinned_array_element): ILC has no real
// pinning -- the GC is a generational COMPACTING collector, so the returned
// address is valid only until the next collection. P/Invoke marshalling is
// copy-in/copy-out and the call is synchronous (no managed allocation runs
// inside it), so the address is stable for the call's duration -- exactly the
// window the pointer is used. The caller must keep the array rooted across
// the call.
//------------------------------------------------------------------------------
uint8_t* il2cpp_pinvoke_byte_array_data(Il2CppArray* array);

// StringToHGlobalUni icall shape: the UTF-16 heap buffer as an intptr_t
// pointer carrier (the fold's "ptr" result kind -- the emitted
// `intptr_t t = icall(...)` compiles without an emitter return cast). null
// input -> IntPtr.Zero (0, .NET semantics). Release the buffer with
// icall_marshal_free_hglobal.
//
// Windows: the buffer is GlobalAlloc'd (GMEM_FIXED -- a DIRECT pointer, so
// the value stays usable as a plain string pointer AND as a global handle).
// SetClipboardData requires a GlobalAlloc'd object -- the clipboard takes
// ownership and later frees the handle with GlobalFree, so a malloc'd
// pointer would be freed from the WRONG heap (0xC0000374). The paired
// icall_marshal_free_hglobal is GlobalFree on Windows. POSIX (no clipboard
// protocol): std::malloc/std::free.
intptr_t il2cpp_pinvoke_string_to_utf16_hglobal(Il2CppString* s);

// Register a native callback under 'name' (see header). Duplicate names
// replace the previous entry; null name/fn and table overflow abort loudly.
void icall_pinvoke_register_callback(const char* name, void* fn);

// Look up a registered callback by exact name; returns nullptr when absent.
void* il2cpp_pinvoke_find_callback(const char* name);

// Reset the OS error slot (GetLastError/errno) to zero -- the P/Invoke
// pre-call cleanup emitted before calls marked SetLastError=true.
void il2cpp_pinvoke_clear_last_error();

} // namespace il2cpp
