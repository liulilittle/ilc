//------------------------------------------------------------------------------
// resourcemanager.cpp -- RM-CTOR (resmgmt/resops, b23): ResourceManager ctor
// blob resolution.
//
// The single-assembly AOT model pins the invariant culture, so
// ResourceManager..ctor(string baseName, ...) resolves the INVARIANT
// "<BaseName>.resources" blob from the registry. This file reuses the public
// Assembly.GetManifestResourceStream surface (il2cpp_assembly_get_manifest_
// resource_stream -- same registry, D1) by appending the ".resources" suffix
// to the base name; the Assembly ctor argument is dropped at the transpiler
// (the registry IS the manifest).
//
// Cross-platform: managed strings through il2cpp_string_new_from_utf8, no
// platform code. A null / non-ASCII / over-long base name returns null (the
// ctor receiver then has no blob and every GetString on it is a null result --
// net48 defensive contract).
//------------------------------------------------------------------------------

#include "il2cpp_resources.h"

#include <cstring>

namespace il2cpp {

namespace {

// ASCII-only managed-string -> C buffer (same contract as resources.cpp's
// NameFromManagedString: resource base names are ASCII by project convention).
constexpr int32_t kMaxBaseNameLen = 240; // room for ".resources" + NUL

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
    char nbuf[kMaxBaseNameLen + 16];
    for (int32_t i = 0; i < s->length; i++)
    {
        char16_t u = s->chars[i];
        if (u == 0 || u > 0x7F)
        {
            return nullptr;
        }
        nbuf[i] = static_cast<char>(u);
    }
    std::memcpy(nbuf + s->length, ".resources", 11); // 10 chars + NUL
    Il2CppObject* fullName = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(nbuf));
    // The .resources parser reads BYTES: resolve the raw blob (NOT the
    // Stream wrapper of GetManifestResourceStream -- il2cpp_resources_get_blob
    // is the byte[] half of that surface).
    return il2cpp_resources_get_blob(fullName);
}

} // namespace il2cpp
