//------------------------------------------------------------------------------
// xmlreader2.cs -- XML reading DEEP probe 2 (e2e batch C66): the READ surface
// (traversal structure / attribute values / namespace round-trips / entity
// decoding / CDATA), byte-exact vs the net48 oracle.
//
// SURFACE NOTE (reachable face, xml_doc.h): the runtime has NO System.Xml
// XmlReader (no il2cpp_xml_reader_* anywhere -- recorded gap D-XMLR-1 below).
// The reading surface that IS wired is the XmlDocument handle-gap face:
//   LoadXml / get_OuterXml / get_InnerText / get_InnerXml (see xml_doc.cpp),
// so every "read" probe here drives the DOM through the serializer (OuterXml
// as a FIXED-POINT round-trip = a deterministic traversal of the parsed
// tree, attributes, and namespaces in document order) and through InnerText
// (the decoded-text concat). This pins the parser's traversal/attr/namespace
// semantics net48-byte-for-byte on the reachable face.
//
// net48-shape discipline (xmldeep2 precedent):
//   * No comments / declarations / EMPTY elements: the runtime drops the
//     former and renders the latter self-closing ("<a/>") while net48 keeps
//     comments and emits "<a />" -- empty elements are OUT.
//   * InnerXml-on-the-document is a RECORDED divergence (net48 returns the
//     whole doc incl. the root tag; the runtime's get_inner_xml returns only
//     children) -- only OuterXml / InnerText are probed.
//   * SelectNodes / SelectSingleNode results are UNCONSUMABLE (ReturnsValue
//     = false -> DiscardedValueExpr LOUD on use) -- traversal goes through
//     serialization, not the query engine.
//
// RECORDED WIRING GAPS (defects, NOT probed -- the case stays byte-exact):
//   * D-XMLR-1: System.Xml.XmlReader (and Create/XmlReaderSettings/Read/
//     NodeType/MoveToNextAttribute/... ) has NO runtime icall and NO
//     MethodMap entry -- a use falls to the generic generated-name path
//     (undefined extern at cmake build, NOT a LOUD reject). XmlTextReader/
//     XmlNodeReader are in the same boat.
//   * SelectNodes / SelectSingleNode result consumption -- rv=false in
//     MethodMap.Xml.cs (see xmldeep2's header note).
//   * get_InnerXml on the document -- children-only vs net48 whole-doc.
//------------------------------------------------------------------------------
using System;
using System.Xml;

