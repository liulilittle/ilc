# GAPS.md -- datetime2deep recorded findings (e2e batch C62, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 70-line
byte-exact, all True). Active probes cover the WIRED DateTime add/subtract/
compare/boundary surface.

## D-DT2D-1 -- DateTime VALUE cannot be a helper-method argument

- **Phenomenon**: passing a `DateTime` (an `Il2CppDateTime` 8-byte VALUE) as a
  plain method argument fails the C++ build: the generated helper signature is
  `int32_t Cases_..._ThrowsAddDays(int32_t arg0, double arg1)` and the call
  site passes `Il2CppDateTime` -- `no known conversion from
  'il2cpp::runtime::Il2CppDateTime' to 'int32_t'`. DateTime arguments reach
  ICALL parameters (deref:dt / "" uncast) but NOT regular method parameters.
- **Worked around**: the AddDays/AddHours/AddTicks overflow probes use INLINE
  try/catch blocks inside the method body (the same shape as linqgroup's
  `minempty`), producing the bool inside the handler. The int-only ctor
  validation probes keep helper methods (ThrowsCtor / ThrowsCtorH -- int args
  ride fine).

## D-DT2D-2 -- DateTime.MinValue / MaxValue static fields unverified (avoided)

- **Phenomenon**: the decimal domain proves BCL static-field storage works for
  decimal (decimalops2 cm_max uses `decimal.MaxValue`), but the DateTime
  statics were not re-verified here. The boundary dates are instead constructed
  with the wired ctors: `new DateTime(1,1,1)` (MinValue) and
  `new DateTime(9999,12,31,23,59,59)` (MaxValue -- the 7-arg ms ctor is a LOUD
  gap, so the .9999999 tail is unreachable; AddTicks stays in-bounds up to
  +9999999 ticks on that date, probed as bd_ticksok).
- **Recorded**: not a defect -- a verification-boundary choice. If the
  DateTime statics are confirmed wired in a later batch, `MinValue`/`MaxValue`
  could be probed directly.
