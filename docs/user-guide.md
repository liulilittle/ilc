# ILC User Guide (v1)

> Audience: developers who want to compile a .NET program to native code with ILC.
> Scope: what ILC is, installation, quick start, CLI reference, supported .NET
> surface, reflection modes, testing, and known behavior differences.
>
> Version: 1 (2026-08-03). This is the first edition of the user-facing guide;
> it describes the tool as it exists today. Implementation details live in
> `docs/architecture/overview.md` (pipeline), `docs/alignment-tracker.md`
> (BCL alignment status), `docs/architecture/reflection-runtime-design.md`
> (reflection M5.5 design) and `docs/REQUIREMENTS.md` (iron rules).

---

## 1. What is ILC?

**ILC (IL Compiler)** is an ahead-of-time (AOT) compiler that translates .NET
IL (Common Intermediate Language) into readable, auditable C++ source code,
which is then compiled by a native toolchain (MinGW g++, MSVC, or WSL GCC)
into a native executable. It is the same family as .NET Native / ILCompiler.

```
C# source (net48) --csc--> assembly (PE+IL+metadata)
  --il2cppc transpiler--> generated C++ project (per-type .cpp + headers + entry.cpp + CMakeLists.txt)
  --native toolchain (-O2)--> native EXE linked against the ILC C++ runtime (src/runtime)
```

The pipeline inside the transpiler (`src/il2cppc/`, C# / .NET 8):

`MetadataLoader` (assembly + metadata decode) → `MethodBodyReader` +
`IlDecoder` (IL instruction decode) → `StackFold` (stack machine → expression
tree folding) → `CodeGenerator` (types, methods, vtables, descriptors,
entry point emission) → generated C++ project.

### Naming (iron rule 2m)

**This project is called ILC, not "il2cpp".** "il2cpp" is only used as an
informal metaphor (both are IL→C++). The current directory and the
transpiler executable still carry the historical working name `il2cppc`
(`src/il2cppc/`, assembly name `il2cppc`); renaming is planned as a separate
engineering task. In all documentation and new code the formal name is
**ILC**. Do not describe ILC as "the il2cpp compiler" — it is a standalone
IL compiler.

### Design principles (summary, full text in `docs/REQUIREMENTS.md`)

- **AOT static translation, never an interpreter** (iron rule 1): every IL
  sequence becomes equivalent static C++ statements; the generated code is
  the only execution path. Example: `ldarg.0; ldarg.1; add; stloc.0` →
  `loc0 = arg0 + arg1;`.
- **Performance ≥ official .NET JIT** (iron rule 2e): expression folding at
  compile time, no per-call metadata lookups, no unnecessary temporaries;
  the C++ optimizer is free to inline and constant-propagate.
- **Deterministic, reproducible output** (iron rule 1): same input assembly
  → same C++ project every run.
- **JIT reservation** (iron rule 2j): call sites go through replaceable
  function-pointer slots (vtables), type descriptors are extensible, all
  managed allocations go through the runtime allocator, so a future JIT can
  coexist. Today only the AOT path exists.

---

## 2. Quick start

### 2.1 Requirements (install)

| Tool | Required | Purpose | Notes |
|---|---|---|---|
| .NET SDK ≥ 8.0 | Yes | Builds and runs the transpiler (`src/il2cppc`, targets `net8.0`) | `dotnet --version` to check |
| .NET Framework 4.8 targeting pack | Yes (dev) | Compiles the *input* C# programs (net48) | `Microsoft.NETFramework.ReferenceAssemblies.net48` package is referenced by the transpiler and by test fixtures; `dotnet build` restores it automatically |
| CMake ≥ 3.10 | Yes | Configures the generated C++ project | `cmake --version` |
| MinGW-w64 (g++ + make) | Yes | Default compiler for the generated C++ | `g++ --version`; `mingw32-make` or `make` on PATH |
| Visual Studio 2022 (MSVC) | Optional | Second verification compiler | run-tests uses `-G "Visual Studio 17 2022"` |
| WSL with Ubuntu 1804 (GCC 7.5) | Optional | Third verification compiler (Linux baseline) | `wsl -d Ubuntu1804` |

The transpiler itself is a plain .NET console app — building it:

```powershell
dotnet build src/il2cppc/il2cppc.csproj        # or: pwsh scripts/build-transpiler.ps1
```

### 2.2 End-to-end example

Step 1 — compile your C# program to a .NET Framework 4.8 assembly (ILC
currently consumes net48 PE assemblies; the loader does not care whether
the file is an .exe or a .dll):

