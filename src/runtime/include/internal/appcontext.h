#pragma once
//------------------------------------------------------------------------------
// appcontext.h -- System.AppContext + System.Runtime.Versioning.Compatibility-
// Switch icall contract (see appcontext.cpp for the implementation).
//
// net48 semantics measured on the default no-config zh-CN console process
// (probe p-appcontext, oracle p-appcontext/expected.txt):
//
//   * AppContext.GetData(name) -- returns null for EVERY key (the AppContext
//     data store holds only config-driven entries; a no-config process has an
//     empty store, and "BaseDirectory"/"TargetFrameworkName" are PROPERTY
//     getters, not GetData keys -- net48 oracle: GetData("BaseDirectory") and
//     GetData("TargetFrameworkName") both null). A null NAME throws
//     System.ArgumentNullException("name") (net48 contract).
//   * AppContext.SetSwitch(name, enabled) / TryGetSwitch(name, out enabled) --
//     an in-process string->bool SWITCH store (empty until a SetSwitch call,
//     exactly like the net48 no-config process). TryGetSwitch returns false
//     + writes false for an UNKNOWN switch, true + the stored value for a
//     known one. Argument contract (net48): SetSwitch(null) -> ArgumentNull-
//     Exception, SetSwitch("") -> ArgumentException, TryGetSwitch(null) ->
//     ArgumentNullException, TryGetSwitch("") -> ArgumentException.
//   * AppContext.BaseDirectory -- a non-empty string (the host's working
//     directory; probes assert shape only, never the absolute value).
//   * AppContext.TargetFrameworkName -- ".NETFramework,Version=v4.8" (the
//     net48 CLR's host framework string, fixed for the net48 baseline).
//   * CompatibilitySwitch.GetAppContextOverridesInternalCall() -- returns
//     null on a no-config process (the raw <AppContextSwitchOverrides>
//     blob; without App.config there is nothing to read).
//   * CompatibilitySwitch.GetValueInternalCall(name, onlyDB) -- null;
//     IsEnabledInternalCall(name, onlyDB) -- false (the compat-switch DB is
//     empty on the default host).
//
// GC CONTRACT (the switch store): the store is a plain C++ table holding raw
// Il2CppString* keys; every stored key is rooted via il2cpp_gc_add_static_root
// at insert (the exception-message side-table precedent, stubs.cpp). Values
// are inline int32 (no boxing). Fixed capacity (switches are few).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"
#include "il2cpp_string.h"

namespace il2cpp { namespace runtime {

// AppContext.GetData(string) -> object | null (always null: the no-config
// data store is empty). Null name -> System.ArgumentNullException.
Il2CppObject* icall_appcontext_get_data(Il2CppObject* name);

// AppContext.SetSwitch(string, bool) -- store/update the in-process switch
// table. Null name -> ArgumentNullException; empty name -> ArgumentException.
void icall_appcontext_set_switch(Il2CppObject* name, int32_t enabled);

// AppContext.TryGetSwitch(string, out bool) -> bool found; on a hit writes
// the stored value (non-zero/zero) into *value, on a miss writes 0 and
// returns 0. Null name -> ArgumentNullException; empty -> ArgumentException.
int32_t icall_appcontext_try_get_switch(Il2CppObject* name, int32_t* value);

// AppContext.BaseDirectory -> a non-empty managed string (the host working
// directory). Never null on a healthy host.
Il2CppObject* icall_appcontext_get_base_directory();

// AppContext.TargetFrameworkName -> ".NETFramework,Version=v4.8".
Il2CppObject* icall_appcontext_get_target_framework_name();

// CompatibilitySwitch.GetAppContextOverridesInternalCall() -> null (no config).
Il2CppObject* icall_compatibility_switch_get_app_context_overrides();

// CompatibilitySwitch.GetValueInternalCall(name, onlyDB) -> null (no config).
Il2CppObject* icall_compatibility_switch_get_value(Il2CppObject* name, int32_t onlyDB);

// CompatibilitySwitch.IsEnabledInternalCall(name, onlyDB) -> false (no config).
int32_t icall_compatibility_switch_is_enabled(Il2CppObject* name, int32_t onlyDB);

}} // namespace il2cpp::runtime