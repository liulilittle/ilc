# ut_sb_buffer -- StringBuilder ReplaceBufferInternal / ReplaceBufferAnsiInternal unit tests

**Result: PASS** -- `all sb_buffer tests passed`, exit code 0.

## Scope

New file `tests/runtime/test_sb_buffer.cpp` (auto-discovered by the
`file(GLOB TEST_SOURCES CONFIGURE_DEPENDS test_*.cpp)` rule in
`tests/runtime/CMakeLists.txt:31` -- **no CMakeLists entry needed**).
Build dir: `E:\il2cpp\build\ut_sb_buffer` (isolated; no other target touched).

Targets under test (`src/runtime/include/internal/il2cpp_stringbuilder.h:321,324`):

- `icall_stringbuilder_replace_buffer_internal(Il2CppObject* self, const char16_t* buffer)`
- `icall_stringbuilder_replace_buffer_ansi_internal(Il2CppObject* self, const int8_t* buffer)`
- The `capacity` struct field read path: generated code reads
  `sb.Capacity` as a direct field load
  `((Il2CppStringBuilder*)obj)->capacity` (the `"fcap"` collapse,
  `CodeGenerator.cs EmitIcallCallArgs:6184`; mapped at
  `MethodMap.StringBuilder.cs:85-87`). There is NO C++ function named
  `il2cpp_stringbuilder_capacity` -- the testable surface is the field
  itself, asserted directly after every operation.

## Requirements coverage (5/5 verified)

| # | Requirement | Evidence |
|---|-------------|----------|
| 1 | Null buffer clears | `replace_buffer_internal(null)` / `_ansi_internal(null)`: same-instance return, length -> 0, capacity UNCHANGED, ToString `""`; also a no-op on an already-empty builder. Impl: `stringbuilder.cpp:1307-1311,1332-1336`. |
| 2 | Short buffer fits without grow | cap-16 builder + `"xyz"`/`"ab"`/ANSI `"AB"`: capacity stays 16, `chars->length` stays 16, shorter buffer TRUNCATES (no residue). Exact-fit boundary `len == capacity` does NOT grow (strict `len > capacity` check, `stringbuilder.cpp:1315`). |
| 3 | Long buffer grows via GrowBuffer | Both GrowBuffer branches exercised through the entry point: required-wins cap 4 + 10 units -> **10**; doubling-wins cap 16 + 20 units -> **32** (`max(required, oldCapacity * 2)`, `stringbuilder.cpp:169-190`). Fresh array sized exactly to the new capacity; content fully preserved; post-growth fitting replace keeps the grown capacity. ANSI grow: cap 4 + 10 bytes -> 10. |
| 4 | ANSI widen semantics (0x80 -> U+0080) | Bytes `{ 'A', 0x80, 0xFF }` widen to units U+0041 / **U+0080** / **U+00FF** via zero-extension (`static_cast<uint8_t>`, `stringbuilder.cpp:1347`); sign extension would give 0xFF80/0xFFFF and fails the assertion. ASCII passes through unchanged. |
| 5 | capacity >= length invariant after ops | `ExpectInvariant` after EVERY mutation: `capacity >= length && chars->length == capacity`; plus an end-of-run sweep over all six builders. |

Plus error paths: null receiver -> `NullReferenceException` on both entry
points (mirrors sibling section 14).

## Build + run evidence

```
cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_sb_buffer -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build E:\il2cpp\build\ut_sb_buffer --target test_sb_buffer
E:\il2cpp\build\ut_sb_buffer\test_sb_buffer.exe
```

- Configure: "Build files have been written to: E:/il2cpp/build/ut_sb_buffer"
- Compile: zero errors, zero warnings under `-Wall -Wextra` (forced-rebuild
  warning scan emitted nothing)
- Run: `all sb_buffer tests passed`, EXIT=0
- Assertions: 50 top-level assertion sites across 7 sections
  (harness mirrors `test_stringbuilder.cpp`: `Expect`/`ExpectString` +
  `DotNetException` type-chain `ExpectThrows`, non-zero exit on any failure)

## Notes

- Two compile fixes during development (test-side only, no runtime changes):
  1. `const char*` literals need `reinterpret_cast<const int8_t*>` for the
     ANSI entry point (signed char is a distinct type).
  2. Added missing `using il2cpp::runtime::icall_stringbuilder_to_string;`.
- No git operations; only files touched:
  `tests/runtime/test_sb_buffer.cpp` (new) and this report.
