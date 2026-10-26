//------------------------------------------------------------------------------
// loud-linq-zipdec.cs -- e2e reference case for Enumerable.Zip with a
// System.Decimal ELEMENT source (net48 Zip<TSource,TSecond,TResult> where
// TSource or TSecond is decimal).
//
// The element crosses as the 16-byte runtime value struct BY VALUE (the decimal
// T3 contract), so the selector's declared parameter type is
// il2cpp::runtime::Il2CppDecimal (IL2CPP_LIST_KIND_DEC / IL2CPP_LINQ_ZIP_DEC).
// Before this change the shape LOUD-rejected ("an element family with no cell
// ABI").
//
// Also covers the reference-TResult Zip over a decimal source (resKind 6), a
// pre-existing gap in the wide Zip path (the ref cell was written as int32).
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqZipDec
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m };
            int[] b = { 10, 20, 30 };

            // decimal first source, int32 second source, decimal result
            var z = a.Zip(b, (x, y) => x + y).ToArray();
            Console.WriteLine("dec=" + D(z[0]) + "," + D(z[1]) + "," + D(z[2]));

            // int32 first source, decimal second source, decimal result
            var z2 = b.Zip(a, (x, y) => x * y).ToArray();
            Console.WriteLine("dec2=" + D(z2[0]) + "," + D(z2[1]) + "," + D(z2[2]));

            // decimal / decimal
            var z3 = a.Zip(a, (x, y) => x + y).ToArray();
            Console.WriteLine("dec3=" + D(z3[0]) + "," + D(z3[1]) + "," + D(z3[2]));

            // decimal source, REFERENCE result
            var z4 = a.Zip(b, (x, y) => x > 1m ? "big" : "small").ToArray();
            Console.WriteLine("ref=" + z4[0] + "," + z4[1]);

            Console.WriteLine("done");
        }
    }
}
