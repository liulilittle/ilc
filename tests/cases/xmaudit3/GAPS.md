# GAPS — xmaudit3 (XPath namespace semantics)

Status: permanent WIRING PIN. e2e-frozen2.sh on BOTH compilers:
transpile stage exits 0 but emits warning-only diagnostics; C++ build FAILS.

## Observed failure chain (mingw + msvc identical)

- transpile log: `warning: inline fold failed for Cases.XmAudit3::Main ->
  skipped`, `warning: method fold failed -> skipped`, then three
  `System.Xml.XmlNodeList::get_Count: not wired in M5` warnings.
- generated `gen/src/Cases_XmAudit3.cpp:27` still references
  `Cases_XmAudit3_Main_8cd4fe41f0fc816d` in the typeinfo table, and
  `gen/src/entry.cpp:85` calls it -- Main was never emitted.
- build errors: mingw `error: use of undeclared identifier
  'Cases_XmAudit3_Main_8cd4fe41f0fc816d'`; msvc C2065 (same symbol),
  C2737, C3861.

Same root cause as xmaudit2: `SelectNodes` is `ReturnsValue=false`
(MethodMap.Xml.cs:93); consuming the result folds-fails and the transpile
stage degrades to warnings + a dangling symbol.

## net48 oracle (expected.txt)

- `<root xmlns="http://e"><a>1</a></root>`, `SelectNodes("/root/a")`:
  Count=0 (XPath 1.0 unprefixed names match the EMPTY namespace).
- `SelectNodes("/ns:root")` with no NamespaceManager: XPathException.
- Static audit predicts runtime (once rv=true): P1=1 and P2=0 with NO throw
  (xml_query.cpp:179-183 compares literal QNames; xmlns is an ordinary
  attribute). The semantics probe activates when the mapping flips rv=true.
