using System;
using System.Xml;

// xmlnodelistops -- codegen-decl probe: the XmlNodeList surface of
// SelectNodes (Count / this[int] / chained InnerText) plus the net48 indexer
// error contract (index outside [0, Count) -> ArgumentOutOfRangeException
// with ParamName "index"). Deterministic output.
class Prog
{
    static void Main()
    {
        XmlDocument d = new XmlDocument();
        d.LoadXml("<r><x>1</x><x>2</x><x>3</x></r>");

        XmlNodeList l = d.SelectNodes("/r/x");
        Console.WriteLine("count=" + l.Count);
        Console.WriteLine("first=" + l[0].InnerText);
        Console.WriteLine("last=" + l[l.Count - 1].InnerText);

        // net48 XPathNodeList.Item: an index outside [0, Count) yields null
        // (NOT ArgumentOutOfRangeException) for a SelectNodes list.
        XmlNode oob = l[3];
        Console.WriteLine("oob=" + (oob == null ? "null" : oob.InnerText));
        XmlNode neg = l[-1];
        Console.WriteLine("neg=" + (neg == null ? "null" : neg.InnerText));

        Console.WriteLine("empty=" + d.SelectNodes("/r/none").Count);
        Console.WriteLine("done");
    }
}
