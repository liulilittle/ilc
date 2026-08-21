# GAPS.md -- nesteddeep recorded findings (e2e batch C39, 2026-08-11)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 30-line byte-exact).
Active probes: nested-class field full chain (int32 / string / object /
self-typed class-ref / int[] / string[] / Inner[]), nested instance + static
methods, nested static field, linked-list object graph rooted by a top-level
static field, array fields through a GC pressure wave, two-level nesting
(Inner.Deep), and release/collection of a dropped root.

## D-NEST-2 -- `string + intField` folds to an ill-formed cast (transpiler)

- **Phenomenon**: `Tag + ":" + V` where `V` is an **int instance FIELD** of a
  nested class fails the C++ build:
  `error: static_cast from 'int32_t *' (aka 'int *') to 'Il2CppObject *' is
  not allowed` (generated `Inner.cpp`, the `Describe()` method).
- **Causal chain**: Roslyn lowers `string + intField` to
  `ldflda V; call Int32::ToString()` -- it passes the FIELD ADDRESS, not the
  value. The transpiler folds that shape into
  `icall_int32_to_string(il2cpp_unbox_int32(static_cast<Il2CppObject*>(&V)))`:
  the `&field` (an `int32_t*`) is cast to `Il2CppObject*` as if it were a
  boxed object -- an ill-formed `static_cast`. The same fold for a LOCAL/param
  int is fine (`icall_string_concat_int(g_strPool[0], loc1)` -- the BuildChain
  `"n" + i` shape, which compiles). threadcase2's recorded `ldsflda`-gap
  comment (its "…+g_workerA" static-field note) is the same defect family.
- **Worked around**: `Describe()` routes the int FIELD through a value-param
  helper (`return Tag + ":" + N(V);` where `N(int x) => "n" + x`) so the
  concat operand is a parameter local, not a field address.
- **Suspect**: `src/il2cppc/Emit/CodeGenerator.cs` -- the `ldflda`/`ldsflda`
  followed-by-`Int32::ToString()` fold (the `il2cpp_unbox_int32` wrapping path
  must load the FIELD VALUE via ldfld, not the field address).
