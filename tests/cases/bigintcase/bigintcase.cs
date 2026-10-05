//------------------------------------------------------------------------------
// bigintcase.cs -- System.Numerics.BigInteger e2e oracle. The transpiler's
// biginteger domain (il2cpp_biginteger.h) wires the ctors, the arithmetic
// operators, comparison/equality, ToString and the explicit int32 conversion.
// Every output is a deterministic bool; expected.txt is generated from a
// net48 reference run.
//------------------------------------------------------------------------------

using System;
using System.Numerics;

class BigIntCase
{
    static void Main()
    {
        // ---- 1. construction + arithmetic ----
        BigInteger a = new BigInteger(5);
        BigInteger b = new BigInteger(3);
        BigInteger sum = a + b;
        Console.WriteLine(sum.ToString() == "8");
        BigInteger diff = a - b;
        Console.WriteLine(diff.ToString() == "2");
        BigInteger prod = a * b;
        Console.WriteLine(prod.ToString() == "15");
        BigInteger quot = a / b;
        Console.WriteLine(quot.ToString() == "1");
        BigInteger rem = a % b;
        Console.WriteLine(rem.ToString() == "2");
        BigInteger neg = -a;
        Console.WriteLine(neg.ToString() == "-5");

        // ---- 2. large numbers: 50! ----
        BigInteger big = new BigInteger(1);
        for (int i = 2; i <= 50; i++) { big = big * new BigInteger(i); }
        Console.WriteLine(big.ToString() == "30414093201713378043612608166064768844377641568960512000000000000");

        // ---- 3. comparison / equality ----
        Console.WriteLine(a.CompareTo(b) > 0);
        Console.WriteLine(a.Equals(new BigInteger(5)));
        Console.WriteLine(a.Equals(b) == false);

        // ---- 4. explicit int32 conversion ----
        Console.WriteLine((int)a == 5);
    }
}
