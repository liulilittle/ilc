using System;
using System.Text;
namespace Cases { public static class Base64Case {
    // Empirically pin the .NET Framework 4.8 System.Convert base64 semantics
    // that the runtime icalls must match (net48 is the project's reference
    // baseline):
    //   * Convert.ToBase64String(byte[]) -> RFC 4648 with '=' padding; the
    //     final quantum pads when the input length is not a multiple of 3.
    //   * Convert.FromBase64String(string) -> ignores ASCII whitespace
    //     (space/tab/CR/LF); malformed input throws FormatException.
    // Round-trip checks print bool; malformed-input checks print the
    // exception TYPE NAME only (messages are OS-localized and not stable
    // for byte-diffing).
    // Known vectors: "Man" -> "TWFu", "M" -> "TQ==", "Ma" -> "TWE=",
    // "Hello"(5B) -> "SGVsbG8=", CJK "zhong wen" (UTF-8) -> "5Lit5paH".
    public static void Main() {
        byte[] man = new byte[] { (byte)'M', (byte)'a', (byte)'n' };
        string manB64 = Convert.ToBase64String(man);
        Console.WriteLine(manB64 == "TWFu");                                  // True (known vector)
        Console.WriteLine(BytesEqual(Convert.FromBase64String(manB64), man)); // True (round trip)

        byte[] one = new byte[] { (byte)'M' };
        string oneB64 = Convert.ToBase64String(one);
        Console.WriteLine(oneB64 == "TQ==");                                  // True (1-byte quantum)
        Console.WriteLine(BytesEqual(Convert.FromBase64String(oneB64), one)); // True

        byte[] two = new byte[] { (byte)'M', (byte)'a' };
        string twoB64 = Convert.ToBase64String(two);
        Console.WriteLine(twoB64 == "TWE=");                                  // True (2-byte quantum)
        Console.WriteLine(BytesEqual(Convert.FromBase64String(twoB64), two)); // True

        byte[] five = new byte[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        string fiveB64 = Convert.ToBase64String(five);
        Console.WriteLine(fiveB64 == "SGVsbG8=");                             // True (5-byte quantum)
        Console.WriteLine(BytesEqual(Convert.FromBase64String(fiveB64), five)); // True

        byte[] empty = new byte[0];
        string emptyB64 = Convert.ToBase64String(empty);
        Console.WriteLine(emptyB64 == "");                                    // True (empty)
        Console.WriteLine(Convert.FromBase64String(emptyB64).Length == 0);    // True

        string zh = "中文";
        byte[] zhBytes = Encoding.UTF8.GetBytes(zh);
        string zhB64 = Convert.ToBase64String(zhBytes);
        Console.WriteLine(zhB64 == "5Lit5paH");                               // True (UTF-8 bridge)
        byte[] zhRt = Convert.FromBase64String(zhB64);
        Console.WriteLine(BytesEqual(zhRt, zhBytes));                         // True (byte round trip)
        Console.WriteLine(Encoding.UTF8.GetString(zhRt) == zh);               // True (string round trip)

        Console.WriteLine(BytesEqual(Convert.FromBase64String("TW Fu"), man)); // True (whitespace ignored)

        try { Convert.FromBase64String("TWFu!"); Console.WriteLine("bad1"); }
        catch (FormatException e) { Console.WriteLine("bad1=" + e.GetType().Name); } // FormatException
        try { Convert.FromBase64String("TW$u"); Console.WriteLine("bad2"); }
        catch (FormatException e) { Console.WriteLine("bad2=" + e.GetType().Name); } // FormatException
        try { Convert.FromBase64String("TW"); Console.WriteLine("bad3"); }
        catch (FormatException e) { Console.WriteLine("bad3=" + e.GetType().Name); } // FormatException (len%4==2)
        try { Convert.FromBase64String("TWE"); Console.WriteLine("bad4"); }
        catch (FormatException e) { Console.WriteLine("bad4=" + e.GetType().Name); } // FormatException (len%4==3)
        try { Convert.FromBase64String("TWFuT"); Console.WriteLine("bad5"); }
        catch (FormatException e) { Console.WriteLine("bad5=" + e.GetType().Name); } // FormatException (len%4==1)
    }
    private static bool BytesEqual(byte[] a, byte[] b) {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}}
