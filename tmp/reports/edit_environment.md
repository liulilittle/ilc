# System.Environment surface sweep — edit report (2026-08-22)

Agent scope: `src/il2cppc/Emit/MethodMap.Environment.cs` + `MethodMap.EnvironmentExtra.cs` ONLY.
No builds run, no git ops (per task constraints). Backups: `tmp/backups/env_sweep_20260822/`.

## 1. Ground truth established

| Fact | Evidence |
|---|---|
| Dispatch chain for owner `System.Environment` is exactly `TryMapEnvironment(...) ?? TryMapEnvironmentExtra(...)`; single dispatch site in the codebase | MethodRefResolver.cs:698-699; grep `"System.Environment"` → 1 hit |
| Base wins; Extra only sees names base returns null for | same |
| Test cases compile against **real net48 reference assemblies** (`Microsoft.NETFramework.ReferenceAssemblies.net48`) → MemberRef names are exact net48 mscorlib names | tests/cases/environment/environment.csproj:3,12-13; envdeep.csproj same |
| Existing icalls — `il2cpp_environment.h`: get/set_environment_variable (:61/:67), get/set_current_directory (:70/:74), tick_count (:78), tick_count64 (:82), fail_fast (:86), current_managed_thread_id (:90), get/set_exit_code (:95/:96), processor_count (:101), user_name (:105), machine_name (:110), os_version (:116), special_folder (:123), command_line (:133) | grep `icall_environment_` on src/runtime/include |
| Plus `icall_environment_get_newline` (il2cpp_runtime.h:150) and stubs.h:135-137,206-207 (`_native`, `_get_version`→**int32_t**, `_version_ex`→int32_t, `_resource_from_default`, `_compatibility_flag`) | grep + read stubs.h:120-214 |
| `I(cpp, pc, rv, rr, inst, casts, resultKind, …)`: rv=ReturnsValue, rr=ReturnsRef (Il2CppObject*), ResultKind null=int32 | MethodRefResolver.cs:1140-1155, 100-127 |

## 2. Findings → actions

### F1. `Environment.CommandLine` never resolved (reachable net48 member)
net48 property getter is `get_CommandLine`; the base file only matched `"GetCommandLine"` (a .NET Core-era alias that envdeep.cs:8-10 itself marks "forward-looking"). `get_CommandLine` fell through BOTH files → generated-name path → dangling C++ symbol.
**Action:** added `case "get_CommandLine":` alongside the existing alias → existing `icall_environment_get_command_line` (il2cpp_environment.h:133). WIRE-EXISTING.

### F2. Overload shapes silently dropped args (the documented KNOWN GAP)
Base matched by name only, so these resolved to the wrong arity with the extra argument dropped:
- `GetEnvironmentVariable(string, EnvironmentVariableTarget)` (count 2 → 1-param icall)
- `SetEnvironmentVariable(string, string, EnvironmentVariableTarget)` (count 3 → 2-param icall)
- `GetFolderPath(SpecialFolder, SpecialFolderOption)` (count 2 → 1-param icall)
- `FailFast(string, string)` net48 overload (count 2 → 1-param icall)

**Action:** arity guards in base (`count == N ? I(...) : null`, N = covered arity) + matching LOUD gate cases in Extra. No icalls exist for any of these overloads → NEEDS-NEW-ICALL if ever required. This closes the gap documented in the old Extra header ("cannot be intercepted here").

### F3. `get_Version` wired to a shape-mismatched stub
Managed shape (net48 AND net9/10): `Environment.Version` → `System.Version` OBJECT (reference slot). Only runtime symbol: `icall_environment_get_version()` returning **int32_t** (stubs.h:136); mapping had rr=false → emitter typed the result temp int32_t. Any use of the value as a Version object = silent shape corruption. The Extra gate already listed `get_Version` as unwired but the base entry intercepted first (dead gate).
**Action:** removed `get_Version`/`GetVersion` from base wiring → now actually reaches Extra's LOUD gate. NEEDS-NEW-ICALL: an icall returning an Il2CppObject* System.Version instance. `GetVersionEx` kept (unreachable mono-internal alias, symbol exists). Note: MethodMap.GC.cs:164 has its own `get_Version` entry for GC-domain owners — different dispatch branch, untouched (not owned).

### F4. Reachable net48 members falling through to generated-name path (dangling symbols)
`get_WorkingSet`, `get_Is64BitProcess`, `get_Is64BitOperatingSystem`, `get_UserDomainName` were in NEITHER file.
**Action:** added to Extra's LOUD gate. All NEEDS-NEW-ICALL (no runtime symbols exist).

