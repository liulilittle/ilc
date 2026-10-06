//------------------------------------------------------------------------------
// resourcemanager.cpp -- RM-CTOR (resmgmt/resops, b23 + R2): ResourceManager
// ctor base-name validation + receiver wiring.
//
// R2 (satellite culture chain): the receiver carries the BASE NAME as a
// managed string. The culture-aware GetString/GetObject/GetStream chain
// (resources.cpp) resolves "<baseName>.<culture>.resources" for every culture
// on the net48 fallback path at QUERY time -- the pre-R2 model that stored the
// resolved invariant byte[] blob in the receiver could not rebuild the chain
// (the blob carries no base name). The base name is ASCII by project
// convention (the registry keys are ASCII).
//
// Cross-platform: managed strings through the existing surface, no platform
// code. A null / non-ASCII / over-long base name returns null (the ctor
// receiver then has no usable base name and every lookup on it is a null
// result -- net48 defensive contract).
//------------------------------------------------------------------------------

#include "il2cpp_resources.h"

namespace il2cpp {

namespace {

// ASCII-only managed-string validation (the base name must be re-derivable as
// a C string by the chain walker; resource base names are ASCII by project
// convention).
constexpr int32_t kMaxBaseNameLen = 240; // room for "<culture>.resources" suffixes

} // namespace

Il2CppObject* il2cpp_resource_manager_ctor_resolve(Il2CppObject* baseName, Il2CppObject* assembly)
{
    (void)assembly; // the Assembly argument is dropped (the registry IS the manifest)
    if (baseName == nullptr)
    {
        return nullptr;
    }
    auto s = reinterpret_cast<Il2CppString*>(baseName);
    if (s->length < 0 || s->length >= kMaxBaseNameLen)
    {
        return nullptr;
    }
    for (int32_t i = 0; i < s->length; i++)
    {
        char16_t u = s->chars[i];
        if (u == 0 || u > 0x7F)
        {
            return nullptr;
        }
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
