// p-gn18fnptr2.cs -- GN-18 B2: USER-method `delegate*` (fnptr) RETURN probe.
//
// B2 wires a user method that RETURNS a function pointer (ELEMENT_TYPE_FNPTR
// 0x1B) through ReturnTypeCpp -> the full-width void* carrier (instead of the
// LOUD "return type FunctionPointer is not supported by M3" reject at
// CodeGenerator.cs:10433). The returned value is the model's void* "p" slot;
// the calli site casts it to the target signature and calls through it.
//
// Reference output (net48 oracle):
//   42     GetInc()(41)           -- call through the fnptr RETURNED by a method
//   7      GetAdd()(3,4)          -- call through a 2-arg fnptr return
//   43     r = GetInc(); r(42)    -- fnptr return bound to a LOCAL
//   86     r(42) + r(42)          -- same fnptr called twice
//   True   GetInc() == GetInc()   -- fnptr identity (both &Inc)
//   False  GetInc() == t          -- &Inc vs &Twice differ
//   True   GetInc() == GetInc()   -- fnptr identity again
using System;

namespace Cases { public static class P_Gn18FnPtr2 {
    static int Inc(int x) => x + 1;
    static int Add(int a, int b) => a + b;
    static int Twice(int x) => x * 2;

    static unsafe delegate*<int, int> GetInc() => &Inc;
    static unsafe delegate*<int, int, int> GetAdd() => &Add;

    public static unsafe void Main() {
        Console.WriteLine(GetInc()(41));                 // 42
        Console.WriteLine(GetAdd()(3, 4));               // 7
        delegate*<int, int> r = GetInc();
        Console.WriteLine(r(42));                        // 43
        Console.WriteLine(r(42) + r(42));                // 86
        Console.WriteLine(GetInc() == GetInc());         // True
        delegate*<int, int> t = &Twice;
        Console.WriteLine(GetInc() == t);                // False
        Console.WriteLine(GetInc() == GetInc());         // True
    }
}}