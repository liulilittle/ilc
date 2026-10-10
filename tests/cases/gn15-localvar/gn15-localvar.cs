// gn15-localvar.cs -- GN-15 probe: a bare VAR (0x13) local + an initobj(default)
// VAR local inside an arity>=2 closed clone whose per-argument VAR slots MIX a
// reference argument (T1 = int[,] -> a GC reference) and a VALUE argument
// (T2 = int -> a 32-bit value).
//
// HEAD defect (D1/D2/D3, same family as GN-14 (c)): "bottom" faces -- the bare
// VAR local declaration (CodeGenerator bare-VAR arm), the initobj VAR locKind
// scan, and the initobj emitter -- all classified the slot on the WHOLE
// InstantiationSuffix ("[int2]_int" != "int"), so the value-T local was
// declared Il2CppObject* and the int32 store could not convert (g++ "invalid
// conversion from 'int32_t' to 'Il2CppObject*'").
//
// A branch-assigned local (CopyT) defeats copy propagation, so the local is
// materialized and its declared width is observable. The reference argument
// (T1) stays a reference in the same clone (never erased the other way).
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Pair<A, B> {
    A a; B b;
    public Pair(A x, B y) { a = x; b = y; }
    public A First() { return a; }
    public B Second() { return b; }

    // D2/D3: initobj on a VAR TypeSpec (default(T) in a generic body).
    public B DefT() { B t = default(B); return t; }

    // D1: a bare VAR local with branch assignments (no copy propagation).
    public B CopyT(B x, bool c) { B t; if (c) t = b; else t = x; return t; }
}

public static class Gn15LocalVar {
    public static void Main() {
        Pair<int[,], int> p = new Pair<int[,], int>(null, 7);
        Console.WriteLine(p.First() == null);
        Console.WriteLine(p.Second());
        Console.WriteLine(p.DefT());
        Console.WriteLine(p.CopyT(3, true));
        Console.WriteLine(p.CopyT(3, false));
    }
}}
