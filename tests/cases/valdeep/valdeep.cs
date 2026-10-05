//------------------------------------------------------------------------------
// valdeep.cs -- end-to-end reference case for VALUE-TYPE deep chains:
// the full primitive family boxed into object, then boxed ToString value
// text, GetType().Name short names (the il2cpp_type_get_short_name fix),
// unbox round-trips, the UInt32 MaxValue bit-pattern round trip, and
// wrong-type / null unbox exceptions.
//
// Every line prints True/False (a bool assertion).
//
// Runs on .NET Framework 4.8 (the reference runtime). Ground truth:
//   * boxing int/long/double/float/char/bool/uint/ulong and calling
//     ToString() on the box prints the VALUE text (a UInt64 MaxValue box
//     prints "18446744073709551615", NOT -1 -- the full 64-bit pattern);
//   * GetType().Name is the SHORT name ("Int32"/"UInt64"/"UInt32"/...)
//     while the GetType() icall returns the FULL name string;
//   * unboxing restores the exact value, including the UInt32 MaxValue
//     pattern (0xFFFFFFFF) that lives in the SAME int32 payload slot as
//     the Int32 box;
//   * wrong-type unbox -> InvalidCastException, null unbox ->
//     NullReferenceException (both caught, printing True).
//
// KNOWN GAPS (probe-verified 2026-08-05 -- see tests/cases/valdeep/README
// and .opencode/discover.md):
//   * `((object)uintVal).ToString()` is NOT wired -- "System.UInt32" is
//     missing from the Object::ToString receiver-type list in
//     MethodMap.Object.cs (the runtime icall_object_to_string DOES format
//     UInt32, valuesemantics.cpp -- a 1-line transpiler gap). The call
//     would emit a vcall into an EMPTY runtime vtable and crash.
//   * `o.GetType().ToString()` is NOT wired -- the GetType() result is
//     tracked as "System.Type" and Object::ToString has no System.Type
//     mapping (same empty-vtable crash). GetType().Name works (it maps to
//     il2cpp_type_get_short_name) -- that is why only .Name is exercised.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ValDeep {
    public static void Main() {
        // --- box the full primitive family ---
        object oi = 42;                  // Int32
        object ol = 1234567890123L;      // Int64
        object od = 3.25;                // Double
        object of = 1.5f;                // Single
        object oc = 'A';                 // Char
        object ob = true;                // Boolean
        object ou = uint.MaxValue;       // UInt32 (0xFFFFFFFF)
        object oul = ulong.MaxValue;     // UInt64 (0xFFFFFFFFFFFFFFFF)

        // --- boxed ToString renders the VALUE text ---
        Console.WriteLine(oi.ToString() == "42");                 // True (Int32 box)
        Console.WriteLine(ol.ToString() == "1234567890123");      // True (Int64 box)
        Console.WriteLine(od.ToString() == "3.25");               // True (Double box)
        Console.WriteLine(of.ToString() == "1.5");                // True (Single box)
        Console.WriteLine(oc.ToString() == "A");                  // True (Char box)
        Console.WriteLine(ob.ToString() == "True");               // True (Boolean box)
        Console.WriteLine(oul.ToString() == "18446744073709551615"); // True (UInt64 box)

        // --- GetType().Name is the SHORT name (short_name fix) ---
        Console.WriteLine(oi.GetType().Name == "Int32");          // True
        Console.WriteLine(ol.GetType().Name == "Int64");          // True
        Console.WriteLine(od.GetType().Name == "Double");         // True
        Console.WriteLine(of.GetType().Name == "Single");         // True
        Console.WriteLine(oc.GetType().Name == "Char");           // True
        Console.WriteLine(ob.GetType().Name == "Boolean");        // True
        Console.WriteLine(ou.GetType().Name == "UInt32");         // True
        Console.WriteLine(oul.GetType().Name == "UInt64");        // True

        // --- unbox round-trips ---
        Console.WriteLine((int)oi == 42);                         // True
        Console.WriteLine((long)ol == 1234567890123L);            // True
        Console.WriteLine((double)od == 3.25);                    // True
        Console.WriteLine((float)of == 1.5f);                     // True
        Console.WriteLine((char)oc == 'A');                       // True
        Console.WriteLine((bool)ob == true);                      // True
        Console.WriteLine((uint)ou == 4294967295u);               // True
        Console.WriteLine((ulong)oul == 18446744073709551615UL);  // True

        // --- UInt32 MaxValue: the FULL 32-bit pattern round-trips ---
        uint u1 = (uint)ou;
        Console.WriteLine(u1 == uint.MaxValue);                   // True
        Console.WriteLine(u1 == 4294967295u);                     // True
        Console.WriteLine((ulong)u1 == 4294967295UL);             // True

        // --- wrong-type unbox throws InvalidCastException ---
        try { int x = (int)ou; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (uint box as int)
        try { uint x = (uint)oi; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (int box as uint)
        try { char x = (char)ol; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (long box as char)

        // --- null unbox throws NullReferenceException ---
        try { uint x = (uint)(object)null; Console.WriteLine(false); }
        catch (NullReferenceException) { Console.WriteLine(true); } // True (null unbox)
    }
}}
