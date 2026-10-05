using System;
using System.Threading;

namespace Cases
{
    public static class InterlockedObj
    {
        public static void Main()
        {
            // Exchange(ref object, object): returns the OLD value
            object a = "first";
            object b = "second";
            object old = Interlocked.Exchange(ref a, b);
            Console.WriteLine(old);
            Console.WriteLine(a);
            Console.WriteLine(ReferenceEquals(a, b));
            // CompareExchange(ref object, object, object): swap on match
            object c = "target";
            object newVal = "replacement";
            object prev = Interlocked.CompareExchange(ref c, newVal, "target");
            Console.WriteLine(prev);
            Console.WriteLine(c);
            // mismatched comparand: no swap, old value returned
            object d = "keep";
            object prev2 = Interlocked.CompareExchange(ref d, "changed", "nope");
            Console.WriteLine(prev2);
            Console.WriteLine(d);
            // null handling: exchange with null
            object e = "gone";
            object old3 = Interlocked.Exchange(ref e, null);
            Console.WriteLine(old3);
            Console.WriteLine(ReferenceEquals(e, null));
        }
    }
}
