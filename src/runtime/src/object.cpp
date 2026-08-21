//------------------------------------------------------------------------------
// object.cpp -- object allocation and reference-field query.
//
// M2 object allocation contract:
//   il2cpp_object_new(klass) returns a freshly zero-initialized instance whose
//   klass header points at the request descriptor. Zero-initialization is a
//   hard requirement: .NET field semantics state that every field (value or
//   reference) starts at its default value (0 / null), and the mark-sweep
//   collector relies on a nulled reference field being a well-formed root.
//
// M2 backing store: allocation now delegates to the mark-sweep collector
// (il2cpp_gc_alloc in gc.cpp). The public API and the call sites in generated
// code are unchanged by this swap.
//------------------------------------------------------------------------------

#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_value_semantics.h" // icall_object_get_hash_code
#include "typeinfo.h"               // il2cpp_object_construct: the .ctor fnPtr + signature

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {

//------------------------------------------------------------------------------
// il2cpp_object_new -- public allocation entry point used by generated newobj.
//
//   Delegates to the GC heap: the collector returns a zero-initialized block
//   whose klass header identifies the concrete type, and is the sole owner of
//   the memory thereafter. instanceSize must be >= sizeof(Il2CppObject); the
//   transpiler guarantees this for every emitted descriptor (the object header
//   is the first struct member).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_object_new(Il2CppClass* klass)
{
    return il2cpp_gc_alloc(klass);
}

//------------------------------------------------------------------------------
// il2cpp_object_construct -- the managed ctor invocation mechanism (COM stage
// 17 batch 3; see il2cpp_runtime.h for the full contract). Allocates via
// il2cpp_object_new, resolves the type's ".ctor" through its typeinfo and
// runs it with the decoded intptr carriers; any failure returns nullptr.
//------------------------------------------------------------------------------

// Decode the i-th parameter's encoding from the signature string
// "<return>(<p0>,<p1>,...)". Returns 'r' for a reference parameter (r:Type),
// 'i' for an int32 parameter (i / V:Type -- the enum underlying value), and
// 0 when the parameter does not exist or the signature is malformed.
static char DecodeCtorParamEncoding(const char* signature, int32_t index)
{
    const char* p = std::strchr(signature, '(');
    if (p == nullptr)
    {
        return 0;
    }
    ++p; // skip '('
    for (int32_t i = 0; i <= index; ++i)
    {
        if (*p == ')' || *p == '\0')
        {
            return 0; // fewer parameters than requested
        }
        const char* start = p;
        while (*p != ',' && *p != ')' && *p != '\0')
        {
            ++p;
        }
        if (i == index)
        {
            if (p - start >= 2 && start[0] == 'r' && start[1] == ':')
            {
                return 'r';
            }
            return 'i'; // i / V:Type -- both int32 carriers
        }
        if (*p == ',')
        {
            ++p;
        }
    }
    return 0;
}

// Validate the ctor's parameter encodings: every parameter must be 'i' or
// 'V:' (int32) except position 0, which may be 'r:' (a reference -- the
// minimal surface the mechanism supports, the DragEventArgs
// (IDataObject,int,int,DragDropEffects) shape). A reference at any other
// position, a malformed signature or an unknown encoding is unsupported.
static bool CtorParamsSupported(const char* signature, int32_t paramCount)
{
    for (int32_t i = 0; i < paramCount; ++i)
    {
        const char enc = DecodeCtorParamEncoding(signature, i);
        if (enc == 0 || (enc == 'r' && i != 0))
        {
            return false;
        }
    }
    return true;
}

Il2CppObject* il2cpp_object_construct(Il2CppClass* klass,
                                      intptr_t a0, intptr_t a1,
                                      intptr_t a2, intptr_t a3)
{
    if (klass == nullptr)
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name);
    if (info == nullptr)
    {
        return nullptr; // the type has no reflected typeinfo
    }
    const Il2CppMethodInfo* ctor = il2cpp_type_get_method(info, ".ctor");
    if (ctor == nullptr || ctor->fnPtr == nullptr || ctor->signature == nullptr)
    {
        return nullptr; // no .ctor / runtime-unreachable fnPtr / no signature
    }
    if (ctor->signature[0] != 'v')
    {
        return nullptr; // a ctor must return void
    }
    const int32_t n = ctor->paramCount;
    if (n < 0 || n > 4 || !CtorParamsSupported(ctor->signature, n))
    {
        return nullptr; // unsupported arity / parameter encodings
    }
    Il2CppObject* obj = il2cpp_object_new(klass);
    const bool ref0 = DecodeCtorParamEncoding(ctor->signature, 0) == 'r';
    switch (n)
    {
        case 0:
            reinterpret_cast<void (*)(Il2CppObject*)>(ctor->fnPtr)(obj);
            break;
        case 1:
            if (ref0)
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*)>(ctor->fnPtr)(
                    obj, reinterpret_cast<Il2CppObject*>(a0));
            else
                reinterpret_cast<void (*)(Il2CppObject*, int32_t)>(ctor->fnPtr)(
                    obj, static_cast<int32_t>(a0));
            break;
        case 2:
            if (ref0)
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*, int32_t)>(ctor->fnPtr)(
                    obj, reinterpret_cast<Il2CppObject*>(a0), static_cast<int32_t>(a1));
            else
                reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t)>(ctor->fnPtr)(
                    obj, static_cast<int32_t>(a0), static_cast<int32_t>(a1));
            break;
        case 3:
            if (ref0)
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*, int32_t, int32_t)>(ctor->fnPtr)(
                    obj, reinterpret_cast<Il2CppObject*>(a0), static_cast<int32_t>(a1),
                    static_cast<int32_t>(a2));
            else
                reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t, int32_t)>(ctor->fnPtr)(
                    obj, static_cast<int32_t>(a0), static_cast<int32_t>(a1),
                    static_cast<int32_t>(a2));
            break;
        case 4:
            if (ref0)
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*, int32_t, int32_t, int32_t)>(ctor->fnPtr)(
                    obj, reinterpret_cast<Il2CppObject*>(a0), static_cast<int32_t>(a1),
                    static_cast<int32_t>(a2), static_cast<int32_t>(a3));
            else
                reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t, int32_t, int32_t)>(ctor->fnPtr)(
                    obj, static_cast<int32_t>(a0), static_cast<int32_t>(a1),
                    static_cast<int32_t>(a2), static_cast<int32_t>(a3));
            break;
    }
    return obj;
}

