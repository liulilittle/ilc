# GAPS.md — exops2 unwired/divering surface (e2e batch C19, 2026-08-10)

Case status: **PASS 13/13 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; compiler=mingw). The active surface drives the custom exception
TYPE chain through intermediate/base catches, the bare-rethrow carrier
survival, the BCL param-name / message passthrough and the runtime-fault
message presence. No product code modified; no commit made.
(compiler=msvc is NOT verified on this machine -- no Visual Studio 2022
install; the C18 threadops2 report recorded a SHARED-RUNTIME WSL build block
D-C18-WSL-1 that also blocks msvc-adjacent CI until fixed.)

## D-EX2-1 — get_InnerException is NOT mapped in the transpiler (HEAD 3411fe8)

- **Phenomenon (recorded, probes PARKED)**: `MethodMap.Exception.cs` maps only
  `GetType` (identity on a catch-variable entry) and `get_Message` (the
  carrier read) at HEAD. `Exception.get_InnerException` has NO case -> an
  unmapped member falls to the generated-name path -> transpiles to an
  undefined extern `System_Exception_get_InnerException_<hash>` -> C++ link
  failure, killing the whole case at the cmake-build stage (zero output).
- **Why parked rather than byte-diffed**: an active InnerException probe
  hides the 12 passing probes behind a link failure. The M3 carrier
  (DotNetException { type, message }) has no inner slot; the managed
  Exception object task is the net48-complete replacement.
- **net48 oracle contract (csc-probe-verified, uncomment when mapped)**:
  ```
  var inner = new ArgumentNullException("inner");
  var outer = new ArgumentException("outer", inner);
  Console.WriteLine(outer.InnerException == inner);            // True
  Console.WriteLine(outer.InnerException.GetType().Name);      // ArgumentNullException
  Console.WriteLine(outer.Message);                            // outer (net48: custom literal)
  ```
  The second line requires get_InnerException AND GetType-on-a-real-object
  (not the catch-variable identity) to both land.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Exception.cs`
  TryMapException (add `case "get_InnerException"`); the runtime carrier
  needs an inner slot first.

## D-EX2-2 — custom-exception ctor MESSAGE is NOT forwarded to the throw carrier

- **Phenomenon (recorded, probes PARKED)**: the m7m message passthrough
  (StackFold.cs:2676-2679) captures the ctor args into `exceptionMessages`
  ONLY when `isBclException` is true (StackFold.cs:2649: type ends with
  "Exception" AND is NOT in the translated type set). A CUSTOM exception type
  (CxEx/CxEx2/CxEx3 -- present in the translated set) therefore NEVER gets
  its ctor message captured, and the throw site (StackFold.cs:855 +
  CodeGenerator.EmitThrowCall:5773) falls back to the default
  `il2cpp_throw_new(chain, "exception thrown")`. Empirically confirmed: the
  first exops2 diff showed `e1=CxEx2|exception thrown` vs net48
  `e1=CxEx2|level2`. The TYPE chain is unaffected (e1/e2/e9 print CxEx2/CxEx3
  correctly), so the defect is message-only.
- **Why parked rather than byte-diffed**: an active raw-message probe on a
  custom exception prints "exception thrown" (ILC) vs the literal (net48) ->
  permanent diff FAIL. The active probes read the message PRESENCE
  (`ex.Message != null`, True on both) so the case stays green while the
  literal passthrough is recorded.
- **net48 oracle contract (csc-probe-verified, uncomment when the gate is
  lifted)**:
  ```
  try { throw new CxEx2("level2"); }
  catch (CxEx ex) { Console.WriteLine(ex.Message); }      // level2
  try { throw new CxEx3("deep3"); }
  catch (CxEx2 ex) { Console.WriteLine(ex.Message); }     // deep3
  try { throw new CxEx("origmsg"); }
  catch (Exception ex) { Console.WriteLine(ex.Message); } // origmsg
  ```
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs:2649` -- drop the
  `!(types?.Any(...))` exclusion so translated (custom) exception types also
  capture their ctor args; ExceptionChain already handles the user-type base
  chain (CodeGenerator.cs:5728-5745).

## D-EX2-3 — net48 LOCALE: BCL factory messages are localized, ILC is English

- **Phenomenon (recorded, handled in the probe design)**: on this machine the
  net48 oracle composes localized text -- `new ArgumentNullException("pxy")`
  -> "值不能为 null。\r\n参数名: pxy"; `new ArgumentOutOfRangeException(
  "pyz","range msg")` -> "range msg\r\n参数名: pyz"; `DivideByZeroException`
  message is localized too. The ILC carriers are hardcoded English
  (exceptionext.cpp). This is the same divergence that makes the OLD excext
  case fail its diff at HEAD (excext has no expected.txt / GAPS.md and is
  FAILING the harness as of this batch). exops2 therefore NEVER prints raw
  BCL-factory messages -- only locale-invariant predicates (Contains /
  != null) and custom literals.

## Notes

- net48 oracle evidence: `out/exops2.exe` exit=0, 13 lines byte-exact (all
  ASCII, no localized text). Harness diff PASS (mingw), 1/1.
- The excext case (same surface family) is RECORDED as failing at HEAD due to
  D-EX2-3 (its ArgumentNullException.Message probe prints localized net48 vs
  English ILC); fixing the probe there is OUT of scope for this e2e batch
  (record-only mandate).
