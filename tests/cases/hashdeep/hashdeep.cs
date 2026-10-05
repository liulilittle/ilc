//------------------------------------------------------------------------------
// hashdeep.cs -- GetHashCode / dictionary-distribution DEEP probe (e2e batch
// C47): string/int hash CONSISTENCY + Dictionary<string,int> & Hashtable
// key-distribution behavior -- byte-exact vs the net48 oracle. Created after
// the hash/value-semantics domain full chain landed (icall_string_get_hash_code
// in valuesemantics.cpp: the ordinal content hash; icall_int32_get_hash_code:
// the value identity; the dictsi string->int32 dictionary from MethodMap.
// Dictionary.cs; the non-generic Hashtable from il2cpp_hashtable.h).
//
// RANDOMIZATION DISCIPLINE (the point of this case): net48 randomizes
// string.GetHashCode per-process (UseRandomizedStringHashAlgorithm), so a
// RAW string hash VALUE is NOT byte-stable across two processes -- printing
// `"abc".GetHashCode()` would diff-fail by construction. Every string-hash
// probe is therefore a CONSISTENCY RELATIONSHIP that holds identically on
// both runtimes:
//   * equal-content strings (distinct instances) -> equal hash (True);
//   * the same instance probed twice -> equal hash (True);
//   * boxed-int / int32 GetHashCode -> the value identity (both runtimes
//     implement Int32.GetHashCode() == value, deterministic and byte-stable,
//     so these CAN be raw equality probes);
//   * Dictionary/Hashtable distribution is probed through the OBSERVABLE
//     surface only -- Count / ContainsKey / TryGetValue / indexer -- never
//     by enumerating keys (enumeration order is hash-dependent and NOT
//     guaranteed by net48; the wired dictsi surface has no Keys enumeration
//     at all). Every inserted key must remain queryable after load-factor
//     resize (72% growth), and a distinct-content key must miss.
//
// RECORDED DEFECT (documented, not probed): net48 and the runtime use
// DIFFERENT string-hash algorithms (randomized per-process vs fixed ordinal),
// so RAW string hash values differ -- deliberately out of reach.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
namespace Cases { public static class HashDeep {
    public static void Main() {
        // =============== 1. string hash consistency (equal content) ===============
        string a = "abc";
        string b = "abc";                                  // same literal (may be interned)
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());              // True
        // distinct INSTANCE with equal content via StringBuilder round-trip
        string c = new StringBuilder("abc").ToString();
        Console.WriteLine(a == c);                                         // True (content)
        Console.WriteLine(a.GetHashCode() == c.GetHashCode());             // True (equal content -> equal hash)
        Console.WriteLine(a.GetHashCode() == a.GetHashCode());             // True (same instance twice)
        // ordinal: case differs -> content differs (net48: different hash --
        // probed as a RELATIONSHIP via equality against the OTHER case string)
        Console.WriteLine("abc".GetHashCode() != "ABC".GetHashCode());     // True (ordinal, case-sensitive)
        Console.WriteLine("Hello".GetHashCode() == "Hello".GetHashCode()); // True
        Console.WriteLine("".GetHashCode() == "".GetHashCode());           // True (empty string)
        Console.WriteLine("a".GetHashCode() != "b".GetHashCode());         // True (distinct single chars)
        // =============== 2. int32 hash identity (byte-stable raw probes) ===============
        Console.WriteLine(0.GetHashCode() == 0);                            // True
        Console.WriteLine(42.GetHashCode() == 42);                          // True
        Console.WriteLine((-17).GetHashCode() == -17);                      // True
        Console.WriteLine(int.MaxValue.GetHashCode() == int.MaxValue);      // True
        Console.WriteLine(int.MinValue.GetHashCode() == int.MinValue);      // True
        Console.WriteLine(((object)123).GetHashCode() == 123);              // True (boxed int identity)
        // =============== 3. Dictionary<string,int> distribution ===============
        var d = new Dictionary<string, int>();
        // 200 fixed-seed keys -> multiple load-factor resizes (72% growth)
        var rng = new Random(12345);
        for (int i = 0; i < 200; i++) d.Add("k" + i, rng.Next(1000000));
        Console.WriteLine(d.Count == 200);                                  // True
        int found = 0;
        for (int i = 0; i < 200; i++) if (d.ContainsKey("k" + i)) found++;
        Console.WriteLine(found == 200);                                    // True (all keys survive resizes)
        // every value retrievable through the indexer
        int sum = 0;
        var rng2 = new Random(12345);
        for (int i = 0; i < 200; i++) { int v; if (d.TryGetValue("k" + i, out v) && v == rng2.Next(1000000)) sum++; }
        Console.WriteLine(sum == 200);                                      // True (TryGetValue round-trip)
        // distinct-content misses + ordinal sensitivity
        Console.WriteLine(!d.ContainsKey("K0"));                            // True (case differs)
        Console.WriteLine(!d.ContainsKey("k200"));                          // True (never inserted)
        Console.WriteLine(!d.ContainsKey("k"));                            // True
        Console.WriteLine(!d.ContainsKey("k2x"));                          // True (no prefix matching)
        // equal-content lookup through a DIFFERENT instance (dedup by content)
        var probe = new Dictionary<string, int>();
        probe.Add("key", 7);
        Console.WriteLine(probe.ContainsKey(new StringBuilder("key").ToString())); // True
        int pv;
        Console.WriteLine(probe.TryGetValue(new StringBuilder("key").ToString(), out pv) && pv == 7); // True
        // =============== 4. Hashtable distribution (string + boxed int keys) ===============
        var h = new Hashtable();
        for (int i = 0; i < 100; i++) h.Add("s" + i, i * 3);
        Console.WriteLine(h.Count == 100);                                  // True
        int hf = 0;
        for (int i = 0; i < 100; i++) if (h.ContainsKey("s" + i)) hf++;
        Console.WriteLine(hf == 100);                                       // True
        Console.WriteLine((int)h["s50"] == 150);                            // True
        Console.WriteLine(!h.ContainsKey("S50"));                           // True (ordinal)
        // boxed int keys hash by value identity -> all distinct values queryable
        for (int i = 0; i < 50; i++) h.Add(i, "int" + i);
        Console.WriteLine(h.Count == 150);                                  // True
        int hi = 0;
        for (int i = 0; i < 50; i++) if (h.ContainsKey(i)) hi++;
        Console.WriteLine(hi == 50);                                        // True
        Console.WriteLine((string)h[17] == "int17");                        // True
        Console.WriteLine(h[100] == null);                                  // True (missing boxed int key)
        // same VALUE, different box: boxed ints hash equal -> same key
        object k1 = 5, k2 = 5;
        Console.WriteLine(k1.GetHashCode() == k2.GetHashCode());            // True
        var hb = new Hashtable();
        hb.Add(k1, "five");
        Console.WriteLine((string)hb[k2] == "five");                        // True (value-equality key)
        Console.WriteLine(hb.ContainsKey(5));                               // True (literal int lookup)
    }
}}
