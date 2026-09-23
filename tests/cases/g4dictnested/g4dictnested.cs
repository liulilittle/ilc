using System;
using System.Collections.Generic;

// g4dictnested -- G4 probe: Dictionary<string,List<int>> (nested value type).
// Expected red until the nested ref-value arm lands. The probe exercises the
// TRACKED receiver chain (d["a"] returns List<int>, then .Add/.Count/[i] on
// the returned list), plus the dictss surface (Add/get_Item/set_Item/
// ContainsKey/Remove/Count). NO key enumeration (bucket order is an
// implementation detail the oracle fixes; Keys-as-string[] is covered by the
// unit spec).
class Prog
{
    static void Main()
    {
        Dictionary<string, List<int>> d = new Dictionary<string, List<int>>();
        d.Add("a", new List<int>());
        d.Add("b", new List<int>());
        d["a"].Add(1);
        d["a"].Add(2);
        d["b"].Add(9);
        Console.WriteLine("count=" + d.Count);
        Console.WriteLine("acount=" + d["a"].Count);
        Console.WriteLine("a0=" + d["a"][0]);
        Console.WriteLine("a1=" + d["a"][1]);
        Console.WriteLine("b0=" + d["b"][0]);
        Console.WriteLine("hasb=" + d.ContainsKey("b"));
        Console.WriteLine("removed_b=" + d.Remove("b"));
        Console.WriteLine("count2=" + d.Count);
        Console.WriteLine("hasb2=" + d.ContainsKey("b"));
        Console.WriteLine("acount2=" + d["a"].Count);

        List<int> la = d["a"];
        la.Add(3);
        Console.WriteLine("a2=" + la[2]);
        Console.WriteLine("done");
    }
}
