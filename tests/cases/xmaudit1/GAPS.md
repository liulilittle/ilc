# GAPS — xmaudit1 (XML audit probe 1: NUL truncation + control-char escaping)

Status: permanent DIVERGENCE PIN (net48-vs-runtime). e2e-frozen2.sh:
transpile/build/run PASS on both compilers; diff FAIL.
  sha out: nat=4d00263418aac06c, ilc=42e9efecbf1cd92e (mingw == msvc).

## Oracle-verified behavior (expected.txt = net48 output)

- P1 `LoadXml("<a/>\0<b/>")`: BOTH sides ACCEPT and truncate at the NUL.
  net48 OuterXml `<a />` (net48 self-closes WITH a space);
  runtime OuterXml `<a/>`.
- P2 `LoadXml("<a>&#1;</a>")`: BOTH sides accept the numeric control
  reference (the audit prediction "net48 throws XmlException" was DISPROVED).
  net48 OuterXml `<a>&#x1;</a>` (control escaped as `&#x1;`, len 12);
  runtime OuterXml contains the RAW U+0001 byte (len 8).

## Divergence causes (not fixed here)

1. Empty-element shape: xml_doc.cpp:227-230 emits `<name/>`; net48 `<name />`.
2. Control-char escaping: EscapeText (xml_doc.cpp:147-169) escapes only
   `& < >`; net48 escapes U+0001 as `&#x1;`. NUL truncation itself is shared
   (ldstr strlen, stringintern.cpp:334; parse strlen, xml_parse.cpp:711).

## Probe design notes

- Typed catch clauses only (no exception introspection) so the generated C++
  stays mapped; the case prints exact OuterXml + length.