```powershell
dotnet build MyApp/MyApp.csproj -c Release -o MyApp/out
# produces MyApp/out/MyApp.exe (net48)
```

Step 2 — transpile IL → C++:

```powershell
dotnet run --project src/il2cppc -- MyApp/out/MyApp.exe gen
# "generated N type(s) -> gen"
```

`gen/` now contains a complete CMake project:

```
gen/
  CMakeLists.txt          # adds src/runtime; emits three targets (see 2.3)
  include/internal/       # generated descriptors, vtable declarations
  src/                    # one .cpp per type + entry.cpp + host.cpp
```

The generated `CMakeLists.txt` references the runtime by relative path
(`add_subdirectory(../../src/runtime ...)` style, depth-adjusted for the
output location), so keeping the output dir inside the repository layout
(2 levels below the root) needs no edits; otherwise patch that line to an
absolute path.

Step 3 — compile the generated C++ with MinGW:

```powershell
cmake -S gen -B gen/build -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build gen/build
```

> `-DCMAKE_BUILD_TYPE=Release` matters: it enables `-O2`, which is what
> performance-critical shapes (e.g. tail-recursive loops) rely on — see
> section 7, FAQ. (MSVC builds use `--config Release` instead.)

Step 4 — run the native executable:

```powershell
./gen/build/il2cpp_gen_exe.exe
```

The output must be byte-identical to running `MyApp.exe` on .NET Framework
4.8 — that is the project's definition of correctness.

### 2.3 One-command build (P-F friendly compiler)

Steps 1–4 above are the fine-grained manual flow. For a single command that
builds the .NET program, transpiles it, drives CMake *and* stages the
artifacts, use `scripts/build-app.ps1` (Windows) or `scripts/build-app.sh`
(POSIX/WSL):

```powershell
# native executable (default)
pwsh scripts/build-app.ps1 -Input MyApp/MyApp.csproj -Kind exe -Toolchain mingw
# distributable static library package: lib<name>.a + libil2cpp_runtime.a + include/
pwsh scripts/build-app.ps1 -Input MyApp/MyApp.csproj -Kind static -Toolchain mingw
# loadable C-API library (.dll / .so), consumed via dlopen/LoadLibrary
pwsh scripts/build-app.ps1 -Input MyApp/MyApp.csproj -Kind shared -Toolchain mingw
```

`-Kind` selects the CMake `--target`: `il2cpp_gen_exe` (exe), `il2cpp_gen`
(static lib), or `il2cpp_gen_shared` (shared lib) — the generated project
emits all three targets sharing the translated sources. `-Toolchain` picks the
generator/toolchain: `mingw` (MinGW-w64), `msvc` (Visual Studio 17 2022),
`wsl` (Linux ELF via WSL, Ubuntu 1804 by default), or `ndk` (Android arm64-v8a
via NDK). Toolchains are discovered from the environment (`IL2CPP_MINGW_ROOT`,
`IL2CPP_MSVC_GENERATOR`, `IL2CPP_WSL_DISTRO`, `IL2CPP_NDK_ROOT`) — no machine
paths are hardcoded. `-LibName <name>` overrides the output library name
(`lib<name>.a`/`<name>.dll`/`lib<name>.so`); `-Clean` resets the per-app
`gen_app/` workspace; `-OutDir <dir>` overrides the artifact directory;
`-Log <file>` forwards `--log <file>` to the transpiler (tee the transpile
stdout+stderr into a log file).

**SHARED form note**: `il2cpp_gen_shared` is a loadable C-API library — the
host must `dlopen`/`LoadLibrary` it and call `il2cpp_entry_main`. Never
co-link it with the static runtime (`il2cpp_runtime`) in the same process:
that would bring up two independent runtime instances (each owns the GC heap
and string pool).

---

## 3. CLI reference

The transpiler entry point is `Program.Main` (`src/il2cppc/Program.cs`),
argument parsing in `src/il2cppc/CliOptions.cs`.

