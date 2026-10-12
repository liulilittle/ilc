// p-gn18fnptr5.cs -- GN-18 B5: `delegate*` (fnptr) ARRAY ELEMENT probe.
//
// B5 wires a SZARRAY of function pointers (`delegate*<...>[]` --
// ELEMENT_TYPE_ARRAY heap with FNPTR 0x1B elements). The element is a
// non-reference pointer-sized slot (8 bytes on x64, void* carrier), so:
//   * ArrayElementType (newarr stride) must size it PointerSize, NON-ref
//     (not the LOUD "everything else" reject, and not a reference stride)
//   * ldelem reads the element into a void* "p" slot and the calli rides it
//   * stelem stores the fnptr value into the slot
//
// Reference output (net48 oracle):
//   42    arr[0](41)      -- call through a stored fnptr array element
//   7     arr[1](3,4)     -- call through a second (2-arg) element
//   43    r = arr[0]; r(42) -- element loaded into a local, then called
using System;

namespace Cases { public static class P_Gn18FnPtr5 {
    static int Inc(int x) => x + 1;
    static int Add(int a, int b) => a + b;

    public static unsafe void Main() {
        delegate*<int, int>[] arr = new delegate*<int, int>[2];
        arr[0] = &Inc;
        delegate*<int, int, int>[] arr2 = new delegate*<int, int, int>[1];
        arr2[0] = &Add;
        Console.WriteLine(arr[0](41));                   // 42
        Console.WriteLine(arr2[0](3, 4));                // 7
        delegate*<int, int> r = arr[0];
        Console.WriteLine(r(42));                        // 43
        Console.WriteLine(arr[1] == null);               // True (zero-init)
        Console.WriteLine(arr[0](1) + arr[0](1));        // 4
    }
}}