# GAPS.md — p-refl-getmethods (GAP-4 / P1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.GetMethods() includes constructors (net48 exclusions)

- **Phenomenon**: the runtime's `get_methods` enumeration used the simplified
  `PublicMethod` filter that INCLUDED ctors (a documented R2 deviation); net48
  `GetMethods()` EXCLUDES `.ctor`/`.cctor` (they surface only through
  `GetConstructors()`).
- **Fix**: `PublicMethod` now also requires the ctor bit CLEAR
  (`flags & 4 == 0`); `PublicCtor`/`il2cpp_typeinfo_get_constructors` is
  unchanged.
- **RED**: runtime emitted `.ctor` in GetMethods (count mismatch / ctor false).
- **GREEN**: all 5 records byte-exact (the inherited System.Object virtuals are
  filtered identically on both sides -- the typeinfo chain does not model them,
  rf-members precedent).
- Also updated test_typeinfo.cpp's pinned R2 get_methods expectations (ctor now
  excluded, count 4, `!hasCtor`).