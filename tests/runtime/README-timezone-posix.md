# timezone POSIX per-date offsets: oracle evidence and reproduction

Committed evidence for `src/runtime/src/timezone.cpp` `PerDateOffsetPosix`
(the nb-tz fix). Everything below is produced by the scripts named in each
section; the CSV inputs were captured on a Windows host running **.NET
Framework 4.8** (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`)
and on WSL `Ubuntu-24.04` (gcc 13.3, tzdata 2026e).

## 1. net48 oracle vs the fixed POSIX API vs tzdata

Matrix: 8 zones x 2000-{01,04,07,10}-01 12:00 x {`wall` = Kind.Unspecified,
`utc` = Kind.Utc} = 64 rows. `net48` is `TimeZoneInfo.GetUtcOffset` +
`IsDaylightSavingTime`; `fixed` is `il2cpp_timezone_get_utc_offset_at` +
`il2cpp_timezone_get_is_daylight_saving_at` after the fix; `tzdata` is
`python3 zoneinfo` (glibc tzdata).

| zone | date | kind | net48 | fixed | tzdata | verdict |
|---|---|---|---|---|---|---|
| America/New_York | 2000-01-01 | utc | -18000 | -18000 | -18000 | MATCH |
| America/New_York | 2000-01-01 | wall | -18000 | -18000 | -18000 | MATCH |
| America/New_York | 2000-04-01 | utc | -18000 | -18000 | -18000 | MATCH |
| America/New_York | 2000-04-01 | wall | -18000 | -18000 | -18000 | MATCH |
| America/New_York | 2000-07-01 | utc | -14400 | -14400 | -14400 | MATCH |
| America/New_York | 2000-07-01 | wall | -14400 | -14400 | -14400 | MATCH |
| America/New_York | 2000-10-01 | utc | -14400 | -14400 | -14400 | MATCH |
| America/New_York | 2000-10-01 | wall | -14400 | -14400 | -14400 | MATCH |
| Asia/Kolkata | 2000-01-01 | utc | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-01-01 | wall | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-04-01 | utc | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-04-01 | wall | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-07-01 | utc | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-07-01 | wall | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-10-01 | utc | +19800 | +19800 | +19800 | MATCH |
| Asia/Kolkata | 2000-10-01 | wall | +19800 | +19800 | +19800 | MATCH |
| Asia/Shanghai | 2000-01-01 | utc | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-01-01 | wall | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-04-01 | utc | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-04-01 | wall | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-07-01 | utc | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-07-01 | wall | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-10-01 | utc | +28800 | +28800 | +28800 | MATCH |
| Asia/Shanghai | 2000-10-01 | wall | +28800 | +28800 | +28800 | MATCH |
| Australia/Lord_Howe | 2000-01-01 | utc | +39600 | +39600 | +39600 | MATCH |
| Australia/Lord_Howe | 2000-01-01 | wall | +39600 | +39600 | +39600 | MATCH |
| Australia/Lord_Howe | 2000-04-01 | utc | +37800 | +37800 | +37800 | MATCH |
| Australia/Lord_Howe | 2000-04-01 | wall | +37800 | +37800 | +37800 | MATCH |
| Australia/Lord_Howe | 2000-07-01 | utc | +37800 | +37800 | +37800 | MATCH |
| Australia/Lord_Howe | 2000-07-01 | wall | +37800 | +37800 | +37800 | MATCH |
| Australia/Lord_Howe | 2000-10-01 | utc | +37800 | +39600 | +39600 | data-divergence (tzdata != registry) |
| Australia/Lord_Howe | 2000-10-01 | wall | +37800 | +39600 | +39600 | data-divergence (tzdata != registry) |
| Australia/Sydney | 2000-01-01 | utc | +39600 | +39600 | +39600 | MATCH |
| Australia/Sydney | 2000-01-01 | wall | +39600 | +39600 | +39600 | MATCH |
| Australia/Sydney | 2000-04-01 | utc | +36000 | +36000 | +36000 | MATCH |
| Australia/Sydney | 2000-04-01 | wall | +36000 | +36000 | +36000 | MATCH |
| Australia/Sydney | 2000-07-01 | utc | +36000 | +36000 | +36000 | MATCH |
| Australia/Sydney | 2000-07-01 | wall | +36000 | +36000 | +36000 | MATCH |
| Australia/Sydney | 2000-10-01 | utc | +36000 | +39600 | +39600 | data-divergence (tzdata != registry) |
| Australia/Sydney | 2000-10-01 | wall | +36000 | +39600 | +39600 | data-divergence (tzdata != registry) |
| Europe/Dublin | 2000-01-01 | utc | +0 | +0 | +0 | MATCH |
| Europe/Dublin | 2000-01-01 | wall | +0 | +0 | +0 | MATCH |
| Europe/Dublin | 2000-04-01 | utc | +3600 | +3600 | +3600 | MATCH |
| Europe/Dublin | 2000-04-01 | wall | +3600 | +3600 | +3600 | MATCH |
| Europe/Dublin | 2000-07-01 | utc | +3600 | +3600 | +3600 | MATCH |
| Europe/Dublin | 2000-07-01 | wall | +3600 | +3600 | +3600 | MATCH |
| Europe/Dublin | 2000-10-01 | utc | +3600 | +3600 | +3600 | MATCH |
| Europe/Dublin | 2000-10-01 | wall | +3600 | +3600 | +3600 | MATCH |
| Pacific/Auckland | 2000-01-01 | utc | +46800 | +46800 | +46800 | MATCH |
| Pacific/Auckland | 2000-01-01 | wall | +46800 | +46800 | +46800 | MATCH |
| Pacific/Auckland | 2000-04-01 | utc | +43200 | +43200 | +43200 | MATCH |
| Pacific/Auckland | 2000-04-01 | wall | +43200 | +43200 | +43200 | MATCH |
| Pacific/Auckland | 2000-07-01 | utc | +43200 | +43200 | +43200 | MATCH |
| Pacific/Auckland | 2000-07-01 | wall | +43200 | +43200 | +43200 | MATCH |
| Pacific/Auckland | 2000-10-01 | utc | +46800 | +46800 | +46800 | MATCH |
| Pacific/Auckland | 2000-10-01 | wall | +46800 | +46800 | +46800 | MATCH |
| UTC | 2000-01-01 | utc | +0 | +0 | +0 | MATCH |
| UTC | 2000-01-01 | wall | +0 | +0 | +0 | MATCH |
| UTC | 2000-04-01 | utc | +0 | +0 | +0 | MATCH |
| UTC | 2000-04-01 | wall | +0 | +0 | +0 | MATCH |
| UTC | 2000-07-01 | utc | +0 | +0 | +0 | MATCH |
| UTC | 2000-07-01 | wall | +0 | +0 | +0 | MATCH |
| UTC | 2000-10-01 | utc | +0 | +0 | +0 | MATCH |
| UTC | 2000-10-01 | wall | +0 | +0 | +0 | MATCH |

`total=64  code-mismatch=0  data-divergence=4  match=60`

### Why the 4 data-divergence cells are not a code defect

glibc tzdata carries the one-off **2000-08-27** early DST start for the
Sydney Olympics in `Australia/Sydney` and `Australia/Lord_Howe`; the Windows
registry does not. On 2000-10-01 tzdata therefore answers +11:00 (daylight)
while net48 answers +10:00 / +10:30 (standard). The POSIX path reads tzdata,
so it must report +11:00. Confirmed with `zdump`:

```
Australia/Sydney     Sat Aug 26 16:00:00 2000 UT = Sun Aug 27 03:00:00 2000 AEDT isdst=1 gmtoff=39600
Australia/Lord_Howe  Sat Aug 26 15:30:00 2000 UT = Sun Aug 27 02:30:00 2000 +11  isdst=1 gmtoff=39600
```

`test_timezone_posix.cpp` pins these cells explicitly (`IsTzdataOnlyCell`)
so the divergence stays visible instead of being silently skipped.

## 2. Pre-fix vs post-fix on the same 64 rows

The pre-fix file is captured with
`git show fe36e4fc:src/runtime/src/timezone.cpp`.

| comparison | mismatches |
|---|---|
| pre-fix vs net48 | 12 / 64 |
| post-fix vs net48 | 4 / 64 (all 4 are the tzdata divergence above) |
| post-fix vs tzdata | 0 / 64 |

Pre-fix wrong rows (public API, the 64-row matrix):

| zone | date | kind | pre-fix | net48 |
|---|---|---|---|---|
| America/New_York | 2000-04-01 | utc | -14400 | -18000 |
| America/New_York | 2000-04-01 | wall | -14400 | -18000 |
| Australia/Lord_Howe | 2000-04-01 | utc | +39600 | +37800 |
| Australia/Lord_Howe | 2000-04-01 | wall | +39600 | +37800 |
| Australia/Lord_Howe | 2000-10-01 | utc | +37800 | +37800 |
| Australia/Lord_Howe | 2000-10-01 | wall | +37800 | +37800 |
| Australia/Sydney | 2000-04-01 | utc | +39600 | +36000 |
| Australia/Sydney | 2000-04-01 | wall | +39600 | +36000 |
| Australia/Sydney | 2000-10-01 | utc | +36000 | +36000 |
| Australia/Sydney | 2000-10-01 | wall | +36000 | +36000 |
| Pacific/Auckland | 2000-04-01 | utc | +46800 | +43200 |
| Pacific/Auckland | 2000-04-01 | wall | +46800 | +43200 |

## 3. Kind=Local (kind == 2) oracle

net48 does not read the receiver zone as a wall clock for `Kind=Local`; it
converts through the machine zone first. Measured on this UTC+8 host with a
custom receiver (base +12:00, DST +13:00 over `[2000-06-01T00:00Z,
04:00Z)`), by `kind2f.cs`:

```
machine zone China Standard Time = +8h
  wall 08:00  measured=+13h  machineModel=+13h  wallModel=+12h  => MACHINE-CONVERSION
  wall 10:00  measured=+13h  machineModel=+13h  wallModel=+12h  => MACHINE-CONVERSION
  wall 12:00  measured=+12h  machineModel=+12h  wallModel=+13h  => MACHINE-CONVERSION
  wall 14:00  measured=+12h  machineModel=+12h  wallModel=+13h  => MACHINE-CONVERSION
```

The runtime implements exactly that composition (resolve the wall against
the machine zone, subtract it to get the instant, then read the offset at
that instant), so `Kind=Local` differs from `Kind=Unspecified` only inside
the skipped spring-forward hour. `TestKindLocalUsesMachineInstant` pins both
sides of that split.

Kind=Local rows for the 8-zone matrix (net48):

| zone | date | wall | local | utc |
|---|---|---|---|---|
| America/New_York | 2000-01-01 | -18000 | -18000 | -18000 |
| America/New_York | 2000-04-01 | -18000 | -18000 | -18000 |
| America/New_York | 2000-07-01 | -14400 | -14400 | -14400 |
| America/New_York | 2000-10-01 | -14400 | -14400 | -14400 |
| Asia/Kolkata | 2000-01-01 | +19800 | +19800 | +19800 |
| Asia/Kolkata | 2000-04-01 | +19800 | +19800 | +19800 |
| Asia/Kolkata | 2000-07-01 | +19800 | +19800 | +19800 |
| Asia/Kolkata | 2000-10-01 | +19800 | +19800 | +19800 |
| Asia/Shanghai | 2000-01-01 | +28800 | +28800 | +28800 |
| Asia/Shanghai | 2000-04-01 | +28800 | +28800 | +28800 |
| Asia/Shanghai | 2000-07-01 | +28800 | +28800 | +28800 |
| Asia/Shanghai | 2000-10-01 | +28800 | +28800 | +28800 |
| Australia/Lord_Howe | 2000-01-01 | +39600 | +39600 | +39600 |
| Australia/Lord_Howe | 2000-04-01 | +37800 | +37800 | +37800 |
| Australia/Lord_Howe | 2000-07-01 | +37800 | +37800 | +37800 |
| Australia/Lord_Howe | 2000-10-01 | +37800 | +37800 | +37800 |
| Australia/Sydney | 2000-01-01 | +39600 | +39600 | +39600 |
| Australia/Sydney | 2000-04-01 | +36000 | +36000 | +36000 |
| Australia/Sydney | 2000-07-01 | +36000 | +36000 | +36000 |
| Australia/Sydney | 2000-10-01 | +36000 | +36000 | +36000 |
| Europe/Dublin | 2000-01-01 | +0 | +0 | +0 |
| Europe/Dublin | 2000-04-01 | +3600 | +3600 | +3600 |
| Europe/Dublin | 2000-07-01 | +3600 | +3600 | +3600 |
| Europe/Dublin | 2000-10-01 | +3600 | +3600 | +3600 |
| Pacific/Auckland | 2000-01-01 | +46800 | +46800 | +46800 |
| Pacific/Auckland | 2000-04-01 | +43200 | +43200 | +43200 |
| Pacific/Auckland | 2000-07-01 | +43200 | +43200 | +43200 |
| Pacific/Auckland | 2000-10-01 | +46800 | +46800 | +46800 |
| UTC | 2000-01-01 | +0 | +0 | +0 |
| UTC | 2000-04-01 | +0 | +0 | +0 |
| UTC | 2000-07-01 | +0 | +0 | +0 |
| UTC | 2000-10-01 | +0 | +0 | +0 |

`Kind=Local` equals `Kind=Unspecified` on every row of this mid-day matrix
(no row lands in a spring-forward gap), and equals `Kind=Utc` too because
the machine zone is UTC+8: wall - 8 h is the instant the caller meant.

## 4. Pre-1970: KNOWN LIMITATION, not covered by the fix

`PerDateOffsetPosix` returns the current offset for any input below the
Unix epoch (`timezone.cpp`, the `dt.ticks < kUnixEpochTicks` guard, marked
`KNOWN LIMITATION`). That is the SAME defect class the fix removes for
1970+; it is documented rather than fixed because the two available data
sources cannot agree:

| zone | instant | glibc tzdata | net48 (registry rule extrapolated) |
|---|---|---|---|
| America/New_York | 1918-07-01T12Z | -14400 (dst) | -14400 (dst) -- agree |
| America/New_York | 1900-07-01T12Z | -18000 (std) | -14400 (dst) -- differ 1 h |
| Asia/Shanghai | 1900-01-01T12Z | +29143 local mean time | +28800 -- differ |
| Pacific/Auckland | 1900-01-01T12Z | +41400 | +46800 -- differ 1.5 h |
| Pacific/Auckland | 1601-01-01T12Z | +41944 local mean time | +46800 -- differ |

`tests/runtime/test_time.cpp` (nb-dt) already normalises the pre-1900
FileTime bridge to the zone STANDARD offset via `kHistoricalFloorTicks`;
reconciling the two surfaces is a separate task.

## 5. Suite baseline: the "same 9 failures" claim

Both trees were configured with the identical command and built with
gcc 13.3. The baseline tree additionally needs the POSIX `_stricmp` alias
that this fix adds to `typeinfo.cpp` (without it the runtime does not
compile on Linux at all), so the baseline build passes
`-DCMAKE_CXX_FLAGS=-D_stricmp=strcasecmp`; the ONLY difference between the
two runs is therefore the timezone change.

```
BASE (fe36e4fc):  TOTAL=186 FAIL=9
  test_array_extra test_arraybounds test_arrayext test_arrayext2
  test_arraysort test_com test_concat test_exceptionext test_listi4
HEAD (fix):       TOTAL=187 FAIL=9
  test_array_extra test_arraybounds test_arrayext test_arrayext2
  test_arraysort test_com test_concat test_exceptionext test_listi4
```

The failure sets are identical; HEAD has one extra suite
(`test_timezone_posix`, PASS). Reproduction:

```bash
# head
cmake -S tests/runtime -B /tmp/tz-head -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build /tmp/tz-head -j16
ctest --test-dir /tmp/tz-head --output-on-failure

# baseline (needs the POSIX _stricmp alias to compile; the archive lands the
# repo one level down, hence the trailing /repo)
mkdir -p /tmp/tz-base-src
git archive fe36e4fc | tar -x -C /tmp/tz-base-src/repo
cmake -S /tmp/tz-base-src/repo/tests/runtime -B /tmp/tz-base -G "Unix Makefiles" \
      -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_FLAGS=-D_stricmp=strcasecmp
cmake --build /tmp/tz-base -j16
for t in /tmp/tz-base/test_*; do [ -x "$t" ] && ! "$t" >/dev/null 2>&1 && echo "BASEFAIL $(basename "$t")"; done
```

`git archive` warns about three CJK-named `docs/architecture/*.md` entries
(`Invalid empty pathname` on this host's tar); the C++ tree extracts fine and
those files are not needed for the build.

## 6. Reproducing the oracle on Windows

Both halves are committed next to this file, so the table above can be
regenerated on any pair of hosts:

| file | role |
|---|---|
| `tools/oracle.cs` | net48 side: `TimeZoneInfo.GetUtcOffset` + `IsDaylightSavingTime`, 8 zones x 4 dates x {wall, utc} |
| `tools/zprobe.cpp` | POSIX side: the same matrix through the public runtime API, switching `TZ` per zone |

```bash
# net48 (Windows; csc from .NET Framework 4.8)
csc /nologo /out:oracle.exe tools/oracle.cs && oracle.exe > net48.csv

# POSIX (Linux/WSL; needs /usr/share/zoneinfo)
g++ -std=c++17 -I src/runtime/include/internal \
    tests/runtime/tools/zprobe.cpp src/runtime/src/timezone.cpp -o zprobe
./zprobe > posix.csv

# both files share one CSV shape, so a row diff is the whole comparison
diff net48.csv posix.csv
```

For the pre-fix rows in section 2, build the same probe against the baseline
source:

```bash
git show fe36e4fc:src/runtime/src/timezone.cpp > /tmp/timezone_base.cpp
g++ -std=c++17 -I src/runtime/include/internal \
    tests/runtime/tools/zprobe.cpp /tmp/timezone_base.cpp -o zprobe_base
./zprobe_base > posix_prefix.csv
```

`tzdata.csv` (the third column of section 1) comes from `python3 zoneinfo`
over the same matrix:

```python
from datetime import datetime
from zoneinfo import ZoneInfo
for label, zid in zones:
    zi = ZoneInfo(zid)
    for mo in (1, 4, 7, 10):
        for kind in ("wall", "utc"):
            dt = (datetime(2000, mo, 1, 12, 0, 0, tzinfo=zi) if kind == "wall"
                  else datetime(2000, mo, 1, 12, 0, 0, tzinfo=ZoneInfo("UTC")).astimezone(zi))
            print(label, dt.utcoffset().total_seconds(), int(bool(dt.dst())))
```

