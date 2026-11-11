//------------------------------------------------------------------------------
// newt.cs -- A3: Activator.CreateInstance<T>() (generic). net48 oracle:
//
//   Act<int>()   -> true   (ctor ran)
//   Act<string>() -> false (Activator throws MissingMethodException)
//
// Activator.CreateInstance<T>() is LOUD today: MethodMap.Reflect.cs
// "CreateInstance" has no <T> genArgs path (line 405-442), so the call falls
// through to the generic "CreateInstance with N parameter(s) not wired"
// rejection. Registered as a gap pending the CreateInstance<T> feature
// decision.
//------------------------------------------------------------------------------
using System;

namespace Cases {
    public static class Act {
        public static bool Run<T>() {
            try {
                var t = Activator.CreateInstance<T>();
                return t != null;
            } catch {
                return false;
            }
        }
    }

    public static class ActMain {
        public static void Main() {
            Console.WriteLine("Act_int=" + Act.Run<int>());
            Console.WriteLine("Act_string=" + Act.Run<string>());
        }
    }
}
