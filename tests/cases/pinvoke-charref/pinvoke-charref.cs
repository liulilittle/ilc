//------------------------------------------------------------------------------
// pinvoke-charref.cs -- `ref char` / `out char` DllImport parameters
// (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED on net48 v4.0.30319 (host CP_ACP=936 zh-CN) with charrefprobe.dll
// (this case's own SHARED fixture):
//   * CharSet.Ansi     -> the native cell is ONE byte and the unit is converted
//     through the SYSTEM ANSI code page in BOTH directions:
//       in U+4E2D  -> callee reads 0xD6 (CP936 lead byte) = 214
//       callee writes 0x5A -> managed 90; 0xD6 -> '?' (63); 0xFF -> 63733
//       U+FFFF in  -> 0x3F;  U+0000 in -> 0
//   * CharSet.Unicode / Auto -> a 2-byte UTF-16 cell, NO conversion:
//       in U+4E2D -> 20013; callee writes 0x6587 -> 25991; U+FFFF -> 65535.
//   * a copy-back ALWAYS happens, so an Ansi `ref` whose callee does not write
//     still round-trips through CP_ACP (U+4E2D -> '?'); only Unicode (and
//     ASCII-representable Ansi units) keep their value.
// => `ref char` / `out char` are CHAR-SET DEPENDENT. The old claim that they
// were CharSet-independent came from measuring Ansi only with ASCII 0x41,
// which is an identity in every single-byte code page.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeCharRef
    {
        // ---- CharSet.Ansi: 1-byte CP_ACP cell ----
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi)]
        private static extern int a_read8(ref char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_read8")]
        private static extern int a_read8_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi)]
        private static extern int a_ref8(ref char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_out8")]
        private static extern int a_out8_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_fill8_41")]
        private static extern void a_fill8_41_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_fill8_d6")]
        private static extern void a_fill8_d6_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_fill8_ff")]
        private static extern void a_fill8_ff_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "a_fill8_00")]
        private static extern void a_fill8_00_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Ansi)]
        private static extern int a_pair(ref char c, string s);

        // ---- CharSet.Unicode: 2-byte UTF-16 cell ----
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode)]
        private static extern int u_read16(ref char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode)]
        private static extern int u_ref16(ref char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "u_out16")]
        private static extern int u_out16_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "u_fill16_4e2d")]
        private static extern void u_fill16_4e2d_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "u_fill16_ffff")]
        private static extern void u_fill16_ffff_out(out char c);
        [DllImport("charrefprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "u_fill16_0000")]
        private static extern void u_fill16_0000_out(out char c);

        // ---- CharSet.Auto on Windows == Unicode (measured, not assumed) -----
        [DllImport("charrefprobe.dll", CharSet = CharSet.Auto, EntryPoint = "u_ref16")]
        private static extern int auto_ref16(ref char c);

        // ---- STATIC-link fixture (the transpiler's own extern "C" shape) ----
        [DllImport("charrefstat", CharSet = CharSet.Ansi)]
        private static extern int stat_a_ref(ref char c);
        [DllImport("charrefstat", CharSet = CharSet.Ansi, EntryPoint = "stat_a_fill")]
        private static extern void stat_a_fill_out(out char c);
        [DllImport("charrefstat", CharSet = CharSet.Unicode)]
        private static extern int stat_u_ref(ref char c);
        [DllImport("charrefstat", CharSet = CharSet.Unicode, EntryPoint = "stat_u_fill")]
        private static extern void stat_u_fill_out(out char c);

        public static void Main()
        {
            // === Ansi (1-byte CP_ACP cell) ==================================
            // non-ASCII: the decisive ANSI-conversion proof.
            char a = (char)0x4E2D;
            Record("ansi.ref.nonascii.seen", a_ref8(ref a));
            Record("ansi.ref.nonascii.after", (int)a);

            // ASCII: identity in every code page (the old false-negative case).
            char ascii = (char)0x0041;
            Record("ansi.ref.ascii.seen", a_ref8(ref ascii));
            Record("ansi.ref.ascii.after", (int)ascii);

            // out on a pre-set local: the callee sees the caller's CP_ACP image.
            char pre = (char)0x4E2D;
            Record("ansi.out.pre.seen", a_out8_out(out pre));
            Record("ansi.out.pre.after", (int)pre);

            // out on a pre-set ASCII local: net48's [Out] temp is NOT seeded,
            // so the callee must still see 0 (NOT 65).
            char preA = (char)0x0041;
            Record("ansi.out.presetA.seen", a_read8_out(out preA));
            Record("ansi.out.presetA.after", (int)preA);

            // out on a fresh local: method-entry zero.
            char fresh;
            Record("ansi.out.fresh.seen", a_out8_out(out fresh));
            Record("ansi.out.fresh.after", (int)fresh);

            // out write-only: ASCII / DBCS lead byte / invalid 0xFF / NUL.
            a_fill8_41_out(out char f41);
            Record("ansi.out.b41", (int)f41);
            a_fill8_d6_out(out char fd6);
            Record("ansi.out.bD6", (int)fd6);
            a_fill8_ff_out(out char fff);
            Record("ansi.out.bFF", (int)fff);
            a_fill8_00_out(out char f00);
            Record("ansi.out.b00", (int)f00);

            // boundary: NUL and U+FFFF on the ANSI side.
            char nul = (char)0x0000;
            Record("ansi.ref.nul.seen", a_read8(ref nul));
            Record("ansi.ref.nul.after", (int)nul);
            char max = (char)0xFFFF;
            Record("ansi.ref.max.seen", a_read8(ref max));
            Record("ansi.ref.max.after", (int)max);

            // ref + callee never writes: the copy-back STILL round-trips CP_ACP.
            char keepAscii = (char)0x0041;
            Record("ansi.nowrite.ascii.seen", a_read8(ref keepAscii));
            Record("ansi.nowrite.ascii.after", (int)keepAscii);
            char keepCjk = (char)0x4E2D;
            Record("ansi.nowrite.nonascii.seen", a_read8(ref keepCjk));
            Record("ansi.nowrite.nonascii.after", (int)keepCjk);

            // byref char + Ansi string in one call.
            char mix = (char)0x0041;
            Record("ansi.mix.seen", a_pair(ref mix, "A"));
            Record("ansi.mix.after", (int)mix);

            // === Unicode (2-byte UTF-16 cell, no conversion) =================
            char u = (char)0x4E2D;
            Record("uni.ref.nonascii.seen", u_ref16(ref u));
            Record("uni.ref.nonascii.after", (int)u);
            char ua = (char)0x0041;
            Record("uni.ref.ascii.seen", u_ref16(ref ua));
            Record("uni.ref.ascii.after", (int)ua);

            char upre = (char)0x4E2D;
            Record("uni.out.pre.seen", u_out16_out(out upre));
            Record("uni.out.pre.after", (int)upre);
            char ufresh;
            Record("uni.out.fresh.seen", u_out16_out(out ufresh));
            Record("uni.out.fresh.after", (int)ufresh);

            u_fill16_4e2d_out(out char u4e2d);
            Record("uni.out.fill", (int)u4e2d);
            u_fill16_ffff_out(out char uffff);
            Record("uni.out.ffff", (int)uffff);
            u_fill16_0000_out(out char uzero);
            Record("uni.out.nul", (int)uzero);

            char unul = (char)0x0000;
            Record("uni.ref.nul.seen", u_read16(ref unul));
            Record("uni.ref.nul.after", (int)unul);
            char umax = (char)0xFFFF;
            Record("uni.ref.max.seen", u_read16(ref umax));
            Record("uni.ref.max.after", (int)umax);

            // Unicode ref + callee never writes: the value survives verbatim.
            char ukeep = (char)0x4E2D;
            Record("uni.nowrite.seen", u_read16(ref ukeep));
            Record("uni.nowrite.after", (int)ukeep);

            // === CharSet.Auto == Unicode (measured) ==========================
            char autoC = (char)0x4E2D;
            Record("auto.ref.nonascii.seen", auto_ref16(ref autoC));
            Record("auto.ref.nonascii.after", (int)autoC);

            // === STATIC-link shape (extern "C" declaration, direct call) =====
            char sa = (char)0x0041;
            Record("stat.ansi.seen", stat_a_ref(ref sa));
            Record("stat.ansi.after", (int)sa);
            stat_a_fill_out(out char saf);
            Record("stat.ansi.fill", (int)saf);
            char su = (char)0x4E2D;
            Record("stat.uni.seen", stat_u_ref(ref su));
            Record("stat.uni.after", (int)su);
            stat_u_fill_out(out char suf);
            Record("stat.uni.fill", (int)suf);
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}