//------------------------------------------------------------------------------
// miscloud.cs -- the statically-analyzable misc-domain LOUD items wired by the
// impl-misc batch (net48 reference oracle):
//
//   * Guid(byte[]) ctor -- the net48 from-bytes ctor (il2cpp_guid_ctor_bytes);
//     the round-trip with the "D" ToString pins the byte layout.
//   * DateTime.Subtract(TimeSpan) -- the tick subtraction (the 1-arg
//     VALUETYPE-param overload disambiguated by the decoded param type).
//   * DateTime.ToLocalTime() / ToUniversalTime() -- the instance converters
//     (the machine zone's current offset, byte-exact with net48 on the
//     verification host).
//   * GCHandle.ToIntPtr(GCHandle) / FromIntPtr(IntPtr) -- the static int32-slot
//     identity (both are static 1-arg in net48).
//
// Every line prints "label=value". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class MiscLoud
    {
        public static void Main()
        {
            GuidFromBytes();
            DateTimeMisc();
            CalendarMisc();
            GCHandleMisc();
        }

        static void CalendarMisc()
        {
            var cal = new System.Globalization.GregorianCalendar();
            DateTime dt = cal.ToDateTime(2021, 3, 14, 15, 9, 26, 535);
            Console.WriteLine("cal.todatetime=" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        }

        static void GuidFromBytes()
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = (byte)(i * 17 + 3);
            }
            Guid g = new Guid(bytes);
            Console.WriteLine("guid.ctor=" + g.ToString());

            Guid z = new Guid(new byte[16]);
            Console.WriteLine("guid.zero=" + z.ToString());

            Guid parsed = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            Console.WriteLine("guid.parse=" + parsed.ToString());
        }

        static void DateTimeMisc()
        {
            DateTime a = new DateTime(2021, 3, 14, 15, 9, 26, 535);
            TimeSpan delta = new TimeSpan(0, 2, 0, 0, 500);
            DateTime b = a.Subtract(delta);
            Console.WriteLine("dt.subts=" + b.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));

            DateTime baseUtc = new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            Console.WriteLine("dt.local=" + baseUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("dt.utc=" + baseUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            DateTime local = new DateTime(2021, 6, 1, 20, 0, 0, DateTimeKind.Local);
            Console.WriteLine("dt.local2utc=" + local.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            DateTime unspecified = new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
            Console.WriteLine("dt.unspec.local=" + unspecified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("dt.unspec.utc=" + unspecified.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        static void GCHandleMisc()
        {
            object target = new object();
            GCHandle h = GCHandle.Alloc(target);
            IntPtr p = GCHandle.ToIntPtr(h);
            GCHandle h2 = GCHandle.FromIntPtr(p);
            Console.WriteLine("gchandle.isallocated=" + (h2.IsAllocated ? "True" : "False"));
            Console.WriteLine("gchandle.target_match=" + object.ReferenceEquals(h2.Target, target));
            h.Free();
            Console.WriteLine("gchandle.after_free=" + (h.IsAllocated ? "True" : "False"));
        }
    }
}