//------------------------------------------------------------------------------
// il2cpp_null_fail -- COLD failure path for the inline null check.
// The inline hot path in il2cpp_object.h performs the null test and reaches
// this function only on failure; keeping the fprintf + std::abort machinery
// out-of-line keeps it out of every hot dereference loop.
//------------------------------------------------------------------------------
void il2cpp_null_fail()
{
    std::fprintf(stderr, "il2cpp_null_check: NullReferenceException (null receiver dereferenced)\n");
    std::abort();
}

//------------------------------------------------------------------------------
// il2cpp_class_is_ref_field -- is the field at 'offset' a GC reference?
//
//   The reference-field bitmap is indexed by POINTER SLOT, not by byte offset:
//   slot = offset / sizeof(void*). This matches the field layout rule of the
//   transpiler (each reference-typed field occupies exactly one pointer slot).
//
//   Out-of-range offsets (negative, or a slot beyond the 32-bit bitmap) are
//   never produced by the transpiler; they are rejected defensively here with
//   false so the caller cannot read garbage from an undefined shift.
//------------------------------------------------------------------------------
bool il2cpp_class_is_ref_field(Il2CppClass* klass, int32_t offset)
{
    const int32_t slot = offset / static_cast<int32_t>(sizeof(void*));
    if (slot < 0 || slot >= 32)
    {
        return false;
    }
    return (klass->refFieldBitmap & (1u << slot)) != 0;
}

} // namespace il2cpp

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// icall_value_type_get_hash_code_ref -- System.ValueType.GetHashCode default
// for a value type whose FIRST instance field is a GC reference (the
// reference arm of the transpiler's per-kind value-type hash wiring; see
// MethodMap.ValueType.cs and il2cpp_array_ext.h). 'data' is the value-type
// receiver's ADDRESS; the first field always sits at offset 0 (the
// transpiler's value-type slot grid has no object header). The referenced
// object is hashed through icall_object_get_hash_code (string -> ordinal
// content, boxed Int32 -> the value, everything else -> address hash); a
// null reference field -> 0 -- the net48 documented "first non-static field
// hash (null -> 0)" rule.
//------------------------------------------------------------------------------
int32_t icall_value_type_get_hash_code_ref(const void* data)
{
    Il2CppObject* obj = *reinterpret_cast<Il2CppObject* const*>(data);
    return obj != nullptr ? icall_object_get_hash_code(obj) : 0;
}

} // namespace runtime
} // namespace il2cpp
