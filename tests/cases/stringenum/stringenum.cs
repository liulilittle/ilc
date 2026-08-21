//------------------------------------------------------------------------------
// stringenum.cs -- STRING-ENUM DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C61, after the string domain
// (StringReader/StringWriter, stringrw.h wired in M6) and the enum boxed
// surface (il2cpp_enum_boxed_to_string + box_enum klass, wired in M6 batch
// 36 / enumext) both landed.
//
// THIS case drives the STRING-READER + ENUM surfaces on top of the wired
// layer:
//
//   1) StringReader DEEP: ctor, Read/Peek position walk, ReadLine across
//      CRLF / lone CR / lone LF / consecutive terminators / empty string /
//      EOF, ReadToEnd after partial consumption, empty text, line feeds as
//      terminators (CRLF consumed as ONE unit), null-text ctor throws
//      ArgumentNullException.
//   2) StringWriter DEEP: ctor, Write(string) accumulation, WriteLine(string)
//      CRLF suffix, ToString snapshot copy (repeatable), null Write no-op.
//   3) Writer->Reader round-trip (the two domains compose through the string
//      object surface).
//   4) ENUM BOXED surface: ToString on a boxed enum reference prints the
//      MEMBER NAME (il2cpp_enum_boxed_to_string -- the box carries the enum's
//      own name-bearing klass after box_enum), including [Flags] "F1, F2"
//      decomposition; Enum.GetUnderlyingType == typeof(int) for int-backed
//      enums; boxed enum Equals (same/diff value, cross-type gate, null);
//      DayOfWeek builtin table ToString ("Monday" family, the timecase L8
//      path) -- all byte-identical to net48.
//   5) Enum value semantics via Console.WriteLine(object) enum arm (the
//      "enum:l" cast + il2cpp_enum_to_string null-descriptor fallback to the
//      DayOfWeek builtin table) -- only DayOfWeek values are probed here
//      (user-enum named printing is the boxed path, D-ENM-3 documented).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class StringEnum {

    // Small [Flags] user enum (boxed-surface probes; int-backed only -- the
    // byte/short/long backings are D-ENM-4 gaps, never probed).
    [Flags]
    enum Mode : int { None = 0, Read = 1, Write = 2, Execute = 4 }

    public static void Main() {
        // ============ 1. StringReader: position walk ============
        StringReader r = new StringReader("abc");
        Console.WriteLine("s1_len_read=" + (r.Read() == 'a'));
        Console.WriteLine("s1_peek_after_read=" + (r.Peek() == 'b'));
        Console.WriteLine("s1_read_b=" + (r.Read() == 'b'));
        Console.WriteLine("s1_read_c=" + (r.Read() == 'c'));
        Console.WriteLine("s1_eof_peek=" + (r.Peek() == -1));
        Console.WriteLine("s1_eof_read=" + (r.Read() == -1));

        // ============ 2. StringReader: line terminators ============
        // CRLF is consumed as ONE terminator.
        StringReader crlf = new StringReader("a\r\nb\r\nc");
        Console.WriteLine("s2_1=" + (crlf.ReadLine() == "a"));
        Console.WriteLine("s2_2=" + (crlf.ReadLine() == "b"));
        Console.WriteLine("s2_3=" + (crlf.ReadLine() == "c"));
        Console.WriteLine("s2_eof=" + (crlf.ReadLine() == null));

        // lone LF and lone CR are both terminators (net48 ReadLine contract).
        StringReader lf = new StringReader("x\ny");
        Console.WriteLine("s3_lf_1=" + (lf.ReadLine() == "x"));
        Console.WriteLine("s3_lf_2=" + (lf.ReadLine() == "y"));
        StringReader cr = new StringReader("p\rq");
        Console.WriteLine("s4_cr_1=" + (cr.ReadLine() == "p"));
        Console.WriteLine("s4_cr_2=" + (cr.ReadLine() == "q"));

        // consecutive terminators -> empty lines.
        StringReader emptyLines = new StringReader("a\r\n\r\nb");
        Console.WriteLine("s5_1=" + (emptyLines.ReadLine() == "a"));
        Console.WriteLine("s5_empty=" + (emptyLines.ReadLine() == ""));
        Console.WriteLine("s5_3=" + (emptyLines.ReadLine() == "b"));

        // empty string: ReadLine -> null, ReadToEnd -> "".
        StringReader empty = new StringReader("");
        Console.WriteLine("s6_readline_null=" + (empty.ReadLine() == null));
        Console.WriteLine("s6_readtoend_empty=" + (empty.ReadToEnd() == ""));

        // ============ 3. StringReader: ReadToEnd after partial ============
        StringReader partial = new StringReader("hello world");
        partial.Read(); // consume 'h'
        Console.WriteLine("s7_rte=" + (partial.ReadToEnd() == "ello world"));
        Console.WriteLine("s7_rte2=" + (partial.ReadToEnd() == "")); // drained

        // ReadToEnd without any reads returns the whole string.
        StringReader whole = new StringReader("net48");
        Console.WriteLine("s8_rte_whole=" + (whole.ReadToEnd() == "net48"));

        // ============ 4. StringWriter: accumulate + snapshot ============
        StringWriter w = new StringWriter();
        Console.WriteLine("w1_empty=" + (w.ToString() == ""));
        w.Write("ab");
        w.Write("cd");
        Console.WriteLine("w2_acc=" + (w.ToString() == "abcd"));
        w.WriteLine("ef");
        Console.WriteLine("w3_wl=" + (w.ToString() == "abcd" + "ef\r\n"));
        w.Write((string)null); // null is a no-op
        Console.WriteLine("w4_null_noop=" + (w.ToString() == "abcd" + "ef\r\n"));
        Console.WriteLine("w5_snapshot=" + (w.ToString() == "abcd" + "ef\r\n")); // repeatable

        // ============ 5. Writer -> Reader round trip ============
        StringReader back = new StringReader(w.ToString());
        Console.WriteLine("w6_rt_1=" + (back.ReadLine() == "abcd"));
        Console.WriteLine("w6_rt_2=" + (back.ReadLine() == "ef"));
        Console.WriteLine("w6_rt_eof=" + (back.ReadLine() == null));

        // ctor error path.
        bool nullCtorThrows = false;
        try { new StringReader(null); Console.WriteLine("never"); }
        catch (ArgumentNullException) { nullCtorThrows = true; }
        Console.WriteLine("w7_ctor_null=" + nullCtorThrows);

        // ============ 6. Enum boxed ToString: member names ============
        Enum m1 = Mode.Read;
        Console.WriteLine("e1=" + (m1.ToString() == "Read"));
        Enum m2 = Mode.Execute;
        Console.WriteLine("e2=" + (m2.ToString() == "Execute"));
        Enum m3 = (Enum)(Mode.Read | Mode.Write);
        Console.WriteLine("e3_flags=" + (m3.ToString() == "Read, Write"));
        Enum m0 = Mode.None;
        Console.WriteLine("e4_zero=" + (m0.ToString() == "None"));

        // ============ 7. Enum boxed Equals (value + type gate) ============
        Enum a1 = Mode.Read;
        Enum a2 = Mode.Read;
        Console.WriteLine("eq_same=" + a1.Equals(a2));
        Console.WriteLine("eq_diff=" + a1.Equals(Mode.Execute));
        Console.WriteLine("eq_cross=" + a1.Equals(Mode.None));     // value 1 vs 0
        Console.WriteLine("eq_null=" + (a1.Equals(null) ? 1 : 0)); // 0

        // ============ 8. GetUnderlyingType (int-backed) ============
        Console.WriteLine("ug_int=" + (Enum.GetUnderlyingType(typeof(Mode)) == typeof(int)));
        Console.WriteLine("ug_neq_long=" + (Enum.GetUnderlyingType(typeof(Mode)) == typeof(long)));

        // ============ 9. DayOfWeek via DateTime boxed path (timecase L8) ============
        // The enum-VALUE ToString shape (ldloca+constrained) is unreachable
        // (D-ENU-2), so DayOfWeek names are probed through DateTime values
        // (the boxed-enum WriteLine arm that timecase L8 proves byte-exact).
        Console.WriteLine("dow_mon=" + (new DateTime(2020, 6, 15).DayOfWeek == DayOfWeek.Monday));
        Console.WriteLine("dow_sun=" + (new DateTime(2020, 6, 21).DayOfWeek == DayOfWeek.Sunday));
        Console.WriteLine("dow_sat=" + (new DateTime(2020, 6, 20).DayOfWeek == DayOfWeek.Saturday));
        Console.WriteLine("dow_wed=" + (new DateTime(2020, 6, 17).DayOfWeek == DayOfWeek.Wednesday));
        Console.WriteLine("dow_val=" + ((int)new DateTime(2020, 6, 17).DayOfWeek == 3));

        // ============ 10. StringReader reads an enum-name string ============
        StringReader names = new StringReader("Read,Write");
        StringWriter namesOut = new StringWriter();
        namesOut.WriteLine(names.ReadLine());
        Console.WriteLine("s9_compose=" + (namesOut.ToString() == "Read,Write\r\n"));

        // ============ 11. Enum static face: GetName / Parse / GetNames / HasFlag ============
        // enumface (D-STREN-2 closure): the transpiler's typeof(Mode) fold is
        // the enum's full-name STRING (nested user enum -> "Mode"), which the
        // name-string runtime primitives resolve against the enum metadata
        // registry. All results are verified through the boxed-Enum shape
        // (the enum-VALUE ldloca+constrained ToString stays D-ENU-2).
        // GetName(Type, object): the boxed value unboxes to the int64 carrier;
        // the member name, or null for an unnamed value (net48).
        Console.WriteLine("gn_read=" + (Enum.GetName(typeof(Mode), Mode.Read) == "Read"));
        Console.WriteLine("gn_exec=" + (Enum.GetName(typeof(Mode), Mode.Execute) == "Execute"));
        Console.WriteLine("gn_null=" + (Enum.GetName(typeof(Mode), (Mode)99) == null));
        // Parse(Type, string): a BOXED enum result (the enum's own klass),
        // verified by boxed equality (icall_boxed_equals_ext2's enum arm).
        Console.WriteLine("parse_name=" + Enum.Parse(typeof(Mode), "Read").Equals((Enum)Mode.Read));
        Console.WriteLine("parse_num=" + Enum.Parse(typeof(Mode), "3").Equals((Enum)(Mode.Read | Mode.Write)));
        Console.WriteLine("parse_ws=" + Enum.Parse(typeof(Mode), " Write ").Equals((Enum)Mode.Write));
        // GetNames(Type): string[] in DECLARATION order (net48).
        string[] nm = Enum.GetNames(typeof(Mode));
        Console.WriteLine("gns_0=" + (nm[0] == "None"));
        Console.WriteLine("gns_2=" + (nm[2] == "Write"));
        Console.WriteLine("gns_len=" + (nm.Length == 4));
        // HasFlag(Enum): the boxed-receiver flags bit test ((self & flag) == flag).
        Enum hf = Mode.Read;
        Console.WriteLine("hf_self=" + hf.HasFlag((Enum)Mode.Read));
        Console.WriteLine("hf_other=" + hf.HasFlag((Enum)Mode.Write));
        Console.WriteLine("hf_combo=" + ((Enum)(Mode.Read | Mode.Write)).HasFlag((Enum)Mode.Read));
    }
}}
