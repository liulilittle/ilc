#pragma once
//------------------------------------------------------------------------------
// il2cpp_object.h -- managed object header.
//
// Every managed object (a translated .NET class instance) physically begins
// with this header: a single pointer to its runtime type descriptor
// (Il2CppClass, see il2cpp_metadata.h). The generated struct for a translated
// type embeds this header as its first member, so an Il2CppObject* aliases the
// beginning of every object on the heap and the klass pointer is always found
// at offset 0.
//
// M2 scope note: the header is exactly one pointer. There is NO GC mark bit in
// the object itself yet -- the M2 collector keeps its mark state in a separate
// side table (see the GC task). A generational header redesign is deferred to
// M3; the layout here is stable and callers must never assume more than the
// klass pointer exists.
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp {

// Forward declaration: full definition lives in il2cpp_metadata.h. This header
// must stay dependency-free so that generated code can include it in isolation
// (only the pointer type is needed for the klass member).
struct Il2CppClass;

// GC object header: every managed object begins with its class pointer.
struct Il2CppObject
{
    Il2CppClass* klass; // runtime type descriptor (compiled-time generated static table)
};

//------------------------------------------------------------------------------
// il2cpp_null_check -- .NET null-receiver semantics for generated code.
//
//   Every instance dereference (field access, instance call, cast) goes
//   through this helper. A null receiver is a NullReferenceException in .NET
//   semantics; the runtime raises it as a loud abort (M2/M3 stand-in -- the
//   managed exception object arrives with the M3 exception task).
//
//   INLINE (generated-code audit Top 1): the hot path is a single null test;
//   the failure path is COLD -- one out-of-line [[noreturn]] abort call
//   (il2cpp_null_fail, defined in object.cpp) that the optimizer sinks out
//   of hot loops. Returns the (non-null) receiver so it can be inlined at
//   the call site: il2cpp_null_check((Il2CppObject*)obj)->field
//------------------------------------------------------------------------------

// Cold failure path for il2cpp_null_check (defined in object.cpp); never
// returns. Declared here (not defined) so this header stays dependency-free.
[[noreturn]] void il2cpp_null_fail();

inline Il2CppObject* il2cpp_null_check(Il2CppObject* obj)
{
    if (obj != nullptr)
    {
        return obj;
    }
    il2cpp_null_fail();
}

} // namespace il2cpp
