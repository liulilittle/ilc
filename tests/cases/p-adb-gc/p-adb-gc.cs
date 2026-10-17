using System;
using System.Text;

//------------------------------------------------------------------------------
// p-adb-gc.cs -- adm1 defect-fix stress probe (D1 GC-safety + NB1 SetData
// capacity), net48 oracle == transpiled runtime byte-exact.
//
//   NB1: set 80 process-level data keys -- the adm1 v1 table registered TWO
//        static roots per key and overflowed the kMaxStaticRoots=128 table at
//        63 keys (FailLoud abort); v2 roots the WHOLE table as one root range
//        with a 256-key loud-overflow bound. The probe must read all 80 keys
//        back unchanged (no silent drop / no abort).
//   D1:  the CreateInstance path held a bare stack local across an intra-
//        icall allocation (the StringBuilder default-ctor buffer + the
//        ObjectHandle wrapper); the compacting collector (256KB collection
//        threshold) can MOVE the just-created object mid-window. The stress
//        loop keeps the heap at pressure (bumping towards the 256KB threshold
//        from within) and verifies every created builder STILL appends + reads back intact
//        (writes/reads would silently diverge on the stale-address path).
//
// Every record is a boolean assertion (True/False) -- byte-exact between the
// net48 oracle and the (fixed) transpiled runtime.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PAdbGc
    {
        private static bool Appends(StringBuilder b, string s)
        {
            if (b == null)
            {
                return false;
            }
            StringBuilder appended = b.Append(s); // tracked StringBuilder result
            return appended.ToString() == s;
        }

        public static void Main()
        {
            AppDomain ad = AppDomain.CurrentDomain;

            // --- NB1: 80 keys, all readable back (no drop, no root overflow) ---
            for (int i = 0; i < 80; i++)
            {
                ad.SetData("padb.k" + i, "__padb.v" + i);
            }
            bool allGet = true;
            for (int i = 0; i < 80; i++)
            {
                if (!object.Equals(ad.GetData("padb.k" + i), "__padb.v" + i)) allGet = false;
            }
            Console.WriteLine("nb1.keys.roundtrip=" + allGet);
            // overwrite a middle key + re-read (in-place update still works)
            ad.SetData("padb.k40", "__padb.v40b");
            Console.WriteLine("nb1.keys.overwrite=" + object.Equals(ad.GetData("padb.k40"), "__padb.v40b"));

            // --- D1: CreateInstance stress across compacting collections ---
            bool ciOk = true, ciuOk = true;
            for (int i = 0; i < 150; i++)
            {
                // ~256KB of churn per iteration keeps the heap near the
                // collection threshold so a collection can land INSIDE the
                // CreateInstance window (moving the just-created object).
                string[] churn = new string[4096];
                for (int j = 0; j < churn.Length; j++)
                {
                    churn[j] = "padb-churn-" + i + "-" + j;
                }
                if ((i & 3) == 0)
                {
                    GC.Collect();
                }

                var h = (System.Runtime.Remoting.ObjectHandle)ad.CreateInstance("mscorlib", "System.Text.StringBuilder");
                StringBuilder sbh = h.Unwrap() as StringBuilder;
                if (!Appends(sbh, "w1")) ciOk = false;

                object sobj = ad.CreateInstanceAndUnwrap("mscorlib", "System.Text.StringBuilder");
                StringBuilder sbc = sobj as StringBuilder;
                if (!Appends(sbc, "w2")) ciuOk = false;
            }
            Console.WriteLine("d1.ci.ok=" + ciOk);
            Console.WriteLine("d1.ciu.ok=" + ciuOk);
        }
    }
}