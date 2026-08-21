//------------------------------------------------------------------------------
// reflect_base.cpp -- Activator.CreateInstance + Type.GetType icalls
// (see reflect_base.h for the contract; reflection-status-v2 R-REFL-07/08).
//
// Both icalls share one primitive: resolve the type NAME string through the
// typeinfo registry (il2cpp_typeinfo_lookup -- the same registry the
// Type.GetProperty/GetMethod/GetField queries use, typeinfo.cpp). The
// lookup key is the same ASCII-managed-string transcoding typeinfo.cpp
// uses for its string-ABI queries.
//
//   * create_instance: the hit's Il2CppTypeInfo::klass is the runtime
//     descriptor the transpiler emits per translated type -- the SAME
//     descriptor the generated code allocates through, so instantiation is
//     just il2cpp_object_new(klass) (zero-init + klass-stamp). A miss (or a
//     null klass) means no descriptor exists for the name in this build ->
//     MissingMethodException (.NET "cannot create this type" family).
//   * get_type: query-only -- the hit returns the INPUT string itself (the
//     simplified Type model: a Type VALUE is its full name string, no
//     allocation); a null name throws ArgumentNullException (net48
//     Type.GetType(null) contract, verified against v4.0.30319); every other
//     miss (malformed name, unregistered type) returns nullptr (Type.GetType's
//     "not found -> null" contract, no throw).
//
// ASSEMBLY-QUALIFIED NAME RESOLUTION (reflection mode C, plan §3.4 / C3):
// the input name is NORMALIZED before the registry lookup -- net48 semantics
// (probe-verified against v4.0.30319):
//   * A top-level comma (depth 0, not inside '[' ']' generic brackets) splits
//     the string: the type name is everything BEFORE the comma; the assembly
//     qualifier -- which may itself contain further commas and be quoted,
//     e.g. "Ns.Type, Asm, Version=..., Culture=..., PublicKeyToken=..." -- is
//     DISCARDED. "Ns.Type, Assembly" -> "Ns.Type"; "System.Int32, mscorlib"
//     -> "System.Int32".
//   * Nested types keep their '+' separator (net48: "Outer+Inner" resolves,
//     "Outer.Inner" does NOT -- the dot form is a miss, not a conversion).
//   * Leading spaces before the type name are skipped (" System.Int32"
//     resolves); trailing whitespace is NOT trimmed ("System.Int32 " and
//     "System.Int32 , mscorlib" both miss) -- matching net48's strictness.
//   * Generic arity ("List`1") is preserved verbatim.
// After normalization the lookup checks the typeinfo registry FIRST (user-
// assembly types), then falls back to the builtin class registry
// (il2cpp_class_by_name) so BCL types resolve exactly like net48's mscorlib
// lookup (gettype_bcl: Type.GetType("System.Int32") is non-null).
//------------------------------------------------------------------------------

#include "reflect_base.h"

#include "typeinfo.h"          // il2cpp_typeinfo_lookup / Il2CppTypeInfo
#include "il2cpp_runtime.h"    // il2cpp_object_new
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / _new

#include <cstring>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// AsciiFromManagedString -- UTF-16 managed string -> ASCII C-string buffer.
//
// DUPLICATE NOTE: typeinfo.cpp:199 carries the identical helper for its
// string-ABI queries. Both translation units need it TU-locally; promoting
// it to a shared header is out of this task's scope (typeinfo.h is the
// ownership boundary), so the copy is kept byte-identical on purpose.
//
// Type names are ASCII by project convention (generated code is pure
// ASCII); a null / over-long / non-ASCII input returns false ("not found"
// semantics -- never a mis-compared name).
//------------------------------------------------------------------------------
constexpr int kNameBufferSize = 256;

