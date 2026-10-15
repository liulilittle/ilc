# GAPS.md -- p-stacktrace-ctor-arity (GN-20 P1/D2-r regression case)

Case status: **PASS (byte-exact, mingw)**. net48 oracle vs transpiled runtime:
`ok=True`, oracle==runtime sha `06bdc7ed78894ccc43c540a4b18ea30ee07feee0fb355bea77bff1fc05e49ddd`,
diff=0, output `ctor-arity ok`.

## What it guards

D2-r audit finding: the D2 fix wired ONLY the 0-arity bare `new StackTrace()`
(path `p-stacktrace-ctor`) to the single-param stub
`icall_stacktrace_ctor(Il2CppObject*)`. The `.ctor` arm returned
`I("il2cpp::runtime::icall_stacktrace_ctor", count, ...)` with NO drop cast,
so each arity>0 ctor emitted the receiver + the explicit args against the
1-param stub -> **cmake FAIL**:

```
error: too many arguments to function 'void il2cpp::runtime::icall_stacktrace_ctor(il2cpp::Il2CppObject*)'
```

The four net48 arity>0 ctor shapes covered here (all construction-only, no
frame read = byte-exact neutral):

- `new StackTrace(bool fNeedFileInfo)`
- `new StackTrace(int skipFrames, bool fNeedFileInfo)`
- `new StackTrace(int skipFrames)`
- `new StackTrace(Exception e)` (non-null `ex` -- net48 throws
  ArgumentNullException on a null exception argument)

## Fix (D2-r, drop cast on the explicit args)

- `MethodMap.GC.cs` `.ctor` arm: `System.Diagnostics.StackTrace` now returns
  the no-op for EVERY arity as `I(..., count, false, false, true,
  dropCasts)` where `dropCasts = ["", "drop"(xcount)]` -- slot0 = receiver
  passthrough (the `il2cpp_object_new(&System_Object_class)` t0),
  slots1..count = "drop" (CodeGenerator.EmitIcallCallArgs: cast == "drop"
  skips the argument, ProviderCulture=false so nothing escapes). The
  generated call stays `icall_stacktrace_ctor(t0)` for EVERY arity; the
  explicit ctor payload (exception / skipFrames / needFileInfo / frames
  source) is meaningless without a frame model.
- 0-arity path UNCHANGED (still byte-exact sha `67505562...`).

## W7 boundary

Identical to `../p-stacktrace-ctor/GAPS.md`: construction is observationally
neutral; frame CONTENT is still the W7 live-stack enumeration. This case
proves construction of every arity does not produce a build/transpile/loud
error.