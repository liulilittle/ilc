using System;

// M4 Task 7 C API fixture: a type whose MAIN-class static methods include
// the export-table signature int32(int32,int32,int32,int32) -- the only
// signature the C API can invoke (M4 contract). Add4(20,22,0,0) == 42 is
// the capi smoke test's target call.
public class Program
{
    public static int Add4(int a, int b, int c, int d)
    {
        return a + b + c + d;
    }

    // C API exception boundary: an explicit managed throw. The throw is
    // conditional so the method also has a return path (the transpiled C++
    // function must not fall off the end -- -Wreturn-type), and all four
    // parameters participate (the generated export must consume its
    // arguments -- -Wunused-parameter). Throw4(1,2,3,4) satisfies the
    // condition (1+2+3+4 == 10) and throws.
    public static int Throw4(int a, int b, int c, int d)
    {
        if (a + b + c + d == 10)
        {
            throw new System.Exception("boom");
        }
        return a + b + c + d;
    }

    // C API exception boundary: integer division by zero throws
    // DivideByZeroException (checked division). The trailing arguments are
    // part of the export signature; c - d folds to zero at the tested call
    // sites (10,2,0,0) and (10,0,0,0).
    public static int Div4(int a, int b, int c, int d)
    {
        return a / b + c - d;
    }

    public static int Main()
    {
        Console.WriteLine(Add4(20, 22, 0, 0));
        return 0;
    }
}
