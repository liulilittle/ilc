using System;
using System.Text;
namespace Cases { public static class SbChainCase {
    public static void Main() {
        // ================= 1. Append . AppendLine . AppendFormat mixed chain =================
        // (net48 AppendLine appends "\r\n")
        StringBuilder sb = new StringBuilder();
        sb.Append("Name: ").Append("Alice").AppendLine();
        sb.Append("Age: ").Append(30).AppendLine();
        sb.AppendFormat("Score: {0} / {1}", 95, 100).AppendLine();
        Console.WriteLine(sb.Length);                                                          // 39
        Console.WriteLine(sb.ToString() == "Name: Alice\r\nAge: 30\r\nScore: 95 / 100\r\n");   // True
        Console.WriteLine(sb.ToString() == "Name: Bob\r\nAge: 30\r\nScore: 95 / 100\r\n");     // False
        // --- Insert / Remove / Replace on the chained content ---
        sb.Insert(6, "X");
        Console.WriteLine(sb.ToString() == "Name: XAlice\r\nAge: 30\r\nScore: 95 / 100\r\n");  // True
        sb.Remove(6, 1);
        Console.WriteLine(sb.ToString() == "Name: Alice\r\nAge: 30\r\nScore: 95 / 100\r\n");   // True
        sb.Replace("Alice", "Bob");
        Console.WriteLine(sb.ToString() == "Name: Bob\r\nAge: 30\r\nScore: 95 / 100\r\n");     // True
        // --- numeric appends ---
        sb.Append("Tries: ").Append(0).Append(' ').Append(-17).Append(' ').Append(int.MaxValue);
        Console.WriteLine(sb.ToString() == "Name: Bob\r\nAge: 30\r\nScore: 95 / 100\r\nTries: 0 -17 2147483647"); // True
        sb.Append(' ').Append(int.MinValue);
        Console.WriteLine(sb.ToString() == "Name: Bob\r\nAge: 30\r\nScore: 95 / 100\r\nTries: 0 -17 2147483647 -2147483648"); // True
        // --- final raw dump (multi-line: AppendLine CRLFs) ---
        Console.WriteLine(sb.ToString());
        // ================= 2. Single-line chain: Append/AppendFormat/Insert/Remove/Replace =================
        StringBuilder rec = new StringBuilder();
        rec.Append("id=").Append(42).Append(", score=").AppendFormat("{0}.{1}", 95, 7);
        Console.WriteLine(rec.ToString());      // id=42, score=95.7
        Console.WriteLine(rec.Length);          // 17
        rec.Append(" | tag:").Append('A');
        Console.WriteLine(rec.ToString());      // id=42, score=95.7 | tag:A
        rec.Insert(0, "[");
        rec.Insert(rec.Length, "]");
        Console.WriteLine(rec.ToString());      // [id=42, score=95.7 | tag:A]
        rec.Remove(0, 1);
        rec.Remove(rec.Length - 1, 1);
        Console.WriteLine(rec.ToString());      // id=42, score=95.7 | tag:A
        rec.Replace("42", "99");
        rec.Replace("95.7", "9.9");
        Console.WriteLine(rec.ToString());      // id=99, score=9.9 | tag:A
        Console.WriteLine(rec.ToString() == "id=99, score=9.9 | tag:A");  // True
        // ================= 3. Long Append chain across growth boundaries =================
        StringBuilder wide = new StringBuilder(4);
        wide.Append("0123456789").Append("abcdefghij").Append("ABCDEFGHIJ");
        Console.WriteLine(wide.ToString());     // 0123456789abcdefghijABCDEFGHIJ
        Console.WriteLine(wide.ToString() == "0123456789abcdefghijABCDEFGHIJ"); // True
    }
}}
