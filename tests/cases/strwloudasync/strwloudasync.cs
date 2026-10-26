//------------------------------------------------------------------------------
// strwloudasync.cs -- the net48 TextReader/TextWriter ASYNC surface for
// StringReader/StringWriter (ReadToEndAsync, ReadLineAsync, ReadAsync,
// ReadBlockAsync, WriteAsync/WriteLineAsync value+range forms, FlushAsync).
//
// The runtime models these as synchronous completion: each returned task is
// already completed. The case exercises both the returned task (IsCompleted)
// and the awaited result.
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

class Program
{
    static void P(string id, object value)
    {
        Console.WriteLine("[" + id + "]" + (value == null ? "<null>" : value.ToString()));
    }

    static void ReaderAsync()
    {
        StringReader r = new StringReader("a\r\nb\nc");
        var te = r.ReadToEndAsync();
        P("readtoendasync.completed", te.IsCompleted);
        P("readtoendasync", te.Result);
        r.Dispose();

        StringReader r2 = new StringReader("line1\nline2");
        var tl = r2.ReadLineAsync();
        P("readlineasync.completed", tl.IsCompleted);
        P("readlineasync", tl.Result);
        r2.Dispose();

        StringReader r3 = new StringReader("abcdef");
        char[] buf = new char[3];
        var tr = r3.ReadAsync(buf, 0, 3);
        P("readasync.completed", tr.IsCompleted);
        P("readasync.count", tr.Result);
        P("readasync.data", new string(buf));
        r3.Dispose();

        StringReader r4 = new StringReader("abcdef");
        char[] buf2 = new char[4];
        var tb = r4.ReadBlockAsync(buf2, 0, 4);
        P("readblockasync.completed", tb.IsCompleted);
        P("readblockasync.count", tb.Result);
        P("readblockasync.data", new string(buf2));
        r4.Dispose();
    }

    static void WriterAsync()
    {
        StringWriter w = new StringWriter();
        var t1 = w.WriteAsync("x");
        P("writeasync.s.completed", t1.IsCompleted);
        var t2 = w.WriteAsync('y');
        P("writeasync.c.completed", t2.IsCompleted);
        var t3 = w.WriteAsync(new char[] { 'z', 'w' });
        P("writeasync.cs.completed", t3.IsCompleted);
        P("writeasync.values", w.ToString());
        w.Dispose();

        StringWriter w2 = new StringWriter();
        var t4 = w2.WriteAsync(new char[] { 'a', 'b', 'c', 'd' }, 1, 2);
        P("writeasync.range.completed", t4.IsCompleted);
        P("writeasync.range", w2.ToString());
        w2.Dispose();

        StringWriter w3 = new StringWriter();
        var l1 = w3.WriteLineAsync();
        P("writelineasync.default.completed", l1.IsCompleted);
        var l2 = w3.WriteLineAsync("s");
        P("writelineasync.s.completed", l2.IsCompleted);
        var l3 = w3.WriteLineAsync('c');
        P("writelineasync.c.completed", l3.IsCompleted);
        P("writelineasync.values", w3.ToString().Replace("\r\n", "<crlf>"));
        w3.Dispose();

        StringWriter w4 = new StringWriter();
        var l4 = w4.WriteLineAsync(new char[] { 'a', 'b', 'c', 'd' }, 1, 2);
        P("writelineasync.range.completed", l4.IsCompleted);
        P("writelineasync.range", w4.ToString().Replace("\r\n", "<crlf>"));
        w4.Dispose();

        StringWriter w5 = new StringWriter();
        w5.Write("q");
        var f = w5.FlushAsync();
        P("flushasync.completed", f.IsCompleted);
        P("flushasync", w5.ToString());
        w5.Dispose();
    }

    static void Main()
    {
        ReaderAsync();
        WriterAsync();
    }
}
