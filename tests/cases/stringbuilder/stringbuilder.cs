using System;
using System.Text;
namespace Cases { public static class StringBuilderCase {
    public static void Main() {
        // --- Constructors: capacity/length pinning (net48 reference) ---
        StringBuilder d = new StringBuilder();
        Console.WriteLine(d.Length);        // 0
        Console.WriteLine(d.Capacity);      // 16 (net48 DefaultCapacity)
        StringBuilder c4 = new StringBuilder(4);
        Console.WriteLine(c4.Capacity);     // 4
        StringBuilder c0 = new StringBuilder(0);
        Console.WriteLine(c0.Capacity);     // net48: 0 is treated as the default capacity
        StringBuilder cs = new StringBuilder("abc");
        Console.WriteLine(cs.Length);       // 3
        Console.WriteLine(cs.Capacity);     // capacity after the string ctor
        Console.WriteLine(new StringBuilder((string)null).Length); // 0 (null -> empty)
        StringBuilder longv = new StringBuilder("01234567890123456789");
        Console.WriteLine(longv.Length);    // 20
        Console.WriteLine(longv.Capacity);  // growth beyond the default capacity
        Console.WriteLine("[" + new StringBuilder().ToString() + "]"); // [] (empty content)
        // --- Append chain: string / char / int / object ---
        StringBuilder sb = new StringBuilder();
        sb.Append("Hello").Append(", ").Append("World").Append('!');
        Console.WriteLine(sb.ToString());   // Hello, World!
        Console.WriteLine(sb.Length);       // 13
        sb.Append(123);
        Console.WriteLine(sb.ToString());   // Hello, World!123
        sb.Append(-456);
        Console.WriteLine(sb.ToString());   // Hello, World!123-456
        sb.Append(0);
        Console.WriteLine(sb.ToString());   // Hello, World!123-4560
        sb.Append(int.MaxValue).Append(' ').Append(int.MinValue);
        Console.WriteLine(sb.ToString());   // Hello, World!123-45602147483647 -2147483648
        sb.Append((object)"OK");
        Console.WriteLine(sb.ToString());   // ...OK appended via Append(object)
        sb.Append((object)null);
        sb.Append((string)null);
        Console.WriteLine(sb.ToString());   // unchanged (both null no-ops)
        // --- Insert(int, string) ---
        sb.Insert(5, "XX");
        Console.WriteLine(sb.ToString());   // HelloXX, World!...
        sb.Insert(0, "S");
        Console.WriteLine(sb.ToString());   // SHelloXX, World!...
        sb.Insert(sb.Length, "E");
        Console.WriteLine(sb.ToString());   // ...OKE
        sb.Insert(2, (string)null);
        Console.WriteLine(sb.ToString());   // unchanged (null insert is a no-op)
        StringBuilder insGrow = new StringBuilder(2);
        insGrow.Insert(0, "abcde");
        Console.WriteLine(insGrow.ToString()); // abcde (insert grows the buffer)
        Console.WriteLine(insGrow.Capacity);   // grown capacity
        // --- Remove(int, int) ---
        sb.Remove(0, 1);
        Console.WriteLine(sb.ToString());   // HelloXX, World!... (S removed)
        sb.Remove(sb.Length - 1, 1);
        Console.WriteLine(sb.ToString());   // ...OK (E removed)
        sb.Remove(5, 2);
        Console.WriteLine(sb.ToString());   // Hello, World!... (XX removed)
        StringBuilder wipe = new StringBuilder("abcdef");
        wipe.Remove(0, 6);
        Console.WriteLine(wipe.Length);     // 0 (remove all)
        Console.WriteLine("[" + wipe.ToString() + "]"); // []
        // --- Growth under append pressure (capacity reallocations) ---
        StringBuilder big = new StringBuilder(4);
        big.Append("0123456789");
        Console.WriteLine(big.ToString());  // 0123456789
        Console.WriteLine(big.Capacity);    // capacity after growth
        big.Append("abcdefghij");
        Console.WriteLine(big.ToString());  // 0123456789abcdefghij
        Console.WriteLine(big.Capacity);    // capacity after second growth
        // --- Exception paths ---
        try { new StringBuilder(-1); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE"); }
        try { StringBuilder n = null; Console.WriteLine(n.Length); } catch (NullReferenceException) { Console.WriteLine("NRE"); }
        try { Console.WriteLine(sb.Insert(9999, "x")); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE2"); }
        try { Console.WriteLine(sb.Insert(-1, "x")); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE3"); }
        try { Console.WriteLine(sb.Remove(5, 9999)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE4"); }
        try { Console.WriteLine(sb.Remove(-1, 1)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE5"); }
        try { Console.WriteLine(sb.Remove(5, -1)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE6"); }
        // --- Append(long) / Append(double) / Append(bool) (batch 151) ---
        StringBuilder num = new StringBuilder();
        num.Append(42L);
        num.Append("|");
        num.Append(-987654321012345L);
        num.Append("|");
        num.Append(3.5);
        num.Append("|");
        num.Append(-0.25);
        num.Append("|");
        num.Append(true);
        num.Append("|");
        num.Append(false);
        Console.WriteLine(num.ToString());
        Console.WriteLine(num.Length);
    }
}}
