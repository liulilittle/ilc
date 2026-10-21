//------------------------------------------------------------------------------
// b8sortcmp.cs -- B8-1: Array.Sort<T> Comparison<T> / IComparer<T> dispatch.
//
// Every line a fixed record so the net48 oracle and the runtime compare
// byte-for-byte. The case is DISCRIMINATING:
//   * values ABOVE int.MaxValue for uint/long, so a signed compare is caught
//     (a small-valued array cannot tell signed from unsigned);
//   * the comparator returns int.MinValue / int.MaxValue, so a kernel that
//     treats the result as a bool or negates it mis-orders;
//   * the user comparator COUNTS its calls, proving it actually ran (the
//     default comparer would leave the count at 0);
//   * null elements through BOTH a Comparison<T> and an IComparer<T> whose
//     null ordering is the OPPOSITE of the default (null-last);
//   * single-element, empty and sub-range (i, i) shapes;
//   * IComparer<T> reached through an INTERFACE-typed local (the type
//     tracking may be lost at the call site);
//   * Comparer<T>.Default and Comparer<T>.Create for every wired family.
//
// Deliberately NOT here (recorded gaps -- NOT SILENT, see the B8-1 report):
//   * ulong (0x0B) -- every Array.Sort overload is LOUD at TRANSPILE time:
//       "Array.Sort<T> generic argument 0x0B with N arg(s) not wired in M5".
//     The runtime has NO uint64 sort kernel: il2cpp_array_sort_uint32/_int64
//     are SIGNED-order kernels, the comparer family has no U8 member, and
//     RequireAnySortableStride accepts a kindless 8-byte cell only via the
//     int64 path. Needed kernel (B8-2):
//       void il2cpp_array_sort_uint64(Il2CppArray*);
//       void il2cpp_array_sort_uint64_comparison(Il2CppArray*, Il2CppObject*);
//       void il2cpp_array_sort_icomparer_uint64(Il2CppArray*, Il2CppObject*);
//       enum: kComparerFamilyU8 (fragment "(l,l)" -- the int64 carrier; the
//         slot match is a SIGNATURE-STRING fragment and the emit side maps
//         ulong onto int64, so "(q,q)" would never match a user
//         IComparer<ulong>.Compare(ulong,ulong) slot);
//       plus elemKind 8 stamped for System.UInt64 newarr.
//   * decimal (0x11) -- same: LOUD at transpile time ("0x11 with N arg(s) not
//     wired in M5"). No decimal sort kernel exists. Needed kernel (B8-2, shares
//     the B7-1 decimal callback typedef):
//       void il2cpp_array_sort_decimal(Il2CppArray*);
//       void il2cpp_array_sort_decimal_comparison(Il2CppArray*, Il2CppObject*);
//         // callback ABI: int32_t (*)(Il2CppDecimal, Il2CppDecimal) and the
//         // instance-bound int32_t (*)(Il2CppObject*, Il2CppDecimal, Il2CppDecimal)
//         // -- 16B BY VALUE (MS x64 hidden sret), NOT the icall result-slot form
//       void il2cpp_array_sort_icomparer_decimal(Il2CppArray*, Il2CppObject*);
//       enum: kComparerFamilyDec (fragment "i(V:System.Decimal,V:System.Decimal)");
//       plus RequireAnySortableStride accepting elemKind 8/16-byte decimal.
//   * Comparer<TClass>.Default: the transpiler emits no user-class default
//     singleton (a compile-time gap independent of Array.Sort).
//
// FIXED in this batch (were silent wrong orders, all reproduced on the
// pre-fix runtime and now covered by records above):
//   * J1 Array.Sort<uint>(a,i,len,Comparer<uint>.Create(rev)) dropped the
//     wrapped delegate -- the "Comparer`1" klass-name shortcut mistook the
//     created comparer for the default singleton, so [u_range_create] came
//     back ascending. The created-comparer test now runs FIRST (the
//     sortedlist.cpp order).
//   * J2 non-generic Array.Sort(keys, items, Comparer.Default) ordered uint
//     KEYS signed ([ng_keys_uint]).
//   * J3 non-generic Array.Sort(Array,i,len,IComparer) / BinarySearch(Array,
//     obj, IComparer) ordered uint signed ([ng_range_uint],
//     [ng_bsearch_uint]).
//   * J4 Array.Sort((Array)float[], Comparer.Default) compared the float BITS
//     as int32 ([ng_float]). This one was PRE-EXISTING (identical on the
//     parent commit), not introduced by the uint work; fixed here because the
//     4-byte branch now dispatches on elemKind (float -> float order).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

