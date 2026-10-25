using System;
using System.Security.Cryptography;

//------------------------------------------------------------------------------
// hash-obj-surface -- the SHA/MD5 HashAlgorithm OBJECT surface: HashSize (the
// digest size in BITS) and Initialize (a no-op -- ComputeHash is stateless per
// call). Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class HashObjSurface
    {
        public static void Main()
        {
            Console.WriteLine("md5=" + MD5.Create().HashSize);
            Console.WriteLine("sha1=" + SHA1.Create().HashSize);
            Console.WriteLine("sha256=" + SHA256.Create().HashSize);
            Console.WriteLine("sha384=" + SHA384.Create().HashSize);
            Console.WriteLine("sha512=" + SHA512.Create().HashSize);

            MD5 md5 = MD5.Create();
            md5.Initialize();
            Console.WriteLine("init_md5=" + (md5.HashSize == 128));
            Console.WriteLine("init_sha256=" + (SHA256.Create().HashSize == 256));
        }
    }
}
