//------------------------------------------------------------------------------
// p-refl-ctorinvoke.cs -- ConstructorInfo.Invoke + GetConstructor(Type[])
// probe (GAP-5 / P1). net48 surface:
//   * typeof(C).GetConstructor(new Type[]{...}) -- matches a PUBLIC
//     constructor by parameter-type list (empty Type[] = the parameterless
//     ctor); non-null on a hit, null on a miss.
//   * ctorInfo.Invoke(object[] args) -- creates a NEW instance of the
//     declaring type (allocates + runs the ctor with the unboxed args) and
//     returns it.
// The typeinfo method table carries the ctor entry (name ".ctor", flags bit2
// = ctor, the same table GetConstructors()/il2cpp_methodinfo_invoke use), so
// the invoke reuses the ctor dispatch. Every record is a bool verdict on the
// constructed object's observable state, byte-stable.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Ctor2
    {
        private int _a;
        private int _b;
        public Ctor2() { _a = 1; _b = 2; }
        public Ctor2(int a) { _a = a; _b = 0; }
        public Ctor2(int a, int b) { _a = a; _b = b; }
        public int Sum { get { return _a + _b; } }
    }

    public static class PReflCtorinvoke
    {
        public static void Main()
        {
            Type t = typeof(Ctor2);

            // GetConstructor(Type[]) by arity: non-null on a matching PUBLIC
            // ctor, null on a miss.
            ConstructorInfo p0 = t.GetConstructor(new Type[0]);
            ConstructorInfo p1 = t.GetConstructor(new Type[] { typeof(int) });
            ConstructorInfo p2 = t.GetConstructor(new Type[] { typeof(int), typeof(int) });
            Console.WriteLine("[gc.empty]=" + (p0 != null));
            Console.WriteLine("[gc.one]=" + (p1 != null));
            Console.WriteLine("[gc.two]=" + (p2 != null));

            // Invoke(object[]): creates a new instance running the matched ctor.
            Ctor2 a = (Ctor2)p0.Invoke(new object[0]);
            Console.WriteLine("[inv.empty.sum]=" + (a.Sum == 3));
            Ctor2 b = (Ctor2)p1.Invoke(new object[] { 40 });
            Console.WriteLine("[inv.one.sum]=" + (b.Sum == 40));
            Ctor2 c = (Ctor2)p2.Invoke(new object[] { 10, 20 });
            Console.WriteLine("[inv.two.sum]=" + (c.Sum == 30));

            // The ctor that was matched is the one with the requested arity.
            Console.WriteLine("[inv.empty.is2]=" + (a.Sum == 3 && a.Sum != 0));
        }
    }
}