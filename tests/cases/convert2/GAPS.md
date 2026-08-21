# GAPS.md -- convert2 recorded findings (e2e batch C83, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 65-line
byte-exact). Active probes drive the WIRED radix/bit-plane surface
(ToString(int, base) + ToInt32(string, base) over the full 32-bit pattern
lattice in bases 2/8/16, round-trip identity, cross-radix identity, base-10
signed rendering, tight-parse round trips). Gaps below are RECORDED, NOT
FIXED; the surfaces are genuinely unwired (the transpiler would fall through
to the generated-name path and abort the C++ build).

## D-C2-1 -- Convert.ChangeType has no runtime icall

- **Phenomenon**: `Convert.ChangeType(object, Type)` (any overload) is
  completely absent from MethodMap.Convert.cs (il2cpp_convert.h carries only
  the int32/int64/double/char/bool/string forms). A probe would resolve to
  the generated-name mangling and fail the C++ build, so the surface is NOT
  exercised. net48 semantics (IConvertible-based cross-type conversion) are
  unreachable e2e.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Convert.cs`
  (no ChangeType arm); `src/runtime/src/convert.cpp` (no ChangeType helper).

## D-C2-2 -- Convert.ToObject(Type, object) unwired

- **Phenomenon**: `Convert.ToObject(Type, object)` -- the boxed-enum
  construction path -- is not in MethodMap.Convert.cs and is LOUD-rejected
  on the Enum side too (MethodMap.Enum.cs "ToObject" throws
  NotSupportedException: the result is a boxed enum and the runtime has no
  enum box helper for the *ToObject* shape). Unreachable e2e; recorded.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Convert.cs`;
  `src/il2cppc/Emit/MethodMap.Enum.cs` ToObject arm.

## D-C2-3 -- Checked narrowing family still LOUD

- **Phenomenon**: `Convert.ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/
  ToUInt64/ToSingle/ToDecimal` are rejected loudly by
  MethodMap.ConvertExtra.cs (no runtime icall on the M5 surface; reusing a
  wrong-range helper would silently change overflow semantics). Not probed.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.ConvertExtra.cs`.

## Notes

- The `unchecked((int)0xF0F0F0F0)` spelling (convertbase precedent) is
  required: 0xF0F0F0F0 is a UINT literal and would bind to the unwired
  Convert.ToString(uint,int) overload otherwise.
- Radix validator parity: convert.cpp IsValidRadix == the net48 set
  {2,8,10,16} (convertfull K-section proof), so no bases 3..36 probes.
