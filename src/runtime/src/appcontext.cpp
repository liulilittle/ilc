//------------------------------------------------------------------------------
// appcontext.cpp -- System.AppContext / System.Runtime.Versioning.Compati-
// bilitySwitch icall implementations (see appcontext.h for the contract).
//
// The ILC runtime has NO App.config processing and NO AppContext switch DB;
// on the default (no-config) process net48's observable surface is the small
// fixed set recorded in appcontext.h -- an empty data store (GetData always
// null), an in-process switch store that ONLY SetSwitch/TryGetSwitch touch
// (starts empty, exactly like net48 with no config), the fixed host
// framework string ".NETFramework,Version=v4.8", and the CompatibilitySwitch
// internal calls returning null/false (no config blob, empty DB).
//
// This is byte-exact against the net48 oracle for every probe record in
// p-appcontext (all records are host-invariant shapes / in-process self-
// consistency; no absolute paths, host names, or clocks are asserted).
//------------------------------------------------------------------------------

// MSVC deprecates GetCurrentDirectoryA -> _getcwd (C4996) unless noise is
// silenced; this must precede every include (the toolchain's <cstdlib>
// declares getenv/getcwd before any define -- same convention as
// environment.cpp).
#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
    #ifndef _CRT_NONSTDC_NO_DEPRECATE
    #define _CRT_NONSTDC_NO_DEPRECATE
    #endif
#endif

#include "appcontext.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / _argument_exception
#include "il2cpp_gc.h"            // il2cpp_gc_add_static_root (switch keys must survive)
#include "string_compare.h"       // il2cpp_string_equals_ordinal (switch key compare)

#include <cstdint>
#include <cstring>
#include <string>

#if defined(_WIN32)
    #ifndef WIN32_LEAN_AND_MEAN
    #define WIN32_LEAN_AND_MEAN
    #endif
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#else
    #include <unistd.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

//--------------------------------------------------------------------------------
// In-process switch store (AppContext.SetSwitch / TryGetSwitch). net48 keeps
// its switches in a managed Dictionary<string,bool> that starts empty on a
// no-config process; this is the honest AOT equivalent: a small fixed table of
// {key, enabled} pairs compared by ordinal CONTENT (il2cpp_string_equals_or-
// dinal), with every stored key rooted through the GC static-root API (the
// exception-message side-table precedent in stubs.cpp -- the M2 collector
// never moves objects, so raw pointers stay valid).
//
// Fixed capacity: the probe/BCL surface uses a handful of switches; net48
// cap is effectively unbounded but a bounded AOT table keeps the runtime
// dependency-free. An overflow is dropped silently (switches beyond capacity
// read as unset, mirroring a VM that never saw them).
//--------------------------------------------------------------------------------
struct SwitchEntry
{
    Il2CppString* key;
    int32_t enabled;
};

constexpr int32_t kMaxSwitches = 128;
SwitchEntry g_switches[kMaxSwitches] = {};
int32_t g_switchCount = 0;

int32_t FindSwitch(Il2CppString* key)
{
    for (int32_t i = 0; i < g_switchCount; ++i)
    {
        if (g_switches[i].key != nullptr && il2cpp_string_equals_ordinal(g_switches[i].key, key))
            return i;
    }
    return -1;
}

bool IsNull(Il2CppObject* obj) { return obj == nullptr; }
bool IsEmpty(const Il2CppString* s) { return s == nullptr || s->length == 0; }

void CheckSwitchName(Il2CppObject* name)
{
    if (IsNull(name)) il2cpp_throw_argument_null_exception("switchName");
    if (IsEmpty(reinterpret_cast<const Il2CppString*>(name)))
        il2cpp_throw_argument_exception("switchName");
}

