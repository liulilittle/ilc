//------------------------------------------------------------------------------
// xmaudit2.cs -- XML audit probe 2 (G6 static audit, points 2/5): XPath
// descendant-axis root inclusion and positional-predicate context.
//
// net48 baseline:
//   * SelectNodes("//root") on <root/> returns the document element itself
//     (// is /descendant-or-self::node()/child::root from the document node).
//   * SelectNodes("/r//x[2]") applies [2] PER PARENT: on
//     <r><a><x>1</x></a><b><x>2</x><x>3</x></b></r> it hits b's 2nd x -> "3".
// Static audit of this runtime (xml_query.cpp:263-266 descendant-only;
// xml_query.cpp:277-283 [n] over the merged per-candidate match list) predicts
// 0 hits for P1 and "2" for P2.
//
// WIRING NOTE (xml-b23; xpathdeep GAPS.md section 1): SelectNodes is mapped
// ReturnsValue=false (MethodMap.Xml.cs:93), so consuming the result is a LOUD
// transpile reject (DiscardedValueExpr). If this case fails stage=transpile,
// that is the consumption block, NOT the XPath semantics; the semantics probe
// activates the day the mapping flips rv=true.
//
// Output discipline: typed catch clauses only; no exception introspection.
//------------------------------------------------------------------------------
using System;
using System.Xml;
using System.Xml.XPath;

namespace Cases { public static class XmAudit2 {
    public static void Main() {
        // P1: //root vs the document element itself.
        XmlDocument d1 = new XmlDocument();
        d1.LoadXml("<root/>");
        try {
            XmlNodeList l1 = d1.SelectNodes("//root");
            Console.WriteLine("p1_count=" + l1.Count);
        } catch (Exception) {
            Console.WriteLine("p1_ex=Exception");
        }

        // P2: //x[2] positional-predicate context.
        XmlDocument d2 = new XmlDocument();
        d2.LoadXml("<r><a><x>1</x></a><b><x>2</x><x>3</x></b></r>");
        try {
            XmlNodeList l2 = d2.SelectNodes("/r//x[2]");
            Console.WriteLine("p2_count=" + l2.Count);
            Console.WriteLine("p2_text=" + (l2.Count > 0 ? l2[0].InnerText : "none"));
        } catch (Exception) {
            Console.WriteLine("p2_ex=Exception");
        }
    }
}}
