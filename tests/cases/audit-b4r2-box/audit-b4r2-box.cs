using System;
using System.Collections.Generic;
using System.Linq;

//------------------------------------------------------------------------------
// audit-b4r2-box -- R1 regression guard for the D2/D8/D9 value-slot logic.
//
// D8 tightened the unbox whitelists per cast code (unbox:i only accepts
// int32-family kinds, unbox:l/ul only int64) and D9 made the resolver's
// unrecognized fallback "unknown" (so an unidentified expression is NOT assumed
// to be a value slot). A REAL boxed object must therefore still take the unbox
// helper path -- never `static_cast<int64_t>(Il2CppObject*)`.
//
// net48 oracle:
//   [box_long_tostring]=42
//   [box_long_cast]=42
//   [box_uint_tostring]=42
//   [box_uint_cast]=42
//   [box_ulong_tostring]=42
//   [box_ulong_cast]=42
//   [box_int_tostring]=42
//   [box_int_cast]=42
//   [box_field]=7
//   [box_array]=8
//   [box_param]=9
//   [box_cast]=10
//   [box_method]=1234567890123
//   [box_method_unbox]=1234567890123
//   [box_objparam_int]=11
//   [box_objparam_long]=12
//------------------------------------------------------------------------------

internal static class P
{
    private static object _staticObj = 42;

    private static object Id(object o) { return o; }

    private static int TakeObj(object o) { return (int)o; }

    private static long TakeObjLong(object o) { return (long)o; }

    private static void Main()
    {
        // Boxed int64 through a local: ToString and unbox.
        object oLong = 42L;
        Console.WriteLine("[box_long_tostring]=" + oLong.ToString());
        Console.WriteLine("[box_long_cast]=" + ((long)oLong).ToString());

        object oUint = 42u;
        Console.WriteLine("[box_uint_tostring]=" + oUint.ToString());
        Console.WriteLine("[box_uint_cast]=" + ((uint)oUint).ToString());

        object oUlong = 42UL;
        Console.WriteLine("[box_ulong_tostring]=" + oUlong.ToString());
        Console.WriteLine("[box_ulong_cast]=" + ((ulong)oUlong).ToString());

        object oInt = 42;
        Console.WriteLine("[box_int_tostring]=" + oInt.ToString());
        Console.WriteLine("[box_int_cast]=" + ((int)oInt).ToString());

        // Static field holding a boxed value.
        Console.WriteLine("[box_field]=" + ((int)_staticObj).ToString());

        // object[] element.
        object[] arr = { (object)8 };
        Console.WriteLine("[box_array]=" + ((int)arr[0]).ToString());

        // object parameter.
        Console.WriteLine("[box_param]=" + TakeObj((object)9).ToString());

        // Explicit cast to object then unbox.
        object oCast = (object)10;
        Console.WriteLine("[box_cast]=" + ((int)oCast).ToString());

        // Method returning object, chained ToString and unbox.
        Console.WriteLine("[box_method]=" + Id(1234567890123L).ToString());
        Console.WriteLine("[box_method_unbox]=" + ((long)Id(1234567890123L)).ToString());

        // object-typed parameter carrying different boxed widths.
        Console.WriteLine("[box_objparam_int]=" + TakeObj((object)11).ToString());
        Console.WriteLine("[box_objparam_long]=" + TakeObjLong((object)12L).ToString());
    }
}
