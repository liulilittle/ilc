using System;

//------------------------------------------------------------------------------
// conv-narrow -- pin the net48 semantics of the Convert narrowing targets
// that were still LOUD: char sources into ToByte/ToSByte/ToInt16/ToUInt16 and
// decimal sources into ToByte/ToSByte/ToInt16/ToUInt16. Byte-exact against
// the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvNarrow
    {
        public static void Main()
        {
            // ---- char -> narrow targets (char is a 16-bit unsigned value) ----
            Console.WriteLine("b_A=" + Convert.ToByte('A'));
            Console.WriteLine("b_e9=" + Convert.ToByte('\u00E9'));       // 233
            Try("b_100", delegate { Console.WriteLine(Convert.ToByte('\u0100')); });   // 256 -> overflow
            Try("sb_e9", delegate { Console.WriteLine(Convert.ToSByte('\u00E9')); });  // 233 -> overflow
            Console.WriteLine("i2_A=" + Convert.ToInt16('A'));
            Try("i2_8000", delegate { Console.WriteLine(Convert.ToInt16('\u8000')); }); // 32768 -> overflow
            Console.WriteLine("u2_ffff=" + Convert.ToUInt16('\uFFFF')); // 65535
            Console.WriteLine("u2_A=" + Convert.ToUInt16('A'));

            // ---- decimal -> narrow targets (round half-to-even then range-check) ----
            Console.WriteLine("b_3_5=" + Convert.ToByte(3.5m));          // 4
            Console.WriteLine("b_n0_5=" + Convert.ToByte(-0.5m));        // 0
            Try("b_256", delegate { Console.WriteLine(Convert.ToByte(256m)); });
            Console.WriteLine("sb_3_5=" + Convert.ToSByte(3.5m));        // 4
            Try("sb_128", delegate { Console.WriteLine(Convert.ToSByte(128m)); });
            Try("sb_n129", delegate { Console.WriteLine(Convert.ToSByte(-129m)); });
            Console.WriteLine("i2_3_5=" + Convert.ToInt16(3.5m));         // 4
            Try("i2_32768", delegate { Console.WriteLine(Convert.ToInt16(32768m)); });
            Try("i2_n32769", delegate { Console.WriteLine(Convert.ToInt16(-32769m)); });
            Console.WriteLine("u2_3_5=" + Convert.ToUInt16(3.5m));       // 4
            Try("u2_65536", delegate { Console.WriteLine(Convert.ToUInt16(65536m)); });
            Try("u2_n1", delegate { Console.WriteLine(Convert.ToUInt16(-1m)); });
        }

        private static void Try(string tag, Action a)
        {
            try { a(); }
            catch (Exception e) { Console.WriteLine(tag + "=" + e.GetType().Name); }
        }
    }
}
