//------------------------------------------------------------------------------
// baudit5 -- G6 container-audit probe B5: List<string> sort comparer + comparer
// exception transport.
//
// Audit findings:
//   * the runtime List<reference>.Sort() (listobj.cpp -> il2cpp_array_sort_icomparer
//     -> ComparerCompareCore) compares strings ORDINALLY, while net48
//     List<string>.Sort() uses Comparer<string>.Default (String.CompareTo,
//     culture-sensitive). Array.Sort<string> in this runtime uses the culture
//     comparator (arrayext2.cpp CompareCulture), so the same runtime disagrees
//     with itself.
//   * net48 array/list sorts wrap ANY comparer exception in
//     InvalidOperationException("Failed to compare two elements in the array",
//     inner); the runtime propagates the original exception unwrapped.
//
// SUBSTITUTION NOTE 1: the audit asked for List<int>.Sort(Comparison<int>) for the
// exception-transport probe. List<T>.Sort(IComparer/Comparison) is LOUD-unwired
// in the transpiler (MethodMap.List.cs maps only the parameterless Sort; a call
// would fail at transpile and mask the culture probe). The wired equivalent is
// Array.Sort<int>(int[], Comparison<int>) (il2cpp_array_sort_int32_comparison),
// used here.
//
// SUBSTITUTION NOTE 2: Exception.get_InnerException is not wired in this
// transpiler (transpile warning "get_InnerException: not wired in M5",
// which skipped Main and failed the C++ build), so the wrapper is witnessed by
// the OUTER type instead: the comparer throws ArgumentException.
// net48 wraps it -> InvalidOperationException; the runtime propagates the raw
// ArgumentException. If the runtime also wrapped, both would print
// InvalidOperationException and the line would match.
//
// Expected diff: net48 prints sort=a,B and cmp=InvalidOperationException;
// the runtime prints sort=B,a and cmp=ArgumentException.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Baudit5 {
    static int Boom(int x, int y) { throw new ArgumentException("boom"); }

    public static void Main()
    {
        List<string> ls = new List<string>();
        ls.Add("a");
        ls.Add("B");
        ls.Sort();
        Console.WriteLine("sort=" + ls[0] + "," + ls[1]);

        int[] li = new int[] { 2, 1 };
        try
        {
            Array.Sort(li, new Comparison<int>(Boom));
            Console.WriteLine("cmp=no-throw");
        }
        catch (Exception e)
        {
            Console.WriteLine("cmp=" + e.GetType().Name);
        }
    }
} }
