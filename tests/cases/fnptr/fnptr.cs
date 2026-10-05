using System;
namespace Cases { public static class FnPtr {
    static int Add(int a, int b) => a + b;
    static int Twice(int x) => x * 2;
    static void Print(int v) => Console.WriteLine(v);
    // P-D3 matrix: int64/double/ref returns & params, multi-use results,
    // null/equality probes, fnptr-as-param forwarding, void* / IntPtr casts.
    static long Mul(long a, long b) => a * b;
    static double MulD(double a, double b) => a * b;
    static string Concat(string a, string b) => a + b;
    static unsafe int Apply(delegate*<int, int> f, int x) => f(x);
    public static unsafe void Main() {
        {
            delegate*<int, int, int> p = &Add;
            Console.WriteLine(p(3, 4));     // 7
            delegate*<int, int> q = &Twice;
            Console.WriteLine(q(21));       // 42
            delegate*<int, int> q2 = &Twice;
            Console.WriteLine(q2(5) + q(4)); // 10 + 8 = 18
            delegate*<int, void> v = &Print;
            v(99);                          // 99

            // P-D3 int64 return + param (single-use).
            delegate*<long, long, long> ml = &Mul;
            Console.WriteLine(ml(2000000000L, 2L)); // 4000000000
            // P-D3 double return + param.
            delegate*<double, double, double> md = &MulD;
            Console.WriteLine(md(1.5, 2.25));       // 3.375
            // P-D3 ref (string) return + params.
            delegate*<string, string, string> ms = &Concat;
            Console.WriteLine(ms("ab", "cd"));      // abcd

            // P-D3 multi-use int64/double results: the result must land in a
            // full-width temp (ResultKind), not an int32 truncation.
            long r = ml(2000000000L, 2L);
            Console.WriteLine(r);          // 4000000000
            Console.WriteLine(r + 1);      // 4000000001
            double s = md(1.5, 2.25);
            Console.WriteLine(s);          // 3.375
            Console.WriteLine(s + 0.5);    // 3.875

            // P-D3 fnptr passed as a parameter (void* direct).
            Console.WriteLine(Apply(q, 21));       // 42

            // P-D3 null/equality probes: pointers compare at full width.
            delegate*<int, int> nullp = null;
            Console.WriteLine(nullp == null);      // True
            Console.WriteLine(nullp != null);      // False
            Console.WriteLine(q == q2);            // True
            Console.WriteLine(q != q2);            // False
            // void* cast preserves the address.
            void* vp = (void*)q;
            Console.WriteLine(vp == (void*)q);     // True
            // NOTE: `(IntPtr)fnptr` is NOT asserted here -- it routes through
            // BCL System.IntPtr::op_Explicit(void*), whose body the transpiler
            // has no generated body for (pre-existing P2 gap, P-D3 out of scope).
        }
    }
}}