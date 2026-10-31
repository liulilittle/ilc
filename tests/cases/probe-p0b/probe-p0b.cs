//------------------------------------------------------------------------------
// probe-p0b.cs -- Marshal COM surface probe (P0 #12-#30, 19 members).
//
// Oracle: .NET Framework 4.8 x64. Every record is a fixed int/bool derived
// from deterministic inputs (no raw pointer values are printed).
//
// G1 (#12 AddRef / #30 Release / #29 QueryInterface): a real IUnknown is
// obtained through Marshal.GetIUnknownForObject(new object()) -- net48 and
// this runtime both hand out a CCW with reference count 1, so the measured
// counts are directly comparable. (A direct ole32 CreateStreamOnHGlobal
// P/Invoke is impossible here: by-ref DllImport parameters are LOUD-rejected
// by the transpiler.)
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbeP0b
    {
        public static void Main()
        {
            object o = new object();
            IntPtr p = Marshal.GetIUnknownForObject(o);
            Console.WriteLine("[g1.ccw.nonzero]=" + ((long)p != 0));

            Console.WriteLine("[g1.addref]=" + Marshal.AddRef(p));
            Console.WriteLine("[g1.release]=" + Marshal.Release(p));

            Guid iidUnk = new Guid(0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46);
            IntPtr ppv = (IntPtr)0;
            int hrQi = Marshal.QueryInterface(p, ref iidUnk, out ppv);
            Console.WriteLine("[g1.qi.unk.hr]=" + hrQi.ToString("X8"));
            Console.WriteLine("[g1.qi.unk.same]=" + ((long)ppv == (long)p));
            Console.WriteLine("[g1.qi.unk.release]=" + Marshal.Release(ppv));

            Guid iidDisp = new Guid(0x00020400, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46);
            IntPtr pd = (IntPtr)0;
            int hrDisp = Marshal.QueryInterface(p, ref iidDisp, out pd);
            Console.WriteLine("[g1.qi.disp.hr]=" + hrDisp.ToString("X8"));
            Console.WriteLine("[g1.qi.disp.nonzero]=" + ((long)pd != 0));
            if ((long)pd != 0)
            {
                Console.WriteLine("[g1.qi.disp.release]=" + Marshal.Release(pd));
            }

            Guid iidBad = new Guid(0x11111111, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            IntPtr pb = (IntPtr)0x1234;
            int hrBad = Marshal.QueryInterface(p, ref iidBad, out pb);
            Console.WriteLine("[g1.qi.bad.hr]=" + hrBad.ToString("X8"));
            Console.WriteLine("[g1.qi.bad.ppv.zero]=" + ((long)pb == 0));

            Console.WriteLine("[g1.release.final]=" + Marshal.Release(p));

            try { Marshal.AddRef(IntPtr.Zero); }
            catch (Exception e) { Console.WriteLine("[g1.addref.null]=" + e.GetType().Name); }
            try { Marshal.Release(IntPtr.Zero); }
            catch (Exception e) { Console.WriteLine("[g1.release.null]=" + e.GetType().Name); }
            try
            {
                Guid g = Guid.Empty;
                IntPtr q = (IntPtr)0;
                Marshal.QueryInterface(IntPtr.Zero, ref g, out q);
            }
            catch (Exception e) { Console.WriteLine("[g1.qi.null]=" + e.GetType().Name); }
        }
    }
}
