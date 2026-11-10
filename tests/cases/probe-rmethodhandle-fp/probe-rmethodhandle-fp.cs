using System;
using System.Reflection;
using System.Runtime.CompilerServices;

class P
{
    public static int Add(int a, int b) { return a + b; }
    public static int Mul(int a, int b) { return a * b; }
    public int Instance(int a) { return a; }

    static void Main()
    {
        // 1) MethodInfo.MethodHandle.GetFunctionPointer() for a static method.
        var mi = typeof(P).GetMethod("Add");
        var fp = mi.MethodHandle.GetFunctionPointer();
        Console.WriteLine("[add.nonzero] " + (fp.ToInt64() != 0));
        Console.WriteLine("[add.eq] " + (fp == typeof(P).GetMethod("Add").MethodHandle.GetFunctionPointer()));
        Console.WriteLine("[add.ne.mul] " + (fp != typeof(P).GetMethod("Mul").MethodHandle.GetFunctionPointer()));

        // 2) An instance method.
        var imi = typeof(P).GetMethod("Instance");
        var ifp = imi.MethodHandle.GetFunctionPointer();
        Console.WriteLine("[inst.nonzero] " + (ifp.ToInt64() != 0));
        Console.WriteLine("[inst.ne.add] " + (ifp != fp));

        // 3) A zero handle -> ArgumentNullException (net48 Arg_InvalidHandle).
        try
        {
            var h = default(RuntimeMethodHandle);
            var v = h.GetFunctionPointer();
            Console.WriteLine("[zero] none:" + v);
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("[zero] ArgumentNullException");
        }
        catch (Exception e)
        {
            Console.WriteLine("[zero] other:" + e.GetType().Name);
        }

        // 4) ldtoken -> RuntimeMethodHandle -> GetFunctionPointer.
        var ldt = (RuntimeMethodHandle)typeof(P).GetMethod("Add").MethodHandle;
        Console.WriteLine("[ldtoken.eq] " + (ldt.GetFunctionPointer() == fp));

        // 5) The entry address is the AOT function address, NOT the handle
        // (typeinfo table entry) address -- the two are different objects.
        Console.WriteLine("[fp.ne.handle] " + (fp.ToInt64() != mi.MethodHandle.Value.ToInt64()));
    }
}
