//------------------------------------------------------------------------------
// decimalcase.cs -- System.Decimal e2e oracle (enabled after T1-T3 land).
// The transpiler's decimal domain is not wired yet (no decimal literal / ctor /
// operator emission paths); this case is built now to freeze the semantic
// expectation: every output is a deterministic bool and expected.txt is
// generated from a net48 reference run.
//   T1 = literals + locals + comparison     T2 = ToString/Parse known values
//   T3 = decimal->int conversion + precision chain (100m/7m does not loop,
//        *7 goes over 100m)
//------------------------------------------------------------------------------

using System;
namespace Cases { public static class DecimalCase {
    // Constant declarations: const decimal literals (compile-time constants;
    // the m suffix is required beyond 5 decimal places)
    private const decimal Euler = 2.7182818284590452353602874713m;
    public static void Main() {
        // ---- 1. constant declarations / literal identity ----
        decimal zero = 0m;
        decimal one = 1m;
        decimal pi = 3.1415926535897932384626433832m;
        Console.WriteLine(zero == 0m);
        Console.WriteLine(one == 1m);
        Console.WriteLine(pi == 3.1415926535897932384626433832m);
        Console.WriteLine(pi > 3.14m);
        Console.WriteLine(Euler > 2.7m);

        // ---- 2. addition: 0.1m+0.2m==0.3m (decimal is exact, no double error) ----
        Console.WriteLine(0.1m + 0.2m == 0.3m);

        // ---- 3. multiplication ----
        Console.WriteLine(1.5m * 2m == 3m);
        Console.WriteLine(0.1m * 0.2m == 0.02m);

        // ---- 4. division / modulo precision ----
        Console.WriteLine(100m / 7m > 14.28m && 100m / 7m < 14.29m);
        Console.WriteLine(10m / 4m == 2.5m);
        Console.WriteLine(10m % 3m == 1m);
        // Precision chain: 100m/7m keeps 29 significant digits, so *7 does not
        // return to 100m (reference the net48 output).
        Console.WriteLine(100m / 7m * 7m == 100m);

        // ---- 5. ToString known values (after T2) ----
        Console.WriteLine(1.5m.ToString() == "1.5");
        Console.WriteLine((0.1m + 0.2m).ToString() == "0.3");

        // ---- 6. decimal.Parse (after T2) ----
        Console.WriteLine(decimal.Parse("3.14") == 3.14m);
        Console.WriteLine(decimal.Parse("-0.5") == -0.5m);
        Console.WriteLine(decimal.Parse("123456789.123456789") == 123456789.123456789m);

        // ---- 7. decimal -> int conversion (truncation toward zero) ----
        Console.WriteLine((int)2.7m == 2);
        Console.WriteLine((int)(-2.7m) == -2);
        Console.WriteLine((int)0.9999m == 0);
        Console.WriteLine((int)100m == 100);

        // ---- 8. comparison ----
        Console.WriteLine(1.1m < 1.2m);
        Console.WriteLine(1.2m <= 1.2m);
        Console.WriteLine(2m > 1.9m);
        Console.WriteLine(2m >= 2.1m);
        Console.WriteLine(1m != 2m);
        Console.WriteLine(1.0m == 1.00m);      // comparison ignores scale
        Console.WriteLine(-3.5m < 0m);
        Console.WriteLine(-3.5m == -(3.5m));
        Console.WriteLine(1m + 2 == 3m);       // int implicitly converts to decimal
    }
}}
