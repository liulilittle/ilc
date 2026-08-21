using System;
namespace Cases { public static class OvfCase {
    static double DBig() { return 1e18; }
    static double DNeg() { return -1e18; }
    public static void Main() {
        // checked double -> int64
        try { long x = checked((long)DBig()); Console.WriteLine("i8-big " + (x == 1000000000000000000L)); }
        catch (OverflowException) { Console.WriteLine("i8-big-throw"); }
        try { long y = checked((long)DNeg()); Console.WriteLine("i8-neg " + (y == -1000000000000000000L)); }
        catch (OverflowException) { Console.WriteLine("i8-neg-throw"); }
        // checked double -> int32
        double d5 = 5.5;
        int i5 = checked((int)d5);
        Console.WriteLine("i4-d " + (i5 == 5));
        try { int i6 = checked((int)DBig()); Console.WriteLine("i4-big-no"); }
        catch (OverflowException) { Console.WriteLine("i4-big-throw"); }
        // checked int64 -> int32
        long big = 2147483648L;
        try { int i7 = checked((int)big); Console.WriteLine("i4-l-no"); }
        catch (OverflowException) { Console.WriteLine("i4-l-throw"); }
        long ok = 42L;
        Console.WriteLine("i4-l-ok " + (checked((int)ok) == 42));
        // checked -> uint32
        long negl = -1L;
        try { uint u1 = checked((uint)negl); Console.WriteLine("u4-no"); }
        catch (OverflowException) { Console.WriteLine("u4-throw"); }
        long posl = 7L;
        Console.WriteLine("u4-ok " + (checked((uint)posl) == 7u));
        // checked -> uint64
        int negi = -1;
        try { ulong u2 = checked((ulong)negi); Console.WriteLine("u8-no"); }
        catch (OverflowException) { Console.WriteLine("u8-throw"); }
        int posi = 9;
        Console.WriteLine("u8-ok " + (checked((ulong)posi) == 9ul));
        // checked narrow: byte/sbyte/ushort/short
        int b300 = 300;
        try { byte b1 = checked((byte)b300); Console.WriteLine("b-no"); }
        catch (OverflowException) { Console.WriteLine("b-throw"); }
        int b200 = 200;
        Console.WriteLine("b-ok " + (checked((byte)b200) == 200));
        int sb130 = 130;
        try { sbyte s1 = checked((sbyte)sb130); Console.WriteLine("sb-no"); }
        catch (OverflowException) { Console.WriteLine("sb-throw"); }
        int sb100 = -100;
        Console.WriteLine("sb-ok " + (checked((sbyte)sb100) == -100));
        int us70000 = 70000;
        try { ushort us1 = checked((ushort)us70000); Console.WriteLine("us-no"); }
        catch (OverflowException) { Console.WriteLine("us-throw"); }
        int us40000 = 40000;
        Console.WriteLine("us-ok " + (checked((ushort)us40000) == 40000));
        int sh40000 = 40000;
        try { short sh1 = checked((short)sh40000); Console.WriteLine("sh-no"); }
        catch (OverflowException) { Console.WriteLine("sh-throw"); }
        int sh30000 = -30000;
        Console.WriteLine("sh-ok " + (checked((short)sh30000) == -30000));
        // checked unsigned-input casts (conv.ovf.*.un -- batch 154b)
        uint ubig = 4294967295u;
        try { int iu1 = checked((int)ubig); Console.WriteLine("iu-no"); }
        catch (OverflowException) { Console.WriteLine("iu-throw"); }
        uint umid = 2000000000u;
        Console.WriteLine("iu-ok " + (checked((int)umid) == 2000000000));
        uint usmall = 123u;
        Console.WriteLine("iu-ok2 " + (checked((int)usmall) == 123));
        ulong lubig = 18446744073709551615ul;
        try { long lu1 = checked((long)lubig); Console.WriteLine("lu-no"); }
        catch (OverflowException) { Console.WriteLine("lu-throw"); }
        ulong luok = 42ul;
        Console.WriteLine("lu-ok " + (checked((long)luok) == 42L));
        ulong lsmall = 7ul;
        Console.WriteLine("lu-ok2 " + (checked((uint)lsmall) == 7u));
        ulong lbig = 4294967296ul;
        try { uint lu2 = checked((uint)lbig); Console.WriteLine("lu2-no"); }
        catch (OverflowException) { Console.WriteLine("lu2-throw"); }
        uint u300 = 300u;
        try { byte ub1 = checked((byte)u300); Console.WriteLine("ub-no"); }
        catch (OverflowException) { Console.WriteLine("ub-throw"); }
        uint u200 = 200u;
        Console.WriteLine("ub-ok " + (checked((byte)u200) == 200));
        uint u130 = 130u;
        try { sbyte us1 = checked((sbyte)u130); Console.WriteLine("usb-no"); }
        catch (OverflowException) { Console.WriteLine("usb-throw"); }
        uint u70000 = 70000u;
        try { ushort uus1 = checked((ushort)u70000); Console.WriteLine("uus-no"); }
        catch (OverflowException) { Console.WriteLine("uus-throw"); }
        uint u40000 = 40000u;
        Console.WriteLine("uus-ok " + (checked((ushort)u40000) == 40000));
        uint u50000 = 50000u;
        try { short ush1 = checked((short)u50000); Console.WriteLine("ush-no"); }
        catch (OverflowException) { Console.WriteLine("ush-throw"); }
        uint us30000 = 30000u;
        Console.WriteLine("ush-ok " + (checked((short)us30000) == 30000));
    }
}}