bool AsciiFromManagedString(Il2CppObject* name, char (&buf)[kNameBufferSize])
{
    if (name == nullptr)
    {
        return false;
    }
    auto s = reinterpret_cast<Il2CppString*>(name);
    if (s->length < 0 || s->length >= kNameBufferSize)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] > 0x7F)
        {
            return false; // non-ASCII: out of the project convention
        }
        buf[i] = static_cast<char>(s->chars[i]);
    }
    buf[s->length] = '\0';
    return true;
}

// MissingMethodException type-name chain (il2cpp_exception.h carrier
// format): the type repeats as the first walk segment (the catch matcher
// walks the chain AFTER the first comma), followed by the .NET base chain
// (MissingMethodException : MissingMemberException : MemberAccessException
// : SystemException : Exception).
constexpr const char* kMissingMethodChain =
    "System.MissingMethodException,System.MissingMethodException,"
    "System.MissingMemberException,System.MemberAccessException,"
    "System.SystemException,System.Exception";

} // namespace

//------------------------------------------------------------------------------
// NormalizeTypeName -- map a Type.GetType / Activator.CreateInstance input
// string to the typeinfo registry key, following net48 assembly-qualified-
// name semantics (probe-verified against v4.0.30319):
//   1. Strip the assembly qualifier: everything from the FIRST top-level
//      comma (bracket depth 0 -- commas inside '[' ']' generic-argument
//      brackets are ignored) onward is the assembly name and is dropped.
//      "Ns.Type, Assembly" -> "Ns.Type";
//      "System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, ..." ->
//      "System.Int32". The ASSEMBLY part (after skipping leading whitespace)
//      must be non-empty, else the whole name is invalid (net48:
//      "System.Int32," / "System.Int32,,," -> null).
//   2. Skip leading spaces of the type name (net48: " System.Int32"
//      resolves); trailing whitespace is PRESERVED ("System.Int32 " does
//      NOT resolve) -- the desktop parser drops whitespace preceding an
//      identifier but keeps whitespace ending one.
//   3. Nested types keep their '+' separator (net48: "Outer+Inner"
//      resolves; "Outer.Inner" does NOT -- the dot form is never a
//      translation of '+').
//   4. Quoted assembly names ("Ns.Type, \"Asm, v1\"") are accepted: the
//      quotes live in the dropped assembly part, so the type-name part is
//      unaffected.
//   5. Generic arity ("List`1") is preserved verbatim (the registry key
//      carries the arity suffix exactly as emitted).
// Returns the length of the normalized key (written NUL-terminated into
// 'key'), or 0 when the input has no usable type-name part (-> "not
// found"). A key of length kNameBufferSize-1 or more is rejected (over-long
// normalized name escapes to "not found", matching the transcoding rule).
//------------------------------------------------------------------------------
int NormalizeTypeName(const char* name, char (&key)[kNameBufferSize])
{
    int depth = 0;
    int i = 0;
    // Skip leading spaces (net48 SkipWhiteSpace drops whitespace preceding
    // an identifier; tabs are NOT treated as whitespace by this parser --
    // "System.Int32\t,\tmscorlib" misses, probe-verified).
    while (name[i] == ' ')
    {
        ++i;
    }
    const int start = i;
    int end = -1; // index of the first top-level comma, or -1 if none
    for (; name[i] != '\0'; ++i)
    {
        const char c = name[i];
        if (c == '[')
        {
            ++depth;
        }
        else if (c == ']' && depth > 0)
        {
            --depth;
        }
        else if (c == ',' && depth == 0)
        {
            end = i;
            break;
        }
    }
    const int typeLen = (end < 0 ? i : end) - start;
    if (typeLen <= 0 || typeLen >= kNameBufferSize)
    {
        return 0; // empty or over-long type-name part
    }
    if (end >= 0)
    {
        // The assembly part must be non-empty after skipping leading spaces
        // (net48: "System.Int32," / "System.Int32,,," / "System.Int32, " ->
        // null, probe-verified).
        int a = end + 1;
        while (name[a] == ' ')
        {
            ++a;
        }
        if (name[a] == '\0' || name[a] == ',')
        {
            return 0;
        }
    }
    std::memcpy(key, name + start, static_cast<size_t>(typeLen));
    key[typeLen] = '\0';
    return typeLen;
}

