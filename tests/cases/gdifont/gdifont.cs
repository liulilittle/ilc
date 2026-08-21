//------------------------------------------------------------------------------
// gdifont.cs -- deep probe of the GDI font surface (stage-14 complement).
// All drawing runs on a MEMORY DC (CreateCompatibleDC + CreateCompatibleBitmap),
// so it is headless-safe and deterministic on the same machine for BOTH the
// net48 oracle and the transpiled runtime.
//
// Surface: CreateFontW VARIANTS (negative char height, positive cell height,
// bold, italic, underline, strikethrough, fixed-pitch Consolas, Tahoma) each
// through SelectObject -> measure -> restore -> DeleteObject; GetTextMetricsW
// (the 11 LONG fields, via a 16-int struct whose 11 leading ints match the
// native TEXTMETRICW layout bytes 0..43 -- the WCHAR/BYTE tail is modeled as
// 4 unread pad ints so the native 57-byte write never overruns the 64-byte
// buffer); GetTextExtentPoint32W (logical extent in the selected font, wide
// vs narrow / mono vs proportional / regular vs bold); DrawTextW DT_CALCRECT
// (measure into a RECT without drawing; the return is the text height), one
// real DrawTextW + one TextOutW draw; GetTextFaceW face-name readback.
//
// TRANSPILER SAFETY: struct int fields are copied into int locals / static
// fields BEFORE any use in an expression (the "value-type field value boxing"
// path is a documented transpiler gap -- see report); the per-font measure
// helper writes its results into static fields (the winforms1 stsfld shape,
// proven PASS) and the caller copies them to locals.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Gdifont
    {
        public const int FW_NORMAL = 400;
        public const int FW_BOLD = 700;
        public const int DEFAULT_CHARSET = 1;
        public const int OUT_DEFAULT_PRECIS = 0;
        public const int CLIP_DEFAULT_PRECIS = 0;
        public const int DEFAULT_QUALITY = 0;
        public const int DEFAULT_PITCH = 0;
        public const int FIXED_PITCH = 1;
        public const int MONO_FONT = 8;
        public const int DT_SINGLELINE = 0x20;
        public const int DT_NOCLIP = 0x100;
        public const int DT_CALCRECT = 0x400;

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct TEXTMETRIC
        {
            public int tmHeight;           // 0
            public int tmAscent;           // 4
            public int tmDescent;          // 8
            public int tmInternalLeading;  // 12
            public int tmExternalLeading;  // 16
            public int tmAveCharWidth;     // 20
            public int tmMaxCharWidth;     // 24
            public int tmWeight;           // 28
            public int tmOverhang;         // 32
            public int tmDigitizedAspectX; // 36
            public int tmDigitizedAspectY; // 40
            public int tmPad1;             // pad only (native WCHAR/BYTE tail)
            public int tmPad2;
            public int tmPad3;
            public int tmPad4;
        }

        // MeasureFont results (static fields -- the winforms1 stsfld shape).
        // MSteps: bitmask 1=select 2=extent 4=restore 8=delete (all == 15).
        public static int MSteps = 0;
        public static int MCx = 0;
        public static int MCy = 0;

        [DllImport("gdi32", EntryPoint = "CreateCompatibleDC")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32", EntryPoint = "CreateCompatibleBitmap")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32", EntryPoint = "DeleteDC")]
        public static extern int DeleteDC(IntPtr hdc);
        [DllImport("gdi32", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontW(int h, int w, int e, int o, int wt,
            int it, int un, int so, int cs, int op, int cp, int q, int pf, string face);
        [DllImport("gdi32", EntryPoint = "SelectObject")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32", EntryPoint = "DeleteObject")]
        public static extern int DeleteObject(IntPtr obj);
        [DllImport("gdi32", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode)]
        public static extern int GetTextExtentPoint32W(IntPtr hdc, string text, int c, out SIZE sz);
        [DllImport("gdi32", EntryPoint = "GetTextMetricsW", CharSet = CharSet.Unicode)]
        public static extern int GetTextMetricsW(IntPtr hdc, out TEXTMETRIC tm);
        [DllImport("gdi32", EntryPoint = "GetTextFaceW", CharSet = CharSet.Unicode)]
        public static extern int GetTextFaceW(IntPtr hdc, int cch, IntPtr buf);
        [DllImport("gdi32", EntryPoint = "TextOutW", CharSet = CharSet.Unicode)]
        public static extern int TextOutW(IntPtr hdc, int x, int y, string text, int c);
        [DllImport("user32", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
        public static extern int DrawTextW(IntPtr hdc, string text, int c, ref RECT rc, int format);

        public static void MeasureFont(IntPtr hdc, IntPtr font, string text)
        {
            MSteps = 0;
            IntPtr prev = SelectObject(hdc, font);
            if (prev != IntPtr.Zero) MSteps = MSteps | 1;
            SIZE e = new SIZE();
            if (GetTextExtentPoint32W(hdc, text, text.Length, out e) != 0) MSteps = MSteps | 2;
            MCx = e.cx;
            MCy = e.cy;
            IntPtr back = SelectObject(hdc, prev);
            if (back != IntPtr.Zero) MSteps = MSteps | 4;
            if (DeleteObject(font) != 0) MSteps = MSteps | 8;
        }
        public static void Main()
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            Console.WriteLine("memdc_created=" + (dc != IntPtr.Zero));
            IntPtr bmp = CreateCompatibleBitmap(dc, 400, 200);
            Console.WriteLine("bitmap_created=" + (bmp != IntPtr.Zero));
            IntPtr oldBmp = SelectObject(dc, bmp);
            Console.WriteLine("bitmap_selected=" + (oldBmp != IntPtr.Zero));

            IntPtr fontA = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            Console.WriteLine("arial_created=" + (fontA != IntPtr.Zero));
            IntPtr prevA = SelectObject(dc, fontA);
            Console.WriteLine("arial_select=" + (prevA != IntPtr.Zero));
            TEXTMETRIC tm = new TEXTMETRIC();
            int gmRet = GetTextMetricsW(dc, out tm);
            Console.WriteLine("metrics_ok=" + (gmRet != 0));
            int vh = tm.tmHeight;
            int va = tm.tmAscent;
            int vd = tm.tmDescent;
            int vi = tm.tmInternalLeading;
            int ve = tm.tmExternalLeading;
            int vave = tm.tmAveCharWidth;
            int vmax = tm.tmMaxCharWidth;
            int vw = tm.tmWeight;
            int vo = tm.tmOverhang;
            int vdx = tm.tmDigitizedAspectX;
            int vdy = tm.tmDigitizedAspectY;
            Console.WriteLine("tm_height=" + vh);
            Console.WriteLine("tm_ascent=" + va);
            Console.WriteLine("tm_descent=" + vd);
            Console.WriteLine("tm_intlead=" + vi);
            Console.WriteLine("tm_extlead=" + ve);
            Console.WriteLine("tm_ave=" + vave);
            Console.WriteLine("tm_max=" + vmax);
            Console.WriteLine("tm_weight=" + vw);
            Console.WriteLine("tm_overhang=" + vo);
            Console.WriteLine("tm_digx=" + vdx);
            Console.WriteLine("tm_digy=" + vdy);
            IntPtr backA = SelectObject(dc, prevA);
            Console.WriteLine("arial_restore=" + (backA != IntPtr.Zero));
            Console.WriteLine("arial_delete=" + (DeleteObject(fontA) != 0));

            IntPtr fBig = CreateFontW(-32, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fBig, "WfGDI");
            int bigCx = MCx;
            int bigCy = MCy;
            int bigStep = MSteps;
            Console.WriteLine("big_ok=" + (bigStep == 15));
            Console.WriteLine("big_cx=" + bigCx);
            Console.WriteLine("big_cy=" + bigCy);
            Console.WriteLine("big_gt=" + (bigCy > vh));

            IntPtr fCell = CreateFontW(16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fCell, "WfGDI");
            int cellCx = MCx;
            int cellStep = MSteps;
            Console.WriteLine("cell_ok=" + (cellStep == 15));

            IntPtr fBold = CreateFontW(-16, 0, 0, 0, FW_BOLD, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fBold, "WfGDI");
            int boldCx = MCx;
            int boldStep = MSteps;
            Console.WriteLine("bold_ok=" + (boldStep == 15));

            IntPtr fItalic = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 1, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fItalic, "WfGDI");
            Console.WriteLine("italic_ok=" + (MSteps == 15));

            IntPtr fUnder = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 1, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fUnder, "WfGDI");
            Console.WriteLine("underline_ok=" + (MSteps == 15));

            IntPtr fStrike = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 1,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fStrike, "WfGDI");
            Console.WriteLine("strike_ok=" + (MSteps == 15));

            IntPtr fMono = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, FIXED_PITCH | MONO_FONT, "Consolas");
            MeasureFont(dc, fMono, "WfGDI");
            int monoCy = MCy;
            Console.WriteLine("mono_ok=" + (MSteps == 15));
            Console.WriteLine("mono_cy=" + monoCy);

            IntPtr fTahoma = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Tahoma");
            MeasureFont(dc, fTahoma, "WfGDI");
            int tahomaCy = MCy;
            Console.WriteLine("tahoma_ok=" + (MSteps == 15));
            Console.WriteLine("tahoma_cy=" + tahomaCy);

            Console.WriteLine("bold_wider=" + (boldCx != cellCx));
            IntPtr fBold2 = CreateFontW(-16, 0, 0, 0, FW_BOLD, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            IntPtr prevB = SelectObject(dc, fBold2);
            TEXTMETRIC tmb = new TEXTMETRIC();
            GetTextMetricsW(dc, out tmb);
            int wgt = tmb.tmWeight;
            Console.WriteLine("bold_weight=" + (wgt == FW_BOLD));
            SelectObject(dc, prevB);
            DeleteObject(fBold2);

            IntPtr fMono2 = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, FIXED_PITCH | MONO_FONT, "Consolas");
            IntPtr prevM = SelectObject(dc, fMono2);
            TEXTMETRIC tmm = new TEXTMETRIC();
            GetTextMetricsW(dc, out tmm);
            int aveW = tmm.tmAveCharWidth;
            int maxW = tmm.tmMaxCharWidth;
            Console.WriteLine("mono_ave_eq_max=" + (aveW == maxW));
            SelectObject(dc, prevM);
            DeleteObject(fMono2);

            IntPtr fA2 = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fA2, "iii");
            int nCx = MCx;
            IntPtr fA3 = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fA3, "MMM");
            int wCx = MCx;
            IntPtr fA4 = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fA4, "WfGDI");
            int mixedCx = MCx;
            Console.WriteLine("wide_gt_narrow=" + (wCx > nCx));
            Console.WriteLine("mixed_cx=" + mixedCx);

            IntPtr fDraw = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            IntPtr prevD = SelectObject(dc, fDraw);
            RECT cr = new RECT();
            cr.left = 0; cr.top = 0; cr.right = 500; cr.bottom = 100;
            int crRet = DrawTextW(dc, "WfGDI", 5, ref cr, DT_CALCRECT | DT_SINGLELINE | DT_NOCLIP);
            int crw = cr.right - cr.left;
            int crh = cr.bottom - cr.top;
            Console.WriteLine("calcrect_ok=" + (crRet != 0));
            Console.WriteLine("calcrect_w=" + crw);
            Console.WriteLine("calcrect_h=" + crh);
            Console.WriteLine("calcrect_h_eq_ret=" + (crh == crRet));

            RECT dr = new RECT();
            dr.left = 10; dr.top = 10; dr.right = 300; dr.bottom = 60;
            int dRet = DrawTextW(dc, "WfGDI", 5, ref dr, DT_SINGLELINE | DT_NOCLIP);
            Console.WriteLine("draw_ok=" + (dRet != 0));
            Console.WriteLine("draw_h=" + dRet);
            int toRet = TextOutW(dc, 10, 80, "WfGDI", 5);
            Console.WriteLine("textout_ok=" + (toRet != 0));

            IntPtr fb = Marshal.AllocHGlobal(128);
            int faceLen = GetTextFaceW(dc, 128, fb);
            string faceName = Marshal.PtrToStringUni(fb);
            Console.WriteLine("face_len=" + faceLen);
            Console.WriteLine("face_arial=" + (faceName == "Arial"));
            Marshal.FreeHGlobal(fb);
            SelectObject(dc, prevD);
            DeleteObject(fDraw);

            IntPtr oldRestore = SelectObject(dc, oldBmp);
            Console.WriteLine("bmp_restore=" + (oldRestore != IntPtr.Zero));
            Console.WriteLine("bmp_delete=" + (DeleteObject(bmp) != 0));
            Console.WriteLine("dcd_delete=" + (DeleteDC(dc) != 0));
        }
    }
}
