using System;

//------------------------------------------------------------------------------
// conv-b7 -- batch-7 Convert residuals: the static-analyzable arms that were
// LOUD before this batch.
//
//   * Convert.ToSingle(bool)   -- net48 `value ? 1f : 0f`
//   * Convert.ToSingle(char)   -- net48 throws InvalidCastException (char has
//                                no IConvertible.ToSingle; the implicit char ->
//                                float widening exists but Convert does not
//                                expose it). The throw is caught and the type
//                                name printed (the runtime host's unhandled
//                                format is not byte-comparable to the net48
//                                console host).
//   * Convert.ToSingle(uint)   -- zero-extended uint -> float (RNE)
//   * Convert.ToUInt32(uint)   -- the identity conversion (bit-exact)
//   * Convert.ToDouble(uint)   -- exact uint -> double
//   * Convert.ToDouble(float)  -- the bit-exact float -> double identity
//
// Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvB7
    {
        public static void Main()
        {
            // ToSingle(bool): 1f / 0f
            Console.WriteLine("b1=" + Convert.ToSingle(true));
            Console.WriteLine("b2=" + Convert.ToSingle(false));

            // ToSingle(char): InvalidCastException on net48
            try { Console.WriteLine("c1=" + Convert.ToSingle('A')); }
            catch (Exception ex) { Console.WriteLine("c1=" + ex.GetType().FullName); }
            try { Console.WriteLine("c2=" + Convert.ToSingle('\uFFFF')); }
            catch (Exception ex) { Console.WriteLine("c2=" + ex.GetType().FullName); }

            // ToSingle(uint): zero-extended (uint >= 2^31 must stay positive)
            Console.WriteLine("u1=" + Convert.ToSingle(0u));
            Console.WriteLine("u2=" + Convert.ToSingle(4000000000u));
            Console.WriteLine("u3=" + Convert.ToSingle(uint.MaxValue));

            // ToUInt32(uint): identity (uint.MaxValue must survive bit-exact)
            Console.WriteLine("i1=" + Convert.ToUInt32(0u));
            Console.WriteLine("i2=" + Convert.ToUInt32(4000000000u));
            Console.WriteLine("i3=" + Convert.ToUInt32(uint.MaxValue));

            // ToDouble(uint) / ToDouble(float): exact widenings
            Console.WriteLine("d1=" + Convert.ToDouble(0u));
            Console.WriteLine("d2=" + Convert.ToDouble(4000000000u));
            Console.WriteLine("d3=" + Convert.ToDouble(uint.MaxValue));
            Console.WriteLine("d4=" + Convert.ToDouble(1.5f));
            Console.WriteLine("d5=" + Convert.ToDouble(0.1f));
        }
    }
}
