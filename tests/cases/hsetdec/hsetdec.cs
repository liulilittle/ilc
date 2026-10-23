using System;
using System.Collections.Generic;

class Prog
{
    static void Main()
    {
        HashSet<decimal> de = new HashSet<decimal>();
        de.Add(1.5m); de.Add(2.75m); de.Add(1.50m);
        string sde = "";
        foreach (decimal v in de) sde += v + ",";
        Console.WriteLine("decimal=" + de.Count + "|" + sde);
        Console.WriteLine("decimal_has=" + de.Contains(1.500m));
    }
}
