//------------------------------------------------------------------------------
// resourcemanager.cpp -- RM-CTOR (resmgmt/resops, b23 + R2): ResourceManager
// ctor base-name validation + receiver wiring.
//
// R2 (satellite culture chain): the receiver carries the BASE NAME as a
// managed string. The culture-aware GetString/GetObject/GetStream chain
// (resources.cpp) resolves "<baseName>.<culture>.resources" for every culture
// on the net48 fallback path at QUERY time -- the pre-R2 model that stored the
// resolved invariant byte[] blob in the receiver could not rebuild the chain
// (the blob carries no base name). R6 (batch C): the base name may be a
// non-ASCII managed string -- it is lifted to UTF-8 by
// il2cpp_resources_utf16_to_utf8 and the chain compares UTF-8 bytes.
//
// Cross-platform: managed strings through the existing surface, no platform
// code. A null / embedded-NUL / unpaired-surrogate / over-long base name
// returns null (the ctor receiver then has no usable base name and every
// lookup on it is a null result -- net48 defensive contract).
//------------------------------------------------------------------------------

#include "il2cpp_resources.h"

namespace il2cpp {

namespace {

// UTF-8-BYTE cap for the base name (room for "<culture>.resources" suffixes in
// the chain's 512-byte resName). The translated UTF-8 encoding, not the UTF-16
// unit length: non-ASCII widens the name past its unit count.
constexpr int32_t kMaxBaseNameLen = 240;

} // namespace

Il2CppObject* il2cpp_resource_manager_ctor_resolve(Il2CppObject* baseName, Il2CppObject* assembly)
{
    (void)assembly; // the Assembly argument is dropped (the registry IS the manifest)
    if (baseName == nullptr)
    {
        return nullptr;
    }
    auto s = reinterpret_cast<Il2CppString*>(baseName);
    if (s->length < 0)
    {
        return nullptr;
    }
    // R6 (batch C): the base name may be non-ASCII -- it is lifted to UTF-8 by
    // the shared encoder (surrogate-pair aware) and the chain resolves
    // "<baseName>[.<culture>].resources" over the UTF-8 registry names. The
    // cap counts UTF-8 BYTES (non-ASCII widens past the UTF-16 length); the
    // chain suffix still fits in the 512-byte resName. An embedded NUL /
    // unpaired surrogate / over-long encoding -> null (every lookup on the
    // receiver is then a null result -- net48 defensive contract).
    char buf[kMaxBaseNameLen];
    if (il2cpp_resources_utf16_to_utf8(s->chars, s->length, buf, kMaxBaseNameLen) < 0)
    {
        return nullptr;
    }
    // R2: pass the base name THROUGH as the receiver value (ResultIsReceiver
    // stores it into the receiver local). The culture chain resolves the
    // invariant "<baseName>.resources" and every "<baseName>.<culture>.resources"
    // at query time.
    return baseName;
}

//------------------------------------------------------------------------------
// new CultureInfo(string) handle-gap (R2): a CultureInfo IS its culture-name
// string -- the transpiled ctor stores the NAME into the receiver local
// (ResultIsReceiver), and the culture-aware GetString/GetObject/GetStream
// surface passes it as the cultureName slot. Same convention as
// il2cpp_culture_get_invariant (cultureinfo.cpp: the invariant culture is the
// empty name string). null -> null (defensive).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resource_manager_culture_from_name(Il2CppObject* name)
{
    return name;
}

} // namespace il2cpp
