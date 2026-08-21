# GAPS.md -- mathext2 recorded findings (e2e batch C60, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 71-line
byte-exact, all True). Active probes cover the WIRED Abs / IEEERemainder /
DivRem surface and pin the BigMul value contract inline.

## D-ME2-1 -- Math.BigMul(int,int) is NOT wired (LOUD transpile abort)

- **Phenomenon**: `MethodMap.MathExtra.cs` (TryMapMathExtra case "BigMul",
  lines 105-107) throws `NotSupportedException("Math.BigMul not wired: net48
  member with no runtime icall in il2cpp_math.h/il2cpp_math_ext.h (long
  BigMul(int,int) would need an (int64_t)a*b runtime helper)")`. A call to
  `Math.BigMul(a, b)` therefore aborts the WHOLE transpile (run-tests.ps1
  throws at the transpile stage), so no BigMul call can exist in a PASSing
  case.
- **Impact**: the batch title names BigMul; the case instead pins the VALUE
  CONTRACT it must satisfy -- `Math.BigMul(a,b) == (long)a * b` (exact int64
  product, no overflow for int32 operands) -- via the equivalent inline long
  multiplication (both the net48 oracle and the transpiled runtime compute it
  identically). Contract pins include MaxValue*MaxValue, MinValue*MinValue,
  MaxValue*MinValue, mixed-sign and 0 operands.
- **Fix path (future)**: add `int64_t icall_math_bigmul(int32_t a, int32_t b)`
  returning `(int64_t)a * b` to il2cpp_math.h/math.cpp + a MethodMap.Math.cs
  `case "BigMul"` with ps == [0x08, 0x08] and result kind "l".

## D-ME2-2 -- double.IsNaN / IsPositiveInfinity / IsNegativeInfinity NOT wired

- **Phenomenon**: `double.IsNaN(x)` / `double.IsPositiveInfinity(x)` /
  `double.IsNegativeInfinity(x)` resolve to the undeclared-identifier C++
  error `use of undeclared identifier 'System_Double_IsNaN_...'` (no
  MethodMap.Double domain exists). Empirically hit on the first draft of this
  case.
- **Impact**: NaN / Infinity probes use the mathdeep pattern instead -- a
  static `IsNaN(double v) { return v != v; }` helper (NaN != NaN is TRUE) and
  direct `== double.PositiveInfinity` comparisons.
- **Fix path (future)**: add a MethodMap.Double domain mapping IsNaN /
  IsPositiveInfinity / IsNegativeInfinity / IsFinite to il2cpp_double_* bit
  helpers.

## D-ME2-3 -- (int32) hex literal pitfall is a SOURCE issue, not a runtime gap

- **Phenomenon**: during convertbase development, `Convert.ToString(0xAAAA,
  2)` bound to the UNWIRED `Convert.ToString(uint,int)` overload because
  0xAAAAAAAA is a uint32 literal (> int.MaxValue). This is a net48 compile-time
  overload-resolution fact, not a runtime divergence; the fix was an explicit
  `unchecked((int)0xAAAAAAAA)` cast in the case source. Recorded here so
  future cases never write a bare >0x7FFFFFFF hex literal against the wired
  int32 Convert surface. (Full detail in convertbase/GAPS.md D-CB-2.)
