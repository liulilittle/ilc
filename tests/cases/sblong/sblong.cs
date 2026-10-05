using System;
using System.Text;
namespace Cases { public static class SbLongCase {
    public static void Main() {
        // ================= 1. 10000 Appends: capacity growth + content/length =================
        StringBuilder sb = new StringBuilder();
        bool capMonotonic = true;
        int prevCap = sb.Capacity;
        for (int i = 0; i < 10000; i++) {
            sb.Append("x");
            if (sb.Capacity < prevCap) { capMonotonic = false; }
            prevCap = sb.Capacity;
        }
        Console.WriteLine(capMonotonic);                        // True (capacity never shrank)
        Console.WriteLine(sb.Length == 10000);                  // True
        Console.WriteLine(sb.ToString().Length == 10000);       // True
        Console.WriteLine(sb.ToString()[9999] == 'x');          // True (last unit intact)
        Console.WriteLine(sb.Capacity >= 10000);                // True (grown past the default)
        string bigStr = sb.ToString();
        bool allX = true;
        for (int i = 0; i < bigStr.Length; i++) { if (bigStr[i] != 'x') { allX = false; } }
        Console.WriteLine(allX);                                // True
        Console.WriteLine(bigStr[0] == 'y');                    // False (inequality machinery check)
        // ================= 2. Insert at the head: mass shifting =================
        StringBuilder ins = new StringBuilder();
        for (int i = 0; i < 500; i++) { ins.Insert(0, "AB"); }  // 500 head-inserts
        string istr = ins.ToString();
        bool abPattern = istr.Length == 1000;
        for (int i = 0; i < istr.Length && abPattern; i += 2) {
            if (istr[i] != 'A' || istr[i + 1] != 'B') { abPattern = false; }
        }
        Console.WriteLine(abPattern);                           // True (every head-insert shifted the tail)
        // Insert a large block at the head of a big buffer -> whole buffer shifts once
        StringBuilder shift = new StringBuilder(1000);
        for (int i = 0; i < 1000; i++) { shift.Append((char)('a' + (i % 3))); }
        shift.Insert(0, "HEAD");
        string st = shift.ToString();
        Console.WriteLine(st.Length == 1000);                   // False (now 1004)
        bool headOk = st.Length == 1004 && st[0] == 'H' && st[1] == 'E' && st[2] == 'A' && st[3] == 'D';
        bool tailOk = true;
        for (int i = 0; i < 1000 && tailOk; i++) {
            if (st[4 + i] != (char)('a' + (i % 3))) { tailOk = false; }
        }
        Console.WriteLine(headOk);                              // True
        Console.WriteLine(tailOk);                              // True (shifted tail preserved)
        // ================= 3. Remove large ranges =================
        StringBuilder rm = new StringBuilder();
        for (int i = 0; i < 2000; i++) { rm.Append((char)('a' + (i % 26))); }
        rm.Remove(0, 500);                                      // drop the first quarter
        Console.WriteLine(rm.Length == 1500);                   // True
        Console.WriteLine(rm.ToString()[0] == 'g');             // True (old index 500 -> 500 % 26 == 6)
        rm.Remove(500, 1000);                                   // drop a 1000-char middle range
        Console.WriteLine(rm.Length == 500);                    // True
        string rt = rm.ToString();
        bool middle = true;
        for (int i = 0; i < rt.Length && middle; i++) {
            if (rt[i] != (char)('a' + ((i + 500) % 26))) { middle = false; }
        }
        Console.WriteLine(middle);                              // True (old [500, 1000) kept verbatim)
        Console.WriteLine(rt[499] == 'l');                      // True (old index 999 -> 999 % 26 == 11)
        rm.Remove(0, 500);                                      // drop everything
        Console.WriteLine(rm.Length == 0);                      // True
        Console.WriteLine(rm.ToString() == "");                 // True
        Console.WriteLine(rm.ToString() == "abc");              // False (empty vs non-empty)
        // ================= 4. Replace: full-buffer replacement, shrink, grow =================
        StringBuilder rp = new StringBuilder();
        for (int i = 0; i < 1000; i++) { rp.Append("ab"); }
        rp.Replace("ab", "cd");                                 // every occurrence, same width
        Console.WriteLine(rp.Length == 2000);                   // True
        rp.Replace('c', 'C');                                   // char replace across the whole buffer
        string r1 = rp.ToString();
        bool allCd = true;
        for (int i = 0; i < r1.Length && allCd; i += 2) {
            if (r1[i] != 'C' || r1[i + 1] != 'd') { allCd = false; }
        }
        Console.WriteLine(allCd);                               // True
        rp.Replace("Cd", "e");                                  // shrink: every 2-char match -> 1 char
        Console.WriteLine(rp.Length == 1000);                   // True
        rp.Replace("e", "long");                                // grow: every 1-char match -> 4 chars
        Console.WriteLine(rp.Length == 4000);                   // True
        string r3 = rp.ToString();
        bool allLong = true;
        for (int i = 0; i < r3.Length && allLong; i += 4) {
            if (r3[i] != 'l' || r3[i + 1] != 'o' || r3[i + 2] != 'n' || r3[i + 3] != 'g') { allLong = false; }
        }
        Console.WriteLine(allLong);                             // True
        Console.WriteLine(r3[3999] == 'g');                     // True (last unit of the grown tail)
        // ================= 5. AppendFormat mixed with Append / Append(char) / Append(int) =================
        StringBuilder mix = new StringBuilder();
        mix.Append("start;");
        for (int i = 0; i < 5; i++) {
            mix.AppendFormat("n={0},", i).Append('s').Append(i % 2).Append(';');
        }
        mix.Append("end");
        Console.WriteLine(mix.ToString() == "start;n=0,s0;n=1,s1;n=2,s0;n=3,s1;n=4,s0;end"); // True
        Console.WriteLine(mix.ToString().Length == 44);         // True (6 + 5*7 + 3)
        // 100 AppendFormat calls: length accumulation + spot checks
        StringBuilder fmt = new StringBuilder();
        int fmtLen = 0;
        for (int i = 0; i < 100; i++) {
            fmt.AppendFormat("[{0}]", i);
            fmtLen += (i < 10 ? 3 : 4);                         // "[0]" is 3 units, "[99]" is 4
        }
        string ft = fmt.ToString();
        Console.WriteLine(ft.Length == fmtLen);                 // True
        Console.WriteLine(ft.Length == 390);                    // True (10*3 + 90*4)
        bool spot = ft[0] == '[' && ft[1] == '0' && ft[2] == ']'
                 && ft[386] == '[' && ft[387] == '9' && ft[388] == '9' && ft[389] == ']'
                 && ft[fmtLen - 1] == ']';
        Console.WriteLine(spot);                                // True ("[99]" begins at 10*3 + 89*4 = 386)
        // ================= 6. Isolation: the 10000-unit builder is untouched =================
        Console.WriteLine(sb.Length == 10000 && sb.ToString().Length == 10000); // True
    }
}}
