//------------------------------------------------------------------------------
// xmlwrite.cs -- XML WRITE probe (e2e batch C65): System.Xml.XmlWriter
// output / formatting surface, oracle-pinned against net48.
//
// GAP D-XMLW-1 RESOLVED (2026-08-12): System.Xml.XmlWriter / XmlWriterSettings
// now route to the xml_write.h minimal write surface (MethodMap.Xml.cs
// TryMapXmlWriter -- Create / WriteStartElement / WriteAttributeString /
// WriteElementString / WriteString / WriteEndElement / WriteEndDocument /
// Flush / Close + the XmlWriterSettings setters, xml_write.cpp). Before the
// wiring these members fell to the generic generated-name path -> undefined
// C++ externs -> cmake_build FAIL.
//
// The net48 oracle output below is DETERMINISTIC and byte-pinned so the case
// is a self-verifying write test (dual-compiler PASS on the wired surface):
//   * XmlWriter.Create(StringBuilder) + default settings -> single-line doc
//     WITH the xml declaration (`<?xml version="1.0" encoding="utf-16"?>`).
//   * OmitXmlDeclaration=true + Indent=true -> no declaration, indented tree
//     (net48 emits CRLF line endings and 2-space indentation).
//   * Entity escaping in both attribute values and element text
//     (< & > " -> &lt; &amp; &gt; &quot;; ' stays literal in "..." attrs).
//------------------------------------------------------------------------------
using System;
using System.Text;
using System.Xml;

namespace Cases { public static class XmlWrite {
    public static void Main()
    {
        // S1: default settings -- declaration + single-line element output.
        var sb = new StringBuilder();
        XmlWriter w = XmlWriter.Create(sb);
        w.WriteStartElement("root");
        w.WriteAttributeString("id", "1");
        w.WriteElementString("a", "text & more");
        w.WriteEndElement();
        w.Close();
        Console.WriteLine(sb.ToString());
        // <?xml version="1.0" encoding="utf-16"?><root id="1"><a>text &amp; more</a></root>

        // S2: OmitXmlDeclaration + Indent -- formatted tree.
        var sb2 = new StringBuilder();
        XmlWriterSettings s2 = new XmlWriterSettings();
        s2.OmitXmlDeclaration = true;
        s2.Indent = true;
        XmlWriter w2 = XmlWriter.Create(sb2, s2);
        w2.WriteStartElement("r");
        w2.WriteElementString("x", "v");
        w2.WriteEndElement();
        w2.Close();
        string t2 = sb2.ToString();
        Console.WriteLine("indent=" + t2.Contains("\r\n  <x>")); // True (net48 CRLF + 2-space indent)
        Console.WriteLine("xml_decl=" + t2.StartsWith("<r>"));   // True (declaration omitted)

        // S3: entity escaping in attribute + text values.
        var sb3 = new StringBuilder();
        XmlWriterSettings s3 = new XmlWriterSettings();
        s3.OmitXmlDeclaration = true;
        XmlWriter w3 = XmlWriter.Create(sb3, s3);
        w3.WriteStartElement("e");
        w3.WriteAttributeString("attr", "a<b>\"c\" & 'd'");
        w3.WriteString("x<y>&z");
        w3.WriteEndElement();
        w3.Close();
        string t3 = sb3.ToString();
        Console.WriteLine("attr_esc=" + t3.Contains("attr=\"a&lt;b&gt;&quot;c&quot; &amp; 'd'\"")); // True
        Console.WriteLine("text_esc=" + t3.Contains("x&lt;y&gt;&amp;z"));                        // True
    }
}}
