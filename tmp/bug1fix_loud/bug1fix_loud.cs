//------------------------------------------------------------------------------
// bug1fix_loud.cs -- BUG-1 LOUD-gate probe: a VOID-returning delegate with a
// MIXED param shape (int + ref) cannot be expressed by any multicast icall
// (i4 needs all-int32 params, rr_void needs exactly 2 refs, r_void needs
// exactly 1 ref). The transpiler must LOUDLY reject this at emit time (never
// fall back to the raw methodPtr dispatch -- multicast UB). Building this
// fixture must FAIL with NotSupportedException.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Bug1FixLoud {
    public delegate void Mixed(int a, string b);

    public static void Handler(int a, string b) { }

    public static void Main() {
        Mixed m = Handler;
        m += Handler;   // multicast -- must LOUD at transpile
        m(1, "x");
    }
}}
