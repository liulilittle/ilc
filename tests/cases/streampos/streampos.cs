//------------------------------------------------------------------------------
// streampos.cs -- System.IO stream POSITION deep probe (e2e batch C72): the
// Position get/set round-trip surface over FileStream and MemoryStream --
// position after write/read, absolute repositioning, EOF reads, position
// past the end, negative-position rejection, and the length/timeout surface.
// Byte-exact vs the net48 oracle (expected.txt).
//
// Builds on streamcase/streamops/streamdeep2 (which probe the write/read
// chain and Position basics) with a POSITION-CENTRIC deep matrix over the
// wired stream surface (MethodMap.StreamIO.cs -> icall_stream_{get,set}_position
// / get_length / get_can_* / get_can_timeout / read_bytes / write_bytes /
// memory_to_array).
//
// net48 ground-truth pinned by probe (2026-08-12):
//   * Write advances Position to the byte count; Length == Position after an
//     in-bounds write chain. Position = 0 + full Read returns every byte and
//     leaves Position == Length.
//   * Absolute reposition (Position = 3) then a partial Read advances by the
//     bytes actually read (3 + 4 = 7); the window bytes match.
//   * Position past the end (MemoryStream) -> Read returns 0 (not an
//     exception) and Position is UNCHANGED.
//   * Position == Length -> Read returns 0 (clean EOF, no exception).
//   * Writing at a Position past the end extends Length to position+count and
//     advances Position accordingly (the intermediate GAP bytes are not
//     probed -- see D-STP-1).
//   * Negative Position -> ArgumentOutOfRangeException.
//   * CanRead / CanWrite true for both; CanTimeout false for both.
//   * FileStream mirrors MemoryStream: write chain, seek to 0, full read,
//     mid re-position + partial read, overwrite at a repositioned spot, EOF
//     read returns 0, Position == Length at EOF.
//
// DELIBERATELY NOT PROBED (documented runtime/mapping gaps -- see GAPS.md):
//   * StreamWriter/StreamReader text surface -- covered by streamcase/
//     streamdeep2; not re-probed here.
//
// D-STP-2/3 (2026-08-12): Seek / CanSeek / SetLength / ReadByte / WriteByte
// are now wired (MethodMap.StreamIO.cs -> icall_stream_{seek,get_can_seek,
// set_length,read_byte,write_byte}, streamio.cpp) and probed in section 3:
// origin-aware Seek (Begin/Current/End) returns the new position; CanSeek is
// True for memory + non-append files and False for append-mode files; SetLength
// truncates (clamping a position past the new end) and extends zero-filled;
// ReadByte returns -1 at end-of-stream; WriteByte writes a single byte.
//
// EVERY line prints a labeled bool; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.IO;

