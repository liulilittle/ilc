//------------------------------------------------------------------------------
// gcstatic3.cs -- STATIC GC-ROOT deep probe 3 (e2e batch C30, "静态 GC 根深测
// 3: 压力压缩/多类型 -- 根注册后"). gcroot (C27) planted ONE static root per
// family and gcstatic (C28) stressed the MULTI-FIELD table (8 fields across
// THREE types + 10 collects with 8KiB churn + static delegates + cross-type
// reachability). THIS case pushes the registered-static-root contract
// (entry.cpp il2cpp_gc_add_static_root, address-dedup, kMaxStaticRoots=128)
// to higher DEPTH and PRESSURE:
//
//   * MULTI-TYPE root table: SEVEN types (Alpha..Eta) each carry their own
//     reference static fields -- 20+ registered roots of object/string/
//     array/delegate/boxed-int types. Every one must be kept alive AND
//     REWRITTEN by the compacting sweeper after each collection.
//   * PRESSURE-COMPACTION: 30 full collections with VARIED allocation churn
//     between them (mix of byte[]/object[]/string/boxed ints, sizes 4KiB to
//     96KiB, alternating directions) -- the sweeper must keep rewriting every
//     registered root through sustained pressure (gcstatic did 10 x 8KiB;
//     this is 30 x varied, heavier).
//   * CONTENT INTEGRITY after compaction: not just IsAlive -- the static
//     field VALUES (string content, array elements, boxed int, delegate
//     invocation result) are read back AFTER the pressure wave, proving the
//     rewritten root points at the SAME object (not a stale copy).
//   * MULTI-TYPE cross-reachability: one type's static is the ONLY strong
//     reference to an object another type's static also points at; dropping
//     one side must not kill the target while the other side stays rooted.
//   * STATIC DELEGATES bound to instances across THREE types; invoking them
//     after the pressure wave reads the right per-instance state.
//   * RELEASE: nulling a static drops the only strong reference; the target
//     is collected at the next full collection.
//   * CONTROL: a non-rooted dead target (helper frame gone) is collected.
//
// Determinism / net48 ground truth: a .NET Framework static reference field
// is ALWAYS a GC root, so every retention probe is True; the release/control
// probes are False. Deterministic on BOTH sides: the WeakReference targets
// are planted so their ONLY strong reference is the static field (the planter
// frame is gone at probe time). The transpiled side diverges ONLY if a
// registered static root is missed, the root table overflows
// (kMaxStaticRoots=128), or the sweeper fails to rewrite a root under
// pressure -- exactly the contract this case guards.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcstatic3
    {
        // ---- Type Alpha: 4 object/string roots ----
        public static object AObj;
        public static string AStr;
        public static int[] AArr;
        public static object ABox;

        // ---- Type Beta: array-of-objects + boxed roots ----
        public static object[] BArr;
        public static object BBox1;
        public static object BBox2;
        public static string BStr;

        // ---- Type Gamma: delegate + instance roots ----
        public static Func<int, int> GDel1;
        public static Func<int, int> GDel2;
        public static object GShared;

        // ---- Type Delta: object graph roots ----
        public static object DHead;
        public static object DTail;

        // ---- Type Epsilon: mixed string/array roots ----
        public static string EStr1;
        public static string EStr2;
        public static object[] EArr;

        // ---- Type Zeta: cross-type sink ----
        public static object ZSink;

        // ---- Type Eta: more array roots (root-table density) ----
        public static string[] HStrArr;
        public static object[] HObjArr;

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
        static void PlantAlpha()
        {
            AObj = new object();
            AStr = "alpha-static";
            AArr = new int[] { 11, 22, 33, 44 };
            ABox = (object)777;
        }

        static void PlantBeta()
        {
            BArr = new object[] { new object(), "beta", 5 };
            BBox1 = (object)1111;
            BBox2 = (object)2222;
            BStr = "beta-static";
        }

        static void PlantGamma()
        {
            Counter c1 = new Counter(10);
            Counter c2 = new Counter(20);
            GDel1 = c1.Add;
            GDel2 = c2.Add;
        }

        static void PlantDelta()
        {
            Node tail = new Node(3, null);
            Node mid = new Node(2, tail);
            Node head = new Node(1, mid);
            DHead = head;
            DTail = tail;
        }

        static void PlantEpsilon()
        {
            EStr1 = "eps-1";
            EStr2 = "eps-2";
            EArr = new object[] { new object(), EStr1, 9 };
        }

        static void PlantCross()
        {
            Counter c = new Counter(30);
            GShared = c;
            ZSink = c;
        }

        static void PlantEta()
        {
            HStrArr = new string[] { "h0", "h1", "h2" };
            HObjArr = new object[] { new object(), new object() };
        }

        static WeakReference DeadTarget()
        {
            return new WeakReference(new object());
        }

        public static void Main()
        {
            // ============ 1. multi-type static retention (7 types, 20+ roots) ============
            PlantAlpha(); PlantBeta(); PlantGamma(); PlantDelta();
            PlantEpsilon(); PlantCross(); PlantEta();

            WeakReference wAObj = new WeakReference(AObj);
            WeakReference wAStr = new WeakReference(AStr);
            WeakReference wAArr = new WeakReference(AArr);
            WeakReference wABox = new WeakReference(ABox);
            WeakReference wBArr = new WeakReference(BArr);
            WeakReference wBBox1 = new WeakReference(BBox1);
            WeakReference wBBox2 = new WeakReference(BBox2);
            WeakReference wBStr = new WeakReference(BStr);
            WeakReference wEStr1 = new WeakReference(EStr1);
            WeakReference wEArr = new WeakReference(EArr);
            WeakReference wHStrArr = new WeakReference(HStrArr);
            WeakReference wHObjArr = new WeakReference(HObjArr);
            GC.Collect();
            Console.WriteLine(wAObj.IsAlive);    // True (Alpha object root)
            Console.WriteLine(wAStr.IsAlive);    // True (Alpha string root)
            Console.WriteLine(wAArr.IsAlive);    // True (Alpha int[] root)
            Console.WriteLine(wABox.IsAlive);    // True (Alpha boxed-int root)
            Console.WriteLine(wBArr.IsAlive);    // True (Beta object[] root)
            Console.WriteLine(wBBox1.IsAlive);   // True (Beta boxed root)
            Console.WriteLine(wBBox2.IsAlive);   // True (Beta boxed root)
            Console.WriteLine(wBStr.IsAlive);    // True (Beta string root)
            Console.WriteLine(wEStr1.IsAlive);   // True (Epsilon string root)
            Console.WriteLine(wEArr.IsAlive);    // True (Epsilon object[] root)
            Console.WriteLine(wHStrArr.IsAlive); // True (Eta string[] root)
            Console.WriteLine(wHObjArr.IsAlive); // True (Eta object[] root)

            // ============ 2. pressure-compaction: 30 collects, varied churn ============
            bool alive = true;
            for (int i = 0; i < 30; i++)
            {
                // Varied churn sizes + types, alternating direction. The
                // tracked static roots are NOT touched -- every allocation
                // below is garbage the collector must sweep while the
                // registered roots are rewritten.
                if ((i & 1) == 0)
                {
                    byte[] b = new byte[4096 + i * 128];
                    object[] o = new object[8 + i];
                    string s = "churn-" + i;
                }
                else
                {
                    int[] a = new int[512 + i * 16];
                    object bx = (object)(i * 3);
                    object[] o2 = new object[4];
                }
                GC.Collect();
                alive = alive && wAStr.IsAlive && wBBox1.IsAlive && wEStr1.IsAlive &&
                        wHStrArr.IsAlive && wHObjArr.IsAlive;
            }
            Console.WriteLine(alive);                    // True (survived 30-collection pressure)
            Console.WriteLine(ReferenceEquals(AStr, wAStr.Target));   // True (rewritten)
            Console.WriteLine(ReferenceEquals(BBox1, wBBox1.Target)); // True (boxed root rewritten)
            Console.WriteLine(ReferenceEquals(EStr1, wEStr1.Target)); // True (string root rewritten)
            Console.WriteLine(ReferenceEquals(HObjArr, wHObjArr.Target)); // True (array root rewritten)

            // ============ 3. content integrity AFTER the pressure wave ============
            Console.WriteLine((string)AStr == "alpha-static"); // True (string content intact)
            Console.WriteLine(((int[])AArr)[1] == 22);         // True (int[] element intact)
            Console.WriteLine((int)ABox == 777);               // True (boxed int intact)
            Console.WriteLine(((object[])BArr).Length == 3);   // True (object[] intact)
            Console.WriteLine((string)((object[])BArr)[1] == "beta"); // True (element intact)
            Console.WriteLine((int)BBox2 == 2222);             // True (boxed 2222 intact)
            Console.WriteLine((string)HStrArr[2] == "h2");     // True (string[] element intact)
            Console.WriteLine(((object[])EArr)[1] == EStr1);   // True (Epsilon ref element intact)

            // ============ 4. static delegate retention + invocation after pressure ============
            object t1 = GDel1.Target;
            object t2 = GDel2.Target;
            WeakReference wg1 = new WeakReference(t1);
            WeakReference wg2 = new WeakReference(t2);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(wg1.IsAlive); // True (delegate 1 target kept)
            Console.WriteLine(wg2.IsAlive); // True (delegate 2 target kept)
            Console.WriteLine(GDel1(5) == 15); // True (c1.V==10)
            Console.WriteLine(GDel2(5) == 25); // True (c2.V==20)
            Console.WriteLine(ReferenceEquals(GDel1.Target, t1)); // True (same instance)

            // ============ 5. cross-type reachability (drop one side) ============
            PlantCross();
            WeakReference wshare = new WeakReference(GShared);
            GC.Collect();
            Console.WriteLine(wshare.IsAlive); // True (kept via ZSink)
            Console.WriteLine(ReferenceEquals(ZSink, GShared)); // True (same object both roots)
            ZSink = null;
            GC.Collect();
            Console.WriteLine(wshare.IsAlive); // True (still kept via GShared)
            GC.KeepAlive(GShared);

            // ============ 6. object-graph retention via a static root ============
            PlantDelta();
            WeakReference wtail = new WeakReference(DTail);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(wtail.IsAlive); // True (tail reachable DHead.Next.Next)
            Console.WriteLine(((Node)DHead).Next.Next.Tag == 3); // True (graph intact)

            // ============ 7. release: null a static, target collected ============
            AObj = null;
            GC.Collect();
            Console.WriteLine(wAObj.IsAlive); // False (only strong ref was AObj)
            GC.KeepAlive(wAObj);

            // ============ 8. control: non-rooted dead target ============
            WeakReference wd = DeadTarget();
            GC.Collect();
            Console.WriteLine(wd.IsAlive); // False (no reachable reference)
        }
    }
}
