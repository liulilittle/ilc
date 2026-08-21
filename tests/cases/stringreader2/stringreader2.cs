//------------------------------------------------------------------------------
// stringreader2.cs -- System.IO.StringReader DEEP probe #2 (e2e batch C70).
// Extends stringrwcase (first-generation: read/peek/readline/readtoend
// basics) with the SPECIFIC line-family / EOF / nesting deep probes:
//
//   * READLINE FAMILY        -- every terminator form (CRLF / LF / CR) over
//     the same stream, lone-CR and lone-LF terminations, consecutive
//     terminators, empty lines, no-final-terminator EOF line.
//   * READLINE / EOF         -- ReadLine after a final terminator returns
//     null; an empty line is "" (never null); EOF probes for Read (-1),
//     Peek (-1), ReadLine (null), ReadToEnd ("") once exhausted.
//   * NESTING                -- StringWriter -> StringReader round trip with
//     multi-line content, a reader created from a line extracted by another
//     reader, and interleaved Read/Peek/ReadLine/ReadToEnd on one stream.
//
// Every probe prints a BOOL (string comparison) or an (int) code unit --
// never a raw char -- so the diff stays byte-stable across console
// encodings (stringrwcase discipline). net48 is the reference baseline (the
// runtime's stringrw.cpp implements the same CR/LF/CRLF terminator
// semantics, verified against corert StringReader.cs:182-189).
//
// DELIBERATELY NOT PROBED (documented runtime gap -- do NOT call here):
//   * StringReader.Read(char[], int, int) -- LOUD (MethodMap.StringIO.cs):
//     needs a managed-array shaped icall. Expressed via Read/ReadLine/
//     ReadToEnd only.
//   * Use-after-Close -- net48 throws ObjectDisposedException; this runtime
//     follows the M3 result conventions (read/peek -> -1, read_line ->
//     null). Documented divergence, stringrw.h -- NOT probed.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class StringReader2 {
    public static void Main()
    {
        // ===== S1: ReadLine family -- all terminator forms on one stream =====
        StringReader r = new StringReader("one\r\ntwo\nthree\rfour");
        Console.WriteLine("rl_crlf=" + (r.ReadLine() == "one"));      // CRLF
        Console.WriteLine("rl_lf=" + (r.ReadLine() == "two"));        // LF
        Console.WriteLine("rl_cr=" + (r.ReadLine() == "three"));      // CR
        Console.WriteLine("rl_eof_nl=" + (r.ReadLine() == "four"));   // no terminator
        Console.WriteLine("rl_eof=" + (r.ReadLine() == null));        // EOF -> null

        // ===== S2: consecutive terminators / empty lines =====
        r = new StringReader("\r\n\r\n");
        Console.WriteLine("empty1=" + (r.ReadLine() == ""));
        Console.WriteLine("empty2=" + (r.ReadLine() == ""));
        Console.WriteLine("empty_eof=" + (r.ReadLine() == null));
        r = new StringReader("\r\n");
        Console.WriteLine("one_empty=" + (r.ReadLine() == ""));
        Console.WriteLine("one_empty_eof=" + (r.ReadLine() == null));

        // ===== S3: lone CR / lone LF terminators at end-of-string =====
        r = new StringReader("a\r");
        Console.WriteLine("lone_cr=" + (r.ReadLine() == "a"));
        Console.WriteLine("lone_cr_eof=" + (r.ReadLine() == null));
        r = new StringReader("b\n");
        Console.WriteLine("lone_lf=" + (r.ReadLine() == "b"));
        Console.WriteLine("lone_lf_eof=" + (r.ReadLine() == null));

        // ===== S4: empty string / single char / final-terminator EOF =====
        r = new StringReader("");
        Console.WriteLine("empty_ctor=" + (r.ReadLine() == null));
        r = new StringReader("x");
        Console.WriteLine("single=" + (r.ReadLine() == "x"));
        Console.WriteLine("single_eof=" + (r.ReadLine() == null));
        r = new StringReader("a\r\n");
        Console.WriteLine("trail_crlf=" + (r.ReadLine() == "a"));
        Console.WriteLine("trail_crlf_eof=" + (r.ReadLine() == null));

        // ===== S5: EOF for the whole reader family after exhaustion =====
        r = new StringReader("ab");
        Console.WriteLine("eof_read_a=" + (r.Read() == 'a'));
        Console.WriteLine("eof_peek_b=" + (r.Peek() == 'b'));
        Console.WriteLine("eof_readline_b=" + (r.ReadLine() == "b"));
        Console.WriteLine("eof_read_m1=" + (r.Read() == -1));
        Console.WriteLine("eof_peek_m1=" + (r.Peek() == -1));
        Console.WriteLine("eof_readline_null=" + (r.ReadLine() == null));
        Console.WriteLine("eof_readtoend=" + (r.ReadToEnd() == ""));

        // ===== S6: ReadToEnd from the middle =====
        r = new StringReader("hello\nworld");
        Console.WriteLine("rte_line=" + (r.ReadLine() == "hello"));
        Console.WriteLine("rte_rest=" + (r.ReadToEnd() == "world"));
        Console.WriteLine("rte_again=" + (r.ReadToEnd() == ""));

        // ===== S7: nesting -- writer -> reader round trip + reader-of-line =====
        StringWriter w = new StringWriter();
        w.WriteLine("alpha");
        w.WriteLine("beta");
        w.Write("gamma");
        Console.WriteLine("w_content=" + (w.ToString() == "alpha\r\nbeta\r\ngamma"));
        r = new StringReader(w.ToString());
        Console.WriteLine("nest_l1=" + (r.ReadLine() == "alpha"));
        Console.WriteLine("nest_l2=" + (r.ReadLine() == "beta"));
        Console.WriteLine("nest_rest=" + (r.ReadToEnd() == "gamma"));
        StringReader outer = new StringReader("outer\ninner");
        Console.WriteLine("nest_outer=" + (outer.ReadLine() == "outer"));
        StringReader inner = new StringReader(outer.ReadLine());
        Console.WriteLine("nest_inner=" + (inner.ReadLine() == "inner"));
        Console.WriteLine("nest_inner_eof=" + (inner.ReadLine() == null));

        // ===== S8: interleaved Read / Peek / ReadLine / ReadToEnd =====
        r = new StringReader("12345");
        Console.WriteLine("il_read=" + (r.Read() == '1'));
        Console.WriteLine("il_peek=" + (r.Peek() == '2'));
        Console.WriteLine("il_peek2=" + (r.Peek() == '2'));   // Peek does not consume
        Console.WriteLine("il_read2=" + (r.Read() == '2'));
        Console.WriteLine("il_rte=" + (r.ReadToEnd() == "345"));
        r = new StringReader("abc");
        Console.WriteLine("il_rte_first=" + (r.ReadToEnd() == "abc"));
        Console.WriteLine("il_readline_after=" + (r.ReadLine() == null));

        // ===== S9: ctor null text -> ArgumentNullException (caught) =====
        try { new StringReader(null); Console.WriteLine("ctor_null=False"); }
        catch (ArgumentNullException) { Console.WriteLine("ctor_null=True"); }
    }
}}
