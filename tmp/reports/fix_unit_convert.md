# fix_unit_convert — TEST-FIX report (Convert narrow-family premise updates)

Date: 2026-08-22 · Agent: TEST-FIX · Git: NONE

## 0. Scope

7 failing members pinned the superseded "no runtime icall on the Convert
narrow surface" premise; the ~50-overload Convert wiring
(MethodMap.Convert.cs / MethodMap.ConvertExtra.cs) now maps
ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle via the
`icall_conv_ovf_*` checked-narrow family + `conv_r4` float family.

Owned file (both ownership targets coincide): `tests/unit/MethodMapTests.Base64.cs`
— the `MethodMapBase64Tests` class lives there; it holds BOTH the ToByte fact
and the NarrowingFamily theory. No other test file contains the
narrow-family ThrowsLoud theory (grep over tests/unit verified).

Backup: `tmp/backups/fix_unit_convert/MethodMapTests.Base64.cs.20260822_055430.bak`.

## 1. Changes (single file)

1. Header comment: replaced the stale "CHECKED narrowings with no runtime icall
   are rejected LOUD" description with the current contract (int sources wire;
   float/double sources + ToDecimal stay LOUD).
2. `Resolve_Convert_ToByte_ThrowsLoud` → `Resolve_Convert_ToByte_IntSource_MapsToCheckedNarrowIcall`:
   asserts success; CppName `il2cpp::runtime::icall_conv_ovf_u1_i8`, cast
   `["l"]` (sign-extend int32→int64 checked range test), ResultKind null,
   static, ReturnsValue, ParamCount 1.
3. `Resolve_Convert_NarrowingFamily_ThrowsLoud` (7 members) split into:
   - `Resolve_Convert_NarrowingFamily_IntSource_Wires` theory — 6 wired members,
     each asserting success + exact CppName/cast/resultKind:
     | member   | icall                    | cast | resultKind |
     |----------|--------------------------|------|------------|
     | ToSByte  | icall_conv_ovf_i1_i8     | l    | null       |
     | ToInt16  | icall_conv_ovf_i2_i8     | l    | null       |
     | ToUInt16 | icall_conv_ovf_u2_i8     | l    | null       |
     | ToUInt32 | icall_conv_ovf_u4_i4     | i    | null       |
     | ToUInt64 | icall_conv_ovf_u8_i4     | i    | l          |
     | ToSingle | icall_conv_r4_i4         | i    | d          |
   - `Resolve_Convert_NarrowingFamily_FloatDoubleSource_ThrowsLoud` theory —
     ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64 × {float 0x0C, double
     0x0D} sources stay LOUD (net48 rounds half-to-even before the range
     check; existing ovf_*_d helpers truncate — silently different).
   - `Resolve_Convert_ToDecimal_IntSource_ThrowsLoud` fact — still LOUD
     ("no decimal-target runtime icalls"), message asserted.

Every expected name/cast/resultKind was verified against the CURRENT wiring
source before writing assertions (not from the edit_convert.md report alone):
[READ:src/il2cppc/Emit/MethodMap.ConvertExtra.cs:74-173]; exception wrapping
`{owner}::{name}: {msg}` confirmed at
[READ:src/il2cppc/Emit/MethodRefResolver.cs:605-615]; dispatch order
Convert → BitConverter(Convert-guarded) → Base64 → ConvertExtra at
[READ:src/il2cppc/Emit/MethodRefResolver.cs:636-640] +
[READ:src/il2cppc/Emit/MethodMap.BitConverter.cs:58];
MethodMap.Convert.cs owns none of the seven names
[READ:src/il2cppc/Emit/MethodMap.Convert.cs:48-209].

## 2. Verification

- `dotnet test E:\il2cpp\tests\unit\unit.csproj -c Release --filter
  "FullyQualifiedName~Base64|FullyQualifiedName~Convert"` → **56/56 passed,
  0 failed** (Release, net8.0).
- Class-scoped re-run (`--filter FullyQualifiedName~MethodMapBase64Tests`,
  --no-build) → **19/19 passed**, matching the expected case count exactly:
  5 facts + wired theory ×6 + float/double-loud theory ×6 + … = 19.

## 3. Concurrent-breakage episode (observed, NOT touched)

First two builds failed with CS0103 in `CodeGenerator.cs` (747/2187:
`ManagedReturnStructName` / `managedRetStruct` / `returnType`) — a DIFFERENT
agent's mid-flight F2 decimal-domain refactor (file mtime 05:55:39, one minute
after my backup). Per file ownership I did not touch it; polled the build until
the owner landed their fix (mtime 06:09:12) → build green → full verification
above executed after that point. No test failures were caused by or hidden by
this wait.

## 4. Honest boundaries

- Assertions pin resolver OUTPUT (CppName/cast/resultKind), not runtime
  numerics — net48 semantic equivalence proofs live in the wiring sources and
  edit_convert.md §2, not here.
- The float/double LOUD theory asserts the exception type + self-identifying
  member name only; the "rounds vs truncates" rationale stays documented in
  comments/wiring, un-pinned by unit tests (needs-new rounding icalls first).
