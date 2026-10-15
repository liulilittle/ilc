# GAPS.md -- p-appcontext recorded findings (GN-20 legacy AppContext surface)

Case status: **PASS** (mingw, net48 oracle 22-record byte-exact; oracle sha ==
runtime sha f6b2b29f75e6e888551f86a755d8884f9ae95af91462b8b5216de586c4fe42a9).

Scope: the default NO-CONFIG zh-CN console process (no App.config / runtimeconfig)
observable surface of `System.AppContext` (GetData / SetSwitch / TryGetSwitch /
BaseDirectory / TargetFrameworkName) + `System.Runtime.Versioning.Compatibility-
Switch` (the audited realGap at docs/audit/icall-inventory-net48.md row 780).
`expected.txt` is the exact net48 oracle; the transpiled runtime reproduces it
byte-for-byte (verified: 22 records, 0 diff).

## FIXED this batch (byte-exact, runtime icall domain appcontext.h)

`src/runtime/src/appcontext.cpp` + `appcontext.h` implement the no-config
defaults:
- `AppContext.GetData(name)` → **null for every key** (the data store is empty
  without config; net48 measured GetData("BaseDirectory") and
  GetData("TargetFrameworkName") both null). Null name → ArgumentNullException.
- `AppContext.SetSwitch(name, enabled)` / `TryGetSwitch(name, out)` → an
  in-process string→bool SWITCH store (empty until a SetSwitch, exactly the
  net48 no-config process; unknown switch → false + out=false, known switch →
  true + stored value). Argument contract (net48 measured): SetSwitch(null) →
  ArgumentNullException, SetSwitch("") → ArgumentException, TryGetSwitch(null)
  → ArgumentNullException.
- `AppContext.BaseDirectory` → a non-empty string (the host working directory;
  net48 returns the exe dir, which differs between oracle/runtime run dirs, so
  the probe asserts shape only).
- `AppContext.TargetFrameworkName` → the net48 host string
  ".NETFramework,Version=v4.8" (fixed for the net48 baseline, asserted verbatim).
- `CompatibilitySwitch.GetAppContextOverridesInternalCall` → null (no config
  blob); `GetValueInternalCall` → null; `IsEnabledInternalCall` → false (empty
  compat-switch DB).
`MethodMap.AppContext.cs` maps both owners (System.AppContext +
System.Runtime.Versioning.CompatibilitySwitch) to the runtime icalls;
`CodeGenerator.cs` adds `#include "appcontext.h"`.

Audit effect: audit-internalcalls mscorlib realGap **2 → 1** (the remaining gap
is `System.Security.Principal.Win32.OpenThreadToken`, a separate native-token
family outside this batch; the panel confirmed it stays).

## W7 -- not aligned (honest white list)

1. **Config-driven AppContext data/switches** (`<AppContextSwitchOverrides>` in
   App.config / `AppContext.SetData`): the AOT runtime processes NO App.config,
   so GetData is permanently null and the switch store is fed only by live
   SetSwitch calls. A host that reads a config blob into `CompatibilitySwitch.
   GetAppContextOverridesInternalCall` would see non-null values absent here.
   The default-process records (this probe) are byte-exact; config scenarios are
   recorded W7 (the ILC target has no config-processing contract -- same class
   as the string-collation/culture W7 boundaries).
2. **AppContext.SetData / other managed AppContext members not in the probe**:
   net48 also has internal `SetData`-family machinery and DefaultSwitch patterns
   wired through DefineSwitchDefault; those appear only under framework
   initialization, never on the default console process, and are not asserted.
   They fall to the generated-name path if a host program ever calls them
   (unknown on the byte-exact probe surface).

## Empirical net48 facts pinned (oracle/expected.txt + full-framework harness)

- GetData(null) throws ArgumentNullException; GetData(every key) = null.
- SetSwitch/TryGetSwitch are in-process self-consistent; TryGetSwitch(unset) →
  false/false; getter-after-set does NOT populate GetData (distinct stores).
- TargetFrameworkName = ".NETFramework,Version=v4.8"; BaseDirectory non-empty.
- CompatibilitySwitch.IsEnabled(unset) → false, GetValue(unset) → null
  (full-framework reflection harness; the type is hidden from the net48 SDK
  reference assemblies so it cannot be compiled in the probe csproj).