```
usage: il2cppc [--reflection-list <manifest>] [--reflection-granularity all|members] [--log <file>] [--quiet] [--verbose] <input-assembly> <output-dir>
usage: il2cppc audit-internalcalls <assembly>
```

`--help`/`-h` prints the matching usage to stdout and exits 0; `--version`
prints the transpiler version and exits 0. The `help` subcommand
(`il2cppc help`, `il2cppc help audit-internalcalls`) and
`il2cppc audit-internalcalls --help` do the same. Help/version are pre-scanned
across all arguments before parsing, so `--help` never falls through to a
"bad usage" exit 1.

### 3.1 Parameters

| Parameter | Kind | Default | Description |
|---|---|---|---|
| `<input-assembly>` | positional (required) | — | Path to the .NET Framework 4.8 assembly (PE file, `.exe` or `.dll`) to transpile. |
| `<output-dir>` | positional (required) | — | Directory for the generated C++ project. Created by the tool. |
| `--reflection-list <manifest>` | option | off | Mode B reflection manifest: a text file, one full type name per line (see section 5.2). When provided, the tool also writes `reflection-manifest.txt` into the output dir. |
| `--reflection-granularity all\|members` | option | `members` | Reflection detail granularity for the manifest. `members` = types plus their members; `all` = full detail. Values are case-insensitive. |
| `--log <file>` | option | off | Tee a copy of both stdout and stderr into `<file>` for the whole run. An unopenable path is reported after parse and exits 1. |
| `--quiet` | option | off | Suppress the stdout diagnostics (symbols / pool reports / final summary); stderr errors and warnings are unaffected. |
| `--verbose` | option | off | Print per-type progress lines to stdout while generating. Mutually exclusive with `--quiet` (exit 1). |
| `--help`, `-h` | option | off | Print usage to stdout and exit 0. |
| `--version` | option | off | Print the transpiler version to stdout and exit 0. |

Options may appear before, between, or after the positionals; anything not
matching an option is treated as a positional.

### 3.2 Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. `generated N type(s) -> <dir>` printed to stdout. Also `--help`/`-h`/`--version`, the `help` subcommand, and a successful `audit-internalcalls`. |
| 1 | Command-line parse error (bad usage, missing manifest file, invalid granularity, `--quiet`+`--verbose` together, `audit-internalcalls` without `<assembly>`, unopenable `--log` path). Message on stderr: `il2cppc error: ...`. |
| 2 | Transpile failure (metadata load, decode, code generation, loud rejection of unsupported constructs). Message + stack trace on stderr. Also an `audit-internalcalls` run whose assembly cannot be read. |

### 3.3 Errors

The transpiler treats unsupported constructs on two tracks:

- **Hard (exit 2):** IL decode failures and emission-stage loud rejections
  (`NotSupportedException` with the IL offset/opcode) abort the transpile,
  name the offending `type::method` in the error line, and print the original
  throw-point stack trace. There is no silent fallback for these.
- **Soft (warning + stub):** per-method body-read / fold failures and
  `Guid`-returning methods are downgraded to a stderr warning and skipped (a
  non-fatal stub) instead of failing the whole transpile.

The manifest file must exist at parse time; `--reflection-list` without a
path, or a bad granularity value, is a parse error (exit 1).

---

## 4. Supported .NET surface

ILC today is a closed-world AOT compiler: it transpiles the *input assembly*
(plus the runtime support linked in from `src/runtime`). The .NET surface is
implemented through a BCL wiring layer (`MethodRefResolver` → per-domain
`MethodMap.*.cs`) mapping method calls to C++ runtime functions (`icall_*` /
`il2cpp_*` in `src/runtime/include/internal/*.h`).

### 4.1 Domain coverage

The authoritative, continuously updated status table is
**`docs/alignment-tracker.md`** (domain × header × icall count × wired ×
status). Summary at snapshot 2026-08-03 (64 headers, 313 icall declarations,
236 unique references wired in the transpiler):

**Complete:** System.Math (+ Math Ext) · System.Convert · System.DateTime /
TimeSpan · System.Text.StringBuilder · System.Environment ·
System.Collections.Hashtable · value-type boxing/unboxing /
value semantics.

