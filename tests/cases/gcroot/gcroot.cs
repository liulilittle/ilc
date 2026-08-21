//------------------------------------------------------------------------------
// gcroot.cs -- GC-ROOT deep probe (e2e batch C27, "GC 根深测: 静态引用字段保持/
// 压缩下存活/委托静态 -- GC 根修复后"). Built AFTER commit 01afb99 (reference
// static fields now register as GC roots via entry.cpp il2cpp_gc_add_static_root)
// to VERIFY the fix on the reference-static-field surface and probe its edges.
//
// The runtime collector is a compacting Gen0 GC whose root set is ONLY
// registered static roots (il2cpp_gc_add_static_root) + frame root ranges
// (gc.cpp). Before the fix an unregistered reference static field kept a stale
// pointer after a collection -- the object was reclaimed or moved without the
// field being rewritten. This case makes every retention probe observable
// through a WeakReference: if a static-field-retained object is NOT kept, the
// IsAlive check reads False after the collection instead of net48's True.
//
// Covered families:
//   1. STATIC OBJECT FIELD RETENTION -- an object planted so its ONLY strong
//      reference is a reference static field survives a full collection
//      (WeakReference stays alive; the static field still references it).
//   2. STATIC STRING FIELD RETENTION -- same for a string (reference field);
//      the value reads back intact after the collect.
//   3. COMPACTION SURVIVAL -- a static-field object survives FIVE consecutive
//      full collections with interleaved allocation pressure (the compacting
//      sweeps must keep rewriting the registered root); the field value + a
//      nested field read intact.
//   4. STATIC DELEGATE FIELD -- a static delegate field keeps its bound
//      INSTANCE target alive across collections (delegate Target readback via
//      the static field) AND invoking the delegate afterwards still reads the
//      target's fields correctly (10 + 5 == 15).
//   5. RELEASE -- setting the static field to null drops the only strong
//      reference; the target is collected at the next full collection.
//   6. CONTROL -- a non-rooted dead target (helper frame gone, no static
//      reference) is collected at the next full collection (the weakrefcase
//      DeadTarget pattern).
//
// Determinism / net48 ground truth: a .NET Framework static reference field is
// always a GC root, so every retention probe is True and the release/control
// probes are False. The transpiled side diverges ONLY if a static root fails to
// register -- exactly the 01afb99 defect this case guards.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcroot
    {
        // ---- reference static fields: the GC-root registration surface ----
        public static object RootObj;
        public static object RootStr;
        public static object RootArr;
        public static Func<int, int> RootDel;

        public class Counter
        {
            public int V;
            public Counter(int v) { V = v; }
            public int Add(int x) { return V + x; }
        }

        // Planters: the ONLY strong reference escapes through the static field,
        // so the helper frame does not keep the target alive past the return.
        static void PlantObject() { RootObj = new object(); }
        static void PlantString() { RootStr = new string(new char[] { 'R', 'o', 'o', 't' }); }
        static void PlantArray() { RootArr = new int[] { 1, 2, 3, 4, 5 }; }
        static void PlantCounter() { Counter c = new Counter(10); RootDel = c.Add; }

        static WeakReference DeadTarget()
        {
            return new WeakReference(new object());
        }

        public static void Main()
        {
            // ---- 1. static object field retention ----
            PlantObject();
            object po = RootObj;
            WeakReference w1 = new WeakReference(po);
            GC.Collect();
            Console.WriteLine(w1.IsAlive);                    // True (static object field is a root)
            Console.WriteLine(ReferenceEquals(RootObj, po));  // True (value intact after collect)

            // ---- 2. static string field retention ----
            PlantString();
            WeakReference w2 = new WeakReference(RootStr);
            GC.Collect();
            Console.WriteLine(w2.IsAlive);                    // True (static string field is a root)
            Console.WriteLine((string)RootStr == "Root");     // True (content intact)

            // ---- 3. compaction survival: five collects + allocation pressure ----
            PlantArray();
            object pa = RootArr;
            WeakReference w3 = new WeakReference(pa);
            for (int i = 0; i < 5; i++)
            {
                byte[] junk = new byte[4096];
                GC.Collect();
            }
            Console.WriteLine(w3.IsAlive);                    // True (survived compaction)
            Console.WriteLine(((int[])RootArr)[2] == 3);      // True (element intact)

            // ---- 4. static delegate field keeps its bound instance alive ----
            PlantCounter();
            object target = RootDel.Target;
            WeakReference w4 = new WeakReference(target);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(w4.IsAlive);                    // True (delegate Target kept via static field)
            Console.WriteLine(RootDel(5) == 15);              // True (invoke reads target.V == 10)
            Console.WriteLine(ReferenceEquals(RootDel.Target, target)); // True (same instance)

            // ---- 5. release: nulling the static field drops the root ----
            RootObj = null;
            GC.Collect();
            Console.WriteLine(w1.IsAlive);                    // False (only strong ref was the static field)
            GC.KeepAlive(po);

            // ---- 6. control: non-rooted dead target ----
            WeakReference wd = DeadTarget();
            GC.Collect();
            Console.WriteLine(wd.IsAlive);                    // False (no reachable reference)
        }
    }
}
