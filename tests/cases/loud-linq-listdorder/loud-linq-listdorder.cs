//------------------------------------------------------------------------------
// loud-linq-listdorder.cs -- e2e reference case for the DOUBLE-element List
// key-selector OrderBy entries (net48 OrderBy<TSource,TKey>(List<TSource>,
// Func<TSource,TKey>) with TSource=TKey=double).
//
// The runtime ships il2cpp_linq_list_order_by_key[_desc]_d (linq_list.h): the
// list is snapped through its own accessor (never cast to an array header) and
// the array double key-selector entry runs on the snapshot. Before this change
// a List<double>.OrderBy LOUD-rejected ("the list key-selector surface covers
// int32 and reference elements").
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class LoudLinqListDOrder
    {
        private static double Id(double x) { return x; }
        private static double Neg(double x) { return -x; }

        private static string Join(double[] a) { return string.Join(",", a); }

        public static void Main()
        {
            List<double> dl = new List<double> { 1.5, -2.5, 0.0, 3.25, -1.0 };

            // ===== ascending / descending by the double key =====
            Console.WriteLine("[asc]=" + Join(dl.OrderBy(Id).ToArray()));
            Console.WriteLine("[desc]=" + Join(dl.OrderByDescending(Id).ToArray()));

            // ===== a non-identity double key (negated) =====
            Console.WriteLine("[neg-asc]=" + Join(dl.OrderBy(Neg).ToArray()));

            // ===== stability under equal double keys =====
            List<double> eq = new List<double> { 2.0, 1.0, 2.0, 1.0 };
            double[] st = eq.OrderBy(Id).ToArray();
            Console.WriteLine("[stable]=" + Join(st));

            // ===== copy semantics (the list is never written) =====
            List<double> src = new List<double> { 3.0, 1.0, 2.0 };
            double[] copy = src.OrderBy(Id).ToArray();
            Console.WriteLine("[copy]=" + Join(copy));
            Console.WriteLine("[src-untouched]=" + Join(src.ToArray()));

            // ===== empty / single-element sources =====
            List<double> empty = new List<double>();
            Console.WriteLine("[empty]=" + (empty.OrderBy(Id).Count() == 0));
            List<double> single = new List<double> { 7.5 };
            Console.WriteLine("[single]=" + single.OrderBy(Id).First());

            Console.WriteLine("done");
        }
    }
}
