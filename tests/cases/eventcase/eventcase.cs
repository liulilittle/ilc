using System;
namespace Cases { public static class EventCase {
    static int g_events;
    public static void Main() {
        // A field-backed event (the compiler emits add_X/remove_X accessors
        // -> Delegate.Combine/Remove). The runtime bridge dispatches the
        // multicast chain through icall_multicast_invoke_rr_void (the
        // EventHandler(object, EventArgs) 2-ref-arg void ABI, batch 161).
        Counter c = new Counter();
        c.Changed += OnChangedA;
        c.Changed += OnChangedB;
        c.Changed += OnChangedA;  // duplicate: multicast keeps both leaves
        c.Raise(1);
        Console.WriteLine(g_events == 3);
        c.Changed -= OnChangedA;  // Remove: leaves [B, A]
        c.Raise(2);
        Console.WriteLine(g_events == 5);
    }
    static void OnChangedA(object sender, EventArgs e) { g_events++; }
    static void OnChangedB(object sender, EventArgs e) { g_events++; }
}}
public class Counter {
    private EventHandler _changed;
    public event EventHandler Changed {
        add { _changed += value; }
        remove { _changed -= value; }
    }
    public void Raise(int x) {
        EventHandler h = _changed;
        if (h != null) h(this, EventArgs.Empty);
    }
}
