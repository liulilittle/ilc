# impl-procname-determinism — NOTES

Seat: impl-procname-determinism. Baseline: main `cc451d3f`.
Worktree: `F:\il2cpp\.worktrees\impl-procname-determinism`.
Scope: `tests/cases/probe-procname/*`, this NOTES.md, new files under `.opencode/tmp/`.

## Stage 0 — setup

- Worktree created from `cc451d3f` (detached HEAD).
- The case has NO `expected.txt` (only `probe-procname.cs` + `probe-procname.csproj`), so no
  expected.txt regeneration is needed; the anchor sha is the oracle stdout sha reported by
  `scripts/difftest.ps1`.
- Offending record: `tests/cases/probe-procname/probe-procname.cs` `[h]` (Start ping -n 30,
  `Kill()`, `HasExited` poll, first-ever `ProcessName` read).
- Same-class audit targets: `[e]` (live read + Kill + HasExited), `[g]` (WaitForExit, first
  read), `[i]` (HasExited poll, first read).

## Stage 1 — net48 scratch measurement

Tool: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /platform:x64 /optimize+`
Sources/binaries: `.opencode/tmp/procname-probe/` (`procname-probe.cs` / `.exe`).
Method: every run is a FRESH process (`& procname-probe.exe <mode>`), 20 runs per mode per
batch. `orig_h` is the current `[h]` body verbatim.

Modes (net48 Process):
- orig_h = Start ping -n 30, Kill, HasExited poll (600x10ms), first-ever ProcessName read
- a      = Kill, immediate read (no poll)
- b      = Kill, WaitForExit, read
- c      = Kill, HasExited poll, Close, read
- d      = Kill, WaitForExit, Close, read
- e      = live read, Kill, WaitForExit, read (prints alive|after)
- e2     = EXACT current [e]: live read, Kill, HasExited poll, read, read (alive|r1|r2)
- f      = Kill, Sleep(100), read
- g      = EXACT current [g]: cmd /c ping -n 2, WaitForExit, first read
- i      = EXACT current [i]: cmd /c ping -n 2, HasExited poll, first read
- j      = natural exit, HasExited poll, Close, read
- k      = ping -n 2 natural exit, WaitForExit, read
- l      = Kill, WaitForExit, read twice
- m      = Kill, HasExited poll, read twice
- n      = Kill, WaitForExit, Close, read twice
- o      = live read, Kill, WaitForExit, Close, read (alive|after)

### Batch 1 (20 runs each, raw counts)

```
orig_h : !InvalidOperationExceptionx18 PINGx2
a : !InvalidOperationExceptionx18 PINGx2
b : !InvalidOperationExceptionx20
c : !InvalidOperationExceptionx20
d : !InvalidOperationExceptionx20
e : PING|PINGx20
f : !InvalidOperationExceptionx20
g : !InvalidOperationExceptionx20
i : !InvalidOperationExceptionx20
j : !InvalidOperationExceptionx20
k : !InvalidOperationExceptionx20
l : !InvalidOperationException|!InvalidOperationExceptionx20
m : !InvalidOperationException|!InvalidOperationExceptionx20
n : !InvalidOperationException|!InvalidOperationExceptionx20
```

### Batch 2 (20 runs each, independent repeat + mode o)

```
orig_h : !InvalidOperationExceptionx19 PINGx1
a : !InvalidOperationExceptionx19 PINGx1
b : !InvalidOperationExceptionx20
c : !InvalidOperationExceptionx20
d : !InvalidOperationExceptionx20
e : PING|PINGx20
f : !InvalidOperationExceptionx20
g : !InvalidOperationExceptionx20
i : !InvalidOperationExceptionx20
l : !InvalidOperationException|!InvalidOperationExceptionx20
m : !InvalidOperationException|!InvalidOperationExceptionx20
o : PING|!InvalidOperationExceptionx20
```

### Batch 3 (20 runs, exact-[e] mode + repeat of o)

```
e2 : PING|PING|PINGx20
o : PING|!InvalidOperationExceptionx20
```

### Extended repeat (60 runs each)

```
b x60 : !InvalidOperationExceptionx60
g x60 : !InvalidOperationExceptionx60
i x60 : !InvalidOperationExceptionx60
```

### Conclusion (RED evidence)

- `orig_h` (= current `[h]`) is NOT deterministic: 3/40 runs answered `PING`
  (18+2 and 19+1) — exactly the 615B/33ac9c77 false-red.
- Candidate (a) (Kill + immediate read) is the same race: 3/40 `PING`.
  Killing and then reading a never-read `ProcessName` races the kernel's teardown of the
  process object: the read sometimes still answers.
- Candidate (b) Kill + WaitForExit + first read: 100/100 `!InvalidOperationException`.
  (c)/(d) 20/20 each, (f) 20/20: any full-exit barrier + no live-read cache = IOE.
- Candidate (e) live read + Kill + WaitForExit: 40/40 `PING|PING`. Exact current `[e]`
  (live read + Kill + HasExited poll): 20/20 `PING|PING|PING`. The answering arm is
  deterministic only when the name was read while alive (net48 caches it).
- (o) live read + Kill + WaitForExit + `Close`: 40/40 `PING|!InvalidOperationException`
  (`Close` drops the cache) — measured but NOT used, because it adds a new contract.
- Current `[g]` 100/100 IOE, `[i]` 100/100 IOE, `[e]` 20/20 PING: no rewrite needed.

## Stage 2 — rewrite of `[h]` (both arms deterministic, exact bytes)

- `[h.unread_after_kill]` = never read alive -> `Kill()` -> **`WaitForExit()`** -> read
  => `!InvalidOperationException` (candidate b; 100/100).
- `[h.cached_after_kill]` = live read (fills net48's cache) -> `Kill()` -> `WaitForExit()`
  -> read => `PING` (candidate e; 40/40), plus
  `[h.cached_after_kill.eq_alive]=True`.
- No fuzzy "either outcome" matching: each record prints one exact value or an exact bool.
- The old bare `HasExited`-poll window (which raced) is gone.
- Records added/split: 23 record lines now (was 21); the oracle byte length and sha change.
- Header comment updated to the measured semantics (the old "Kill, then read -> cached
  value or IOE" line was the racy claim and is replaced by the measured statement).

## Stage 3 — determinism proof (10 consecutive difftest runs)

Command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\difftest.ps1 -Case probe-procname`
Run from the worktree with the gate PATH. Raw logs: `.opencode/tmp/procname-probe/det-run-1..10.log`.

