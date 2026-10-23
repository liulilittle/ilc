using System;

//------------------------------------------------------------------------------
// bcrange1 -- System.BitConverter remainder (E item of the value-type family
// alignment): the ToString(byte[], int) / ToString(byte[], int, int)
// overloads (net48 surface), plus a re-verification of the already-wired
// GetBytes/ToT/IsLittleEndian surface that shares this file.
//
// Raw outputs (never booleans alone) so the oracle text is the evidence.
//------------------------------------------------------------------------------
class Program
{
    static void Show(string id, Func<string> f)
    {
        try
        {
            Console.WriteLine("[" + id + "] " + f());
        }
        catch (Exception e)
        {
            Console.WriteLine("[" + id + "] EX " + e.GetType().Name);
        }
    }

    static void Main()
    {
        byte[] b = new byte[] { 0x00, 0x0A, 0xFF, 0x7F, 0x80, 0x01 };

        Show("s0", delegate { return BitConverter.ToString(b); });
        Show("s1", delegate { return BitConverter.ToString(b, 0); });
        Show("s2", delegate { return BitConverter.ToString(b, 1); });
        Show("s3", delegate { return BitConverter.ToString(b, 5); });
        Show("s4", delegate { return BitConverter.ToString(b, 6); });
        Show("s5", delegate { return BitConverter.ToString(b, 2, 3); });
        Show("s6", delegate { return BitConverter.ToString(b, 0, 0); });
        Show("s7", delegate { return BitConverter.ToString(b, 6, 0); });
        Show("s8", delegate { return BitConverter.ToString(b, 5, 1); });
        Show("s9", delegate { return BitConverter.ToString(b, 1, 5); });
        Show("s10", delegate { return BitConverter.ToString(b, 1, 6); });
        Show("s11", delegate { return BitConverter.ToString(b, -1); });
        Show("s12", delegate { return BitConverter.ToString(b, 2, -1); });
        Show("s13", delegate { return BitConverter.ToString(b, 7); });
        Show("s14", delegate { return BitConverter.ToString((byte[])null); });
        Show("s15", delegate { return BitConverter.ToString((byte[])null, 0); });
        Show("s16", delegate { return BitConverter.ToString((byte[])null, 0, 0); });
        Show("s17", delegate { return BitConverter.ToString(new byte[0]); });
        Show("s18", delegate { return BitConverter.ToString(new byte[0], 0); });
        Show("s19", delegate { return BitConverter.ToString(new byte[0], 0, 0); });
        Show("s20", delegate { return BitConverter.ToString(new byte[] { 0xAB }); });
        Show("s21", delegate { return BitConverter.ToString(new byte[] { 0xAB }, 0, 1); });

        // GetBytes / ToT re-verification (same file, already wired).
        Show("g1", delegate { return BitConverter.ToString(BitConverter.GetBytes(0x12345678)); });
        Show("g2", delegate { return BitConverter.ToString(BitConverter.GetBytes(1.5f)); });
        Show("g3", delegate { return BitConverter.ToString(BitConverter.GetBytes('Z')); });
        Show("g4", delegate { return BitConverter.ToInt32(BitConverter.GetBytes(-123456), 0).ToString(); });
        Show("g5", delegate { return BitConverter.ToDouble(BitConverter.GetBytes(2.25), 0).ToString("R"); });
        Show("g6", delegate { return BitConverter.ToString(BitConverter.GetBytes(true)); });
        Show("g7", delegate { return BitConverter.ToString(BitConverter.GetBytes(0xFEDCBA9876543210UL)); });
        Show("g8", delegate { return BitConverter.ToString(BitConverter.GetBytes(65535)); });
        Show("g9", delegate { return BitConverter.ToInt64(BitConverter.GetBytes(0x0123456789ABCDEFL), 0).ToString(); });
        Show("g10", delegate { return BitConverter.ToBoolean(new byte[] { 42 }, 0).ToString(); });
        Show("le", delegate { return BitConverter.IsLittleEndian.ToString(); });
        Show("d2i", delegate { return BitConverter.DoubleToInt64Bits(-3.25).ToString(); });
        Show("i2d", delegate { return BitConverter.Int64BitsToDouble(0x3FF0000000000000L).ToString("R"); });
    }
}
