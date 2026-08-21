# Base-Conversion Domain Conformance Audit (Base64 / BitConverter / String Hash / Ordinal Compare)

Date: 2026-08-04 · Domain: convertbase64.cpp / bitconverter.cpp / valuesemantics.cpp + tests
Method: .NET reference source reading (net48 referencesource + .NET Core v9.0.0, saved under
`reference/netcore/`) + **net48 csc.exe oracle probe** (`%TEMP%\opencode\bcconv_probe\`) +
**.NET Core 8 runtime probe** (`%TEMP%\opencode\core_probe\`) + standalone g++ cross-check.
Baseline: net48 (the project's declared reference semantics; probes are authoritative).

## 1. Base64 — Convert.ToBase64String / FromBase64String

Reference sources compared:
- net48: `reference/netcore/net48_convert.cs` (FromBase64CharPtr:2420, FromBase64_Decode:2474,
  FromBase64_ComputeResultLength:2654)
- .NET Core v9: `reference/netcore/System.Private.CoreLib/src/System/Convert.cs`
  (FromBase64String:2641, FromBase64CharPtr:2824, FromBase64_ComputeResultLength:2868) +
  `Convert.Base64.cs` (TryDecodeFromUtf16 fast path)

### 1a. Semantics that CONFORM (no change)
- Encode: 3 bytes → 4 chars, final quantum zero-padded with `=`; vectors pinned
  (net48 probe: `{0xFF}`→"/w==", `{0xFF,0xFE}`→"//4=", 你好世界→"5L2g5aW95LiW55WM") — all match.
- Whitespace set = exactly space/tab/CR/LF, ignored anywhere in the input
  (net48 FromBase64_Decode:2540, Core IsSpace:2785) — matches.
- Illegal char / char after `=` / `=` before the final quantum → FormatException — matches.
- Null → ArgumentNullException — matches.

### 1b. Divergences found and FIXED (probe-evidenced)
| Input | net48 oracle | .NET Core 8 probe | old impl | new impl |
|---|---|---|---|---|
| `"TWE"` (unpadded 3) | FormatException | FormatException | **accepted, 2 bytes** | FormatException |
| `"TW"` (unpadded 2) | FormatException | FormatException | **accepted, 1 byte** | FormatException |
| `"AB= "` (lone `=` after 2 chars) | FormatException | FormatException | **accepted, 1 byte** | FormatException |
| `"AB="` | FormatException | FormatException | FormatException | FormatException |
| `"TWE=="` (3 valid + `==`) | **1 byte {0x58}** | FormatException | **FormatException** | 1 byte {0x58} |
| `"TWE =="`, `"TWE= ="`, `"TWE== "` | 1 byte {0x58} | FormatException (ws inside pads) | FormatException | 1 byte {0x58} |
| `"TW =="` (2 valid + ws + `==`) | 1 byte {0x4D} | 1 byte | 1 byte | 1 byte |
| `"ABCD=="` / `"AAAAA=="` / `"TWE==="` | FormatException | FormatException | FormatException | FormatException |
| `"AAAAAA=="` (6 valid + `==`) | 4 zero bytes | 4 zero bytes | 4 zero bytes | 4 zero bytes |

Root cause: the old pass-1 rule "`=` legal at quantum positions 2/3" (a) accepted a lone `=`
at position 2 (net48 trims trailing whitespace FIRST, so `"AB= "` ≡ `"AB="` and throws),
(b) rejected the net48 "3 valid chars + `==`" shape (`"TWE=="`), and (c) accepted unpadded
2/3-char leftovers (net48's decoder requires the final quantum to complete: 4 valid, 3+`=`,
2+`==`, or 3+`==`).

Fix (convertbase64.cpp): pass-1 now tracks the first-`=` position (2 vs 3):
- `=` at position 2 → requires a closing second `=` (else FormatException, incl. `"AB= "`);
- `=` at position 3 → final, or followed by one closing `=` at next-quantum position 0
  (the `"TWE=="` shape);
- leftover/padding coupling: leftover 1 → throw; leftover 0/2/3 legal only with 0/2/1-2
  pads respectively (net48 length formula: `(valid/4)*3 + (pad==1 ? 2 : pad==2 ? 1 : 0)`).
Pass-2 is now a faithful port of the net48 FromBase64_Decode marker-block state machine
(4×6-bit accumulator + 0xFF marker bit; `=` shifts 6/12 zero bits), which reproduces the
net48 byte output for every legal shape — including `"TWE=="` → {0x58} (byte from the LAST
two valid chars).

Verification: 41-vector cross-check harness (`%TEMP%\opencode\bcconv_build\crosscheck.cpp`)
produces output byte-identical to the net48 oracle for all 41 cases (incl. control chars
`\x01`/`\x0B` → FormatException, `\x0B` after `=` → FormatException, CRLF inside → OK).

### 1c. net48 vs .NET Core framework divergence (documented, NOT fixed)
`.NET Core v9 REJECTS the "3 valid + `==`" family ("TWE==", "TWE ==", "TWE= =") with
FormatException; net48 accepts and returns 1 byte.` The project baseline is net48 (user
principle: net48 probes are authoritative), so the runtime follows net48. If the baseline
ever moves to .NET Core, the `"TWE=="` allowance (pass-1 `!firstEqAtPosTwo` branch and
pass-2 `<<= 12` path) is the exact code to remove.
Also: `Convert.FromBase64String("")`/`" "` → empty array in BOTH frameworks — matches.

## 2. BitConverter

Reference: .NET Core v9 `reference/netcore/.../BitConverter.cs` + net48
`reference/netcore/net48_bitconverter.cs`.

### 2a. Conforms (no change)
- `GetBytes(T)`: .NET Core writes the value natively (`Unsafe.As<byte,int>`, GetBytes(int):108)
  → little-endian on x86-64 = our memcpy. All layout vectors probe-verified identical
  (int32 1 → 01-00-00-00, double 1.0 → 00..F0-3F, char 0x4142 → 42-41, int16 -2 → FE-FF, ...).
- Bit bridges = pure BitCast/memcpy in .NET Core (BitConverter.cs:895-919) = our BitBridge;
  NaN payload roundtrip `7FF8000000000123` preserved, canonical NaN `FFF8000000000000`,
  `-0.0f` → `0x80000000` — all probe-verified identical.
- ToT: null → ArgumentNullException; non-empty undersized → ArgumentException
  (net48 & Core identical) — matches.
- ToSingle = Int32BitsToSingle(ToInt32(...)) (BitConverter.cs:719) — matches.

### 2b. Divergence found and FIXED (probe-evidenced)
EMPTY byte[] + startIndex 0: **ArgumentOutOfRangeException** in BOTH net48
(net48_bitconverter.cs ToInt32: `(uint)startIndex >= value.Length`) and .NET Core
(BitConverter.cs:462 `(uint)startIndex >= (uint)value.Length`) — the OLD impl threw
ArgumentException. Fix: `ToValue` now throws AOOE for `length == 0`, ArgumentException for
`0 < length < sizeof(T)`. (net48 lacks `SingleToInt32Bits`/`Int32BitsToSingle` — .NET Core
additions; our runtime implements the .NET Core surface there, correct as-is.)

## 3. String.GetHashCode

### Findings
- **net48** (COMString::GetHashCode): two 5381-seeded accumulators, char-wise — the
  implementation in valuesemantics.cpp HashStringOrdinal. Re-probed 2026-08-04: all 12
  pinned oracle values in test_stringhash.cpp match exactly ("" 371857150, "a" 372029373,
  "abcd" -1664220650, "hello" -327419862, "Hello, World!" 562640209, hello你好 1724669551,
  你好 2047063037, 1000×'x' 1082926846, 5×U+00E9 -2103616059, ...). CONFORMS — no change.
- **.NET Core public `string.GetHashCode`** (String.Comparison.cs:753-758): **Marvin with a
  per-process RANDOM seed** — values vary between runs (probe: "hello" → 300644401 on this
  run; stable within one process). Byte-compat with net48 is impossible by design.
- **.NET Core internal `GetNonRandomizedHashCode`** (String.Comparison.cs:830-858): this is
  the "5381 seed" the task hypothesis referenced — BUT it seeds `(5381 << 16) + 5381` and
  processes TWO chars per step (`RotateLeft(hash,5)+hash ^ uint`), producing DIFFERENT values
  from net48's plain-5381 char-wise form. The runtime deliberately pins the net48 form
  (deterministic AOT, probe-verified values in test_stringhash.cpp) — the choice is
  documented in valuesemantics.cpp:77-89. NO CHANGE (switching seeds would break the pinned
  oracle values and the hashtable consistency contract).

## 4. String ordinal comparison

Reference: .NET Core `String.Comparison.cs` CompareOrdinalHelper:76-192.
- .NET CompareOrdinal returns the **numeric difference of the first differing char unit**
  (`if (*a != *b) return *a - *b`, line 187; second unit line 191), prefix → length
  difference (line 162). net48 CompareOrdinal is the same shape.
- Our `icall_string_compare_to` (stringext.cpp — READ-ONLY this task): first differing unit
  → `chars[i] - chars[j]` (int32), prefix → `length diff`. **CONFORMS** to CompareOrdinal's
  first-diff semantics; .NET Core probe: `CompareOrdinal("A","a") == -32`,
  `CompareOrdinal("abc","abd") == -1`, `CompareOrdinal("\u0100","\u00ff") == 1` — same sign
  and (for first-char diffs) same value as ours.
- Documented nuance: .NET Core's word-at-a-time compare includes the null terminator in the
  final 2-char unit, so `CompareOrdinal("abc","abcd") == -100` (0x00 - 'd') rather than -1;
  sign is identical, exact value is an implementation detail. Also `string.CompareTo` in
  net48 is CULTURE-aware (probe: `"A".CompareTo("a") == 1`, not -32) — the runtime's
  CompareTo is ordinal by design (no culture support); sign-equivalent, value differs.
  Both are pre-existing documented deviations, outside the fix list.

## 5. Changes made (each with ≥1 assertion)

1. convertbase64.cpp — pass-1 `=` placement + leftover/padding coupling + pass-2 net48
   decoder port. Assertions: `"TWE==" → {0x58}`, `"TWE ==" / "TWE= =" / "TWE== " / "TWE==\n"`
   → {0x58}, `"TW==" → {0x4D}`, `"AB= =" / "AB= = " → {0x00}`, `"AAAAAA==" → 4×0x00`;
   throws: `"TWE"`, `"TW"`, `"AB="`, `"AB= "`, `"A=="`, `"ABCD=="`, `"AAAAA=="`.
2. bitconverter.cpp — `ToValue` empty array → ArgumentOutOfRangeException. Assertions:
   to_bool/to_int16/to_int32 on byte[0] → AOOE (chain tag pinned); byte[1]/[3]/[7] →
   ArgumentException unchanged.
3. valuesemantics.cpp — audited only (hash/compare conform; oracle values re-verified),
   NO code change.
4. Tests: test_convertbase64.cpp (fixed the WRONG old "TWE unpadded → 2 bytes" assertion —
   it asserted non-net48 behavior; +11 new assertions), test_bitconverter.cpp (+3 AOOE
   assertions), test_stringhash.cpp unchanged (all 14 oracles already correct).

## 6. Verification evidence

- [BASH] cmake configure OK; full build blocked by in-flight parallel-agent files
  numberformat.cpp (const-assign error) + formatext.cpp (undeclared MakeString) — per task
  fallback, standalone g++ used (81/81 sources compile clean, metadata.cpp now fixed by its
  agent; stub_format.o satisfies one unused formatting symbol; NOT production code).
- [BASH] g++ -Wall -Wextra -fexceptions -fno-rtti standalone builds + runs:
  - test_convertbase64 → PASS exit 0
  - test_bitconverter → PASS exit 0
  - test_stringhash → PASS exit 0
  - test_valuesemantics → PASS exit 0
- [BASH] crosscheck.exe: 41 base64 vectors byte-identical to the net48 csc.exe oracle.
- [BASH] net48 oracle probe + .NET Core 8 probe outputs cited in the tables above.

## 7. Out-of-domain flags (read-only files needing later attention)

- `include/internal/il2cpp_convertbase64.h:8-19` still documents "Unpadded input is
  accepted (e.g. 'TWE' decodes to two bytes)" — now known WRONG (net48 throws); header is
  outside this task's writable domain. Needs a comment update in a future task.
- numberformat.cpp / formatext.cpp in-flight (other agents) — block full cmake until fixed.
