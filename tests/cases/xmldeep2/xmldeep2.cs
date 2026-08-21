//------------------------------------------------------------------------------
// xmldeep2.cs -- XML domain DEEP probe 2 (e2e batch C43): namespace /
// attribute / InnerText round-trip surface, byte-exact vs the net48 oracle.
//
// Wired expression surface exercised (MethodMap.Xml.cs / xml_doc.h):
//   XmlDocument ctor / LoadXml / get_OuterXml / get_InnerText -- all on the
//   DOCUMENT HANDLE (the document local IS the root node, handle-gap
//   contract). LoadXml->OuterXml is a FIXED POINT, so each round-trip
//   assertion pins the serializer against net48 byte-for-byte.
//
// net48-shape discipline (xmlcase precedent, extended):
//   * No comments / declarations / empty elements in the inputs: the runtime
//     drops comments+declarations and renders empty elements self-closing
//     ("<a/>") while net48 keeps comments and emits "<a />".
//   * No xmlns-PREFIXED XPath (net48 needs a namespace manager for a prefix;
//     the runtime matches the literal name -- a documented divergence, so
//     namespaces are probed ONLY through LoadXml/OuterXml round-trips).
//
// RECORDED WIRING GAPS (defects, NOT probed -- the case stays byte-exact on
// the reachable surface):
//   * SelectNodes / SelectSingleNode result consumption -- the method map
//     (MethodMap.Xml.cs:87-101) marks both rv=false (ReturnsValue=false), so
//     the fold pushes a DiscardedValueExpr for the non-void .NET signature;
//     using the result in an expression (assignment / member access /
//     comparison) hits the emitter's "IR expression DiscardedValueExpr is
//     not supported" loud reject. The query-chain surface (XPath subset:
//     /root/.. // .. [n] @attr) is therefore NOT e2e-reachable until the
//     mapping flips ReturnsValue=true.
//   * XmlElement.GetAttribute -- needs an XmlElement-typed receiver; no
//     reachable expression produces one (SelectSingleNode result is dropped,
//     see above), so attribute reads go through OuterXml/InnerText instead.
//   * get_InnerXml on the DOCUMENT -- net48 XmlDocument.InnerXml returns the
//     WHOLE document markup (incl. the root tag) while the runtime's
//     il2cpp_xml_get_inner_xml on the root node returns only the CHILDREN
//     (xml_doc.cpp:388, SerializeChildren) -- a documented divergence, so
//     only OuterXml (both sides root-serialize) and InnerText are probed.
//------------------------------------------------------------------------------
using System;
using System.Xml;

namespace Cases { public static class Xmldeep2 {
    public static void Main() {
        // ================= S1: attribute round-trips (OuterXml) =================
        XmlDocument d1 = new XmlDocument();
        d1.LoadXml("<root id=\"r1\" lang=\"en\"><item id=\"i1\" lang=\"fr\">one</item><item id=\"i2\">two</item></root>");
        Console.WriteLine("o_attr=" + (d1.OuterXml ==
            "<root id=\"r1\" lang=\"en\"><item id=\"i1\" lang=\"fr\">one</item><item id=\"i2\">two</item></root>"));
        XmlDocument d2 = new XmlDocument();
        d2.LoadXml("<root a=\"1\" b=\"2\" c=\"3\"><child x=\"y\">t</child></root>");
        Console.WriteLine("o_multi_attr=" + (d2.OuterXml == "<root a=\"1\" b=\"2\" c=\"3\"><child x=\"y\">t</child></root>"));

        // ================= S2: namespace round-trips =================
        XmlDocument d3 = new XmlDocument();
        d3.LoadXml("<root xmlns=\"http://e\"><a>1</a></root>");
        Console.WriteLine("ns_def=" + (d3.OuterXml == "<root xmlns=\"http://e\"><a>1</a></root>"));
        XmlDocument d4 = new XmlDocument();
        d4.LoadXml("<ns:root xmlns:ns=\"urn:test\"><ns:a>1</ns:a></ns:root>");
        Console.WriteLine("ns_pre=" + (d4.OuterXml == "<ns:root xmlns:ns=\"urn:test\"><ns:a>1</ns:a></ns:root>"));
        XmlDocument d5 = new XmlDocument();
        d5.LoadXml("<r xmlns:a=\"urn:a\" xmlns:b=\"urn:b\"><a:x>1</a:x><b:y>2</b:y></r>");
        Console.WriteLine("ns_two=" + (d5.OuterXml ==
            "<r xmlns:a=\"urn:a\" xmlns:b=\"urn:b\"><a:x>1</a:x><b:y>2</b:y></r>"));

        // ================= S3: InnerText (descendant text concat) =================
        Console.WriteLine("it_flat=" + (d1.InnerText == "onetwo"));
        Console.WriteLine("it_deep=" + (d5.InnerText == "12"));
        XmlDocument d6 = new XmlDocument();
        d6.LoadXml("<root>a<b>c</b>d<e>f</e></root>");
        Console.WriteLine("it_mixed=" + (d6.InnerText == "acdf"));
        XmlDocument d7 = new XmlDocument();
        d7.LoadXml("<root><![CDATA[x < y]]></root>");
        Console.WriteLine("it_cdata=" + (d7.InnerText == "x < y"));

        // ================= S4: entity round-trips =================
        XmlDocument d8 = new XmlDocument();
        d8.LoadXml("<root>a &amp; b &lt;c&gt;</root>");
        Console.WriteLine("ent=" + (d8.OuterXml == "<root>a &amp; b &lt;c&gt;</root>"));
        XmlDocument d9 = new XmlDocument();
        d9.LoadXml("<root x=\"a &amp; b\">t</root>");
        Console.WriteLine("ent_attr=" + (d9.OuterXml == "<root x=\"a &amp; b\">t</root>"));

        // ================= S5: nesting round-trip (control) =================
        XmlDocument d10 = new XmlDocument();
        d10.LoadXml("<root><a><b><c>deep</c></b></a><a><b>shallow</b></a></root>");
        Console.WriteLine("nest=" + (d10.OuterXml ==
            "<root><a><b><c>deep</c></b></a><a><b>shallow</b></a></root>"));
        Console.WriteLine("nest_text=" + (d10.InnerText == "deepshallow"));
    }
}}
