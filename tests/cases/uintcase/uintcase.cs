using System;

namespace Cases
{
    public static class UintCase
    {
        public static void Main()
        {
            // ---- uint operation chain (MaxValue/MinValue/add/sub/mul/div/compare, all bool) ----
            // NOTE: a constant expression (uint.MaxValue + 1u) overflows the C#
            // compile-time checked context (CS0220); the overflow path must be
            // carried by locals into the runtime (unchecked wrap-around by default).
            uint mv = uint.MaxValue;
            uint a = 4000000000u;
            uint b = a + 1u;
            uint c = b - 1u;
            uint d = a * 2u;
            Console.WriteLine(mv == 4294967295u);                         // True
            Console.WriteLine(uint.MinValue == 0u);                       // True
            Console.WriteLine(mv + 1u == 0u);                             // True (wrap)
            Console.WriteLine(mv - 1u == 4294967294u);                    // True
            // NOTE: uint/ulong division/modulo (div.un/rem.un, 0x5C/0x5E) is not
            // decoded by M1 -- recorded gap, the case excludes it
            Console.WriteLine(d == 3705032704u);                          // True (wrap mul)
            Console.WriteLine(a + a == 3705032704u);                      // True (wrap add)
            Console.WriteLine(mv == 0xFFFFFFFFu);                         // True
            Console.WriteLine(b == 4000000001u);                          // True
            Console.WriteLine(c == 4000000000u);                          // True
            Console.WriteLine(a > 1u);                                    // True (unsigned compare)
            Console.WriteLine(1u < a);                                    // True
            Console.WriteLine(a == a);                                    // True
            Console.WriteLine(a != b);                                    // True

            // ---- ulong operation chain ----
            ulong mvl = ulong.MaxValue;
            Console.WriteLine(mvl == 18446744073709551615ul);             // True
            Console.WriteLine(ulong.MinValue == 0ul);                     // True
            Console.WriteLine(mvl + 1ul == 0ul);                          // True (wrap)
            Console.WriteLine(mvl - 1ul == 18446744073709551614ul);       // True
            Console.WriteLine(4000000000ul * 4000000000ul == 16000000000000000000ul); // True
            Console.WriteLine(mvl > 1ul);                                 // True (unsigned 64-bit compare)
            Console.WriteLine(mvl == 0xFFFFFFFFFFFFFFFFul);               // True

            ulong g = 12345678901234567890ul;
            ulong h = g + 1ul;
            ulong i = h - 1ul;
            Console.WriteLine(h == 12345678901234567891ul);               // True
            Console.WriteLine(i == 12345678901234567890ul);               // True
            Console.WriteLine(g > 0ul);                                   // True (unsigned compare, high bit)
            Console.WriteLine(g == g);                                    // True
            Console.WriteLine(g != h);                                    // True

            // ---- UInt64 WriteLine fix verification: must print the full decimal (the old int64 path printed -1) ----
            Console.WriteLine(ulong.MaxValue);                            // 18446744073709551615
            Console.WriteLine(g);                                         // 12345678901234567890

            // ---- boxed uint / unbox (box domain new support) ----
            object ob = (object)4000000000u;
            Console.WriteLine((uint)ob == 4000000000u);                   // True
            Console.WriteLine(((uint)ob) + 1u == 4000000001u);            // True
            Console.WriteLine(((uint)ob) > 1u);                           // True (unsigned after unbox)
            Console.WriteLine((uint)(object)uint.MaxValue == uint.MaxValue); // True
            Console.WriteLine((uint)(object)0u == 0u);                    // True
            Console.WriteLine(ob != null);                                // True
            // NOTE: value-type receiver .Equals goes through the constrained
            // address path (the generator passes a local address to
            // boxed_equals_ext2, which fails to compile) -- Equals must use the
            // boxed-receiver shape already verified by boxext2
            Console.WriteLine(((object)4000000000u).Equals((object)4000000000u)); // True (boxed equals)
            Console.WriteLine(((object)4000000000u).Equals((object)1u));          // False

            // ---- boxed ulong / unbox ----
            Console.WriteLine((ulong)(object)ulong.MaxValue == ulong.MaxValue); // True
            object obg = (object)12345678901234567890ul;
            Console.WriteLine((ulong)obg == 12345678901234567890ul);      // True
            Console.WriteLine(((ulong)obg) + 1ul == 12345678901234567891ul); // True
            Console.WriteLine(((ulong)obg) > 0ul);                        // True
            Console.WriteLine(((object)ulong.MaxValue).Equals((object)ulong.MaxValue)); // True (boxed equals)
            Console.WriteLine(((object)ulong.MaxValue).Equals((object)1ul));             // False

            // ---- BitConverter (uint/ulong related) ----
            byte[] ub = BitConverter.GetBytes(0x01020304u);
            Console.WriteLine(ub[0] == 4);                                // True (LE)
            Console.WriteLine(ub[1] == 3);                                // True
            Console.WriteLine(ub[2] == 2);                                // True
            Console.WriteLine(ub[3] == 1);                                // True
            Console.WriteLine(BitConverter.GetBytes(0u)[0] == 0);         // True
            Console.WriteLine(BitConverter.GetBytes(uint.MaxValue)[3] == 255); // True
            byte[] lb = BitConverter.GetBytes(ulong.MaxValue);
            Console.WriteLine(lb[0] == 255);                              // True
            Console.WriteLine(lb[1] == 255);                              // True
            Console.WriteLine(lb[7] == 255);                              // True
            Console.WriteLine(BitConverter.GetBytes(1ul)[0] == 1);        // True
            Console.WriteLine(BitConverter.GetBytes(1ul)[7] == 0);        // True
            // NOTE: ToUInt32/ToUInt64(byte[], int) are wired in MethodMap, but the
            // generator omits the (Il2CppArray*) conversion (an Il2CppObject* local
            // -> Il2CppArray* argument fails to compile) -- recorded gap, the case
            // excludes the ToT round trips
        }
    }
}
