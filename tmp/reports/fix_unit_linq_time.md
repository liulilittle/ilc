# fix_unit_linq_time — Test-Fix Report

Agent scope: `tests/unit/MethodMapTests.Linq.cs` + `tests/unit/MethodMapTests.TimeExt2.cs` ONLY.
Verification command: `dotnet test E:\il2cpp\tests\unit\unit.csproj -c Release --filter "FullyQualifiedName~MethodMapTestsLinq|FullyQualifiedName~TimeExt2"`
Final result: **34/34 passed, 0 failed** (was 30 pass / 4 fail).

## Failures and root causes

### 1-3. MethodMapTestsLinq: OrderBy_KeySelector / Where_Predicate / OrderBy_StringKey
All three failed identically with `System.BadImageFormatException : Read out of bounds`
thrown from `MethodRefResolver.RawSignature` (src/il2cppc/Emit/MethodRefResolver.cs:1209)
— i.e. BEFORE any icall mapping ran. The premise of all three tests still holds; the
mappings did NOT regress:

- `Where(pred)` -> `il2cpp::il2cpp_linq_where_i4` (MethodMap.Linq.cs:186-190) unchanged
- `OrderBy(keySelector)` int/int -> `il2cpp::il2cpp_linq_order_by_key_i4` (MethodMap.Linq.cs:263-283) unchanged
- non-int32 OrderBy key still throws LOUD NotSupportedException

Root cause was in the tests' synthetic signature blobs: they encoded a BARE `0x15`
(GENERICINST) return-type byte followed by full parameter blobs. The current
`RawSignature` fully consumes a GENERICINST return payload (marker + TypeHandle +
arg count + args — P5/e2e_ilgen/G2 ret-consumption rules, MethodRefResolver.cs:1199-1224),
so the ret-consumer desynchronized into the params and overread the blob.

Fix: re-encoded the three memberRefSignature blobs to the well-formed shape already
used by the passing Select/Take/Skip tests — a COMPLETE GENERICINST return type
(`0x15 0x12 0x09 0x01 0x13 0x00` = IEnumerable<TSource>) followed by the two params
(source GenericInst + Func GenericInst). Decoded ps stays `[0x15, 0x15]`, so the
asserted contract (CppName / ParamCount / casts / ReturnTypeName) is byte-for-byte
the same as before. Also removed the stale header comment claiming OrderBy is an
unwireable UB gap (it has been wired via D-LINQ-1).

### 4. MethodMapTestsTimeExt2: Resolve_DateTime_DaysInMonth_ThrowsLoud
Failed with "Assert.Throws() Failure: No exception was thrown" — DaysInMonth is now
WIRED in the primary DateTime domain per the net9/10 campaign
(MethodMap.DateTime.cs:57-58 -> `icall_datetime_days_in_month`, 2 params,
casts `["i","i"]`; runtime impl time.cpp DaysInMonth).

Fix: renamed the test to `Resolve_DateTime_DaysInMonth_MapsToDaysInMonthIcall` and
changed it to assert success + CppName (`il2cpp::runtime::icall_datetime_days_in_month`),
static, ReturnsValue, ResultKind null, ParamCount 2, casts `["i","i"]`. Updated the
file-header comment accordingly (get_Ticks batch-151b precedent + DaysInMonth net9/10).

## Files changed
- tests/unit/MethodMapTests.Linq.cs (3 signature blobs + header comment)
- tests/unit/MethodMapTests.TimeExt2.cs (1 test rewritten + header comment)

No source files touched. No git operations. Backups of both pre-edit files:
tmp/reports/backups/*.20260822_055509.bak

## Environment note
During verification the shared project transiently failed to compile due to a PARALLEL
agent's in-flight edits in src/il2cppc/Emit/CodeGenerator.cs (CS0103/CS0841 at lines
668/2187/2188). Not my ownership; I polled until that agent's edit sequence compiled
(~12 min), then ran the suite green. My changes were never the cause of any build error.
