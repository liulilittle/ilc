//------------------------------------------------------------------------------
// p-gn11mdim0x15.cs -- GN-11 probe: 0x15-multidim generic-argument NAME fork.
// `Box<List<int>[,]>` instantiates a generic whose argument is a multidim ARRAY
// with a nested GENERICINST (0x15) element. The local-kind naming authority
// (MethodBodyReader.MultidimArrayArgSuffix) decoded the 0x15 element through
// SimpleGenericArgSuffix to the nested token "List`1_int", emitting the clone
// key "[List`1_int2]"; the three call-side producers (MethodRefResolver /
// StackFold / CodeGenerator) always return the rank-aware fallback token
// "array" -> "[array2]", so the definition-side clone and the call sites forked
// (clone Box_1__List_1_int2_ vs newobj/callvirt Box_1__array2_) -> g++ "was not
// declared".
//
// The fix aligns MethodBodyReader to the three producers ("array" for a 0x15
// element); a 0x15 multidim element is always a reference (Il2CppObject*) so
// the Box<T> object-erasure ABI is safe.
//
// A green control (Box<List<int>[]> -- SZARRAY of nested generic) rides
// alongside: the 0x15 SZARRAY element already agrees across all producers.
//
// net48 is the reference baseline; every line must print True byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PGn11Mdim0x15 {
    public static void Main() {
        // ---- 1. Box<List<int>[,]>: multidim with 0x15 element ----
        Box<List<int>[,]> b = new Box<List<int>[,]>();
        b.Set(null);
        Console.WriteLine(b.Get() == null);   // True

        // ---- 2. control: Box<List<int>[]> (SZARRAY of 0x15 element) ----
        Box<List<int>[]> b2 = new Box<List<int>[]>();
        b2.Set(null);
        Console.WriteLine(b2.Get() == null);  // True
    }
}}
