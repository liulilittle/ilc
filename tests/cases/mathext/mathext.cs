using System;
namespace Cases { public static class MathExtCase {
    public static void Main() {
        // net48 reference baseline for the trig/log/exp Math icalls
        // (il2cpp_math_ext.h / mathext.cpp). net48 double.ToString() prints
        // 15 significant digits -- every line below is the oracle that the
        // runtime helpers must reproduce bit-exactly.
        //
        // Trigonometry (known lattice values; domain errors yield NaN,
        // net48 never throws for these inputs):
        Console.WriteLine(Math.Sin(0.0));                        // 0
        Console.WriteLine(Math.Sin(Math.PI / 2.0));              // 1
        Console.WriteLine(Math.Cos(0.0));                        // 1
        Console.WriteLine(Math.Cos(Math.PI));                    // -1
        Console.WriteLine(Math.Tan(0.0));                        // 0
        Console.WriteLine(Math.Asin(1.0));                       // 1.5707963267949
        Console.WriteLine(Math.Asin(-1.0));                      // -1.5707963267949
        Console.WriteLine(Math.Acos(1.0));                       // 0
        Console.WriteLine(Math.Acos(-1.0));                      // 3.14159265358979
        Console.WriteLine(Math.Atan(1.0));                       // 0.785398163397448
        Console.WriteLine(Math.Atan(0.0));                       // 0
        Console.WriteLine(Math.Atan2(1.0, 1.0));                 // 0.785398163397448
        Console.WriteLine(Math.Atan2(1.0, 0.0));                 // 1.5707963267949
        Console.WriteLine(Math.Atan2(0.0, 1.0));                 // 0
        Console.WriteLine(Math.Asin(2.0));                       // NaN
        Console.WriteLine(Math.Acos(2.0));                       // NaN
        Console.WriteLine(Math.Sin(double.PositiveInfinity));    // NaN
        Console.WriteLine(Math.Tan(double.PositiveInfinity));    // NaN
        // Exponential / natural logarithm:
        Console.WriteLine(Math.Exp(0.0));                        // 1
        Console.WriteLine(Math.Exp(1.0));                        // 2.71828182845905
        Console.WriteLine(Math.Exp(-1.0));                       // 0.367879441171442
        // R5: ∞/Infinity print lines avoid byte comparison -- net48 console
        // encodes U+221E as GBK A1 DE while the ilc runtime writes UTF-8
        // E2 88 9E, so a byte-exact oracle diff can never match. Assert the
        // VALUE as a bool instead (both sides agree on the semantics).
        Console.WriteLine(Math.Exp(1000.0) == double.PositiveInfinity); // True
        Console.WriteLine(Math.Exp(-1000.0));                    // 0
        Console.WriteLine(Math.Log(1.0));                        // 0
        Console.WriteLine(Math.Log(Math.E));                     // 1
        Console.WriteLine(Math.Log(2.0));                        // 0.693147180559945
        Console.WriteLine(Math.Log(100.0));                      // 4.60517018598809
        Console.WriteLine(Math.Log(0.0) == double.NegativeInfinity);    // True (-Infinity)
        Console.WriteLine(Math.Log(-1.0));                       // NaN
        // Base-10 logarithm:
        Console.WriteLine(Math.Log10(100.0));                    // 2
        Console.WriteLine(Math.Log10(1000.0));                   // 3
        Console.WriteLine(Math.Log10(0.1));                      // -1
        Console.WriteLine(Math.Log10(0.0) == double.NegativeInfinity);  // True (-Infinity)
        Console.WriteLine(Math.Log10(-1.0));                     // NaN
        // Log(a, newBase) -- net48 empirical semantics (a==1 -> 0 checked
        // BEFORE newBase==1 -> NaN, per the .NET Framework reference source;
        // verified by this oracle run):
        Console.WriteLine(Math.Log(8.0, 2.0));                   // 3
        Console.WriteLine(Math.Log(100.0, 10.0));                // 2
        Console.WriteLine(Math.Log(2.0, 2.0));                   // 1
        Console.WriteLine(Math.Log(16.0, 2.0));                  // 4
        Console.WriteLine(Math.Log(3.0, 2.0));                   // 1.58496250072116
        Console.WriteLine(Math.Log(1.0, 10.0));                  // 0
        Console.WriteLine(Math.Log(10.0, 1.0));                  // NaN
        Console.WriteLine(Math.Log(1.0, 1.0));                   // NaN (newBase==1 wins over a==1)
        Console.WriteLine(Math.Log(-1.0, 10.0));                 // NaN
        Console.WriteLine(Math.Log(10.0, -1.0));                 // NaN
        Console.WriteLine(Math.Log(0.0, 2.0) == double.NegativeInfinity); // True (net48 oracle: ln0/ln2 = -Inf, NOT NaN)
        Console.WriteLine(Math.Log(8.0, 0.5));                   // -3
        // Truncate (toward zero; sign and zero preserved -- note net48
        // prints "-0" for a negative zero result):
        Console.WriteLine(Math.Truncate(1.9));                   // 1
        Console.WriteLine(Math.Truncate(-1.9));                  // -1
        Console.WriteLine(Math.Truncate(1.0));                   // 1
        Console.WriteLine(Math.Truncate(0.5));                   // 0
        Console.WriteLine(Math.Truncate(-0.5));                  // 0 (value is -0.0, ToString hides the sign)
        Console.WriteLine(Math.Truncate(double.NaN));            // NaN
        Console.WriteLine(Math.Truncate(double.PositiveInfinity) == double.PositiveInfinity); // True (Infinity)
        // Hyperbolic functions:
        Console.WriteLine(Math.Cosh(0.0));                       // 1
        Console.WriteLine(Math.Sinh(0.0));                       // 0
        Console.WriteLine(Math.Tanh(0.0));                       // 0
        Console.WriteLine(Math.Cosh(1.0));                       // 1.54308063481524
        Console.WriteLine(Math.Sinh(1.0));                       // 1.1752011936438
        Console.WriteLine(Math.Tanh(1.0));                       // 0.761594155955765
        // IEEERemainder: IEEE-754 remainder -- x - n*y with n the nearest
        // integral quotient, ties to EVEN n (NOT away-from-zero). The oracle
        // pins the tie cases: IEEERemainder(5,2) == 1 because 5/2 = 2.5 ties
        // toward the even quotient 2 -> 5 - 2*2 = 1.
        Console.WriteLine(Math.IEEERemainder(3.0, 2.0));         // -1
        Console.WriteLine(Math.IEEERemainder(5.0, 2.0));         // 1 (tie -> even quotient)
        Console.WriteLine(Math.IEEERemainder(-3.0, 2.0));        // 1
        Console.WriteLine(Math.IEEERemainder(3.0, -2.0));        // -1
        Console.WriteLine(Math.IEEERemainder(7.0, 4.0));         // -1
        Console.WriteLine(Math.IEEERemainder(0.0, 3.0));         // 0
        Console.WriteLine(Math.IEEERemainder(-0.0, 3.0));        // -0
        Console.WriteLine(Math.IEEERemainder(3.0, 0.0));         // NaN
        Console.WriteLine(Math.IEEERemainder(3.0, double.PositiveInfinity)); // 3
        Console.WriteLine(Math.IEEERemainder(double.PositiveInfinity, 3.0)); // NaN
        Console.WriteLine(Math.IEEERemainder(double.NaN, 3.0));  // NaN
        Console.WriteLine(Math.IEEERemainder(3.0, double.NaN));  // NaN
        // --- Bit-level edge probes (net48 truth for signed zeros and the
        // remaining Log precondition order). DoubleToInt64Bits exposes the
        // IEEE-754 bit pattern: 0x8000000000000000 = negative zero/negative
        // result, 0x7FF8000000000000 = NaN, 0x7FF0000000000000 = +Inf. ---
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Truncate(-0.5)));   // -0.0 sign bit
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.IEEERemainder(-0.0, 3.0))); // -0.0 sign bit
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.IEEERemainder(3.0, -0.0))); // NaN
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Atan2(-0.0, 1.0))); // -0.0 sign bit
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Atan2(-0.0, -1.0))); // -pi sign bit
        Console.WriteLine(Math.Log(1.0, -2.0));                  // NaN (newBase<0 beats a==1)
        Console.WriteLine(Math.Log(1.0, 0.0));                   // 0 (a==1 or 0/-Inf? bits probe below)
        Console.WriteLine(Math.Log(0.0, 0.0));                   // NaN
        Console.WriteLine(Math.Log(-2.0, -2.0));                 // NaN
        Console.WriteLine(Math.Log(-2.0, 2.0));                  // NaN (a<0)
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Log(1.0, 0.0))); // -0.0: real division, no +0 shortcut
        Console.WriteLine(Math.Log(2.0, 0.0));                   // NaN: b==0 intercepted for a!=1
        Console.WriteLine(Math.Log(0.5, 0.0));                   // NaN
        // More probes to pin the net48 Log(a,b) check order:
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Log(1.0, -0.0))); // 0/ln(-0.0)=0/-Inf = -0.0 ?
        Console.WriteLine(Math.Log(2.0, -0.0));                   // NaN if b==0 intercept catches -0.0
        Console.WriteLine(Math.Log(1.0, double.PositiveInfinity)); // 0/Inf = +0
        Console.WriteLine(Math.Log(2.0, double.PositiveInfinity)); // ln2/Inf = +0
        Console.WriteLine(Math.Log(double.PositiveInfinity, 2.0) == double.PositiveInfinity); // True (Inf/ln2 = Inf)
        Console.WriteLine(Math.Log(double.NaN, 2.0));            // NaN
        Console.WriteLine(Math.Log(2.0, double.NaN));            // NaN
        // Sanity: what 0/+-Inf yields in net48 (must match the -0.0 above):
        Console.WriteLine(BitConverter.DoubleToInt64Bits(0.0 / double.NegativeInfinity)); // -0.0
        Console.WriteLine(BitConverter.DoubleToInt64Bits(0.0 / double.PositiveInfinity)); // +0.0
        // Exact bit pattern of Log(3,2) -- the unit-test constant must be the
        // identical double (15-digit printouts round a small interval):
        Console.WriteLine(BitConverter.DoubleToInt64Bits(Math.Log(3.0, 2.0))); // exact bits
        // a==0 / signed-zero completion of the Log(a,b) truth table:
        Console.WriteLine(Math.Log(0.0, 0.5) == double.PositiveInfinity); // True (net48 oracle: +Inf -- ln0/ln0.5 = -Inf/-0.693)
        Console.WriteLine(Math.Log(0.0, -2.0));                  // ln0/ln(-2) = NaN?
        Console.WriteLine(Math.Log(-0.0, 2.0) == double.NegativeInfinity); // True (net48 oracle: -Inf -- ln(-0)/ln2)
        Console.WriteLine(Math.Log(-0.0, 0.0));                  // b==0 intercept?
        Console.WriteLine(Math.Log(2.0, double.NegativeInfinity)); // ln2/ln(-Inf) = NaN?
        Console.WriteLine(Math.Log(0.5, -0.0));                  // b==0 intercept catches -0.0
    }
}}
