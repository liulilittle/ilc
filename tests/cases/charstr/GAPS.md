# GAPS.md — charstr unwired surface (2026-08-06)

The case probes ONLY the wired surface (34 lines PASS vs net48, mingw).
Gaps below were verified as build aborts before being excluded.

## D-CHA-1 — UNPAIRED-surrogate string literal aborts the string-pool emission

- **Phenomenon**: a literal `"\uD800"` (unpaired surrogate) in the program
  makes the transpile emit
  `warning: string pool report skipped: Unable to translate Unicode
  character \\uD800 at index 682 to specified code page.` and the generated
  `include/internal/string-pool.h` is written EMPTY (0 bytes) -- every
  generated `il2cpp::g_strPool[i]` reference then fails to compile ("no
  member named 'g_strPool' in namespace 'il2cpp'", 20 errors). Removing the
  literal makes the case build and PASS.
- **Causal chain**: the string-pool layer (StringPoolPass T3) translates each
  pooled literal through the console/UTF-8 code page when writing the header;
  an unpaired surrogate is not a valid Unicode scalar and throws, aborting the
  whole header emission. A VALID surrogate PAIR (`"\uD83D\uDE00"`) is fine and
  IS probed.
- **Suspected root file**: `src/il2cppc/Ir/StringPoolPass.cs` (or the
  string-pool emission writer) -- must not let one unencodable literal nuke
  the entire table (skip-and-warn or escape the units instead).

## D-CHA-2 — System.Char STATIC surface silently transpiles to undefined externs

- **Phenomenon**: `char.IsDigit('5')`, `char.IsLetter('a')`,
  `char.IsWhiteSpace(' ')` and `Char.ConvertToUtf32("\uD83D\uDE00", 0)` all
  TRANSPILE without a LOUD error, emitting UNDEFINED generated-name externs
  (`System_Char_IsDigit_5b57cb6d7ce8e3bb(int32_t)`,
  `System_Char_IsLetter_...`, `System_Char_IsWhiteSpace_...`,
  `System_Char_ConvertToUtf32_...`) that no runtime header declares → cmake
  build fails with undefined symbols.
- **Causal chain**: there is NO `MethodMap.Char.cs` (and no System.Char arm in
  any MethodMap file -- grep for IsDigit/IsLetter/IsWhiteSpace/ConvertToUtf32
  over src/il2cppc → 0 hits). The BCL resolver falls through to the
  generated-name emission path for these instead of LOUD-rejecting them (the
  worklist "not wired" pattern). net48 semantics to implement eventually:
  IsDigit/IsLetter/IsWhiteSpace per Unicode category, ConvertToUtf32 pair
  arithmetic.
- **Suspected root file**: a new `MethodMap.Char.cs` (or System.Char arms) +
  runtime `icall_char_is_digit/is_letter/is_white_space/convert_to_utf32`;
  also note the broader pattern: unmapped BCL statics silently become
  undefined externs rather than LOUD-failing at transpile time.
