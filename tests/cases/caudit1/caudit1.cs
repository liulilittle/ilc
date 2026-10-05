//------------------------------------------------------------------------------
// caudit1.cs -- comparer/equality probe #1 (G6-N follow-up, 2026-09-12).
//
// The GENERIC comparer family:
//   * EqualityComparer<int>.Default.Equals(1,1) -- net48: True
//     (ObjectEqualityComparer<int> -> IEquatable<int> -> value equality).
//   * Comparer<double>.Default.Compare(1.0, 2.0) -- net48: -1
//     (GenericComparer<double> -> IComparable<double>).
//
// The runtime mapping status IS the probe: if the owner
// (System.Collections.Generic.EqualityComparer`1 / Comparer`1) is not wired,
// the transpile fails LOUD. That is recorded, not worked around.
//
// Every probe prints a bool / int -- byte-stable across console encodings.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Caudit1 {
    public static void Main() {
        Console.WriteLine("eq_int=" + EqualityComparer<int>.Default.Equals(1, 1));
        Console.WriteLine("cmp_double=" + Comparer<double>.Default.Compare(1.0, 2.0));
    }
}}
