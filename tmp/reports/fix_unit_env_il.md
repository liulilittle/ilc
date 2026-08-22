# fix_unit_env_il — TEST-FIX agent report

Scope: `tests/unit/MethodMapTests.EnvRandom.cs` + `tests/unit/MethodMapTests.Interlocked.cs` ONLY.
Task: flip 4 failing tests to the NEW post-wiring behavior.

## Changes

### MethodMapTests.EnvRandom.cs
1. `Resolve_Environment_GetCurrentManagedThreadId_ThrowsLoud`
   → renamed `Resolve_Environment_GetCurrentManagedThreadId_MapsToThreadIdIcall`.
   Now asserts SUCCESS against the P6 wiring (`MethodMap.Environment.cs:67`):
   - CppName == `il2cpp::runtime::icall_environment_get_current_managed_thread_id`
   - ReturnsValue true / ReturnsRef false / ParamCount 0 (int32 result → ResultKind null).

2. `Resolve_Environment_GetEnvironmentVariable_WithTarget_CountBlindToBaseIcall`
   → renamed `Resolve_Environment_GetEnvironmentVariable_WithTarget_ThrowsLoud`.
   New behavior verified in source before writing the assertion:
   - `MethodMap.Environment.cs:27` arity guard: `count == 1 ? I(...) : null` —
     the 2-arg shape (string + EnvironmentVariableTarget enum 0x08) returns null.
   - Falls to `TryMapEnvironmentExtra` (`MethodMap.EnvironmentExtra.cs:61`),
     which throws `NotSupportedException("System.Environment.GetEnvironmentVariable not wired: ...")`.
   - `BclResolve` wraps it with the `{owner}::{name}: ` prefix
     (`MethodRefResolver.cs:613`) → asserts Contains `System.Environment::GetEnvironmentVariable`.

### MethodMapTests.Interlocked.cs
3. `Resolve_Exchange_RefDouble_ThrowsLoud`
   → renamed `Resolve_Exchange_RefDouble_MapsToDoubleIcall`.
   Asserts the P6 float-in-double wiring (`MethodMap.Interlocked.cs:72-73`):
   - CppName == `il2cpp::runtime::il2cpp_interlocked_exchange_double`
   - ResultKind "d", ParamTypeNames ["", "d"], ParamCount 2,
     ReturnsValue true / ReturnsRef false / IsInstance false.
   - Signature blob corrected to the real net48 shape `00 02 0D 10 0D 0D`
     (old test had a wrong return byte 0x08; irrelevant while it only asserted
     a throw, required now that it pins success).

4. `Resolve_CompareExchange_RefDouble_ThrowsLoud`
   → renamed `Resolve_CompareExchange_RefDouble_MapsToDoubleIcall`.
   Same model per `MethodMap.Interlocked.cs:103-104`:
   - CppName == `il2cpp::runtime::il2cpp_interlocked_compare_exchange_double`
   - ResultKind "d", ParamTypeNames ["", "d", "d"], ParamCount 3.
   Stale "floating-point atomics have no runtime icall" block comment above the
   pair updated to describe the float-in-double wiring.

## Verification

Command: `dotnet test E:\il2cpp\tests\unit\unit.csproj -c Release --filter "FullyQualifiedName~EnvRandom|FullyQualifiedName~Interlocked"`

RESULT: **PASS — 失败: 0，通过: 32，已跳过: 0，总计: 32** (63 ms).

Targeted confirmation of exactly the 4 flipped tests
(`--filter "FullyQualifiedName~GetCurrentManagedThreadId|FullyQualifiedName~GetEnvironmentVariable_WithTarget|FullyQualifiedName~RefDouble"`):
**PASS — 失败: 0，通过: 4，总计: 4**.

### Transient external blocker (resolved, documented for the record)
The first test attempt failed to BUILD with CS0103 errors in
`src/il2cppc/Emit/CodeGenerator.cs` (lines 747/2187: `managedRetStruct`,
`returnType`, `ManagedReturnStructName`) — a CONCURRENT wiring agent's mid-edit
state in a file outside this agent's ownership (locals declared in one method,
referenced in another). Evidence it was transient: error count changed between
consecutive builds (3 → 2 → fluctuating while their edits landed); after a
bounded poll loop (6 builds / ~7 min) the build went GREEN with no action from
this agent. This agent made zero modifications outside its two owned files.
