//------------------------------------------------------------------------------
// rlaudit4.cs -- multicast invocation + Delegate.Equals probes.
// net48: f() = "B" (all leaves run, the last result wins);
//        a1.Equals(a2) = False (different methods);
//        dx.Equals(dy) = False (different delegate TYPES, same target method;
//        Delegate.Equals/InternalEqualTypes compares the runtime type).
// runtime: ref-returning multicast shapes fall back to raw methodPtr
// (the container's `first` is read as code -> access violation), and
// icall_delegate_equals compares methodPtr+target only, ignoring the type.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Rlaudit4 {
    public delegate void DelX();
    public delegate void DelY();
    private static void Method1() { }
    private static void Method2() { }
    private static void Shared() { }

    public static void Main()
    {
        try
        {
            Func<string> f = () => "A";
            f += () => "B";
            Console.WriteLine("mc=" + f());
        }
        catch (Exception ex)
        {
            Console.WriteLine("mc_ex=" + ex.GetType().Name);
        }

        try
        {
            Action a1 = Method1;
            Action a2 = Method2;
            Console.WriteLine("eq1=" + a1.Equals(a2));
        }
        catch (Exception ex)
        {
            Console.WriteLine("eq1_ex=" + ex.GetType().Name);
        }

        try
        {
            DelX dx = Shared;
            DelY dy = Shared;
            Console.WriteLine("eq2=" + dx.Equals(dy));
        }
        catch (Exception ex)
        {
            Console.WriteLine("eq2_ex=" + ex.GetType().Name);
        }
    }
} }
