#pragma once
// IL2CPP in-house runtime internal header (the generated-code / runtime contract, not external)
// M1 minimal core: Console icalls + static-field registration

#include <cstdint>

// Object-model headers: Il2CppObject (managed object header) and Il2CppClass
// (runtime type descriptor). Included here so every generated translation unit
// that includes this single runtime contract sees the same object-model types
// and the API declared below.
#include "il2cpp_object.h"
#include "il2cpp_metadata.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"
#include "il2cpp_string_intern.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_delegate.h"
#include "il2cpp_frame.h"
#include "il2cpp_pinvoke.h"
// C42 fold fix (culturedeep): cultureinfo.h declares the il2cpp_culture_*
// icalls (il2cpp_culture_get_current -- the transpiler's get_CurrentCulture
// maps to it now). Every generated translation unit includes this contract
// header first, so the generated C++ can see the declaration without a
// CodeGenerator include-table edit.
#include "cultureinfo.h"
// ADO stage 1 (batch 152f): il2cpp_datatable.h declares the System.Data
// icalls (icall_datatable_*) -- every generated translation unit includes
// this contract header first, so the generated C++ can see them.
#include "il2cpp_datatable.h"
// Batch 155d: il2cpp_task.h -- the minimal Task<T> synchronous surface.
#include "il2cpp_task.h"

#if defined(_WIN32)
    #define IL2CPP_NOINLINE __declspec(noinline)
    #define IL2CPP_EXPORT extern "C" __declspec(dllexport)
#else
    #define IL2CPP_NOINLINE __attribute__((noinline))
    #define IL2CPP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace il2cpp {

//------------------------------------------------------------------------------
// Object-model API (namespace il2cpp) -- the contract between generated code
// and the runtime for managed allocation and reference-field queries.
//
// il2cpp_object_new      -- allocate + zero-init + klass-stamp an instance
// il2cpp_class_is_ref_field -- bitmap query: is the field at 'offset' a GC ref
// il2cpp_class_register  -- register a type descriptor (generated startup code)
// il2cpp_class_by_name   -- resolve a descriptor by full type name
//
// M2 note: allocation is calloc-backed; the GC heap replaces the internals in
// the next task with no API change. Registration is a fixed-size static table.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_object_new(Il2CppClass* klass);
bool il2cpp_class_is_ref_field(Il2CppClass* klass, int32_t offset);
void il2cpp_class_register(Il2CppClass* klass);
Il2CppClass* il2cpp_class_by_name(const char* name);
// ASCII case-insensitive variant of il2cpp_class_by_name (Type.GetType's
// ignoreCase semantics). Same null / miss contract as the ordinal lookup.
Il2CppClass* il2cpp_class_by_name_ignore_case(const char* name);

//------------------------------------------------------------------------------
// il2cpp_object_construct -- the managed ctor invocation mechanism (COM stage
// 17 batch 3). Allocates via il2cpp_object_new, then runs the type's ".ctor"
// resolved through its typeinfo (the fnPtr + signature + paramCount -- the
// M5.5 reflection tables). The four intptr carriers a0..a3 carry the ctor
// arguments; each parameter is decoded from the signature encoding (r: ->
// Il2CppObject*, i / V: -> int32) and the call dispatches on paramCount
// 0..4. Any failure -- null klass, no typeinfo, no ".ctor", null fnPtr, an
// unsupported signature (non-'v' return / unknown param encoding / paramCount
// out of 0..4) -- returns nullptr (never throws). The mechanism packs the
// object_new + typeinfo-ctor pieces for the com.cpp bridges and tests; the
// transpiler's own newobj path does NOT use it (generated code inlines simple
// ctors and calls the transpiled ctor directly).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_object_construct(Il2CppClass* klass,
                                      intptr_t a0, intptr_t a1,
                                      intptr_t a2, intptr_t a3);

// M4 Task 7: C API export table -- generated startup code registers each
// transpiled static method whose signature exactly matches the export
// contract int32_t(int32_t,int32_t,int32_t,int32_t). The fn parameter type
// makes mismatched signatures a compile error (no UB casts).
void il2cpp_runtime_register_export(const char* typeName, const char* methodName,
    int32_t (*fn)(int32_t, int32_t, int32_t, int32_t));

