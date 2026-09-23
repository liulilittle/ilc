//------------------------------------------------------------------------------
// rlaudit2.cs -- Regex anchor-phantom defect probe (regex_api.cpp
// CollectMatches phantom test). Pattern "^|," contains a start anchor in ONE
// alternative; the suffix-slice search then treats every later slice-0 match
// as a phantom and drops it.
// net48: Replace("a,b", "^|,", "_") = "_a_b"; Split -> 3 segments
//       ["", "a", "b"].
// runtime: only the empty ^ match at 0 survives; the comma match is lost.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases { public static class Rlaudit2 {
    public static void Main()
    {
        Console.WriteLine("rep=" + Regex.Replace("a,b", "^|,", "_"));

        string[] parts = Regex.Split("a,b", "^|,");
        Console.WriteLine("n=" + parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            Console.WriteLine("p" + i + "=[" + parts[i] + "]");
        }
    }
} }
