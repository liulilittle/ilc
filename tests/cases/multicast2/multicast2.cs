//------------------------------------------------------------------------------
// multicast2.cs -- DEEP multicast delegate semantics (the delegatecb follow-up).
//
// Builds on delegate2/delegatecase with the deep probes the shallow cases do
// not cover. Every line is the net48 ground truth captured from this very
// file's reference run:
//   1. Combine dedup semantics: net48 does NOT dedup -- a + a (same delegate
//      object, same method+target on both operands) has a 2-element list;
//      combining the SAME multicast object with itself (ab + ab) has a
//      4-element list and is a NEW object (no identity shortcut).
//   2. Remove: net48 removes exactly ONE occurrence per op -- the LAST
//      contiguous match; "remove all" is a loop, each step dropping one
//      occurrence; removal of the last element yields null.
//   3. GetInvocationList order: the flattened leaf list in invocation order
//      for every tree shape (left/right/deep nesting); the elements are the
//      exact leaf delegate OBJECTS (identity check via ReferenceEquals).
//   4. Nested multicast: a multicast combined with a multicast flattens in
//      first-subtree-then-second order; a nested multicast VALUE removes as a
//      contiguous sequence; a leaf removed from a nested chain rebuilds.
//   5. Exception interruption: invoking a chain aborts at the first throwing
//      leaf (net48) -- a mid-chain throw skips every LATER leaf (order probe);
//      a first-leaf throw runs nothing; a last-leaf throw runs everything;
//      the chain is IMMUTABLE (still 3 entries after the throw) and a
//      throws-once leaf shows a SECOND invocation continues from the start.
//
// TRANSPILER-SAFE SUBSET (delegateinst precedent): static-method delegates
// only, the icall_multicast_invoke_i4 fast path (int32 return, <=4 int32
// args). No instance-bound leaves (the transpiler drops the target today).
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Multicast2 {
    private delegate int IntFn(int x);

    private static int order = 0;
    private static bool onceFired = false;

    private static void ResetOrder() { order = 0; }

    private static int A(int x) { order++; Console.WriteLine("A:" + order); return x + 1; }
    private static int B(int x) { order++; Console.WriteLine("B:" + order); return x * 10; }
    private static int C(int x) { order++; Console.WriteLine("C:" + order); return x - 5; }
    private static int D(int x) { order++; Console.WriteLine("D:" + order); return x + 100; }
    private static int Ten(int x) { return 10; }
    private static int Twenty(int x) { return 20; }
    // Always throws for x >= 0 (every call site below passes 1). The x < 0
    // fallback return is unreachable in this test; it exists ONLY because the
    // transpiler emits no trailing return for a throw-only non-void body and
    // MSVC rejects that shape (C4716; delegate2's ThrowsEx has the same gap --
    // see the report).
    private static int Thrower(int x) { if (x >= 0) throw new Exception("boom"); return 0; }
    private static int ThrowsOnce(int x)
    {
        if (!onceFired) { onceFired = true; throw new Exception("once"); }
        return 7;
    }

    public static void Main() {
        IntFn a = A, b = B, c = C, d = D;

        // === 1. Combine dedup semantics (net48: no dedup anywhere) ===
        IntFn aa = a + a;
        Console.WriteLine("1 a+a len: " + aa.GetInvocationList().Length);            // 2
        IntFn aSame = A;                                                              // same method+target, NEW object
        Console.WriteLine("2 same-method len: " + (a + aSame).GetInvocationList().Length); // 2
        IntFn ab = a + b;
        IntFn abab = ab + ab;                                                          // SAME multicast object twice
        Console.WriteLine("3 ab+ab len: " + abab.GetInvocationList().Length);          // 4
        Console.WriteLine("4 ab+ab newobj: " + !object.ReferenceEquals(abab, ab));     // True

        // === 2. Remove: ONE occurrence per op, LAST match, loop to remove all ===
        IntFn aba = ab + a;                                                            // [a,b,a]
        Console.WriteLine("5 aba len: " + aba.GetInvocationList().Length);             // 3
        IntFn abaMinusA = aba - a;                                                     // last a -> [a,b]
        Console.WriteLine("6 aba-a len: " + abaMinusA.GetInvocationList().Length);     // 2
        Console.WriteLine("7 aba-a[0] is a: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[0], a)); // True
        Console.WriteLine("8 aba-a[1] is b: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[1], b)); // True
        IntFn abMinusA = abaMinusA - a;                                                // [a,b] - a -> [b]
        Console.WriteLine("9 [a,b]-a len: " + abMinusA.GetInvocationList().Length);    // 1
        Console.WriteLine("10 [a,b]-a[0] is b: " + object.ReferenceEquals(abMinusA.GetInvocationList()[0], b)); // True
        IntFn gone = abMinusA - b;                                                     // [b] - b -> null
        Console.WriteLine("11 [b]-b null: " + (gone == null));                         // True

        // loop "remove all occurrences of b" from [a,b,a,b]
        IntFn all = a + b; all += a; all += b;                                         // [a,b,a,b]
        Console.WriteLine("12 all len: " + all.GetInvocationList().Length);            // 4
        IntFn cur = all;
        int removals = 0;
        while (true)
        {
            IntFn next = cur - b;
            if (next == null) { removals++; cur = next; break; }
            if (next == cur) break;                                                    // identity: no more matches
            removals++;
            cur = next;
        }
        Console.WriteLine("13 removals: " + removals);                                 // 2 (last b, then first b)
        Console.WriteLine("14 left len: " + cur.GetInvocationList().Length);           // 2 ([a,a])
        Console.WriteLine("15 left[0] is a: " + object.ReferenceEquals(cur.GetInvocationList()[0], a)); // True
        Console.WriteLine("16 left[1] is a: " + object.ReferenceEquals(cur.GetInvocationList()[1], a)); // True
        IntFn aaGone = (a + a) - a - a;                                                // [a,a] - a - a -> null
        Console.WriteLine("17 [a,a]-a-a null: " + (aaGone == null));                   // True

        // === 3. GetInvocationList order + nested multicast ===
        IntFn nested = (a + b) + (c + d);                                              // [[a,b],[c,d]]
        Console.WriteLine("18 nested len: " + nested.GetInvocationList().Length);      // 4
        Console.WriteLine("19 nested[0] is a: " + object.ReferenceEquals(nested.GetInvocationList()[0], a)); // True
        ResetOrder();
        int last = nested(1);                                                          // A:1 B:2 C:3 D:4
        Console.WriteLine("20 nested last: " + last);                                  // 101 (D: 1+100)
        Console.WriteLine("21 nested order: " + order);                                // 4
        IntFn deepR = a + (b + (c + d));                                               // right-deep
        Console.WriteLine("22 deepR len: " + deepR.GetInvocationList().Length);        // 4
        ResetOrder();
        deepR(1);                                                                      // A:1 B:2 C:3 D:4
        Console.WriteLine("23 deepR order: " + order);                                 // 4
        ResetOrder();
        abab(1);                                                                       // A:1 B:2 A:3 B:4
        Console.WriteLine("24 abab order: " + order);                                  // 4

        // === 4. Nested multicast removal ===
        IntFn nestedMinusB = nested - b;                                               // [a,b,c,d] - b -> [a,c,d]
        Console.WriteLine("25 nested-b len: " + nestedMinusB.GetInvocationList().Length); // 3
        Console.WriteLine("26 nested-b[1] is c: " + object.ReferenceEquals(nestedMinusB.GetInvocationList()[1], c)); // True
        IntFn cd = c + d;
        IntFn nestedMinusCd = nested - cd;                                             // remove nested value [c,d]
        Console.WriteLine("27 nested-cd len: " + nestedMinusCd.GetInvocationList().Length); // 2
        Console.WriteLine("28 nested-cd[0] is a: " + object.ReferenceEquals(nestedMinusCd.GetInvocationList()[0], a)); // True
        Console.WriteLine("29 nested-cd[1] is b: " + object.ReferenceEquals(nestedMinusCd.GetInvocationList()[1], b)); // True

        // === 5. Exception interruption chain (net48: abort at first throw) ===
        IntFn t = A; t += Thrower; t += B;                                             // [A, THROW, B]
        ResetOrder();
        try { t(1); } catch (Exception e) { Console.WriteLine("30 ex type: " + e.GetType().Name); } // Exception
        Console.WriteLine("31 mid-throw order: " + order);                             // 1 (B skipped)
        IntFn t2 = Thrower; t2 += A;                                                   // [THROW, A]
        ResetOrder();
        try { t2(1); } catch (Exception) { Console.WriteLine("32 first throws"); }
        Console.WriteLine("33 first-throw order: " + order);                           // 0 (nothing ran)
        IntFn t3 = A; t3 += B; t3 += Thrower;                                          // [A, B, THROW]
        ResetOrder();
        try { t3(1); } catch (Exception) { Console.WriteLine("34 last throws"); }
        Console.WriteLine("35 last-throw order: " + order);                            // 2 (A and B ran)
        Console.WriteLine("36 chain intact: " + (t.GetInvocationList().Length == 3));  // True (immutable)
        // throws-once: the SAME chain re-invokes from the start after the catch
        IntFn once = A; once += ThrowsOnce; once += B;                                 // [A, THROW-ONCE, B]
        ResetOrder();
        onceFired = false;
        try { once(1); } catch (Exception) { Console.WriteLine("37 once threw"); }
        Console.WriteLine("38 once order: " + order);                                  // 1
        int onceLast = once(1);                                                        // A:2 ThrowsOnce:7 B:3
        Console.WriteLine("39 once last: " + onceLast);                                // 10 (B: 1*10)
        Console.WriteLine("40 once order2: " + order);                                 // 3
        // throw inside a NESTED chain skips the rest of the WHOLE list
        IntFn nestedEx = (a + Thrower) + (c + d);
        ResetOrder();
        try { nestedEx(1); } catch (Exception) { Console.WriteLine("41 nested throws"); }
        Console.WriteLine("42 nested-throw order: " + order);                          // 1 (C,D skipped)
    }
}}
