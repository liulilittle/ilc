using System;

namespace Cases
{
    public struct VaPoint { public int X; public int Y; }
    public struct VaBig { public int A; public int B; public int C; public int D; }
    public enum VaColor { Red = 1, Green = 2 }
    public class VaBox { public int F; }

    public static class ProbeVarargs
    {
        // (1) baseline: GetRemainingCount decrements, GetNextArg() unwraps int.
        static void ShowCountSum(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            int n = ai.GetRemainingCount();
            int sum = 0;
            while (ai.GetRemainingCount() > 0)
            {
                TypedReference tr = ai.GetNextArg();
                sum += __refvalue(tr, int);
            }
            Console.WriteLine("[count]=" + n);
            Console.WriteLine("[sum]=" + sum);
            Console.WriteLine("[count2]=" + ai.GetRemainingCount());
        }

        // (2) types must survive: __reftype reports each declared element type.
        static void ShowTypes(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            Console.WriteLine("[n]=" + ai.GetRemainingCount());
            while (ai.GetRemainingCount() > 0)
            {
                TypedReference tr = ai.GetNextArg();
                Console.WriteLine("[t]=" + __reftype(tr));
            }
        }

        // (3) mixed values read back with their exact types.
        static void ShowMixedValues(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            Console.WriteLine("[v.int]=" + __refvalue(ai.GetNextArg(), int));
            Console.WriteLine("[v.str]=" + __refvalue(ai.GetNextArg(), string));
            Console.WriteLine("[v.dbl]=" + __refvalue(ai.GetNextArg(), double));
            Console.WriteLine("[v.lng]=" + __refvalue(ai.GetNextArg(), long));
            Console.WriteLine("[v.cnt]=" + ai.GetRemainingCount());
        }

        // (4) empty varargs.
        static void ShowEmpty(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            Console.WriteLine("[e.count]=" + ai.GetRemainingCount());
            try
            {
                TypedReference tr = ai.GetNextArg();
                Console.WriteLine("[e.next]=" + __reftype(tr));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[e.next.ex]=" + ex.GetType().FullName);
            }
            try
            {
                TypedReference tr = ai.GetNextArg(typeof(int).TypeHandle);
                Console.WriteLine("[e.typed]=" + __reftype(tr));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[e.typed.ex]=" + ex.GetType().FullName);
            }
        }

        // (5) GetNextArg(RuntimeTypeHandle) overload with matching types.
        static void ShowTyped(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            TypedReference a = ai.GetNextArg(typeof(int).TypeHandle);
            Console.WriteLine("[ty.int]=" + __refvalue(a, int));
            TypedReference b = ai.GetNextArg(typeof(string).TypeHandle);
            Console.WriteLine("[ty.str]=" + __refvalue(b, string));
            Console.WriteLine("[ty.cnt]=" + ai.GetRemainingCount());
        }

        // (6) GetNextArg(RuntimeTypeHandle) mismatch.
        static void ShowTypedMismatch(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            try
            {
                TypedReference a = ai.GetNextArg(typeof(string).TypeHandle);
                Console.WriteLine("[tym.val]=" + __refvalue(a, string));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[tym.ex]=" + ex.GetType().FullName);
            }
            Console.WriteLine("[tym.cnt]=" + ai.GetRemainingCount());
        }

