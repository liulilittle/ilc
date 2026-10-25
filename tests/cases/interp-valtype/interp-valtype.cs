using System;

//------------------------------------------------------------------------------
// interp-valtype -- DefaultInterpolatedStringHandler.AppendFormatted<T> over a
// generic VALUE type (decimal / DateTime / Guid): the 1/2/3-argument forms
// (plain, alignment, format string). The net8+ interpolation lowering calls
// these; the runtime boxes the value slot and renders it through the composite
// engine. Byte-exact against the net8 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class InterpValType
    {
        public static void Main()
        {
            decimal m = 1.2345m;
            Console.WriteLine($"dec1=[{m}]");
            Console.WriteLine($"dec2=[{m,7}]");
            Console.WriteLine($"dec3=[{m,7:N2}]");
            Console.WriteLine($"dec4=[{m:N2}]");

            DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5);
            Console.WriteLine($"dt1=[{dt}]");
            Console.WriteLine($"dt2=[{dt,8}]");
            Console.WriteLine($"dt3=[{dt,8:yyyy}]");

            Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Console.WriteLine($"guid1=[{g}]");
            Console.WriteLine($"guid2=[{g,8}]");
            Console.WriteLine($"guid3=[{g,8:D}]");

            // mixed with a primitive on the same line
            int x = 5;
            Console.WriteLine($"mix=[{x:D3}|{m:N2}|{dt:yyyy}]");
        }
    }
}
