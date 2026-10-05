using System;
using System.Globalization;

namespace Cases { public static class Saudit4 {
    public static void Main()
    {
        Console.WriteLine("intC_inv=" + 1234.ToString("C", CultureInfo.InvariantCulture));
        Console.WriteLine("fmt_inv=" + string.Format(CultureInfo.InvariantCulture, "{0:C}", 12.5));
        string cs = NumberFormatInfo.InvariantInfo.CurrencySymbol;
        Console.WriteLine("inv_cur_sym=" + cs);
        Console.WriteLine("inv_cur_sym_cp=" + (int)cs[0]);
    }
}}