//------------------------------------------------------------------------------
// il2cpp_null_check -- .NET null-receiver semantics for generated code.
//
//   Every instance dereference (field access, instance call, cast) goes
//   through this helper. A null receiver is a NullReferenceException in .NET
//   semantics; M2 raises it as a C++ exception carrying a stable message
//   (the full managed exception object / SEH integration is M3's exception
//   system -- this helper's CONTRACT is fixed: null input must never pass
//   through to the dereference).
//
//   Returns the (non-null) receiver so it can be inlined at the call site:
//   il2cpp_null_check((Il2CppObject*)obj)->field
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_null_check(Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_interface_dispatch -- resolve an interface call's vtable slot.
//
//   obj        : the receiver (already null-checked by the caller)
//   iface      : the interface descriptor being dispatched through
//   ifaceSlot  : the slot index on the INTERFACE's virtual table
//
//   Returns the receiver's vtable slot implementing ifaceSlot (the address of
//   the function pointer the caller casts and calls):
//     obj->klass->interfaces  -- linear search for the iface entry (M2: small
//                                interface counts; hash map is M3)
//     entry.slotMap[ifaceSlot] -- interface slot -> type vtable slot
//     return &obj->klass->vtable[typeSlot]
//
//   Contract: the receiver's klass MUST implement the interface (the compiler
//   enforces it at call sites; a missing entry is a runtime programming
//   error and aborts loudly -- never a silent null).
//------------------------------------------------------------------------------
void** il2cpp_interface_dispatch(Il2CppObject* obj, Il2CppClass* iface, int32_t ifaceSlot);

//------------------------------------------------------------------------------
// Boxing / unboxing / type-conformance helpers (Task 9).
//
//   il2cpp_box_int32    : allocate a boxed int32 (managed object, payload at
//                         offset 8) and store the value
//   il2cpp_unbox_int32  : extract the int32 payload (type-checked; wrong type
//                         is InvalidCastException semantics -- M2 aborts)
//   il2cpp_is_instance  : .NET isinst/castclass conformance test (base chain
//                         walk + interface table scan; null -> false)
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_box_int32(Il2CppClass* klass, int32_t value);
int32_t il2cpp_unbox_int32(Il2CppObject* obj);
bool il2cpp_is_instance(Il2CppObject* obj, Il2CppClass* klass);

namespace runtime {

// M1 icall: Console.WriteLine supports int and string constants
void icall_console_write_line_i(int32_t value);
void icall_console_write_line_s(Il2CppObject* value); // managed string (cast internally)
void icall_console_write_line_n(); // WriteLine()
void icall_console_write_i(int32_t value); // Console.Write(int), no newline
void icall_console_write_s(Il2CppObject* value); // Console.Write(string), no newline

// System.Object::.ctor no-op (generated code calls it: the base constructor
// has no fields or behavior; the this placeholder is ignored)
void il2cpp_object_ctor_nop(Il2CppObject* this_);

// M4 Task 5: Environment.NewLine (unified \r\n across platforms to match
// the net48 reference output)
Il2CppObject* icall_environment_get_newline();

//------------------------------------------------------------------------------
// M4 Task 6: Marshal icalls (System.Runtime.InteropServices.Marshal).
//   AllocHGlobal / FreeHGlobal : native heap allocation. On Windows the
//                                buffers come from the GLOBAL heap
//                                (GlobalAlloc GMEM_FIXED / GlobalFree) --
//                                net48 AllocHGlobal is LocalAlloc-backed
//                                (same global-heap family) and the shared
//                                hglobal free must release StringToHGlobalUni
//                                buffers that SetClipboardData GlobalFree's
//                                (a calloc'd ptr freed with GlobalFree =
//                                0xC0000374). POSIX: calloc/free.
//   Copy(array, start, ptr, len) / Copy(ptr, array, start, len):
//                                int32 array <-> native buffer copies.
//
// Pointer-carrier ABI (M4.5 marshalcase fix): the transpiler carries an
// IntPtr value in an intptr_t slot (the emitter's "ptr" result kind), so
// the ALLOC return is intptr_t -- a void* return would need an emitter
// cast the assignment path does not have (intptr_t temp = void* call is
// an implicit pointer->integer conversion error on MSVC/MinGW). The Copy
// array parameters are Il2CppArray* -- the emitter's "a" cast passes
// (Il2CppArray*)(slot) and the two types are unrelated structs in this
// runtime (no implicit conversion either way).
//------------------------------------------------------------------------------
intptr_t icall_marshal_alloc_hglobal(int32_t size);
void icall_marshal_free_hglobal(void* ptr);
void icall_marshal_copy_to_unmanaged(Il2CppArray* array, int32_t start, void* dest, int32_t length);
void icall_marshal_copy_from_unmanaged(void* src, Il2CppArray* array, int32_t start, int32_t length);

// Runtime entry point: the type table registered by generated code
void runtime_init();

} // namespace runtime

} // namespace il2cpp

//------------------------------------------------------------------------------
// Runtime bootstrap -- the runtime's INTERNAL half of il2cpp_runtime_init.
//
// The public C API entry (il2cpp_runtime_init, generated by the transpiler
// in entry.cpp) first calls il2cpp::runtime::runtime_init(), which MUST leave
// the runtime's own global state usable before the generated init proceeds to
// the per-assembly registrations (string pool / resource pool / type tables).
// This function performs that runtime half (the collector heap bootstrap).
// Defined in api.cpp; declared here so both the generated-code-facing
// runtime_init() (runtime.cpp) and the C API layer share one declaration.
//
// Contract: idempotent -- safe to call more than once (the collector heap
// initializes lazily on first use as well; this makes the init explicit).
//------------------------------------------------------------------------------
void il2cpp_runtime_bootstrap();
