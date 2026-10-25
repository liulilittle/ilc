using System;
using System.Security.Cryptography;
using System.Text;

//------------------------------------------------------------------------------
// hmac-key -- HMAC Key property + keyless ctor (net48 HMAC.Key / InitializeKey).
//
// net48 HMAC.Key getter returns a CLONE of the stored key (never the stored
// array identity); the setter runs InitializeKey (null -> NullReferenceException,
// a key longer than the block size is hashed first, otherwise it is cloned);
// the keyless HMACSHAx() ctor generates a random key of the block size. This
// pins the surface end to end against the net48 oracle (only lengths / hex of
// the deterministic keyed case are compared -- the keyless key is random).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class HmacKey
    {
        public static void Main()
        {
            // ---- keyed getter: clone semantics + payload ----
            byte[] key = Encoding.UTF8.GetBytes("key");
            var h = new HMACSHA256(key);
            Console.WriteLine("getLen=" + h.Key.Length);
            Console.WriteLine("getKeySame=" + object.ReferenceEquals(h.Key, h.Key));
            Console.WriteLine("getKey=" + Hex(h.Key));

            // ---- setter: clone semantics ----
            byte[] k2 = Encoding.UTF8.GetBytes("newkey");
            h.Key = k2;
            Console.WriteLine("setLen=" + h.Key.Length);
            Console.WriteLine("setSame=" + object.ReferenceEquals(h.Key, k2));
            Console.WriteLine("setKey=" + Hex(h.Key));

            // ---- setter null -> NullReferenceException ----
            try { h.Key = null; Console.WriteLine("setNull=NO"); }
            catch (Exception e) { Console.WriteLine("setNull=" + e.GetType().Name); }

            // ---- key longer than the block size is hashed first ----
            byte[] longKey = Encoding.UTF8.GetBytes(new string('a', 80));
            var hl = new HMACSHA256(longKey);
            Console.WriteLine("longLen=" + hl.Key.Length);
            Console.WriteLine("longKey=" + Hex(hl.Key));

            // ---- keyless ctor: block-size random key (length only) ----
            Console.WriteLine("h1Len=" + new HMACSHA1().Key.Length);
            Console.WriteLine("h2Len=" + new HMACSHA256().Key.Length);
            Console.WriteLine("h3Len=" + new HMACSHA512().Key.Length);

            // ---- ComputeHash determinism for a fixed key ----
            byte[] a = Encoding.UTF8.GetBytes("abc");
            Console.WriteLine("det=" + (Hex(h.ComputeHash(a)) == Hex(h.ComputeHash(a))));
            Console.WriteLine("digestLen=" + h.ComputeHash(a).Length);
        }

        private static string Hex(byte[] arr)
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
    }
}
