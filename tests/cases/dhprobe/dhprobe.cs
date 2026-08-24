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
        Console.WriteLine($"sum={x} and {s}");
        Console.WriteLine($"empty={x}");
    }
}
