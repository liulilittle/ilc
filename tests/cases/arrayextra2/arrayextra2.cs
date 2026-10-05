//------------------------------------------------------------------------------
// arrayextra2.cs -- System.Array delegate-combo DEEP probe 2 (e2e batch C43):
// FindIndex / ConvertAll / Find / Exists / TrueForAll / Resize chains --
// byte-exact vs the net48 oracle. Created after the arrayext2 full chain
// (arrfuncs/arrayext2/findidx precedents; MethodMap.Array.cs wir9-b23
// delegate icalls).
//
// Surface discipline:
//   * ALL delegate forms use STATIC HELPER methods (the single-cast static
//     methodPtr ABI -- arrfuncs precedent; no closures so the receiver slot
//     stays null, exactly what the runtime's per-binding dispatch expects).
//   * FindIndex uses ONLY the 4-arg full-window form (arr, start, count,
//     match) -- findidx precedent: the 2/3-arg net48 overloads fold to the
//     4-arg shape in the runtime, so the full-window form is the reachable
//     e2e surface.
//   * int32 scope only (0x08): the runtime exposes no other generic element.
//   * chains feed ConvertAll results back into FindIndex / Exists /
//     TrueForAll / ConvertAll so the results of one delegate op become the
//     source of the next.
//
// RECORDED DEFECT (NOT probed -- unmapped, transpile aborts): Array.ForEach<T>
// (T[], Action<T>) has no MethodMap entry (src/il2cppc has no ForEach
// mapping), so the delegate combo it would exercise is e2e-unreachable until
// the wiring lands. Recorded for the batch report, not fixed.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Arrayextra2 {
    private static bool IsTwelve(int x) { return x == 12; }
    private static bool IsOdd(int x) { return (x & 1) == 1; }
    private static bool IsNegative(int x) { return x < 0; }
    private static bool IsHuge(int x) { return x > 100; }
    private static bool AllPositive(int x) { return x > 0; }
    private static int Square(int x) { return x * x; }
    private static int PlusOne(int x) { return x + 1; }
    private static int Negate(int x) { return -x; }
    private static int Half(int x) { return x / 2; }
    private static int TimesTen(int x) { return x * 10; }
    private static int Double(int x) { return x * 2; }

    public static void Main() {
        int[] data = { 5, 12, 3, 12, 7, 12, 9 };

        // ============ S1: FindIndex (4-arg full-window forms) ============
        Console.WriteLine("fi_full=" + Array.FindIndex(data, 0, data.Length, IsTwelve));
        Console.WriteLine("fi_start=" + Array.FindIndex(data, 2, data.Length - 2, IsTwelve));
        Console.WriteLine("fi_range=" + Array.FindIndex(data, 2, 3, IsTwelve));
        Console.WriteLine("fi_miss=" + Array.FindIndex(data, 0, data.Length, IsHuge));
        Console.WriteLine("fi_odd=" + Array.FindIndex(data, 0, data.Length, IsOdd));
        Console.WriteLine("fi_neg=" + Array.FindIndex(data, 0, data.Length, IsNegative));

        // ============ S2: Find / Exists / TrueForAll ============
        Console.WriteLine("f_hit=" + (Array.Find(data, IsHuge) == 0));
        Console.WriteLine("f_first=" + (Array.Find(data, IsOdd) == 5));
        Console.WriteLine("f_mid=" + (Array.Find(data, AllPositive) == 5));
        Console.WriteLine("e_true=" + Array.Exists(data, IsTwelve));
        Console.WriteLine("e_false=" + Array.Exists(data, IsNegative));
        Console.WriteLine("t_all=" + Array.TrueForAll(data, AllPositive));
        Console.WriteLine("t_not=" + Array.TrueForAll(data, IsOdd));

        // ============ S3: ConvertAll (fresh results, source untouched) ============
        int[] squares = Array.ConvertAll(data, Square);
        Console.WriteLine("c_len=" + (squares.Length == 7));
        Console.WriteLine("c_val=" + (squares[0] == 25 && squares[6] == 81));
        Console.WriteLine("c_src=" + (data[0] == 5 && data[6] == 9));
        int[] plus = Array.ConvertAll(data, PlusOne);
        Console.WriteLine("c_plus=" + (plus[0] == 6 && plus[6] == 10));
        int[] negs = Array.ConvertAll(data, Negate);
        Console.WriteLine("c_neg=" + (negs[0] == -5 && negs[6] == -9));
        int[] halfs = Array.ConvertAll(data, Half);
        Console.WriteLine("c_half=" + (halfs[0] == 2 && halfs[5] == 6));
        int[] emptyIn = new int[0];
        int[] emptyOut = Array.ConvertAll(emptyIn, Square);
        Console.WriteLine("c_empty=" + (emptyOut.Length == 0));

        // ============ S4: delegate chains ============
        int[] scaled = Array.ConvertAll(data, TimesTen);
        Console.WriteLine("chain_ca_fi=" + (Array.FindIndex(scaled, 0, scaled.Length, IsTwelve) == -1));
        Console.WriteLine("chain_ca_fi12=" + (Array.FindIndex(scaled, 0, scaled.Length, x => false) == -1));
        int[] shifted = Array.ConvertAll(data, x => x - 7);
        Console.WriteLine("chain_ex=" + Array.Exists(shifted, IsTwelve));
        Console.WriteLine("chain_ex0=" + (Array.Exists(shifted, x => x == 0)));
        int[] evens = Array.ConvertAll(data, Double);
        Console.WriteLine("chain_tfa=" + Array.TrueForAll(evens, x => x % 2 == 0));
        int[] notevens = Array.ConvertAll(data, x => x * 2 + 1);
        Console.WriteLine("chain_tfanot=" + Array.TrueForAll(notevens, x => x % 2 == 1));
        int[] combined = Array.ConvertAll(Array.ConvertAll(data, Square), PlusOne);
        Console.WriteLine("chain_conv_conv=" + (combined[0] == 26 && combined[6] == 82));
        int[] filtered2 = Array.ConvertAll(data, Half);
        Console.WriteLine("chain_fi_conv=" + Array.FindIndex(filtered2, 0, filtered2.Length, x => x == 6));
        Console.WriteLine("chain_fi_convmiss=" + Array.FindIndex(filtered2, 0, filtered2.Length, x => x == 99));

        // ============ S5: Resize / Empty interaction ============
        int[] grow = { 1, 2, 3 };
        Array.Resize(ref grow, 5);
        Console.WriteLine("rs_len=" + (grow.Length == 5));
        Console.WriteLine("rs_keep=" + (grow[2] == 3));
        Console.WriteLine("rs_zero=" + (grow[4] == 0));
        Array.Resize(ref grow, 2);
        Console.WriteLine("rs_shrink=" + (grow.Length == 2 && grow[1] == 2));
        Console.WriteLine("rs_fi=" + Array.FindIndex(grow, 0, grow.Length, IsTwelve));
        int[] empty = Array.Empty<int>();
        Console.WriteLine("em_len=" + (empty.Length == 0));
        Console.WriteLine("em_fi=" + Array.FindIndex(empty, 0, 0, x => true));
        Console.WriteLine("em_find=" + (Array.Find(empty, AllPositive) == 0));
        Console.WriteLine("em_exists=" + Array.Exists(empty, AllPositive));
        Console.WriteLine("em_tfa=" + Array.TrueForAll(empty, IsHuge));
        int[] resized = Array.ConvertAll(empty, Square);
        Console.WriteLine("em_conv=" + (resized.Length == 0));
    }
}}
