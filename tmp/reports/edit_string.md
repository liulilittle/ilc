# edit_string.md — System.String mapping sweep (net48 + net9 public surface)

Agent scope: `src/il2cppc/Emit/MethodMap.String.cs` + `MethodMap.StringExt3.cs` ONLY.
Method: for each candidate member, grep `il2cpp_string*.h` + `il2cpp_concat.h` for an
existing runtime icall with matching semantics; wire ONLY existing icalls; everything
else → needs-new-icall below. Backups: `tmp/backups/edit_string/*.bak`.

---

## 1. ADDED mappings (icall exists — exact header name)

### Join(char separator, string[]/object[] values)  [.NET Core 2.0+ / net9]
- Wired to: `il2cpp::runtime::il2cpp_string_builder_join`
  (declared `il2cpp_concat.h:104`, impl `concat.cpp:352`).
- Shape: `ps = (0x03 char, 0x1D SZARRAY)`, static, casts `{ "c", "" }`,
  ReturnTypeName `System.String`.
- Semantics verified in impl: delegates to `ConcatFromArray(values, true, sep)`
  (`concat.cpp:354`) — string elements join directly, NON-string elements convert
  via `icall_object_to_string` (`concat.cpp:102-106`, the object[] overload
  semantics), null elements count as empty, requires ref-element arrays
  (`concat.cpp:136`). Same documented null-array deviation as the wired
  `(string, string[])` join.
- Deliberately NOT wired: the generic `IEnumerable<T>` shape (`ps[1] == 0x15`) —
  a value-typed array argument (e.g. `byte[]`) is not a reference array and
  `ConcatFromArray` would return `""` defensively instead of joining
  (`concat.cpp:136`). Stays on the loud throw.

## 2. CORRECTED mappings (silently-wrong shapes made exact or LOUD)

All four are pre-existing entries whose unwired overloads produced wrong code or
fold stack desyncs instead of failing loudly (the file's own convention, e.g. the
IndexOf `_ => throw` dispatch).

1. **Trim(params char[]) / Trim(char)** — was mapped unconditionally to the
   0-param `icall_string_trim` while the IL pushes this + array; ParamCount must
   track IL arity per the TrimStart lockstep discipline documented at
   MethodMap.String.cs (TrimStart comment). Now: `ps.Length == 1` → pc=1 +
   `{ "", "drop" }` (TrimStart/TrimEnd pattern); trim-set still ignored by the
   whitespace-only runtime (same documented deviation).
2. **Contains(char)** (net9) — was routed to `icall_string_contains`, whose value
   param is `Il2CppObject*` (`il2cpp_string_ext2.h:142`); a raw char crossing that
   slot is a garbage-pointer deref. Now string-shape only (`ps[0] == 0x0E`);
   char shape throws NotSupported.
3. **GetHashCode(StringComparison)** (net9) — was routed to the 0-param
   `icall_string_get_hash_code` (pc=0 vs IL arity 1 → shadow-stack desync). Now
   gated on `ps.Length == 0`; other shapes throw NotSupported.
4. **Compare(string, string, bool ignoreCase)** — the shared Compare/CompareOrdinal
   branch dropped ANY 3rd arg, so `ignoreCase: true` silently returned the
   case-SENSITIVE ordinal result. No mode-aware static compare icall exists.
   Now: enum drop (`ps[2] == 0x11`, documented deviation) kept; bool shape
   (`ps[2] == 0x02`) throws NotSupported naming the needed icall.

## 3. Already wired — verified, NO change needed

