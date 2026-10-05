// delegategen.cs -- delegate Invoke signature GENERALIZATION e2e (P-D2 #1):
// non-4-shape multicast signatures must route through the transpiler-generated
// per-signature multicast helper (il2cpp_delegate_multicast_<hash>) which
// preserves the FULL leaf ABI -- int64/double/float returns and int64/float
// params were silently truncated to int32_t before (P-D1 P0) and multi-leaf
// invocation through a raw methodPtr would read a container's first field as
// code (0xC0000005).
//
// Reference output (net48):
//   10
//   9
//   4
//   15
//   1.5
//
// net48 semantics the case pins:
//   * int64 multicast: l = Bump + Double -- the SAME argument is passed to
//     every leaf in invocation order and the LAST result wins: l(5) ->
//     Bump(5)=6, Double(5)=10 -> 10. The int64 return + int64 param survive
//     the helper (no int32 truncation, no first-field-read-as-code crash).
//   * double multicast: d = Sq + Sq; d(3.0) -> Sq(3)=9, Sq(3)=9 -> 9.
//   * float param AND float return multicast (the leaf-ABI asymmetry: a float
//     PARAM is carried as double across the generated ABI, a float RETURN is
//     float): f(12f) -> 4.
//   * 5-param int32 delegate (beyond the 4-shape fast path) -> helper, 15.
//   * float-returning zero-arg single-cast delegate -> helper, 1.5.
using System;
namespace Cases { public static class DelegateGen {
    public delegate long Lng(long x);
    public delegate double Dbl(double x);
    public delegate float Flt(float x);
    public delegate int Many(int a, int b, int c, int d, int e);
    public delegate float FRet();

    public static long Bump(long x) { return x + 1; }
    public static long Double(long x) { return x * 2; }
    public static double Sq(double x) { return x * x; }
    public static float Third(float x) { return x / 3f; }
    public static int Sum5(int a, int b, int c, int d, int e) { return a + b + c + d + e; }
    public static float OneF() { return 1.5f; }

    public static void Main() {
        Lng l = Bump;
        l += Double;
        Console.WriteLine(l(5));        // 10 (Bump(5)=6, Double(5)=10 -- same arg, last wins)

        Dbl d = Sq;
        d += Sq;
        Console.WriteLine(d(3.0));      // 9 (Sq(3)=9 per leaf; last wins)

        Flt f = Third;
        Console.WriteLine(f(12f));      // 4 (float param + float return ABI)

        Many m = Sum5;
        Console.WriteLine(m(1, 2, 3, 4, 5)); // 15 (beyond the 4-shape fast path)

        FRet r = OneF;
        Console.WriteLine(r());         // 1.5 (float return)
    }
}}