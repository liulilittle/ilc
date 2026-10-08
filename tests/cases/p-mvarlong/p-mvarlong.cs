// p-mvarlong.cs -- GN-N4 stage-1 verification: by-value MVAR long/double
// pass-through variants only (no user struct). Locks the M6 concrete-slot
// variants independently of the stage-2 user-struct decision.
using System;

namespace Cases {

public static class PMvarLong {
    static T Id<T>(T x) { return x; }

    public static void Main() {
        Console.WriteLine(Id<int>(5));         // 5
        long l = Id<long>(7L);
        Console.WriteLine(l == 7L);            // True
        double d = Id<double>(3.5);
        Console.WriteLine(d == 3.5);           // True
        Console.WriteLine(Id<double>(1.25));   // 1.25
    }
}}