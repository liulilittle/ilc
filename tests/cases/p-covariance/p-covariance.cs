// p-covariance.cs -- GN-0 probe: interface COVARIANCE (out T) (GENERICS-FULL-
// ARG-GAPS.md 2/5 P2 + covariance, P-covariance). Locks the status quo; no
// implementation changes in this batch.
//
// The gap: GetShareMask does not read the VarianceMask and interface defs are
// all rejected for sharing (safe side), but a covariant receiver slot
// (IBox<string> assigned into an IBox<object> reference) needs the concrete
// IBox<object> interface to resolve on a Box<string> object. net48 runs all
// three lines byte-exact (the covariant conversion is legal and Get() returns
// the stored string).
//------------------------------------------------------------------------------
using System;

namespace Cases {

interface IBox<out T> { T Get(); }

class Box<T> : IBox<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PCovariance {
    public static void Main() {
        // ---- 1. covariant reference conversion + dispatch ----
        Box<string> bs = new Box<string>();
        bs.Set("hello");
        IBox<string> isb = bs;
        IBox<object> iob = isb;
        Console.WriteLine(iob.Get() == "hello");   // True

        // ---- 2. the concrete source still dispatches directly ----
        Console.WriteLine(isb.Get() == "hello");   // True

        // ---- 3. a second instance through the object slot ----
        Box<string> bs2 = new Box<string>();
        bs2.Set("world");
        IBox<object> iob2 = bs2;
        Console.WriteLine(iob2.Get() == "world");  // True
    }
}}