**Partial (implemented subset wired):** System.Console (except
`IsInputRedirected`, explicitly rejected) · System.String (concat/join
partially wired) · System.Array (sort/search partially wired) · System.Random
(~half) · System.IO File/Directory/Path (file delete + stream subset;
`Directory.Delete` rejected) · System.GC (gc_api wired; weak references
rejected) · System.Delegate (partial) · System.Type/reflection (narrow
surface) · System.Exception (type-tag carrier; catch variable / `get_Message`
rejected) · P/Invoke `DllImport` (MarshalUtf8 path).

**Implemented but not yet wired (runtime functions exist, resolver mapping
missing):** BitConverter (24 helpers) · IO Stream/BinaryReader/BinaryWriter
(36) · Stack/Queue (14) · Monitor (5) · Interlocked (12) · CultureInfo (6) ·
Version/Uri (7) · IntPtr (6) · Stopwatch (2) · ConvertBase64 (2) · Buffer (2) ·
Encoding (skeleton) · Guid · Parse/TryParse (6) · conv instruction family
(partial) · numberformat (new).

The authoritative per-gap work order is `docs/audit/icall-gap-list.md`
(174 forward gaps at snapshot; 0 reverse gaps).

### 4.2 What happens when something is unsupported

Unsupported members fail **loudly at transpile time**, never silently:
`MethodRefResolver` raises `NotSupportedException` (wrapped with a clear
message naming the member) and the transpiler exits with code 2. Examples
(the full list evolves with `alignment-tracker.md` §3):

- `System.WeakReference` (needs weak-reference object model + finalizer chain)
- `Thread.CurrentThread` / `ManagedThreadId` / `Priority` (no thread object model yet)
- `Console.IsInputRedirected` (needs redirection state)
- `Directory.Delete` (only file delete implemented)
- `Exception.get_Message` / catch variables (M3 exception carrier is a C++ type tag)

The design intent (iron rule 2m): **unwired icall = program cannot run, so
every call the code makes must be implemented** — coverage is a correctness
prerequisite, and loud rejection is the safety net that makes gaps visible
at build time instead of at runtime.

---

## 5. Reflection: the three modes (A / B / C)

Reflection metadata is hard to trim (iron rule 2m), so ILC compiles in
three modes. One runtime, three compile strategies: the modes only change
which types get reflection metadata generated; the runtime reflection API
is identical.

### 5.1 The modes

| Mode | Name | Behavior | Cost |
|---|---|---|---|
| **A** | Full compile | Generate reflection metadata for **all** types in the assembly (with per-member granularity, iron rule 2m §5). | Slowest compile, largest tables. No configuration needed. |
| **B** | User manifest | User lists the types whose reflection metadata must be kept (one full type name per line in a manifest file). Only those types get metadata. | Smallest output; user maintains the list. `Type.GetType(name)` for an unlisted type returns `null` (.NET semantics). |
| **C** | Static analysis (default direction) | The transpiler scans IL for statically inferable reflection: `typeof(T)` / `ldtoken T` + `GetTypeFromHandle`, `Type.GetType("...")` string constants, `nameof` (already compile-time constants). Hit types are kept automatically. | Volume ≈ actual reflection surface; string-driven `GetType` that cannot be analyzed is flagged at compile time with advice to switch to mode B. |

Modes are merged conservatively: mode C analysis runs by default and mode B's
manifest is a union with it; mode A is the full set.

**Implementation status:** of the three modes, only mode B's CLI contract
(`--reflection-list` capture + manifest echo) is implemented today (see 5.2).
The runtime reflection tables and the mode C IL pre-scan / mode A full compile
land with the M5.5 milestone (see 5.3) — until then the modes describe the
designed behavior, and mode B is the only one you can invoke from the CLI.

### 5.2 Mode B: `--reflection-list` manifest syntax (implemented today)

The CLI already implements mode B capture (`CliOptions.cs`, unit-tested in
`tests/unit/CliTests.cs`):

- Manifest = plain text file, **one full type name per line**.
- Blank lines and lines starting with `#` are ignored (comments).
- Leading/trailing whitespace on a line is trimmed.
- Type names are assembly metadata full names, e.g. `Calc.Program`,
  `System.String`, `Calc.Math`.

Example `reflection.txt`:

