# edit_convert — System.Convert surface sweep (edit agent report)

Date: 2026-08-22 · Agent: EDIT (owned files only) · Builds: NONE (per constraint) · Git: NONE

## 0. Scope

Sweep the `System.Convert` surface — ToInt16/ToInt32/ToInt64/ToByte/ToSByte/
ToUInt16/ToUInt32/ToUInt64/ToSingle/ToDouble/ToDecimal/ToChar/ToBoolean ×
string/value overloads, plus ToBase64String/FromBase64 variants — against the
current MethodMap wiring, wiring ONLY icalls that already exist in
`src/runtime/include/internal/il2cpp_convert*.h` + `il2cpp_conv.h`, and
listing everything else as needs-new.

Owned files edited:
- `src/il2cppc/Emit/MethodMap.Convert.cs`   (rewritten)
- `src/il2cppc/Emit/MethodMap.ConvertExtra.cs` (rewritten)
- `src/il2cppc/Emit/MethodMap.Base64.cs`    (NOT changed — surface complete vs available helpers)

Backups: `tmp/backups/edit_convert/*.20260822_041932.bak`.

## 1. Runtime inventory found (grep results)

`il2cpp_convert.h` — 17 icalls, ALL already wired pre-sweep (verified line-by-line):
`icall_convert_to_int32_string/_string_base/_i8/_d/_char/_bool`,
`icall_convert_to_double_i4/_string`, `icall_convert_to_int64_string`,
`icall_convert_to_string_i4/i8/d/bool/_i4_base`, `icall_convert_to_bool_i4`,
`icall_convert_to_char_i4`.

`il2cpp_convertbase64.h` — 2 icalls, both already wired:
`il2cpp_convert_to_base64_string(Il2CppArray*)`,
`il2cpp_convert_from_base64_string(Il2CppString*)`.

`il2cpp_conv.h` — conv family, ZERO references in src/il2cppc before this sweep.
The checked narrow batch (152e/154b) is the decisive new capacity:
- `icall_conv_ovf_u1_i8 / _i1_i8 / _u2_i8 / _i2_i8(int64)` — checked byte/sbyte/ushort/short from int64 ([READ:il2cpp_conv.h:235-238])
- `icall_conv_ovf_u1_u8 / _i1_u8 / _u2_u8 / _i2_u8(uint64)` — same from uint64 bit pattern ([READ:il2cpp_conv.h:246-249])
- `icall_conv_ovf_u4_i4/_i8, ovf_u4_u8, ovf_u8_i4/_i8, ovf_i8_u8, ovf_i4_u8` — checked u4/u8/i8 forms ([READ:il2cpp_conv.h:189-209])
- zero-extension: `icall_conv_u8_i4(int32)` ((long)uint.MaxValue == 4294967295) ([READ:il2cpp_conv.h:124-128])
- float family: `icall_conv_r4_d/i4/i8/u4/u8` → float returns ([READ:il2cpp_conv.h:173-187])
- widenings: `icall_conv_d_i4/_i8/_u4/_u8`, `icall_conv_i8_i4`, `icall_conv_i8_u8`

Supporting facts verified:
- `il2cpp_conv.h` is included in EVERY generated file: [READ:CodeGenerator.cs:100]; convert/convertbase64/format/parse headers likewise (:99-110).
- Cast codes available at call sites: `"l"→int64_t`, `"q"→uint64_t`, `"i"`, `"d"`, `"s"`, `"b"` ([READ:CodeGenerator.cs:6252-6289]). NO unsigned VALUE cast code exists (only `deref:ui`).
- Float-returning icall precedent: MathF maps `float`-returning `icall_mathf_*_f` with resultKind `"d"` ([READ:MethodMap.MathF.cs:54]).
- `"q"` cast precedent for ulong slots: BitConverter.GetBytes(ulong), Console.WriteLine(ulong) ([READ:MethodMap.BitConverter.cs:82], [READ:MethodMap.Console.cs:94]).
- Dispatch chain: Convert → BitConverter(owner-guarded) → Base64 → ConvertExtra ([READ:MethodRefResolver.cs:636-640]); BitConverter refuses System.Convert owners ([READ:MethodMap.BitConverter.cs:58-61]).
- Parse domain is TryParse-contract ONLY (no-throw, out-param): [READ:il2cpp_parse.h:79-98] — unusable for throwing Convert string overloads.
- `icall_decimal_to_int32/to_int64/to_uint32/to_uint64` TRUNCATE then check = explicit-cast semantics, NOT Convert's banker's rounding ([READ:decimal.h:118-123]).

