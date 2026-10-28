//------------------------------------------------------------------------------
// guid-ctor-string.cs -- `new Guid(string)` on locals: the Roslyn
// `ldloca <slot>; ldstr; call Guid::.ctor(string)` lowering (a plain call,
// NOT newobj).
//
// Contract: the ctor call folds as a VOID statement (rv=false, the same ABI
// as Guid(byte[])) -- the fold's shadow stack stays pop2/push0 balanced, so
// later joins (if/else, loops) never see a stray +1 slot. The pre-fix shape
// (4f1cde64 mapped rv=true) died with "join: fall-through stack height 0 !=
// join slots 1" whenever a new Guid(string) sat inside a branch/loop.
//
// Covers: plain local init, if/else join, loop body, byte[] control, the
// value slot semantics (ToString / == / Guid.Empty comparisons).
// Every printed line is deterministic (fixed strings, invariant "D" form).
//------------------------------------------------------------------------------
using System;

static class Program
{
    static void Main()
    {
        // ---- plain local init (the linear ldloca+call shape) ----
        Guid g1 = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        Console.WriteLine("g1=" + g1.ToString());

        // ---- if/else join: both arms assign; the ctor sits before br.s ----
        Guid g2;
        int pick = 1;
        if (pick == 1)
        {
            g2 = new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566");
        }
        else
        {
            g2 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        }
        Console.WriteLine("g2=" + g2.ToString());
        Console.WriteLine("g2==g1: " + (g2 == g1));

        // ---- loop body: the ctor runs per iteration, back-edge join ----
        Guid acc = Guid.Empty;
        for (int i = 0; i < 3; i++)
        {
            acc = new Guid(i == 1
                ? "11111111-2222-3333-4444-555555555555"
                : "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        }
        Console.WriteLine("acc=" + acc.ToString());

        // ---- byte[] control (the rv=false shape that must not regress) ----
        // (the bytes are the net48 layout image of
        // 0aa79e57-1c2b-4d3e-9f40-112233445566; Guid::ToByteArray itself is a
        // separate unwired BCL member -- out of scope here, so the array is
        // spelled out to keep this case ctor-shape-only.)
        byte[] bytes = new byte[] { 0x57, 0x9e, 0xa7, 0x0a, 0x2b, 0x1c, 0x3e, 0x4d,
                                    0x9f, 0x40, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
        Guid g3 = new Guid(bytes);
        Console.WriteLine("g3=" + g3.ToString());
        Console.WriteLine("g3==g2pick: " + (g3 == new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566")));

        // ---- ctor in a condition guard (join right after the call) ----
        Guid g4 = Guid.Empty;
        if (new Guid("22222222-2222-2222-2222-222222222222") != g4)
        {
            g4 = new Guid("33333333-3333-3333-3333-333333333333");
        }
        Console.WriteLine("g4=" + g4.ToString());
        Console.WriteLine("g4 empty: " + (g4 == Guid.Empty));
    }
}
