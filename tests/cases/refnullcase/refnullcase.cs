using System;
using System.Threading;
namespace Cases { public static class ProbeRefNull {
    public static void Main() {
        object x = "lit";
        Interlocked.Exchange(ref x, null);
        Console.WriteLine(x == null);
        Console.WriteLine(ReferenceEquals(x, null));
        object y = null;
        Interlocked.Exchange(ref y, "set");
        Console.WriteLine(y == null);
        Console.WriteLine(y);
        object z = "keep";
        Interlocked.Exchange(ref z, "set");
        Console.WriteLine(z == null);
        Console.WriteLine(z);
    }
}}
