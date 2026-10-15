# GAPS.md -- p-stacktrace-ctor (GN-20 P1/D2 regression case)

Case status: **PASS (byte-exact, mingw)**. net48 oracle vs transpiled runtime:
`ok=True`, oracle==runtime sha `67505562bbdffd654f29e04ea5c51381aae893b7e9df860d005611be33393ef0`,
diff=0, output `ctor-only ok`.

## What it guards

A BARE `new StackTrace()` with NO frame access. Regression anchor for the
D2 audit finding on commit `c3c82682`: routing `System.Diagnostics.StackTrace`
to `TryMapGC` left the bare ctor without a `.ctor` arm, so the newobj fell to
the `BclAllocExpr` fallback and emitted `&System_Diagnostics_StackTrace_class`
+ a phantom `System_Diagnostics_StackTrace__ctor_...` call -> **cmake build
FAIL** ("'System_Diagnostics_StackTrace_class' was not declared").

## Fix (D2, byte-exact construction path)

- `CodeGenerator.BclAllocExpr`: `System.Diagnostics.StackTrace` -> 
  `il2cpp_object_new(&System_Object_class)` (the `System.Exception` precedent).
- `MethodMap.GC.cs` `.ctor` arm: `System.Diagnostics.StackTrace` (any arity)
  -> `il2cpp::runtime::icall_stacktrace_ctor` (documented no-op).
- `stubs.cpp/.h`: `icall_stacktrace_ctor(Il2CppObject*)` no-op.

net48 `new StackTrace()` captures the LIVE managed stack, but an unread
object is observationally empty -> constructing + discarding it is
byte-exact (no stdout either side, code 0). Frame READS
(`get_FrameCount` / `ToString` / `GetFrames` / `StackFrame.*`) remain not-wired
(W7) and fold loudly.

## W7 boundary

The StackTrace FRAME CONTENT is still the W7-recorded live-stack enumeration
(see `../p-stacktrace/GAPS.md`): absolute PDB paths, line/col, zh-CN locale
markers, CLR-internal frames. This case proves only that CONSTRUCTION does
not silently- or loudly-fail; it says nothing about frame fidelity (which is
empty-frame by design).