public sealed class Node : IComparable<Node>
{
    public int Key;
    public Node(int k) { Key = k; }
    public int CompareTo(Node other) { return Key.CompareTo(other.Key); }
}

public sealed class RevInt : IComparer<int>
{
    public int Compare(int a, int b) { return b.CompareTo(a); }
}

public sealed class RevStr : IComparer<string>
{
    public int Compare(string a, string b) { return string.CompareOrdinal(b, a); }
}

// null sorts LAST (the opposite of the default null-first contract)
public sealed class NullLastStr : IComparer<string>
{
    public int Compare(string a, string b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        return string.CompareOrdinal(a, b);
    }
}

public sealed class RevUint : IComparer<uint>
{
    public int Compare(uint a, uint b) { return b.CompareTo(a); }
}

public sealed class RevLong : IComparer<long>
{
    public int Compare(long a, long b) { return b.CompareTo(a); }
}

public sealed class RevDouble : IComparer<double>
{
    public int Compare(double a, double b) { return b.CompareTo(a); }
}

public sealed class RevNode : IComparer<Node>
{
    public int Compare(Node a, Node b) { return b.Key.CompareTo(a.Key); }
}

public static class Program
{
    static int s_calls;

    static string Ji3(int[] a) { return a[0] + "," + a[1] + "," + a[2]; }
    static string Jl3(long[] a) { return a[0] + "," + a[1] + "," + a[2]; }
    static string Ju3(uint[] a) { return a[0] + "," + a[1] + "," + a[2]; }
    static string Jd3(double[] a) { return a[0] + "," + a[1] + "," + a[2]; }
    static string Js3(string[] a) { return (a[0] ?? "null") + "," + (a[1] ?? "null") + "," + (a[2] ?? "null"); }
    static string Jo3(object[] a) { return (a[0] ?? "null") + "," + (a[1] ?? "null") + "," + (a[2] ?? "null"); }
    static string Jc3(Node[] a) { return a[0].Key + "," + a[1].Key + "," + a[2].Key; }
    static string Ji5(int[] a) { return a[0] + "," + a[1] + "," + a[2] + "," + a[3] + "," + a[4]; }
    static string Ju5(uint[] a) { return a[0] + "," + a[1] + "," + a[2] + "," + a[3] + "," + a[4]; }
    static string Jd5(double[] a) { return a[0] + "," + a[1] + "," + a[2] + "," + a[3] + "," + a[4]; }

