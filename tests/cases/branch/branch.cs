using System;
namespace Cases { public static class Branch {
    public static void Main() {
        int x = 5;
        if (x > 3) Console.WriteLine("gt"); else Console.WriteLine("le");
        if (x == 5) Console.WriteLine("eq"); else Console.WriteLine("ne");
        if (x != 4) Console.WriteLine("ne2"); else Console.WriteLine("eq2");
        int y = x > 3 ? 100 : 200;
        Console.WriteLine(y);
        Console.WriteLine(x >= 5 ? "ge" : "lt");
    }
}}
