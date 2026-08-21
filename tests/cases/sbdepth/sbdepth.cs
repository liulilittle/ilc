//------------------------------------------------------------------------------
// sbdepth.cs -- System.Text.StringBuilder DEEP probe 3 (e2e batch C68):
// capacity / formatting / chaining -- byte-exact vs the net48 oracle.
// Goes beyond stringbuilddeep (deep 2: chain + capacity + replace + insert
// + AppendFormat basics) into:
//   S1 EXACT capacity probes: ctor(int) pins, ctor(0)->default 16, ctor(string)
//      prealloc, the net48 Grow policy newCapacity = max(required,
//      max(Length, oldCapacity) * 2) pinned at growth boundaries, capacity
//      KEPT by Remove / Insert-into-room / Replace-shrink, and the Insert /
//      Replace grow policies (oldCapacity + valueLength / oldCapacity +
//      totalDelta).
//   S2 FORMATTING deep: AppendFormat alignment ({0,5}/{0,-5} min-width,
//      string args, no truncation), format specs ({0:D5}/{0:X4}/{0:D4}/{0:F2}/
//      {0:N1}), index reordering ({1}-{0}), skipped indexes, escaped braces
//      ({{/}}), edge-brace nesting ({{{0}}}) and the FormatException /
//      ArgumentNullException error set.
//   S3 CHAINING deep: reference identity of the chained return values
//      (Append/Insert/Replace return the SAME builder -- object.ReferenceEquals
//      across growth), single-statement mega-chains mixing every operation,
//      and Append(object) dispatch (boxed int/long/bool/char/string/null).
//
// All AppendFormat args are int/string/double (the wired composite-engine
// dispatch -- boxed Int32/Int64/Double/Single + string + other objects via
// icall_object_to_string). Decimal/params-object[]/IFormatProvider forms are
// NOT wired (recorded D-SBD-1). Output is deterministic and byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class SbDepth {
    public static void Main()
    {
        // ================= S1: exact capacity probes =================
        Console.WriteLine("cap_def=" + (new StringBuilder().Capacity == 16));
        Console.WriteLine("cap_0=" + (new StringBuilder(0).Capacity == 16));
        Console.WriteLine("cap_5=" + (new StringBuilder(5).Capacity == 5));
        Console.WriteLine("cap_16=" + (new StringBuilder(16).Capacity == 16));
        Console.WriteLine("cap_17=" + (new StringBuilder(17).Capacity == 17));
        Console.WriteLine("cap_s3=" + (new StringBuilder("abc").Capacity == 16));
        Console.WriteLine("cap_s20=" + (new StringBuilder("01234567890123456789").Capacity == 20));

        // growth boundary: 16 exact then +1 -> 32 (doubling wins)
        StringBuilder sb16 = new StringBuilder(16);
        sb16.Append("0123456789abcdef");
        Console.WriteLine("g16_full=" + (sb16.Capacity == 16 && sb16.Length == 16));
        sb16.Append("X");
        Console.WriteLine("g16_grow=" + (sb16.Capacity == 32));
        Console.WriteLine("g16_len=" + (sb16.Length == 17));

        // ctor(3) + 3 -> 3, +1 -> max(4, 6) = 6
        StringBuilder sb3 = new StringBuilder(3);
        sb3.Append("abc");
        Console.WriteLine("g3_fill=" + (sb3.Capacity == 3 && sb3.Length == 3));
        sb3.Append("d");
        Console.WriteLine("g3_grow=" + (sb3.Capacity == 6));
        Console.WriteLine("g3_text=" + (sb3.ToString() == "abcd"));

        // ctor(17) + 17 -> 17, +1 -> max(18, 34) = 34
        StringBuilder sb17 = new StringBuilder(17);
        sb17.Append("0123456789abcdefg");
        Console.WriteLine("g17_fill=" + (sb17.Capacity == 17));
        sb17.Append("!");
        Console.WriteLine("g17_grow=" + (sb17.Capacity == 34));

        // ctor(string 20) + 1 -> max(21, 40) = 40
        StringBuilder sbL = new StringBuilder("0123456789ABCDEFGHIJ");
        sbL.Append("!");
        Console.WriteLine("g20_grow=" + (sbL.Capacity == 40));

        // Remove does NOT shrink capacity
        StringBuilder sbK = new StringBuilder("0123456789");
        Console.WriteLine("k_10=" + (sbK.Capacity == 16));
        sbK.Remove(0, 5);
        Console.WriteLine("k_rem1=" + (sbK.Length == 5 && sbK.Capacity == 16));
        sbK.Remove(0, 5);
        Console.WriteLine("k_rem2=" + (sbK.Length == 0 && sbK.Capacity == 16));

        // Insert growth: capacity 2 + Insert(3 units) -> 5 (oldCapacity + len)
        StringBuilder sbI = new StringBuilder(2);
        sbI.Append("ab");
        Console.WriteLine("i_cap2=" + (sbI.Capacity == 2 && sbI.Length == 2));
        sbI.Insert(0, "cde");
        Console.WriteLine("i_grow=" + (sbI.Capacity == 5));
        Console.WriteLine("i_text=" + (sbI.ToString() == "cdeab"));

        // Insert into room: no growth
        StringBuilder sbN = new StringBuilder(10);
        sbN.Append("ab");
        sbN.Insert(1, "XY");
        Console.WriteLine("i_room=" + (sbN.Capacity == 10));
        Console.WriteLine("i_roomtext=" + (sbN.ToString() == "aXYb"));

        // Replace growth: capacity 4 + delta(1) x 2 = 6
        StringBuilder sbR = new StringBuilder(4);
        sbR.Append("abab");
        sbR.Replace("ab", "xyz");
        Console.WriteLine("r_grow=" + (sbR.Capacity == 6));
        Console.WriteLine("r_text=" + (sbR.ToString() == "xyzxyz"));

        // ================= S2: formatting deep =================
        Console.WriteLine("f_0=" + (new StringBuilder().AppendFormat("{0}", 7).ToString() == "7"));
        Console.WriteLine("f_d5=" + (new StringBuilder().AppendFormat("{0:D5}", 42).ToString() == "00042"));
        Console.WriteLine("f_x4=" + (new StringBuilder().AppendFormat("{0:X4}", 255).ToString() == "00FF"));
        Console.WriteLine("f_d4n=" + (new StringBuilder().AppendFormat("{0:D4}", -7).ToString() == "-0007"));
        Console.WriteLine("f_x=" + (new StringBuilder().AppendFormat("{0:X}", 0xABCD).ToString() == "ABCD"));
        Console.WriteLine("f_f2=" + (new StringBuilder().AppendFormat("{0:F2}", 3.5).ToString() == "3.50"));
        Console.WriteLine("f_n1=" + (new StringBuilder().AppendFormat("{0:N1}", 1234.5).ToString() == "1,234.5"));
        Console.WriteLine("f_align_r=" + (new StringBuilder().AppendFormat("[{0,5}]", 42).ToString() == "[   42]"));
        Console.WriteLine("f_align_l=" + (new StringBuilder().AppendFormat("[{0,-5}]", 42).ToString() == "[42   ]"));
        Console.WriteLine("f_align_s=" + (new StringBuilder().AppendFormat("[{0,4}]", "ab").ToString() == "[  ab]"));
        Console.WriteLine("f_align_nc=" + (new StringBuilder().AppendFormat("[{0,3}]", "abcdef").ToString() == "[abcdef]"));
        Console.WriteLine("f_reorder=" + (new StringBuilder().AppendFormat("{1}-{0}", "a", "b").ToString() == "b-a"));
        Console.WriteLine("f_skip=" + (new StringBuilder().AppendFormat("{0} and {2}", "a", "b", "c").ToString() == "a and c"));
        Console.WriteLine("f_esc=" + (new StringBuilder().AppendFormat("{0} {{x}}", 1).ToString() == "1 {x}"));
        Console.WriteLine("f_edge=" + (new StringBuilder().AppendFormat("{{{0}}}", 5).ToString() == "{5}"));
        Console.WriteLine("f_multi=" + (new StringBuilder().AppendFormat("{0}={1:X} @{2,4}", 42, 0x1F, 7).ToString() == "42=1F @   7"));
        // chained formatting with literals
        StringBuilder fm = new StringBuilder();
        fm.Append("p=").AppendFormat("{0:X}", 0xABCD).Append(" q=").AppendFormat("{0,3}", 5);
        Console.WriteLine("f_chain=" + (fm.ToString() == "p=ABCD q=  5"));

        // formatting error set (net48 exception TYPE names)
        StringBuilder fe = new StringBuilder("seed");
        bool threw = false;
        try { fe.AppendFormat(null, 1); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("e_null=" + threw);
        threw = false;
        try { fe.AppendFormat("{", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_lbrace=" + threw);
        threw = false;
        try { fe.AppendFormat("}", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_rbrace=" + threw);
        threw = false;
        try { fe.AppendFormat("{x}", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_nondigit=" + threw);
        threw = false;
        try { fe.AppendFormat("{0", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_unclosed=" + threw);
        threw = false;
        try { fe.AppendFormat("{2}", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_oob=" + threw);
        threw = false;
        try { fe.AppendFormat("{}", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_empty=" + threw);
        threw = false;
        try { fe.AppendFormat("{0,}", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_align0=" + threw);
        threw = false;
        try { fe.AppendFormat("{0,5", 1); } catch (FormatException) { threw = true; }
        Console.WriteLine("e_alignnc=" + threw);
        Console.WriteLine("e_seed=" + (fe.ToString() == "seed"));   // unchanged by throws

        // ================= S3: chaining deep =================
        // reference identity: Append/Insert/Replace return the SAME builder
        StringBuilder id = new StringBuilder("base");
        Console.WriteLine("id_app=" + object.ReferenceEquals(id, id.Append("+a")));
        Console.WriteLine("id_app2=" + object.ReferenceEquals(id, id.Append("+b").Append("+c")));
        Console.WriteLine("id_ins=" + object.ReferenceEquals(id, id.Insert(0, "<")));
        Console.WriteLine("id_rep=" + object.ReferenceEquals(id, id.Replace("+b", "-b-")));
        // identity ACROSS a growth boundary (Append forcing a realloc)
        StringBuilder idg = new StringBuilder(2);
        idg.Append("xx");
        Console.WriteLine("idg_grow=" + object.ReferenceEquals(idg, idg.Append("yyy")));
        Console.WriteLine("idg_text=" + (idg.ToString() == "xxyyy"));
        // single-statement mega-chain mixing every wired operation
        StringBuilder mega = new StringBuilder("M");
        StringBuilder outM = mega.Append("x").Append(123).Append('c')
                                .AppendFormat("{0}", 1).AppendLine()
                                .Insert(0, "<").Replace("M", "m").Remove(0, 1);
        Console.WriteLine("mega_same=" + object.ReferenceEquals(mega, outM));
        Console.WriteLine("mega_len=" + (mega.Length == 9));
        Console.WriteLine("mega_text=" + (mega.ToString() == "mx123c1\r\n"));
        // Append(object) dispatch: boxed primitives + string + null no-op
        StringBuilder o = new StringBuilder();
        o.Append((object)42).Append(' ').Append((object)"txt").Append(' ');
        o.Append((object)9L).Append(' ').Append((object)true).Append(' ').Append((object)'Q');
        o.Append((object)null).Append((string)null);
        Console.WriteLine("obj_text=" + (o.ToString() == "42 txt 9 True Q"));
        Console.WriteLine("obj_len=" + (o.Length == 15));
        // chain reordering: Remove+Append on the same line then re-verify
        StringBuilder rr = new StringBuilder("0123456789");
        rr.Remove(0, 4).Append("END");
        Console.WriteLine("rr_text=" + (rr.ToString() == "456789END"));
    }
}}