class Program
{
    static void Main()
    {
        // ============ 1. MemoryStream position round-trip ============
        MemoryStream ms = new MemoryStream();
        byte[] data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        ms.Write(data, 0, 10);
        Console.WriteLine("w_pos=" + (ms.Position == 10));
        Console.WriteLine("w_len=" + (ms.Length == 10));
        ms.Position = 0;
        byte[] r = new byte[10];
        Console.WriteLine("r0=" + (ms.Read(r, 0, 10) == 10));
        Console.WriteLine("r0_pos=" + (ms.Position == 10));
        Console.WriteLine("r0_bytes=" + (r[0] == 0 && r[9] == 9));

        ms.Position = 3;
        byte[] small = new byte[4];
        Console.WriteLine("m3_read=" + (ms.Read(small, 0, 4) == 4));
        Console.WriteLine("m3_pos=" + (ms.Position == 7));
        Console.WriteLine("m3_bytes=" + (small[0] == 3 && small[3] == 6));

        // Position past the end: read returns 0, Position unchanged.
        ms.Position = 20;
        Console.WriteLine("past_read=" + (ms.Read(small, 0, 4) == 0));
        Console.WriteLine("past_pos=" + (ms.Position == 20));

        // Position == Length: clean EOF read returns 0.
        ms.Position = ms.Length;
        Console.WriteLine("eof_read=" + (ms.Read(small, 0, 4) == 0));

        // Write at a position past the end extends Length to pos+count.
        ms.Position = 12;
        ms.Write(new byte[] { 90, 91 }, 0, 2);
        Console.WriteLine("pe_pos=" + (ms.Position == 14));
        Console.WriteLine("pe_len=" + (ms.Length == 14));
        byte[] arr = ms.ToArray();
        Console.WriteLine("pe_arrlen=" + (arr.Length == 14));

        // Negative Position -> ArgumentOutOfRangeException.
        bool aoo = false;
        try { ms.Position = -1; } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("negpos=" + aoo);

        Console.WriteLine("ms_canread=" + ms.CanRead);
        Console.WriteLine("ms_canwrite=" + ms.CanWrite);
        Console.WriteLine("ms_cantimeout=" + ms.CanTimeout);

        // ============ 2. FileStream position round-trip ============
        string file = "sp_tmp.bin";
        File.Delete(file);
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite);
        byte[] wdata = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        fs.Write(wdata, 0, 8);
        fs.Flush();
        Console.WriteLine("f_pos8=" + (fs.Position == 8));
        Console.WriteLine("f_len8=" + (fs.Length == 8));

        fs.Position = 0;
        byte[] rb = new byte[8];
        Console.WriteLine("f_r8=" + (fs.Read(rb, 0, 8) == 8));
        Console.WriteLine("f_rb=" + (rb[0] == 10 && rb[7] == 80));

        fs.Position = 2;
        byte[] rb2 = new byte[3];
        Console.WriteLine("f_r3=" + (fs.Read(rb2, 0, 3) == 3));
        Console.WriteLine("f_r3b=" + (rb2[0] == 30 && rb2[2] == 50));
        Console.WriteLine("f_pos5=" + (fs.Position == 5));

        // Reposition and overwrite a byte.
        fs.Position = 1;
        fs.Write(new byte[] { 99 }, 0, 1);
        fs.Flush();
        fs.Position = 0;
        byte[] rb3 = new byte[2];
        fs.Read(rb3, 0, 2);
        Console.WriteLine("f_ovr=" + (rb3[0] == 10 && rb3[1] == 99));

        // EOF: read past the end returns 0, Position pinned to Length.
        fs.Position = fs.Length;
        Console.WriteLine("f_eof=" + (fs.Read(rb2, 0, 3) == 0));
        Console.WriteLine("f_eofpos=" + (fs.Position == fs.Length));

        Console.WriteLine("f_canread=" + fs.CanRead);
        Console.WriteLine("f_canwrite=" + fs.CanWrite);
        Console.WriteLine("f_cantimeout=" + fs.CanTimeout);

        fs.Close();
        File.Delete(file);

        // ============ 3. Seek / CanSeek / SetLength / ReadByte / WriteByte ===
        // (D-STP-2/3: the origin-aware seek surface + the single-byte surface)
        MemoryStream ms2 = new MemoryStream();
        byte[] sdata = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        ms2.Write(sdata, 0, 10);
        Console.WriteLine("s_canseek=" + ms2.CanSeek);
        Console.WriteLine("s_begin=" + (ms2.Seek(3, SeekOrigin.Begin) == 3));
        Console.WriteLine("s_cur=" + (ms2.Seek(2, SeekOrigin.Current) == 5));
        Console.WriteLine("s_end=" + (ms2.Seek(-2, SeekOrigin.End) == 8));
        Console.WriteLine("s_endpos=" + (ms2.Position == 8));

        // ReadByte / WriteByte round-trip over the memory stream.
        ms2.Position = 0;
        Console.WriteLine("rb0=" + (ms2.ReadByte() == 0));
        Console.WriteLine("rb_seq=" + (ms2.ReadByte() == 1 && ms2.ReadByte() == 2 && ms2.ReadByte() == 3));
        Console.WriteLine("rb_pos=" + (ms2.Position == 4));
        ms2.WriteByte(0x5A);
        ms2.Position = 4;
        Console.WriteLine("wb_byte=" + (ms2.ReadByte() == 0x5A));
        Console.WriteLine("wb_next=" + (ms2.ReadByte() == 5));
        ms2.Position = ms2.Length;
        Console.WriteLine("rb_eof=" + (ms2.ReadByte() == -1));

