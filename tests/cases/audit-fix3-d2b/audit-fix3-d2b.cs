// audit-fix3-d2b -- DefaultIfEmpty(source, defaultValue) over a VALUE-STRUCT
// ARRAY element family (System.DateTime array, plus a Guid[] source) and the
// non-empty pass-through. The List<DateTime> shape is a separate LOUD case
// (loud-linq-die-listvt): a by-value value-struct LIST element has no list
// storage kind yet (defect D of the impl-loudvalstruct seat).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2b
    {
        private static string D(DateTime v)
        {
            return v.Year + "-" + v.Month + "-" + v.Day + " " + v.Hour + ":" + v.Minute + ":" + v.Second;
        }

        public static void Main()
        {
            DateTime dt = new DateTime(2021, 6, 7, 8, 9, 10);

            DateTime[] ea = new DateTime[0];
            DateTime[] ra = ea.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_arr_len=" + ra.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_arr_0=" + D(ra[0]));

            DateTime[] na = new DateTime[] { new DateTime(2000, 1, 2) };
            DateTime[] rn = na.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_arr_nonempty=" + rn.Length.ToString(CultureInfo.InvariantCulture) +
                              "/" + rn[0].Year.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_arr_nonempty_0=" + D(rn[0]));

            Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Guid[] eg = new Guid[0];
            Guid[] rg = eg.DefaultIfEmpty(g).ToArray();
            Console.WriteLine("guid_arr_len=" + rg.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("guid_arr_0=" + rg[0].ToString());
        }
    }
}
