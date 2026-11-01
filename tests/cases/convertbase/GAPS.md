# GAPS.md -- convertbase recorded findings (e2e batch C60, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 129-line
byte-exact, all True). Active probes exercise the WIRED base-conversion
surface (ToString(int,base) + ToInt32(string,base), bases 2/8/10/16) to
exhaustion plus the shared-rejection custom-radix boundary.

## D-CB-1 -- radix validator: CLOSED (the {2,8,10,16} rule is enforced)

- **Status**: re-measured for impl-input-contracts. The wired icalls
  (icall_convert_to_int32_string_base / icall_convert_to_string_i4_base)
  enforce EXACTLY {2,8,10,16} and throw ArgumentException otherwise, which is
  the net48 contract -- the old note (claiming a 2..36 superset) described a
  stale build. `tests/cases/convertbaseguard1` pins the whole b = 0..37 sweep
  across every To*(string, base) width plus Convert.ToString(value, base).
- **Note**: the TryParse-SHAPED icall `icall_try_parse_int32_base` (unused by
  the transpiler) used to return 0 for an invalid base, contradicting net48.
  It now throws ArgumentException as well, so the module carries ONE rule.

## D-CB-2 -- (int32) hex literals > 0x7FFFFFFF bind to the UNWIRED uint overload

- **Phenomenon**: C# hex literals are typed by value: `0xAAAAAAAA` exceeds
  int.MaxValue so it is a UINT32 literal, and `Convert.ToString(0xAAAA..., 2)`
  overload-resolves to `Convert.ToString(uint,int)` -- a member with NO
  MethodMap wiring (MethodMap.Convert.cs maps only the int32 base overload).
  Empirically the first draft emitted
  `icall_string_concat_2(2863311530LL, t0)` (the raw uint constant leaking
  into the concat slot) and the mingw build failed with "cannot initialize a
  parameter of type 'Il2CppObject*' with an rvalue of type 'long long'".
- **Impact**: fixed in the case SOURCE with an explicit
  `unchecked((int)0xAAAAAAAA)` cast (the runtime, and the net48 oracle,
  then see an int32 -1431655766 and print its uint32 two's-complement
  binary). Not a runtime defect; recorded so future Convert/bit cases never
  write bare >0x7FFFFFFF hex literals against the int32 surface.
- **Fix path (future)**: wire the Convert.ToString(uint,int) /
  ToInt32/ToUInt32 etc. uint32 family if the batch scope ever reaches it.

## D-CB-3 -- Convert.ToInt64(string, base) and ToString(long, base) NOT wired

- **Phenomenon**: MethodMap.Convert.cs wires ToInt64 ONLY for the 1-param
  string form (`icall_convert_to_int64_string`) and ToString(int,base) ONLY
  for int32; `Convert.ToInt64(string, int)` and `Convert.ToString(long, int)`
  have no mapping (the ToString i8 case requires ps.Length == 1). A base-64
  or base-long conversion would abort the transpile.
- **Impact**: all base-conversion probes here are int32; the int64 base
  family is unexercisable (recorded, not fixed).
- **Fix path (future)**: extend the base icalls to int64 (ParseNumbers
  StringToInt64/IntToString64) + MethodMap cases for (0x0A, 0x08) signatures.

