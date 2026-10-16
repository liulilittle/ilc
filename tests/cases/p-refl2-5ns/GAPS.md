# GAPS.md — p-refl2-5ns (refl2 item 5)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.get_Namespace

- **Phenomenon**: Type.get_Namespace had NO MethodMap arm -- the receiver fell
  to the generated-member path where the emitted call can never resolve
  (phantom) and the transpile aborted the entry (runtime exit 1, 0 records).
- **Fix**:
  - MethodMap.Type.cs: wire `get_Namespace` -> `il2cpp_typeinfo_get_namespace`
    (name-string receiver, "System.String" ResultKind).
  - runtime typeinfo.cpp: truncate the Type VALUE full name at the LAST '.' --
    net48 Namespace keeps the namespace of the OUTERMOST declaring type (the
    nested '+'-chain rides inside the final segment, so the truncation is
    correct for nested types too: "Cases.OuterTop+InnerTop" -> "Cases"). A
    GLOBAL-namespace type (no '.') returns null -- net48 returns NULL (NOT the
    empty string), probe-pinned.
- **RED**: runtime exit 1, all 6 records missing (get_Namespace phantom).
- **GREEN**: 6/6 records byte-exact (in-namespace, deep namespace, global ->
  null, nested-of-namespaced, nested-of-global -> null).

## Known simplification (W7)

- net48's Namespace is stored on the outermost declaring type; the truncation
  model reproduces it for the probe lattice. Types whose reflection names use
  a '.' inside a generic arity backtick or compiler-synthesized segments are
  outside the probe set.