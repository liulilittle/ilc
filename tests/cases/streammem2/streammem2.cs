//------------------------------------------------------------------------------
// streammem2.cs -- MemoryStream ctor family + GetBuffer/Capacity/expandability
// + Stream.CopyTo (io2 batch B1).
//
// net48 MemoryStream state matrix (verified against the net48 oracle):
//   ()                                   writable, expandable, exposable
//   (byte[])                             writable, NOT expandable, NOT exposable
//   (int capacity)                       writable, expandable, exposable
//   (byte[], bool writable)              writable per flag, NOT exposable
//   (byte[], int index, int count)       writable, NOT expandable, NOT exposable
//   (byte[], int, int, bool writable)
//   (byte[], int, int, bool, bool publiclyVisible)
// Every probe prints a bool / raw number / exception type name.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class Streammem2 {
    static string Hex(byte[] b) {
        string s = "";
        for (int i = 0; i < b.Length; i++) s += b[i].ToString("X2");
        return s;
    }
    static string Ex(Action a) {
        try { a(); return "none"; }
        catch (Exception e) { return e.GetType().Name; }
    }
    public static void Main() {
        MemoryStream a = new MemoryStream();
        Console.WriteLine("a_rw=" + (a.CanRead && a.CanWrite && a.CanSeek));
        Console.WriteLine("a_len=" + a.Length);
        Console.WriteLine("a_pos=" + a.Position);
        Console.WriteLine("a_cap=" + a.Capacity);
        Console.WriteLine("a_buf=" + a.GetBuffer().Length);

        byte[] seed = new byte[] { 1, 2, 3 };
        MemoryStream b = new MemoryStream(seed);
        Console.WriteLine("b_len=" + b.Length);
        Console.WriteLine("b_pos=" + b.Position);
        Console.WriteLine("b_canwrite=" + b.CanWrite);
        Console.WriteLine("b_cap=" + b.Capacity);
        Console.WriteLine("b_r0=" + b.ReadByte());
        Console.WriteLine("b_after=" + b.Position);
        Console.WriteLine("b_getbuf=" + Ex(() => { byte[] x = b.GetBuffer(); }));
        b.Position = 0;
        Console.WriteLine("b_write=" + Ex(() => b.WriteByte(9)));
        Console.WriteLine("b_seed1=" + seed[0]);
        b.Position = 3;
        Console.WriteLine("b_grow=" + Ex(() => b.WriteByte(9)));

        MemoryStream c = new MemoryStream(10);
        Console.WriteLine("c_cap=" + c.Capacity);
        Console.WriteLine("c_len=" + c.Length);
        Console.WriteLine("c_canwrite=" + c.CanWrite);
        Console.WriteLine("c_buf=" + c.GetBuffer().Length);

        MemoryStream d = new MemoryStream(new byte[] { 7, 8, 9 }, false);
        Console.WriteLine("d_canwrite=" + d.CanWrite);
        Console.WriteLine("d_len=" + d.Length);
        Console.WriteLine("d_r0=" + d.ReadByte());
        Console.WriteLine("d_getbuf=" + Ex(() => { byte[] x = d.GetBuffer(); }));

        byte[] big = new byte[] { 10, 11, 12, 13, 14 };
        MemoryStream e = new MemoryStream(big, 1, 3);
        Console.WriteLine("e_pos=" + e.Position);
        Console.WriteLine("e_len=" + e.Length);
        Console.WriteLine("e_cap=" + e.Capacity);
        Console.WriteLine("e_canwrite=" + e.CanWrite);
        Console.WriteLine("e_r0=" + e.ReadByte());
        Console.WriteLine("e_pos2=" + e.Position);
        e.Position = 0;
        Console.WriteLine("e_seek0=" + e.ReadByte());
        Console.WriteLine("e_getbuf=" + Ex(() => { byte[] x = e.GetBuffer(); }));
        e.Position = 1;
        Console.WriteLine("e_write=" + Ex(() => e.WriteByte(99)));
        Console.WriteLine("e_big2=" + big[2]);

        MemoryStream f = new MemoryStream(big, 1, 3, true);
        Console.WriteLine("f_pos=" + f.Position);
        Console.WriteLine("f_len=" + f.Length);
        Console.WriteLine("f_canwrite=" + f.CanWrite);
        Console.WriteLine("f_r0=" + f.ReadByte());
        f.Position = 2;
        f.WriteByte(99);
        Console.WriteLine("f_wrote3=" + (big[3] == 99));
        Console.WriteLine("f_len2=" + f.Length);

        MemoryStream g = new MemoryStream(big, 1, 3, true, true);
        Console.WriteLine("g_buflen=" + g.GetBuffer().Length);
        Console.WriteLine("g_cap=" + g.Capacity);

        MemoryStream h = new MemoryStream();
        h.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
        Console.WriteLine("h_len=" + h.Length);
        h.SetLength(2);
        Console.WriteLine("h_trunc=" + h.Length);
        Console.WriteLine("h_cap_ge=" + (h.Capacity >= 2));
        h.Capacity = 100;
        Console.WriteLine("h_cap100=" + (h.Capacity == 100));
        Console.WriteLine("h_capshrink=" + Ex(() => { h.Capacity = 4; }));
        Console.WriteLine("h_cap4=" + h.Capacity);
        Console.WriteLine("h_len2=" + h.Length);
        Console.WriteLine("h_capthrow=" + Ex(() => { h.Capacity = 1; }));

        // Non-expandable stream: Capacity cannot grow; writes past end throw.
        Console.WriteLine("e_capthrow=" + Ex(() => { e.Capacity = 100; }));
        e.Position = e.Length;
        Console.WriteLine("e_write_end=" + Ex(() => e.WriteByte(1)));

        // Stream.CopyTo(Stream) / CopyTo(Stream, bufferSize).
        MemoryStream src = new MemoryStream();
        src.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
        src.Position = 1;
        MemoryStream dst = new MemoryStream();
        src.CopyTo(dst);
        byte[] cp = dst.ToArray();
        Console.WriteLine("cp_len=" + cp.Length);
        Console.WriteLine("cp_first=" + cp[0]);
        Console.WriteLine("cp_last=" + cp[3]);
        Console.WriteLine("cp_srcpos=" + src.Position);

        src.Position = 0;
        MemoryStream dst2 = new MemoryStream();
        src.CopyTo(dst2, 2);
        Console.WriteLine("cp2_len=" + dst2.Length);
        Console.WriteLine("cp2_last=" + dst2.ToArray()[4]);
        Console.WriteLine("cp2_srcpos=" + src.Position);

        MemoryStream ro = new MemoryStream(new byte[8], false);
        src.Position = 0;
        Console.WriteLine("cp_ro=" + Ex(() => src.CopyTo(ro)));

        // BufferSize validation.
        src.Position = 0;
        Console.WriteLine("cp_bs0=" + Ex(() => src.CopyTo(dst2, 0)));
    }
}}