| Candidate | Status | Evidence |
|---|---|---|
| Compare static 2-arg / (…,StringComparison) | wired (ordinal; mode dropped = documented deviation) | MethodMap.String.cs Compare case |
| Format 3-arg / 4-arg | wired | `icall_string_format_2/_3` exist: il2cpp_stringbuilder.h:251-254 |
| Replace(char,char) / Replace(string,string) | wired | icall_string_replace ext.h:88; icall_string_replace_s_s ext.h:102 |
| get_Chars (single shape) | wired (inline body) | icall_string_get_chars ext2.h:184 |
| Split(char[]) 1-arg | wired | icall_string_split ext.h:163 |
| Split(char[]/string[], StringSplitOptions) | wired (isRefElem disambiguation) | icall_string_split_options ext.h:197, stringext.cpp:846 |
| IndexOf/LastIndexOf positional + StringComparison families | wired | all 17 declarations verified in il2cpp_string_ext3.h |
| PadLeft/PadRight, Insert, Remove(int,int), Substring, Concat family, Copy, Intern/IsInterned, IsNullOrEmpty/WhiteSpace, op_Equality/op_Inequality, Equals family | wired | MethodMap.String.cs |

**Caveat found (no map-level fix possible):** `Split(string[])` **1-arg** shares the
char[] entry (both surface as `ps=(0x1D)` after element-byte consumption), but
`icall_string_split` reads separators with a fixed char16 stride
(`stringext.cpp:723 reinterpret_cast<char16_t*>`) — unlike split_options it does
NOT check `isRefElem`. A non-empty string[] separator set would be misread.
Needs a runtime-side fix (mirror split_options' disambiguation) or a new icall.

## 4. needs-new-icall list (NO existing icall — nothing invented)

High priority (common user code):
- `ToCharArray()` / `ToCharArray(int, int)` — no icall anywhere in the string headers.
- `CopyTo(int, char[], int, int)`.
- `Contains(char)` — needs e.g. `icall_string_contains_c`.
- `Remove(int startIndex)` (1-arg form; only the 3-param range icall exists, ext.h:122).
- `IndexOfAny(char[])` / `IndexOfAny(char[], int)` — only the (char[],int,int) range
  icall exists (ext3.h:220); map cannot synthesize default args.
- `LastIndexOfAny(char[])` / `LastIndexOfAny(char[], int)` — same (ext3.h:230).
- `Split(char separator)` and all count-aware splits:
  Split(char[]|string[], int), Split(char[]|string[], StringSplitOptions, int),
  Split(char, int), Split(char, StringSplitOptions), Split(char, int, StringSplitOptions),
  Split(string, StringSplitOptions), Split(string, int, StringSplitOptions).
- `Join(string, string[], int, int)` and `Join(char, string[], int, int)` — no
  start/count-aware join icall.
- Mode-aware static compare: `Compare(string, string, bool ignoreCase)` (+ the
  CultureInfo/CompareOptions 4/5-arg forms) — needs a flag/mode-taking compare icall.
- `GetHashCode(StringComparison)` — needs a mode-aware hash icall.
- `Split(string[])` 1-arg runtime stride fix (see caveat above).

Lower priority:
- `ReplaceLineEndings()` / `ReplaceLineEndings(string)` (net Core+/net9).
- `Replace(string, string, StringComparison)` / `(string, string, bool, CultureInfo)` (net9).
- `GetEnumerator()` / CharEnumerator (foreach-over-string support surface).
- `CompareOrdinal(string, int, string, int[, int])` — possibly reusable from
  `icall_string_native_compare_ordinal_ex` (already mapped for the internal method,
  MethodMap.String.cs) pending slot-semantics verification.
- `Normalize()` / `IsNormalized()` — globalization domain.
- `Clone()` — needs NO icall: net48 returns `this`; an IdentityMarker fold exactly
  like the existing String.ToString() entry would suffice (left unedited per the
  icall-only constraint; flagged for orchestrator decision).

## 5. Files touched

- `src/il2cppc/Emit/MethodMap.String.cs` — sections 1 + 2 above (5 edits).
- `src/il2cppc/Emit/MethodMap.StringExt3.cs` — NO changes (every mapping verified
  against a real il2cpp_string_ext3.h declaration).
