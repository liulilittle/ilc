using System;

// B5-R6: conv WIDTH dispatch over a method PARAMETER, and the same body reached
// through the INLINE candidate fold.
//
// Two defects this case locks:
//   * the production fold context never carried ParamTypeNames, so conv.i8 over
//     a DOUBLE parameter emitted icall_conv_i8_i4 (the int32 widening helper):
//     (long)(1.5 + 3000000000.0) returned -2147483648 instead of 3000000000.
//   * the inline candidate fold used a SEPARATE MethodContext that lacked the
//     same fields, so an INLINED body produced a different (wrong) helper than
//     the non-inlined body. SmallI8D is small enough to be inlined; its result
//     is compared against the identical non-inlined call path.
//
// Coverage: (long)double, (long)float, (int)sbyte, (int)byte, (int)int,
// (long)long, instance `this` offset (pi = ai - 1), and the inline fold.
namespace Cases
{
    public sealed class ConvHolder
    {
        // Instance methods: the parameter slot is arg1, not arg0 (arg0 = this).
        public long ToLongFromDouble(double d) { return (long)d; }
        public long ToLongFromFloat(float f) { return (long)f; }
        public int ToIntFromSByte(sbyte v) { return (int)v; }
        public int ToIntFromByte(byte v) { return (int)v; }
        public int ToIntFromInt(int v) { return (int)v; }
        public long ToLongFromLong(long v) { return (long)v; }
        public long MixedDoubleLong(double d, long l) { return (long)(d + l); }
    }

    public static class B5Conv
    {
        // Inline candidate: returns int32, tiny body, no exception regions.
        static int SmallI8D(double d) { return (int)(long)d; }

        // Non-inlined twin with an identical body (marked big enough to stay
        // out of the inliner by adding an opaque call).
        static int BigI8D(double d)
        {
            Console.WriteLine("[big-called]=" + (d > 0.0));
            return (int)(long)d;
        }

        static long StaticDoubleLong(double d, long l) { return (long)(d + l); }
        static long StaticFloatLong(float f) { return (long)f; }

        public static void Main()
        {
            ConvHolder h = new ConvHolder();

            // ===== (long) of a double/float PARAMETER =====
            Console.WriteLine("[d-param-long]=" + h.ToLongFromDouble(3000000000.5));
            Console.WriteLine("[f-param-long]=" + h.ToLongFromFloat(3000000000.5f));
            Console.WriteLine("[static-d-param-long]=" + StaticDoubleLong(1.5, 3000000000L));
            Console.WriteLine("[static-f-param-long]=" + StaticFloatLong(1234567.75f));

            // ===== narrow PARAMETER -> int32 =====
            Console.WriteLine("[sbyte-param-int]=" + h.ToIntFromSByte(-5));
            Console.WriteLine("[byte-param-int]=" + h.ToIntFromByte(200));
            Console.WriteLine("[int-param-int]=" + h.ToIntFromInt(-12345));
            Console.WriteLine("[long-param-long]=" + h.ToLongFromLong(3000000000L));

            // ===== instance `this` offset (arg0 = this, so the parameter is arg1) =====
            Console.WriteLine("[this-offset]=" + h.MixedDoubleLong(1.5, 3000000000L));

            // ===== the INLINE fold path =====
            Console.WriteLine("[inline-i8d]=" + SmallI8D(3000000000.5));
            Console.WriteLine("[inline-i8d-neg]=" + SmallI8D(-3000000000.5));
            Console.WriteLine("[noninline-i8d]=" + BigI8D(3000000000.5));

            // ===== the truncation must stay a truncation, not a bit copy =====
            Console.WriteLine("[inline-frac]=" + SmallI8D(2.9));
            Console.WriteLine("[inline-small]=" + SmallI8D(-2.9));
        }
    }
}