```
run 1  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 2  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 3  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 4  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 5  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 6  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 7  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 8  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 9  rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
run 10 rc=0 ok=True oracle 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020 runtime 702B sha=1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020
```

10/10 `ok=True`, all 10 oracle shas identical AND equal to the runtime sha.
**New anchor sha = `1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020`.**
Old anchor `ba80bc4b...` is retired (it was the lucky IOE arm of the race).
Oracle records: 23 (was 21). `[h]` family now:
`[h.unread_after_kill]=!InvalidOperationException`, `[h.cached_after_kill]=PING`,
`[h.cached_after_kill.eq_alive]=True`.

## Stage 4 — C0 build + unit tests

- `dotnet build src\il2cppc -c Debug` -> EXIT=0, 0 warnings, 0 errors (transpiler used by
  difftest: `src/il2cppc/bin/Debug/net8.0/il2cppc.dll`).
- `dotnet test tests\unit -c Debug` on the fresh worktree first failed 6/4094 ONLY because
  the net48 oracle fixtures had never been built in this worktree
  (`fixture ...\tests\cases\arrayext2|base64case|delecb2\obj\Release\net48\*.exe not found
  -- build the case first`). Fix per the script's own contract: run
  `scripts\build-unit-fixtures.ps1` (fixtures built=43 skipped=0 failed=0) then rerun.
- Rerun result: see "Stage 5 — final validation" below (appended after the rerun).

## Stage 5 — final validation (after fixtures)

- `scripts\build-unit-fixtures.ps1` -> `fixtures built=43 skipped=0 failed=0` (EXIT=0).
- `dotnet test tests\unit -c Debug` -> EXIT=0, `失败: 0，通过: 4094，已跳过: 0，总计: 4094`
  (38 s). Raw log: `.opencode/tmp/procname-probe/unit-test-run2.log`.
- The 6 earlier failures were fixture-absence only (fresh worktree), not regressions; after
  building the documented fixtures the suite is 4094/0/0.
- Final changed files: `tests/cases/probe-procname/probe-procname.cs` (only) + this NOTES.md.
  No `src/`, `scripts/`, or other-case edits. No `expected.txt` exists in the case.
- New anchor: `1f2ef33e7df68f48a9e329d4d85dc2958624f085d226b474caa9d1a9325ef020`
  (702B, oracle == runtime, 10/10 consecutive `ok=True`).
