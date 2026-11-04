using System;

// ctorunused -- a constructor invoked for its SIDE EFFECTS whose result is
// never read. net48 always runs the ctor; a transpiler that decides "the
// result temp is dead, drop the call" silently loses the side effect.
namespace Cases
{
    public sealed class Counter
    {
        public static int Built;
        public static int FieldSeen;
        public int Value;

        public Counter()
        {
            ++Built;
            Value = 42;
        }

        public Counter(int seed)
        {
            ++Built;
            // A ctor that only touches ITS OWN fields is unobservable once the
            // object dies; the static counter is what makes it observable.
            FieldSeen = seed;
        }

        public static Counter Make()
        {
            return new Counter();
        }
    }

    public static class CtorUnused
    {
        public static void Main()
        {
            // 1) bare `new` statement: the ctor must still run
            new Counter();
            Console.WriteLine("bare=" + Counter.Built);

            // 2) factory whose result is discarded
            Counter.Make();
            Console.WriteLine("factory=" + Counter.Built);

            // 3) the SAME shape through a local that is never read again
            Counter c = new Counter(7);
            Console.WriteLine("local=" + Counter.Built + "," + Counter.FieldSeen);

            // 4) an unused ctor inside a loop keeps running every iteration
            for (int i = 0; i < 3; ++i)
            {
                new Counter(i);
            }
            Console.WriteLine("loop=" + Counter.Built + "," + Counter.FieldSeen);

            // 5) an unused ctor whose body THROWS must still throw
            try
            {
                new Thrower();
                Console.WriteLine("throw=none");
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine("throw=" + e.Message);
            }

            // 6) control: a used instance still reads its own field
            Console.WriteLine("used=" + c.Value);
        }
    }

    public sealed class Thrower
    {
        public Thrower()
        {
            throw new InvalidOperationException("ctor-ran");
        }
    }
}