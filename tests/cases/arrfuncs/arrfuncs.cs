//------------------------------------------------------------------------------
// arrfuncs.cs -- System.Array Find/Exists/TrueForAll/ConvertAll/Resize DEEP
// e2e reference case (net48 oracle, byte-for-byte). Created in e2e batch C11.
//
// Targets the M6 batch-32 RUNTIME icalls (arrayext2.cpp / il2cpp_array_ext2.h:
// icall_array_find_i4 / icall_array_exists_i4 / icall_array_true_for_all_i4 /
// icall_array_convert_all_i4_i4 / icall_array_resize_i4) -- the int32 scope
// the runtime exposes. All probes are int32 arrays with STATIC helper methods
// (the single-cast static delegate ABI the linq/findidx pattern uses; the
// Predicate<int>/Converter<int,int> newobj+ctor interception is CG8,
// CodeGenerator.cs:2280-2291).
//
// net48 semantics exercised:
//   * Array.Find<int>       -> the FIRST element satisfying the predicate,
//                              or default(T)=0 when none (empty -> 0)
//   * Array.Exists<int>     -> any element satisfies (empty -> false)
//   * Array.TrueForAll<int> -> every element satisfies (empty VACUOUSLY true)
//   * Array.ConvertAll      -> a NEW int[] of the same length; the source is
//                              never modified (value-element copy)
//   * Array.Resize<int>     -> grow pads with 0, shrink truncates; null array
//                              becomes new int[newSize]; negative -> AOO
//   * exception contracts: null array / null predicate -> ANE
//
// Byte-stability: every probe prints a label + bool/int; net48 is the oracle.
//
// RECORDED DEFECT (verified at transpile, see the batch C11 report D-ARF-*):
// the METHODMAP side is NOT wired -- MethodMap.Array.cs has only FindIndex
// (icall_array_find_index); the new Find/Exists/TrueForAll/ConvertAll/Resize
// icalls exist on the RUNTIME surface only. A generic-instantiation call
// Array.Find<int>(int[], Predicate<int>) resolves through BclResolve ->
// TryMapArray("Find") -> null -> TryMapArraySort("Find") -> null, so
// MethodRefResolver throws "generic instantiation of System.Array.Find has no
// BCL mapping in M5" and the whole transpile aborts. The net48 oracle is
// captured regardless; the e2e diff for the icall surface is the gap to close
// (the wiring work order is outside this batch's scope -- cases only).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ArrFuncs {
    private static bool IsTwenty(int x) { return x == 20; }
    private static bool IsOdd(int x) { return (x & 1) == 1; }
    private static bool AllPositive(int x) { return x > 0; }
    private static int Double(int x) { return x * 2; }
    private static int PlusTen(int x) { return x + 10; }

    public static void Main() {
        int[] a = { 10, 20, 30, 20, 40 };
        int[] b = { 1, 3, 5 };
        int[] empty = new int[0];

        // ============ A. Array.Find<int> ============
        Console.WriteLine("f_hit=" + (Array.Find(a, IsTwenty) == 20));
        Console.WriteLine("f_firstodd=" + (Array.Find(b, IsOdd) == 1));
        Console.WriteLine("f_miss=" + (Array.Find(b, IsTwenty) == 0));
        Console.WriteLine("f_empty=" + (Array.Find(empty, IsTwenty) == 0));

        // ============ B. Array.Exists<int> ============
        Console.WriteLine("e_true=" + Array.Exists(a, IsTwenty));
        Console.WriteLine("e_false=" + Array.Exists(b, IsTwenty));
        Console.WriteLine("e_empty=" + Array.Exists(empty, IsTwenty));

        // ============ C. Array.TrueForAll<int> ============
        Console.WriteLine("t_allpos=" + Array.TrueForAll(b, AllPositive));
        Console.WriteLine("t_notall=" + Array.TrueForAll(a, AllPositive));
        Console.WriteLine("t_empty=" + Array.TrueForAll(empty, AllPositive));

        // ============ D. Array.ConvertAll<int,int> ============
        int[] d = Array.ConvertAll(b, Double);
        Console.WriteLine("c_len=" + (d.Length == 3));
        Console.WriteLine("c_val=" + (d[0] == 2 && d[1] == 6 && d[2] == 10));
        Console.WriteLine("c_src_untouched=" + (b[0] == 1));
        int[] p = Array.ConvertAll(a, PlusTen);
        Console.WriteLine("c_pten=" + (p[0] == 20 && p[4] == 50));
        Console.WriteLine("c_empty_len=" + (Array.ConvertAll(empty, Double).Length == 0));

        // ============ E. Array.Resize<int> ============
        int[] r = { 1, 2, 3 };
        Array.Resize(ref r, 5);
        Console.WriteLine("rs_grow_len=" + (r.Length == 5));
        Console.WriteLine("rs_grow_pad=" + (r[3] == 0 && r[4] == 0));
        Console.WriteLine("rs_grow_keep=" + (r[0] == 1 && r[2] == 3));
        Array.Resize(ref r, 2);
        Console.WriteLine("rs_shrink_len=" + (r.Length == 2));
        Console.WriteLine("rs_shrink_keep=" + (r[0] == 1 && r[1] == 2));
        int[] nul = null;
        Array.Resize(ref nul, 3);
        Console.WriteLine("rs_null_len=" + (nul != null && nul.Length == 3));
        Console.WriteLine("rs_null_zero=" + (nul != null && nul[0] == 0));

        // ============ F. exception contracts ============
        bool ane1 = false;
        try { Array.Find((int[])null, IsTwenty); } catch (ArgumentNullException) { ane1 = true; }
        Console.WriteLine("ane_arr=" + ane1);
        bool ane2 = false;
        try { Array.Find(a, (Predicate<int>)null); } catch (ArgumentNullException) { ane2 = true; }
        Console.WriteLine("ane_match=" + ane2);
        bool aoo = false;
        try { int[] t = { 1 }; Array.Resize(ref t, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("aoo_neg=" + aoo);
    }
}}
