# GAPS.md -- xpathdeep (e2e batch C56)

## 1. XPath query RESULT consumption is blocked (transpiler, not fixed per batch rules)

`System.Xml.XmlDocument.SelectNodes(string)` and `SelectSingleNode(string)` are
mapped in `MethodMap.Xml.cs` but with `ReturnsValue=false` (the `I(..., false,
true, true, ...)` calls at MethodMap.Xml.cs:93/99). With `ReturnsValue=false`
and a non-void .NET signature, StackFold folds the call into a statement +
`DiscardedValueExpr` (StackFold.cs:2190-2197), and **using the result in an
expression (assignment / member access / comparison / chained InnerText) is
loudly rejected** by the emitter:

    il2cppc error: IR expression DiscardedValueExpr is not supported by M1 emission

Empirically verified on this HEAD (2026-08-12, bc1c0b9): a probe
`XmlNode n = d.SelectSingleNode("/root/item[2]"); ... n.InnerText` transpiles to
exactly that loud reject.

Consequence: **node-selection / positional-predicate / descendant-axis /
attribute-step RESULTS are not e2e-observable.** This case pins what IS
reachable instead:
  * valid-path statement acceptance (the engine parses + evaluates the path
    without rejecting it) and
  * the catchable XPathException syntax-error surface.

Fix path (NOT applied -- batch rule "defects recorded, not fixed"): flip
`ReturnsValue=true` on the two mappings and give the XmlNodeList/XmlNode result
a usable managed representation (an object[] is already returned by
`il2cpp_xml_select_nodes`; a receiver/result-type track exists via
`ReturnTypeName`).

## 2. Documented XPath divergences (excluded from the byte-exact case)

Probe-verified net48-vs-runtime differences; the case deliberately avoids these
shapes so the diff stays byte-exact:

| shape | net48 | runtime (xml_query.cpp) |
|-------|-------|--------------------------|
| `[0]` positional predicate | empty result, no error | `XPathException: index must be >= 1` |
| relative paths (`root/item`, `item`) | evaluated relative to context node | `XPathException: relative XPath expressions are not supported` |
| `*` wildcard step | matches all element children | `XPathException: step is missing its name` |

## 3. Whitespace-only text nodes (serialization divergence)

net48 drops whitespace-only text nodes during LoadXml (PreserveWhitespace=false
default); the runtime keeps them. A round-trip of `<root>  <a>x</a> </root>` is
therefore `<root><a>x</a></root>` on net48 but `<root>  <a>x</a> </root>` on the
runtime. Excluded from the case (probe-verified `rt_ws=False` on net48).

## 4. Not-newly-tested (covered elsewhere)

Empty elements / comments / XML declarations diverge (net48 keeps them, the
runtime drops them / self-closes) -- the same exclusion xmldeep2 documents.
`XmlElement.GetAttribute` is unreachable: no expression produces an XmlElement
typed receiver (the only producer is the gated SelectSingleNode result).