```text
# Types needed for reflection (mode B)
Calc.Program
Calc.Math
System.String
```

Invocation:

```powershell
dotnet run --project src/il2cppc -- `
  --reflection-list reflection.txt `
  --reflection-granularity members `
  MyApp/out/MyApp.exe gen
```

When `--reflection-list` is given, the transpiler additionally writes the
resolved manifest back as `gen/reflection-manifest.txt` (with the granularity
recorded as a comment header) — the documented contract for downstream
consumers of the manifest.

`--reflection-granularity` accepts `all` or `members` (case-insensitive,
default `members`).

### 5.3 Status note (what exists vs. what is designed)

- **Implemented:** CLI parse of mode B manifest + granularity; manifest
  echo to the output dir (`reflection-manifest.txt`); test coverage in
  `tests/unit/CliTests.cs`.
- **Designed, not yet implemented (M5.5):** the runtime reflection
  surface — static per-type `TypeInfo` tables (methods/fields/properties),
  unified invoker wrappers mapping reflection calls to AOT functions,
  `typeof` folding to `&{Type}_class`, and the mode C IL pre-scan pass.
  Full design: `docs/architecture/reflection-runtime-design.md` (§2 tables,
  §3 invoker ABI, §4 mode differences).

So today, passing `--reflection-list` records the mode-B contract into the
generated project; the runtime reflection APIs (GetType/GetMethod/Invoke/…)
land with the M5.5 milestone per that design.

---

## 6. Running the tests

The project verifies itself against .NET Framework 4.8 by double-running:
each case is executed on net48 (reference) and as transpiled native code,
and the stdout is diffed byte-for-byte.

### 6.1 e2e suite — `scripts/run-tests.ps1`

```powershell
pwsh -NoProfile -File scripts/run-tests.ps1 -Compiler mingw   # default
pwsh -NoProfile -File scripts/run-tests.ps1 -Compiler msvc
pwsh -NoProfile -File scripts/run-tests.ps1 -Compiler wsl
```

What it does for every directory under `tests/cases/<name>/` (each is one
case = `<name>.cs` + `<name>.csproj`):

1. Build the case as a net48 exe (`dotnet build -c Release`).
2. If the case has a `mylib/` native library (P/Invoke cases), build
   `mylib.dll` with g++ for the net48 reference run.
3. **Reference run**: execute the net48 exe, capture stdout.
4. **Transpile**: `dotnet run --project src/il2cppc -- <exe> <gen>`.
5. **Build the generated C++** with the chosen compiler (MinGW Makefiles /
   VS 2022 / WSL Ubuntu 1804 GCC 7.5, Release).
6. **Run** `il2cpp_gen_exe` and diff stdout against the reference output.

Exit code 0 = all cases passed; 1 = any case failed (failures are printed
with expected vs. actual). Note: a transpile failure aborts the whole run
(throw), so fix the first failing case before rerunning.

### 6.2 Other suites

| Suite | Command | Contents |
|---|---|---|
| Unit tests | `dotnet test tests/unit/unit.csproj` | Transpiler internals: CLI parsing, IL decode, stack folding, code generation, metadata loading (407 tests, snapshot 2026-08-03; the suite grows as domains get wired). |
| Runtime C++ tests | `cmake -S tests/runtime -B build/tests_runtime -G "MinGW Makefiles"` + `cmake --build build/tests_runtime` | C++ unit tests for the runtime (`test_object`, `test_gc`, …). |
| C API | `pwsh scripts/run-capi.ps1 -Compiler mingw\|msvc\|wsl` | C API surface tests (`tests/capi`). |
| Full CI | `pwsh -NoProfile -File scripts/ci.ps1` | Transpiler build → fixtures → unit → runtime C++ → e2e (mingw, msvc, wsl) → C API (all three). Must be green after every code change. |

---

## 7. Frequently asked questions

### 7.1 Deep recursion / stack depth

Deep recursion is real machine recursion on the native stack (the
transpiler emits real C++ call frames — no trampolines). Verified behavior
(`docs/audit/recursion-audit.md`, tests/cases/recurse):

- **Tail-recursive shapes are handled by the C++ optimizer.** The generated
  code for `return F(...)` is the plain shape `t0 = F(...); return t0;`, which
  clang/g++/MSVC turn into a jump loop at `-O2` — `TailSum` runs to
  **1,000,000** deep at -O2, matching the net48 JIT's tail-call behavior.
  Without `-O2` (no `CMAKE_BUILD_TYPE=Release`), the same recursion
  stack-overflows around 16k–18k frames (60–65 B/frame; allocation-heavy
  frames with GC frame records ~190–200 B/frame → ~5k frames).
- The default `recurse` case (10,000 / 4,000 / 20,000) passes only under
  Release. **Always build with `-DCMAKE_BUILD_TYPE=Release`**.
- Non-tail recursion depth ≈ 16k frames at -O0, more at -O2; the runtime
  records one managed frame per call (GC safe point = stack root scan), so
  deep recursion + GC both consume stack — DeepAlloc tops out earlier
  (~5k–8k frames).

### 7.2 Chinese / non-ASCII strings

Strings are UTF-16 managed objects; the native boundary converts with
full UTF-8 decoding (`src/runtime/src/string.cpp`, `il2cpp_string_new_from_utf8`)
aligned with the .NET Framework `UTF8Encoding` **replacement-fallback
semantics** (invalid sequences become U+FFFD, exactly like .NET). String
literals in generated C++ are emitted as UTF-8; console output goes through
the runtime console icalls. See `tests/runtime/test_string_utf8.cpp` for
coverage.

### 7.3 Exception semantics differences vs. C#

The runtime provides managed exceptions (`DotNetException` + type-tag
matching, `try/catch/finally`), but the M3 exception handling has known
behavior gaps relative to .NET — documented in `docs/audit/` and
`.opencode/discover.md`:

- **Nested catch regions are not yet supported** (M3 scope: NON-NESTED
  regions). An inner catch's handler code may be emitted as dead code inside
  the try — the outer catch wins. Verify exception flows with a case before
  relying on them.
- **A bare `throw;` (rethrow) skips sibling catch handlers in C++** — the
  generated serial-catch dispatch (`catch(DotNetException&) { if(!matches)
  throw; }`) does not resume scanning later catch clauses of the same try.
  Multi-catch (sibling) chains can therefore dispatch to the outer handler.
- **`catch { }` (catch-all)** is currently parsed as type `System.Object`,
  which never matches the exception chain → a rethrow crashes. Avoid
  catch-all handlers in cases; use typed catches.
- **`Exception.Message` / catch variables** are not implemented (rejected
  loudly at transpile time).
- Rethrowing preserves the original exception type and is verified working
  for the supported shapes (single catch + finally, sibling chains with one
  match).

If your case hits one of these, the transpiler either rejects it loudly
(better: visible at build time) or produces known-wrong control flow —
check `alignment-tracker.md` §3 / `icall-gap-list.md` for the current gaps
before debugging.

### 7.4 What toolchain should I use?

MinGW is the default and fastest to set up. MSVC and WSL GCC 7.5 are used
as verification compilers — the quality gate is **zero warnings on all
three** (`-Wall -Wextra` / `/W4`). A case that passes only on one compiler
is a bug, not a workaround.

### 7.5 I want to use reflection — where do I start?

Add `--reflection-list` with your reflection-facing types (section 5.2) so
the manifest contract is recorded. The runtime reflection APIs arrive with
M5.5 (`docs/architecture/reflection-runtime-design.md`); until then, `typeof`
and direct member access are the reliable paths — string-driven `Type.GetType`
calls on unlisted types will be analyzed (mode C) or warned about.

---

## 8. Where to go next

| Topic | Document |
|---|---|
| Project requirements / iron rules | `docs/REQUIREMENTS.md` |
| Architecture overview + pipeline | `docs/architecture/overview.md` |
| BCL alignment status (authoritative table) | `docs/alignment-tracker.md` |
| Wiring gap work order | `docs/audit/icall-gap-list.md` |
| Reflection design (M5.5) | `docs/architecture/reflection-runtime-design.md` |
| Correctness debt (C1–C4, H1–H9) | `docs/audit/audit-summary.md` |
| Performance roadmap / property folding | `docs/architecture/性能路线图.md`, `docs/architecture/属性折叠设计.md` |
| Test cases | `tests/cases/README.md`, `tests/bench/README.md` |
