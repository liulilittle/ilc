//------------------------------------------------------------------------------
// loud-linq-list-idx.cs -- e2e reference case for the List<T> INDEX predicate /
// selector overloads (net48 Func<TSource,int,bool> / Func<TSource,int,TResult>).
//
// The callback receives the element AND its zero-based source index. The
// runtime ships il2cpp_linq_list_where_idx / il2cpp_linq_list_select_idx
// (linq_list.h) -- the int32-source and reference-source, int32-result shapes
// reuse the array index kernels; the generic path widens each source family
// into its own slot. Before this change both overloads LOUD-rejected ("the
// runtime has no list index-predicate/selector entry").
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class LoudLinqListIdx
    {
        private static bool EvenIndex(int x, int i) { return i % 2 == 0; }
        private static bool OddIndex(int x, int i) { return i % 2 == 1; }
        private static bool EvenIndexLong(long x, int i) { return i % 2 == 0; }
        private static bool EvenIndexDouble(double x, int i) { return i % 2 == 0; }
        private static bool EvenIndexRef(string x, int i) { return i % 2 == 0; }

        private static int AddIndex(int x, int i) { return x + i; }
        private static long AddIndexLong(int x, int i) { return (long)x + i; }
        private static string WithIndex(int x, int i) { return x + ":" + i; }
        private static long AddIndexLongSrc(long x, int i) { return x + i; }
        private static double AddIndexDouble(double x, int i) { return x + i; }
        private static string WithIndexRef(string x, int i) { return x + ":" + i; }

        private static string Join(int[] a) { return string.Join(",", a); }
        private static string JoinL(long[] a) { return string.Join(",", a); }
        private static string JoinD(double[] a) { return string.Join(",", a); }
        private static string JoinS(string[] a) { return string.Join(",", a); }

        public static void Main()
        {
            List<int> il = new List<int> { 10, 20, 30, 40, 50 };

            // ===== Where with an INDEX predicate (int32 source) =====
            Console.WriteLine("[where-idx-even]=" + Join(il.Where(EvenIndex).ToArray()));
            Console.WriteLine("[where-idx-odd]=" + Join(il.Where(OddIndex).ToArray()));

            // ===== Select with an INDEX selector (int32 source -> int32) =====
            Console.WriteLine("[sel-idx-add]=" + Join(il.Select(AddIndex).ToArray()));

            // ===== Select with an INDEX selector (int32 -> long) =====
            Console.WriteLine("[sel-idx-long]=" + JoinL(il.Select(AddIndexLong).ToArray()));

            // ===== Select with an INDEX selector (int32 -> string) =====
            Console.WriteLine("[sel-idx-ref]=" + JoinS(il.Select(WithIndex).ToArray()));

            // ===== Reference source: Where / Select with an index =====
            List<string> sl = new List<string> { "a", "b", "c", "d" };
            Console.WriteLine("[where-idx-ref]=" + JoinS(sl.Where(EvenIndexRef).ToArray()));
            Console.WriteLine("[sel-idx-ref-ref]=" + JoinS(sl.Select(WithIndexRef).ToArray()));

            // ===== Wide source: int64 / double =====
            List<long> ll = new List<long> { 5L, 3L, 9L, 1L };
            Console.WriteLine("[where-idx-i8]=" + JoinL(ll.Where(EvenIndexLong).ToArray()));
            Console.WriteLine("[sel-idx-i8]=" + JoinL(ll.Select(AddIndexLongSrc).ToArray()));

            List<double> dl = new List<double> { 1.5, 2.5, 3.5, 4.5 };
            Console.WriteLine("[where-idx-d]=" + JoinD(dl.Where(EvenIndexDouble).ToArray()));
            Console.WriteLine("[sel-idx-d]=" + JoinD(dl.Select(AddIndexDouble).ToArray()));

            // ===== The 1-arg overloads keep their non-index entries =====
            Console.WriteLine("[where-1arg]=" + Join(il.Where(x => x > 20).ToArray()));
            Console.WriteLine("[sel-1arg]=" + Join(il.Select(x => x * 2).ToArray()));

            // ===== Empty / single-element sources =====
            List<int> empty = new List<int>();
            Console.WriteLine("[where-idx-empty]=" + (empty.Where(EvenIndex).Count() == 0));
            List<int> single = new List<int> { 7 };
            Console.WriteLine("[sel-idx-single]=" + (single.Select(AddIndex).First() == 7));

            Console.WriteLine("done");
        }
    }
}
