// p-gn18fnptr6.cs -- GN-18 B4: calli V:/B: value-parameter arms.
//
// B4 wires the calli StandAloneSig parameter decoders that the fixed-width
// "i"/"l"/"d"/"r:" arms did NOT cover:
//   * V: (ELEMENT_TYPE_VALUETYPE 0x11) -- a value-type parameter passed BY
//     VALUE. Roslyn encodes a non-primitive struct as 0x11 VALUETYPE, and
//     the MethodSignatureCoder encodes it "V:<FullName>". The emitted calli
//     must pass the FULL struct value (value-copy store/load), not an int32
//     truncation.
//   * B: (ELEMENT_TYPE_BYREF 0x10) -- a byref value parameter (ref/out).
//     Encoded "B:<elem>" (B:i for ref int32, B:r:<name> for ref string/
//     object). The calli must pass the ADDRESS of the slot (ref/out ABI).
//
// The probe drives both through a `delegate*` (fnptr) so the StandAloneSig
// calli emission decodes the V:/B: params:
//   * V:Pt -- a user struct VALUE param (int32 X/Y payload) passed by value;
//     the calli target type must be `int32_t(*)(Cases_Pt)`.
//   * B:r:System.String / B:r:System.Object -- byref reference params; the
//     calli passes the slot ADDRESS (Il2CppObject**).
//
// Reference output (net48 oracle):
//   42    Sum(pt)               -- V: user struct value param
//   42    Add(20, 22)           -- baseline int32 param (i arm)
//   hw    SetRef(ref string)     -- B:r:System.String byref param
//   True  SetObj(ref object)     -- B:r:System.Object byref param
using System;

namespace Cases { public static class P_Gn18FnPtr6 {
    public struct Pt { public int X; public int Y; }
    static int Sum(Pt p) => p.X + p.Y;
    static int Add(int a, int b) => a + b;
    static void SetRef(ref string s) => s = "hw";
    static void SetObj(ref object o) => o = "boxed";

    public static unsafe void Main() {
        // V: user struct value param (V:Cases.P_Gn18FnPtr6/Pt).
        Pt pt = new Pt { X = 20, Y = 22 };
        delegate*<Pt, int> sp = &Sum;
        Console.WriteLine(sp(pt));               // 42

        // Baseline int32 params (i arm).
        delegate*<int, int, int> pa = &Add;
        Console.WriteLine(pa(20, 22));           // 42

        // B:r:System.String -- a ref string parameter (byref ABI).
        string s = "";
        delegate*<ref string, void> rs = &SetRef;
        rs(ref s);
        Console.WriteLine(s);                    // hw

        // B:r:System.Object -- a ref object parameter (byref ABI).
        object o = null;
        delegate*<ref object, void> ro = &SetObj;
        ro(ref o);
        Console.WriteLine((string)o == "boxed"); // True
    }
}}
