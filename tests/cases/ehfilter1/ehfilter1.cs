using System;

namespace Cases
{
    public static class EhFilter1
    {
        static int F1(int x)
        {
            try
            {
                if (x == 0) throw new InvalidOperationException("zero");
                if (x == 1) throw new ArgumentException("one");
                return 10;
            }
            catch (Exception e) when (e.Message == "zero")
            {
                Console.WriteLine("[c1]=" + e.Message);
                return 1;
            }
            catch (ArgumentException ae) when (ae.Message == "x")
            {
                Console.WriteLine("[c2]=" + ae.Message);
                return 2;
            }
            catch (Exception e2)
            {
                Console.WriteLine("[c3]=" + e2.Message);
                return 3;
            }
        }

        static int F2()
        {
            int hits = 0;
            try
            {
                throw new InvalidOperationException("a");
            }
            catch (Exception e) when (++hits > 0 && e.Message.Length == 1)
            {
                Console.WriteLine("[f2.hits]=" + hits);
                return 7;
            }
        }

        static int F3()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("inner");
                }
                finally
                {
                    Console.WriteLine("[f3.fin]=1");
                }
            }
            catch (Exception e) when (e.Message == "inner")
            {
                Console.WriteLine("[f3.catch]=" + e.Message);
                return 9;
            }
        }

        // A rejected filter must keep UNWINDING: the next handler is the
        // ENCLOSING try's, not another clause of the same try.
        static int F4()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("reject");
                }
                catch (Exception e) when (e.Message == "accept")
                {
                    Console.WriteLine("[f4.wrong]=1");
                    return -1;
                }
                catch (Exception e) when (e.Message == "reject")
                {
                    Console.WriteLine("[f4.same-try]=" + e.Message);
                    return 4;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[f4.outer]=" + e.Message);
                return 40;
            }
        }

        // Every clause's filter is evaluated, in source order, until one matches.
        static int F5()
        {
            int first = 0, second = 0;
            try
            {
                throw new InvalidOperationException("two");
            }
            catch (Exception e) when (++first > 0 && e.Message == "one")
            {
                Console.WriteLine("[f5.wrong]=1");
                return -1;
            }
            catch (Exception e) when (++second > 0 && e.Message == "two")
            {
                Console.WriteLine("[f5.first]=" + first);
                Console.WriteLine("[f5.second]=" + second);
                return 5;
            }
            catch (Exception e)
            {
                Console.WriteLine("[f5.third]=1");
                return -1;
            }
        }

        // An exception THROWN BY A FILTER replaces the original and propagates
        // out of the whole catch set (net48: the filter search is abandoned).
        static int F6()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("original");
                }
                catch (Exception e) when (Boom(e.Message))
                {
                    Console.WriteLine("[f6.wrong]=1");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[f6.outer]=" + e.Message);
                return 6;
            }
        }

        static bool Boom(string m)
        {
            throw new ArgumentException("fromfilter:" + m);
        }

        public static void Main()
        {
            Console.WriteLine("[f1.0]=" + F1(0));
            Console.WriteLine("[f1.1]=" + F1(1));
            Console.WriteLine("[f1.2]=" + F1(2));
            Console.WriteLine("[f2]=" + F2());
            Console.WriteLine("[f3]=" + F3());
            Console.WriteLine("[f4]=" + F4());
            Console.WriteLine("[f5]=" + F5());
            Console.WriteLine("[f6]=" + F6());
        }
    }
}
