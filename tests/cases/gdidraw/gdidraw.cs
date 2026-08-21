//------------------------------------------------------------------------------
// gdidraw.cs -- end-to-end deep probe of the GDI DRAW surface (the stage-14
// drawing complement to gdifont, which measured fonts). All drawing runs on
// a MEMORY DC (CreateCompatibleDC + CreateCompatibleBitmap, filled white via
// PatBlt WHITENESS), so it is headless-safe and deterministic on the same
// machine for BOTH the net48 oracle and the transpiled runtime.
//
// Surface: DrawTextW ALIGNMENT (DT_LEFT / DT_CENTER / DT_RIGHT horizontal,
// DT_TOP / DT_VCENTER / DT_BOTTOM vertical -- verified by scanning the
// rendered bitmap with GetPixel for the text ink bounding box, so the probe
// observes WHERE GDI actually put the ink), DrawTextW DT_CALCRECT (measure
// into a RECT without drawing: single-line, DT_WORDBREAK multi-line, empty
// string), multi-font drawing (Arial / Consolas / Tahoma, regular vs bold,
// -16 vs -32 char height) with GetTextExtentPoint32W cross-font width
// comparisons, one real TextOutW draw, and SetTextColor / SetBkMode
// round-trip probes.
//
// PIXEL-SCAN INVARIANT: a memory DC renders text WITHOUT ClearType (no
// subpixel antialiasing on a bitmap DC), so every text pixel is either the
// white background (0x00FFFFFF) or an ink pixel (black text 0x000000, with
// grayscale-antialiased edges). Scanning a region for the bounding box of
// non-white pixels therefore locates the ink deterministically; a "no ink"
// scan reports BMinX=-1. Both the oracle and the transpiled runtime call
// the SAME GDI functions on the SAME machine, so the ink geometry is
// byte-identical.
//
// ALIGNMENT GEOMETRY (verified on the net48 oracle): for the 300x40 rect
// (20,40)-(320,80) the "WfGDI" ink spans y=43..54; DT_LEFT ink starts at
// x=20, DT_CENTER starts at x=147, DT_RIGHT starts at x=274 -- the center
// offset is exactly symmetric ((147-20) == (274-147) == 127). For the 80-tall
// rect (20,100)-(320,180) the ink y is 103 (DT_TOP), 134 (DT_VCENTER),
// 165 (DT_BOTTOM) -- again symmetric ((134-103) == (165-134) == 31). These
// raw numbers are the oracle; the assertions pin the symmetries.
//
// TRANSPILER SAFETY (the gdifont rules): struct int fields are copied into
// int locals BEFORE use in expressions; the per-font measure helper and the
// pixel-bbox helper write their results into STATIC fields (the winforms1
// stsfld shape) and the caller copies them to locals; the pixel helpers use
// plain for loops over ints.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Gdidraw
    {
        public const int FW_NORMAL = 400;
        public const int FW_BOLD = 700;
        public const int DEFAULT_CHARSET = 1;
        public const int OUT_DEFAULT_PRECIS = 0;
        public const int CLIP_DEFAULT_PRECIS = 0;
        public const int DEFAULT_QUALITY = 0;
        public const int DEFAULT_PITCH = 0;
        public const int FIXED_PITCH = 1;

        // DrawText format flags.
        public const int DT_TOP = 0x0;
        public const int DT_LEFT = 0x0;
        public const int DT_CENTER = 0x1;
        public const int DT_RIGHT = 0x2;
        public const int DT_VCENTER = 0x4;
        public const int DT_BOTTOM = 0x8;
        public const int DT_WORDBREAK = 0x10;
        public const int DT_SINGLELINE = 0x20;
        public const int DT_NOCLIP = 0x100;
        public const int DT_CALCRECT = 0x400;

        // GDI background modes + colors.
        public const int TRANSPARENT = 1;
        public const int BLACK_COLOR = 0x000000;
        public const int WHITE_COLOR = 0xFFFFFF;
        public const int WHITENESS = 0x00FF0062;

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
        public static int MTmHeight = 0;
        public static int MTmAve = 0;

        // BBox results (static fields -- the same stsfld shape): the bounding
        // box of the non-white ink in the scanned region, or -1 when no ink.
        public static int BMinX = 0;
        public static int BMaxX = 0;
        public static int BMinY = 0;
        public static int BMaxY = 0;
        public static int BCount = 0;

        [DllImport("gdi32", EntryPoint = "CreateCompatibleDC")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32", EntryPoint = "CreateCompatibleBitmap")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32", EntryPoint = "DeleteDC")]
        public static extern int DeleteDC(IntPtr hdc);
        [DllImport("gdi32", EntryPoint = "SelectObject")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32", EntryPoint = "DeleteObject")]
        public static extern int DeleteObject(IntPtr obj);
        [DllImport("gdi32", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontW(int h, int w, int e, int o, int wt,
            int it, int un, int so, int cs, int op, int cp, int q, int pf, string face);
        [DllImport("gdi32", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode)]
        public static extern int GetTextExtentPoint32W(IntPtr hdc, string text, int c, out SIZE sz);
        [DllImport("gdi32", EntryPoint = "GetTextMetricsW", CharSet = CharSet.Unicode)]
        public static extern int GetTextMetricsW(IntPtr hdc, out TEXTMETRIC tm);
        [DllImport("gdi32", EntryPoint = "TextOutW", CharSet = CharSet.Unicode)]
        public static extern int TextOutW(IntPtr hdc, int x, int y, string text, int c);
        [DllImport("gdi32", EntryPoint = "SetTextColor")]
        public static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("gdi32", EntryPoint = "SetBkMode")]
        public static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32", EntryPoint = "PatBlt")]
        public static extern int PatBlt(IntPtr hdc, int x, int y, int w, int h, int rop);
        [DllImport("gdi32", EntryPoint = "GetPixel")]
        public static extern int GetPixel(IntPtr hdc, int x, int y);
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
            TEXTMETRIC tm = new TEXTMETRIC();
            if (GetTextMetricsW(hdc, out tm) != 0)
            {
                MTmHeight = tm.tmHeight;
                MTmAve = tm.tmAveCharWidth;
            }
            IntPtr back = SelectObject(hdc, prev);
            if (back != IntPtr.Zero) MSteps = MSteps | 4;
            if (DeleteObject(font) != 0) MSteps = MSteps | 8;
        }

        // Scan the region (x0,y0)-(x1,y1) for the bounding box of non-white
        // ink. Results go to the B* static fields; BCount counts ink pixels.
        public static void BBox(IntPtr dc, int x0, int y0, int x1, int y1)
        {
            int minx = -1;
            int maxx = -1;
            int miny = -1;
            int maxy = -1;
            int cnt = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (GetPixel(dc, x, y) != WHITE_COLOR)
                    {
                        cnt = cnt + 1;
                        if (minx < 0 || x < minx) minx = x;
                        if (x > maxx) maxx = x;
                        if (miny < 0 || y < miny) miny = y;
                        if (y > maxy) maxy = y;
                    }
                }
            }
            BMinX = minx;
            BMaxX = maxx;
            BMinY = miny;
            BMaxY = maxy;
            BCount = cnt;
        }

        public static void Clear(IntPtr dc)
        {
            PatBlt(dc, 0, 0, 400, 200, WHITENESS);
        }

        public static void Main()
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            Console.WriteLine("memdc_created=" + (dc != IntPtr.Zero));
            IntPtr bmp = CreateCompatibleBitmap(dc, 400, 200);
            Console.WriteLine("bitmap_created=" + (bmp != IntPtr.Zero));
            IntPtr oldBmp = SelectObject(dc, bmp);
            Console.WriteLine("bitmap_selected=" + (oldBmp != IntPtr.Zero));

            // White background; black text; transparent background mode.
            Clear(dc);
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, BLACK_COLOR);

            // ===== 1. Multi-font / multi-size extent + metrics =====
            IntPtr fArial = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fArial, "WfGDI");
            int aCx = MCx; int aCy = MCy; int aH = MTmHeight; int aStep = MSteps;
            Console.WriteLine("arial_ok=" + (aStep == 15));
            Console.WriteLine("arial_cx=" + aCx);
            Console.WriteLine("arial_cy=" + aCy);
            Console.WriteLine("arial_h=" + aH);

            IntPtr fConsolas = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, FIXED_PITCH, "Consolas");
            MeasureFont(dc, fConsolas, "WfGDI");
            int cCx = MCx; int cCy = MCy; int cStep = MSteps;
            Console.WriteLine("consolas_ok=" + (cStep == 15));
            Console.WriteLine("consolas_cx=" + cCx);
            Console.WriteLine("consolas_cy=" + cCy);

            IntPtr fTahoma = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Tahoma");
            MeasureFont(dc, fTahoma, "WfGDI");
            int tCx = MCx; int tCy = MCy;
            Console.WriteLine("tahoma_cx=" + tCx);
            Console.WriteLine("tahoma_cy=" + tCy);

            IntPtr fBig = CreateFontW(-32, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fBig, "WfGDI");
            int bCx = MCx; int bCy = MCy;
            Console.WriteLine("big_ok=" + (MSteps == 15));
            Console.WriteLine("big_cx=" + bCx);
            Console.WriteLine("big_cy=" + bCy);
            Console.WriteLine("big_gt_small=" + (bCy > aCy));
            Console.WriteLine("big_gt_cx=" + (bCx > aCx));

            IntPtr fBold = CreateFontW(-16, 0, 0, 0, FW_BOLD, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            MeasureFont(dc, fBold, "WfGDI");
            int boCx = MCx;
            Console.WriteLine("bold_cx=" + boCx);
            Console.WriteLine("bold_gt_reg=" + (boCx > aCx));

            // ===== 2. DT_CALCRECT measurement (no drawing) =====
            IntPtr fA2 = CreateFontW(-16, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            IntPtr prev2 = SelectObject(dc, fA2);
            RECT cr = new RECT();
            cr.left = 10; cr.top = 10; cr.right = 300; cr.bottom = 60;
            int crRet = DrawTextW(dc, "WfGDI", 5, ref cr, DT_CALCRECT | DT_SINGLELINE | DT_NOCLIP);
            int crw = cr.right - cr.left;
            int crh = cr.bottom - cr.top;
            Console.WriteLine("calcrect_ret=" + crRet);
            Console.WriteLine("calcrect_w=" + crw);
            Console.WriteLine("calcrect_h=" + crh);
            Console.WriteLine("calcrect_w_eq_extent=" + (crw == aCx));
            Console.WriteLine("calcrect_h_eq_ret=" + (crh == crRet));
            Console.WriteLine("calcrect_left_kept=" + (cr.left == 10));
            Console.WriteLine("calcrect_top_kept=" + (cr.top == 10));

            // Wordbreak multi-line calcrect: narrow rect forces wrap.
            RECT wc = new RECT();
            wc.left = 10; wc.top = 10; wc.right = 60; wc.bottom = 200;
            int wcRet = DrawTextW(dc, "WfGDI WfGDI WfGDI WfGDI", 22, ref wc,
                DT_CALCRECT | DT_WORDBREAK | DT_NOCLIP);
            int wcw = wc.right - wc.left;
            int wch = wc.bottom - wc.top;
            Console.WriteLine("wordbreak_ret=" + wcRet);
            Console.WriteLine("wordbreak_w=" + wcw);
            Console.WriteLine("wordbreak_h=" + wch);
            Console.WriteLine("wordbreak_gt_single=" + (wch > crh));

            // Empty string: returns the line height, rect unchanged.
            RECT ec = new RECT();
            ec.left = 10; ec.top = 10; ec.right = 200; ec.bottom = 60;
            int ecRet = DrawTextW(dc, "", 0, ref ec, DT_CALCRECT | DT_SINGLELINE | DT_NOCLIP);
            Console.WriteLine("empty_ret=" + ecRet);
            Console.WriteLine("empty_w=" + (ec.right - ec.left));
            Console.WriteLine("empty_h=" + (ec.bottom - ec.top));

            // ===== 3. Horizontal alignment via ink bbox scan =====
            // Draw "WfGDI" in a 300-wide rect; the leftmost ink x differs by
            // alignment. DT_LEFT -> ink at rect.left; DT_RIGHT -> text right
            // edge at rect.right; DT_CENTER -> midway.
            Clear(dc);
            RECT al = new RECT();
            al.left = 20; al.top = 40; al.right = 320; al.bottom = 80;
            int alRet = DrawTextW(dc, "WfGDI", 5, ref al, DT_SINGLELINE | DT_LEFT | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkL = BMinX; int inkLMax = BMaxX; int inkLY = BMinY; int inkLYmax = BMaxY;
            Console.WriteLine("align_left_ret=" + alRet);
            Console.WriteLine("align_left_ink=" + inkL);
            Console.WriteLine("align_left_at_edge=" + (inkL == 20));
            Console.WriteLine("align_left_ymin=" + inkLY);
            Console.WriteLine("align_left_ymax=" + inkLYmax);

            Clear(dc);
            RECT ar = new RECT();
            ar.left = 20; ar.top = 40; ar.right = 320; ar.bottom = 80;
            int arRet = DrawTextW(dc, "WfGDI", 5, ref ar, DT_SINGLELINE | DT_RIGHT | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkR = BMinX; int inkRMax = BMaxX;
            Console.WriteLine("align_right_ret=" + arRet);
            Console.WriteLine("align_right_ink=" + inkR);
            Console.WriteLine("align_right_inkmax=" + inkRMax);
            Console.WriteLine("align_right_at_right=" + (inkRMax == 318));

            Clear(dc);
            RECT ac = new RECT();
            ac.left = 20; ac.top = 40; ac.right = 320; ac.bottom = 80;
            int acRet = DrawTextW(dc, "WfGDI", 5, ref ac, DT_SINGLELINE | DT_CENTER | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkC = BMinX; int inkCMax = BMaxX;
            Console.WriteLine("align_center_ret=" + acRet);
            Console.WriteLine("align_center_ink=" + inkC);
            Console.WriteLine("align_center_inkmax=" + inkCMax);
            // The center offset is EXACTLY the midpoint of the two edges.
            Console.WriteLine("align_center_sym=" + ((inkC - inkL) == (inkR - inkC)));
            Console.WriteLine("align_order=" + (inkL < inkC && inkC < inkR));

            // ===== 4. Vertical alignment via ink bbox scan =====
            // DT_TOP -> ink at rect.top; DT_BOTTOM -> text bottom at
            // rect.bottom; DT_VCENTER -> vertically centered.
            Clear(dc);
            RECT vt = new RECT();
            vt.left = 20; vt.top = 100; vt.right = 320; vt.bottom = 180;
            DrawTextW(dc, "WfGDI", 5, ref vt, DT_SINGLELINE | DT_LEFT | DT_TOP | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkTop = BMinY; int inkTopMax = BMaxY;
            Console.WriteLine("vert_top_ink=" + inkTop);
            Console.WriteLine("vert_top_inkmax=" + inkTopMax);

            Clear(dc);
            RECT vb = new RECT();
            vb.left = 20; vb.top = 100; vb.right = 320; vb.bottom = 180;
            DrawTextW(dc, "WfGDI", 5, ref vb, DT_SINGLELINE | DT_LEFT | DT_BOTTOM | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkBot = BMinY; int inkBotMax = BMaxY;
            Console.WriteLine("vert_bot_ink=" + inkBot);
            Console.WriteLine("vert_bot_inkmax=" + inkBotMax);
            Console.WriteLine("vert_bot_at_bottom=" + (inkBotMax == 176));

            Clear(dc);
            RECT vc = new RECT();
            vc.left = 20; vc.top = 100; vc.right = 320; vc.bottom = 180;
            DrawTextW(dc, "WfGDI", 5, ref vc, DT_SINGLELINE | DT_LEFT | DT_VCENTER | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            int inkVc = BMinY; int inkVcMax = BMaxY;
            Console.WriteLine("vert_center_ink=" + inkVc);
            Console.WriteLine("vert_center_inkmax=" + inkVcMax);
            Console.WriteLine("vert_center_sym=" + ((inkVc - inkTop) == (inkBot - inkVc)));
            Console.WriteLine("vert_order=" + (inkTop < inkVc && inkVc < inkBot));

            // ===== 5. Real draw + TextOutW (ink presence) =====
            Clear(dc);
            RECT rd = new RECT();
            rd.left = 20; rd.top = 40; rd.right = 320; rd.bottom = 80;
            int dRet = DrawTextW(dc, "WfGDI", 5, ref rd, DT_SINGLELINE | DT_LEFT | DT_NOCLIP);
            BBox(dc, 0, 0, 399, 199);
            Console.WriteLine("draw_ret=" + dRet);
            Console.WriteLine("draw_ink=" + (BMinX != -1));
            Console.WriteLine("draw_cnt=" + BCount);

            Clear(dc);
            int toRet = TextOutW(dc, 20, 40, "WfGDI", 5);
            BBox(dc, 0, 0, 399, 199);
            Console.WriteLine("textout_ret=" + toRet);
            Console.WriteLine("textout_ink=" + (BMinX != -1));
            Console.WriteLine("textout_ink_x=" + BMinX);

            // ===== 6. SetTextColor / SetBkMode round-trip =====
            int prevColor = SetTextColor(dc, BLACK_COLOR);
            int prevBk = SetBkMode(dc, TRANSPARENT);
            Console.WriteLine("setcolor_ok=" + (prevColor != -1));
            Console.WriteLine("setbkmode_ok=" + (prevBk != -1));

            SelectObject(dc, prev2);
            Console.WriteLine("font_deleted=" + (DeleteObject(fA2) != 0));
            IntPtr oldRestore = SelectObject(dc, oldBmp);
            Console.WriteLine("bmp_restore=" + (oldRestore != IntPtr.Zero));
            Console.WriteLine("bmp_delete=" + (DeleteObject(bmp) != 0));
            Console.WriteLine("dcd_delete=" + (DeleteDC(dc) != 0));
        }
    }
}
