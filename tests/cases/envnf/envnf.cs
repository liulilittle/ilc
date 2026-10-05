using System;
using System.Globalization;
class P
{
    static void Main()
    {
        Console.WriteLine("env1=" + (Environment.GetEnvironmentVariable("NONEXIST_VAR_X9QQ") == null));
        Console.WriteLine("env2=" + (Environment.Is64BitOperatingSystem == (IntPtr.Size == 8)));
        Console.WriteLine("env3=" + (Environment.ProcessorCount >= 1));
        Console.WriteLine("nf0=" + ((-1234).ToString("N0", CultureInfo.InvariantCulture) == "-1,234"));
        Console.WriteLine("nf1=" + ((12345.678).ToString("N2", CultureInfo.InvariantCulture) == "12,345.68"));
        Console.WriteLine("nf2=" + (string.Format(CultureInfo.InvariantCulture, "{0:N2}", 99999.99) == "99,999.99"));
        
        Console.WriteLine("nf4=" + ((1234.5678).ToString("E2", CultureInfo.InvariantCulture) == "1.23E+003"));
        Console.WriteLine("nf5=" + ((255).ToString("X4") == "00FF"));
        Console.WriteLine("nf6=" + ((7).ToString("D3") == "007"));
        Console.WriteLine("nf7=" + ((0.24f).ToString("F1", CultureInfo.InvariantCulture) == "0.2"));
        Console.WriteLine("nf8=" + ((255L).ToString("X") == "FF"));
        Console.WriteLine("nf9=" + ((3.5m).ToString("N2", CultureInfo.InvariantCulture) == "3.50"));
        
    }
}
