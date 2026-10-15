# GAPS.md -- p-timezone-dst recorded findings (TimeZone DST audit, GN-20)

Case status: **PASS** (mingw, net48 oracle 24-row byte-exact; oracle sha
`4b78917b06dcfccea1774c321f44fb4efae1fbc5790172ce02aeb241bd6b0a81` == runtime
sha; 0 stdout diff on 609 bytes).

Scope: the net48 instance DateTime surface of the TimeZone domain --
`TimeZoneInfo.Local.GetUtcOffset(DateTime)` / `IsDaylightSavingTime` /
`IsInvalidTime`, the obsolete `TimeZone.GetUtcOffset(DateTime)` (Kind-aware),
`TimeZoneInfo.Utc.GetUtcOffset`, and the multi-region registry path across the
2026 US DST spring-forward boundary.

## Fixed here (byte-exact, red->green)

- **Per-date `GetUtcOffset(DateTime)` / `IsDaylightSavingTime(DateTime)` /
  `IsInvalidTime(DateTime)`** (timezone.cpp per-date family, wired through
  MethodMap.TimeZone.cs "tzsec_at" / "tzsec_legacy" / `_at` casts): the
  machine zone's adjustment rules are read from the SAME Windows registry the
  .NET Framework reads (`HKLM\...\Time Zones\<id>\TZI` + the **Dynamic DST
  subkey**: FirstEntry/LastEntry + per-year REG_TZI_FORMAT entries). net48's
  ranges are replicated ([1..first] <- first dynamic entry, per-year middle,
  [last,9999] <- last entry). This is byte-exact with net48 on ANY Windows
  zone (DST or not) -- on this UTC+8 host every query collapses to the
  constant +08:00 (the fast-close path in the probe).
- **Legacy Kind-aware zero**: `TimeZone.CurrentTimeZone.GetUtcOffset(dt)` with
  Kind=Utc in net48 returns TimeSpan.Zero (the TimeZoneInfoWrapper), while the
  old current-instant mapping returned +08:00. RED (legacy.utckind.min 0 vs
  480) -> GREEN.
- **Known wall-vs-UTC model** (net48-measured): Unspecified/Local input uses
  the candidate-UTC window [startUtc + dstDelta, endUtc); Kind=Utc uses
  [startUtc, endUtc); IsInvalidTime is the non-existent spring-forward hour
  [springWall, springWall + dstDelta); IsDaylightSavingTime == (offset ==
  daylight offset). Verified against 23 net48-measured Eastern Standard Time
  constants (2026 boundaries, 2005/1988 historical through the Dynamic-DST
  2006 rule) in `tests/runtime/test_timezone_perdate.cpp` Part B.
- **Registry-DST subkey bug**: the loader initially read FirstEntry/LastEntry
  from the zone key ROOT; they live in the `Dynamic DST` subkey. Fixed in
  timezone.cpp and the test mirror.

## Recorded, NOT fixed (documented residuals)

- **ToLocalTime / ToUniversalTime** stay on the current-instant converters
  (il2cpp_datetime_to_local / from_local): near-now exact (timezonecase pins),
  a DST-zone machine's per-date/historic conversions remain the documented
  simplification. net48 per-date converters on DST machines are out of the
  probe surface.
- **Instance GetUtcOffset on a FindSystemTimeZoneById result**: the
  untracked-receiver limitation (timezonecase GAPS) keeps it on the LOCAL
  mapping -- methodically the same documented divergence as Encoding. The
  probe covers the registry multi-region surface via ConvertTimeFromUtc
  (byte-exact, tzprobe 2020 + p-timezone-dst 2026).
- **Registry pre-2007 DST-zone dates** (timezone_registry.cpp) throw the LOUD
  NotSupportedException (kFirstModelledYear 2007); net48 applies the Dynamic
  DST 2006 rule (Apr 1st-Sunday / Oct 5th-Sunday) to pre-2007 years -- the
  runtime registry models the 2007+ US rules only (pre-existing boundary,
  tzprobe GAPS). The machine-local per-date path HAS the historical rule via
  Dynamic DST; the registry path (fixed zone set) does not.
- **POSIX per-date**: localtime_r/gmtime_r at the instant (system tzdata); the
  invalid-hour surface is 0 (unmodelled) and the wall-vs-UTC boundary nuance
  is approximated. Authoritative on win32 (the build platform) is the registry
  model.
- **StandardBias != 0 zones**: the base offset uses -(Bias + StandardBias)*60
  everywhere; net48's no-rule years use -Bias alone -- the common Windows
  layout has StandardBias == 0, so this is a cosmetic edge, unobserved on the
  probe set (all StandardBias==0).
- **IsInvalidTime ignores DateTimeKind** (two independent auditor findings):
  net48 returns false for IsInvalidTime(DateTime) when Kind is Utc or Local
  (even on a spring-forward gap on a DST machine), while the runtime
  is_invalid_time evaluates the civil wall clock of the candidate window
  without a Kind guard. On this UTC+8 no-DST host every query collapses to
  the constant-zone branch so the probe set cannot expose the divergence;
  the Kind guard is recorded here pending verification on a DST-participating
  machine.

## Evidence

- net48 oracle measurements: `.opencode/tmp/gn20-legacy/ptzd-measure` (r.cs /
  r2.cs scratch probes + rule dumps; Eastern 2026/2020 wall+Utc+isdst+invalid,
  historical 2005/1988).
- DST-zone model verification: `tests/runtime/test_timezone_perdate.cpp`
  Part B (Eastern registry TZI + Dynamic DST, 23 net48 constants, all pass on
  any Windows).
- `tests/runtime/test_timezone.cpp` / `test_timezone_registry.cpp` unchanged
  and green.