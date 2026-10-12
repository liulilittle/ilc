// p-gn18fnptr1.cs -- GN-18 B1: `delegate*` (fnptr) FIELD carrier probe.
//
// B1 wires the three 0x1B field decoders to the full-width pointer carrier
// (FieldTypeKind.NativeInt / intptr_t) instead of LOUDLY rejecting them:
//   * MetadataLoader.DecodeFieldSignature   (top-level INSTANCE field)
//   * MetadataLoader.DecodeExtendedStaticType (top-level STATIC field)
//   * TypeEmitter.DecodeNestedFieldSignature (NESTED-type field)
// A fnptr field is a plain value slot (non-GC-reference: the class
// refFieldBitmap does not set its bit), exactly like an intptr_t member.
//
// The probe stores a &method function pointer into each of the three shapes,
// reads it back and CALLS through it -- the round trip must preserve the x64
// address and produce the expected integer results.
//
// Reference output (net48 oracle):
//   7     inst.cb(3,4)   -- call through a top-level INSTANCE fnptr field
//   42    inst.cb2(21)   -- call through a second instance fnptr field
//   42    s_cb(21)       -- call through a STATIC fnptr field
//   40    n.f(20)        -- call through a NESTED-type fnptr field
using System;

namespace Cases { public static class P_Gn18FnPtr1 {
    static int Add(int a, int b) => a + b;
    static int Twice(int x) => x * 2;

    public static unsafe void Main() {
        // B1: fnptr INSTANCE field on a top-level class.
        Holder inst = new Holder();
        inst.cb = &Add;
        Console.WriteLine(inst.cb(3, 4));       // 7
        inst.cb2 = &Twice;
        Console.WriteLine(inst.cb2(21));        // 42

        // B1: fnptr STATIC field on a top-level class.
        s_cb = &Twice;
        Console.WriteLine(s_cb(21));            // 42

        // B1: fnptr field on a NESTED type (Outer.Inner).
        Outer.Inner n = new Outer.Inner();
        n.f = &Twice;
        Console.WriteLine(n.f(20));             // 40
    }

    public unsafe class Holder {
        public delegate*<int, int, int> cb;
        public int pad;
        public delegate*<int, int> cb2;
    }

    public unsafe class Outer {
        public unsafe class Inner {
            public delegate*<int, int> f;
        }
    }

    public static unsafe delegate*<int, int> s_cb;
}}
