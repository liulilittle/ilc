# GAPS — xmaudit2 (XPath: `//root` root inclusion + `/r//x[2]` predicate context)

Status: permanent WIRING PIN. e2e-frozen2.sh on BOTH compilers:
transpile stage exits 0 but emits warning-only diagnostics; C++ build FAILS.

## Observed failure chain (mingw + msvc identical)

- transpile log: `warning: inline fold failed for Cases.XmAudit2::Main ->
  skipped`, `warning: method fold failed -> skipped`, then three
  `System.Xml.XmlNodeList::get_Count: not wired in M5` warnings.
- generated `gen/src/Cases_XmAudit2.cpp:27` still references
  `Cases_XmAudit2_Main_665dc3de557ca80c` in the typeinfo table, and
  `gen/src/entry.cpp:85` calls it -- but the Main definition was never
  emitted.
- build errors: mingw `error: use of undeclared identifier
  'Cases_XmAudit2_Main_665dc3de557ca80c'`; msvc C2065 (same symbol),
  C2737 (typeinfo_methods cannot be const-initialized), C3861 (entry.cpp).

Root cause: `SelectNodes` is mapped `ReturnsValue=false`
(MethodMap.Xml.cs:93); consuming the result (`XmlNodeList l = ...; l.Count`)
makes the StackFold bail. The transpile stage does NOT fail loudly; the
dangling Main reference only breaks the C++ build.

## net48 oracle (expected.txt)

- `/root`-document `<root/>`, `SelectNodes("//root")`: Count=1 (the document
  element is included by `//`).
- `<r><a><x>1</x></a><b><x>2</x><x>3</x></b></r>`, `SelectNodes("/r//x[2]")`:
  Count=1, text=3 (predicate applies per parent).
- Static audit predicts runtime (once rv=true): 0 (xml_query.cpp:263-266
  excludes the root from descendant steps) and 2 (xml_query.cpp:277-283 [n]
  over the merged per-candidate list). The semantics probe activates when the
  mapping flips rv=true.