        // SetLength: extend (zero-filled gap) + truncate (position clamp).
        ms2.Position = 0;
        ms2.SetLength(12);
        Console.WriteLine("sl_len=" + (ms2.Length == 12));
        ms2.Position = 10;
        Console.WriteLine("sl_extz=" + (ms2.ReadByte() == 0));
        ms2.SetLength(4);
        Console.WriteLine("sl_len2=" + (ms2.Length == 4));
        Console.WriteLine("sl_pos=" + (ms2.Position == 4));
        ms2.Position = 1;
        ms2.WriteByte(9);
        ms2.Position = 0;
        Console.WriteLine("sl_ovr=" + (ms2.ReadByte() == 0 && ms2.ReadByte() == 9));

        // Seek error paths: invalid origin -> ArgumentException; negative
        // result -> IOException.
        bool seekArg = false;
        try { ms2.Seek(0, (SeekOrigin)7); } catch (ArgumentException) { seekArg = true; }
        Console.WriteLine("seek_arg=" + seekArg);
        bool seekIo = false;
        try { ms2.Seek(-5, SeekOrigin.Begin); } catch (IOException) { seekIo = true; }
        Console.WriteLine("seek_io=" + seekIo);
        bool slNeg = false;
        try { ms2.SetLength(-1); } catch (ArgumentOutOfRangeException) { slNeg = true; }
        Console.WriteLine("sl_neg=" + slNeg);

        // FileStream: origin-aware seek + single-byte surface + SetLength.
        string file2 = "sp_tmp2.bin";
        File.Delete(file2);
        FileStream fs2 = new FileStream(file2, FileMode.Create, FileAccess.ReadWrite);
        Console.WriteLine("fs_canseek=" + fs2.CanSeek);
        byte[] fw = new byte[] { 10, 20, 30, 40, 50, 60 };
        fs2.Write(fw, 0, 6);
        Console.WriteLine("fs_begin=" + (fs2.Seek(2, SeekOrigin.Begin) == 2));
        Console.WriteLine("fs_cur=" + (fs2.Seek(2, SeekOrigin.Current) == 4));
        Console.WriteLine("fs_end=" + (fs2.Seek(-2, SeekOrigin.End) == 4));
        Console.WriteLine("fs_rb=" + (fs2.ReadByte() == 50));
        Console.WriteLine("fs_rb2=" + (fs2.ReadByte() == 60));
        fs2.WriteByte(70);
        Console.WriteLine("fs_wb_len=" + (fs2.Length == 7));
        fs2.SetLength(9);
        Console.WriteLine("fs_sl9=" + (fs2.Length == 9));
        fs2.SetLength(3);
        Console.WriteLine("fs_sl3=" + (fs2.Length == 3));
        fs2.Position = 0;
        Console.WriteLine("fs_sl3b=" + (fs2.ReadByte() == 10 && fs2.ReadByte() == 20 && fs2.ReadByte() == 30));
        fs2.Position = fs2.Length;
        Console.WriteLine("fs_rbeof=" + (fs2.ReadByte() == -1));
        fs2.Close();
        File.Delete(file2);

        // Append-mode FileStream: CanSeek false, Seek throws NotSupportedException.
        FileStream fsApp = new FileStream(file2, FileMode.Append, FileAccess.Write);
        Console.WriteLine("fsapp_canseek=" + fsApp.CanSeek);
        bool appNse = false;
        try { fsApp.Seek(0, SeekOrigin.Begin); } catch (NotSupportedException) { appNse = true; }
        Console.WriteLine("fsapp_seek_nse=" + appNse);
        fsApp.Close();
        File.Delete(file2);
    }
}
