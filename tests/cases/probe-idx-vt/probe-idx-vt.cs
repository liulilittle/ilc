// probe-idx-vt.cs -- b5-gaps section 90 candidate: the (element, index)
// Select/Where forms over the 16-byte VALUE-STRUCT sources (decimal /
// DateTime / TimeSpan / Guid / DateTimeOffset). Section 87 wired the index
// forms over the ILookup surface (reference cells); the value-struct source
// families are the registered remaining seat. Each family sits in its OWN
// method so one transpile LOUD does not hide the others.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeIdxVt
    {
        static string JoinD(decimal[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x.ToString() + ";";
            return s;
        }

        static string JoinDt(DateTime[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x.Year + "-" + x.Month + ";";
            return s;
        }

        static string JoinTs(TimeSpan[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x.Ticks + ";";
            return s;
        }

        static string JoinG(Guid[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x.ToString().Length + ";";
            return s;
        }

        static string JoinDto(DateTimeOffset[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x.Offset.Minutes + ";";
            return s;
        }

        static void RunDec()
        {
            var dec = new decimal[] { 1.5m, 2.5m, 3.5m, 4.5m };
            Console.WriteLine("d1:" + JoinD(dec.Select((x, i) => x + i).ToArray()));
            // i % 3 == 0 over 4 elements: {0,3}. Inverting the predicate gives
            // {1,2}; an off-by-one index gives {2} -- three distinct results, so
            // both the inversion and the off-by-one mutations are observable.
            Console.WriteLine("d2:" + JoinD(dec.Where((x, i) => i % 3 == 0).ToArray()));
            Console.WriteLine("d3:" + dec.Select((x, i) => i).ToArray().Length);
        }

        static void RunDt()
        {
            var dt = new DateTime[] { new DateTime(2020, 1, 2), new DateTime(2021, 3, 4) };
            Console.WriteLine("d4:" + JoinDt(dt.Select((x, i) => x.AddDays(i)).ToArray()));
            Console.WriteLine("d5:" + JoinDt(dt.Where((x, i) => i == 1).ToArray()));
        }

        static void RunTs()
        {
            var ts = new TimeSpan[] { new TimeSpan(1, 0, 0), new TimeSpan(2, 0, 0) };
            Console.WriteLine("d6:" + JoinTs(ts.Select((x, i) => x.Add(new TimeSpan(i, 0, 0))).ToArray()));
            Console.WriteLine("d7:" + JoinTs(ts.Where((x, i) => i == 1).ToArray()));
        }

        static void RunG()
        {
            var g = new Guid[] { Guid.Empty, Guid.NewGuid() };
            Console.WriteLine("d8:" + JoinG(g.Select((x, i) => x).ToArray()));
            Console.WriteLine("d9:" + JoinG(g.Where((x, i) => i == 0).ToArray()));
        }

        static void RunDto()
        {
            var dto = new DateTimeOffset[] { new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)) };
            Console.WriteLine("d10:" + JoinDto(dto.Select((x, i) => x).ToArray()));
            Console.WriteLine("d11:" + JoinDto(dto.Where((x, i) => i == 0).ToArray()));
        }

        public static void Main()
        {
            RunDec();
            RunDt();
            RunTs();
            RunG();
            RunDto();
            Console.WriteLine("done");
        }
    }
}