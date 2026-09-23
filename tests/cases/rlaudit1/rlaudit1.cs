//------------------------------------------------------------------------------
// rlaudit1.cs -- Regex defect probe: an unmatched ')' must be an
// ArgumentException. net48: Regex.IsMatch("a", "a)") throws ArgumentException
// ("Too many )"). The runtime parser (regex_parse.cpp RegexParse) never checks
// that the whole pattern was consumed, so 'a)' parses as 'a' -> True.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases { public static class Rlaudit1 {
    public static void Main()
    {
        try
        {
            Console.WriteLine("isimg=" + Regex.IsMatch("a", "a)"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("isimg_ex=" + ex.GetType().Name);
        }
    }
} }