namespace Cases { public static class XmlReader2 {
    public static void Main() {
        // ================= S1: traversal structure (OuterXml fixed point) =================
        XmlDocument d1 = new XmlDocument();
        d1.LoadXml("<root><a><b><c>deep</c></b></a><a><b>shallow</b></a></root>");
        Console.WriteLine("t_deep=" + (d1.OuterXml ==
            "<root><a><b><c>deep</c></b></a><a><b>shallow</b></a></root>"));
        XmlDocument d2 = new XmlDocument();
        d2.LoadXml("<root>a<b>c</b>d<e>f</e></root>");
        Console.WriteLine("t_mixed=" + (d2.OuterXml == "<root>a<b>c</b>d<e>f</e></root>"));
        XmlDocument d3 = new XmlDocument();
        d3.LoadXml("<root><x><y><z>1</z></y></x><x>2</x><x><y>3</y></x></root>");
        Console.WriteLine("t_branch=" + (d3.OuterXml ==
            "<root><x><y><z>1</z></y></x><x>2</x><x><y>3</y></x></root>"));
        Console.WriteLine("t_text=" + (d3.InnerText == "123"));

        // ================= S2: attribute values (OuterXml round-trip) =================
        XmlDocument a1 = new XmlDocument();
        a1.LoadXml("<root a=\"1\" b=\"two\" c=\"three\">t</root>");
        Console.WriteLine("a_multi=" + (a1.OuterXml == "<root a=\"1\" b=\"two\" c=\"three\">t</root>"));
        XmlDocument a2 = new XmlDocument();
        a2.LoadXml("<root x=\"\">t</root>");
        Console.WriteLine("a_empty=" + (a2.OuterXml == "<root x=\"\">t</root>"));
        XmlDocument a3 = new XmlDocument();
        a3.LoadXml("<root x=\"  spaced  \">t</root>");
        Console.WriteLine("a_ws=" + (a3.OuterXml == "<root x=\"  spaced  \">t</root>"));
        XmlDocument a4 = new XmlDocument();
        a4.LoadXml("<root a=\"x&quot;y\">t</root>");
        Console.WriteLine("a_quote=" + (a4.OuterXml == "<root a=\"x&quot;y\">t</root>"));
        XmlDocument a5 = new XmlDocument();
        a5.LoadXml("<root a=\"a&amp;b\">t</root>");
        Console.WriteLine("a_amp=" + (a5.OuterXml == "<root a=\"a&amp;b\">t</root>"));

        // ================= S3: namespaces (OuterXml round-trip) =================
        XmlDocument n1 = new XmlDocument();
        n1.LoadXml("<root xmlns=\"http://e\"><a>1</a></root>");
        Console.WriteLine("ns_def=" + (n1.OuterXml == "<root xmlns=\"http://e\"><a>1</a></root>"));
        XmlDocument n2 = new XmlDocument();
        n2.LoadXml("<r xmlns:a=\"urn:a\"><a:x>1</a:x><a:y>2</a:y></r>");
        Console.WriteLine("ns_pre=" + (n2.OuterXml == "<r xmlns:a=\"urn:a\"><a:x>1</a:x><a:y>2</a:y></r>"));
        XmlDocument n3 = new XmlDocument();
        n3.LoadXml("<root xmlns=\"a\"><child xmlns=\"b\"><g>1</g></child></root>");
        Console.WriteLine("ns_nest=" + (n3.OuterXml ==
            "<root xmlns=\"a\"><child xmlns=\"b\"><g>1</g></child></root>"));
        XmlDocument n4 = new XmlDocument();
        n4.LoadXml("<root xmlns:p=\"urn:u\" p:attr=\"v\">t</root>");
        Console.WriteLine("ns_attr=" + (n4.OuterXml == "<root xmlns:p=\"urn:u\" p:attr=\"v\">t</root>"));

        // ================= S4: entity + numeric-ref decoding =================
        XmlDocument e1 = new XmlDocument();
        e1.LoadXml("<root>a &amp; b &lt;c&gt;</root>");
        Console.WriteLine("e_round=" + (e1.OuterXml == "<root>a &amp; b &lt;c&gt;</root>"));
        Console.WriteLine("e_text=" + (e1.InnerText == "a & b <c>"));
        XmlDocument e2 = new XmlDocument();
        e2.LoadXml("<root>&#65;&#x42;</root>");
        Console.WriteLine("e_num_round=" + (e2.OuterXml == "<root>AB</root>"));
        Console.WriteLine("e_num_text=" + (e2.InnerText == "AB"));
        XmlDocument e3 = new XmlDocument();
        e3.LoadXml("<root>&#38;&#60;</root>");
        Console.WriteLine("e_special_round=" + (e3.OuterXml == "<root>&amp;&lt;</root>"));
        Console.WriteLine("e_special_text=" + (e3.InnerText == "&<"));
        XmlDocument e4 = new XmlDocument();
        e4.LoadXml("<root>a&apos;b</root>");
        Console.WriteLine("e_apos_round=" + (e4.OuterXml == "<root>a'b</root>"));
        Console.WriteLine("e_apos_text=" + (e4.InnerText == "a'b"));

        // ================= S5: CDATA (kept verbatim) + InnerText concat =================
        XmlDocument c1 = new XmlDocument();
        c1.LoadXml("<root><![CDATA[x < y & z]]></root>");
        Console.WriteLine("c_round=" + (c1.OuterXml == "<root><![CDATA[x < y & z]]></root>"));
        Console.WriteLine("c_text=" + (c1.InnerText == "x < y & z"));
        XmlDocument c2 = new XmlDocument();
        c2.LoadXml("<root>a<![CDATA[<]]>b</root>");
        Console.WriteLine("c_mixed_round=" + (c2.OuterXml == "<root>a<![CDATA[<]]>b</root>"));
        Console.WriteLine("c_mixed_text=" + (c2.InnerText == "a<b"));
        XmlDocument c3 = new XmlDocument();
        c3.LoadXml("<root><a>1</a>sep<![CDATA[2]]><b>3</b></root>");
        Console.WriteLine("c_concat=" + (c3.InnerText == "1sep23"));
        Console.WriteLine("c_concat_round=" + (c3.OuterXml ==
            "<root><a>1</a>sep<![CDATA[2]]><b>3</b></root>"));
    }
}}
