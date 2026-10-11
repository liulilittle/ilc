// swprobe.cs — control-flow family (switch table + leave/endfinally) net48 evidence.
// Key points: out-of-range/negative switch value -> default; long switch = comparison chain, not a jump table; leave exiting try always runs finally;
// nested finally LIFO; finally determinism on every exit path (return/break/continue).
using System;

class SwProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);

    static int SwInt(int x)
    {
        // consecutive integers -> IL switch (jump table)
        switch (x)
        {
            case 0: return 100;
            case 1: return 101;
            case 2: return 102;
            default: return -1;
        }
    }

    static void Main()
    {
        Pi("switch(0)", SwInt(0));
        Pi("switch(1)", SwInt(1));
        Pi("switch(2)", SwInt(2));
        Pi("switch(3) [default]", SwInt(3));
        Pi("switch(-1) [default]", SwInt(-1));
        Pi("switch(100) [default]", SwInt(100));

        // sparse switch + negative case: non-contiguous ranges become a comparison chain (il2cpp transpiler note)
        Pi("sparse(5)", Sparse(5));
        Pi("sparse(-3)", Sparse(-3));
        Pi("sparse(7) [default]", Sparse(7));

        // long switch (legal since C# 7; IL is a comparison chain, not a switch instruction)
        Pi("longSwitch(5L)", LongSwitch(5L));
        Pi("longSwitch(99L) [default]", LongSwitch(99L));

        // goto case (same jump table)
        Pi("gotoCase(1)", GotoCase(1));

        // ---- leave/finally: return exiting try -> finally still runs ----
        Pi("retThroughFinally(1)", RetThroughFinally(1));
        P("-- retThroughFinally(2) exception path --");
        try { Pi("retThroughFinally(2)", RetThroughFinally(2)); }
        catch (Exception e) { P("caught: " + e.GetType().Name); }

        // ---- finally runs before exception propagation (try/finally without catch) ----
        P("-- try/finally exception propagation order --");
        try { ThrowInTryFinally(); }
        catch (Exception e) { P("caught: " + e.GetType().Name); }

        // ---- nested finally: LIFO (exception caught by outer catch, program exits normally) ----
        P("-- nested finally LIFO --");
        try
        {
            try { throw new Exception("inner"); }
            finally { P("finally: inner"); }
        }
        catch (Exception e) { P("caught: " + e.GetType().Name); }
        finally { P("finally: outer"); }
        P("-- end --");

        // ---- finally of a try inside a loop on break/continue ----
        P("-- try/finally break inside loop --");
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (i == 1) break;
                P("iter " + i);
            }
            finally { P("finally iter " + i); }
        }
        P("-- try/finally continue inside loop --");
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (i == 1) continue;
                P("iter " + i);
            }
            finally { P("finally iter " + i); }
        }
        P("-- done --");
    }

    static int Sparse(int x)
    {
        switch (x)
        {
            case 5: return 50;
            case -3: return -30;
            default: return -1;
        }
    }

    static int LongSwitch(long x)
    {
        switch (x)
        {
            case 5L: return 50;
            case 6L: return 60;
            default: return -1;
        }
    }

    static int GotoCase(int x)
    {
        switch (x)
        {
            case 1: goto case 2;
            case 2: return 20;
            default: return -1;
        }
    }

    static int RetThroughFinally(int mode)
    {
        try
        {
            if (mode == 2) throw new Exception("boom");
            P("try: mode=" + mode);
            return mode * 10;      // leave: finally runs first, return value is 10
        }
        finally
        {
            P("finally: runs before return/exception");
        }
    }

    static void ThrowInTryFinally()
    {
        try { P("try: throw"); throw new InvalidOperationException("x"); }
        finally { P("finally: before exception propagation"); }
    }
}
