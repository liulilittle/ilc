# GAPS.md — p-refl2-6iface (refl2 item 6)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.GetInterface(string) / (string, bool)

- **Phenomenon**: Type.GetInterface had NO MethodMap arm -- the receiver fell
  to the generated-member phantom and the entry was skipped (runtime exit 1).
- **Fix**:
  - MethodMap.Type.cs: wire `GetInterface` (string) with `appendZero` for the
    default ignoreCase=0, and (string, bool) with the bool riding as "i".
  - runtime typeinfo.cpp: `il2cpp_typeinfo_get_interface(typeName, name,
    ignoreCase)` scans the typeinfo interface list (own + base chain, most-
    derived first) and returns the interface FULL-NAME string (a Type VALUE).
- **net48 matching pinned by the oracle**:
  - a query name WITH a dot uses the FULL-name path ONLY -- always ordinal
    case-sensitive, IGNORING ignoreCase ("cAsEs.IDerived6", ignoreCase=true ->
    still a miss);
  - a DOT-FREE query name is the SIMPLE-name path, following ignoreCase.
- **RED**: runtime exit 1, all 10 records missing (phantom).
- **GREEN**: 10/10 records byte-exact (full+base inherited, simple, missing,
  unrelated, ignoreCase true/false both paths, result .Name short name).

## Known simplification (W7)

- Interfaces are matched against the typeinfo-registered interface list only
  (user interfaces); BCL interfaces (e.g. IDisposable) are not registered and
  cannot be queried by this model just as GetInterfaces() omits them.