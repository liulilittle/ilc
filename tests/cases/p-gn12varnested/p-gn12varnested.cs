//------------------------------------------------------------------------------
// p-gn12varnested.cs -- GN-12 probe: VAR/MVAR-nested generic-argument index
// consumption in the MBR local-kind naming authority.
//
// The divergence at HEAD (GN-11 residual): MethodBodyReader.SimpleGenericArgSuffix
// has no 0x13 (VAR) / 0x1E (MVAR) arm, so it falls to `_ => null` WITHOUT
// consuming the compressed parameter index. A nested generic argument that is a
// bare VAR (e.g. the inner T of List<T>[,] inside a Host<T> body) therefore
// leaves the index byte in the stream: the enclosing MultidimArrayArgSuffix then
// reads that index as the RANK (0) instead of reading the real rank (2) and
// emits the definition-side clone token "[array0]", while the three call-side
// producers (MethodRefResolver ConsumeComplexM3ArgPayload / StackFold and
// CodeGenerator ConsumeComplexM3ArrayElem) emit "[array2]". The clone key and
// the newobj/callvirt token fork -> g++ "was not declared".
//
// Coverage (all inside the generic-definition body of Host<T>, so every array
// element / nested generic argument is a VAR 0x13):
//   A: Box<List<T>[,]>  -- multidim element = 0x15 nested generic (VAR inner)
//   B: Box<List<T>[]>   -- szarray  element = 0x15 nested generic (VAR inner)
//   C: Box<T[,]>        -- multidim element = VAR directly
//   D: Box<T[]>         -- szarray  element = VAR directly
// each of A/B/C/D is instantiated through BOTH a shared Box<T> (only Set/Get ->
// the object-share collapse) and a non-shared Box3<T> (the by-value MVAR generic
// method Echo<U> keeps the whole definition on the per-instance path so no
// collapse can mask a name fork).
//
// NOTE (GN-12 double-audit decision, option B): a 2-arg Pair<List<T>[,],int>
// case was deliberately dropped -- an arity-2 generic whose argument is a
// multidim array trips an INDEPENDENT pre-existing defect (open-owner suffix
// doubling in StackFold/CodeGenerator ReadM3VarArg), tracked as fix4 in its own
// batch. It is not part of the VAR-index-consumption root cause.
//
// The positive control p-gn11mdim0x15 (Box<List<int>[,]>, non-VAR) rides
// alongside and must stay green.
//
// Output is deliberately the same shape as p-gn11mdim0x15 ("True" x2, 12 bytes,
// net48 sha=bb68342c640680daa3f3777b65e8918837e16275d3bc585358807e4d0da41d9c).
// net48 is the reference baseline; both lines must print True byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

class Box3<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    // By-value MVAR generic method: keeps the whole definition on the
    // per-instance path (no object collapse can mask a name fork).
    public static void Echo<U>(U u) { }
}

class Host<T> {
    // ---- shared Box<T> arm (A/B/C/D) ----
    public static bool Shared() {
        bool ok = true;
        Box<List<T>[,]> a = new Box<List<T>[,]>();
        a.Set(null);
        ok &= a.Get() == null;
        Box<List<T>[]> b = new Box<List<T>[]>();
        b.Set(null);
        ok &= b.Get() == null;
        Box<T[,]> c = new Box<T[,]>();
        c.Set(null);
        ok &= c.Get() == null;
        Box<T[]> d = new Box<T[]>();
        d.Set(null);
        ok &= d.Get() == null;
        return ok;
    }

    // ---- non-shared Box3<T> arm (A/B/C/D) ----
    public static bool NonShared() {
        bool ok = true;
        Box3<List<T>[,]> a = new Box3<List<T>[,]>();
        a.Set(null);
        ok &= a.Get() == null;
        Box3<List<T>[]> b = new Box3<List<T>[]>();
        b.Set(null);
        ok &= b.Get() == null;
        Box3<T[,]> c = new Box3<T[,]>();
        c.Set(null);
        ok &= c.Get() == null;
        Box3<T[]> d = new Box3<T[]>();
        d.Set(null);
        ok &= d.Get() == null;
        return ok;
    }
}

public static class PGn12VarNested {
    public static void Main() {
        Console.WriteLine(Host<int>.Shared());
        Console.WriteLine(Host<int>.NonShared());
    }
}}
