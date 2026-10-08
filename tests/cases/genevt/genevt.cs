//------------------------------------------------------------------------------
// genevt.cs -- C3 (A3) managed e2e probe: the IConnectionPoint SINK event
// mounting surface through the reflection event APIs, net48 oracle.
//
// The fake-source SELF-LOOP (DECISION C3-0 "net48 oracle = managed source implementing the event
// interface"): the event source is a MANAGED class (FakeSource) exposing the event
// interface IFakeEvents (a COM event-source interface whose [Guid] folds into
// the event IID at TRANSPILE time -- the C3-1 "event -> source interface ->
// [Guid]" chain). The sink is a managed object (FakeSink) attached through
// EventInfo.AddEventHandler -- the C3-2 surface. Both platforms run the SAME
// managed code; net48 delivers through its native managed-event machinery,
// ILC delivers through the event add/remove accessors the C3-2 icalls invoke.
//
// The probe locks the C3-0 status quo: BEFORE the C3 batch, GetEvents /
// GetEvent / AddEventHandler / RemoveEventHandler are NOT wired in
// MethodMap -- the transpiler falls back to the generated-name path (link
// error / LOUD), so the ILC side FAILS while net48 produces the oracle.
// AFTER C3-1 (events table + GetEvents/GetEvent) and C3-2 (AddEventHandler/
// RemoveEventHandler through the events table's accessor names), the ILC side
// reproduces the oracle byte-for-byte.
//
// Byte-stability: every line prints a labeled string/bool/count; net48 is
// the reference baseline. No real COM dependency, no environment sensitivity
// (pure-memory managed self-loop -- the C3 managed e2e "fake-source self-loop").
//------------------------------------------------------------------------------
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Genevt
    {
        public static void Main()
        {
            FakeSource src = new FakeSource();
            FakeSink sink = new FakeSink();

            // ---- 1. C3-1: GetEvents enumerates the source's public events
            //         (the events table's name strings; net48 oracle).
            EventInfo[] evs = typeof(FakeSource).GetEvents();
            Console.WriteLine("evcount=" + evs.Length);
            string names = "";
            for (int i = 0; i < evs.Length; i++)
            {
                names += evs[i].Name + ",";
            }
            Console.WriteLine("events=" + names);

            // ---- 2/3. C3-2: AddEventHandler attaches the handler (the
            //         sink's OnTick) through the event add accessor; the
            //         source Raise delivers. RemoveEventHandler detaches --
            //         a later Raise reaches nobody.
            EventInfo ev = (evs.Length > 0) ? evs[0] : null;
            if (ev != null)
            {
                ev.AddEventHandler(src, new FakeTickHandler(sink.OnTick));
                src.Raise(7);
                Console.WriteLine("after_add=" + sink.Total);
                ev.RemoveEventHandler(src, new FakeTickHandler(sink.OnTick));
                src.Raise(3);
                Console.WriteLine("after_remove=" + sink.Total);
            }

            // ---- 4. C3-1: Type.GetEvent(name) returns a non-null EventInfo
            //         VALUE (the simplified member model -- the name string).
            Console.WriteLine("event_nonnull=" + (typeof(FakeSource).GetEvent("Tick") != null));
        }
    }

    // The COM event-source interface: the [Guid] folds into the event IID at
    // transpile time (the C3-1 "event -> source interface -> [Guid]" chain).
    [ComVisible(true)]
    [Guid("9B1C9D3E-4A2B-5C3D-6E4F-111111112222")]
    public interface IFakeEvents
    {
        void Tick(int n);
    }

    public class FakeSource : IFakeEvents
    {
        public event FakeTickHandler Tick;

        // Explicit interface implementation -- the event and the interface
        // method share the "Tick" name, and C# forbids an implicit one (CS0102).
        void IFakeEvents.Tick(int n) { }

        public void Raise(int n)
        {
            FakeTickHandler h = Tick;
            if (h != null) h(n);
        }
    }

    public class FakeSink
    {
        public int Total;
        public void OnTick(int n) { Total += n; }
    }

    public delegate void FakeTickHandler(int n);
}
