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

//------------------------------------------------------------------------------
// G2 metadata fixtures (the slot / visibility surface).
//------------------------------------------------------------------------------
[ComImport, Guid("41111111-2222-3333-4444-555555555551"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IZero { }

[ComImport, Guid("41111111-2222-3333-4444-555555555552"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOne { void A(); }

[ComImport, Guid("41111111-2222-3333-4444-555555555553"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThree { void A(); int B(int x); string C { get; } }

[ComImport, Guid("41111111-2222-3333-4444-555555555554"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDerived : IOne { void D(); }

public interface IPlain { void A(); }

[ComVisible(true), Guid("41111111-2222-3333-4444-555555555555")]
public class VisibleClass { }

[ComVisible(false), Guid("41111111-2222-3333-4444-555555555556")]
public class HiddenClass { }

public class NoAttrClass { }

[ComVisible(false)]
public class HiddenBase { }

public class DerivedFromHidden : HiddenBase { }

// G3 fixtures: a [Guid]-annotated ComImport interface (the typed-object IID
// path) and a COM-visible class (the aggregation path).
[ComImport, Guid("71111111-2222-3333-4444-555555555551"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITyped { }

[ComVisible(true), Guid("71111111-2222-3333-4444-555555555552")]
public class VisibleAgg { }

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
                IntPtr qn = (IntPtr)0;
                Marshal.QueryInterface(IntPtr.Zero, ref g, out qn);
            }
            catch (Exception e) { Console.WriteLine("[g1.qi.null]=" + e.GetType().Name); }

            // ---- G2: COM slot / visibility metadata (transpile-time fold) ----
            Console.WriteLine("[g2.zero.start]=" + Marshal.GetStartComSlot(typeof(IZero)));
            Console.WriteLine("[g2.zero.end]=" + Marshal.GetEndComSlot(typeof(IZero)));
            Console.WriteLine("[g2.one.start]=" + Marshal.GetStartComSlot(typeof(IOne)));
            Console.WriteLine("[g2.one.end]=" + Marshal.GetEndComSlot(typeof(IOne)));
            Console.WriteLine("[g2.three.start]=" + Marshal.GetStartComSlot(typeof(IThree)));
            Console.WriteLine("[g2.three.end]=" + Marshal.GetEndComSlot(typeof(IThree)));
            Console.WriteLine("[g2.derived.start]=" + Marshal.GetStartComSlot(typeof(IDerived)));
            Console.WriteLine("[g2.derived.end]=" + Marshal.GetEndComSlot(typeof(IDerived)));
            Console.WriteLine("[g2.plain.start]=" + Marshal.GetStartComSlot(typeof(IPlain)));
            Console.WriteLine("[g2.plain.end]=" + Marshal.GetEndComSlot(typeof(IPlain)));
            Console.WriteLine("[g2.class.start]=" + Marshal.GetStartComSlot(typeof(VisibleClass)));
            Console.WriteLine("[g2.class.end]=" + Marshal.GetEndComSlot(typeof(VisibleClass)));
            Console.WriteLine("[g2.vis.zero]=" + Marshal.IsTypeVisibleFromCom(typeof(IZero)));
            Console.WriteLine("[g2.vis.one]=" + Marshal.IsTypeVisibleFromCom(typeof(IOne)));
            Console.WriteLine("[g2.vis.plain]=" + Marshal.IsTypeVisibleFromCom(typeof(IPlain)));
            Console.WriteLine("[g2.vis.visibleclass]=" + Marshal.IsTypeVisibleFromCom(typeof(VisibleClass)));
            Console.WriteLine("[g2.vis.hiddenclass]=" + Marshal.IsTypeVisibleFromCom(typeof(HiddenClass)));
            Console.WriteLine("[g2.vis.noattr]=" + Marshal.IsTypeVisibleFromCom(typeof(NoAttrClass)));
            Console.WriteLine("[g2.vis.derivedhidden]=" + Marshal.IsTypeVisibleFromCom(typeof(DerivedFromHidden)));

            // ---- G3: the RCW lifecycle surface (real runtime state) ----
            // A FRESH IUnknown: the G1 section already released 'p' to 0.
            IntPtr q = Marshal.GetIUnknownForObject(o);
            Console.WriteLine("[g3.cleanup.avail]=" + Marshal.AreComObjectsAvailableForCleanup());
            Marshal.CleanupUnusedObjectsInCurrentContext();
            Console.WriteLine("[g3.cleanup.run]=ok");
            Console.WriteLine("[g3.cleanup.avail2]=" + Marshal.AreComObjectsAvailableForCleanup());

            object typed1 = Marshal.GetTypedObjectForIUnknown(q, typeof(object));
            object typed2 = Marshal.GetTypedObjectForIUnknown(q, typeof(object));
            Console.WriteLine("[g3.typed.identity]=" + object.ReferenceEquals(typed1, typed2));
            Console.WriteLine("[g3.typed.iscom]=" + Marshal.IsComObject(typed1));

            object unique1 = Marshal.GetUniqueObjectForIUnknown(q);
            object unique2 = Marshal.GetUniqueObjectForIUnknown(q);
            Console.WriteLine("[g3.unique.iscom]=" + Marshal.IsComObject(unique1));
            Console.WriteLine("[g3.unique.ccw.managed]=" + object.ReferenceEquals(unique1, o));
            Console.WriteLine("[g3.unique.same]=" + object.ReferenceEquals(unique1, unique2));

            object chs = new object();
            Marshal.ChangeWrapperHandleStrength(chs, true);
            Marshal.ChangeWrapperHandleStrength(chs, false);
            Console.WriteLine("[g3.chs.ok]=True");
            try { Marshal.ChangeWrapperHandleStrength(null, true); }
            catch (Exception e) { Console.WriteLine("[g3.chs.null]=" + e.GetType().Name); }

            IntPtr agg = Marshal.CreateAggregatedObject(q, new VisibleAgg());
            Console.WriteLine("[g3.agg.nonzero]=" + ((long)agg != 0));
            IntPtr aggUnk = (IntPtr)0;
            int aggHr = Marshal.QueryInterface(agg, ref iidUnk, out aggUnk);
            Console.WriteLine("[g3.agg.qi.hr]=" + aggHr.ToString("X8"));
            Console.WriteLine("[g3.agg.qi.nonzero]=" + ((long)aggUnk != 0));
            if ((long)aggUnk != 0) Marshal.Release(aggUnk);
            Marshal.Release(agg);
            try { Marshal.CreateAggregatedObject(IntPtr.Zero, chs); }
            catch (Exception e) { Console.WriteLine("[g3.agg.nullouter]=" + e.GetType().Name); }
            try { Marshal.CreateAggregatedObject(q, null); }
            catch (Exception e) { Console.WriteLine("[g3.agg.nullobj]=" + e.GetType().Name); }

            Marshal.Release(q);
        }
    }
}
