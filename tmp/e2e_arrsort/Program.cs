// E2E arrsort: Array.Sort<int>/Sort<string>(ordinal)/BinarySearch hit+miss(~complement)/
// Reverse / multi-op pipelines on 100-element deterministic arrays, checksum-verified.
// Prints one "PASS name detail" line per check, then ALL-PASS (or ALL-FAIL failures=N).
using System;

namespace E2eArrSort
{
    public static class Program
    {
        const int N = 100;

        static int _s = 20260822;
        static int Next() { unchecked { _s = _s * 1664525 + 1013904223; return _s; } }

        static int Rnd(int mod) { return (Next() & 0x7fffffff) % mod; }

        static int _fails = 0;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) Console.WriteLine("PASS " + name + " " + detail);
            else { _fails = _fails + 1; Console.WriteLine("FAIL " + name + " " + detail); }
        }

        // order-independent multiset checksum (commutative XOR/additive combine)
        static long CkInt(int[] a)
        {
            long sum = 0;
            long mix = 0;
            for (int i = 0; i < a.Length; ++i)
            {
                sum += a[i];
                mix ^= a[i] * 0x5bd1e995L + 0x27d4eb2fL;
            }
            return sum ^ (mix * 1000003L);
        }

        static long StrHash(string s)
        {
            long h = 1469598103934665603L;
            for (int i = 0; i < s.Length; ++i)
            {
                h = (h ^ s[i]) * 1099511628211L;
            }
            return h;
        }

        static long CkStr(string[] a)
        {
            long lenSum = 0;
            long mix = 0;
            for (int i = 0; i < a.Length; ++i)
            {
                lenSum += a[i].Length;
                mix ^= StrHash(a[i]) * 31L + 1L;
            }
            return lenSum ^ (mix * 1000003L);
        }

        static string Root(int r)
        {
            if (r == 0) return "apple";
            if (r == 1) return "banana";
            if (r == 2) return "cherry";
            if (r == 3) return "date";
            if (r == 4) return "elder";
            if (r == 5) return "fig";
            if (r == 6) return "grape";
            if (r == 7) return "haw";
            if (r == 8) return "ivy";
            if (r == 9) return "jujube";
            if (r == 10) return "kiwi";
            return "lemon";
        }

        static string BuildStr()
        {
            string s = Root(Rnd(12)) + Rnd(100).ToString("D2");
            return s;
        }

        static void GenStrings(string[] a)
        {
            for (int i = 0; i < N; ++i) a[i] = BuildStr();
            a[13] = a[0];                       // forced duplicates
            a[26] = a[13];
            a[39] = a[26];
            a[52] = a[39];
            a[65] = a[52];
            a[78] = a[65];
            a[91] = a[78];
        }

        static bool AscInt(int[] a)
        {
            for (int i = 1; i < a.Length; ++i) if (a[i - 1] > a[i]) return false;
            return true;
        }

        static bool DescInt(int[] a)
        {
            for (int i = 1; i < a.Length; ++i) if (a[i - 1] < a[i]) return false;
            return true;
        }

        static bool AscOrd(string[] a)
        {
            for (int i = 1; i < a.Length; ++i) if (string.CompareOrdinal(a[i - 1], a[i]) > 0) return false;
            return true;
        }

        static bool DescOrd(string[] a)
        {
            for (int i = 1; i < a.Length; ++i) if (string.CompareOrdinal(a[i - 1], a[i]) < 0) return false;
            return true;
        }

        public static void Main()
        {
            Console.WriteLine("e2e_arrsort N=" + N);

            // ================= 1. Sort<int> ascending =================
            int[] ia = new int[N];
            for (int i = 0; i < N; ++i) ia[i] = Rnd(2000) - 500;
            long c0 = CkInt(ia);
            int mn = ia[0], mx = ia[0];
            for (int i = 1; i < N; ++i)
            {
                if (ia[i] < mn) mn = ia[i];
                if (ia[i] > mx) mx = ia[i];
            }
            Array.Sort(ia);
            Check("sort_int_asc",
                  AscInt(ia) && ia[0] == mn && ia[N - 1] == mx && ia.Length == N && CkInt(ia) == c0,
                  "min=" + mn + " max=" + mx + " ck=" + CkInt(ia));

            // ================= 2. BinarySearch<int> hits =================
            bool hitAll = true;
            for (int i = 0; i < N; ++i)
            {
                int idx = Array.BinarySearch(ia, ia[i]);
                if (idx < 0 || ia[idx] != ia[i]) { hitAll = false; break; }
            }
            Check("bs_int_hit", hitAll, "probes=" + N);

            // ================= 3. BinarySearch<int> misses (~complement) =================
            int rLow = Array.BinarySearch(ia, mn - 1);          // below all -> ~0
            int rHigh = Array.BinarySearch(ia, mx + 1);         // above all -> ~N
            int gapIdx = -1;
            for (int i = 0; i + 1 < N; ++i)
                if (ia[i + 1] > ia[i] + 1) { gapIdx = i; break; }
            int rGap = gapIdx >= 0 ? Array.BinarySearch(ia, ia[gapIdx] + 1) : -1;
            int rEmpty = Array.BinarySearch(new int[0], 7);     // empty -> -1 (~0)
            Check("bs_int_miss",
                  rLow < 0 && ~rLow == 0 && rHigh < 0 && ~rHigh == N &&
                  gapIdx >= 0 && rGap < 0 && ~rGap == gapIdx + 1 &&
                  rEmpty == -1,
                  "low=~" + (~rLow) + " high=~" + (~rHigh) + " gap=~" + (~rGap) + " empty=" + rEmpty);

            // ================= 4. Reverse<int> =================
            int[] snapI = new int[N];
            for (int i = 0; i < N; ++i) snapI[i] = ia[i];
            Array.Reverse(ia);
            bool mirrorI = true;
            for (int i = 0; i < N; ++i) if (ia[i] != snapI[N - 1 - i]) { mirrorI = false; break; }
            Array.Reverse(ia);
            bool restoreI = true;
            for (int i = 0; i < N; ++i) if (ia[i] != snapI[i]) { restoreI = false; break; }
            Check("reverse_int",
                  mirrorI && restoreI && AscInt(ia) && CkInt(ia) == c0,
                  "ck=" + CkInt(ia));

            // ================= 5. Sort<string> ordinal =================
            string[] sa = new string[N];
            GenStrings(sa);
            long cs0 = CkStr(sa);
            string smin = sa[0], smax = sa[0];
            for (int i = 1; i < N; ++i)
            {
                if (string.CompareOrdinal(sa[i], smin) < 0) smin = sa[i];
                if (string.CompareOrdinal(sa[i], smax) > 0) smax = sa[i];
            }
            Array.Sort(sa);
            Check("sort_str_ordinal",
                  AscOrd(sa) && string.CompareOrdinal(sa[0], smin) == 0 &&
                  string.CompareOrdinal(sa[N - 1], smax) == 0 && CkStr(sa) == cs0,
                  "first=" + sa[0] + " last=" + sa[N - 1] + " ck=" + CkStr(sa));

            // ================= 6. BinarySearch<string> hits =================
            bool hitS = true;
            for (int i = 0; i < N; ++i)
            {
                int idx = Array.BinarySearch(sa, sa[i]);
                if (idx < 0 || string.CompareOrdinal(sa[idx], sa[i]) != 0) { hitS = false; break; }
            }
            Check("bs_str_hit", hitS, "probes=" + N);

            // ================= 7. BinarySearch<string> misses (~complement) =================
            string lowProbe = smin.Substring(0, smin.Length - 1);   // proper prefix of min: below all
            string highProbe = smax + "~";                          // '~' > 'z': above all
            string midProbe = sa[N / 2] + "~";                      // contains '~': never generated
            int pMid = 0;
            for (int i = 0; i < N; ++i) if (string.CompareOrdinal(sa[i], midProbe) < 0) pMid++;
            int rsLow = Array.BinarySearch(sa, lowProbe);
            int rsHigh = Array.BinarySearch(sa, highProbe);
            int rsMid = Array.BinarySearch(sa, midProbe);
            int rsEmpty = Array.BinarySearch(new string[0], "x");
            Check("bs_str_miss",
                  rsLow < 0 && ~rsLow == 0 && rsHigh < 0 && ~rsHigh == N &&
                  rsMid < 0 && ~rsMid == pMid && rsEmpty == -1,
                  "low=~" + (~rsLow) + " mid=~" + (~rsMid) + "/" + pMid +
                  " high=~" + (~rsHigh) + " empty=" + rsEmpty);

            // ================= 8. Reverse<string> =================
            string[] snapS = new string[N];
            for (int i = 0; i < N; ++i) snapS[i] = sa[i];
            Array.Reverse(sa);
            bool mirrorS = true;
            for (int i = 0; i < N; ++i)
                if (string.CompareOrdinal(sa[i], snapS[N - 1 - i]) != 0) { mirrorS = false; break; }
            Array.Reverse(sa);
            bool restoreS = true;
            for (int i = 0; i < N; ++i)
                if (string.CompareOrdinal(sa[i], snapS[i]) != 0) { restoreS = false; break; }
            Check("reverse_str",
                  mirrorS && restoreS && AscOrd(sa) && CkStr(sa) == cs0,
                  "ck=" + CkStr(sa));

            // ================= 9. multi-op pipeline: int =================
            int[] b = new int[N];
            for (int i = 0; i < N; ++i) b[i] = Rnd(5000) - 2500;
            long cb = CkInt(b);
            int bmn = b[0];
            for (int i = 1; i < N; ++i) if (b[i] < bmn) bmn = b[i];
            Array.Sort(b);
            bool ascB = AscInt(b);
            int[] sortedB = new int[N];
            for (int i = 0; i < N; ++i) sortedB[i] = b[i];
            int bh = Array.BinarySearch(b, b[7]);
            int bm = Array.BinarySearch(b, bmn - 1);
            Array.Reverse(b);
            bool descB = DescInt(b);
            bool mirrorB = true;
            for (int i = 0; i < N; ++i) if (b[i] != sortedB[N - 1 - i]) { mirrorB = false; break; }
            Array.Reverse(b);
            bool restoreB = true;
            for (int i = 0; i < N; ++i) if (b[i] != sortedB[i]) { restoreB = false; break; }
            Check("multi_int_pipeline",
                  ascB && bh >= 0 && b[bh] == b[7] && bm < 0 && ~bm == 0 &&
                  descB && mirrorB && restoreB && AscInt(b) && CkInt(b) == cb,
                  "ck=" + cb);

            // ================= 10. multi-op pipeline: string =================
            string[] t = new string[N];
            GenStrings(t);
            long ct = CkStr(t);
            string tmin = t[0];
            for (int i = 1; i < N; ++i) if (string.CompareOrdinal(t[i], tmin) < 0) tmin = t[i];
            Array.Sort(t);
            bool ascT = AscOrd(t);
            string[] sortedT = new string[N];
            for (int i = 0; i < N; ++i) sortedT[i] = t[i];
            int th = Array.BinarySearch(t, t[42]);
            int tm = Array.BinarySearch(t, tmin.Substring(0, tmin.Length - 1));
            Array.Reverse(t);
            bool descT = DescOrd(t);
            bool mirrorT = true;
            for (int i = 0; i < N; ++i)
                if (string.CompareOrdinal(t[i], sortedT[N - 1 - i]) != 0) { mirrorT = false; break; }
            Array.Reverse(t);
            bool restoreT = true;
            for (int i = 0; i < N; ++i)
                if (string.CompareOrdinal(t[i], sortedT[i]) != 0) { restoreT = false; break; }
            Check("multi_str_pipeline",
                  ascT && th >= 0 && string.CompareOrdinal(t[th], t[42]) == 0 &&
                  tm < 0 && ~tm == 0 && descT && mirrorT && restoreT && AscOrd(t) && CkStr(t) == ct,
                  "ck=" + ct);

            if (_fails == 0) Console.WriteLine("ALL-PASS");
            else Console.WriteLine("ALL-FAIL failures=" + _fails);
        }
    }
}
