# GAPS.md -- int64deep recorded findings (e2e batch C76, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 127-line
byte-exact). Active probes cover the FULL wired int64 surface: unchecked
wrap arithmetic at the 64-bit boundaries, div/rem truncation semantics with
safe divisors, 63-bit shift masking, 64-bit bit-ops, the conv-family
conversions (checked + unchecked), checked int64/uint64 add/sub/mul
overflow, Math long surface, int64 ToString/format/parse round trips,
int64 arrays (Sort/IndexOf/BinarySearch), and BitConverter round trips.
Gaps below are RECORDED, NOT FIXED (this batch creates test cases only).

## D-I64D-1 -- int64 unchecked multiply overflow is C++ signed-overflow UB

- **Phenomenon**: `long lb = long.MinValue; lb * 2 == 0` is FALSE on the
  transpiled runtime (net48 True -- unchecked wrap gives 0). The emitter
  lowers int64 `mul` to a raw C++ expression `(loc1 * 2LL)` (CodeGenerator.cs
  OpSymbol path); `INT64_MIN * 2` is signed-overflow UB in C++, and clang
  (mingw, -O2) folds the result differently than the two's-complement wrap.
- **Confirmed**: probe removed (o4_min_mul2) -- the remaining max-add/sub
  wraps (o1/o2/o5) pass because they are compiler-agnostic there; this is a
  compiler-optimization-dependent result, not a stable semantic.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (the plain
  `(a * b)` emission for int64 operands -- a 64-bit checked/wrap helper
  would be required for deterministic wrap semantics).

## D-I64D-2 -- conv.u4 from an int64 source is an identity (no truncation)

- **Phenomenon**: `(uint)long.MaxValue` on the transpiled runtime keeps the
  FULL 64-bit pattern (4294967295u comparison fails; net48 truncates to the
  low 32 bits). StackFold.cs folds Conv_u4 as an identity (the
  "8/16-bit widths live in int32 slots" arm), so an int64 -> uint32
  narrowing never truncates.
- **Confirmed**: probe removed (c9_long_to_u4). conv.u4 from an int32
  source is fine (identity is correct there); only the int64-source arm is
  wrong.
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs` (Conv_u4 handling --
  needs an int64-source truncation arm, e.g. icall_conv_u4_i8).