## 2. ABI slot model used (from header contracts)

int-family sources ride int32 slots; long/ulong ride int64 slots; bool/char are
int32 carriers (0/1, UTF-16 unit); float rides a double slot holding the exact
float; string/object ride Il2CppObject*. Results: int32-carried targets default;
64-bit targets resultKind `"l"`; float/double targets resultKind `"d"`.

Key proof reused throughout: sign-extending an int32 slot (`"l"`) preserves every
signed value exactly, and a uint bit pattern ≥ 2^31 reads negative — which fails
any `[0, max]` checked test. Since every Convert target range ⊆ [0, 2^31) except
the 64-bit ones, sign-extension routes are semantically EXACT for ALL int-family
sources of the narrow/u4 targets (a value that goes negative under extension is
precisely a value net48 throws on).

## 3. Sweep table (final state after this edit)

Legend: ps code → source. 02=bool 03=char 04=sbyte 05=byte 06=short 07=ushort
08=int 09=uint 0A=long 0B=ulong 0C=float 0D=double 0E=string dec=decimal.
**W** = wired this sweep · **P** = wired pre-sweep (unchanged) · **R** = LOUD
reject (reason) · **N** = needs-new icall.

### ToInt32 (MethodMap.Convert.cs)
| ps | status | target |
|----|--------|--------|
| 0E | P | icall_convert_to_int32_string |
| 0E+08 | P | icall_convert_to_int32_string_base |
| 04..0B | W (0A was P) | icall_convert_to_int32_i8 ["l"] — identity/widening/checked-exact for all int-family |
| 0C | W | icall_convert_to_int32_d ["d"] — float slot holds exact float; RNE == net48 ToInt32(float) |
| 0D | P | icall_convert_to_int32_d ["d"] |
| 03 | P | icall_convert_to_int32_char |
| 02 | P | icall_convert_to_int32_bool ["b"] |
| dec | N | banker's-rounding decimal converter (icall_decimal_to_int32 truncates — wrong semantics) |
| 0E+prov | N | culture-aware parse |
| obj | N | runtime type dispatch |

