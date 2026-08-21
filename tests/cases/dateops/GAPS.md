# GAPS.md -- dateops notes (2026-08-07)

55 probes PASS vs net48 (mingw). The wired DateTime/TimeSpan surface holds:
3/6-arg ctors, getters, DayOfWeek, TimeSpan-surface Ticks, AddDays cross-month
(leap + negative + fractional), Parse (invariant ISO/US subset) round-trip,
ToString "yyyy-MM-dd HH:mm:ss" + custom set, CompareTo + static Compare,
DateTime subtraction -> TimeSpan with the TimeSpan getter surface.

## DEFECT-DATE-1 -- DateTime.Ticks / ToUniversalTime / subtract-TimeSpan / relational ops are LOUD gaps

These members are NOT wired in MethodMap.DateTimeExtra.cs (each throws
NotSupportedException): DateTime.get_Ticks, get_Date, get_TimeOfDay,
get_Millisecond, get_DayOfYear, get_Kind, get_Today, ToUniversalTime,
ToLocalTime, AddSeconds/AddMilliseconds/AddMonths/AddYears/Add, op_Addition,
TryParse/TryParseExact, relational operators (<, >, ==), Equals/GetHashCode,
Subtract(TimeSpan).

The case therefore routes Ticks through the WIRED TimeSpan surface
(DateTime - DateTime -> TimeSpan.get_Ticks / TimeSpan ctor(ticks)) exactly as
datetime2 does, and uses CompareTo / static Compare for comparison. The
ToUniversalTime and DateTime.Ticks probes the audit requested are documented
here rather than called (calling them would abort the transpile).

NOT FIXED (defect recorded only).
