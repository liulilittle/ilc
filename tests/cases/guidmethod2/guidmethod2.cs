//------------------------------------------------------------------------------
// guidmethod2.cs -- the RETURN-SHAPE surface the audit found missing from
// guidmethod: a Guid produced by a FIELD read (class field, struct field),
// by Guid.Empty, and by a GENERIC-INSTANCE owner; plus the VALUETYPE return
// family (decimal / DateTime / TimeSpan / user enum) that shares the same
// RetStmt bridge.
//
// Why this case exists: guidmethod S1..S8 only returned Guids from locals,
// call results and array elements. `return holder.G`, `return Guid.Empty` and
// `return h.G` (struct receiver) all reach RetStmt with a FieldExpr /
// StaticFieldExpr, where the kind heuristic classified the expression "val"
// and the VALUETYPE-return bridge boxed it through static_cast<int32_t> --
// uncompilable C++ for a 16-byte Il2CppGuid. The same bridge is what a
// value-typed (decimal/DateTime/TimeSpan/enum) return rides, so those are
// pinned here too.
//
// KNOWN LIMITATION (pre-existing, NOT part of this task): a struct-typed
// INSTANCE FIELD (`struct Outer { Inner I; }` -- o.I.G) is rejected by the
// metadata loader ("value-type instance fields not supported in M2"), so the
// nested-struct shape cannot be expressed here; the struct-OWNER field read
// (`h.G` with `h` a struct) is covered instead.
//
// Determinism: every Guid comes from Parse / Empty / default, never NewGuid.
// DateTime/TimeSpan are asserted via Ticks (integers, culture-free).
//------------------------------------------------------------------------------
using System;

public class GuidBox2
{
    public Guid G;
    public int Tag;
}

public struct InnerH
{
    public Guid G;
    public int N;
}

public class Gen<T>
{
    public Guid G;
    public T Value;
}

public enum Shade
{
    Red = 0,
    Green = 1,
    Blue = 2,
}

public static class GuidMethod2
{
    // S9: a Guid FIELD of a user CLASS (ldfld through the null-checked
    // receiver).
    private static Guid GetField(GuidBox2 h) { return h.G; }

    // S10: Guid.Empty (ldsfld of the shared all-zero slot).
    private static Guid GetEmpty() { return Guid.Empty; }

    // S11: a Guid field of a STRUCT receiver (value-type owner: no null check,
    // the field access goes through the value slot).
    private static Guid GetStructField(InnerH h) { return h.G; }

    // S12 (D2): a Guid field on a GENERIC-INSTANCE owner.
    private static Guid GetGenericField(Gen<int> g) { return g.G; }

    // S13 (D4): VALUETYPE parameter returned as-is.
    private static decimal RetDec(decimal d) { return d; }
    private static DateTime RetDt(DateTime d) { return d; }
    private static TimeSpan RetTs(TimeSpan t) { return t; }

    // S14 (D6): user ENUM parameter returned as-is.
    private static Shade RetEnum(Shade e) { return e; }

    public static void Main()
    {
        Guid one = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Guid two = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // ===== S9: return a Guid FIELD (class owner) =====
        GuidBox2 box = new GuidBox2();
        box.G = one;
        box.Tag = 7;
        Console.WriteLine("[gm2] field=" + GetField(box).ToString());
        Console.WriteLine("[gm2] field_eq=" + (GetField(box) == one));       // True
        Console.WriteLine("[gm2] field_ne=" + (GetField(box) != two));       // True
        Console.WriteLine("[gm2] field_len=" + GetField(box).ToString().Length); // 36
        Console.WriteLine("[gm2] field_tag=" + box.Tag);                     // 7

        // ===== S10: return Guid.Empty =====
        Console.WriteLine("[gm2] empty=" + GetEmpty().ToString());
        Console.WriteLine("[gm2] empty_eq=" + (GetEmpty() == Guid.Empty));   // True
        Console.WriteLine("[gm2] empty_default=" + (GetEmpty() == default(Guid))); // True
        Console.WriteLine("[gm2] empty_ne=" + (GetEmpty() != one));          // True

        // ===== S11: return a Guid field of a STRUCT receiver =====
        InnerH ih = new InnerH();
        ih.G = two;
        ih.N = 3;
        Console.WriteLine("[gm2] struct_field=" + GetStructField(ih).ToString());
        Console.WriteLine("[gm2] struct_eq=" + (GetStructField(ih) == two)); // True
        Console.WriteLine("[gm2] struct_ne=" + (GetStructField(ih) != one)); // True
        Console.WriteLine("[gm2] struct_n=" + ih.N);                         // 3

        // ===== S12: a Guid field on a GENERIC-INSTANCE owner (D2) =====
        // The freshly allocated instance is zero-initialized, so the field is
        // default(Guid) -- the read must still be the 16-byte Guid value (the
        // generic instance owner "Gen`1_int" is not the definition "Gen`1").
        Gen<int> gi = new Gen<int>();
        gi.Value = 42;
        Console.WriteLine("[gm2] gen_empty=" + (gi.G == Guid.Empty));        // True
        Console.WriteLine("[gm2] gen_str=" + gi.G.ToString());
        Console.WriteLine("[gm2] gen_ne=" + (gi.G != one));                  // True
        Console.WriteLine("[gm2] gen_field=" + GetGenericField(gi).ToString());
        Console.WriteLine("[gm2] gen_value=" + gi.Value);                    // 42

        // ===== S13: VALUETYPE parameters returned as-is (D4) =====
        Console.WriteLine("[gm2] dec_eq=" + (RetDec(2.5m) == 2.5m));         // True
        Console.WriteLine("[gm2] dec_double=" + (int)(RetDec(2.5m) * 2m));   // 5
        Console.WriteLine("[gm2] dec_neg=" + (RetDec(-1.5m) == -1.5m));      // True

        DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        Console.WriteLine("[gm2] dt_ticks=" + RetDt(dt).Ticks);              // 637135310450000000
        Console.WriteLine("[gm2] dt_eq=" + (RetDt(dt) == dt));               // True
        Console.WriteLine("[gm2] dt_kind=" + (int)RetDt(dt).Kind);           // 0

        TimeSpan ts = TimeSpan.FromSeconds(90);
        Console.WriteLine("[gm2] ts_ticks=" + RetTs(ts).Ticks);              // 900000000
        Console.WriteLine("[gm2] ts_eq=" + (RetTs(ts) == ts));               // True
        Console.WriteLine("[gm2] ts_neg=" + RetTs(TimeSpan.FromTicks(-5)).Ticks); // -5

        // ===== S14: user enum returned as-is (D6) =====
        Console.WriteLine("[gm2] enum_eq=" + (RetEnum(Shade.Blue) == Shade.Blue)); // True
        Console.WriteLine("[gm2] enum_int=" + (int)RetEnum(Shade.Blue));     // 2
        Console.WriteLine("[gm2] enum_str=" + RetEnum(Shade.Blue).ToString()); // Blue
        Console.WriteLine("[gm2] enum_zero=" + (int)RetEnum(Shade.Red));     // 0
    }
}
