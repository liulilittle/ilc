//------------------------------------------------------------------------------
// base64-chararray -- Convert.ToBase64CharArray, both net48 overloads
// (convert.cs:2193 and 2203), which were LOUD before this batch but are
// compile-time analyzable:
//
//   1) ToBase64CharArray(byte[], int, int, char[], int) -- the returned int is
//      the number of chars written at offsetOut; the bytes are encoded exactly
//      like ToBase64String (RFC 4648 with '=' padding).
//   2) ToBase64CharArray(..., Base64FormattingOptions) -- InsertLineBreaks
//      inserts CRLF every 76 output chars; the result must be identical to
//      ToBase64String(bytes, options).
//   3) The net48 validation order / exception types: null in/out arrays,
//      negative length/offsetIn/offsetOut, an invalid options value, an
//      offsetIn past the end, and an offsetOut that is too small. Empty input
//      returns 0 (after the inArray checks) even with a zero-length outArray.
//
// All printed values are deterministic (int / string / exception type name).
//------------------------------------------------------------------------------
using System;
using System.Text;

class Program
{
    static void Show(string id, Func<string> f)
    {
        try
        {
            Console.WriteLine("[" + id + "] " + f());
        }
        catch (Exception e)
        {
            Console.WriteLine("[" + id + "] EX " + e.GetType().Name);
        }
    }

    static void Main()
    {
        byte[] man = new byte[] { (byte)'M', (byte)'a', (byte)'n' };
        char[] buf = new char[16];
        int n = Convert.ToBase64CharArray(man, 0, man.Length, buf, 0);
        Console.WriteLine("n=" + n);
        Console.WriteLine("val=" + new string(buf, 0, n));

        // offsetIn / offsetOut
        byte[] hello = Encoding.UTF8.GetBytes("Hello");
        char[] b2 = new char[16];
        int n2 = Convert.ToBase64CharArray(hello, 1, 3, b2, 2);
        Console.WriteLine("n2=" + n2);
        Console.WriteLine("val2=" + new string(b2, 2, n2));

        // empty input: returns 0 before the outArray capacity check
        Console.WriteLine("empty=" + Convert.ToBase64CharArray(new byte[0], 0, 0, new char[0], 0));

        // options overload: InsertLineBreaks == ToBase64String(options)
        byte[] longBytes = new byte[60];
        for (int i = 0; i < longBytes.Length; i++) longBytes[i] = (byte)('A' + (i % 26));
        char[] b4 = new char[256];
        int n4 = Convert.ToBase64CharArray(longBytes, 0, longBytes.Length, b4, 0,
            Base64FormattingOptions.InsertLineBreaks);
        Console.WriteLine("n4=" + n4);
        Console.WriteLine("opts_match=" + (new string(b4, 0, n4)
            == Convert.ToBase64String(longBytes, Base64FormattingOptions.InsertLineBreaks)));
        Console.WriteLine("breaks=" + (new string(b4, 0, n4).IndexOf('\n') >= 0));

        // options None == ToBase64String(byte[])
        int n5 = Convert.ToBase64CharArray(longBytes, 0, longBytes.Length, b4, 0,
            Base64FormattingOptions.None);
        Console.WriteLine("none_match=" + (new string(b4, 0, n5) == Convert.ToBase64String(longBytes)));

        // a range with options (offsetIn > 0)
        int n6 = Convert.ToBase64CharArray(longBytes, 7, 23, b4, 5,
            Base64FormattingOptions.None);
        Console.WriteLine("n6=" + n6);
        Console.WriteLine("range_match=" + (new string(b4, 5, n6)
            == Convert.ToBase64String(longBytes, 7, 23)));

        // ---- validation paths (net48 types) ----
        Show("null_in", delegate { Convert.ToBase64CharArray(null, 0, 0, b4, 0); return "NO"; });
        Show("null_out", delegate { Convert.ToBase64CharArray(man, 0, 3, null, 0); return "NO"; });
        Show("neg_len", delegate { Convert.ToBase64CharArray(man, 0, -1, b4, 0); return "NO"; });
        Show("neg_offin", delegate { Convert.ToBase64CharArray(man, -1, 3, b4, 0); return "NO"; });
        Show("neg_offout", delegate { Convert.ToBase64CharArray(man, 0, 3, b4, -1); return "NO"; });
        Show("bad_opt", delegate { Convert.ToBase64CharArray(man, 0, 3, b4, 0,
            (Base64FormattingOptions)2); return "NO"; });
        Show("offin_over", delegate { Convert.ToBase64CharArray(man, 1, 3, b4, 0); return "NO"; });
        Show("offout_over", delegate { Convert.ToBase64CharArray(man, 0, 3, new char[2], 0); return "NO"; });
        Show("offout_exact", delegate {
            char[] tiny = new char[4];
            int w = Convert.ToBase64CharArray(man, 0, 3, tiny, 0);
            return w + ":" + new string(tiny, 0, w);
        });
    }
}
