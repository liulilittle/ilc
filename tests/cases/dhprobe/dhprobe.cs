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
        Console.WriteLine($"sum={x} and {s}");
        Console.WriteLine($"empty={x}");
    }
}
