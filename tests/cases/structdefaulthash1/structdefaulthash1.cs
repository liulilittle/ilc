using System;

namespace Cases
{
    // The net48 default GetHashCode of un-overridden structs.
    //
    // net48:  hash = munge(typeID) ^ fieldImage
    //   munge(id)  = id * 711650207 + 2506965631U      (32-bit wrap)
    //   typeID     = the ONE lazy ValueType typeID, assigned on the FIRST
    //                GetHashCode call for that type, from 3 stepping by 2 --
    //                so it follows HASH order, NOT declaration order.
    //   fieldImage = fast path (tightly packed, no GC refs): XOR of every
    //                complete 4-byte word of the PACKED image (a 1..3-byte tail
    //                is dropped); otherwise the first non-null field's own
    //                4-byte words.
    //
    // Every value below was measured on real .NET Framework 4.8.

    // Declared Alpha -> Beta -> Gamma, hashed Gamma -> Alpha -> Beta.
    public struct Alpha { public int a; }
    public struct Beta  { public int a; }
    public struct Gamma { public int a; }

    // 2-int struct: the two field words XOR, so (1,0) and (0,1) collide.
    public struct TwoInt { public int a; public int b; }

    // Field-width folding: a short field lands in the HIGH half of its 4-byte
    // word (net48 packs shorts at their true width), and a byte field folds
    // away entirely inside a 4-byte word.
    public struct TwoShort  { public short a; public short b; }
    public struct FourByte  { public byte a; public byte b; public byte c; public byte d; }
    public struct OneLong   { public long l; }

    // A struct with interior padding takes net48's REGULAR path (first field).
    public struct ShortInt  { public short s; public int i; }

    public static class StructDefaultHash1
    {
        static void W(string k, int v) { Console.WriteLine("[" + k + "]=" + v); }

        public static void Main()
        {
            // ---- declaration order vs HASH order ---------------------------
            // The typeID is handed out in first-hash order: Gamma=3, Alpha=5,
            // Beta=7. A declaration-order model would give Alpha=3.
            W("decl.alpha", new Alpha { a = 0 }.GetHashCode());
            W("decl.beta", new Beta { a = 0 }.GetHashCode());
            W("decl.gamma", new Gamma { a = 0 }.GetHashCode());

            var g = (new Gamma { a = 0 }, 0);
            var al = (new Alpha { a = 0 }, 0);
            var be = (new Beta { a = 0 }, 0);
            W("hash.gamma", g.GetHashCode());
            W("hash.alpha", al.GetHashCode());
            W("hash.beta", be.GetHashCode());
            W("hash.gamma2", g.GetHashCode());

            // ---- 2-int XOR -------------------------------------------------
            var t10 = (new TwoInt { a = 1, b = 0 }, 0);
            var t01 = (new TwoInt { a = 0, b = 1 }, 0);
            var t00 = (new TwoInt { a = 0, b = 0 }, 0);
            W("twoint.1_0", t10.GetHashCode());
            W("twoint.0_1", t01.GetHashCode());
            W("twoint.eq", t10.GetHashCode() == t01.GetHashCode() ? 1 : 0);
            W("twoint.0_0", t00.GetHashCode());

            // ---- field-width folding --------------------------------------
            W("twoshort.1_0", (new TwoShort { a = 1, b = 0 }, 0).GetHashCode());
            W("twoshort.0_1", (new TwoShort { a = 0, b = 1 }, 0).GetHashCode());
            W("fourbyte.b0", (new FourByte { a = 1 }, 0).GetHashCode());
            W("fourbyte.b3", (new FourByte { d = 1 }, 0).GetHashCode());
            W("onelong.lo", (new OneLong { l = 1 }, 0).GetHashCode());
            W("onelong.hi", (new OneLong { l = 0x100000000L }, 0).GetHashCode());
            W("onelong.eq", (new OneLong { l = 1 }, 0).GetHashCode()
                            == (new OneLong { l = 0x100000000L }, 0).GetHashCode() ? 1 : 0);

            // ---- interior padding -> the REGULAR path ----------------------
            W("shortint.s", (new ShortInt { s = 1 }, 0).GetHashCode());
            W("shortint.i", (new ShortInt { i = 1 }, 0).GetHashCode());

            // ---- plain path == tuple path (ONE counter) --------------------
            var direct = new TwoInt { a = 3, b = 4 };
            W("plain.twoint", direct.GetHashCode());
            W("tuple.twoint", (new TwoInt { a = 3, b = 4 }, 0).GetHashCode());

            // ---- the ValueTuple element hash itself ------------------------
            W("vt.12", (1, 2).GetHashCode());
            W("vt.21", (2, 1).GetHashCode());
            W("vt.00", (0, 0).GetHashCode());
        }
    }
}
