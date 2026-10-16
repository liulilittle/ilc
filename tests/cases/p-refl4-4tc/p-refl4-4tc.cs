//------------------------------------------------------------------------------
// p-refl4-4tc.cs -- refl4 item 4 net48 oracle probe: Type.GetTypeCode(Type)
// enum-UNDERLYING folding (the enum-deficit B7) + ConstructorInfo.get_Name
// (C1 -- the "<FullName>::.ctor" member value decodes to ".ctor").
//
//   * Type.GetTypeCode(typeof(E)) for USER enums with various underlying
//     types (byte/short/int/long) -- net48 returns the UNDERLYING TypeCode
//     (Byte=6 Int16=7 Int32=9 Int64=11), NOT TypeCode.Object (the pre-refl4
//     fold's `_ => 1` fallback). Comparison against the NUMERIC constant
//     keeps the probe byte-stable and avoids allocating a BCL enum
//     (System.TypeCode) local at all (the COMPILE-BROKEN this batch fixes:
//     a `System.TypeCode` BCL enum local has no generated C++ body -- the
//     fold result must be usable DIRECTLY as an int constant).
//   * ConstructorInfo.get_Name -- the ctor member VALUE is the disambiguated
//     "<FullName>::.ctor"; net48 ConstructorInfo.Name is ".ctor". The fold
//     family must decode the member-value's ".ctor" key specially (without
//     breaking the Invoke dependency on the disambiguated value).
//
// Records are bool records (byte-stable).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public enum TcByte : byte { A = 1 }
    public enum TcShort : short { A = 2 }
    public enum TcIntEnum { A = 3 }
    public enum TcLong : long { A = 4 }

    public class TcCtor { public TcCtor() { } public TcCtor(int x) { } }

    public static class PRefl4Tc
    {
        public static void Main()
        {
            // ---- enum underlying TypeCode (B7) ----
            // The (int) cast keeps the RHS a plain int constant -- no BCL
            // System.TypeCode enum local is allocated anywhere.
            Console.WriteLine("[tc.byte]=" + ((int)Type.GetTypeCode(typeof(TcByte)) == 6));
            Console.WriteLine("[tc.short]=" + ((int)Type.GetTypeCode(typeof(TcShort)) == 7));
            Console.WriteLine("[tc.int]=" + ((int)Type.GetTypeCode(typeof(TcIntEnum)) == 9));
            Console.WriteLine("[tc.long]=" + ((int)Type.GetTypeCode(typeof(TcLong)) == 11));
            // non-enum regression anchor: a CLASS still folds to Object (1).
            Console.WriteLine("[tc.class]=" + ((int)Type.GetTypeCode(typeof(TcCtor)) == 1));

            // ---- ConstructorInfo.get_Name (C1: ".ctor" decode) ----
            var c0 = typeof(TcCtor).GetConstructor(new Type[0]);
            Console.WriteLine("[cn.p0.name]=" + (c0.Name == ".ctor"));
            Console.WriteLine("[cn.p0.nonnull]=" + (c0.Name != ""));
            var c1 = typeof(TcCtor).GetConstructor(new Type[] { typeof(int) });
            Console.WriteLine("[cn.p1.name]=" + (c1.Name == ".ctor"));
            // decoding must NOT disturb Invoke (the member VALUE stays the
            // disambiguated "<FullName>::.ctor" -- Invoke still creates).
            Console.WriteLine("[cn.invoke]=" + (c1.Invoke(new object[] { 5 }) != null));
        }
    }
}