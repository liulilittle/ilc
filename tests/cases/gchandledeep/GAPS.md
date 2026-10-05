# GAPS.md -- gchandledeep recorded findings (e2e batch C53, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 37-line byte-exact).

## D-GH-1 -- GCHandle value-COPY aliasing diverges (net48 struct-field vs ILC slot)

- **Phenomenon (probed on net48)**: `GCHandle a = Alloc(o); GCHandle b = a;
  b.Free();` then `a.IsAllocated` is **True** on net48 (IsAllocated reflects
  the struct field `m_handle != 0`), and `a.Free()` afterwards is a silent
  no-op. The ILC runtime tracks SLOT liveness: `a.IsAllocated` is False once
  the shared slot is freed, and `a.Free()` throws InvalidOperationException.
  Same-variable double Free throws on BOTH runtimes.
- **Disposition**: the case avoids value-copy probes entirely and stays on the
  subset both sides agree on (same-variable Free/IsAllocated sequences).
- **Suspect**: the ILC runtime is a safe superset of net48's undefined-ish
  copy semantics; recorded so maintainers know the e2e cannot guard copies.

## D-GH-2 -- M3 user-method VALUE-TYPE returns unsupported ("return type 17")

- **Phenomenon**: a user helper `static GCHandle WeakToDead()` throws
  `NotSupportedException: return type 17 is not supported by M3`
  (SignatureTypeCode.ValueType). The transpiler's ReturnTypeCpp has no arm for
  a user method returning a value-type struct.
- **Disposition**: the dead-target helpers are void and escape the handle
  through a static GCHandle field (the int32 carrier).

## D-GH-3 -- Pinned target requirements differ (net48 blittable-only)

- **Phenomenon**: `GCHandle.Alloc(new object(), GCHandleType.Pinned)` throws
  ArgumentException on net48 ("Object contains non-primitive or non-blittable
  data") while the ILC runtime pins any object.
- **Disposition**: the pinned probes use string literals (pinnable on both).

## D-GH-4 -- GCHandle.ToIntPtr / FromIntPtr LOUD-unwired

- The runtime has no `icall_gchandle_to_intptr` / `from_intptr` (gchandle-b23
  report 4.4 lists them as conditional additions); the MethodMap throws
  NotSupportedException. Not probed in the e2e.

## D-GH-5 -- same-frame weak death: both runtimes root locals across allocations

- **Phenomenon**: a helper that allocates a weak handle to a local, nulls the
  local, and collects INSIDE the same frame does NOT reclaim the target on the
  ILC runtime (and net48 is unreliable there too -- the weakrefcase JIT-liveness
  precedent). The transpiler's root pass also INLINES the helper unless the
  body writes a static field, which would hoist the dying local into Main.
- **Disposition**: the collect is always issued in the CALLER after the
  helper frame is gone; the helper keeps a static-field write (`s_guard`)
  so the inline pass (purity gate) leaves it as a real function.