### F5. Stale gate entries / stale comments
- `case "get_CurrentManagedThreadId"` in Extra was dead (base wires it unconditionally since P6) → removed, header updated.
- Extra header claimed "12/12 icalls wired" and described the KNOWN GAP as open → rewritten to reflect the closed gap and the new contract ("base wires exact arity; Extra rejects uncovered shapes").
- Base header gained the sweep summary.

## 3. Final net48 public-surface coverage matrix

| Member (IL name) | Status after sweep |
|---|---|
| get_NewLine | WIRED ✓ |
| get/set_CurrentDirectory | WIRED ✓ |
| get/set_ExitCode | WIRED ✓ |
| get_TickCount | WIRED ✓ |
| get_ProcessorCount | WIRED ✓ |
| get_UserName / get_MachineName | WIRED ✓ |
| get_OSVersion | WIRED — **documented shape limitation**: icall returns the ToString() STRING while managed type is OperatingSystem; e2e pins this byte-exact (envdeep D-ENV-1, envdeep.cs:94-101) so left as-is. True OperatingSystem-shape = NEEDS-NEW-ICALL if richer access (.Version/.Platform/.VersionString) is ever required |
| get_CommandLine | WIRED (F1) ✓ |
| GetFolderPath(SpecialFolder) | WIRED count==1 ✓ |
| GetEnvironmentVariable(string) | WIRED count==1 ✓ |
| SetEnvironmentVariable(string,string) | WIRED count==2 ✓ |
| FailFast(string) | WIRED count==1 ✓ |
| Exit(int) | LOUD (no icall) |
| ExpandEnvironmentVariables | LOUD |
| GetCommandLineArgs | LOUD |
| GetLogicalDrives | LOUD |
| GetEnvironmentVariables()/(target) | LOUD |
| GetEnvironmentVariable(string,target) | LOUD via arity guard (F2) |
| SetEnvironmentVariable(string,string,target) | LOUD via arity guard (F2) |
| GetFolderPath(folder,option) | LOUD via arity guard (F2) |
| FailFast(string,string) | LOUD via arity guard (F2) |
| get_HasShutdownStarted / get_StackTrace / get_SystemDirectory / get_UserInteractive | LOUD |
| get_UserDomainName | LOUD (F4) |
| get_Is64BitProcess / get_Is64BitOperatingSystem | LOUD (F4) |
| get_WorkingSet | LOUD (F4) |
| get_Version | LOUD (F3) — needs-new-icall |

Forward-looking aliases kept (unreachable from net48/net9/net10 public surface, all symbols exist): GetCurrentDirectory/SetCurrentDirectory/get_TickCount64(net8+)/get_CurrentManagedThreadId(net8+)/nativeGetEnvironmentVariable/GetVersionEx/GetResourceFromDefault/GetCompatibilityFlag.

## 4. Needs-new-icall list (runtime work, NOT done here)

1. `icall_environment_get_version` → must return an Il2CppObject* System.Version instance (currently int32_t stub in stubs.h:136).
2. `icall_environment_get_logical_drives` → string[] (GetLogicalDrives/GetCommandLineArgs class).
3. `icall_environment_get_command_line_args` → string[] (could share argv state with get_command_line).
4. `icall_environment_exit(int32)` → process termination honoring stored ExitCode.
5. `icall_environment_get_working_set` → int64.
6. `icall_environment_get_is64bit_process` / `_is64bit_operating_system` → bool.
7. `icall_environment_get_user_domain_name` → string.
8. `icall_environment_get_stack_trace` → string.
9. `icall_environment_get_has_shutdown_started` / `_system_directory` / `_user_interactive` → bool/string/bool.
10. `icall_environment_expand_environment_variables` / `get_environment_variables` → string / IDictionary.
11. EnvironmentVariableTarget overloads + GetFolderPath(SpecialFolderOption) + FailFast(string,string): separate icalls or argument extension of the existing ones.
12. Optional: true OperatingSystem-object `get_OSVersion` (would supersede the documented string-shape D-ENV-1 limitation).

## 5. Verification

- Post-edit full reads of both files confirm balanced structure and intended content (lines cited above).
- grep confirms single `System.Environment` dispatch site; no other handler can bypass the chain.
- grep confirms no test in tests/cases exercises any changed member (`Environment.Version|CommandLine|WorkingSet|Is64Bit|Exit(|GetCommandLineArgs|GetLogicalDrives|UserDomainName|…Option|FailFast` → 0 hits), so no e2e behavior regression is possible from this change set.
- Builds intentionally NOT run (task constraint). C# changes are switch-case/ternary/comment edits only; syntax visually verified against the surrounding style.
