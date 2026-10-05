using System;

// convd -- T1 parity probe: Convert double/float sources over the rounding
// _d icalls (net48 rounds half-to-even, then range-checks).
class Prog
{
    static void Main()
    {
        Console.WriteLine("i64_35=" + Convert.ToInt64(3.5));
        Console.WriteLine("i64_25=" + Convert.ToInt64(2.5));
        Console.WriteLine("i64_n25=" + Convert.ToInt64(-2.5));
        Console.WriteLine("i64_f=" + Convert.ToInt64(2.5f));
        Console.WriteLine("u64_123=" + Convert.ToUInt64(123.9));
        Console.WriteLine("u32_55=" + Convert.ToUInt32(5.5));
        Console.WriteLine("u16_1006=" + (int)Convert.ToUInt16(100.6));
        Console.WriteLine("i16_1006=" + (int)Convert.ToInt16(100.6));
        Console.WriteLine("b_35=" + (int)Convert.ToByte(3.5));
        Console.WriteLine("sb_n35=" + (int)Convert.ToSByte(-3.5));
        Console.WriteLine("done");
    }
}
