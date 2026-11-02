# pinvoke-object-any -- `[MarshalAs(UnmanagedType.AsAny)] object` P/Invoke parameters

Baseline `6c95c1db`, seat impl-pinvoke-object.

## What net48 actually does (measured, not recalled)

All of the following was measured with executable probes under
`F:\il2cpp\.opencode\tmp\impl-pinvoke-object\phase0\oracle\`:

* `anyprobe.dll` (mingw) hex-dumps what the callee receives.
* `IntOracle.exe` / `intprobe.dll` receive the argument as an **integer**, so
  "the value" and "the address of a temp copy" cannot be confused.
* `CwOracle.exe` drives the REAL `user32!CreateWindowExW` and reads
  `CREATESTRUCT.lpCreateParams` from `WM_CREATE`.

Results:

| input | callee receives |
|---|---|
| `[AsAny]` + boxed struct | address of a native **temp copy**; the struct's field bytes at offset 0 |
| `[AsAny]` + boxed `IntPtr` | address of a temp whose first 8 bytes are the `IntPtr` value |
| `[AsAny]` + `null` | `NULL` |
| `[AsAny]` + `[In]` only | temp is NOT copied back (a native write is lost) |
| `[AsAny]` + `[In, Out]` | temp IS copied back into the managed box |
| NO `MarshalAs` + boxed struct | a **VARIANT** pointer; `vt = 0x24` (`VT_RECORD`) at offset 0 |
| NO `MarshalAs` + boxed `IntPtr` | `vt = 0x16` (`VT_INT`) |
| NO `MarshalAs` + boxed `int` | `vt = 3` (`VT_I4`) |
| NO `MarshalAs` + `null` | an all-zero VARIANT |

So `AsAny` is **not** "pass the value through": it is a temp copy at the
received address. A "pass the `IntPtr` value directly" implementation would
be wrong, and this case is shaped to catch it (`any_struct` dereferences the
received pointer; the value form would crash or mismatch).

## The audited rows (.NET Framework 4.8 GAC)

```
System.Windows.Forms.UnsafeNativeMethods.CreateWindowEx          p11 blob 28 (AsAny)
System.Windows.Forms.Internal.IntUnsafeNativeMethods.CreateFontIndirect(Object)  blob none
System.Drawing.Internal.IntUnsafeNativeMethods.CreateFontIndirect(Object)        blob none
System.Drawing.SafeNativeMethods+Gdip::GdipCreateFontFromLogfontA  p1 blob 28 (AsAny)
System.Drawing.SafeNativeMethods+Gdip::GdipCreateFontFromLogfontW  p1 blob 28 (AsAny)
System.Drawing.SafeNativeMethods+Gdip::GdipGetLogFontA             p2 blob 28 (AsAny)
System.Drawing.SafeNativeMethods+Gdip::GdipGetLogFontW             p2 blob 28 (AsAny)
```

Beware: `NATIVE_TYPE_ASANY` = 0x28 = 40, and `NATIVE_TYPE_LPSTRUCT` = 0x2C.
They are adjacent; read the byte, do not infer from the type.

## What this case exercises

Value-type boxes only (struct payload, null, `[In, Out]` copy-back). The
reference-payload family (`string` / array / `[StructLayout]` **class**) and
the default-VARIANT family are registered gaps -- see
`docs/audit/b5-gaps.md` section 32.
