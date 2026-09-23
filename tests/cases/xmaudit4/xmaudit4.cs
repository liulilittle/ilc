//------------------------------------------------------------------------------
// xmaudit4.cs -- XML audit probe 4 (G6 static audit, points 4/5): XmlWriter
// empty-element byte shape and use-after-Close lifecycle.
//
// net48 baseline: WriteStartElement+WriteEndElement with no content renders
// self-closing ("<a />", with a space); Close is idempotent; WriteStartElement
// after Close throws (ObjectDisposedException/InvalidOperationException -- the
// oracle output pins the actual type).
// Static audit of this runtime (xml_write.cpp:280-287 no-content emits
// "<name></name>"; xml_write.cpp:327-341 Close deletes the writer struct with
// no idempotency guard) predicts: P1 differs byte-for-byte and P2/P3 are
// use-after-free (crash or garbage -- the truncated/diff output is evidence).
// This is a PERMANENT DIVERGENCE PIN.
//
// Output discipline: exact output lines + typed catch clauses only.
//------------------------------------------------------------------------------
using System;
using System.Text;
using System.Xml;

namespace Cases { public static class XmAudit4 {
    public static void Main() {
        // P1: empty-element byte shape.
        StringBuilder sb = new StringBuilder();
        XmlWriter w = XmlWriter.Create(sb);
        w.WriteStartElement("a");
        w.WriteEndElement();
        w.Close();
        Console.WriteLine("p1=[" + sb.ToString() + "]");

        // P2: second Close.
        try {
            w.Close();
            Console.WriteLine("p2=none");
        } catch (ObjectDisposedException) {
            Console.WriteLine("p2=ObjectDisposedException");
        } catch (InvalidOperationException) {
            Console.WriteLine("p2=InvalidOperationException");
        } catch (Exception) {
            Console.WriteLine("p2=Exception");
        }

        // P3: write after Close.
        try {
            w.WriteStartElement("b");
            Console.WriteLine("p3=none");
        } catch (ObjectDisposedException) {
            Console.WriteLine("p3=ObjectDisposedException");
        } catch (InvalidOperationException) {
            Console.WriteLine("p3=InvalidOperationException");
        } catch (Exception) {
            Console.WriteLine("p3=Exception");
        }
    }
}}
