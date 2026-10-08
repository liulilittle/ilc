//------------------------------------------------------------------------------
// rf-prop2.cs -- RF-N2 audit-A probe (observations B/C): indexer null-index and
// unknown-property-name semantics against the net48 oracle, byte-for-byte.
//
//   B. INDEXER + null index: GetValue(obj) / SetValue(obj, value) -- the
//      non-indexed arity forms -- on an indexer property. net48 throws
//      TargetParameterCountException ("Parameter count mismatch"); the
//      runtime previously built a 0-length arg array and the invoker arity
//      mismatch silently returned nullptr.
//   C. UNKNOWN property name: GetValue/SetValue with a receiver name that
//      does not resolve in the target type's own+base property tables.
//      net48's documented behavior is an ArgumentException for a
//      non-existent property access.
//
// The typed catches print which exception class actually surfaces, so the
// probe records the oracle's real behavior (no assumed semantics).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class PropIdx2
    {
        private int _d0, _d1, _d2, _d3;
        public int this[int i]
        {
            get
            {
                if (i == 0) return _d0;
                if (i == 1) return _d1;
                if (i == 2) return _d2;
                return _d3;
            }
            set
            {
                if (i == 0) { _d0 = value; return; }
                if (i == 1) { _d1 = value; return; }
                if (i == 2) { _d2 = value; return; }
                _d3 = value;
            }
        }
    }

    public class PropHost2
    {
        public int Num { get { return 3; } set { } }
    }

    public static class RfProp2
    {
        public static void Main()
        {
            var pi = new PropIdx2();
            var item = typeof(PropIdx2).GetProperty("Item");

            // B. indexer GetValue with a null index (the 1-arg form).
            bool tpcGet = false;
            try { item.GetValue(pi); }
            catch (TargetParameterCountException) { tpcGet = true; }
            Console.WriteLine("indexer_getvalue_nullidx_tpc=" + tpcGet);

            // B. indexer SetValue with a null index (the 2-arg form).
            bool tpcSet = false;
            try { item.SetValue(pi, 5); }
            catch (TargetParameterCountException) { tpcSet = true; }
            Console.WriteLine("indexer_setvalue_nullidx_tpc=" + tpcSet);

            // B. indexer WITH a correct index still round-trips (control).
            int got = (int)item.GetValue(pi, new object[] { 2 });
            item.SetValue(pi, 9, new object[] { 1 });
            Console.WriteLine("indexer_getvalue_idx=" + (got == 0));
            Console.WriteLine("indexer_setvalue_idx=" + (pi[1] == 9));

            // C. unknown property name on GetValue / SetValue.
            // net48: typeof(X).GetProperty("DoesNotExist") returns null, so
            // the chained .GetValue/.SetValue call dereferences null -- the
            // probe records the CONCRETE type that surfaces (net48 ground
            // truth: NullReferenceException, NOT ArgumentException, despite
            // the audit's hint -- GetProperty null-returns first).
            string nullGet = "none";
            try { typeof(PropHost2).GetProperty("DoesNotExist").GetValue(new PropHost2()); }
            catch (NullReferenceException) { nullGet = "nre"; }
            catch (ArgumentException) { nullGet = "argex"; }
            Console.WriteLine("unknown_get_exc=" + nullGet);

            string nullSet = "none";
            try { typeof(PropHost2).GetProperty("DoesNotExist").SetValue(new PropHost2(), 1); }
            catch (NullReferenceException) { nullSet = "nre"; }
            catch (ArgumentException) { nullSet = "argex"; }
            Console.WriteLine("unknown_set_exc=" + nullSet);
        }
    }
}