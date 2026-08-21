using System;
namespace Cases { public static class BitConvCase {
    // Empirically pin the .NET Framework 4.8 System.BitConverter semantics that
    // the runtime icalls must match (net48 is the project's reference baseline,
    // Windows x86-64 = little-endian):
    //   * GetBytes(1.0f) -- IEEE-754 single 0x3F800000, little-endian byte
    //     order -> 00-00-80-3F (LSB at index 0).
    //   * GetBytes(int) -> ToInt32 roundtrip is the identity; the byte order
    //     is documented by the hex dumps (0x12345678 -> 78-56-34-12).
    //   * DoubleToInt64Bits(1.0) == 0x3FF0000000000000 (bit-for-bit
    //     reinterpretation, no rounding); Int64BitsToDouble is the inverse.
    //   * BitConverter.IsLittleEndian is true on this platform.
    //   * ToBoolean on a single-byte array is "non-zero -> true".
    //   * GetBytes(bool) yields the 1-byte array 01/00.
    public static void Main() {
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(1.0f)));        // 00-00-80-3F
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(-2.5f)));       // 00-00-20-C0
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(0.0f)));        // 00-00-00-00
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(0x12345678)));  // 78-56-34-12
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(-2147483648))); // 00-00-00-80
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(-1)));          // FF-FF-FF-FF
        Console.WriteLine(BitConverter.ToInt32(BitConverter.GetBytes(0x12345678), 0)); // 305419896
        Console.WriteLine(BitConverter.ToInt32(BitConverter.GetBytes(-1), 0));         // -1
        Console.WriteLine(BitConverter.DoubleToInt64Bits(1.0));                       // 4607182418800017408
        Console.WriteLine(BitConverter.DoubleToInt64Bits(-0.0));                      // -9223372036854775808
        Console.WriteLine(BitConverter.Int64BitsToDouble(0x3FF0000000000000));        // 1
        Console.WriteLine(BitConverter.IsLittleEndian);                               // True
        Console.WriteLine(BitConverter.ToBoolean(new byte[] { 0 }, 0));               // False
        Console.WriteLine(BitConverter.ToBoolean(new byte[] { 1 }, 0));               // True
        Console.WriteLine(BitConverter.ToBoolean(new byte[] { 42 }, 0));              // True
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(true)));        // 01
        Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(false)));       // 00
    }
}}
