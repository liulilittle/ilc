# GAPS.md -- pinvokedeep2 recorded findings (e2e batch C59, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 15-line byte-exact).

No NEW runtime/transpiler defects surfaced in this case. The callback
bridge (GetFunctionPointerForDelegate -> icall_delegate_get_fn_ptr), the
multi-invocation native callers, and the native/managed struct-layout
cross-check (sizeof/offsetof == Marshal.SizeOf/OffsetOf for the registered
Point/Rectangle family) all match the net48 oracle byte-for-byte on both
compilers.

## Inherited from prior pinvoke-domain batches (unchanged, not probed)

- delegatecb deviation: `Marshal.GetFunctionPointerForDelegate` on a
  MULTICAST delegate is LOUD-rejected by the runtime (the net48 quirk of
  returning the last handler's pointer is not reproduced) -- the case uses
  single-cast delegates only.
- PtrToStructure/StructureToPtr remain LOUD (struct-typed local/result
  temp extension, MethodMap.Marshal.cs); System.Windows.Forms.Message
  SizeOf/OffsetOf follow the Win32 MSG registry layout, not net48's Message.