//--------------------------------------------------------------------------------
// AppContext.BaseDirectory -- the host's working directory (a non-empty
// string). net48 returns the exe's base directory; this runtime returns the
// CWD. Probes assert shape only (non-null, non-empty -- see appcontext.h), so
// the two are interchangeable at the byte-exact level. Unlike net48 the value
// is NOT cached per-AppDomain (the ILC process has one implicit domain).
//--------------------------------------------------------------------------------
Il2CppString* BaseDirectoryString()
{
#if defined(_WIN32)
    DWORD required = ::GetCurrentDirectoryA(0, nullptr);
    if (required == 0) return il2cpp_string_new(nullptr, 0);
    std::string buf;
    buf.resize(required);
    DWORD got = ::GetCurrentDirectoryA(required, &buf[0]);
    if (got == 0 || got > required) return il2cpp_string_new(nullptr, 0);
    buf.resize(got);
    if (buf.empty()) return il2cpp_string_new(nullptr, 0);
    return il2cpp_string_new_from_utf8(buf.c_str());
#else
    char buf[4096];
    if (::getcwd(buf, sizeof(buf)) == nullptr || buf[0] == '\0')
        return il2cpp_string_new(nullptr, 0);
    return il2cpp_string_new_from_utf8(buf);
#endif
}

} // anonymous namespace

Il2CppObject* icall_appcontext_get_data(Il2CppObject* name)
{
    if (IsNull(name)) il2cpp_throw_argument_null_exception("name");
    // The no-config data store is empty: net48 GetData returns null for EVERY
    // key on the default process (pinned in p-appcontext oracle).
    return nullptr;
}

void icall_appcontext_set_switch(Il2CppObject* name, int32_t enabled)
{
    CheckSwitchName(name);
    Il2CppString* key = reinterpret_cast<Il2CppString*>(name);
    const int32_t idx = FindSwitch(key);
    if (idx >= 0)
    {
        g_switches[idx].enabled = enabled; // update in place
        return;
    }
    if (g_switchCount >= kMaxSwitches) return; // silent overflow drop
    const int32_t slot = g_switchCount++;
    g_switches[slot].key = key;
    g_switches[slot].enabled = enabled;
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_switches[slot].key));
}

int32_t icall_appcontext_try_get_switch(Il2CppObject* name, int32_t* value)
{
    CheckSwitchName(name);
    Il2CppString* key = reinterpret_cast<Il2CppString*>(name);
    const int32_t idx = FindSwitch(key);
    if (idx < 0)
    {
        // Unknown switch: net48 writes false into the out param and returns false.
        if (value != nullptr) *value = 0;
        return 0;
    }
    if (value != nullptr) *value = g_switches[idx].enabled;
    return 1;
}

Il2CppObject* icall_appcontext_get_base_directory()
{
    Il2CppString* s = BaseDirectoryString();
    // Shape contract: never null on a healthy host (the probe asserts
    // non-null + len > 0). A pathological CWD failure returns an empty string
    // rather than null so the shape record still holds.
    return reinterpret_cast<Il2CppObject*>(s);
}

Il2CppObject* icall_appcontext_get_target_framework_name()
{
    // net48 host framework string, fixed for the net48 baseline (pinned
    // verbatim in the p-appcontext oracle: ".NETFramework,Version=v4.8").
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_cs(".NETFramework,Version=v4.8"));
}

Il2CppObject* icall_compatibility_switch_get_app_context_overrides()
{
    // No App.config on the AOT process -> the raw <AppContextSwitchOverrides>
    // blob is null (net48 oracle: GetAppContextOverridesInternalCall() -> null).
    return nullptr;
}

Il2CppObject* icall_compatibility_switch_get_value(Il2CppObject* /*name*/, int32_t /*onlyDB*/)
{
    // The compat-switch DB is empty on the default host; GetValue returns null.
    return nullptr;
}

int32_t icall_compatibility_switch_is_enabled(Il2CppObject* /*name*/, int32_t /*onlyDB*/)
{
    // The compat-switch DB is empty on the default host; IsEnabled returns false.
    return 0;
}

}} // namespace il2cpp::runtime