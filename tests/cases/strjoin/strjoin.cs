using System;
using System.Text;
namespace Cases { public static class StrJoinCase {
    public static void Main() {
        // ================= 1. Join(string, string[]): multi-element + separators =================
        string[] s3 = new string[] { "a", "b", "c" };
        Console.WriteLine(string.Join(",", s3) == "a,b,c");          // True (single-unit sep)
        Console.WriteLine(string.Join("|", s3) == "a|b|c");          // True (another single-unit sep)
        Console.WriteLine(string.Join("--", s3) == "a--b--c");       // True (multi-unit sep)
        Console.WriteLine(string.Join("", s3) == "abc");             // True (empty separator)
        // ================= 2. Join single element: identity =================
        Console.WriteLine(string.Join("-", new string[] { "only" }) == "only"); // True
        Console.WriteLine(string.Join(",", new string[] { "x" }) == "x"); // True (1-elem via literal)
        // ================= 3. Join: empty array / null separator / null elements =================
        Console.WriteLine(string.Join(",", new string[0]) == "");    // True (empty array -> empty)
        Console.WriteLine(string.Join(null, new string[] { "a", "b" }) == "ab"); // True (null sep = empty)
        Console.WriteLine(string.Join(",", new string[] { "a", null, "c" }) == "a,,c"); // True (null elem = empty)
        Console.WriteLine(string.Join(null, new string[] { null, "x", null }) == "x"); // True (both empty)
        // ================= 4. Join(object[]): ToString per element =================
        Console.WriteLine(string.Join(",", new object[] { 1, "x", null, true }) == "1,x,,True"); // True (mixed)
        Console.WriteLine(string.Join(",", new object[] { 1, 2, 3 }) == "1,2,3"); // True (all boxed ints)
        Console.WriteLine(string.Join(",", new object[] { 42 }) == "42"); // True (single boxed element)
        Console.WriteLine(string.Join(",", new object[] { "v", 1.5, "w" }) == "v,1.5,w"); // True (boxed double)
        // ================= 5. Concat: fixed arities + array form =================
        string p = "a", q = "b";
        Console.WriteLine(string.Concat(p, q) == "ab");              // True (2-arg)
        Console.WriteLine(string.Concat(p, q, "c") == "abc");        // True (3-arg)
        Console.WriteLine(string.Concat(p, q, "c", "d") == "abcd");  // True (4-arg)
        Console.WriteLine(string.Concat(new string[] { "a", "b", "c" }) == "abc"); // True (array form)
        // ================= 6. Concat numeric conveniences (string + number) =================
        Console.WriteLine(string.Concat("n=", 42) == "n=42");        // True (int)
        Console.WriteLine(string.Concat("d=", 1.5) == "d=1.5");      // True (double)
        Console.WriteLine(string.Concat("b=", true) == "b=True");    // True (bool)
        // ================= 7. + operator chains =================
        Console.WriteLine(p + q == "ab");                            // True (2-link)
        Console.WriteLine(p + q + "c" == "abc");                     // True (3-link)
        Console.WriteLine("x" + 1 + "y" == "x1y");                   // True (string + int + string)
        Console.WriteLine("v" + 1.5 + "w" == "v1.5w");               // True (string + double + string)
        string acc = "z";
        acc += 5;
        acc += "!";
        Console.WriteLine(acc == "z5!");                             // True (+= chain)
        // ================= 8. Large array Join (10000 elements) =================
        string[] big = new string[10000];
        for (int i = 0; i < 10000; i++) { big[i] = "e" + i; }
        string joined = string.Join(",", big);
        StringBuilder expect = new StringBuilder();
        for (int i = 0; i < 10000; i++) {
            if (i > 0) { expect.Append(','); }
            expect.Append('e');
            expect.Append(i);
        }
        Console.WriteLine(joined == expect.ToString());              // True (full 10000-element content)
        Console.WriteLine(joined.Length == 58889);                   // True (2*10+3*90+4*900+5*9000+9999)
        Console.WriteLine(string.Join("", big).Length == 48890);     // True (empty-sep join: 20+270+3600+45000)
        Console.WriteLine(joined[0] == 'e' && joined[1] == '0');     // True (head chars)
    }
}}
