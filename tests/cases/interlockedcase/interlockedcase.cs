using System;
using System.Threading;

// interlockedcase.cs -- net48 reference oracle for System.Threading.Interlocked
// (see src/runtime/include/internal/il2cpp_interlocked.h and the icall
// implementations in src/runtime/src/interlocked.cpp).
//
// Value semantics exercised (int32 and int64 overloads):
//   Increment/Decrement   -> returns the NEW value
//   Add                   -> returns the NEW sum
//   Exchange              -> returns the OLD value
//   CompareExchange       -> returns the OLD value (write only when it equals
//                            the comparand)
//
// The 8-thread accumulation variant (8 * 1000 increments -> 8000) is NOT part
// of this case: the M-stage transpiler has no Thread object model
// (new Thread/ThreadStart are unmapped; Thread.CurrentThread is LOUDLY
// rejected -- MethodMap.Thread.cs). Until threading is wired, the e2e runs the
// single-threaded value-semantics portion only; the threaded variant is a
// TODO for the wiring agent that fills MethodMap.Interlocked.cs.
//
// Expected output (deterministic, all-bool):
//   28x True
class InterlockedCase
{
    static void Main()
    {
        // --- Increment (int) ---
        int a = 0;
        Console.WriteLine(Interlocked.Increment(ref a) == 1);   // returns NEW
        Console.WriteLine(a == 1);

        // --- Decrement (int) ---
        int b = 5;
        Console.WriteLine(Interlocked.Decrement(ref b) == 4);   // returns NEW
        Console.WriteLine(b == 4);

        // --- Add (int) ---
        int c = 10;
        Console.WriteLine(Interlocked.Add(ref c, 5) == 15);     // returns NEW sum
        Console.WriteLine(c == 15);
        Console.WriteLine(Interlocked.Add(ref c, -3) == 12);    // negative addend
        Console.WriteLine(c == 12);

        // --- Exchange (int) ---
        int d = 7;
        Console.WriteLine(Interlocked.Exchange(ref d, 99) == 7); // returns OLD
        Console.WriteLine(d == 99);

        // --- CompareExchange (int) ---
        int e = 42;
        Console.WriteLine(Interlocked.CompareExchange(ref e, 100, 42) == 42); // match -> writes, returns OLD
        Console.WriteLine(e == 100);
        Console.WriteLine(Interlocked.CompareExchange(ref e, 200, 42) == 100); // no match -> unchanged, returns CURRENT
        Console.WriteLine(e == 100);

        // --- Increment (long) ---
        long la = 0;
        Console.WriteLine(Interlocked.Increment(ref la) == 1);
        Console.WriteLine(la == 1);

        // --- Decrement (long) ---
        long lb = 5;
        Console.WriteLine(Interlocked.Decrement(ref lb) == 4);
        Console.WriteLine(lb == 4);

        // --- Add (long) ---
        long lc = 10;
        Console.WriteLine(Interlocked.Add(ref lc, 5) == 15);
        Console.WriteLine(lc == 15);
        Console.WriteLine(Interlocked.Add(ref lc, -3) == 12);
        Console.WriteLine(lc == 12);

        // --- Exchange (long) ---
        long ld = 7;
        Console.WriteLine(Interlocked.Exchange(ref ld, 99) == 7);
        Console.WriteLine(ld == 99);

        // --- CompareExchange (long) ---
        long le = 42;
        Console.WriteLine(Interlocked.CompareExchange(ref le, 100, 42) == 42);
        Console.WriteLine(le == 100);
        Console.WriteLine(Interlocked.CompareExchange(ref le, 200, 42) == 100);
        Console.WriteLine(le == 100);
    }
}
