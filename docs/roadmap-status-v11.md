# Roadmap Status v11 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-16 (snapshot final: batches 183-184 progress; supersedes snapshot
  v10; v10 kept at `docs/roadmap-status-v10.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  183-184 progress (**WinForms Phase 27 ChooseFontW -- the comdlg32 dialog
  family (Open/Color/Font) is now complete + COM stage 12 IStream via
  CreateStreamOnHGlobal with the LARGE_INTEGER ABI solution**), and the next
  batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `a66c7934317202eb5dd98ef50a38042e61377aa3`
    (branch m1; commit chain new->old, [BASH:git log --oneline -10]): `a66c793`
    (batch 184) -> `bf6912c` (batch 183) -> `a859974` (docs v10) -> `c8507df`
    (batch 182) -> `24da086` (batch 181) -> `035ef71` (batch 180b) -> `c79ef30`
    (batch 180) -> `3f79a12` (batch 179) -> `63b5ab8` (batch 178) -> `77d1d42`
    (batch 178b).
  - `git status --short` -> [BASH] worktree clean except `?? tmp/` (probe
    scratch only); no uncommitted src/test changes.
  - Batch commit messages (full subjects, [BASH:git log]):
    - `bf6912c` (batch 183): **WinForms Phase 27 FontDialog** -- comdlg32
      ChooseFontW with the x64 CHOOSEFONTW + LOGFONTW (pinned int[] layouts
      offsetof-probed: LOGFONTW 92B/int[23], lfHeight@0=-16, lfWeight@16=
      FW_NORMAL, lfCharSet@23=DEFAULT_CHARSET packed as int[5]=0x01000000,
      lfFaceName@28 WCHAR-pair packed 'Arial' (int[7]=0x00720041/
      int[8]=0x00610069/int[9]=0x0000006C); **CHOOSEFONTW 104B/int[26] -- the
      audit draft's 96B was short the SDK trailing hInstance/lpszStyle/
      nFontType/nSizeMin/nSizeMax** -- lStructSize@0=104, hwndOwner@8=NULL,
      lpLogFont@24=pinned LOGFONTW, Flags@36=CF_INITTOLOGFONTSTRUCT),
      worker-thread modal + thread-id-filtered FindWindowExW(#32770,
      GetWindowThreadProcessId == worker tid) + GetDlgItem(IDOK) + synchronous
      SendMessageW(BM_CLICK), iPointSize@32 + lfFaceName face-name readback
      (p27_face_match 'Arial') -- **completes the comdlg32 dialog family
      (Open/Color/Font)**, net48 byte-exact 361/361. Zero new shim (comdlg32
      INTERFACE shim from Phase 25 forwards ChooseFontW). (Layout probe-
      verified: the real SDK struct carries 104B, not the audit draft's 96B --
      sizeof AND lStructSize itself both probed, discover 218.)
    - `a66c793` (batch 184): **COM stage 12 IStream** --
      icall_com_stream_create_on_hglobal/get_hglobal/get_iunknown +
      icall_com_istream_read/write/seek/set_size (**dedicated int64_t-parameter
      icalls for the by-value LARGE_INTEGER/ULARGE_INTEGER ABI -- the 4x-intptr
      vtable-slot carrier splits 64-bit values into mirror-image 32-bit halves
      across x86/x64 and cannot generalize, empirically verified**); ComStream
      non-RCW runtime-only class descriptor (System.Runtime.InteropServices.
      ComStream, IsComObject false -- distinct from the RCW descriptor), ole32
      CreateStreamOnHGlobal/GetHGlobalFromStream resolved at runtime (kept OUT
      of GetOle32().ok -- the stage-11 strategy), streamio.cpp kCom StreamState
      delegation (read/write/flush/position/length/seek/set_length/
      StreamGetByte/close; read_line/read_to_end documented nullptr; StatSize
      through a **full-size STATSTG buffer -- the truncated mirror overflowed
      the stack, probe-caught**), Marshal.CreateStreamOnHGlobal/
      GetHGlobalFromStream wired (MethodMap.Marshal.cs com4). test_com section
      14 + 4g (36 new assertions, **360 total**: byte round-trip /
      0x0123456789ABCDEF ABI rejection discriminator / EOF / SetLength native
      passthrough / close idempotent / arg guards / POSIX E_NOTIMPL), net48
      oracle byte-identical (P/Invoke ole32 + GetObjectForIUnknown RCW -- net48
      Marshal lacks these members, netcore additions), runtime 139/139. Zero new
      shim (pure runtime internal, no DllImport into generated code). (The
      HGLOBAL IStream's internal seek is 32-bit wide -- the anchor test became
      a "rejection discriminator" not a round-trip, discover 222.)
  - WinForms suite line counts (THIS turn, [BASH:Measure-Object]):
    `tests/cases/winforms1/expected.txt` = 361 lines, `actual.txt` = 361 lines
    (Phase 27 after; net48 vs transpiled C++ byte-exact, Compare-Object
    DIFF=0 IDENTICAL, per batch-183 verification).
  - Runtime suite count (THIS turn, [BASH:Measure-Object]): 139
    `tests/runtime/test_*.cpp`.
  - M-stage gate documents (this turn):
    - `docs/audit/e2e-full-matrix7.md` [READ:lines 1-12]: matrix7 100% gate at
      HEAD cef2bdd -- **196-case matrix = 195 correctness + 1 bench (virtbench,
      checksum-identical timing, not a real FAIL)**; 250 total case dirs, 246
      PASS / 4 FAIL / 0 MISSING with the 4 FAILs all OUTSIDE the 196 口径
      (decfmt2/resmgmt/resops known gaps, non-regression).
    - Full-matrix 413-case parallel re-sweep: **413 cases / 104 batches 100%
      PASS** -- completed per plan.md [READ:.opencode/plan.md line 6 "[x] 全矩阵
      413 案例 104 批次并行复扫：100% PASS"] and memory.md [READ:.opencode/
      memory.md "全量复扫已完成（413 案例 104 批次 100% PASS）"].
    - `docs/audit/bench-verify-m7-20260815.md` (committed at 96d6880) + docs
      commit `c8e320c`: **13/13 zero-regression confirmed** vs baseline
      efa6741 (batch 168), re-confirmed after the BUG-1 fix.
    - Unit counts: **unit 1808/1808** (first-ever all-green, batch 180b
      `035ef71`; 2 stale-test fixes, zero src changes) [READ:.opencode/
      memory.md line 4 "unit 1808/1808 全绿"]; **runtime 139/139** re-measured
      [BASH:Measure-Object 139 test_*.cpp]; test_com assertions **360** (batch
      184, +36 section 14 + 4g) [READ:.opencode/process.md "360 断言"].
- M-stage gate status:
  - **M5** BCL full translation: matrix7 **196/196** confirmed
    (e2e-full-matrix7.md: 195 correctness + 1 bench = 100%). **Full-matrix
    413-case / 104-batch parallel re-sweep 100% PASS** (completed, prior wave);
    no M5-relevant case regressed in batches 183-184 (all winforms/com surface,
    e2e byte-exact per batch).
  - **M6** Transpiler: unit **1808/1808** (all-green, carried from batch 180b)
    + runtime **139/139** (139 test_*.cpp [BASH:Measure-Object]) + test_com
    **360 assertions** (batch 184, +36; stage-12 IStream section 14 + POSIX 4g)
    + transpiler test surface (21 surfaces, carried from v9); batch 183/184
    produced 0 transpiler errors (winforms1 transiled "generated 19 type(s)",
    no error).
  - **M7** Performance: **zero-regression confirmed 13/13** vs baseline
    (docs/audit/bench-verify-m7-20260815.md at 96d6880, re-confirmed at
    `c8e320c`); no bench-affecting change landed in batches 183-184 (all
    winforms/com surface).
  - **M8** COM interop: stages 1-12 done (**stage 12 IStream batch 184**;
    prior: stage 9 multi-sink 176b, stage 10 IEnumVARIANT 178, stage 11 BSTR
    items 181); WinForms 27 phases done (**Phase 27 ChooseFontW batch 183 --
    comdlg32 family complete**; prior: 22 TreeView / 23 PictureBox / 24
    NotifyIcon / 25 file dialog / 26 ColorDialog); S2 remaining: macOS/OpenBSD
    real-machine verification (external hardware).
- Remaining / next batch pool (task order):
  - COM stage 13 (**drag-drop IOleDropTarget / or Marshal.StringToBSTR
    wiring** -- in progress planning).
  - WinForms Phase 28.
  - #114 original crash repro (needs user case).
  - M8 S2 macOS/OpenBSD real-machine (external hardware dependency).
