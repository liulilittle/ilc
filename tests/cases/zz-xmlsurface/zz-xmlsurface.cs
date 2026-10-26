//------------------------------------------------------------------------------
// zz-xmlsurface -- the XmlWriter + XmlDocument member surface:
//   XmlWriter.WriteStartDocument() / WriteFullEndElement / WriteCData /
//     WriteComment / WriteWhitespace / WriteEntityRef / WriteRaw /
//     get_WriteState
//   XmlDocument.get_DocumentElement / XmlNode.getFirstChild / get_Name /
//     get_NodeType
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Cases
{
    public static class XmlSurface
    {
        public static void Main()
        {
            var sb = new StringBuilder();
            var w = XmlWriter.Create(sb);
            w.WriteStartDocument();
            w.WriteStartElement("root");
            w.WriteAttributeString("id", "1");
            w.WriteCData("hello <world>");
            w.WriteComment("a comment");
            w.WriteWhitespace("  ");
            w.WriteEntityRef("amp");
            w.WriteRaw("<b/>");
            w.WriteFullEndElement();
            w.WriteEndDocument();
            w.Close();
            Console.WriteLine(sb.ToString());

            // The StringWriter target: the runtime writes into its backing
            // StringBuilder, so StringWriter.ToString() returns the markup.
            var sw = new StringWriter();
            var w2 = XmlWriter.Create(sw);
            w2.WriteStartDocument();
            w2.WriteStartElement("root");
            w2.WriteAttributeString("id", "1");
            w2.WriteFullEndElement();
            w2.WriteEndDocument();
            w2.Close();
            Console.WriteLine(sw.ToString());

            var doc = new XmlDocument();
            doc.LoadXml("<root><a>1</a></root>");
            Console.WriteLine(doc.DocumentElement.Name);
            Console.WriteLine(doc.DocumentElement.FirstChild.Name);
            Console.WriteLine((int)doc.DocumentElement.NodeType);
        }
    }
}
