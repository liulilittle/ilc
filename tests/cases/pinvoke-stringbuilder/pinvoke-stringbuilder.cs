//------------------------------------------------------------------------------
// pinvoke-stringbuilder.cs -- System.Text.StringBuilder as a P/Invoke
// parameter (net48 marshalling semantics).
//
// net48 hands the native callee the STRINGBUILDER'S OWN character buffer, so
// the callee's writes are visible in the managed object after the call and
// StringBuilder.Length is recomputed from the buffer (no copy-back temp):
//
//   * the native buffer is Capacity UTF-16 units (NOT Length) -- a 20-unit
//     write into "hello" (Capacity 32) lands and reads back whole;
//   * the buffer is initialised to the current content followed by a NUL at
//     index Length (the marshaller writes that terminator);
//   * after the call Length == strnlen(buffer, Capacity): a NUL-terminated
//     shorter write SHRINKS Length, a longer write (up to Capacity) GROWS it,
//     and a buffer with no NUL inside Capacity reads back as Capacity units;
//   * residual content between the new and the old length is observable
//     ("ab" written over "ZZZZZZ" with Capacity 8 -> "abZZZZ", Length 6).
//
// The fixture below writes through the raw pointer, so a wrong buffer size /
// no terminator / stale Length shows up as a record diff against the oracle.
// CharSet.Ansi is a self-identifying LOUD rejection (net48 marshals via the
// system ANSI code page; this model has no CP_ACP conversion -- see
// docs/audit/b5-gaps.md section 29), so only Unicode/Auto imports appear here.
//------------------------------------------------------------------------------
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class StringBuilderPInvokeProbe
{
    // Writes 'text' followed by a NUL unit (the GetWindowText shape).
    [DllImport("sbprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int sb_write_terminated(StringBuilder sb, string text);

    // Writes exactly text.Length units, NO terminator.
    [DllImport("sbprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int sb_write_raw(StringBuilder sb, string text);

    // The GetWindowText shape: buffer supplied by the caller, callee writes a
    // NUL-terminated string and returns its length.
    [DllImport("sbprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int sb_get_window_text(StringBuilder sb);

    // Reads one unit of the native view (pins the content/terminator the
    // marshaller handed over).
    [DllImport("sbprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int sb_peek(StringBuilder sb, int index);

    // GC contract: the delegate re-enters managed code (GC.Collect) while the
    // native side already holds the builder's chunk pointer; the callee writes
    // through it AFTER the collection. A relocated chunk would make the write
    // land elsewhere and the readback differ (see sbprobe.cpp / section 14).
    private delegate void GcPoke();

    [DllImport("sbprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int sb_gc_poke(StringBuilder sb, GcPoke poke);






    public static void Main()
    {
        // --- Unicode: native writes a SHORTER NUL-terminated text ---------
        var u1 = new StringBuilder("hello", 32);
        Record("u1.ret", sb_write_terminated(u1, "abc"));
        Record("u1.len", u1.Length);
        Record("u1.text", u1.ToString());

        // --- Unicode: native writes to zero (NUL at index 0) --------------
        var u2 = new StringBuilder("hello", 32);
        Record("u2.ret", sb_write_terminated(u2, ""));
        Record("u2.len", u2.Length);
        Record("u2.text", u2.ToString());

        // --- Unicode: LONGER than the old Length, still below Capacity ----
        var u3 = new StringBuilder("hello", 32);
        Record("u3.ret", sb_write_terminated(u3, "0123456789ABCDEFGHIJ"));
        Record("u3.len", u3.Length);
        Record("u3.text", u3.ToString());

        // --- Unicode: empty builder, native writes + NUL ------------------
        var u4 = new StringBuilder(16);
        Record("u4.ret", sb_write_terminated(u4, "ABCD"));
        Record("u4.len", u4.Length);
        Record("u4.text", u4.ToString());

        // --- Unicode: fill EXACTLY Capacity, no NUL (Capacity boundary) ---
        // Length == Capacity before the call, so the marshaller's own
        // terminator sits at index Capacity (outside the scanned window).
        var u5 = new StringBuilder("01234567", 8);
        Record("u5.ret", sb_write_raw(u5, "abcdefgh"));
        Record("u5.len", u5.Length);
        Record("u5.text", u5.ToString());

        // --- Unicode: raw write WITHOUT NUL over residual content ---------
        // "ab" over "ZZZZZZ" (Capacity 8): the scan reaches the original
        // terminator at index 6 -> "abZZZZ" / Length 6.
        var u6 = new StringBuilder("ZZZZZZ", 8);
        Record("u6.ret", sb_write_raw(u6, "ab"));
        Record("u6.len", u6.Length);
        Record("u6.text", u6.ToString());

        // --- Unicode: GetWindowText shape (caller-sized buffer) -----------
        var u7 = new StringBuilder(260);
        Record("u7.ret", sb_get_window_text(u7));
        Record("u7.len", u7.Length);
        Record("u7.text", u7.ToString());

        // --- Unicode: the native view starts with the managed content -----
        var u8 = new StringBuilder("hello", 32);
        Record("u8.first", sb_peek(u8, 0));
        Record("u8.last", sb_peek(u8, 4));
        Record("u8.nul", sb_peek(u8, 5));
        Record("u8.len", u8.Length);

        // --- Unicode: the chunk survives a compaction DURING the call ------
        var g1 = new StringBuilder("before", 32);
        Record("g1.ret", sb_gc_poke(g1, CollectNow));
        Record("g1.len", g1.Length);
        Record("g1.text", g1.ToString());

    }

    private static void CollectNow()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    private static void Record(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Record(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }
}
