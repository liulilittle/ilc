// mvarusecase.cs -- P-G4 F3 fixture: method-level MVAR body uses that a SHARED
// method must never carry (P-G4-AUDIT-2.md §3.2/3.4 -- the constructively
// reachable "Passer<T> calls per-instance Identity<T>" misshare). The unit
// tests read METADATA ONLY; Main is a plain semantic probe so the fixture stays
// e2e-ready.
//
//   Tool2.Identity<T>   -- by-value MVAR param -> per-instance generic method.
//   Passer2.Pass<T>     -- byref-only MVAR param (passes rules ① ② of
//                          MethodMvarShareable) BUT the body calls
//                          Tool2.Identity<!!0>(t): a MethodSpec whose genArg is
//                          this method's OWN MVAR (0x1E). F3 marks that call
//                          directed -> Passer2 must stay per-instance (mask null).
//   Box2<T> / FieldUser2 -- byref-only MVAR param whose body accesses
//                          Box2<!!0>.item (ldfld on a MemberRef whose parent
//                          TypeSpec carries the method's MVAR) -> F3 marks the
//                          field access directed -> per-instance.

using System;

public class Tool2
{
    public static T Identity<T>(T x) => x;
}

public class Passer2
{
    public static void Pass<T>(ref T a) { T t = a; a = Tool2.Identity(t); }
}

public class Box2<T>
{
    public T item;
}

public class FieldUser2
{
    public static void Store<T>(Box2<T> b, ref T a) { b.item = a; }
}

namespace Cases { public static class MvarUseCase {
    public static void Main() {
        Console.WriteLine("mvaruse");
    }
}}
