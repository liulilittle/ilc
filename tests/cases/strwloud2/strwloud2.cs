//------------------------------------------------------------------------------
// strwloud2.cs -- residual System.IO.StringReader/StringWriter overload sweep.
//
// The TextReader/TextWriter inherited overload surface (Write/WriteLine
// value and format forms, ReadBlock, the provider/StringBuilder ctors,
// GetStringBuilder/Encoding/NewLine). Each family is a separate method so a
// transpile gap in one family never hides the others.
//------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Text;

class Program
{
    static void P(string id, object value)
    {
        Console.WriteLine("[" + id + "]" + (value == null ? "<null>" : value.ToString()));
    }

    static void ReaderBasics()
    {
        StringReader r = new StringReader("a\r\nb\nc");
        P("read", r.Read());
        P("peek", r.Peek());
        char[] buf = new char[3];
        P("read.chars", r.Read(buf, 0, 3));
        P("read.chars.data", new string(buf));
        P("readline", r.ReadLine());
        P("readtoend", r.ReadToEnd());
        r.Dispose();
    }

    static void ReaderBlock()
    {
        StringReader r = new StringReader("abcdef");
        char[] buf = new char[4];
        P("readblock", r.ReadBlock(buf, 0, 4));
        P("readblock.data", new string(buf));
        r.Dispose();
    }

    static void WriterWriteValues()
    {
        StringWriter w = new StringWriter();
        bool bo = true;
        byte by = 200;
        sbyte sby = -5;
        short sh = -300;
        ushort ush = 60000;
        uint ui = 4000000000u;
        ulong ul = 18000000000000000000ul;
        long l = -9000000000000000000L;
        float fl = 1.5f;
        double db = 2.25;
        decimal dec = 1.2345m;
        char ch = 'Z';
        char[] cs = { 'a', 'b', 'c' };
        object ob = "obj";
        string s = "str";
        w.Write(bo); w.Write('|');
        w.Write(by); w.Write('|');
        w.Write(sby); w.Write('|');
        w.Write(sh); w.Write('|');
        w.Write(ush); w.Write('|');
        w.Write(ui); w.Write('|');
        w.Write(ul); w.Write('|');
        w.Write(l); w.Write('|');
        w.Write(fl); w.Write('|');
        w.Write(db); w.Write('|');
        w.Write(dec); w.Write('|');
        w.Write(ch); w.Write('|');
        w.Write(cs); w.Write('|');
        w.Write(ob); w.Write('|');
        w.Write(s);
        P("write.values", w.ToString());
    }

    static void WriterWriteRanges()
    {
        StringWriter w = new StringWriter();
        char[] cs = { 'a', 'b', 'c', 'd' };
        w.Write(cs, 1, 2);
        w.Write('|');
        w.Write("abcdef", 1, 3);
        P("write.ranges", w.ToString());
    }

    static void WriterWriteFormats()
    {
        StringWriter w = new StringWriter();
        w.Write("{0}", "a");
        w.Write('|');
        w.Write("{0}-{1}", "a", "b");
        P("write.formats", w.ToString());
    }

    static void WriterWriteLineValues()
    {
        StringWriter w = new StringWriter();
        bool bo = true;
        int i = 42;
        double db = 2.25;
        char ch = 'Z';
        char[] cs = { 'a', 'b' };
        object ob = "obj";
        w.WriteLine();
        w.WriteLine("s");
        w.WriteLine(bo);
        w.WriteLine(i);
        w.WriteLine(db);
        w.WriteLine(ch);
        w.WriteLine(cs);
        w.WriteLine(ob);
        P("writeline.values", w.ToString().Replace("\r\n", "<crlf>"));
    }

    static void WriterWriteLineFormats()
    {
        StringWriter w = new StringWriter();
        w.WriteLine("{0}", "a");
        w.WriteLine("{0}-{1}", "a", "b");
        w.WriteLine("abcdef", 1, 3);
        P("writeline.formats", w.ToString().Replace("\r\n", "<crlf>"));
    }

    static void WriterCtors()
    {
        StringWriter a = new StringWriter(CultureInfo.InvariantCulture);
        a.Write("inv");
        P("ctor.provider", a.ToString());

        StringBuilder sb = new StringBuilder("pre");
        StringWriter b = new StringWriter(sb);
        b.Write("-post");
        P("ctor.sb", b.ToString());
        P("ctor.sb.identity", ReferenceEquals(sb, b.GetStringBuilder()));
        P("ctor.sb.builder", sb.ToString());

        StringWriter c = new StringWriter(new StringBuilder("x"), CultureInfo.InvariantCulture);
        c.Write("y");
        P("ctor.sb.provider", c.ToString());
    }

    static void WriterSurface()
    {
        StringWriter w = new StringWriter();
        w.Write("abc");
        P("encoding", w.Encoding.GetType().Name);
        P("formatprovider", w.FormatProvider.ToString());
        string old = w.NewLine;
        w.NewLine = "<NL>";
        w.WriteLine("z");
        P("newline", w.ToString());
        w.NewLine = old;
        P("getstringbuilder", w.GetStringBuilder().ToString());
        w.Dispose();
    }

    static void Main()
    {
        ReaderBasics();
        ReaderBlock();
        WriterWriteValues();
        WriterWriteRanges();
        WriterWriteFormats();
        WriterWriteLineValues();
        WriterWriteLineFormats();
        WriterCtors();
        WriterSurface();
    }
}

