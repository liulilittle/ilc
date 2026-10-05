//------------------------------------------------------------------------------
// binaryiodeep.cs -- binary byte-stream DEEP probe (e2e batch C56):
// little-endian binary protocol layout + type round-trips over the reachable
// System.IO byte-stream surface, byte-exact vs the net48 oracle.
//
// SCOPE (updated 2026-08-14, batch 150d): the BinaryReader / BinaryWriter
// OBJECT surface is now WIRED -- MethodMap.BinaryIO.cs maps the managed
// icalls (icall_binary_read_*/write_*, streamio.cpp) with the Stream-taking
// ctors (the runtime descriptors + shared StreamState exist since binrw-b23;
// the handle-API mappings were corrected to the managed object surface).
// The round-trip path is exercised by the binrw2 e2e case. This case
// continues to pin the binary PROTOCOL semantics (little-endian fixed-width
// layout, type round-trips, multi-field records, overwrite / append / EOF)
// through the reachable composition of MemoryStream (byte-stream carrier) +
// BitConverter (exact binary encoders) -- the same byte-level contract
// BinaryReader/Writer speak, exercised deep. The bool width (1 byte) and the
// little-endian int32/int64/double/single layouts are exactly the
// BinaryWriter.Write protocol widths.
//
// net48-safe primitives used only:
//   * BitConverter.GetBytes / ToT(byte[], 0) -- the transpiler's ToT DROPS
//     startIndex (reads index 0), so every decode reads a dedicated buffer
//     at index 0 (never a nonzero offset).
//   * BitConverter.IsLittleEndian is LOUD -- never referenced.
//   * MemoryStream Write/Position/Length/ToArray/Read (streamcase precedent).
// Every probe prints a raw bool; all lines are deterministic on net48.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class Binaryiodeep {
    public static void Main() {
        // =============== S1: int32 little-endian round-trip matrix ============
        int[] i32s = new int[] { 0, 1, -1, 42, 123456, int.MaxValue, int.MinValue };
        MemoryStream ms = new MemoryStream();
        bool s1 = true;
        for (int i = 0; i < i32s.Length; i++) {
            byte[] enc = BitConverter.GetBytes(i32s[i]);
            ms.Write(enc, 0, enc.Length);
            byte[] got = ms.ToArray();
            byte[] rt = new byte[4];
            rt[0] = got[got.Length - 4]; rt[1] = got[got.Length - 3];
            rt[2] = got[got.Length - 2]; rt[3] = got[got.Length - 1];
            if (!(enc.Length == 4 && BitConverter.ToInt32(rt, 0) == i32s[i])) s1 = false;
        }
        Console.WriteLine("i32_rt=" + s1);

        // Exact LE byte layout (byte-exact binary):
        byte[] b42 = BitConverter.GetBytes(42);
        Console.WriteLine("i32_le=" + (b42.Length == 4 && b42[0] == 42 && b42[1] == 0 && b42[2] == 0 && b42[3] == 0));
        byte[] bneg = BitConverter.GetBytes(-1);
        Console.WriteLine("i32_neg=" + (bneg.Length == 4 && bneg[0] == 255 && bneg[1] == 255 && bneg[2] == 255 && bneg[3] == 255));
        byte[] bmin = BitConverter.GetBytes(int.MinValue);
        Console.WriteLine("i32_min=" + (bmin.Length == 4 && bmin[0] == 0 && bmin[1] == 0 && bmin[2] == 0 && bmin[3] == 128));

        // =============== S2: int64 / uint32 / double / single / bool ==========
        MemoryStream ms2 = new MemoryStream();
        long lv = 0x0102030405060708L;
        byte[] lg0 = BitConverter.GetBytes(lv);
        ms2.Write(lg0, 0, lg0.Length);
        byte[] lg = ms2.ToArray();
        Console.WriteLine("i64_rt=" + (lg.Length == 8 && BitConverter.ToInt64(lg, 0) == lv));
        Console.WriteLine("i64_le=" + (lg[0] == 0x08 && lg[1] == 0x07 && lg[6] == 0x02 && lg[7] == 0x01));

        byte[] u32 = BitConverter.GetBytes(0xFFFFFFFFu);
        Console.WriteLine("u32_le=" + (u32.Length == 4 && u32[0] == 255 && u32[1] == 255 && u32[2] == 255 && u32[3] == 255));

        double dv = 1.5;
        byte[] db = BitConverter.GetBytes(dv);
        Console.WriteLine("dbl_rt=" + (db.Length == 8 && BitConverter.ToDouble(db, 0) == dv));
        byte[] d1 = BitConverter.GetBytes(1.0);
        Console.WriteLine("dbl_le=" + (d1.Length == 8 && d1[0] == 0 && d1[1] == 0 && d1[6] == 0xF0 && d1[7] == 0x3F));
        byte[] dneg = BitConverter.GetBytes(-0.25);
        Console.WriteLine("dbl_neg=" + (dneg.Length == 8 && dneg[7] == 0xBF && dneg[6] == 0xD0));

        float fv = 2.5f;
        byte[] fb = BitConverter.GetBytes(fv);
        Console.WriteLine("sgl_rt=" + (fb.Length == 4 && BitConverter.ToSingle(fb, 0) == fv));
        byte[] fs1 = BitConverter.GetBytes(1.0f);
        Console.WriteLine("sgl_le=" + (fs1.Length == 4 && fs1[0] == 0 && fs1[1] == 0 && fs1[2] == 0x80 && fs1[3] == 0x3F));

        // BinaryWriter.Write(bool) protocol width: exactly 1 byte (0x01 / 0x00).
        byte[] tb = BitConverter.GetBytes(true);
        byte[] fb2 = BitConverter.GetBytes(false);
        Console.WriteLine("bool_rt=" + (tb.Length == 1 && BitConverter.ToBoolean(tb, 0) && fb2.Length == 1 && !BitConverter.ToBoolean(fb2, 0)));
        Console.WriteLine("bool_le=" + (tb[0] == 1 && fb2[0] == 0));

        // =============== S3: multi-field binary record round-trip =============
        // record = int32 tag (4) + double value (8) + bool flag (1) + int64 id (8) = 21 bytes
        MemoryStream r = new MemoryStream();
        byte[] t0 = BitConverter.GetBytes(7);
        byte[] t1 = BitConverter.GetBytes(2.5);
        byte[] t2 = BitConverter.GetBytes(true);
        byte[] t3 = BitConverter.GetBytes(1000L);
        r.Write(t0, 0, t0.Length);
        r.Write(t1, 0, t1.Length);
        r.Write(t2, 0, t2.Length);
        r.Write(t3, 0, t3.Length);
        byte[] rec = r.ToArray();
        Console.WriteLine("rec_len=" + (rec.Length == 21));
        byte[] f0 = new byte[4]; f0[0] = rec[0]; f0[1] = rec[1]; f0[2] = rec[2]; f0[3] = rec[3];
        byte[] f1 = new byte[8]; for (int i = 0; i < 8; i++) f1[i] = rec[4 + i];
        byte[] f2 = new byte[1]; f2[0] = rec[12];
        byte[] f3 = new byte[8]; for (int i = 0; i < 8; i++) f3[i] = rec[13 + i];
        Console.WriteLine("rec_tag=" + (BitConverter.ToInt32(f0, 0) == 7));
        Console.WriteLine("rec_val=" + (BitConverter.ToDouble(f1, 0) == 2.5));
        Console.WriteLine("rec_flag=" + (BitConverter.ToBoolean(f2, 0) == true));
        Console.WriteLine("rec_id=" + (BitConverter.ToInt64(f3, 0) == 1000L));

        // Read the record back through the stream, field by field (binary reader
        // semantics: sequential little-endian consumption, 1-byte bool width).
        r.Position = 0;
        byte[] b0 = new byte[4]; r.Read(b0, 0, 4);
        byte[] b1 = new byte[8]; r.Read(b1, 0, 8);
        byte[] b2 = new byte[1]; r.Read(b2, 0, 1);
        byte[] b3 = new byte[8]; r.Read(b3, 0, 8);
        Console.WriteLine("seq=" + (BitConverter.ToInt32(b0, 0) == 7 && BitConverter.ToDouble(b1, 0) == 2.5 &&
                                    BitConverter.ToBoolean(b2, 0) && BitConverter.ToInt64(b3, 0) == 1000L));

        // =============== S4: overwrite / append / EOF on the carrier ==========
        r.Position = 0;
        byte[] n99 = BitConverter.GetBytes(99);
        r.Write(n99, 0, n99.Length);                        // overwrite the tag field
        r.Position = 0;
        byte[] ov = new byte[21];
        Console.WriteLine("ov_read=" + (r.Read(ov, 0, 21) == 21));
        byte[] ot = new byte[4]; ot[0] = ov[0]; ot[1] = ov[1]; ot[2] = ov[2]; ot[3] = ov[3];
        Console.WriteLine("ov_tag=" + (BitConverter.ToInt32(ot, 0) == 99));
        byte[] o1 = new byte[8]; for (int i = 0; i < 8; i++) o1[i] = ov[4 + i];
        Console.WriteLine("ov_val=" + (BitConverter.ToDouble(o1, 0) == 2.5));

        r.Position = r.Length;                              // append a 5th field
        byte[] n5 = BitConverter.GetBytes(5);
        r.Write(n5, 0, n5.Length);
        Console.WriteLine("ap_len=" + (r.Length == 25));
        r.Position = 21;
        byte[] tail = new byte[4];
        Console.WriteLine("ap_read=" + (r.Read(tail, 0, 4) == 4));
        Console.WriteLine("ap_val=" + (BitConverter.ToInt32(tail, 0) == 5));

        r.Position = r.Length;                              // EOF read returns 0
        byte[] eof = new byte[4];
        Console.WriteLine("eof_read=" + (r.Read(eof, 0, 4) == 0));
    }
}}
