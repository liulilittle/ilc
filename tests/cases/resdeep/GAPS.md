# GAPS.md -- resdeep recorded findings (e2e batch C37, 2026-08-11)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 27-line byte-exact).

## D-RD-1 -- GetManifestResourceStream returns UnmanagedMemoryStream on net48, MemoryStream on ILC

- **Phenomenon**: net48 returns an `UnmanagedMemoryStream` (read-only view over
  the resource data), not a `MemoryStream`. The ILC runtime returns a
  MemoryStream-backed object (commit 272bb57). For the probes used here
  (Length / Position / CanRead / Read / EOF) both behave identically, so the
  diff is green.
- **Causal chain / oracle nuance**: `UnmanagedMemoryStream.Read` validates the
  BUFFER bounds at call time (offset + count > buffer.Length throws
  ArgumentException), it does not clamp to the stream's remaining bytes. A
  chunked-read loop that requests a count larger than the remaining buffer
  slots from `offset` throws on net48. Fix used in the case: read chunks into a
  scratch buffer whose capacity always satisfies `offset + count`.
- **Suspect**: net48 BCL behavior (oracle side); no ILC defect.

## D-RD-2 -- byte[] instance fields unsupported (M2 field signature)

- **Phenomenon**: a user class with a `byte[]` instance field fails the
  transpile with "field type byte 0x1D not supported in M2"
  (TypeEmitter.DecodeNestedFieldSignature). byte[] LOCALS transpile fine.
- **Causal chain**: the nested-type field decoder (TypeEmitter.cs:281) switches
  on the field's element byte; SZARRAY (0x1D) has no arm, and static fields
  bypass the decoder. This is the SAME defect as D-GC-1 (shared root).
- **Suspect**: TypeEmitter.cs DecodeNestedFieldSignature.
- **STATUS -- CLOSED (W11 resource batch A / R8, 2026-10-07)**: the SZARRAY
  (0x1D) / ARRAY (0x14) / 0x1F INSTANCE-field arm was added by D-GC-1
  (ba09646c, 2026-08-21, fork-recovery that consolidated the D-GC work) in BOTH
  field decoders: MetadataLoader.DecodeFieldSignature (top-level types) and
  TypeEmitter.DecodeNestedFieldSignature (nested types) now classify an array
  field as `(Reference, "System.Array")` -- a uniform Il2CppObject* GC slot,
  bitmap bit set. The byte[] case is now locked by this case's section 7
  (ByteFieldHolder + ByteFieldNest.NestedByteHolder): field layout
  (value slots bracketing the array-ref slot), Length/element read/write, and
  GC survival through the field root -- byte-exact vs the net48 oracle on
  mingw and msvc.
