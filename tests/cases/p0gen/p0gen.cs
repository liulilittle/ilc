// p0gen.cs -- generic METHOD variant routing: emission/resolution symmetry.
//
// DEFECT (phantom symbol): a generic method definition emits the concrete
// slot variants ({name}_string / _long / _double / _<Struct>) only when the
// folded body is a PURE PASS-THROUGH (CodeGenerator.IsPurePassThrough). The
// call site (MethodRefResolver, MethodSpec-on-MethodDefinition) appended the
// suffix UNCONDITIONALLY, so a non-pass-through generic method produced a
// call to a symbol that was never emitted: transpile OK, cmake
// "'<name>_long' was not declared in this scope".
//
// This case pins BOTH halves:
//   * pure pass-through (Id<T>, Pick<T>) -- the variants must stay green;
//   * TYPE-AGNOSTIC but not a bare move (Ignore<T> returns a constant,
//     Twice<T> returns a non-MVAR constant): T never reaches an operation, so
//     the slot variants are compilable -- these are the phantom cells.
//
// The `S<T>(T v) => v.ToString()` shape (a constrained callvirt on the MVAR)
// is deliberately NOT here: its T=int dispatch is a SEPARATE gap (the
// generated call passes &arg0 (int32_t*) to an Il2CppObject* icall) and is
// registered in docs/audit/p0gen-gaps.md.
using System;

namespace Cases
{
    public struct Pt
    {
        public int X;
        public Pt(int x) { X = x; }
    }

    public static class P0Gen
    {
        // pure pass-through (the pre-existing variant gate accepts this)
        static T Id<T>(T x) => x;

        // TYPE-AGNOSTIC, not a bare move: T is unused, the body is a constant.
        static int Ignore<T>(T v) => 42;
        static long Twice<T>(T v) => 7L;

        // pass-through with TWO MVAR slots
        static T Pick<T>(T a, T b) => a;

        public static void Main()
        {
            Console.WriteLine(Id<int>(5));
            Console.WriteLine(Id<long>(7L));
            Console.WriteLine(Id<double>(1.5));
            Console.WriteLine(Id<string>("s"));
            Pt p = Id<Pt>(new Pt(9));
            Console.WriteLine(p.X);

            Console.WriteLine(Ignore<int>(1));
            Console.WriteLine(Ignore<long>(2L));
            Console.WriteLine(Ignore<double>(3.0));
            Console.WriteLine(Ignore<string>("x"));

            long t1 = Twice<long>(1L);
            Console.WriteLine(t1);
            long t2 = Twice<double>(2.0);
            Console.WriteLine(t2);
            long t3 = Twice<string>("s");
            Console.WriteLine(t3);

            Console.WriteLine(Pick<long>(1L, 2L));
            Console.WriteLine(Pick<double>(1.5, 2.5));
            Console.WriteLine(Pick<string>("a", "b"));
        }
    }
}
