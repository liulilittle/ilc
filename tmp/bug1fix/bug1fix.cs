//------------------------------------------------------------------------------
// bug1fix.cs -- BUG-1 regression probe (void-N-int-arg multicast delegate
// crash, batch-168 fix incomplete). net48 reference oracle.
//
//   * Action<int>  += 2 handlers, invoke -> both run (void-N-int-arg N=1)
//   * Action<string> += 2 handlers, invoke -> both run (void-1-ref)
//   * custom void M(int) multicast (custom delegate, void-N-int-arg)
//   * Action<int,int> += 2 handlers, invoke (void-2-int-arg)
//   * custom void M(int,string) mixed-param -> NOT in this file (LOUD at
//     transpile; separate probe verifies the LOUD gate)
//   * single-cast Action<int> invoke (no +=)
//   * Action<int> static + instance mixed chain (per-leaf dispatch)
//------------------------------------------------------------------------------
using System;

namespace Cases {
    public class C {
        public int Value;
        public C(int v) { Value = v; }
        public void Bump(int x) { Value += x; }
    }

    public static class Bug1Fix {
        public delegate void IntOnly(int x);

        public static int order = 0;

        public static void Add3(int x) { order += x; }
        public static void Add30(int x) { order += x * 10; }

        public static void Cat(string s) { order += s.Length; }
        public static void Cat2(string s) { order += s.Length * 2; }

        public static void BothInts(int a, int b) { order += a + b; }
        public static void BothInts2(int a, int b) { order += (a + b) * 10; }

        public static void Main() {
            // ============ 1. Action<int> multicast (void-1-int-arg) ============
            order = 0;
            Action<int> act = Add3;
            act(2);
            Console.WriteLine("a0=" + (order == 2));
            Action<int> act2 = Add30;
            act += act2;
            Console.WriteLine("a1=" + (act.GetInvocationList().Length == 2));
            act(3);
            Console.WriteLine("a2=" + (order == 2 + 3 + 30));   // 2 + 3 + 30 = 35

            // ============ 2. Action<string> multicast (void-1-ref) ============
            order = 0;
            Action<string> cats = Cat;
            cats += Cat2;
            Console.WriteLine("s0=" + (cats.GetInvocationList().Length == 2));
            cats("abc");
            Console.WriteLine("s1=" + (order == 3 + 6));        // 3 + 6 = 9

            // ============ 3. custom void M(int) multicast ============
            order = 0;
            IntOnly io = Add3;
            io += Add30;
            Console.WriteLine("m0=" + (io.GetInvocationList().Length == 2));
            io(4);
            Console.WriteLine("m1=" + (order == 4 + 40));       // 4 + 40 = 44

            // ============ 4. Action<int,int> multicast (void-2-int-arg) ============
            order = 0;
            Action<int, int> pair = BothInts;
            pair += BothInts2;
            Console.WriteLine("p0=" + (pair.GetInvocationList().Length == 2));
            pair(2, 3);
            Console.WriteLine("p1=" + (order == 5 + 50));       // 5 + 50 = 55

            // ============ 5. single-cast Action<int> (no +=) ============
            order = 0;
            Action<int> single = Add3;
            single(5);
            Console.WriteLine("sg0=" + (order == 5));

            // ============ 6. instance-bound leaf joins a chain ============
            order = 0;
            C c = new C(10);
            Action<int> inst = c.Bump;
            inst += Add3;
            inst(2);
            Console.WriteLine("i0=" + (c.Value == 12));         // 10 + 2
            Console.WriteLine("i1=" + (order == 2));            // Add3 runs
        }
    }
}
