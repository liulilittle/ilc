using System;
using System.Globalization;

namespace Cases { public static class Saudit2 {
    public static void Main()
    {
        try
        {
            double v = double.Parse("1,000");
            Console.WriteLine("dp_1000=" + v);
        }
        catch (Exception ex)
        {
            Console.WriteLine("dp_1000=EX:" + ex.GetType().Name);
        }
        try
        {
            int v = int.Parse("\u00A0123");
            Console.WriteLine("ip_nbsp=" + v);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ip_nbsp=EX:" + ex.GetType().Name);
        }
    }
}}
