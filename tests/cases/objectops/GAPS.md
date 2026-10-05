# GAPS.md — objectops recorded findings (e2e batch C69, 2026-08-12)

Case status: net48 oracle 86-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-OBJ-1 — string.GetHashCode absolute values: net48 per-process randomized

- **Phenomenon**: net48 randomizes the string hash seed PER PROCESS (DoS
  hardening), so `"x".GetHashCode()` returns a DIFFERENT value in every net48
  process. The runtime deliberately pins a fixed seed (HashStringOrdinal,
  valuesemantics.cpp) so AOT output is deterministic.
- **Causal chain**: cross-process absolute string-hash values are not
  reproducible on the oracle side, so a pinned literal could never pass both
  compilers reliably.
- **Disposition**: recorded, NOT fixed (the runtime's determinism is a
  deliberate design choice). The case asserts string-hash CONSISTENCY only:
  `"hello world".GetHashCode() == "hello world".GetHashCode()` and
  equal-strings-equal-hashes (`h_str_self`, `h_str_equal`) -- both sides
  agree. Boxed-VALUE hashes (int/long/bool/char/double/byte/sbyte/short/
  ushort/uint) ARE net48-fixed and asserted as absolute constants.

## D-OBJ-2 — user-struct ValueType::GetHashCode: net48 whole-struct hash is randomized

- **Phenomenon**: net48's actual ValueType.GetHashCode for a USER struct is a
  process-randomized whole-struct hash (the DOCUMENTED "first non-static
  field" rule is NOT what net48 computes, per il2cpp_value_semantics.h:107-112).
  The runtime implements the documented rule deterministically (per-kind first
  field).
- **Phenomenon 2 (probe-verified 2026-08-12)**: a `Pt { int X }` local
  constrained-callvirt GetHashCode is NOT reachable from this case's shape --
  the transpiled output routed `pa.GetHashCode()` to
  `icall_object_get_hash_code((&loc8))` (an `Il2CppObject*` slot) with a
  `Pt*` value-type address, which does not compile (`cannot initialize a
  parameter of type 'Il2CppObject*' with an rvalue of type 'Pt*'`). The
  TryMapValueTypeGetHashCode first-field classification was never selected;
  the struct-hash probes were therefore REMOVED from the case (batch rule:
  no transpiler changes).
- **Disposition**: recorded, NOT fixed. The user-struct constrained-hash
  surface needs a receiver-type tracking fix on the fold side before a
  net48-oracle case can assert it.

## D-OBJ-3 — Object::ToString on untracked / non-boxed receivers: loud downstream

- The wired ToString receiver set (MethodMap.Object.cs) covers the boxed
  numerics/bool/char/string/object/decimal + StringBuilder/Version/Uri/
  StringWriter/IPAddress. Anything else falls through to the vtable path
  (loud failure downstream). The case stays within the wired set: boxed
  values print value text, `new object().ToString()` prints "System.Object".
