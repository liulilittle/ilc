//------------------------------------------------------------------------------
// probe-listi4src -- List<int> COLLECTION-SOURCE normalization (e2e, net48
// oracle).
//
// net48 accepts ANY IEnumerable<int> at the three collection-source entry
// points:
//   new List<int>(IEnumerable<int>)
//   List<int>.AddRange(IEnumerable<int>)
//   List<int>.InsertRange(int, IEnumerable<int>)
//
// The runtime used to accept ONLY a one-dimensional int32 ARRAY at all three
// (RequireInt32ArrayArg raised ArgumentException("Object is not an array.")
// for every other shape), so List<int>/yield/LINQ/HashSet sources failed.
//
// Pinned here, per entry point, over the source matrix:
//   int[]              (the fast path: ICollection<int> -> exact capacity)
//   List<int>          (ICollection<int> -> exact capacity; SELF reference
//                       must snapshot, never grow while enumerating)
//   HashSet<int>       (ICollection<int> -> exact capacity, slot order)
//   Queue<int>         (NOT ICollection<int> -> incremental growth: 0/4/8)
//   yield iterator     (NOT ICollection<int> -> incremental growth)
//   LINQ Select        (NOT ICollection<int> -> incremental growth)
//   empty x {array,list,set,queue,yield}
//   null               (ArgumentNullException, paramName "collection")
//   GetEnumerator null (NullReferenceException, like the net48 foreach)
//   mid-enumeration throw (already-inserted/appended prefix retained)
//   get_Current after MoveNext==false must never be read
//   Dispose count (always exactly 1, including on the throwing path)
// plus InsertRange at head/middle/tail/end, empty source, and the
// ArgumentOutOfRangeException shape of an out-of-range index (the net48
// InsertRange uses the element-range resource, NOT the single-Insert one).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeListI4Src
    {
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static string J(List<int> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) s += ",";
                s += I(l[i]);
            }
            return "[" + s + "]";
        }

        private static string Shape(List<int> l)
        {
            return "n=" + I(l.Count) + " c=" + I(l.Capacity) + " " + J(l);
        }

        // Elements ONLY (no Capacity). Used for a LINQ Select source: net48
        // hands the runtime a lazy iterator (not ICollection<int>, so it grows
        // through Add), while this runtime's LINQ kernels materialize eagerly
        // into int[] (which IS ICollection<int> and pre-sizes exactly). The
        // elements are identical either way, the Capacity is not, so only the
        // element sequence is pinned for that source shape.
        private static string Elems(List<int> l)
        {
            return "n=" + I(l.Count) + " " + J(l);
        }

        private static IEnumerable<int> Y(int n)
        {
            for (int i = 0; i < n; i++) yield return i + 1;
        }

        // Throws while enumerating, after yielding two elements.
        private sealed class ThrowMid : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator()
            {
                yield return 1;
                yield return 2;
                throw new InvalidOperationException("boom");
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        // Tracks Dispose and prints every Current read, so the probe pins the
        // call ORDER (MoveNext -> get_Current -> ... -> Dispose exactly once).
        private sealed class Counting : IEnumerable<int>
        {
            public int Disposed;

            public IEnumerator<int> GetEnumerator() { return new CountingEnum(this); }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private sealed class CountingEnum : IEnumerator<int>
            {
                private readonly Counting owner;
                private int i;

                public CountingEnum(Counting owner) { this.owner = owner; }

                public bool MoveNext() { return ++i <= 3; }

                public int Current
                {
                    get
                    {
                        Console.WriteLine("[cur_" + i + "]=" + (i * 10));
                        return i * 10;
                    }
                }

                object System.Collections.IEnumerator.Current { get { return Current; } }

                public void Reset() { i = 0; }

                public void Dispose()
                {
                    owner.Disposed = owner.Disposed + 1;
                    Console.WriteLine("[dispose]=" + owner.Disposed);
                }
            }
        }

        // Enumerates, then throws; Dispose must still run once (foreach finally).
        private sealed class ThrowMidDispose : IEnumerable<int>
        {
            public int Disposed;

            public IEnumerator<int> GetEnumerator() { return new ThrowMidDisposeEnum(this); }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private sealed class ThrowMidDisposeEnum : IEnumerator<int>
            {
                private readonly ThrowMidDispose owner;
                private int i;

                public ThrowMidDisposeEnum(ThrowMidDispose owner) { this.owner = owner; }

                public bool MoveNext()
                {
                    ++i;
                    if (i == 3) throw new InvalidOperationException("boom");
                    return true;
                }

                public int Current { get { return i; } }

                object System.Collections.IEnumerator.Current { get { return Current; } }

                public void Reset() { i = 0; }

                public void Dispose()
                {
                    owner.Disposed = owner.Disposed + 1;
                }
            }
        }

        // Records (and refuses) any get_Current read after MoveNext returned false.
        private sealed class LateCurrent : IEnumerable<int>
        {
            public int AfterEndReads;

            public IEnumerator<int> GetEnumerator() { return new LateCurrentEnum(this); }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private sealed class LateCurrentEnum : IEnumerator<int>
            {
                private readonly LateCurrent owner;
                private int i;

                public LateCurrentEnum(LateCurrent owner) { this.owner = owner; }

                public bool MoveNext() { return ++i <= 2; }

                public int Current
                {
                    get
                    {
                        if (i > 2)
                        {
                            owner.AfterEndReads = owner.AfterEndReads + 1;
                            throw new InvalidOperationException("current after end");
                        }
                        return i;
                    }
                }

                object System.Collections.IEnumerator.Current { get { return Current; } }

                public void Reset() { i = 0; }

                public void Dispose() { }
            }
        }

        // GetEnumerator returns null: net48's foreach dereferences it (NRE).
        private sealed class NullEnum : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() { return null; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private static string Kind(Action a)
        {
            try { a(); return "no"; }
            catch (ArgumentNullException e) { return "ane/" + (e.ParamName ?? "<null>"); }
            catch (ArgumentOutOfRangeException e) { return "aoore/" + (e.ParamName ?? "<null>"); }
            catch (ArgumentException e) { return "ae/" + (e.ParamName ?? "<null>"); }
            catch (NullReferenceException) { return "nre"; }
            catch (InvalidOperationException) { return "ioe"; }
            catch (Exception e) { return e.GetType().Name; }
        }

        private static List<int> AddR(int cap, IEnumerable<int> src)
        {
            List<int> l = new List<int>(cap);
            for (int i = 0; i < cap; i++) l.Add(100 + i);
            l.AddRange(src);
            return l;
        }

        private static string AddKind(int cap, IEnumerable<int> src)
        {
            List<int> l = new List<int>(cap);
            for (int i = 0; i < cap; i++) l.Add(100 + i);
            string k = Kind(() => l.AddRange(src));
            return k + " " + Shape(l);
        }

        private static List<int> InsR(int idx, IEnumerable<int> src)
        {
            List<int> l = new List<int>(new int[] { 1, 2, 3, 4, 5 });
            l.InsertRange(idx, src);
            return l;
        }

        private static string InsKind(int idx, IEnumerable<int> src)
        {
            List<int> l = new List<int>(new int[] { 1, 2, 3, 4, 5 });
            string k = Kind(() => l.InsertRange(idx, src));
            return k + " " + Shape(l);
        }

        private static HashSet<int> Set(params int[] values)
        {
            HashSet<int> s = new HashSet<int>();
            for (int i = 0; i < values.Length; i++) s.Add(values[i]);
            return s;
        }

        private static Queue<int> Q(params int[] values)
        {
            Queue<int> q = new Queue<int>();
            for (int i = 0; i < values.Length; i++) q.Enqueue(values[i]);
            return q;
        }

        public static void Main()
        {
            //========== 1. ctor: source matrix ==========
            Console.WriteLine("[ctor_arr]=" + Shape(new List<int>(new int[] { 1, 2, 3 })));
            Console.WriteLine("[ctor_lst]=" + Shape(new List<int>(new List<int>(new int[] { 4, 5 }))));
            Console.WriteLine("[ctor_hs]=" + Shape(new List<int>(Set(7, 8, 9))));
            Console.WriteLine("[ctor_q]=" + Shape(new List<int>(Q(1, 2, 3, 4, 5))));
            Console.WriteLine("[ctor_yield]=" + Shape(new List<int>(Y(3))));
            Console.WriteLine("[ctor_sel]=" + Elems(new List<int>(new int[] { 1, 2, 3 }.Select(x => x * 2))));
            Console.WriteLine("[ctor_emptyarr]=" + Shape(new List<int>(new int[0])));
            Console.WriteLine("[ctor_emptylst]=" + Shape(new List<int>(new List<int>())));
            Console.WriteLine("[ctor_emptyhs]=" + Shape(new List<int>(Set())));
            Console.WriteLine("[ctor_emptyq]=" + Shape(new List<int>(Q())));
            Console.WriteLine("[ctor_emptyyield]=" + Shape(new List<int>(Y(0))));
            Console.WriteLine("[ctor_null]=" + Kind(() => new List<int>((IEnumerable<int>)null)));

            // ctor independence (copy, not alias)
            List<int> orig = new List<int>(new int[] { 1, 2 });
            List<int> copy = new List<int>(orig);
            orig.Add(3);
            Console.WriteLine("[ctor_indep]=" + Shape(copy));
            Console.WriteLine("[ctor_nested]=" + Shape(new List<int>(new List<int>(new List<int>(new int[] { 3, 1, 2 })))));

            // ctor: mid-enumeration throw / GetEnumerator null
            Console.WriteLine("[ctor_throw]=" + Kind(() => new List<int>(new ThrowMid())));
            Console.WriteLine("[ctor_nullenum]=" + Kind(() => new List<int>(new NullEnum())));

            // ctor: call order + Dispose exactly once
            Counting cnt = new Counting();
            Console.WriteLine("[ctor_counting]=" + Shape(new List<int>(cnt)) + " disposed=" + I(cnt.Disposed));

            // ctor: get_Current is never read after MoveNext == false
            LateCurrent late = new LateCurrent();
            Console.WriteLine("[ctor_late]=" + Shape(new List<int>(late)) + " after_end=" + I(late.AfterEndReads));

            //========== 2. AddRange: source matrix ==========
            Console.WriteLine("[add_arr]=" + Shape(AddR(5, new int[] { 1, 2, 3 })));
            Console.WriteLine("[add_arr_empty]=" + Shape(AddR(5, new int[0])));
            Console.WriteLine("[add_lst]=" + Shape(AddR(5, new List<int>(new int[] { 1, 2, 3 }))));
            Console.WriteLine("[add_hs]=" + Shape(AddR(5, Set(1, 2, 3))));
            Console.WriteLine("[add_q]=" + Shape(AddR(5, Q(1, 2, 3))));
            Console.WriteLine("[add_yield]=" + Shape(AddR(5, Y(3))));
            Console.WriteLine("[add_sel]=" + Elems(AddR(5, new int[] { 1, 2, 3 }.Select(x => x * 2))));
            Console.WriteLine("[add_null]=" + AddKind(5, null));
            Console.WriteLine("[add_nullenum]=" + AddKind(5, new NullEnum()));

            // capacity growth: ICollection<int> pre-sizes exactly; a plain
            // enumerable grows through Add (0->4->...); EnsureCapacity is the
            // net48 (len==0 ? 4 : len*2, raised to the required minimum).
            List<int> bigArr = new List<int>();
            bigArr.AddRange(new int[100]);
            Console.WriteLine("[big_arr]=" + I(bigArr.Count) + "/" + I(bigArr.Capacity));
            List<int> bigYield = new List<int>();
            bigYield.AddRange(Y(100));
            Console.WriteLine("[big_yield]=" + I(bigYield.Count) + "/" + I(bigYield.Capacity));
            List<int> bigSet = new List<int>();
            bigSet.AddRange(new HashSet<int>(new int[100]));
            Console.WriteLine("[big_hs]=" + I(bigSet.Count) + "/" + I(bigSet.Capacity));
            List<int> from4 = new List<int>(new int[] { 1, 2, 3 });
            from4.AddRange(new int[10]);
            Console.WriteLine("[big_from4]=" + I(from4.Count) + "/" + I(from4.Capacity));

            //========== 3. self reference ==========
            List<int> self = new List<int>(new int[] { 1, 2, 3 });
            self.AddRange(self);
            Console.WriteLine("[self_add]=" + Shape(self));
            List<int> selfIns = new List<int>(new int[] { 1, 2, 3 });
            selfIns.InsertRange(1, selfIns);
            Console.WriteLine("[self_ins]=" + Shape(selfIns));
            List<int> selfEmpty = new List<int>();
            selfEmpty.AddRange(selfEmpty);
            Console.WriteLine("[self_empty]=" + Shape(selfEmpty));

            //========== 4. InsertRange positions ==========
            Console.WriteLine("[ins_head]=" + Shape(InsR(0, new int[] { 9, 8 })));
            Console.WriteLine("[ins_mid]=" + Shape(InsR(2, new int[] { 9, 8 })));
            Console.WriteLine("[ins_end]=" + Shape(InsR(5, new int[] { 9, 8 })));
            Console.WriteLine("[ins_empty]=" + Shape(InsR(2, new int[0])));
            Console.WriteLine("[ins_lst]=" + Shape(InsR(2, new List<int>(new int[] { 9, 8 }))));
            Console.WriteLine("[ins_yield]=" + Shape(InsR(2, Y(2))));
            Console.WriteLine("[ins_q]=" + Shape(InsR(2, Q(9, 8))));
            Console.WriteLine("[ins_sel]=" + Elems(InsR(2, new int[] { 5, 4 }.Select(x => x - 1))));
            Console.WriteLine("[ins_hs]=" + Shape(InsR(2, Set(9, 8))));

            //========== 5. InsertRange argument errors ==========
            {
                List<int> probe = new List<int>(new int[] { 1, 2 });
                string insertMsg = "";
                try { probe.Insert(9, 0); }
                catch (ArgumentOutOfRangeException e) { insertMsg = e.Message; }
                string irMsg = "";
                string irKind = "no";
                try { probe.InsertRange(9, new int[1]); }
                catch (ArgumentOutOfRangeException e) { irKind = "aoore/" + (e.ParamName ?? "<null>"); irMsg = e.Message; }
                Console.WriteLine("[ins_bad]=" + irKind + " distinct=" + (irMsg != insertMsg) + " nonempty=" + (irMsg.Length > 0));
            }
            Console.WriteLine("[ins_neg]=" + InsKind(-1, new int[] { 1 }));
            Console.WriteLine("[ins_badnull]=" + InsKind(99, null));
            Console.WriteLine("[ins_badidx_empty]=" + InsKind(6, new int[0]));

            //========== 6. exception mid-enumeration keeps the prefix ==========
            List<int> midAdd = new List<int>(new int[] { 7 });
            Console.WriteLine("[mid_add]=" + Kind(() => midAdd.AddRange(new ThrowMid())) + " " + Shape(midAdd));
            List<int> midIns = new List<int>(new int[] { 7 });
            Console.WriteLine("[mid_ins]=" + Kind(() => midIns.InsertRange(1, new ThrowMid())) + " " + Shape(midIns));
            List<int> midCtor = null;
            string midCtorKind = Kind(() => midCtor = new List<int>(new ThrowMid()));
            Console.WriteLine("[mid_ctor]=" + midCtorKind + " obj=" + (midCtor == null ? "null" : Shape(midCtor)));

            //========== 7. Dispose exactly once (also on the throwing path) =====
            Counting cnt2 = new Counting();
            List<int> disp = new List<int>();
            disp.AddRange(cnt2);
            Console.WriteLine("[add_counting]=" + Shape(disp) + " disposed=" + I(cnt2.Disposed));

            ThrowMidDispose tmd = new ThrowMidDispose();
            string tmdKind = "no";
            try { disp.AddRange(tmd); }
            catch (InvalidOperationException) { tmdKind = "ioe"; }
            Console.WriteLine("[add_throw_dispose]=" + tmdKind + " disposed=" + I(tmd.Disposed));

            LateCurrent late2 = new LateCurrent();
            List<int> l2 = new List<int>(new int[] { 5 });
            l2.AddRange(late2);
            Console.WriteLine("[add_late]=" + Shape(l2) + " after_end=" + I(late2.AfterEndReads));

            Console.WriteLine("[done]=1");
        }
    }
}
