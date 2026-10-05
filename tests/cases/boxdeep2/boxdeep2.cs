//------------------------------------------------------------------------------
// boxdeep2.cs -- end-to-end reference case for the TWO remaining deep-boxing
// gaps (probe-verified 2026-08-05, see .opencode/discover.md):
//
//   GAP 1: ((object)uintVal).ToString() is NOT wired -- "System.UInt32" is
//          missing from the Object::ToString receiver-type list in
//          MethodMap.Object.cs (the runtime icall_object_to_string DOES
//          format UInt32, valuesemantics.cpp -- a 1-line transpiler gap).
//          The call would emit a vcall into an EMPTY runtime vtable and
//          crash.
//   GAP 2: o.GetType().ToString() is NOT wired -- the GetType() result is
//          tracked as "System.Type" and Object::ToString has no
//          System.Type mapping (same empty-vtable crash). GetType().Name
//          works (il2cpp_type_get_short_name) -- that is why valdeep only
//          exercises .Name.
//
// This case probes BOTH gaps directly (lines marked PROBE-1 / PROBE-2),
// plus regression lines for the already-wired families: boxed
// long/double/char/bool ToString, a mixed object[] traversal (where the
// element's static type is System.Object, so ToString routes through the
// wired icall and MUST print the value), is/as on boxed uint, and
// wrong-type unbox catches. Every line prints True/False.
//
// Runs on .NET Framework 4.8 (the reference runtime). Ground truth:
//   * boxed uint ToString -> "4294967295" (UInt32.MaxValue, the full
//     32-bit pattern, NOT -1);
//   * GetType().ToString() -> FULL name "System.UInt32" (Type.ToString is
//     the full name, unlike Type.Name which is the short name);
//   * boxed long/double/char/bool ToString -> value text;
//   * object[] traversal: each boxed element's ToString -> its value text
//     (element static type is object, wired path);
//   * boxed uint is IConvertible/IComparable -> True; is string -> False;
//     boxed uint `is uint` -> True and `is int` -> False (exact box type);
//   * wrong-type unbox -> InvalidCastException (caught, True).
//------------------------------------------------------------------------------
using System;

public interface IConvProbe { }
public interface ICmpProbe { }

namespace Cases { public static class BoxDeep2 {
    public static void Main() {
        // --- GAP-1 probe: boxed uint ToString renders the VALUE text ---
        object ou = uint.MaxValue;
        Console.WriteLine(ou.ToString() == "4294967295");   // True (PROBE-1: UInt32 box -> value)

        // --- GAP-2 probe: GetType().ToString() is the FULL type name ---
        Console.WriteLine(ou.GetType().ToString() == "System.UInt32"); // True (PROBE-2: Type.ToString -> full name)

        // --- regression: already-wired boxed families ---
        object ol = 1234567890123L;
        object od = 3.25;
        object oc = 'A';
        object ob = true;
        Console.WriteLine(ol.ToString() == "1234567890123"); // True (Int64 box)
        Console.WriteLine(od.ToString() == "3.25");          // True (Double box)
        Console.WriteLine(oc.ToString() == "A");             // True (Char box)
        Console.WriteLine(ob.ToString() == "True");          // True (Boolean box)

        // --- mixed object[] traversal: element static type is object ---
        object[] arr = new object[] { 42, "two", 3.5, true, 'c', 1234567890123L, uint.MaxValue };
        string[] want = new string[] { "42", "two", "3.5", "True", "c", "1234567890123", "4294967295" };
        bool allOk = true;
        for (int i = 0; i < arr.Length; i++)
            allOk = allOk && arr[i].ToString() == want[i];
        Console.WriteLine(allOk);                            // True (array traversal)

        // --- is/as on the boxed uint ---
        object ou2 = 4000000000u;
        Console.WriteLine(ou2 is IConvertible);              // True (BCL interface)
        Console.WriteLine((ou2 as IConvertible) != null);    // True
        Console.WriteLine(ou2 is IComparable);               // True
        Console.WriteLine((ou2 as IComparable) != null);     // True
        Console.WriteLine(ou2 is string);                    // False
        Console.WriteLine((ou2 as string) == null);          // True
        Console.WriteLine(ou2 is uint);                      // True (exact box type)
        Console.WriteLine(ou2 is int);                       // False (uint box is NOT int)

        // --- is/as on null ---
        object nil = null;
        Console.WriteLine(nil is IConvertible);              // False
        Console.WriteLine((nil as IComparable) == null);     // True

        // --- wrong-type unbox -> InvalidCastException ---
        try { int x = (int)ou2; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (uint box as int)
        try { uint x = (uint)(object)42; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (int box as uint)
        try { uint x = (uint)ol; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True (long box as uint)

        // --- null unbox -> NullReferenceException ---
        try { uint x = (uint)(object)null; Console.WriteLine(false); }
        catch (NullReferenceException) { Console.WriteLine(true); } // True (null unbox)
    }
}}
