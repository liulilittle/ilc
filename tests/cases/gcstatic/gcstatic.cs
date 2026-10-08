//------------------------------------------------------------------------------
// gcstatic.cs -- STATIC GC-ROOT deep probe (e2e batch C28, "static GC-root deep probe:
// multiple static fields / compaction / static delegates -- after root registration"). Built AFTER commit 01afb99
// (reference static fields now register as GC roots via entry.cpp
// il2cpp_gc_add_static_root) to VERIFY the fix at higher depth than the C27
// gcroot case. gcroot planted ONE field at a time and checked retention; this
// case stresses the MULTI-FIELD root table (8+ reference static fields across
// THREE types), CROSS-TYPE retention (one type's static root keeps an object
// another type's field also references), COMPACTION under sustained pressure
// (10 full collections with 8KiB allocation churn between them -- the
// compacting sweeper must keep rewriting every registered root), and the
// STATIC DELEGATE family (multiple bound-instance delegates kept alive and
// invocable through their static fields).
//
// Runtime contract (gc.cpp): the collector's root set is ONLY registered
// static roots (il2cpp_gc_add_static_root, address-dedup, kMaxStaticRoots=128)
// + frame root ranges; it sweeps unmarked blocks and rewrites ONLY registered
// roots after a compaction. Before 01afb99 an unregistered reference static
// field kept a stale pointer -- after a collection the object is reclaimed or
// MOVED without the field being rewritten, and every WeakReference probe below
// reads False instead of net48's True. This case guards the WHOLE static-root
// table: if ANY registered reference field is missed (TypeEmitter.
// ReferenceStaticFieldCppNames skips a field, or the root table overflows),
// one of the probes flips.
//
// Determinism / net48 ground truth: .NET Framework static reference fields are
// always GC roots, so every retention probe is True; the release/control
// probes (no static reference) are False. Deterministic on BOTH sides: the
// WeakReference targets are planted so their ONLY strong reference is the
// static field (the planter frame is gone at probe time).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcstatic
    {
        // ---- Type Alpha: EIGHT reference static fields (multi-field table) ----
        public static object A1;
        public static object A2;
        public static object A3;
        public static object A4;
        public static string A5;
        public static string A6;
        public static int[] A7;
        public static object[] A8;

        // ---- Type Beta: delegate statics (bound-instance retention) ----
        public static Func<int, int> BDel1;
        public static Func<int, int> BDel2;
        public static object BShared;

        // ---- Type Gamma: cross-type reachability sink ----
        public static object GSink;

        public class Counter
        {
            public int V;
            public Counter(int v) { V = v; }
            public int Add(int x) { return V + x; }
        }

        public class Node
        {
            public Node Next;
            public int Tag;
            public Node(int tag, Node next) { Tag = tag; Next = next; }
        }

        // Planters: the ONLY strong reference escapes through a static field.
        static void PlantA()
        {
            A1 = new object();
            A2 = new object();
            A3 = new object();
            A4 = new object();
            A5 = "alpha-5";
            A6 = "alpha-6";
            A7 = new int[] { 7, 8, 9 };
            A8 = new object[] { new object(), new object(), new object() };
        }

        static void PlantB()
        {
            Counter c1 = new Counter(10);
            Counter c2 = new Counter(20);
            BDel1 = c1.Add;
            BDel2 = c2.Add;
        }

        static void PlantCross()
        {
            // GSink's target is referenced ONLY by the two statics; dropping
            // one side must not kill it while the other side stays rooted.
            Counter c = new Counter(30);
            BShared = c;
            GSink = c;
        }

        static WeakReference DeadTarget()
        {
            return new WeakReference(new object());
        }

        static WeakReference PlantGraph()
        {
            // A chain rooted ONLY at GSink (A1 gets overwritten later).
            Node leaf = new Node(3, null);
            Node mid = new Node(2, leaf);
            Node head = new Node(1, mid);
            GSink = head;
            return new WeakReference(leaf); // reachable via GSink.Next.Next
        }

        public static void Main()
        {
            // ============ 1. multi-field static retention (8 fields) ============
            PlantA();
            WeakReference wA1 = new WeakReference(A1);
            WeakReference wA2 = new WeakReference(A2);
            WeakReference wA3 = new WeakReference(A3);
            WeakReference wA4 = new WeakReference(A4);
            WeakReference wA5 = new WeakReference(A5);
            WeakReference wA6 = new WeakReference(A6);
            WeakReference wA7 = new WeakReference(A7);
            WeakReference wA8 = new WeakReference(A8);
            GC.Collect();
            Console.WriteLine(wA1.IsAlive); // True (A1 static root)
            Console.WriteLine(wA2.IsAlive); // True (A2 static root)
            Console.WriteLine(wA3.IsAlive); // True (A3 static root)
            Console.WriteLine(wA4.IsAlive); // True (A4 static root)
            Console.WriteLine(wA5.IsAlive); // True (A5 string static root)
            Console.WriteLine(wA6.IsAlive); // True (A6 string static root)
            Console.WriteLine(wA7.IsAlive); // True (A7 array static root)
            Console.WriteLine(wA8.IsAlive); // True (A8 object[] static root)
            Console.WriteLine((string)A5 == "alpha-5"); // True (content intact)
            Console.WriteLine(((int[])A7)[2] == 9);     // True (element intact)
            Console.WriteLine(((object[])A8).Length == 3); // True (array intact)

            // ============ 2. compaction survival: 10 collects + 8KiB churn ============
            bool alive = true;
            for (int i = 0; i < 10; i++)
            {
                byte[] junk = new byte[8192];
                GC.Collect();
                alive = alive && wA1.IsAlive && wA2.IsAlive && wA7.IsAlive &&
                        wA8.IsAlive && wA5.IsAlive;
            }
            Console.WriteLine(alive);                    // True (survived compaction)
            Console.WriteLine(ReferenceEquals(A1, wA1.Target)); // True (rewritten by sweeper)

            // ============ 3. static delegate retention + invocation ============
            PlantB();
            object t1 = BDel1.Target;
            object t2 = BDel2.Target;
            WeakReference wb1 = new WeakReference(t1);
            WeakReference wb2 = new WeakReference(t2);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(wb1.IsAlive); // True (delegate 1 target kept)
            Console.WriteLine(wb2.IsAlive); // True (delegate 2 target kept)
            Console.WriteLine(BDel1(5) == 15); // True (c1.V==10)
            Console.WriteLine(BDel2(5) == 25); // True (c2.V==20)
            Console.WriteLine(ReferenceEquals(BDel1.Target, t1)); // True (same instance)

            // ============ 4. cross-type static reachability ============
            PlantCross();
            WeakReference wshare = new WeakReference(BShared);
            GC.Collect();
            Console.WriteLine(wshare.IsAlive); // True (kept via GSink)
            Console.WriteLine(ReferenceEquals(GSink, BShared)); // True (same object both roots)

            // ============ 5. object-graph retention via a static root ============
            WeakReference wleaf = PlantGraph();
            GC.Collect();
            GC.Collect();
            Console.WriteLine(wleaf.IsAlive); // True (leaf reachable GSink.Next.Next)

            // ============ 6. release: null a static, target collected ============
            A2 = null;
            GC.Collect();
            Console.WriteLine(wA2.IsAlive); // False (only strong ref was A2)
            GC.KeepAlive(wA2);

            // ============ 7. control: non-rooted dead target ============
            WeakReference wd = DeadTarget();
            GC.Collect();
            Console.WriteLine(wd.IsAlive); // False (no reachable reference)
        }
    }
}
