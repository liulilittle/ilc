// p-prefixediv.cs -- GN-7 obs1 probe: collapse/ensure PREFIX-SEGMENT
// divergence (GenericUsageAnalyzer.CollapseInstantiation vs EnsureClone).
// Locks the fix: a user REFERENCE-typed arg whose full name PREFIXES a
// user VALUE-type full name ("Cases.A_B" class vs "Cases.A" struct) must
// be judged by the SAME predicate on BOTH sides of an instantiation.
//
// The divergence at HEAD: EnsureClone's SuffixContainsUserValueType sees
// user struct "Cases.A" as a '_'-bounded PREFIX segment of the suffix
// "Cases.A_B" -> keeps per-instance (emits Box_1_Cases_A_B); the call
// side's CollapseInstantiation matches each arg FULL-NAME exactly ->
// "Cases.A_B" is not in the user-value-type set -> collapses to
// Box_1_object. The emitted clone is then never referenced -> g++
// "undeclared" build failure. net48 runs all lines byte-exact (distinct
// runtime types, distinct Value()).
//
// Box<T> deliberately keeps only Set/Get (no IsNull-style directed
// member) so the object-share path stays open for the probe. Main only
// instantiates Box<A_B> and Box<A> -- no other collapse source can
// produce Box_1_object.
//------------------------------------------------------------------------------
using System;

namespace Cases {

// NOTE: A stays FIELD-BEARING on purpose -- the M3/P25b value-type
// descriptor gate emits {T}_class only for field-bearing or .ctor-bearing
// structs (CodeGenerator.cpp descriptor condition); a fieldless struct's
// missing descriptor breaks the Box<A> box bridge (icall_box_user_struct)
// independently of this probe's name-divergence target. The probe's scope
// is the '_'-prefix name divergence, so the struct shape mirrors the
// known-good p-structarg probes (field X/Y).
struct A {
    public int F;
    public int Value() { return 1; }
}

class A_B {
    public int Value() { return 2; }
}

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PPrefixDiv {
    public static void Main() {
        // Class arg whose full name prefixes user struct "Cases.A".
        Box<A_B> b1 = new Box<A_B>();
        b1.Set(new A_B());
        A_B ab = b1.Get();
        Console.WriteLine(ab.Value());   // 2

        // The user struct itself.
        Box<A> b2 = new Box<A>();
        b2.Set(new A());
        A a = b2.Get();
        Console.WriteLine(a.Value());   // 1
    }
}}
