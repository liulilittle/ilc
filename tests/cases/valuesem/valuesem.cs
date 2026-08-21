//------------------------------------------------------------------------------
// valuesem.cs -- value-semantics reference program (System.Object / primitive
// GetHashCode/ToString/Equals semantics). Runs on REAL .NET Framework 4.8
// (net48) and its printed output is the ORACLE the runtime icalls
// (icall_object_get_hash_code / icall_object_to_string / icall_object_equals
// and the per-type hash icalls in il2cpp_value_semantics.h) must match.
//
// Expected net48 semantics (empirically verified below):
//   * string.GetHashCode is ORDINAL but the seed is randomized PER PROCESS
//     in net48 (DoS hardening) -- only same-process stability is asserted
//     here; the runtime uses the SAME ordinal algorithm UNrandomized
//     (deterministic AOT output), so cross-process raw values differ by
//     design (see the header for the divergence note).
//   * Int32.GetHashCode   = the value itself.
//   * Int64.GetHashCode   = low 32 bits XOR high 32 bits.
//   * Double.GetHashCode  = (int)bits ^ (int)(bits >> 32); +0.0 and -0.0
//     both hash to 0. NaN behaviour is probed below (payload unification).
//   * Boolean.GetHashCode = true -> 1, false -> 0.
//   * ((object)int).ToString() -> decimal; ((object)bool).ToString() ->
//     "True"/"False"; string.ToString() -> the string itself; a plain
//     object's ToString() -> its type full name.
//   * object.Equals: reference equality, boxed values compare by value when
//     the box types are identical, strings compare ordinally.
//------------------------------------------------------------------------------

using System;

class Valuesem
{
    static void Main()
    {
        // ---- 1. string.GetHashCode: process-internal stability only ----
        int sha = "abc".GetHashCode();
        Console.WriteLine("strhash same literal: " + (sha == "abc".GetHashCode()));
        Console.WriteLine("strhash equal content: " + (("a" + "bc").GetHashCode() == sha));
        Console.WriteLine("strhash different: " + (sha != "abd".GetHashCode()));
        Console.WriteLine("strhash empty stable: " + ("".GetHashCode() == "".GetHashCode()));

        // ---- 2. Int32.GetHashCode = the value itself ----
        Console.WriteLine("int32 hash 0: " + (0).GetHashCode());
        Console.WriteLine("int32 hash 1: " + (1).GetHashCode());
        Console.WriteLine("int32 hash -1: " + (-1).GetHashCode());
        Console.WriteLine("int32 hash min: " + (int.MinValue).GetHashCode());
        Console.WriteLine("int32 hash max: " + (int.MaxValue).GetHashCode());

        // ---- 3. Int64.GetHashCode = low 32 ^ high 32 ----
        Console.WriteLine("int64 hash 0: " + (0L).GetHashCode());
        Console.WriteLine("int64 hash 1: " + (1L).GetHashCode());
        Console.WriteLine("int64 hash -1: " + (-1L).GetHashCode());
        Console.WriteLine("int64 hash probe: " + (0x0123456789ABCDEFL).GetHashCode());
        Console.WriteLine("int64 hash min: " + (long.MinValue).GetHashCode());
        Console.WriteLine("int64 hash max: " + (long.MaxValue).GetHashCode());

        // ---- 4. Double.GetHashCode: bit pattern, +0/-0 unify ----
        Console.WriteLine("double hash 0: " + (0.0).GetHashCode());
        Console.WriteLine("double hash -0 same: " + ((-0.0).GetHashCode() == (0.0).GetHashCode()));
        Console.WriteLine("double hash 1.5: " + (1.5).GetHashCode());
        Console.WriteLine("double hash -1.5: " + (-1.5).GetHashCode());
        Console.WriteLine("double hash NaN: " + double.NaN.GetHashCode());
        Console.WriteLine("double hash NaN stable: " + (double.NaN.GetHashCode() == double.NaN.GetHashCode()));
        // Exotic NaN payloads: does net48 unify them with double.NaN?
        Console.WriteLine("double hash NaN payload same: " +
            (BitConverter.Int64BitsToDouble(0x7FF8000000000001L).GetHashCode() == double.NaN.GetHashCode()));
        Console.WriteLine("double hash NaN payload2: " +
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFF0000000000000UL)).GetHashCode());

        // ---- 5. Boolean.GetHashCode ----
        Console.WriteLine("bool hash true: " + (true).GetHashCode());
        Console.WriteLine("bool hash false: " + (false).GetHashCode());

        // ---- 6. ToString dispatch on boxed values ----
        Console.WriteLine("boxed int tostring: " + ((object)42).ToString());
        Console.WriteLine("boxed neg tostring: " + ((object)(-7)).ToString());
        Console.WriteLine("boxed bool tostring: " + ((object)true).ToString());
        Console.WriteLine("string tostring self: " + ("abc".ToString() == "abc"));
        Console.WriteLine("object tostring: " + (new object()).ToString());

        // ---- 7. Object.Equals matrix ----
        Console.WriteLine("equals null null: " + object.Equals(null, null));
        Console.WriteLine("equals null obj: " + object.Equals(null, (object)1));
        Console.WriteLine("equals boxed int eq: " + object.Equals((object)5, (object)5));
        Console.WriteLine("equals boxed int ne: " + object.Equals((object)5, (object)6));
        Console.WriteLine("equals boxed int vs long: " + object.Equals((object)5, (object)5L));
        Console.WriteLine("equals boxed bool: " + object.Equals((object)true, (object)true));
        Console.WriteLine("equals boxed double: " + object.Equals((object)1.5, (object)1.5));
        Console.WriteLine("equals NaN: " + object.Equals((object)double.NaN, (object)double.NaN));
        Console.WriteLine("equals zero negzero: " + object.Equals((object)0.0, (object)-0.0));
        Console.WriteLine("equals string: " + object.Equals("abc", "a" + "bc"));
        Console.WriteLine("equals string ne: " + object.Equals("abc", "abd"));
        Console.WriteLine("equals string boxed: " + object.Equals("abc", (object)3));
        Console.WriteLine("equals obj identity: " + object.Equals((object)"x", (object)"x"));
    }
}
