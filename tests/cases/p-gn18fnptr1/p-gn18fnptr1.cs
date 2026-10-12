// p-gn18fnptr1.cs -- GN-18 B1: `delegate*` (fnptr) FIELD carrier probe.
//
// B1 wires the three 0x1B field decoders to the full-width pointer carrier
// (FieldTypeKind.NativeInt) instead of LOUDLY rejecting them:
//   * MetadataLoader.DecodeFieldSignature   (top-level INSTANCE field)
//   * MetadataLoader.DecodeExtendedStaticType (top-level STATIC field)
//   * TypeEmitter.DecodeNestedFieldSignature (NESTED-type field)
// The fnptr field is a plain value slot (non-GC-reference: the class
// refFieldBitmap does not set its bit), emitted as a void* carrier that
// round-trips the x64 function address.
//
// The probe stores a &method function pointer into each of the three shapes,
// reads it back and CALLS through it. The helpers (UseHolder/UseHolder2/
// UseInner) force the object through a REAL memory round trip (the receiver
// escapes the dead-object fold), and UseHolder2/UseStatic copy a field load
// into a `delegate*` LOCAL first (exercising the "p" local-kind seed).
//
// Reference output (net48 oracle):
//   7     inst.cb(3,4)     -- call through an INSTANCE fnptr field (direct)
//   42    inst.cb2(21)     -- call through a second instance fnptr field
//   7     UseHolder(inst)  -- instance field load + calli in a helper
//   42    UseHolder2(inst) -- instance field load into a LOCAL, then calli
//   42    s_cb(21)         -- call through a STATIC fnptr field (direct)
//   42    UseStatic()      -- static field load into a LOCAL, then calli
//   40    UseInner(n)      -- call through a NESTED-type fnptr field
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
        Console.WriteLine(UseHolder(inst));     // 7
        Console.WriteLine(UseHolder2(inst));    // 42

        // B1: fnptr STATIC field on a top-level class.
        s_cb = &Twice;
        Console.WriteLine(s_cb(21));            // 42
        Console.WriteLine(UseStatic());         // 42

        // B1: fnptr field on a NESTED type (Outer.Inner).
        Outer.Inner n = new Outer.Inner();
        n.f = &Twice;
        Console.WriteLine(UseInner(n));         // 40
    }

    static unsafe int UseHolder(Holder h) => h.cb(3, 4);      // 7
    static unsafe int UseHolder2(Holder h) { delegate*<int, int> p = h.cb2; return p(21); }  // 42
    static unsafe int UseStatic() { delegate*<int, int> p = s_cb; return p(21); }             // 42
    static unsafe int UseInner(Outer.Inner n) => n.f(20);     // 40

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
