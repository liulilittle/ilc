using System;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-enc -- nb9 fix batch: Encoding destination-buffer GetBytes and the
// GetChars 5-arg charIndex boundary.
//
// Before this fix:
//   * GetBytes(string,int,int,byte[],int) / GetBytes(char[],int,int,byte[],int)
//     threw NotSupportedException at transpile (LOUD).
//   * GetChars(byte[],int,int,char[],int) with charIndex > chars.Length threw
//     ArgumentException instead of ArgumentOutOfRangeException.
//
// net48 oracle (host .NET Framework 4.x):
//   [gb_s] =4:A,0,0,0 (GetBytes("A",0,1,buf,0) -> 1 written, buf[0]=65)
//   [gb_c] =3:97,98,99
//   [gb_s_oob]=ArgumentOutOfRangeException  (charIndex out of range)
//   [gb_s_small]=ArgumentException          (destination too small)
//   [gc_idx5]=ArgumentOutOfRangeException
//   [gc_ok]=2:bc
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixEnc
    {
        static string Name(Action a)
        {
            try
            {
                a();
                return "ok";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        public static void Main()
        {
            var utf8 = Encoding.UTF8;

            byte[] buf = new byte[8];
            int n = utf8.GetBytes("A", 0, 1, buf, 0);
            Console.WriteLine("[gb_s]=" + n + ":" + buf[0] + "," + buf[1] + "," + buf[2] + "," + buf[3]);

            byte[] buf2 = new byte[8];
            int n2 = utf8.GetBytes(new[] { 'a', 'b', 'c', 'd' }, 0, 3, buf2, 0);
            Console.WriteLine("[gb_c]=" + n2 + ":" + buf2[0] + "," + buf2[1] + "," + buf2[2]);

            Console.WriteLine("[gb_s_oob]=" + Name(() => utf8.GetBytes("abc", 5, 1, new byte[8], 0)));
            Console.WriteLine("[gb_s_small]=" + Name(() => utf8.GetBytes("abcdef", 0, 6, new byte[2], 0)));

            Console.WriteLine("[gc_idx5]=" + Name(() => utf8.GetChars(new byte[0], 0, 0, new char[4], 5)));
            Console.WriteLine("[gc_ok]=" + ProbeChars(utf8));
        }

        static string ProbeChars(Encoding utf8)
        {
            char[] dst = new char[4];
            int written = utf8.GetChars(new byte[] { 97, 98, 99 }, 1, 2, dst, 0);
            return written + ":" + new string(dst, 0, written);
        }
    }
}