### ToInt64 (MethodMap.Convert.cs)
| ps | status | target |
|----|--------|--------|
| 0E | P | icall_convert_to_int64_string |
| 04,06,08 | W | icall_conv_i8_i4 ["i"]→"l" (sign-extension widening) |
| 05,07,03,02,09 | W | icall_conv_u8_i4 ["i"]→"l" (zero-extension; non-negative domains can't overflow) |
| 0A | W | icall_conv_i8_u8 ["q"]→"l" (bit-exact identity) |
| 0B | W | icall_conv_ovf_i8_u8 ["q"]→"l" (CHECKED: ≥2^63 throws — audit fix during sweep) |
| 0C,0D | N | **icall_convert_to_int64_d** (round half-to-even + range; existing double→i8 helpers truncate) |
| dec, 0E+prov, obj | N | |

### ToDouble (MethodMap.Convert.cs)
| ps | status | target |
|----|--------|--------|
| 0E | P | icall_convert_to_double_string |
| 08 | P | icall_convert_to_double_i4 |
| 04..07 | W | icall_conv_d_i4 ["i"]→"d" |
| 0A | W | icall_conv_d_i8 ["l"]→"d" (exact <2^53, RNE above) |
| 0B | W | icall_conv_d_u8 ["q"]→"d" (net48 oracle pinned in il2cpp_conv.h) |
| 02,03 | R | net48 InvalidCastException ALWAYS; no ICE-raising icall exists |
| 09 | N | zero-extending _i4-input form OR emitter unsigned value-cast code |
| 0C | N | trivial d→d identity helper (float slot already widened) |
| dec, 0E+prov, obj | N | |

### ToBoolean (MethodMap.Convert.cs)
| ps | status | target |
|----|--------|--------|
| 04..09 | W (08 was P) | icall_convert_to_bool_i4 (non-zero incl. negatives → true) |
| 02 | W | icall_conv_bool_i4 (identity on the documented 0/1 bool-slot domain) |
| 0A,0B | N | **icall_convert_to_bool_i8** (low-32 truncation misreads 0x100000000 as false) |
| 03,0C,0D | R | net48 InvalidCastException ALWAYS |
| 0E | N | throwing bool parser (null→false; only TryParse-contract exists) |
| dec, obj, prov | N | |

### ToChar (MethodMap.Convert.cs)
| ps | status | target |
|----|--------|--------|
| 04..09 | W (08 was P) | icall_convert_to_char_i4 (checked [0,0xFFFF]: negatives & uint≥0x8000 patterns throw) |
| 0A,0B | N | **icall_convert_to_char_i8** (truncation would accept 0x100000041 as 'A') |
| 02,0C,0D | R | net48 InvalidCastException ALWAYS |
| 0E | N | null→'\0', len≠1→FormatException |
| dec, obj, prov | N | |

### ToByte / ToSByte / ToInt16 / ToUInt16 (MethodMap.ConvertExtra.cs)
Shared shape per target suffix u1/i1/i2/u2:
| ps | status | target |
|----|--------|--------|
| 04..0A | W | icall_conv_ovf_{suffix}_i8 ["l"] (checked integer range test — exact net48 contract) |
| 0B | W | icall_conv_ovf_{suffix}_u8 ["q"] |
| 02 | W | icall_conv_bool_i4 (0/1 identity) |
| 03 | R | char semantics NOT pinned by any e2e run (pin before wiring — never guess) |
| 0C,0D | N | **icall_convert_to_byte_d / to_sbyte_d / to_int16_d / to_uint16_d** (ROUND half-to-even then check; existing ovf_*_d TRUNCATE — silently different: ToByte(3.5)==4 vs 3) |
| 0E | N | throwing parser w/ null-means-zero |
| dec, obj, prov | N | |

### ToUInt32 (MethodMap.ConvertExtra.cs)
| ps | status | target |
|----|--------|--------|
| 04..08 | W | icall_conv_ovf_u4_i4 ["i"] (negatives throw; byte/ushort pass unchanged) |
| 09 | N | identity conversion, no matching helper (sign-extending routes would WRONGLY throw uint≥2^31) |
| 0A | W | icall_conv_ovf_u4_i8 ["l"] |
| 0B | W | icall_conv_ovf_u4_u8 ["q"] |
| 02 | W | icall_conv_bool_i4 |
| 03 | W | icall_convert_to_int32_char (zero-extend, never throws; pinned ToInt32(char) family) |
| 0C,0D | N | rounding converter |
| 0E | N | throwing parser (no uint parser exists) |

### ToUInt64 (MethodMap.ConvertExtra.cs)
| ps | status | target |
|----|--------|--------|
| 04,06,08 | W | icall_conv_ovf_u8_i4 ["i"]→"l" (negative throws) |
| 05,07,09,02,03 | W | icall_conv_u8_i4 ["i"]→"l" (zero-extension can never overflow for non-negative domains — unchecked helper is EXACT) |
| 0A | W | icall_conv_ovf_u8_i8 ["l"]→"l" |
| 0B | W | icall_conv_i8_u8 ["q"]→"l" (identity) |
| 0C,0D | N | rounding converter |
| 0E | N | throwing parser (no ulong parser exists) |

### ToSingle (MethodMap.ConvertExtra.cs)
| ps | status | target |
|----|--------|--------|
| 0C,0D | W | icall_conv_r4_d →"d" (RNE narrowing, never throws, overflow→±Inf = net48; float source = identity on its own slot) |
| 04..08 | W | icall_conv_r4_i4 ["i"]→"d" |
| 0A | W | icall_conv_r4_i8 ["l"]→"d" |
| 0B | W | icall_conv_r4_u8 ["q"]→"d" |
| 09 | N | _i4-input form (uint32_t param + no unsigned value-cast code) |
| 02,03 | R | net48 InvalidCastException ALWAYS |
| 0E | N | throwing float parser |
| dec, obj, prov | N | |

### ToDecimal (MethodMap.ConvertExtra.cs)
ALL overloads remain LOUD-rejected: no decimal-target runtime icalls exist.
Candidate paths noted for the runtime owner: int-family sources could go through
`icall_decimal_ctor_int32/uint32/int64/uint64` but need the dec-result-slot
protocol (decimal-domain owner); float/double/string need NEW rounding-aware
converters (`icall_decimal_round` ToEven composition is NOT exposed as one icall).

### Base64 variants (MethodMap.Base64.cs — unchanged)
| member | status |
|--------|--------|
| ToBase64String(byte[]) | P — wired to il2cpp_convert_to_base64_string |
| FromBase64String(string) | P — wired to il2cpp_convert_from_base64_string |
| ToBase64String(byte[],int,int,options) | N — offset/length/options helper |
| FromBase64CharArray / ToBase64CharArray | N — char[] helpers (currently LOUD-rejected) |

## 4. Consolidated needs-new-icall list

For `il2cpp_convert.h` (Convert-domain, ROUNDING baseline — round half-to-even,
then range-check, OverflowException; NEVER truncate):
1. `icall_convert_to_byte_d(double)`, `to_sbyte_d`, `to_int16_d`, `to_uint16_d`, `to_uint32_d`, `to_uint64_d`
2. `icall_convert_to_int64_d(double)`
3. `icall_convert_to_bool_i8(int64_t)` — nonzero→1 on the full 64 bits
4. `icall_convert_to_char_i8(int64_t)` — checked UTF-16 range on the full 64 bits
5. Zero-width-source forms: `icall_convert_to_uint32_u4(int32_t)` (identity),
   `icall_convert_to_double_u4(int32_t)` and `icall_convert_to_single_u4(int32_t)`
   (zero-extension inside the helper — mirrors icall_conv_u8_i4 style), OR an
   unsigned VALUE cast code in EmitIcallCallArgs
6. Trivial identities: `icall_convert_to_double_f(double)` (float-slot identity)

Throwing parsers (null-means-zero rule; Parse-contract, not TryParse):
7. byte/sbyte/int16/uint16/uint32/uint64/single/bool/char-from-string/decimal-from-string

Decimal-target converters (banker's rounding then check):
8. `icall_decimal_convert_to_byte/sbyte/int16/uint16/int64/uint32/uint64/single/double`
   (+ ToDecimal identity via the dec-result protocol)

Base64:
9. `il2cpp_convert_to_base64_string_offlen(bytes, offset, length, options)` (or split),
   `il2cpp_convert_from_base64_char_array(char[])`, `il2cpp_convert_to_base64_char_array(...)`

Object-dispatch overloads (ToXxx(object)) need a runtime type-switch helper —
listed as a separate design item, not single icalls.

## 5. Cross-file impacts (NOT edited — ownership constraint)

- `tests/unit/MethodMapTests.Base64.cs`
  - `Resolve_Convert_ToByte_ThrowsLoud` (~line 106): pins ToByte(int) → NotSupportedException containing "no runtime icall". NOW WIRES to `icall_conv_ovf_u1_i8` — assertion will fail.
  - `Resolve_Convert_NarrowingFamily_ThrowsLoud` (~lines 118-132): theory over ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle/ToDecimal with an int arg. SIX of seven now WIRE (only ToDecimal still throws) — assertions will fail.
  - Both tests encode the superseded premise ("no runtime icall on the M5 surface"), which expired when il2cpp_conv.h gained the checked narrow family. Suggested update for the test owner: assert successful resolution to the `icall_conv_ovf_*` names for the int-source cases; keep ToDecimal in the throws-theory.
- `src/il2cppc/Emit/MethodMap.BitConverter.cs` lines 52-57: comment-only staleness ("no runtime icall" rationale); behavior unaffected (its System.Convert owner-guard stays correct). Left untouched.

## 6. Verification (honest boundaries)

- NO builds run (task constraint). Both rewritten files were re-read in full after writing and hand-checked for syntax (switch-arm commas, or-patterns, interpolation holes); one self-caught syntax slip (nested case labels in ToBoolean) and one semantic bug (ToInt64(ulong) routed through the unchecked reinterpret instead of `icall_conv_ovf_i8_u8`) were fixed during the audit pass.
- Every wired icall name was verified present in its header this session (section 1 citations).
- Every cast code / resultKind used has an in-repo precedent (section 1).
- Dispatch precedence re-verified post-edit: Convert → BitConverter(guarded) → Base64 → ConvertExtra; unknown member names still return null (generated-name fallback preserved, matches Resolve_Convert_UnknownMember_FallsBackToGeneratedName).
- Residual risk, disclosed: (a) compile verification pending a build by the build owner; (b) the four narrow-target char overloads stay rejected until a net48 e2e pin exists (deliberate — never guess semantics); (c) net48 ICE classifications (bool/char→float/double, char/float/double/decimal→bool, bool→char) follow documented .NET behavior but are not e2e-pinned in this repo.
