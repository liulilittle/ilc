using System;
using System.Diagnostics;

// reg-stacktrace-ctor.cs -- D2 regression repro: a bare ctor-only
// `new StackTrace()` with NO frame access. Before the GN-20 fix the
// WeakReference-bucket routing made this a clean fold-skip (runtime M5
// error). After the routing+stub change, TryMapGC's `.ctor` arm only matches
// TracingStatusChangedEventArgs, so StackTrace::.ctor -> null -> phantom
// (`System_Diagnostics_StackTrace_class` + phantom ctor) -> cmake FAIL.
namespace Cases
{
    public static class RegStackTraceCtor
    {
        public static void Main()
        {
            var st = new StackTrace();
            Console.WriteLine("ctor-only ok");
        }
    }
}