# GAPS.md — p-refl-visible (GAP-1 / P0)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: private member visibility in no-flags single-member queries

- **Phenomenon**: `typeof(X).GetMethod(string)` / `GetProperty(string)` /
  `GetField(string)` bind net48 `Public|Instance|Static` by default — a PRIVATE
  member is invisible (returns null), while an EXPLICIT
  `BindingFlags.NonPublic|...` query finds it. `get_field` already filtered
  `flags&1`; `get_method`/`get_property` did NOT, so a private method/property
  was hit by the no-flags query (wrong non-null vs net48's null).
- **Fix**:
  - runtime typeinfo.cpp: `il2cpp_type_get_method_flags` /
    `il2cpp_type_get_property_flags` / `il2cpp_type_get_field_flags` — the
    net48 BindingFlags visibility + instance/static filter (Public=16,
    NonPublic=32, Instance=4, Static=8). Properties derive visibility from
    their accessor methods (a property is public when any accessor is public).
    The no-arg `il2cpp_type_get_*` managed-string ABI icalls now pass the
    default Public|Instance|Static (16|4|8) — matching net48.
  - MethodMap.Type.cs: `GetMethod`/`GetProperty`/`GetField` gained the
    `(string, BindingFlags)` overload wiring → `il2cpp_typeinfo_*_flags`.
- **RED**: 2-param overloads threw LOUD (transpile gap); private members were
  returned by the no-flags queries.
- **GREEN**: all 13 records byte-exact.