//------------------------------------------------------------------------------
// il2cpp_activator_create_instance -- Activator.CreateInstance(string).
//
//   null typeName  -> ArgumentNullException (.NET: CreateInstance(null)
//                     throws ArgumentNullException "type").
//   not registered / klass == nullptr / non-ASCII or over-long name ->
//                     MissingMethodException (no descriptor for the name
//                     in this build -- nothing to instantiate; malformed
//                     names escape to "not found", per the transcoding
//                     convention in typeinfo.cpp).
//   registered     -> il2cpp_object_new(klass): a fresh zero-initialized
//                     instance stamped with the descriptor (the standard
//                     generated-code allocation path; the caller then runs
//                     the .ctor through normal generated-code dispatch).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_activator_create_instance(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf))
    {
        il2cpp_throw_new(kMissingMethodChain,
            "Cannot create an instance of the specified type: the type is not "
            "part of this AOT build (not registered in the typeinfo registry).");
    }
    char keyBuf[kNameBufferSize];
    if (NormalizeTypeName(nameBuf, keyBuf) <= 0)
    {
        il2cpp_throw_new(kMissingMethodChain,
            "Cannot create an instance of the specified type: the type is not "
            "part of this AOT build (not registered in the typeinfo registry).");
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(keyBuf);
    if (info == nullptr || info->klass == nullptr)
    {
        il2cpp_throw_new(kMissingMethodChain,
            "Cannot create an instance of the specified type: the type is not "
            "part of this AOT build (not registered in the typeinfo registry).");
    }
    return il2cpp_object_new(const_cast<Il2CppClass*>(info->klass));
}

//------------------------------------------------------------------------------
// il2cpp_type_get_type -- Type.GetType(string).
//
//   null   -> ArgumentNullException (paramName "typeName"; net48
//             Type.GetType(null) verified against v4.0.30319). This is the
//             ONE throwing case of the query -- the project's defensive
//             query icalls return null for null inputs, but get_type mirrors
//             a PUBLIC .NET API whose null contract throws, so it aligns
//             with .NET instead (the same choice create_instance already
//             makes).
//   malformed / not registered -> nullptr (.NET "not found -> null", no
//   throw -- including "" (net48: null, verified), over-long and non-ASCII
//   names escaping to "not found" per the transcoding convention).
//   Registered -> the INPUT name string itself (identity, no allocation --
//   the simplified Type model's Type VALUE representation). Resolution order:
//   the normalized type name is looked up in the typeinfo registry FIRST
//   (user-assembly types); a miss falls back to the builtin class registry
//   (il2cpp_class_by_name) so BCL types resolve exactly like net48's mscorlib
//   lookup (gettype_bcl: Type.GetType("System.Int32") is non-null). The hit
//   still returns the INPUT string (identity -- the normalized key is only
//   the lookup key, the Type VALUE is the caller's name).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_type(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("typeName");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf))
    {
        return nullptr;
    }
    char keyBuf[kNameBufferSize];
    if (NormalizeTypeName(nameBuf, keyBuf) <= 0)
    {
        return nullptr;
    }
    if (il2cpp_typeinfo_lookup(keyBuf) != nullptr)
    {
        return typeName; // user-assembly type: registry hit
    }
    // BCL fallback: the builtin descriptor registry (il2cpp_class_by_name)
    // resolves System.Int32 / System.String / ... even though those types
    // have no typeinfo table in this build (gettype_bcl contract).
    il2cpp_ensure_builtin_system_classes();
    if (il2cpp_class_by_name(keyBuf) != nullptr)
    {
        return typeName;
    }
    return nullptr;
}

} // namespace il2cpp