        // (7) struct / enum / reference / null / object / 16-byte struct.
        static void ShowStructEnum(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            TypedReference s = ai.GetNextArg();
            Console.WriteLine("[s.t]=" + __reftype(s));
            VaPoint p = __refvalue(s, VaPoint);
            Console.WriteLine("[s.v]=" + p.X + "," + p.Y);
            TypedReference e = ai.GetNextArg();
            Console.WriteLine("[en.t]=" + __reftype(e));
            Console.WriteLine("[en.v]=" + __refvalue(e, VaColor));
            TypedReference r = ai.GetNextArg();
            Console.WriteLine("[r.t]=" + __reftype(r));
            Console.WriteLine("[r.v]=" + (__refvalue(r, string) ?? "<null>"));
            TypedReference z = ai.GetNextArg();
            Console.WriteLine("[z.t]=" + __reftype(z));
            Console.WriteLine("[z.v]=" + (__refvalue(z, string) ?? "<null>"));
            TypedReference o = ai.GetNextArg();
            Console.WriteLine("[o.t]=" + __reftype(o));
            Console.WriteLine("[o.v]=" + ((VaBox)__refvalue(o, object)).F);
            TypedReference big = ai.GetNextArg();
            Console.WriteLine("[big.t]=" + __reftype(big));
            VaBig vb = __refvalue(big, VaBig);
            Console.WriteLine("[big.v]=" + vb.A + "," + vb.B + "," + vb.C + "," + vb.D);
            Console.WriteLine("[se.cnt]=" + ai.GetRemainingCount());
        }

        // (8) independent position: consuming one iterator must not move another.
        static void ShowIndependent(__arglist)
        {
            ArgIterator a = new ArgIterator(__arglist);
            ArgIterator b = new ArgIterator(__arglist);
            Console.WriteLine("[ind.a1]=" + __refvalue(a.GetNextArg(), int));
            Console.WriteLine("[ind.b1]=" + __refvalue(b.GetNextArg(), int));
            Console.WriteLine("[ind.b2]=" + __refvalue(b.GetNextArg(), int));
            Console.WriteLine("[ind.a.cnt]=" + a.GetRemainingCount());
            Console.WriteLine("[ind.b.cnt]=" + b.GetRemainingCount());
        }

        // (9) over-read: take past the end.
        static void ShowOverread(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            while (ai.GetRemainingCount() > 0)
            {
                TypedReference tr = ai.GetNextArg();
                Console.WriteLine("[o]=" + __refvalue(tr, int));
            }
            try
            {
                TypedReference extra = ai.GetNextArg();
                Console.WriteLine("[over]=" + __refvalue(extra, int));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[over.ex]=" + ex.GetType().FullName);
            }
        }

        // (10) GetNextArgType (peek, non-consuming) rendered as System.Type.
        static void ShowNextArgType(__arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            Console.WriteLine("[nt.1]=" + Type.GetTypeFromHandle(ai.GetNextArgType()));
            Console.WriteLine("[nt.2]=" + Type.GetTypeFromHandle(ai.GetNextArgType()));
            Console.WriteLine("[nt.cnt]=" + ai.GetRemainingCount());
            Console.WriteLine("[nt.val]=" + __refvalue(ai.GetNextArg(), int));
            Console.WriteLine("[nt.3]=" + Type.GetTypeFromHandle(ai.GetNextArgType()));
        }

        // (11) varargs method with a value return + a fixed parameter.
        static int SumFixed(int seed, __arglist)
        {
            ArgIterator ai = new ArgIterator(__arglist);
            int sum = seed;
            while (ai.GetRemainingCount() > 0)
            {
                sum += __refvalue(ai.GetNextArg(), int);
            }
            return sum;
        }

        public static void Main()
        {
            ShowCountSum(__arglist(1, 2, 3));
            ShowTypes(__arglist(10, "s", 3.5));
            ShowMixedValues(__arglist(7, "mix", 2.25, 1234567890123L));
            ShowEmpty(__arglist());
            ShowTyped(__arglist(42, "typed"));
            ShowTypedMismatch(__arglist(5));
            VaPoint pt = new VaPoint(); pt.X = 3; pt.Y = 4;
            VaBox box = new VaBox(); box.F = 9;
            VaBig big = new VaBig(); big.A = 1; big.B = 2; big.C = 3; big.D = 4;
            ShowStructEnum(__arglist(pt, VaColor.Green, "ref", (string)null, (object)box, big));
            ShowIndependent(__arglist(10, 20));
            ShowOverread(__arglist(1));
            ShowNextArgType(__arglist(77, "x"));
            Console.WriteLine("[sumfixed]=" + SumFixed(100, __arglist(1, 2, 3)));
        }
    }
}
