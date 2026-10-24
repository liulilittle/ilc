//------------------------------------------------------------------------------
// rf-gettype2.cs -- Type.GetType overloads e2e probe (net48 oracle,
// byte-for-byte).
//
// RF-REFL gap closure: Type.GetType(string, bool throwOnError) and
// Type.GetType(string, bool throwOnError, bool ignoreCase). The registry
// resolution is the same as the 1-arg overload; throwOnError controls the
// TypeLoadException-on-miss contract and ignoreCase folds ASCII case for the
// registry key comparison.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class RfGetType2
    {
        private static string Hit(Type t) { return t == null ? "null" : "hit"; }

        public static void Main()
        {
            Console.WriteLine("t2_hit_false=" + Hit(Type.GetType("System.Int32", false)));
            Console.WriteLine("t2_hit_true=" + Hit(Type.GetType("System.Int32", true)));
            Console.WriteLine("t2_miss_false=" + Hit(Type.GetType("No.Such.Type", false)));
            try { Console.WriteLine("t2_miss_true=" + Hit(Type.GetType("No.Such.Type", true))); }
            catch (Exception e) { Console.WriteLine("t2_miss_true=EX:" + e.GetType().Name); }
            Console.WriteLine("t3_ic_true=" + Hit(Type.GetType("system.int32", true, true)));
            try { Console.WriteLine("t3_ic_false=" + Hit(Type.GetType("system.int32", true, false))); }
            catch (Exception e) { Console.WriteLine("t3_ic_false=EX:" + e.GetType().Name); }
            try { Console.WriteLine("t3_miss_true=" + Hit(Type.GetType("No.Such.Type", true, true))); }
            catch (Exception e) { Console.WriteLine("t3_miss_true=EX:" + e.GetType().Name); }
            try { Console.WriteLine("t2_null=" + Hit(Type.GetType(null, false))); }
            catch (Exception e) { Console.WriteLine("t2_null=EX:" + e.GetType().Name); }
        }
    }
}
