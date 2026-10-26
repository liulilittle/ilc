//------------------------------------------------------------------------------
// crypto-range -- the net48 crypto overloads that were LOUD before this batch
// but are compile-time analyzable:
//
//   1) HMAC.ComputeHash(byte[], int, int) -- HashAlgorithm.ComputeHash's range
//      overload is inherited by KeyedHashAlgorithm/HMAC (hashalgorithm.cs:91;
//      HMAC overrides only HashCore/HashFinal/Initialize). The digest must
//      equal the one-shot ComputeHash of the same slice, and the validation
//      order/exception types must match net48.
//   2) new HMACSHA1(byte[] key, bool useManagedSha1) (hmacsha1.cs:24) -- the
//      flag only selects SHA1Managed vs SHA1CryptoServiceProvider; both give
//      the standard HMAC-SHA1 digest, so true/false must agree with the
//      single-argument ctor.
//   3) RandomNumberGenerator.GetBytes(byte[], int, int) (randomnumbergenerator.cs:53)
//      -- fills exactly the [offset, offset+count) slice and leaves the rest
//      untouched; validation exception types match net48.
//
// All printed values are deterministic (bool / exception type name / int).
// The RNG payload is random by contract, so only the untouched-slice and
// changed-slice predicates are printed.
//------------------------------------------------------------------------------
using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static string Hex(byte[] arr)
    {
        string s = "";
        for (int i = 0; i < arr.Length; i++)
        {
            int hi = arr[i] >> 4;
            int lo = arr[i] & 0x0F;
            s += (char)(hi < 10 ? '0' + hi : 'a' + hi - 10);
            s += (char)(lo < 10 ? '0' + lo : 'a' + lo - 10);
        }
        return s;
    }

    static void Show(string id, Func<string> f)
    {
        try
        {
            Console.WriteLine("[" + id + "] " + f());
        }
        catch (Exception e)
        {
            Console.WriteLine("[" + id + "] EX " + e.GetType().Name);
        }
    }

    static void Main()
    {
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] def = Encoding.UTF8.GetBytes("def");
        byte[] full = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz0123456789");
        byte[] empty = new byte[0];

        // ---- 1) HMAC range overload ----
        byte[] key = Encoding.UTF8.GetBytes("secret-key");
        HMACSHA256 h = new HMACSHA256(key);
        Console.WriteLine("hmac_full=" + (Hex(h.ComputeHash(full, 0, full.Length)) == Hex(h.ComputeHash(full))));
        Console.WriteLine("hmac_slice=" + (Hex(h.ComputeHash(full, 3, 3)) == Hex(h.ComputeHash(def))));
        Console.WriteLine("hmac_head=" + (Hex(h.ComputeHash(full, 0, 3)) == Hex(h.ComputeHash(abc))));
        Console.WriteLine("hmac_empty=" + (Hex(h.ComputeHash(full, 5, 0)) == Hex(h.ComputeHash(empty))));
        Console.WriteLine("hmac_repeat=" + (Hex(h.ComputeHash(full, 3, 3)) == Hex(h.ComputeHash(full, 3, 3))));

        // RFC 4231 test case 2: key "Jefe", data "what do ya want for nothing?"
        HMACSHA256 hj = new HMACSHA256(Encoding.UTF8.GetBytes("Jefe"));
        Console.WriteLine("hmac_rfc4231=" + (Hex(hj.ComputeHash(Encoding.UTF8.GetBytes("what do ya want for nothing?")))
            == "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"));

        // The validation calls stay inline: the receiver local's concrete type
        // (HMACSHA256) is what the transpiler uses to pick the algorithm; a
        // captured lambda/field would erase it.
        try { h.ComputeHash((byte[])null, 0, 0); Console.WriteLine("[hmac_null] NO"); }
        catch (Exception e) { Console.WriteLine("[hmac_null] EX " + e.GetType().Name); }
        try { h.ComputeHash(full, -1, 0); Console.WriteLine("[hmac_off] NO"); }
        catch (Exception e) { Console.WriteLine("[hmac_off] EX " + e.GetType().Name); }
        try { h.ComputeHash(full, 0, -1); Console.WriteLine("[hmac_cnt] NO"); }
        catch (Exception e) { Console.WriteLine("[hmac_cnt] EX " + e.GetType().Name); }
        try { h.ComputeHash(full, 1, full.Length); Console.WriteLine("[hmac_len] NO"); }
        catch (Exception e) { Console.WriteLine("[hmac_len] EX " + e.GetType().Name); }
        try { h.ComputeHash(full, full.Length, 1); Console.WriteLine("[hmac_over] NO"); }
        catch (Exception e) { Console.WriteLine("[hmac_over] EX " + e.GetType().Name); }

        // ---- 2) HMACSHA1(byte[], bool) ----
        byte[] k1 = Encoding.UTF8.GetBytes("key");
        string baseline = Hex(new HMACSHA1(k1).ComputeHash(abc));
        Console.WriteLine("h1_false=" + (Hex(new HMACSHA1(k1, false).ComputeHash(abc)) == baseline));
        Console.WriteLine("h1_true=" + (Hex(new HMACSHA1(k1, true).ComputeHash(abc)) == baseline));
        Console.WriteLine("h1_bool_eq=" + (Hex(new HMACSHA1(k1, false).ComputeHash(full))
            == Hex(new HMACSHA1(k1, true).ComputeHash(full))));
        Console.WriteLine("h1_len=" + (new HMACSHA1(k1, true).ComputeHash(abc).Length == 20));

        // ---- 3) RandomNumberGenerator.GetBytes(byte[], int, int) ----
        RandomNumberGenerator rng = new RNGCryptoServiceProvider();
        byte[] buf = new byte[16];
        for (int i = 0; i < buf.Length; i++) buf[i] = 0xAA;
        rng.GetBytes(buf, 4, 8);
        bool outside = true;
        for (int i = 0; i < 4; i++) if (buf[i] != 0xAA) outside = false;
        for (int i = 12; i < 16; i++) if (buf[i] != 0xAA) outside = false;
        Console.WriteLine("rng_outside=" + outside);
        bool changed = false;
        for (int i = 4; i < 12; i++) if (buf[i] != 0xAA) changed = true;
        Console.WriteLine("rng_changed=" + changed);
        Console.WriteLine("rng_len=" + (buf.Length == 16));

        byte[] zero = new byte[8];
        for (int i = 0; i < zero.Length; i++) zero[i] = 0x11;
        rng.GetBytes(zero, 0, 0);
        bool zeroNoop = true;
        for (int i = 0; i < zero.Length; i++) if (zero[i] != 0x11) zeroNoop = false;
        Console.WriteLine("rng_zero_noop=" + zeroNoop);

        // RNGCryptoServiceProvider-typed receiver also resolves the range form.
        RNGCryptoServiceProvider concrete = new RNGCryptoServiceProvider();
        byte[] cbuf = new byte[8];
        concrete.GetBytes(cbuf, 2, 4);
        Console.WriteLine("rng_concrete_len=" + (cbuf.Length == 8));

        Show("rng_null", delegate { rng.GetBytes((byte[])null, 0, 0); return "NO"; });
        Show("rng_off", delegate { rng.GetBytes(buf, -1, 0); return "NO"; });
        Show("rng_cnt", delegate { rng.GetBytes(buf, 0, -1); return "NO"; });
        Show("rng_len_over", delegate { rng.GetBytes(buf, 1, buf.Length); return "NO"; });
        Show("rng_off_over", delegate { rng.GetBytes(buf, buf.Length, 1); return "NO"; });
    }
}
