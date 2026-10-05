# GAPS.md -- gcstress3 recorded findings (e2e batch C37, 2026-08-11)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 19-line byte-exact).

## D-GC-1 -- ARRAY instance fields unsupported in nested types (M2 field signature)

- **Phenomenon**: a user class with ANY array instance field (int[]/byte[]/...,
  element byte SZARRAY 0x1D) fails the transpile:
  `il2cppc error: field type byte 0x1D not supported in M2`
  (TypeEmitter.DecodeNestedFieldSignature).
- **Causal chain**: the nested-type instance-field decoder (TypeEmitter.cs:281)
  switches on the field signature's element byte and has no SZARRAY arm; only
  scalar instance fields (int32 / string / object / class ref / IntPtr) decode.
  STATIC fields skip the decoder, which is why gcstatic3's `int[] AArr` /
  `object[] EArr` static fields work. The case therefore keeps rooted objects
  scalar-only and allocates all arrays as locals.
- **Suspect**: TypeEmitter.cs DecodeNestedFieldSignature (add a SZARRAY arm
  that resolves the element type to a ref field).

## D-GC-2 -- newarr element type whitelist (audit C4) excludes WeakReference / user classes

- **Phenomenon**: `new WeakReference[5]` / `new Payload[5]` fail the transpile:
  `il2cppc error: newarr element type System.WeakReference not supported
  (audit C4)`.
- **Causal chain**: CodeGenerator.ArrayElementType only maps the wired element
  types (int32 / object / string / byte / ...); WeakReference and user classes
  are not in the list.
- **Disposition**: the case uses individual WeakReference locals and individual
  Payload locals instead of arrays.
- **Suspect**: CodeGenerator.cs ArrayElementType (~line 5736).
