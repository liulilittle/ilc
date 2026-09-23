using System;
using System.Collections;
using System.Xml;

//------------------------------------------------------------------------------
// xmlenumops -- dual-side probe (net48 oracle + ilc e2e) for the XmlNodeList
// enumerator surface: foreach over SelectNodes(...).
//
// Covered: document order + count, expression receiver, empty set, single
// node, re-enumeration (fresh enumerator per foreach), explicit IEnumerator
// MoveNext, non-null GetEnumerator on an empty list, Current before the first
// MoveNext and after exhaustion (InvalidOperationException contract).
//
// NOT covered here (net48-only oracle, xmlenum_mutate_oracle): document
// mutation timing (snapshot vs lazy), Reset, IDisposable, type name -- the
// ilc xml domain has no mutation surface and Reset/IDisposable are outside
// the wired foreach surface.
//
// Deterministic output; identical byte-for-byte on net48 and ilc.
//------------------------------------------------------------------------------
class Prog
{
    static void Main()
    {
        XmlDocument d = new XmlDocument();
        d.LoadXml("<r><x>1</x><x>2</x><x>3</x></r>");

        // E1: document order + count, typed foreach over a local
        XmlNodeList l = d.SelectNodes("/r/x");
        int n1 = 0;
        string t1 = "";
        foreach (XmlNode node in l)
        {
            n1++;
            t1 += node.InnerText;
        }
        Console.WriteLine("e1_n=" + n1 + " text=" + t1);

        // E2: directly in the foreach header (expression receiver)
        int n2 = 0;
        foreach (XmlNode node in d.SelectNodes("/r/x"))
        {
            n2++;
        }
        Console.WriteLine("e2_header_n=" + n2);

        // E3: empty node set
        XmlNodeList empty = d.SelectNodes("/r/none");
        int n3 = 0;
        foreach (XmlNode node in empty)
        {
            n3++;
        }
        Console.WriteLine("e3_empty=" + n3 + " count=" + empty.Count);

        // E4: single node
        int n4 = 0;
        string t4 = "";
        foreach (XmlNode node in d.SelectNodes("/r/x[1]"))
        {
            n4++;
            t4 += node.InnerText;
        }
        Console.WriteLine("e4_single=" + n4 + " text=" + t4);

        // E5: re-enumeration (fresh enumerator per foreach)
        int n5 = 0;
        foreach (XmlNode node in l) n5++;
        foreach (XmlNode node in l) n5++;
        Console.WriteLine("e5_twice=" + n5);

        // E6: explicit IEnumerator: MoveNext count
        IEnumerator ie = l.GetEnumerator();
        int n6 = 0;
        while (ie.MoveNext())
        {
            n6++;
        }
        Console.WriteLine("e6_movenext=" + n6);

        // E7: GetEnumerator on an empty list is non-null
        IEnumerator ee = empty.GetEnumerator();
        Console.WriteLine("e7_nonnull=" + (ee != null));

        // E8: Current before the first MoveNext
        IEnumerator pe = d.SelectNodes("/r/x").GetEnumerator();
        try
        {
            object before = pe.Current;
            Console.WriteLine("e8_before=" + (before == null ? "null" : "obj"));
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("e8_before=InvalidOperationException");
        }
        catch (Exception)
        {
            Console.WriteLine("e8_before=Exception");
        }

        // E9: Current after exhaustion
        IEnumerator xe = d.SelectNodes("/r/x").GetEnumerator();
        while (xe.MoveNext()) { }
        try
        {
            object after = xe.Current;
            Console.WriteLine("e9_after=" + (after == null ? "null" : "obj"));
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("e9_after=InvalidOperationException");
        }
        catch (Exception)
        {
            Console.WriteLine("e9_after=Exception");
        }

        Console.WriteLine("done");
    }
}
