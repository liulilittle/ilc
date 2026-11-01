//------------------------------------------------------------------------------
// pinvoke-structreturn.cs -- BY-VALUE struct and enum DllImport RETURNS
// (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED ABI shapes on Windows x64 (net48 oracle, this case's own shared
// fixture structretprobe.dll). The oracle prints every field; the transpiled
// runtime must print the same bytes, so a wrong carrier (int32_t) or a wrong
// aggregate-return shape shows up as a FIELD-VALUE diff, not just a C++
// build error. The observed values are recorded in the seat NOTES and are
// the reason the bridge names the struct type instead of an integer:
//
//   AbiPair   { int32, int32 }          8-byte aggregate  [abi.pair.*]
//   AbiWide   { int64 }                 8-byte aggregate  [abi.wide]
//   AbiTriple { int32, int32, int32 }  12-byte aggregate  [abi.triple]
//   AbiFour   { int32 }                 4-byte aggregate  [abi.four]
//
// The nine WinForms/Drawing declarations the audit classified as
// "struct-return" (CombineRgn x2, GetRgnBox x2, SelectClipRgn x2,
// GetAwarenessFromDpiAwarenessContext, GetCaretPos, GetPhysicalCursorPos) are
// actually nested-ENUM returns (ret-enum: in the audit TSV), i.e. a 4-byte
// value whose native ABI is its int32 backing. This case reproduces their
// exact declaration SHAPES -- nested enum return + 3/2/1 HandleRef params,
// out RECT (same-assembly struct), out System.Drawing.Point, and a by-value
// int mode (a by-value ENUM param is a SEPARATE residual: the param list
// still emits the 16-byte generated enum struct -- see the seat NOTES).
// The enum return is deliberately kept as int32_t (measured): naming the
// generated enum struct (16 bytes, object header) makes the caller pass a
// hidden sret pointer while the native callee returns the int32 in EAX.
//------------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Cases
{
    public struct AbiPair
    {
        public int X;
        public int Y;
    }

    public struct AbiWide
    {
        public long V;
    }

    public struct AbiTriple
    {
        public int A;
        public int B;
        public int C;
    }

    public struct AbiFour
    {
        public int V;
    }

    public struct AbiRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class PinvokeStructReturn
    {
        public static class Native
        {
            public enum BOOL
            {
                FALSE = 0,
                TRUE = 1
            }

            public enum RegionFlags
            {
                ERROR = 0,
                NULLREGION = 1,
                SIMPLEREGION = 2,
                COMPLEXREGION = 3
            }

            public enum RegionCombineMode
            {
                AND = 1,
                OR = 2,
                DIFF = 3,
                COPY = 5
            }

            public enum DPI_AWARENESS
            {
                INVALID = -1,
                UNAWARE = 0,
                SYSTEM_AWARE = 1,
                PER_MONITOR_AWARE = 2
            }

            // (a) two int32 fields.
            [DllImport("structretprobe")]
            public static extern AbiPair zero_pair();
            [DllImport("structretprobe")]
            public static extern AbiPair make_pair(int seed);
            [DllImport("structretprobe")]
            public static extern AbiPair add_pair(AbiPair value, int dx, int dy);

            // (b) one int64 field.
            [DllImport("structretprobe")]
            public static extern AbiWide make_wide(long seed);

            // (c) three int32 fields.
            [DllImport("structretprobe")]
            public static extern AbiTriple make_triple(int seed);

            // (d) 4-byte aggregate.
            [DllImport("structretprobe")]
            public static extern AbiFour make_four(int seed);

            // Audited shapes: enum return + out registered struct.
            [DllImport("structretprobe")]
            public static extern BOOL GetCaretPos(out Point pt);
            [DllImport("structretprobe")]
            public static extern BOOL GetPhysicalCursorPos(out Point pt);
            [DllImport("structretprobe")]
            public static extern int sum_point(Point pt);

            // Audited shape: enum return + IntPtr param.
            [DllImport("structretprobe")]
            public static extern DPI_AWARENESS GetAwarenessFromDpiAwarenessContext(IntPtr context);

            // Audited shape: enum return + 3 HandleRef params + enum param.
            [DllImport("structretprobe", EntryPoint = "IntCombineRgn")]
            public static extern RegionFlags Combine(HandleRef a, HandleRef b, HandleRef c, int mode);

            // Audited shape: enum return + HandleRef + out same-assembly struct.
            [DllImport("structretprobe", EntryPoint = "IntGetRgnBox")]
            public static extern RegionFlags GetRgnBox(HandleRef rgn, out AbiRect box);

            // Audited shape: enum return + 2 HandleRef params.
            [DllImport("structretprobe", EntryPoint = "IntSelectClipRgn")]
            public static extern RegionFlags SelectClipRgn(HandleRef hdc, HandleRef rgn);
        }

        public static void Main()
        {
            // (a) two int32 fields: local round-trip, direct projection off
            // the call (the original defect shape `.cx`), and a struct
            // argument + struct return in the same call.
            AbiPair p = Native.make_pair(3);
            Console.WriteLine("[abi.pair.x]=" + p.X);
            Console.WriteLine("[abi.pair.y]=" + p.Y);
            Console.WriteLine("[abi.pair.direct]=" + Native.make_pair(5).X);
            AbiPair z = Native.zero_pair();
            Console.WriteLine("[abi.pair.zero]=" + z.X + "," + z.Y);
            AbiPair sum = Native.add_pair(p, 10, 20);
            Console.WriteLine("[abi.pair.args]=" + sum.X + "," + sum.Y);

            // (b) one int64 field: the whole 64-bit value must survive.
            AbiWide w = Native.make_wide(0x0102030405060708L);
            Console.WriteLine("[abi.wide]=" + w.V);

            // (c) three int32 fields.
            AbiTriple t = Native.make_triple(100);
            Console.WriteLine("[abi.triple]=" + t.A + "," + t.B + "," + t.C);

            // (d) 4-byte aggregate.
            AbiFour f = Native.make_four(0);
            Console.WriteLine("[abi.four]=" + f.V);

            // Audited shapes.
            Native.DPI_AWARENESS awareness = Native.GetAwarenessFromDpiAwarenessContext((IntPtr)2);
            Console.WriteLine("[enum.dpi]=" + (int)awareness);

            Point caret;
            Native.BOOL caretOk = Native.GetCaretPos(out caret);
            Console.WriteLine("[enum.caret.ret]=" + (int)caretOk);
            Console.WriteLine("[enum.caret.sum]=" + Native.sum_point(caret));

            Point physical;
            Native.BOOL physicalOk = Native.GetPhysicalCursorPos(out physical);
            Console.WriteLine("[enum.phys.ret]=" + (int)physicalOk);
            Console.WriteLine("[enum.phys.sum]=" + Native.sum_point(physical));

            HandleRef r1 = new HandleRef(null, (IntPtr)0x1000);
            HandleRef r2 = new HandleRef(null, (IntPtr)0x2000);
            HandleRef r3 = new HandleRef(null, (IntPtr)0x3000);
            Console.WriteLine("[enum.combine]=" + (int)Native.Combine(r1, r2, r3, (int)Native.RegionCombineMode.OR));

            AbiRect box;
            Native.RegionFlags boxRet = Native.GetRgnBox(new HandleRef(null, (IntPtr)0x1111), out box);
            Console.WriteLine("[enum.rgnbox.ret]=" + (int)boxRet);
            Console.WriteLine("[enum.rgnbox.rect]=" + box.Left + "," + box.Top + "," + box.Right + "," + box.Bottom);

            Console.WriteLine("[enum.clip]=" + (int)Native.SelectClipRgn(new HandleRef(null, (IntPtr)0x4000), new HandleRef(null, (IntPtr)0x2222)));
        }
    }
}