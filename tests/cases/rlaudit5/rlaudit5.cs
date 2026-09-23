//------------------------------------------------------------------------------
// rlaudit5.cs -- closure-capture probe: MakeAdder(3)(4) must be 7 (the lambda
// captures the parameter x; each MakeAdder call gets its own capture slot).
// runtime: capturing lambdas / compiler-generated closure classes are not
// modeled (delecb2/delechain gap) -- expect transpile or run failure.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Rlaudit5 {
    private static Func<int, int> MakeAdder(int x)
    {
        return y => x + y;
    }

    public static void Main()
    {
        try
        {
            Func<int, int> add3 = MakeAdder(3);
            Console.WriteLine("add=" + add3(4));
        }
        catch (Exception ex)
        {
            Console.WriteLine("add_ex=" + ex.GetType().Name);
        }
    }
} }
