//------------------------------------------------------------------------------
// caudit2.cs -- comparer/equality probe #2 (G6-N follow-up, 2026-09-12).
//
// The IEquatable<T> priority of EqualityComparer<T>.Default plus the boxed
// path:
//   * struct St : IEquatable<St> -- EqualityComparer<St>.Default.Equals must
//     route to IEquatable<St>.Equals (net48: True).
//   * ((object)a).Equals((object)b) -- net48: True (the override / the
//     ValueType field comparison; both are true for a same-field struct).
//
// net48 baseline is the reference; a runtime divergence (or a transpile-time
// LOUD for the unmapped EqualityComparer`1 owner) is the probe result.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Caudit2 {
    struct St : IEquatable<St> {
        public int V;
        public St(int v) { V = v; }
        public bool Equals(St other) { return V == other.V; }
        public override bool Equals(object obj) { return obj is St && V == ((St)obj).V; }
    }

    public static void Main() {
        St a = new St(7);
        St b = new St(7);
        Console.WriteLine("eq_equatable=" + EqualityComparer<St>.Default.Equals(a, b));
        Console.WriteLine("eq_object=" + ((object)a).Equals((object)b));
    }
}}
