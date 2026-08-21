//------------------------------------------------------------------------------
// stringbuilddeep.cs -- StringBuilder domain DEEP probe 2 (e2e batch C47):
// chained Append/Insert/Replace + capacity management -- byte-exact vs the
// net48 oracle. Created after the stringbuilder domain full chain landed
// (il2cpp_stringbuilder.h / stringbuilder.cpp: net48-verified growth policy
// Grow(required) = max(required, max(Length, oldCapacity) * 2), ctor(string)
// preallocates max(DefaultCapacity, value.Length)).
//
// sbchain already covers the happy-path mixed chain (Append/AppendLine/
// AppendFormat/Insert/Remove/Replace) and sblong covers the mass-op stress
// (10000 appends, 500 head-inserts, 2000-unit removes). THIS case is the
// DEEP probe 2:
//   * CHAINED return-value routing: Append(...).Append(...).Insert(...).
//     Remove(...) in a SINGLE statement (receiver chaining) across growth
//     boundaries, verified by Length + exact content at each step.
//   * CAPACITY MANAGEMENT as exact-value probes (net48-verified growth):
//     ctor(4) -> 4, ctor(0) -> 16 (default), ctor(string) prealloc, growth
//     past the doubled capacity, capacity unchanged by Remove/Replace.
//   * REPLACE string/char overloads over multiple occurrences (net48
//     replaces ALL occurrences), Insert at head/middle/tail.
//   * EXCEPTION / edge semantics that stay within the wired surface.
//
// net48 discipline: AppendLine appends "\r\n" (both runtimes are Windows
// processes). All output is deterministic and byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Text;
namespace Cases { public static class StringBuildDeep {
    public static void Main() {
        // =============== 1. Single-statement receiver chain ===============
        StringBuilder sb = new StringBuilder();
        sb.Append("A").Append("B").Append('C').Append(42).AppendLine().Append("tail");
        Console.WriteLine(sb.Length == 11);                        // True ("ABC42\r\ntail")
        Console.WriteLine(sb.ToString() == "ABC42\r\ntail");       // True
        // mid-chain Insert + Remove back to the original content
        sb.Insert(3, "XY").Remove(3, 2);
        Console.WriteLine(sb.ToString() == "ABC42\r\ntail");       // True
        // head Insert + tail Append in one chain
        sb.Insert(0, "[").Append("]");
        Console.WriteLine(sb.ToString() == "[ABC42\r\ntail]");     // True
        Console.WriteLine(sb.Length == 13);                        // True

        // =============== 2. Chain across growth boundaries ===============
        StringBuilder wide = new StringBuilder(4);                 // capacity 4
        Console.WriteLine(wide.Capacity == 4);                     // True (ctor pins)
        wide.Append("0123456789").Append("abcdefghij").Append("ABCDEFGHIJ");
        Console.WriteLine(wide.ToString() == "0123456789abcdefghijABCDEFGHIJ"); // True
        Console.WriteLine(wide.Length == 30);                      // True
        Console.WriteLine(wide.Capacity >= 30);                    // True (grown past default)
        // insert at the head of the grown buffer shifts the whole content
        wide.Insert(0, "HDR|");
        Console.WriteLine(wide.ToString() == "HDR|0123456789abcdefghijABCDEFGHIJ"); // True
        Console.WriteLine(wide.Length == 34);                      // True
        wide.Remove(0, 4).Append("END");
        Console.WriteLine(wide.ToString() == "0123456789abcdefghijABCDEFGHIJEND"); // True

        // =============== 3. Capacity management (exact values, net48-verified) ===============
        StringBuilder c0 = new StringBuilder(0);
        Console.WriteLine(c0.Capacity == 16);                      // True (net48: 0 -> default)
        StringBuilder cs = new StringBuilder("abc");
        Console.WriteLine(cs.Capacity == 16);                      // True (max(16, 3))
        Console.WriteLine(cs.Length == 3);                         // True
        StringBuilder c20 = new StringBuilder("01234567890123456789");
        Console.WriteLine(c20.Capacity == 20);                     // True (ctor prealloc, no growth)
        Console.WriteLine(c20.Length == 20);                       // True
        // growth: required exceeds doubled capacity -> required wins
        StringBuilder g = new StringBuilder(4);
        g.Append("0123456789");                                    // required 10 > max(0,4)*2=8 -> 10
        Console.WriteLine(g.Capacity == 10);                       // True
        g.Append("abcdefghij");                                    // required 20 > max(10,10)*2=20 -> 20
        Console.WriteLine(g.Capacity == 20);                       // True
        g.Append("XY");                                            // required 22 > max(20,20)*2=40 -> 40
        Console.WriteLine(g.Capacity == 40);                       // True
        Console.WriteLine(g.Length == 22);                         // True
        // Remove / Replace do NOT shrink the capacity
        g.Remove(0, 22);
        Console.WriteLine(g.Length == 0);                          // True
        Console.WriteLine(g.Capacity == 40);                       // True (capacity kept)
        g.Append("abc").Replace("a", "Z");
        Console.WriteLine(g.ToString() == "Zbc");                  // True
        Console.WriteLine(g.Capacity == 40);                       // True (no shrink)
        Console.WriteLine(g.Length == 3);                          // True

        // =============== 4. Replace: string + char overloads, all occurrences ===============
        StringBuilder r = new StringBuilder("aa-bb-aa-cc-aa");
        r.Replace("aa", "X");
        Console.WriteLine(r.ToString() == "X-bb-X-cc-X");          // True (all 3 replaced)
        r.Replace("-", ":");
        Console.WriteLine(r.ToString() == "X:bb:X:cc:X");          // True
        r.Replace('X', 'Q').Replace(':', ' ');
        Console.WriteLine(r.ToString() == "Q bb Q cc Q");          // True
        // Replace that disappears entirely + null new value (net48: removed)
        StringBuilder r2 = new StringBuilder("one two three");
        r2.Replace("two", "");
        Console.WriteLine(r2.ToString() == "one  three");          // True
        r2.Replace("three", null);
        Console.WriteLine(r2.ToString() == "one  ");               // True
        Console.WriteLine(r2.Length == 5);                         // True

        // =============== 5. Insert positions: head / middle / tail ===============
        StringBuilder p = new StringBuilder("hello");
        p.Insert(5, "!");
        Console.WriteLine(p.ToString() == "hello!");               // True (tail)
        p.Insert(0, "<");
        Console.WriteLine(p.ToString() == "<hello!");              // True (head)
        p.Insert(2, "??");
        Console.WriteLine(p.ToString() == "<h??ello!");            // True (middle)
        Console.WriteLine(p.Length == 9);                          // True

        // =============== 6. AppendFormat with 1/2/3 args in a chain ===============
        StringBuilder f = new StringBuilder();
        f.AppendFormat("{0}", 7).Append(' ').AppendFormat("{0}-{1}", 1, 2);
        Console.WriteLine(f.ToString() == "7 1-2");                // True
        f.Append('-').AppendFormat("{0}{1}{2}", "x", "y", "z");
        Console.WriteLine(f.ToString() == "7 1-2-xyz");            // True
        Console.WriteLine(f.Length == 9);                          // True

        // =============== 7. Append(object) dispatch + null no-ops ===============
        StringBuilder o = new StringBuilder();
        o.Append((object)123).Append(' ').Append((object)"txt");
        Console.WriteLine(o.ToString() == "123 txt");              // True
        o.Append((object)null).Append((string)null);               // both no-ops
        Console.WriteLine(o.ToString() == "123 txt");              // True
        Console.WriteLine(o.Length == 7);                          // True

        // =============== 8. Exceptions / null receiver (net48 semantics) ===============
        try { new StringBuilder(-1); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE1"); }
        try { StringBuilder n = null; Console.WriteLine(n.Length); } catch (NullReferenceException) { Console.WriteLine("NRE"); }
        StringBuilder e = new StringBuilder("abc");
        try { e.Insert(99, "x"); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE2"); }
        try { e.Insert(-1, "x"); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE3"); }
        try { e.Remove(2, 99); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE4"); }
        try { e.Remove(-1, 1); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE5"); }
        Console.WriteLine(e.ToString() == "abc");                  // True (unchanged by all throws)
    }
}}
