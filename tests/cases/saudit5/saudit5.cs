using System;

namespace Cases { public static class Saudit5 {
    public static void Main()
    {
        DateTime dt = new DateTime(2024, 1, 2, 15, 4, 5);
        Console.WriteLine("dt_def=" + dt.ToString());
        Console.WriteLine("dt_d=" + dt.ToString("d"));
        Console.WriteLine("dt_D=" + dt.ToString("D"));
    }
}}
