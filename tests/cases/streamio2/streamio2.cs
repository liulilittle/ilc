//------------------------------------------------------------------------------
// streamio2.cs -- impl-io2: the statically-analyzable StreamReader
// (Stream|String, Encoding[, bool[, int[, bool]]]) ctor family + the generic
// Marshal.SizeOf<T>() primitive surface (net48 oracle, byte-for-byte).
//
// The Encoding ctor shapes are compile-time known (MethodMap.StreamIO.cs used
// to reject them LOUD); the runtime selects the text encoding from the managed
// Encoding object. Marshal.SizeOf<T>() is likewise compile-time known (the
// generic argument name) and routes through the runtime's net48 layout registry.
//
// Only ASCII labels are printed, so the diff is console-encoding stable.
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Cases { public static class StreamIO2 {
    const string file = "streamio2_tmp.bin";

    public static void Main()
    {
        File.Delete(file);

        // ============ 1. StreamReader(Stream, Encoding) -- UTF-8 ============
        File.WriteAllBytes(file, new byte[] { (byte)'h', (byte)'i', 10, (byte)'b', (byte)'y', (byte)'e', 10 });
        FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fs, Encoding.UTF8);
        Console.WriteLine("r1=" + sr.ReadLine());
        Console.WriteLine("r2=" + sr.ReadLine());
        Console.WriteLine("r3=" + (sr.ReadLine() == null));
        sr.Close();

        // ============ 2. StreamReader(Stream, Encoding) -- UTF-16LE =========
        File.WriteAllBytes(file, new byte[] { (byte)'A', 0, (byte)'B', 0, 10, 0, (byte)'C', 0, 10, 0 });
        FileStream fs2 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr2 = new StreamReader(fs2, Encoding.Unicode);
        Console.WriteLine("r4=" + sr2.ReadLine());
        Console.WriteLine("r5=" + sr2.ReadLine());
        sr2.Close();

        // ============ 3. StreamReader(Stream, Encoding) -- UTF-16BE =========
        File.WriteAllBytes(file, new byte[] { 0, (byte)'E', 0, (byte)'F', 0, 10, 0, (byte)'G', 0, 10 });
        FileStream fs3 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr3 = new StreamReader(fs3, Encoding.BigEndianUnicode);
        Console.WriteLine("r6=" + sr3.ReadLine());
        Console.WriteLine("r7=" + sr3.ReadLine());
        sr3.Close();

        // ============ 4. StreamReader(string, Encoding) -- UTF-8 ============
        File.WriteAllBytes(file, new byte[] { (byte)'z', (byte)'z', 10, (byte)'q', 10 });
        StreamReader sr4 = new StreamReader(file, Encoding.UTF8);
        Console.WriteLine("r8=" + sr4.ReadLine());
        Console.WriteLine("r9=" + sr4.ReadLine());
        sr4.Close();

        // ============ 5. StreamReader(Stream, Encoding, bool) -- UTF-8 ======
        File.WriteAllBytes(file, new byte[] { (byte)'k', (byte)'k', 10 });
        FileStream fs5 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr5 = new StreamReader(fs5, Encoding.UTF8, false);
        Console.WriteLine("r10=" + sr5.ReadLine());
        sr5.Close();

        // ============ 6. Marshal.SizeOf<T>() primitives ====================
        Console.WriteLine("so_i4=" + Marshal.SizeOf<int>());
        Console.WriteLine("so_i8=" + Marshal.SizeOf<long>());
        Console.WriteLine("so_u2=" + Marshal.SizeOf<ushort>());
        Console.WriteLine("so_d=" + Marshal.SizeOf<double>());
        Console.WriteLine("so_ip=" + (Marshal.SizeOf<IntPtr>() == IntPtr.Size));

        File.Delete(file);
    }
}}
