// dvprobe4.cs -- delegate Invoke with VALUE-TYPE generic args of ASSORTED
// SIZES (the MS x64 aggregate ABI differs by size/alignment) plus a generic
// method instantiated over a struct.
//
// The multicast helper names one C++ type per signature and lets the compiler
// apply the platform aggregate ABI, so these shapes must all agree between the
// helper's function-pointer cast and the leaf declaration:
//   * 1-byte, 4-byte, 8-byte  -> register-passed aggregates
//   * 16-byte                  -> hidden by-ref slot / sret
//   * 24-byte and 32-byte      -> hidden by-ref slot / sret
//   * a generic method Identity<T>(T) closed over a struct through a delegate
using System;

namespace Cases
{
    public struct S1 { public byte V; public S1(byte v) { V = v; } }
    public struct S4 { public int V; public S4(int v) { V = v; } }
    public struct S8 { public long V; public S8(long v) { V = v; } }
    public struct S16 { public long A; public long B; public S16(long a, long b) { A = a; B = b; } }
    public struct S24 { public long A; public long B; public long C; public S24(long a, long b, long c) { A = a; B = b; C = c; } }
    public struct S32 { public long A; public long B; public long C; public long D;
                        public S32(long a, long b, long c, long d) { A = a; B = b; C = c; D = d; } }

    public static class DelegateSized
    {
        private static S1 Bump1(S1 s) { return new S1((byte)(s.V + 1)); }
        private static S1 Bump1b(S1 s) { return new S1((byte)(s.V + 10)); }
        private static S4 Bump4(S4 s) { return new S4(s.V + 1); }
        private static S4 Bump4b(S4 s) { return new S4(s.V + 10); }
        private static S8 Bump8(S8 s) { return new S8(s.V + 1); }
        private static S8 Bump8b(S8 s) { return new S8(s.V + 10); }
        private static S16 Bump16(S16 s) { return new S16(s.A + 1, s.B + 1); }
        private static S16 Bump16b(S16 s) { return new S16(s.A + 10, s.B + 10); }
        private static S24 Bump24(S24 s) { return new S24(s.A + 1, s.B + 1, s.C + 1); }
        private static S24 Bump24b(S24 s) { return new S24(s.A + 10, s.B + 10, s.C + 10); }
        private static S32 Bump32(S32 s) { return new S32(s.A + 1, s.B + 1, s.C + 1, s.D + 1); }
        private static S32 Bump32b(S32 s) { return new S32(s.A + 10, s.B + 10, s.C + 10, s.D + 10); }

        private static T Identity<T>(T x) { return x; }

        public static void Main()
        {
            Func<S1, S1> f1 = Bump1;
            Console.WriteLine("z_s1_direct=" + f1(new S1(1)).V);
            f1 += Bump1b;
            Console.WriteLine("z_s1_multi=" + f1(new S1(1)).V);

            Func<S4, S4> f4 = Bump4;
            Console.WriteLine("z_s4_direct=" + f4(new S4(1)).V);
            f4 += Bump4b;
            Console.WriteLine("z_s4_multi=" + f4(new S4(1)).V);

            Func<S8, S8> f8 = Bump8;
            Console.WriteLine("z_s8_direct=" + f8(new S8(1)).V);
            f8 += Bump8b;
            Console.WriteLine("z_s8_multi=" + f8(new S8(1)).V);

            Func<S16, S16> f16 = Bump16;
            S16 a16 = f16(new S16(1, 2));
            Console.WriteLine("z_s16_direct=" + a16.A + "/" + a16.B);
            f16 += Bump16b;
            S16 b16 = f16(new S16(1, 2));
            Console.WriteLine("z_s16_multi=" + b16.A + "/" + b16.B);

            Func<S24, S24> f24 = Bump24;
            S24 a24 = f24(new S24(1, 2, 3));
            Console.WriteLine("z_s24_direct=" + a24.A + "/" + a24.B + "/" + a24.C);
            f24 += Bump24b;
            S24 b24 = f24(new S24(1, 2, 3));
            Console.WriteLine("z_s24_multi=" + b24.A + "/" + b24.B + "/" + b24.C);

            Func<S32, S32> f32 = Bump32;
            S32 a32 = f32(new S32(1, 2, 3, 4));
            Console.WriteLine("z_s32_direct=" + a32.A + "/" + a32.B + "/" + a32.C + "/" + a32.D);
            f32 += Bump32b;
            S32 b32 = f32(new S32(1, 2, 3, 4));
            Console.WriteLine("z_s32_multi=" + b32.A + "/" + b32.B + "/" + b32.C + "/" + b32.D);

            // ---- generic method closed over a struct, used as a delegate ----
            Func<S16, S16> gid = Identity<S16>;
            S16 c16 = gid(new S16(7, 8));
            Console.WriteLine("z_generic_identity=" + c16.A + "/" + c16.B);

            Func<S24, S24> gid2 = Identity<S24>;
            S24 c24 = gid2(new S24(9, 10, 11));
            Console.WriteLine("z_generic_identity24=" + c24.A + "/" + c24.B + "/" + c24.C);
        }
    }
}
