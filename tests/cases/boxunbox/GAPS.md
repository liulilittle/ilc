# GAPS.md — boxunbox unwired surface (2026-08-06)

The case probes ONLY the wired surface (20 lines PASS vs net48, mingw).
Every gap below was VERIFIED as a transpile/build abort or a live diff
mismatch before being excluded — each is recorded with its exact phenomenon
and suspected root file.

## GAP-BOX-1 — boxing a USER STRUCT is unsupported ("modify boxed field" blocked)

- **Phenomenon**: `object o = f;` where `f` is a `struct Fix { public int V; }`
  aborts the transpile with `box of Fix not supported -- no runtime box helper
  (audit H3 + M5)` (CodeGenerator.cs:2752, the box_ switch default arm).
- **Causal chain**: the box_ helper table covers the primitive box types only;
  there is no user-struct box descriptor/allocator path. Because a struct
  cannot be boxed, the whole "modify a field of a boxed value type" probe
  family is unreachable: box `Fix` -> `(IFix)box` -> `Add()` mutates the box
  (net48: the interface callvirt dispatches to the struct method on the boxed
  copy, so `((Fix)box).V` changes while the stack copy does not). The
  interface-dispatch mutation probe is parked until struct box helpers land.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (box_ switch,
  ~line 2752) + runtime box factory for arbitrary value types.

## GAP-BOX-2 — `o is long` (isinst Int64 on a box) fails to COMPILE

- **Phenomenon**: cmake build error
  `use of undeclared identifier 'System_Int64_class'; did you mean
  'System_Int32_class'?` for the generated
  `il2cpp_is_instance(t0, &System_Int64_class)`.
- **Causal chain**: isinst on a value type emits `&{TypeCppName}_class`; the
  runtime declares `System_Int32_class` (and friends) but NOT
  `System_Int64_class` — so the boxed-Int64 isinst path references a missing
  descriptor. (boxing2 passes `(long)ol` unboxes via a different, icall-based
  path — only the isinst descriptor is missing.)
- **Suspected root file**: runtime descriptor table (il2cpp_metadata.h
  externs / class registration) — add `System_Int64_class`.

## GAP-BOX-3 — interface callvirt on a boxed value → undefined extern

- **Phenomenon**: `((IComparable)od).CompareTo(42)` on a boxed Int32 resolves
  to an UNDEFINED generated extern
  `System_IComparable_CompareTo_<hash>` (no runtime symbol) → cmake build fail.
- **Causal chain**: callvirt on an interface method is not wired for a boxed
  receiver — the resolver falls through to generated-name emission instead of
  an interface-dispatch icall (or the interface's method table on the box).
- **Suspected root file**: `src/il2cppc/Emit/StackFold.cs` / CodeGenerator
  callvirt path (interface dispatch on boxed values).

## GAP-BOX-4 — Object::ToString on a GENERIC-produced box → undefined extern

- **Phenomenon**: `object bo = bx.Get();` (Box<int>.Get() returns the stored
  T=int32 field, converted to object) then `bo.ToString()` emits the undeclared
  `System_Object_ToString_<hash>` extern.
- **Causal chain**: the box type is NOT TRACKED through the generic
  `T -> object` conversion, so MethodMap.Object.cs's tracked-receiver
  ToString arm never matches and the call falls to the vtable path
  (loud-failure downstream) instead of `icall_object_to_string`.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Object.cs` (ToString
  receiverType whitelist) / StackFold box-type tracking through generic reads.

## GAP-BOX-5 — boxed UInt32 ToString → undefined extern (transpiler whitelist miss)

- **Phenomenon**: `((object)4000000000u).ToString()` emits the undeclared
  `System_Object_ToString_<hash>` extern (build fail), while boxed
  Int32/UInt64/Byte/Int16/Boolean/Char/Double all route correctly to
  `icall_object_to_string`.
- **Causal chain**: the Object::ToString tracked-receiver whitelist in
  MethodMap.Object.cs lists every boxed prim EXCEPT `"System.UInt32"`. The
  runtime icall DOES handle the UInt32 arm (valuesemantics.cpp). A pure
  transpiler whitelist omission.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Object.cs` (ToString
  `receiverType is ...` list) — add `"System.UInt32"`.

## GAP-BOX-6 — boxed GetHashCode beyond Int32 is ADDRESS-based (diff)

- **Phenomenon**: `((object)true).GetHashCode() == ((object)true).GetHashCode()`
  is False (two separate boxes of `true` hash differently), and the hashes
  diverge from net48. The Int32 probes pass (`((object)42).GetHashCode() == 42`,
  two boxes of 42 hash equal).
- **Causal chain**: `icall_object_get_hash_code` (valuesemantics.cpp) is
  value-based ONLY for boxed Int32 (`return BoxedInt32Value(obj)`); every
  other boxed value falls to `return (uintptr_t)obj >> 3` (address hash).
  net48 value semantics: bool->1/0, char->code unit, double->XOR of the two
  int32 halves (NaN-normalized), long->low^high, etc.
- **Suspected root file**: `src/runtime/src/valuesemantics.cpp`
  (icall_object_get_hash_code) — extend the value-hash table to all boxed
  prims (mirroring icall_object_to_string's coverage).
