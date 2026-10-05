using System;
using System.Xml;
namespace Cases { public static class XmlCase {
    public static void Main() {
        // XML basic domain (handle-gap): XmlDocument.LoadXml -> OuterXml round trip.
        // net48-shape discipline: NO comments/declarations/empty elements in the
        // inputs (the runtime drops comments+declarations and renders empty
        // elements self-closing -- net48 keeps comments and emits "<a />").
        XmlDocument doc = new XmlDocument();
        doc.LoadXml("<root><a>1</a></root>");
        Console.WriteLine(doc.OuterXml);                            // <root><a>1</a></root>
        Console.WriteLine(doc.OuterXml == "<root><a>1</a></root>"); // True
        doc.LoadXml("<root x=\"1\" y=\"two\"><c>text</c></root>");
        Console.WriteLine(doc.OuterXml == "<root x=\"1\" y=\"two\"><c>text</c></root>"); // True
        doc.LoadXml("<root>a &amp; b &lt;c&gt;</root>");
        Console.WriteLine(doc.OuterXml == "<root>a &amp; b &lt;c&gt;</root>"); // True
        doc.LoadXml("<root><![CDATA[a < b & c]]></root>");
        Console.WriteLine(doc.OuterXml == "<root><![CDATA[a < b & c]]></root>"); // True
        doc.LoadXml("<root>  pre  </root>");
        Console.WriteLine(doc.OuterXml == "<root>  pre  </root>");   // True
        XmlDocument doc2 = new XmlDocument();
        doc2.LoadXml("<r><n>7</n></r>");
        Console.WriteLine(doc2.OuterXml);                            // <r><n>7</n></r>
    }
}}
