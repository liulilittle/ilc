//------------------------------------------------------------------------------
// xmaudit1.cs -- XML audit probe 1 (G6 static audit, points 1/3): embedded
// U+0000 and an illegal control character on XmlDocument.LoadXml.
//
// net48 baseline (ORACLE-VERIFIED, expected.txt): BOTH inputs are ACCEPTED.
//   * P1: net48 truncates at the embedded NUL too and serializes `<a />`
//     (self-closing WITH a space); runtime truncates via the same ldstr/parse
//     NUL cut (stringintern.cpp:334 strlen; xml_doc.cpp:276-278 /
//     xml_parse.cpp:711) and emits `<a/>`.
//   * P2: net48 decodes &#1; and re-escapes it as `&#x1;`; runtime decodes it
//     and emits the RAW U+0001 byte (xml_parse.cpp:266 rejects only #0 and
//     surrogates; xml_doc.cpp:147-169 escapes only & < >).
// This is a PERMANENT DIVERGENCE PIN: transpile/build must PASS; the nat-vs-
// runtime diff is the recorded evidence (audit prediction "both throw
// XmlException" was DISPROVED by the oracle).
//
// Output discipline: labels + exact OuterXml; no exception-object introspection
// (typed catch clauses only) so the generated C++ stays mapped.
//------------------------------------------------------------------------------
using System;
using System.Xml;

namespace Cases { public static class XmAudit1 {
    public static void Main() {
        // P1: embedded NUL -- net48: XmlException; runtime (audited): truncation.
        XmlDocument d1 = new XmlDocument();
        try {
            d1.LoadXml("<a/>\0<b/>");
            Console.WriteLine("p1_load=ok");
            Console.WriteLine("p1_outer=[" + d1.OuterXml + "]");
        } catch (XmlException) {
            Console.WriteLine("p1_ex=XmlException");
        } catch (Exception) {
            Console.WriteLine("p1_ex=OtherException");
        }

        // P2: &#1; -- net48: XmlException; runtime (audited): U+0001 in text.
        XmlDocument d2 = new XmlDocument();
        try {
            d2.LoadXml("<a>&#1;</a>");
            Console.WriteLine("p2_load=ok");
            string o2 = d2.OuterXml;
            Console.WriteLine("p2_outer=[" + o2 + "]");
            Console.WriteLine("p2_len=" + o2.Length);
        } catch (XmlException) {
            Console.WriteLine("p2_ex=XmlException");
        } catch (Exception) {
            Console.WriteLine("p2_ex=OtherException");
        }
    }
}}
