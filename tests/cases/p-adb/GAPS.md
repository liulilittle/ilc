# GAPS.md -- p-adb recorded findings (adm1 batch: AppDomain minimal object closure)

Case status: **PASS** (mingw; net48 oracle == transpiled runtime byte-for-byte,
oracle sha b6d3edd7 == runtime sha b6d3edd7, 38 records, diff=0).

Scope: the net48 observable surface of the default AppDomain (single-domain
AOT model). `expected.txt` is the exact net48 oracle of the probe (every
record host-INVARIANT: shape / constant / in-process self-consistency -- no
absolute paths or host names, so the two run environments stay byte-identical).
The measured net48 values are pinned in `.opencode/tmp/appdomain/p-adb_adb.txt`
(56-line oracle); the probe reproduces the invariant subset.

## Implemented (byte-exact)

- Object identity: `get_CurrentDomain` singleton (GetType / null / isinst /
  refeq legs), the zero-state `System_AppDomain_class` carrier (appdomain.h).
- `get_Id` = 1 (stubs.cpp `icall_appdomain_get_id` 0 -> 1 fix).
- `get_FriendlyName` / `get_BaseDirectory` (exe dir + trailing separator; the
  AppContext.CWD semantic is NOT reused), `get_RelativeSearchPath` = null,
  `get_DomainManager` = null.
- `SetupInformation` -> `System_AppDomainSetup_class` carrier:
  `ApplicationBase` == BaseDirectory, `ConfigurationFile` == exeDir + exeName
  + ".Config".
- `GetData`/`SetData`: process-level GC-rooted key->object table (single-domain
  model => the net48 per-domain isolation is unobservable, a Do/process-local
  table is byte-equivalent); `GetData("APPBASE")`/`("APP_CONFIG_FILE")`
  derive from the exe path; null key -> ArgumentNullException.
- `CreateInstance` -> `System.Runtime.Remoting.ObjectHandle_class` carrier
  (1 ref field) + `Unwrap`; `CreateInstanceAndUnwrap` -> the instance directly
  (il2cpp_class_by_name of REGISTERED types + il2cpp_object_new +
  StringBuilder default ctor); bad (non-mscorlib) assembly -> FileNotFoundException;
  null args -> ArgumentNullException.
- Constants: `IsDefaultAppDomain()` / `IsFullyTrusted` / `IsHomogenous` = true.
- `UnhandledException` add/remove: process-level delegate chain
  (icall_delegate_combine/remove).
- `StringBuilder` descriptor promoted to GLOBAL scope
  (`System_Text_StringBuilder_class`) so generated isinst/castclass on a
  StringBuilder receiver compiles (the p-refl/stringbuilddeep probes never
  cast an object to StringBuilder before; the p-adb `as` leg does).

## LOUD (mapped to RUNTIME NotSupportedException, appdomain.cpp)

`ExecuteAssembly(string)` / `PermissionSet` / `Evidence` / `ApplicationTrust` /
`GetAssemblies` / `ToString` have no runtime implementation (no assembly
loader, no CAS object model, no culture-localized name). They are mapped to
icalls that THROW NotSupportedException at runtime so the SINGLE-SOURCE probe
compiles+links on the runtime (before adm1 they aborted the transpile). The
probe asserts the loud mapping explicitly:

- `ExecuteAssembly(missing path)` -- byte-exact "throws ANY explicit exception"
  record: net48 throws FileNotFoundException for a missing file, the runtime
  throws NotSupportedException for the SAME call; `catch (Exception)` -> True
  on both sides.
- The other five arms return net48 VALUES this runtime has no object model
  for. Those value bytes cannot be reproduced from a single source, so the
  probe does not call them in the byte-exact path; their NotSupported mapping
  is asserted per-arm in the unit test (MethodMapTests.Legacy.cs --
  `Resolve_AppDomain_*`) instead.

## Recorded, NOT fixed (W7)

- `AppDomain.Contains` per-domain isolation is unobservable (no second domain
  can exist: `CreateDomain`/`Unload` stay LOUD).
- `CreateInstanceAndUnwrap("mscorlib", <unregisteredType>)` -> the runtime
  throws NotSupportedException (LOUD) where net48 would throw TypeLoadException;
  a runtime type loader is W7.
- `CreateInstance` runs only the StringBuilder default ctor; arbitrary
  registered types' default ctors are not invoked (zero-init only) -- W7
  boundary, only the probed StringBuilder surface is reached.
- `AppDomain.ToString()` (W7 culture) / `GetAssemblies` counts / the CAS
  object values are LOUD (see above).
- `ShadowCopyFiles` / `DynamicDirectory` are not wired (not in the adm1 scope
  list); the probe omits them.