using System;
namespace Cases { public static class ConvertDeep {
    // Convert domain DEEP (batch C41). net48 oracle baseline (run-tests.ps1
    // diffs the transpiled run byte-for-byte). Extends convertcase/convdeep
    // over the WIRED M7 surface (MethodMap.Convert.cs):
    //
    //   1. BASE CONVERSIONS (base conversions): Convert.ToString(int, 2/8/10/16) --
    //      bases 2/8/16 render the uint32 two's-complement (negatives print
    //      their full unsigned bit pattern, lowercase hex); and
    //      Convert.ToInt32(string, base) -- net48 allows ONLY 2/8/10/16:
    //      '0x' prefix (radix 16), '+' for any radix, '-' ONLY for radix 10,
    //      radix-10 signed range with the 10-significant-digit overflow
    //      pre-check, radix 2/8/16 uint32 wraparound ("80000000",16 ->
    //      INT32_MIN). RECORDED SUPERSET (not exercised): the runtime's
    //      radix validator accepts 2..36 (net48 rejects 3/36 with
    //      ArgumentException -- the runtime parses; benign since valid net48
    //      programs never pass such a base).
    //   2. STRING<->NUMERIC FAMILY (string<->numeric full family): the wired
    //      ToInt32/ToInt64/ToDouble/ToString/ToBoolean/ToChar forms with
    //      whitespace/sign/exponent/boundary/leading-zero probes plus
    //      string<->number round trips.
    //   3. EXCEPTION SURFACE (exception surface): FormatException/OverflowException/
    //      ArgumentException/ArgumentOutOfRangeException, captured by TYPE
    //      NAME (messages are OS-localized, never byte-diffed).
    //
    // DELIBERATELY OUT OF SCOPE (would FAIL to transpile -- loud
    // NotSupportedException): Convert.ToByte/ToSByte/ToInt16/ToUInt16/
    // ToUInt32/ToUInt64/ToSingle/ToDecimal (CHECKED narrowing, no runtime
    // icall; MethodMap.ConvertExtra.cs). RECORDED DIVERGENCE (not
    // exercised): Convert.ToString(int, 36) -- net48 ArgumentException vs
    // runtime renders "16" (its radix validator accepts 2..36).
    public static void Main() {
        // ============ 1. base conversions: ToString(int, base) ============
        Console.WriteLine(Convert.ToString(42, 2) == "101010");
        Console.WriteLine(Convert.ToString(255, 2) == "11111111");
        Console.WriteLine(Convert.ToString(10, 2) == "1010");
        Console.WriteLine(Convert.ToString(255, 16) == "ff");
        Console.WriteLine(Convert.ToString(255, 8) == "377");
        Console.WriteLine(Convert.ToString(64, 8) == "100");
        Console.WriteLine(Convert.ToString(7, 8) == "7");
        Console.WriteLine(Convert.ToString(255, 10) == "255");
        Console.WriteLine(Convert.ToString(123, 10) == "123");
        Console.WriteLine(Convert.ToString(-1, 16) == "ffffffff");
        Console.WriteLine(Convert.ToString(-42, 16) == "ffffffd6");
        Console.WriteLine(Convert.ToString(-1, 2) == "11111111111111111111111111111111");
        Console.WriteLine(Convert.ToString(int.MinValue, 16) == "80000000");
        Console.WriteLine(Convert.ToString(int.MinValue, 8) == "20000000000");
        Console.WriteLine(Convert.ToString(int.MinValue, 2) == "10000000000000000000000000000000");
        Console.WriteLine(Convert.ToString(int.MaxValue, 16) == "7fffffff");
        Console.WriteLine(Convert.ToString(0xCAFE, 16) == "cafe");
        Console.WriteLine(Convert.ToString(0, 16) == "0");
        Console.WriteLine(Convert.ToString(0, 2) == "0");

        // ============ 2. base conversions: ToInt32(string, base) ============
        Console.WriteLine(Convert.ToInt32("101010", 2) == 42);
        Console.WriteLine(Convert.ToInt32("11111111", 2) == 255);
        Console.WriteLine(Convert.ToInt32("ff", 16) == 255);
        Console.WriteLine(Convert.ToInt32("FF", 16) == 255);
        Console.WriteLine(Convert.ToInt32("0xFF", 16) == 255);
        Console.WriteLine(Convert.ToInt32("0X1F", 16) == 31);
        Console.WriteLine(Convert.ToInt32("377", 8) == 255);
        Console.WriteLine(Convert.ToInt32("-128", 10) == -128);
        Console.WriteLine(Convert.ToInt32("+42", 10) == 42);
        Console.WriteLine(Convert.ToInt32("+101", 2) == 5);
        Console.WriteLine(Convert.ToInt32("80000000", 16) == int.MinValue);
        Console.WriteLine(Convert.ToInt32("FFFFFFFF", 16) == -1);
        Console.WriteLine(Convert.ToInt32("7fffffff", 16) == int.MaxValue);
        Console.WriteLine(Convert.ToInt32("2147483647", 10) == int.MaxValue);
        Console.WriteLine(Convert.ToInt32("-2147483648", 10) == int.MinValue);
        Console.WriteLine(Convert.ToInt32(null, 2) == 0);
        Console.WriteLine(Convert.ToInt32("000000000000000000042", 10) == 42);

        // ============ 3. string <-> numeric family ============
        Console.WriteLine(Convert.ToInt32("42") == 42);
        Console.WriteLine(Convert.ToInt32("  -7  ") == -7);
        Console.WriteLine(Convert.ToInt32("+7") == 7);
        Console.WriteLine(Convert.ToInt32("2147483647") == int.MaxValue);
        Console.WriteLine(Convert.ToInt32("-2147483648") == int.MinValue);
        Console.WriteLine(Convert.ToInt32("00042") == 42);
        Console.WriteLine(Convert.ToInt64("9223372036854775807") == long.MaxValue);
        Console.WriteLine(Convert.ToInt64("-9223372036854775808") == long.MinValue);
        Console.WriteLine(Convert.ToInt64("0") == 0);
        Console.WriteLine(Convert.ToDouble("3.5") == 3.5);
        Console.WriteLine(Convert.ToDouble("-1.25e2") == -125.0);
        Console.WriteLine(Convert.ToDouble("1e3") == 1000.0);
        Console.WriteLine(Convert.ToDouble("0.5") == 0.5);
        Console.WriteLine(Convert.ToDouble("  -42  ") == -42.0);
        Console.WriteLine(Convert.ToDouble("42") == 42.0);
        Console.WriteLine(Convert.ToDouble(42) == 42.0);
        Console.WriteLine(Convert.ToInt32('A') == 65);
        Console.WriteLine(Convert.ToInt32('0') == 48);
        Console.WriteLine(Convert.ToInt32('\0') == 0);
        Console.WriteLine(Convert.ToInt32(char.MaxValue) == 65535);
        Console.WriteLine(Convert.ToChar(65) == 'A');
        Console.WriteLine(Convert.ToChar(0) == '\0');
        Console.WriteLine(Convert.ToChar(0xFFFF) == char.MaxValue);
        Console.WriteLine(Convert.ToBoolean(0) == false);
        Console.WriteLine(Convert.ToBoolean(1) == true);
        Console.WriteLine(Convert.ToBoolean(-5) == true);
        Console.WriteLine(Convert.ToBoolean(int.MinValue) == true);
        Console.WriteLine(Convert.ToString(123) == "123");
        Console.WriteLine(Convert.ToString(-45L) == "-45");
        Console.WriteLine(Convert.ToString(1.5) == "1.5");
        Console.WriteLine(Convert.ToString(true) == "True");
        Console.WriteLine(Convert.ToString(false) == "False");
        Console.WriteLine(Convert.ToString('x') == "x");
        Console.WriteLine(Convert.ToString(int.MaxValue) == "2147483647");
        Console.WriteLine(Convert.ToString(int.MinValue) == "-2147483648");
        Console.WriteLine(Convert.ToString(long.MinValue) == "-9223372036854775808");
        Console.WriteLine(Convert.ToInt32(Convert.ToString(123456789)) == 123456789);
        Console.WriteLine(Convert.ToInt64(Convert.ToString(long.MaxValue)) == long.MaxValue);
        Console.WriteLine(Convert.ToDouble(Convert.ToString(3.5)) == 3.5);
        Console.WriteLine(Convert.ToInt32('Q') == 81);

        // ============ 4. exception surface (type names only) ============
        try { Convert.ToInt32("abc"); Console.WriteLine("intparse:ok"); }
        catch (Exception e) { Console.WriteLine("intparse:" + e.GetType().Name); }
        try { Convert.ToInt32(""); Console.WriteLine("intempty:ok"); }
        catch (Exception e) { Console.WriteLine("intempty:" + e.GetType().Name); }
        try { Convert.ToInt32("1.5"); Console.WriteLine("intdot:ok"); }
        catch (Exception e) { Console.WriteLine("intdot:" + e.GetType().Name); }
        try { Convert.ToInt32("1,000"); Console.WriteLine("intcomma:ok"); }
        catch (Exception e) { Console.WriteLine("intcomma:" + e.GetType().Name); }
        try { Convert.ToInt32("2147483648"); Console.WriteLine("intmax+1:ok"); }
        catch (Exception e) { Console.WriteLine("intmax+1:" + e.GetType().Name); }
        try { Convert.ToInt32("99999999999"); Console.WriteLine("intdigits:ok"); }
        catch (Exception e) { Console.WriteLine("intdigits:" + e.GetType().Name); }
        try { Convert.ToInt64("12x"); Console.WriteLine("longfmt:ok"); }
        catch (Exception e) { Console.WriteLine("longfmt:" + e.GetType().Name); }
        try { Convert.ToInt64("9223372036854775808"); Console.WriteLine("longmax+1:ok"); }
        catch (Exception e) { Console.WriteLine("longmax+1:" + e.GetType().Name); }
        try { Convert.ToDouble("1.2.3"); Console.WriteLine("dblfmt:ok"); }
        catch (Exception e) { Console.WriteLine("dblfmt:" + e.GetType().Name); }
        try { Convert.ToDouble("1e999"); Console.WriteLine("dbloverflow:ok"); }
        catch (Exception e) { Console.WriteLine("dbloverflow:" + e.GetType().Name); }
        try { Convert.ToChar(0x10000); Console.WriteLine("charovf:ok"); }
        catch (Exception e) { Console.WriteLine("charovf:" + e.GetType().Name); }
        try { Convert.ToChar(-1); Console.WriteLine("charneg:ok"); }
        catch (Exception e) { Console.WriteLine("charneg:" + e.GetType().Name); }
        try { Convert.ToInt32("12x", 10); Console.WriteLine("basefmt:ok"); }
        catch (Exception e) { Console.WriteLine("basefmt:" + e.GetType().Name); }
        try { Convert.ToInt32("-5", 2); Console.WriteLine("baseneg:ok"); }
        catch (Exception e) { Console.WriteLine("baseneg:" + e.GetType().Name); }
        try { Convert.ToInt32("101", 37); Console.WriteLine("baseradix:ok"); }
        catch (Exception e) { Console.WriteLine("baseradix:" + e.GetType().Name); }
        try { Convert.ToInt32("", 16); Console.WriteLine("baseempty:ok"); }
        catch (Exception e) { Console.WriteLine("baseempty:" + e.GetType().Name); }
        try { Convert.ToInt32("100000000", 16); Console.WriteLine("baseovf:ok"); }
        catch (Exception e) { Console.WriteLine("baseovf:" + e.GetType().Name); }
        try { Convert.ToInt32(null, 37); Console.WriteLine("basenull:ok"); }
        catch (Exception e) { Console.WriteLine("basenull:" + e.GetType().Name); }
        try { Convert.ToString(42, 1); Console.WriteLine("tstr1:ok"); }
        catch (Exception e) { Console.WriteLine("tstr1:" + e.GetType().Name); }
        // deliberate-false pins
        Console.WriteLine(Convert.ToString(255, 16) == "FF");
        Console.WriteLine(Convert.ToString(10, 2) == "101");
    }
}}
