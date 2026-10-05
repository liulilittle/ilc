#pragma once
//------------------------------------------------------------------------------
// il2cpp_api.h -- the ILC runtime's public C API (M4 Task 7).
//
// This header is the bridge for EXTERNAL C/C++ programs that link against
// the transpiled library (product form: static/shared library). All symbols
// are extern "C" with stable names -- the C ABI contract.
//
// M4 API set (foundation):
//   il2cpp_runtime_init()            -- initialize the runtime (idempotent)
//   il2cpp_gc_collect()              -- force a full collection
//   il2cpp_type_from_name(name)      -- resolve a type descriptor by full name
//   il2cpp_method_invoke_xxx()       -- invoke a transpiled static method
//   il2cpp_get_last_exception()      -- last managed exception message
//
// ABI notes:
//   * Managed objects are opaque (Il2CppObject*). External code MUST NOT
//     dereference them -- access goes through the API.
//   * Exported method calls (static, blittable signature) go through the
//     method-invoke family; the export contract (iron rule) forbids
//     object/string/array crossing the boundary directly.
//------------------------------------------------------------------------------

#include <cstdint>

#if defined(_WIN32)
    #define IL2CPP_C_API __declspec(dllexport)
#else
    #define IL2CPP_C_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Initialize the runtime (idempotent). Call once before any other API.
// NOTE: this symbol is provided by the TRANSPILED library (generated
// startup code registers the translated types/exports), not the runtime.
IL2CPP_C_API void il2cpp_runtime_init(void);

// Force a full mark-sweep cycle.
IL2CPP_C_API void il2cpp_gc_collect(void);

// Resolve a type descriptor by full type name (e.g. "Calc.Program").
// Returns a pointer the caller passes back to invoke APIs; never
// dereferenced externally. Returns 0 when the type is unknown.
IL2CPP_C_API void* il2cpp_type_from_name(const char* name);

// Invoke a transpiled STATIC method returning int32 with up to 4 int32
// arguments, identified by the transpiled C++ name (e.g.
// "Cases_Calculator_Add_<hash>"). Returns 0 and records the exception when
// the method throws. Returns -1 when the method is not found.
IL2CPP_C_API int32_t il2cpp_method_invoke_i4(void* typeHandle, const char* methodName, int32_t a0, int32_t a1, int32_t a2, int32_t a3);

// The message of the last managed exception caught by the API boundary
// (null-terminated; valid until the next API call). Empty string when no
// exception is pending.
IL2CPP_C_API const char* il2cpp_get_last_exception(void);

#ifdef __cplusplus
} // extern "C"
#endif
