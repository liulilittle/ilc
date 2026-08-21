//------------------------------------------------------------------------------
// xpathdeep.cs -- XML domain DEEP probe (e2e batch C56): XPath engine
// reachable surface + deep tree round-trips, byte-exact vs the net48 oracle.
//
// REACHABLE XPath surface (probe-verified on this HEAD, 2026-08-12):
//   * SelectNodes / SelectSingleNode are wired (MethodMap.Xml.cs) but marked
//     ReturnsValue=false, so the fold emits a statement call + a discarded
//     slot -- the RESULT value cannot be consumed in an expression (the
//     emitter rejects "IR expression DiscardedValueExpr is not supported").
//     Node-selection / predicate / axis RESULTS are therefore NOT e2e-
//     observable yet (see GAPS.md); what IS reachable and pinned here:
//       1. Valid-path STATEMENT acceptance -- the query engine parses and
//          evaluates /root/a, [n] predicates, // descendant axes and the
//          @attr attribute step without rejecting the path (probe: no
//          XPathException). Catch-wrapped so a regression prints False.
//       2. SYNTAX-ERROR surface -- XPathException from the engine is a
//          catchable managed exception; the error cases below were
//          probe-verified to throw on BOTH net48 and the runtime:
//          "/root/" (trailing slash), "/root/a[" (unclosed bracket),
//          "/root//" (empty descendant step), "///item" (empty root step).
//       3. Divergent syntax shapes EXCLUDED (runtime throws where net48
//          returns an empty result): "[0]" predicate, relative paths, "*"
//          wildcard -- documented in GAPS.md.
//   * The deep round-trips below use ONLY non-empty elements: empty elements
//     serialize self-closing ("<a/>") on the runtime but "<a />" on net48,
//     comments/declarations drop on the runtime but survive on net48
//     (xmldeep2 precedent -- all byte-exact probes avoid those shapes).
//------------------------------------------------------------------------------
using System;
using System.Xml;
using System.Xml.XPath;

namespace Cases { public static class Xpathdeep {
    static bool Accepted(XmlNode root, string path) {
        // Valid XPath: the engine must parse+evaluate without throwing.
        bool ok = false;
        try { root.SelectNodes(path); ok = true; } catch (XPathException) { }
        return ok;
    }
    static bool SingleAccepted(XmlNode root, string path) {
        bool ok = false;
        try { root.SelectSingleNode(path); ok = true; } catch (XPathException) { }
        return ok;
    }
    static bool Rejected(XmlNode root, string path) {
        // Syntax-error XPath: BOTH net48 and the runtime throw XPathException.
        bool threw = false;
        try { root.SelectNodes(path); } catch (XPathException) { threw = true; }
        return threw;
    }
    public static void Main() {
        // ================= S1: deep tree round-trips (OuterXml fixed point) ===
        XmlDocument d1 = new XmlDocument();
        d1.LoadXml("<root><a><b><c>deep</c></b></a><a><b>sib</b></a><a><b><c>d2</c></b></a></root>");
        Console.WriteLine("rt_deep=" + (d1.OuterXml ==
            "<root><a><b><c>deep</c></b></a><a><b>sib</b></a><a><b><c>d2</c></b></a></root>"));

        XmlDocument d2 = new XmlDocument();
        d2.LoadXml("<root>pre<a>1<b>2</b>3</a>mid<c>4</c>post</root>");
        Console.WriteLine("rt_mixed=" + (d2.OuterXml == "<root>pre<a>1<b>2</b>3</a>mid<c>4</c>post</root>"));

        XmlDocument d3 = new XmlDocument();
        d3.LoadXml("<root>&#65;&#x42;&amp;&lt;x&gt;</root>");
        Console.WriteLine("rt_ent=" + (d3.OuterXml == "<root>AB&amp;&lt;x&gt;</root>"));

        XmlDocument d4 = new XmlDocument();
        d4.LoadXml("<root><![CDATA[a < b & c]]>tail</root>");
        Console.WriteLine("rt_cdata=" + (d4.OuterXml == "<root><![CDATA[a < b & c]]>tail</root>"));

        // NOTE: whitespace-only text nodes are EXCLUDED -- net48 drops them on
        // LoadXml (PreserveWhitespace=false default) while the runtime keeps
        // them (probe-verified divergence, documented in GAPS.md).

        XmlDocument d6 = new XmlDocument();
        d6.LoadXml("<r xmlns:a=\"urn:a\"><a:x b=\"1\">t</a:x></r>");
        Console.WriteLine("rt_ns=" + (d6.OuterXml == "<r xmlns:a=\"urn:a\"><a:x b=\"1\">t</a:x></r>"));

        // ================= S2: InnerText (descendant text concat) =============
        Console.WriteLine("it_deep=" + (d1.InnerText == "deepsibd2"));
        Console.WriteLine("it_mixed=" + (d2.InnerText == "pre123mid4post"));
        Console.WriteLine("it_cdata=" + (d4.InnerText == "a < b & ctail"));

        // ================= S3: XPath engine statement acceptance ==============
        // The engine must accept node selection / predicates / axes / the
        // attribute step (result consumption is gated -- GAPS.md).
        Console.WriteLine("xp_sel=" + Accepted(d1, "/root/a"));
        Console.WriteLine("xp_pred=" + Accepted(d1, "/root/a[2]"));
        Console.WriteLine("xp_desc=" + Accepted(d1, "/root//b"));
        Console.WriteLine("xp_path=" + Accepted(d1, "/root/a[2]/b[1]"));
        Console.WriteLine("xp_deepdesc=" + Accepted(d1, "/root//c"));
        Console.WriteLine("xp_single=" + SingleAccepted(d1, "/root/a[1]"));
        XmlDocument d7 = new XmlDocument();
        d7.LoadXml("<root><item id=\"a\">one</item><item id=\"b\">two</item></root>");
        Console.WriteLine("xp_attr=" + Accepted(d7, "/root/item/@id"));
        Console.WriteLine("xp_attrdesc=" + Accepted(d7, "/root//item/@id"));
        Console.WriteLine("xp_pred2=" + SingleAccepted(d7, "/root/item[2]"));

        // ================= S4: XPath syntax-error surface (XPathException) =====
        Console.WriteLine("err_trail=" + Rejected(d1, "/root/"));
        Console.WriteLine("err_unclosed=" + Rejected(d1, "/root/a["));
        Console.WriteLine("err_doubleslash=" + Rejected(d1, "/root//"));
        Console.WriteLine("err_slash3=" + Rejected(d1, "///item"));
        bool singleThrew = false;
        try { d1.SelectSingleNode("/root/"); } catch (XPathException) { singleThrew = true; }
        Console.WriteLine("err_single=" + singleThrew);
    }
}}
