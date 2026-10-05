using System;
using System.Globalization;

namespace Cases { public static class Saudit1 {
    public static void Main()
    {
        Console.WriteLine("inv_G=" + double.PositiveInfinity.ToString("G", CultureInfo.InvariantCulture));
        Console.WriteLine("def_tos=" + double.PositiveInfinity.ToString());
        Console.WriteLine("cur_sym=" + NumberFormatInfo.CurrentInfo.PositiveInfinitySymbol);
    }
}}
