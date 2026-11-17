//------------------------------------------------------------------------------
// probe-excbase.cs -- a user-defined exception must sit in the exception
// subtree, so `is Exception` answers True.
//
// A user exception's base class lives in the BCL, and the loader only links
// types inside the translated set, so the emitted descriptor said
// baseClass = nullptr. il2cpp_is_instance walks that pointer, so
// `(object)new MyEx() is Exception` answered False where net48 says True.
//
// Every line must match the net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    // Direct subclass of Exception.
    public class MyEx : Exception
    {
        public MyEx() { }
    }

    // Two levels deep, to prove the chain walks through user types too.
    public sealed class MyDerivedEx : MyEx
    {
        public MyDerivedEx() { }
    }

    // Subclass of a modelled BCL exception.
    public sealed class MyArgEx : ArgumentException
    {
        public MyArgEx(string m) : base(m) { }
    }

    // Base is a BCL exception with NO runtime descriptor. This is the only
    // shape that reaches BclExceptionBaseDescriptor's IsBclExceptionName
    // fallback (the modelled bases above are matched inside the walk), so
    // without it a mutation of that fallback stays green.
    public sealed class MyBclDerived : InvalidOperationException
    {
        public MyBclDerived() { }
    }

    public static class ProbeExcBase
    {
        public static void Main()
        {
            object a = new MyEx();
            Console.WriteLine("ex.base_is_exception=" + (a is Exception));
            Console.WriteLine("ex.base_is_object=" + (a is object));
            Console.WriteLine("ex.base_is_system=" + (a is SystemException));

            object b = new MyDerivedEx();
            Console.WriteLine("ex.derived_is_exception=" + (b is Exception));
            Console.WriteLine("ex.derived_is_base=" + (b is MyEx));

            object c = new MyArgEx("x");
            Console.WriteLine("ex.arg_is_exception=" + (c is Exception));
            Console.WriteLine("ex.arg_is_system=" + (c is SystemException));

            // Unmodelled BCL exception base -> falls back to System.Exception,
            // so the universal is Exception answer stays correct.
            object e = new MyBclDerived();
            Console.WriteLine("ex.bcl_derived_is_exception=" + (e is Exception));
            Console.WriteLine("ex.bcl_derived_is_object=" + (e is object));

            // A non-exception user class must keep its null root.
            object d = new Plain();
            Console.WriteLine("ex.plain_is_object=" + (d is object));
            Console.WriteLine("ex.plain_is_exception=" + (d is Exception));

            // And the catch path, which is what the carrier work depends on.
            try
            {
                throw new MyEx();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex.catch_is_exception=" + (ex is Exception));
                Console.WriteLine("ex.catch_type=" + ex.GetType().FullName);
            }
        }
    }

    public sealed class Plain
    {
    }
}