    public static void Main()
    {
        //========== int: all five overload families ==========
        { var a = new int[] { 3, 1, 2 }; Array.Sort(a); Console.WriteLine("[i_default]=" + Ji3(a)); }
        { var a = new int[] { 3, 1, 2 }; Array.Sort(a, (Comparison<int>)((x, y) => y.CompareTo(x))); Console.WriteLine("[i_comparison]=" + Ji3(a)); }
        { var a = new int[] { 3, 1, 2 }; Array.Sort(a, new RevInt()); Console.WriteLine("[i_icomparer]=" + Ji3(a)); }
        { var a = new int[] { 3, 1, 2 }; Array.Sort(a, Comparer<int>.Default); Console.WriteLine("[i_cmpdefault]=" + Ji3(a)); }
        { var a = new int[] { 3, 1, 2 }; Array.Sort(a, Comparer<int>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[i_cmpcreate]=" + Ji3(a)); }
        { IComparer<int> c = new RevInt(); var a = new int[] { 3, 1, 2 }; Array.Sort(a, c); Console.WriteLine("[i_icmp_iface]=" + Ji3(a)); }
        { IComparer<int> c = Comparer<int>.Create((x, y) => y.CompareTo(x)); var a = new int[] { 3, 1, 2 }; Array.Sort(a, c); Console.WriteLine("[i_create_iface]=" + Ji3(a)); }
        { var a = new int[] { 5, 4, 3, 2, 1 }; Array.Sort(a, 1, 3, new RevInt()); Console.WriteLine("[i_range]=" + Ji5(a)); }
        { var a = new int[] { 5, 4, 3, 2, 1 }; Array.Sort(a, 1, 3, Comparer<int>.Default); Console.WriteLine("[i_range_def]=" + Ji5(a)); }

        //========== uint: signedness boundary (> int.MaxValue) ==========
        // A signed kernel sorts 4000000000 as a negative number.
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a); Console.WriteLine("[u_default]=" + Ju3(a)); }
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, (Comparison<uint>)((x, y) => y.CompareTo(x))); Console.WriteLine("[u_comparison]=" + Ju3(a)); }
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, new RevUint()); Console.WriteLine("[u_icomparer]=" + Ju3(a)); }
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, Comparer<uint>.Default); Console.WriteLine("[u_cmpdefault]=" + Ju3(a)); }
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, Comparer<uint>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[u_cmpcreate]=" + Ju3(a)); }
        { var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, (IComparer<uint>)null); Console.WriteLine("[u_nullcmp]=" + Ju3(a)); }
        { IComparer<uint> c = Comparer<uint>.Default; var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, c); Console.WriteLine("[u_def_iface]=" + Ju3(a)); }
        { var a = new uint[] { 9u, 4000000000u, 1u, 3000000000u, 2u }; Array.Sort(a, 1, 3, new RevUint()); Console.WriteLine("[u_range]=" + Ju5(a)); }
        { var a = new uint[] { 9u, 4000000000u, 1u, 3000000000u, 2u }; Array.Sort(a, 1, 3, (IComparer<uint>)null); Console.WriteLine("[u_range_def]=" + Ju5(a)); }

        // uint through a Comparer<uint>.Create behind an INTERFACE local
        { IComparer<uint> c = Comparer<uint>.Create((x, y) => y.CompareTo(x)); var a = new uint[] { 4000000000u, 1u, 3000000000u }; Array.Sort(a, c); Console.WriteLine("[u_create_iface]=" + Ju3(a)); }
        // uint range + Create: the wrapped delegate MUST run (a default-order
        // shortcut here silently drops it and returns ascending)
        { var a = new uint[] { 9u, 4000000000u, 1u, 3000000000u, 2u }; Array.Sort(a, 1, 3, Comparer<uint>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[u_range_create]=" + Ju5(a)); }
        // uint comparator result extremes (int.MinValue / int.MaxValue)
        {
            var a = new uint[] { 4000000000u, 1u, 3000000000u };
            Array.Sort(a, (Comparison<uint>)((x, y) => x == y ? 0 : (x < y ? int.MinValue : int.MaxValue)));
            Console.WriteLine("[u_minmax]=" + Ju3(a));
        }

        //========== non-generic Array surface over uint cells ==========
        // Sort(keys, items, Comparer.Default): the uint KEY order is unsigned
        {
            var keys = new uint[] { 4000000000u, 1u, 3000000000u };
            var items = new string[] { "big", "one", "mid" };
            Array.Sort((Array)keys, (Array)items, System.Collections.Comparer.Default);
            Console.WriteLine("[ng_keys_uint]=" + Ju3(keys) + "|" + items[0] + items[1] + items[2]);
        }
        // Sort(Array, index, length, IComparer) over uint
        {
            var a = new uint[] { 9u, 4000000000u, 1u, 3000000000u, 2u };
            Array.Sort((Array)a, 1, 3, System.Collections.Comparer.Default);
            Console.WriteLine("[ng_range_uint]=" + Ju5(a));
        }
        // BinarySearch(Array, object, IComparer) over uint
        {
            var a = new uint[] { 1u, 3000000000u, 4000000000u };
            Console.WriteLine("[ng_bsearch_uint]=" + Array.BinarySearch((Array)a, 3000000000u, System.Collections.Comparer.Default));
        }
        // J4 (pre-existing): float[] through the non-generic comparer sort must
        // order by FLOAT value, not by the int32 image of the bits
        {
            var a = new float[] { 3.5f, -7.5f, 2.5f, -1.5f };
            Array.Sort((Array)a, System.Collections.Comparer.Default);
            Console.WriteLine("[ng_float]=" + a[0] + "," + a[1] + "," + a[2] + "," + a[3]);
        }

        //========== long: same boundary above int.MaxValue ==========
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a); Console.WriteLine("[l_default]=" + Jl3(a)); }
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a, (Comparison<long>)((x, y) => y.CompareTo(x))); Console.WriteLine("[l_comparison]=" + Jl3(a)); }
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a, new RevLong()); Console.WriteLine("[l_icomparer]=" + Jl3(a)); }
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a, Comparer<long>.Default); Console.WriteLine("[l_cmpdefault]=" + Jl3(a)); }
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a, Comparer<long>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[l_cmpcreate]=" + Jl3(a)); }
        { var a = new long[] { 4000000000L, 1L, 3000000000L }; Array.Sort(a, (IComparer<long>)null); Console.WriteLine("[l_nullcmp]=" + Jl3(a)); }

        //========== double: Comparison / IComparer / Create ==========
        { var a = new double[] { 3.5, 1.5, 2.5 }; Array.Sort(a); Console.WriteLine("[d_default]=" + Jd3(a)); }
        { var a = new double[] { 3.5, 1.5, 2.5 }; Array.Sort(a, (Comparison<double>)((x, y) => y.CompareTo(x))); Console.WriteLine("[d_comparison]=" + Jd3(a)); }
        { var a = new double[] { 3.5, 1.5, 2.5 }; Array.Sort(a, new RevDouble()); Console.WriteLine("[d_icomparer]=" + Jd3(a)); }
        { var a = new double[] { 3.5, 1.5, 2.5 }; Array.Sort(a, Comparer<double>.Default); Console.WriteLine("[d_cmpdefault]=" + Jd3(a)); }
        { var a = new double[] { 3.5, 1.5, 2.5 }; Array.Sort(a, Comparer<double>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[d_cmpcreate]=" + Jd3(a)); }
        { var a = new double[] { 9, 2, 3, 4, 8 }; Array.Sort(a, 1, 3, new RevDouble()); Console.WriteLine("[d_range]=" + Jd5(a)); }
        { var a = new double[] { 9, 2, 3, 4, 8 }; Array.Sort(a, 1, 3, Comparer<double>.Create((x, y) => y.CompareTo(x))); Console.WriteLine("[d_range_create]=" + Jd5(a)); }

        //========== string: Comparison / IComparer / Default / Create ==========
        { var a = new string[] { "c", "a", "b" }; Array.Sort(a); Console.WriteLine("[s_default]=" + Js3(a)); }
        { var a = new string[] { "c", "a", "b" }; Array.Sort(a, (Comparison<string>)((x, y) => string.CompareOrdinal(y, x))); Console.WriteLine("[s_comparison]=" + Js3(a)); }
        { var a = new string[] { "c", "a", "b" }; Array.Sort(a, new RevStr()); Console.WriteLine("[s_icomparer]=" + Js3(a)); }
        { var a = new string[] { "c", "a", "b" }; Array.Sort(a, Comparer<string>.Default); Console.WriteLine("[s_cmpdefault]=" + Js3(a)); }
        { var a = new string[] { "c", "a", "b" }; Array.Sort(a, Comparer<string>.Create((x, y) => string.CompareOrdinal(y, x))); Console.WriteLine("[s_cmpcreate]=" + Js3(a)); }
        { IComparer<string> c = new RevStr(); var a = new string[] { "c", "a", "b" }; Array.Sort(a, c); Console.WriteLine("[s_icmp_iface]=" + Js3(a)); }

        //========== class: Comparison / IComparer / Create ==========
        { var a = new Node[] { new Node(3), new Node(1), new Node(2) }; Array.Sort(a); Console.WriteLine("[c_default]=" + Jc3(a)); }
        { var a = new Node[] { new Node(3), new Node(1), new Node(2) }; Array.Sort(a, (Comparison<Node>)((x, y) => y.Key.CompareTo(x.Key))); Console.WriteLine("[c_comparison]=" + Jc3(a)); }
        { var a = new Node[] { new Node(3), new Node(1), new Node(2) }; Array.Sort(a, new RevNode()); Console.WriteLine("[c_icomparer]=" + Jc3(a)); }
        { var a = new Node[] { new Node(3), new Node(1), new Node(2) }; Array.Sort(a, Comparer<Node>.Create((x, y) => y.Key.CompareTo(x.Key))); Console.WriteLine("[c_cmpcreate]=" + Jc3(a)); }

        //========== object: Comparison / Default ==========
        { var a = new object[] { "c", "a", "b" }; Array.Sort(a); Console.WriteLine("[o_default]=" + Jo3(a)); }
        { var a = new object[] { "c", "a", "b" }; Array.Sort(a, (Comparison<object>)((x, y) => string.CompareOrdinal((string)y, (string)x))); Console.WriteLine("[o_comparison]=" + Jo3(a)); }
        { var a = new object[] { "c", "a", "b" }; Array.Sort(a, Comparer<object>.Default); Console.WriteLine("[o_cmpdefault]=" + Jo3(a)); }

        //========== the comparator contract, not just the ordering ==========
        // int.MinValue / int.MaxValue as the RESULT (a bool-style kernel fails)
        {
            var a = new int[] { 3, 1, 2 };
            Array.Sort(a, (Comparison<int>)((x, y) => x == y ? 0 : (x < y ? int.MinValue : int.MaxValue)));
            Console.WriteLine("[minmax]=" + Ji3(a));
        }
        // the user comparator must actually RUN (count > 0)
        {
            s_calls = 0;
            var a = new int[] { 5, 4, 3, 2, 1 };
            Array.Sort(a, (Comparison<int>)((x, y) => { s_calls++; return x.CompareTo(y); }));
            Console.WriteLine("[calls]=" + (s_calls > 0 ? "yes" : "no") + ":" + Ji5(a));
        }
        // the user comparator must actually RUN through IComparer too
        {
            s_calls = 0;
            var a = new int[] { 5, 4, 3, 2, 1 };
            Array.Sort(a, new CountingInt());
            Console.WriteLine("[calls_icmp]=" + (s_calls > 0 ? "yes" : "no") + ":" + Ji5(a));
        }
        // null elements: Comparison sees them and decides
        { var a = new string[] { "b", null, "a" }; Array.Sort(a, (Comparison<string>)((x, y) => string.CompareOrdinal(x, y))); Console.WriteLine("[null_cmp]=" + Js3(a)); }
        // null elements: an IComparer with the OPPOSITE null ordering (null last)
        { var a = new string[] { "b", null, "a" }; Array.Sort(a, new NullLastStr()); Console.WriteLine("[null_icmp]=" + Js3(a)); }
        // single / empty are no-ops (no throw)
        { var a = new int[] { 7 }; Array.Sort(a, (Comparison<int>)((x, y) => y.CompareTo(x))); Console.WriteLine("[single]=" + a[0]); }
        { var a = new int[0]; Array.Sort(a, (Comparison<int>)((x, y) => y.CompareTo(x))); Console.WriteLine("[empty]=" + a.Length); }
        // all-equal keys
        { var a = new int[] { 1, 1, 1, 1 }; Array.Sort(a, (Comparison<int>)((x, y) => y.CompareTo(x))); Console.WriteLine("[equal]=" + a[0] + "," + a[3]); }
    }

    public sealed class CountingInt : IComparer<int>
    {
        public int Compare(int a, int b) { s_calls++; return a.CompareTo(b); }
    }
}
