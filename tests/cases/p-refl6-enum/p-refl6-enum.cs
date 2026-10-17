//------------------------------------------------------------------------------
// p-refl6-enum.cs -- refl6 item A net48 oracle probe: the System.Type enum
// surface (GetEnumNames / GetEnumValues / GetEnumName / GetEnumUnderlyingType).
// net48 semantics pinned here (all probe-verified on the live oracle):
//   * GetEnumNames    -> member names in DECLARATION order (A,B,C);
//   * GetEnumValues   -> the value array; elements unbox via (int)(object)
//     GetValue(i) (the int-backed case reads the int32 slot both sides);
//   * GetEnumName     -> the member name for a matching value, null for an
//     unnamed value (printed "NULL");
//   * GetEnumUnderlyingType -> the underlying primitive's fully-qualified
//     name as a Type VALUE ("System.Int32" / "System.Int64", printed via
//     .FullName);
//   * a NON-ENUM receiver (typeof(int) / typeof(string)) throws
//     ArgumentException on all four methods (probe catches and prints the
//     exception type name -- byte-stable).
// The long-backed enum (E6L) exercises the Int64 underlying + the wide
// GetEnumValues path (boxed-element array).
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases
{
    public enum E6N { A = 1, B = 2, C = 4 }
    public enum E6L : long { A = 1, B = 2, C = 4 }

    public static class PRefl6Enum
    {
        public static void Main()
        {
            var t = typeof(E6N);

            // ---- GetEnumNames: declaration-order names, comma-joined ----
            var sb = new StringBuilder();
            bool first = true;
            foreach (var n in t.GetEnumNames())
            {
                if (!first) sb.Append(',');
                sb.Append(n);
                first = false;
            }
            Console.WriteLine("[en.names]=" + sb.ToString());

            // ---- GetEnumValues: value array, unboxed via (int)(object)GetValue(i) ----
            Array ev = t.GetEnumValues();
            Console.WriteLine("[en.v.len]=" + ev.Length);
            Console.WriteLine("[en.v0]=" + (int)(object)ev.GetValue(0));
            Console.WriteLine("[en.v1]=" + (int)(object)ev.GetValue(1));
            Console.WriteLine("[en.v2]=" + (int)(object)ev.GetValue(2));

            // ---- GetEnumName: matching value -> name; unnamed -> null ----
            Console.WriteLine("[en.name2]=" + t.GetEnumName((object)2));
            var n9 = t.GetEnumName((object)9);
            Console.WriteLine("[en.name9]=" + (n9 == null ? "NULL" : n9));
            var n0 = t.GetEnumName((object)0);
            Console.WriteLine("[en.name0]=" + (n0 == null ? "NULL" : n0));

            // ---- GetEnumUnderlyingType: underlying primitive Type VALUE ----
            Console.WriteLine("[en.under]=" + t.GetEnumUnderlyingType().FullName);

            // ---- long-backed enum ----
            var tl = typeof(E6L);
            Console.WriteLine("[el.under]=" + tl.GetEnumUnderlyingType().FullName);
            Array evl = tl.GetEnumValues();
            Console.WriteLine("[el.v1]=" + (long)(object)evl.GetValue(1));

            // ---- non-enum receivers -> ArgumentException ----
            try
            {
                var u = typeof(int).GetEnumUnderlyingType();
                Console.WriteLine("[en.int.under.throw]=none");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("[en.int.under.throw]=" + ex.GetType().Name);
            }
            try
            {
                var g = typeof(int).GetEnumNames();
                Console.WriteLine("[en.int.names.throw]=none");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("[en.int.names.throw]=" + ex.GetType().Name);
            }
            try
            {
                var s = typeof(string).GetEnumName((object)1);
                Console.WriteLine("[en.str.name.throw]=none");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("[en.str.name.throw]=" + ex.GetType().Name);
            }
        }
    }
}