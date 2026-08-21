# GAPS.md — consoleio unwired surface (2026-08-06)

The case probes ONLY the wired surface (all 31 lines PASS vs net48, mingw).
Everything below was verified against the current transpiler + runtime and is
deliberately NOT called from `consoleio.cs` — each entry aborts the transpile
(LOUD NotSupportedException), emits an undefined extern, or has no
deterministic harness input path.

## D-CON-1 — String.Format(string) single-arg overload → Array.Empty<object>() LOUD

- **Phenomenon**: `Console.WriteLine(String.Format("no args"))` aborts the
  transpile with
  `System.Array::Empty: Array.Empty<T> generic argument 0x1C not wired in M5`
  (MethodRefResolver.cs:426, StackFold.cs:1272).
- **Causal chain**: Roslyn lowers the net48 `String.Format(string)` call to
  `String.Format(string, Array.Empty<object>())` (the params overload with a
  zero-length `object[]`). The transpiler's `Array.Empty<T>` wiring
  (`MethodMap.Array.cs` Empty case) covers only `0x03` (char) / `0x08`
  (int32) / `0x0E` (string); `0x1C` (object) falls to the LOUD `_` arm.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Array.cs` (Empty
  switch) + a runtime zero-length `object[]` factory
  (`icall_array_empty_object`). Minimal repro verified:
  `Console.WriteLine(String.Format("no args"))` alone.
- **Workaround in case**: the case uses `String.Format("{0}", ...)` (2/3/4-arg
  overloads → `icall_string_format_1/2/3`, exercised via locals so the
  composite kernel is NOT constant-folded).

## D-CON-2 — Console.Error / error stream not wired (undefined extern)

- **Phenomenon**: `Console.Error.WriteLine("errstream")` transpiles WITHOUT a
  LOUD error, but `get_Error` resolves to a GENERATED-NAME extern
  `System_Console_get_Error_<hash>()` that no runtime header declares →
  cmake build fails with an undefined symbol (verified: generated
  `Cases_P.cpp` line 29).
- **Causal chain**: `TryMapConsole` has no `get_Error` case → default
  generated-name resolution. The `WriteLine(string)` on the returned
  `TextWriter` receiver is then (mis)routed to
  `icall_stringwriter_write_line` (the StringWriter domain) — the TextWriter
  object model has no carrier.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Console.cs` (no
  get_Error/Out/In cases) + `MethodMap.StringIO.cs` (TextWriter receiver
  misclassified as StringWriter).

## D-CON-3 — Console.ReadKey / ReadLine input path

- `icall_console_read_key(int32_t intercept)` exists (console.cpp:245) and is
  mapped (MethodMap.Console.cs:110-111), but the runtime uses blocking
  `_getch()` (conio.h) — on the harness's EOF stdin there is no deterministic
  key event and the call blocks. No e2e-usable input path exists through
  run-tests.ps1 (no stdin redirection).
- `ReadLine` IS safely probeable: the harness launches both processes with
  EOF stdin, so `Console.ReadLine()` returns `null` on BOTH sides (net48 EOF
  semantics; `icall_console_read_line` returns nullptr when `std::getline`
  hits EOF). The case probes this (`read1_null=1`, `read2_null=1`).
- `Console.IsInputRedirected` is LOUD: "not wired: the runtime has no
  icall_console_is_input_redirected" (MethodMap.Console.cs:112-113). A real
  stdin-driven ReadLine probe stays parked until that icall + a harness
  stdin-redirect flag land.

## D-CON-4 — String.Format alignment specifiers "{N,width}" not supported

- `String.Format("{0,8}", x)` (printf-style padding) is REJECTED at runtime:
  the stringbuilder.cpp composite-format kernel throws
  `NotSupportedException("AppendFormat alignment/format specifiers are not
  supported in this runtime.")` (AppendFormatCore). The `{N:fmt}` format
  specifiers (D/X/C...) are likewise unsupported; only bare `{N}` positional
  substitution is wired. Positional substitution IS exercised in the case.

## D-CON-5 — Console.WriteLine(string, object) composite overloads

- `Console.WriteLine("x={0}", 5)` (the composite WriteLine overloads) is
  NOT safe: `TryMapConsole`'s WriteLine switch dispatches on `ps[0]` only and
  returns a 1-arg mapping for the 0x0E arm regardless of IL arity → a 2-arg
  call would emit an arity-mismatched icall. The case uses
  `String.Format` + `WriteLine(string)` instead. Root:
  `src/il2cppc/Emit/MethodMap.Console.cs` WriteLine switch (no
  `string, object...` overload arms).
