using System;
namespace Cases { public static class HexCase {
    // Hex byte formatter: b.ToString("X2") on the wired format_ext int32 X
    // path (icall_format_int32_custom, MethodMap.Format.cs:35; format_ext
    // covers D/X/N/C/E/G). Yields the canonical uppercase "00".."FF" form.
    static string HexByte(int b) { return b.ToString("X2"); }

    // byte[] -> "AB-CD-EF" via the WIRED BitConverter.ToString(byte[]) icall
    // (il2cpp_bitconverter_to_hex_string -- G2 worklist #3: MethodMap.
    // BitConverter.cs ToString case; net48 uppercase pairs + '-' separators,
    // empty array -> ""). This replaces the old hand-rolled loop on the X2
    // path; the output shape is byte-identical.
    static string ToHex(byte[] a) { return BitConverter.ToString(a); }

    // char -> nibble value (0..15), -1 for non-hex chars.
    static int HexDigit(char c) {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return -1;
    }

    // "AB" (starting at index i) -> byte value 0..255, -1 for malformed.
    static int HexPair(string s, int i) {
        int hi = HexDigit(s[i]);
        int lo = HexDigit(s[i + 1]);
        if (hi < 0 || lo < 0) return -1;
        return hi * 16 + lo;
    }

    public static void Main() {
        // --- known-value formatting (255 -> "FF", 0 -> "00") ---
        Console.WriteLine(HexByte(0) == "00");       // True
        Console.WriteLine(HexByte(15) == "0F");      // True
        Console.WriteLine(HexByte(255) == "FF");     // True
        Console.WriteLine(HexByte(128) == "80");     // True
        Console.WriteLine(HexByte(0xAB) == "AB");    // True

        // --- byte[] reachable via BitConverter.GetBytes (an icall-returned
        //     SZARRAY; no newarr byte emission) + ldelem.u1 reads ---
        byte[] b4 = BitConverter.GetBytes(0x12345678); // LE: 78-56-34-12
        Console.WriteLine(HexByte(b4[0]) == "78");   // True
        Console.WriteLine(HexByte(b4[1]) == "56");   // True
        Console.WriteLine(HexByte(b4[2]) == "34");   // True
        Console.WriteLine(HexByte(b4[3]) == "12");   // True
        Console.WriteLine(ToHex(b4) == "78-56-34-12"); // True

        // --- parsing (uppercase, lowercase, malformed) ---
        Console.WriteLine(HexDigit('0') == 0);       // True
        Console.WriteLine(HexDigit('9') == 9);       // True
        Console.WriteLine(HexDigit('A') == 10);      // True
        Console.WriteLine(HexDigit('F') == 15);      // True
        Console.WriteLine(HexDigit('a') == 10);      // True lowercase
        Console.WriteLine(HexDigit('z') == -1);      // True non-hex
        Console.WriteLine(HexPair("FF", 0) == 255);  // True
        Console.WriteLine(HexPair("00", 0) == 0);    // True
        Console.WriteLine(HexPair("10", 0) == 16);   // True
        Console.WriteLine(HexPair("3f", 0) == 63);   // True lowercase
        Console.WriteLine(HexPair("zz", 0) == -1);   // True malformed

        // --- parse roundtrip: ToHex output parses back to source bytes ---
        byte[] g = BitConverter.GetBytes(0xDEADBEEF); // LE: EF-BE-AD-DE
        string h = ToHex(g);
        Console.WriteLine(h == "EF-BE-AD-DE");       // True
        Console.WriteLine(HexPair(h, 0) == g[0]);    // True
        Console.WriteLine(HexPair(h, 3) == g[1]);    // True
        Console.WriteLine(HexPair(h, 6) == g[2]);    // True
        Console.WriteLine(HexPair(h, 9) == g[3]);    // True

        // --- int[] IS constructible (int32 newarr is wired); hex values can
        //     be carried in int slots ---
        int[] vals = new int[4];
        vals[0] = 0x00; vals[1] = 0xFF; vals[2] = 0x80; vals[3] = 0x12;
        Console.WriteLine(HexByte(vals[0]) == "00"); // True
        Console.WriteLine(HexByte(vals[1]) == "FF"); // True
        Console.WriteLine(HexByte(vals[2]) == "80"); // True
        Console.WriteLine(HexByte(vals[3]) == "12"); // True

    }
}}
