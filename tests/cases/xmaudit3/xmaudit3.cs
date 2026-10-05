//------------------------------------------------------------------------------
// xmaudit3.cs -- XML audit probe 3 (G6 static audit, points 2/7): namespace
// semantics in XPath.
//
// net48 baseline (XPath 1.0): an unprefixed node test matches nodes with an
// EMPTY namespace URI, so on <root xmlns="http://e"><a>1</a></root> the path
// /root/a has 0 hits. A prefixed path (/ns:root) with no NamespaceManager
// throws XPathException.
// Static audit of this runtime (xml_query.cpp:179-183 literal QName compare;
// xmlns is an ordinary attribute) predicts: P1 hits the literal "root"/"a"
// names -> 1; P2 simply misses (literal "ns:root" != "root") -> 0, no throw.
//
// WIRING NOTE (same as xmaudit2): SelectNodes is ReturnsValue=false
// (MethodMap.Xml.cs:93) -> consuming the result is a LOUD transpile reject.
//
// Output discipline: typed catch clauses only; no exception introspection.
//------------------------------------------------------------------------------
using System;
using System.Xml;
using System.Xml.XPath;

namespace Cases { public static class XmAudit3 {
    public static void Main() {
        XmlDocument d = new XmlDocument();
        d.LoadXml("<root xmlns=\"http://e\"><a>1</a></root>");

        // P1: unprefixed path vs the default namespace.
        try {
            XmlNodeList l1 = d.SelectNodes("/root/a");
            Console.WriteLine("p1_count=" + l1.Count);
        } catch (XPathException) {
            Console.WriteLine("p1_ex=XPathException");
        } catch (Exception) {
            Console.WriteLine("p1_ex=OtherException");
        }

        // P2: prefixed path with no NamespaceManager.
        try {
            XmlNodeList l2 = d.SelectNodes("/ns:root");
            Console.WriteLine("p2_count=" + l2.Count);
        } catch (XPathException) {
            Console.WriteLine("p2_ex=XPathException");
        } catch (Exception) {
            Console.WriteLine("p2_ex=OtherException");
        }
    }
}}
