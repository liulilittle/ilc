using System;
class P {
    static void Main() {
        int x = 5; string s = "hi";
        Console.WriteLine("a=" + ($"sum={x} and {s}" == "sum=5 and hi"));
        Console.WriteLine("b=" + ($"lit only" == "lit only"));
        Console.WriteLine("c=" + ($"empty={x}" == "empty=5"));
        Console.WriteLine("d=" + ($"multi={x}{s}--{x + 1}" == "multi=5hi--6"));
        Console.WriteLine("e=" + ($"lead{1}tail" == "lead1tail"));
Console.WriteLine("f=" + ($"{s}/2" == "hi/2"));
        long l = 12345678901L; double dd = 5.5; bool bb = true; char cc = 'x';
        Console.WriteLine("g=" + ($"align={x,5}|" == "align=    5|"));
        Console.WriteLine("h=" + ($"aligntxt={s,6}|" == "aligntxt=    hi|"));
        Console.WriteLine("i=" + ($"fmt={x:D3}" == "fmt=005"));
        Console.WriteLine("j=" + ($"{l}" == "12345678901"));
        Console.WriteLine("k=" + ($"{dd}" == "5.5"));
        Console.WriteLine("m=" + ($"{bb}" == "True"));
        Console.WriteLine("n=" + ($"{cc}" == "x"));
Console.WriteLine("o=" + ($"wdth={x,3}" == "wdth=  5"));
        byte by = 7; float ff = 1.5f;
        Console.WriteLine("p=" + ($"byf={by,3:D4}" == "byf=0007"));
        Console.WriteLine("q=" + ($"chf={cc,2:000}" == "chf=120"));
        Console.WriteLine("r=" + ($"dbf={dd,8:0.00}" == "dbf=    5.50"));
        Console.WriteLine("s2=" + ($"lof={l,10:D5}" == "lof=12345678901"));
        Console.WriteLine("t=" + ($"stf={s,4:D}" == "stf=  hi"));
        Console.WriteLine("u=" + ($"bywf={by,8:D2}" == "bywf=      07"));
        Console.WriteLine("v=" + ($"chwf={cc,6:00}" == "chwf=   120"));
        Console.WriteLine("w=" + ($"dbwf={dd,12:0.00}" == "dbwf=        5.50"));
        Console.WriteLine("x2=" + ($"lowf={l,15:D6}" == "lowf=  12345678901"));
        Console.WriteLine("y=" + ($"stwf={s,7:D}" == "stwf=     hi"));
        Console.WriteLine("z=" + ($"splitopts={string.Join("|", "a,,b,c".Split(',', StringSplitOptions.RemoveEmptyEntries))}" == "splitopts=a|b|c"));
        Console.WriteLine("aa=" + ($"anyof={s.IndexOfAny(new[]{'h','i'})}=1"));
        Console.WriteLine("ab=" + ($"byfmt={by:D4}" == "byfmt=0007"));
        Console.WriteLine("ac=" + ($"chfmt={cc:000}" == "chfmt=120"));
        Console.WriteLine("ad=" + ($"dbfmt={dd:0.00}" == "dbfmt=5.50"));
        Console.WriteLine("ae=" + ($"lofmt={l:D6}" == "lofmt=12345678901"));
        Console.WriteLine("af=" + ($"stfmt={s,0:D}" == "stfmt=hi"));
        Console.WriteLine("ag=" + ($"bofmt={bb:D}" == "bofmt=True"));
        Console.WriteLine("ah=" + ($"bowf={bb,4:D}" == "bowf= True"));
        Console.WriteLine($"sum={x} and {s}");
        Console.WriteLine($"empty={x}");
    }
}
