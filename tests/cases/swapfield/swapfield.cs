// swapfield -- P-G5: `ref obj.Field` / `ref Type.Field` passed to a SHARED
// generic method's byref T parameter (`SwapRef<T>(ref T, ref T)`).
//
// The shared clone's byref T parameters are Il2CppObject** object-ABI slots,
// so a VALUE field's address must be bridged: box the field, pass the box's
// address, then write the unboxed result back INTO the field. MEASURED on
// net48 (this case's oracle run): every shape performs a real swap of the two
// fields, and `Bump(ref o.I)` -- where the callee assigns default(T) -- leaves
// the FIELD at 0 rather than throwing.
using System;

public class SwapFieldBox
{
    public int I;
    public long L;
    public double D;
    public string S;
    public bool B;
}

public class SwapFieldSwap
{
    // Shared (object-ABI) generic: byref T params are Il2CppObject** slots.
    public static void SwapRef<T>(ref T a, ref T b)
    {
        T t = a; a = b; b = t;
    }

    public static void Bump<T>(ref T a)
    {
        a = default(T);
    }

    public static int StaticI;
    public static long StaticL;
}

namespace Cases
{
    public static class SwapField
    {
        public static void Main()
        {
            var o = new SwapFieldBox { I = 10, L = 20, D = 1.5, S = "ab", B = true };
            var p = new SwapFieldBox { I = 30, L = 40, D = 2.5, S = "cd", B = false };

            SwapFieldSwap.SwapRef(ref o.I, ref p.I);
            Console.WriteLine("[i]=" + o.I + "," + p.I);

            SwapFieldSwap.SwapRef(ref o.L, ref p.L);
            Console.WriteLine("[l]=" + o.L + "," + p.L);

            SwapFieldSwap.SwapRef(ref o.D, ref p.D);
            Console.WriteLine("[d]=" + o.D.ToString("R") + "," + p.D.ToString("R"));

            SwapFieldSwap.SwapRef(ref o.S, ref p.S);
            Console.WriteLine("[s]=" + o.S + "," + p.S);

            SwapFieldSwap.SwapRef(ref o.B, ref p.B);
            Console.WriteLine("[b]=" + o.B + "," + p.B);

            SwapFieldSwap.Bump(ref o.I);
            Console.WriteLine("[bump]=" + o.I);

            // Static FIELD addresses reach the same bridge through a dotted
            // AssignStmt instead of a FieldAssignStmt.
            SwapFieldSwap.StaticI = 11;
            SwapFieldSwap.StaticL = 22;
            int si = 33;
            long sl = 44;
            SwapFieldSwap.SwapRef(ref SwapFieldSwap.StaticI, ref si);
            SwapFieldSwap.SwapRef(ref SwapFieldSwap.StaticL, ref sl);
            Console.WriteLine("[si]=" + SwapFieldSwap.StaticI + "," + si);
            Console.WriteLine("[sl]=" + SwapFieldSwap.StaticL + "," + sl);
            SwapFieldSwap.Bump(ref SwapFieldSwap.StaticL);
            Console.WriteLine("[sbump]=" + SwapFieldSwap.StaticL);
        }
    }
}
