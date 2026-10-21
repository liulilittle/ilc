//------------------------------------------------------------------------------
// guidmethod.cs -- System.Guid as a METHOD PARAMETER and RETURN VALUE.
//
// The Guid value chain (fields / arrays / locals / ToString / Parse / ==)
// landed earlier, but a Guid in a METHOD SIGNATURE did not: the emitter had
// no C++ type for the parameter/return slot (B3 added the Il2CppGuid value
// struct for fields, but `static Guid Id(Guid g)` still emitted a raw
// uint8_t[16] local ABI and the guid.h icalls took the struct VALUE where a
// byte pointer is required). This case pins the whole signature surface:
//
//   S1 static Guid -> int / Guid -> Guid / () -> Guid
//   S2 instance methods on a class (this + Guid param, Guid return)
//   S3 multi-parameter (Guid, Guid) and mixed (Guid, int) signatures
//   S4 Guid + decimal in one signature (both are 16-byte MS x64 ABI:
//      hidden pointer in, sret out)
//   S5 chained calls on a Guid return (Make().ToString(), Id(x).ToString(),
//      Make() == one, Make().ToString().Length)
//   S6 a Guid[] element returned from a method (the ldelem_guid cell copy)
//   S7 boundary arguments: Guid.Empty, default(Guid), new Guid(string)
//      passed straight into a Guid parameter
//   S8 a Guid-taking leaf method bound to a delegate (Func<Guid,int>):
//      the leaf must be emitted with the Guid-by-value ABI so the delegate
//      batch can bind it later. Invoking through the delegate stays out of
//      scope (the multicast helper has no struct-value ABI -- documented gap).
//
// Determinism: every Guid comes from Parse/Empty/default, never NewGuid, so
// the D-form strings are byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
using System;

public class GuidMethods
{
    // S2: instance methods -- `this` is the object reference, the Guid
    // parameter/return ride the value ABI.
    public Guid F(Guid g) { return g; }
    public int G(Guid g) { return g == Guid.Empty ? 0 : 1; }
    public Guid Echo2(Guid a, Guid b) { return a == b ? a : b; }
}

public static class GuidMethod
{
    // S1: read-only Guid parameter, int return.
    private static int Rank(Guid g) { return g == Guid.Empty ? 0 : 1; }

    // S1: Guid parameter + Guid return (identity).
    private static Guid Id(Guid g) { return g; }

    // S1: Guid return only.
    private static Guid Make() { return Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"); }

    // S3: two Guid parameters.
    private static string Fmt(Guid a, Guid b) { return a.ToString() + "/" + b.ToString(); }

    // S3: Guid mixed with an int.
    private static int Mix(Guid a, int n) { return (a == Guid.Empty ? 0 : n) + n; }

    // S4: Guid mixed with a decimal (16-byte MS x64 ABI for both).
    private static decimal D(Guid g, decimal m) { return m * 2m; }

    // S6: Guid[] element read through a method boundary.
    private static Guid FromArray(Guid[] arr, int i) { return arr[i]; }

    // S6b: conditional Guid return from two parameters.
    private static Guid Pick(Guid a, Guid b, bool first) { return first ? a : b; }

    public static void Main()
    {
        Guid one = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Guid two = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // ===== S1: static Guid parameter / return =====
        Console.WriteLine("[gm] rank_one=" + Rank(one));            // 1
        Console.WriteLine("[gm] rank_two=" + Rank(two));            // 1
        Console.WriteLine("[gm] id=" + Id(one).ToString());         // 00112233-4455-6677-8899-aabbccddeeff
        Console.WriteLine("[gm] id_rt=" + (Id(one) == one));        // True
        Console.WriteLine("[gm] id_nested=" + Id(Id(two)).ToString());
        Console.WriteLine("[gm] make=" + Make().ToString());        // 00112233-...
        Console.WriteLine("[gm] make_eq=" + (Make() == one));       // True
        Console.WriteLine("[gm] make_ne=" + (Make() != two));       // True

        // ===== S3: multiple / mixed parameters =====
        Console.WriteLine("[gm] fmt=" + Fmt(one, two));
        Console.WriteLine("[gm] mix=" + Mix(one, 5));               // 10
        Console.WriteLine("[gm] mix_zero=" + Mix(Guid.Empty, 5));   // 5

        // ===== S4: Guid + decimal =====
        Console.WriteLine("[gm] dec=" + string.Format("{0}", D(one, 2.5m))); // 5.0
        Console.WriteLine("[gm] dec_zero=" + string.Format("{0}", D(Guid.Empty, 1m))); // 2.0

        // ===== S5: chained calls on a Guid return =====
        Console.WriteLine("[gm] chain_tostring=" + Make().ToString());
        Console.WriteLine("[gm] chain_len=" + Make().ToString().Length);        // 36
        Console.WriteLine("[gm] chain_id_str=" + Id(one).ToString());
        Console.WriteLine("[gm] chain_eq=" + (Make() == one));                  // True
        Console.WriteLine("[gm] chain_parse=" + Guid.Parse(Id(one).ToString()).ToString());

        // ===== S2: instance methods =====
        GuidMethods c = new GuidMethods();
        Console.WriteLine("[gm] inst_f=" + c.F(one).ToString());
        Console.WriteLine("[gm] inst_g=" + c.G(one) + "," + c.G(Guid.Empty));   // 1,0
        Console.WriteLine("[gm] inst_echo=" + c.Echo2(one, two).ToString());
        Console.WriteLine("[gm] inst_echo_eq=" + (c.Echo2(two, two) == two));   // True

        // ===== S6: Guid[] element through a method boundary =====
        Guid[] arr = new Guid[2];
        arr[0] = one;
        arr[1] = two;
        Console.WriteLine("[gm] arr0=" + FromArray(arr, 0).ToString());
        Console.WriteLine("[gm] arr1=" + FromArray(arr, 1).ToString());
        Console.WriteLine("[gm] arr_pick=" + Pick(arr[0], arr[1], false).ToString());
        Console.WriteLine("[gm] arr_pick_eq=" + (Pick(arr[0], arr[1], true) == one)); // True

        // ===== S7: boundary arguments =====
        Console.WriteLine("[gm] arg_empty=" + Rank(Guid.Empty));                 // 0
        Console.WriteLine("[gm] arg_default=" + Rank(default(Guid)));            // 0
        Console.WriteLine("[gm] arg_ctor=" + Rank(new Guid("00112233-4455-6677-8899-aabbccddeeff"))); // 1
        Console.WriteLine("[gm] arg_empty_id=" + (Id(Guid.Empty) == Guid.Empty)); // True
        Console.WriteLine("[gm] arg_default_id=" + (Id(default(Guid)) == Guid.Empty)); // True
        Console.WriteLine("[gm] arg_empty_str=" + Id(Guid.Empty).ToString());    // 00000000-...
        Console.WriteLine("[gm] arg_ctor_str=" + Id(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")).ToString());

        // ===== S8: Guid-taking leaf bound to a delegate =====
        Func<Guid, int> f = Rank;
        Console.WriteLine("[gm] del_created=" + (f != null));        // True
        Console.WriteLine("[gm] del_static_target=" + (f.Target == null)); // True
        Func<Guid, Guid> g = Id;
        Console.WriteLine("[gm] del_id_created=" + (g != null));     // True
    }
}
