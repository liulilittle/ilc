using System;

namespace Cases { public static class Saudit3 {
    public static void Main()
    {
        string u1 = "ä".ToUpper();
        Console.WriteLine("upper_ae_val=" + u1);
        Console.WriteLine("upper_ae_eq=" + (u1 == "Ä"));
        string l1 = "Δ".ToLower();
        Console.WriteLine("lower_delta_val=" + l1);
        Console.WriteLine("lower_delta_eq=" + (l1 == "δ"));
        string s1 = "\u00DF".ToUpper();
        Console.WriteLine("upper_sharp_val=" + s1);
        Console.WriteLine("upper_sharp_eq=" + (s1 == "\u00DF"));
    }
}}
