//------------------------------------------------------------------------------
// deflatecase.cs -- System.IO.Compression.DeflateStream e2e oracle. The
// transpiler's deflate domain (il2cpp_deflate.h) wires the DeflateStream
// ctor(Stream, CompressionMode), Write/Read and Close. Every output is a
// deterministic bool; expected.txt is generated from a net48 reference run.
//
// net48 ownership: closing a DeflateStream closes the underlying stream, so
// the compressed bytes are extracted via MemoryStream.ToArray() (which works
// on a closed stream) and decompressed through a FRESH MemoryStream.
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;

class DeflateCase
{
    static void Main()
    {
        // ---- 1. Compress then decompress round-trip ----
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var ms = new MemoryStream();
        var ds = new DeflateStream(ms, CompressionMode.Compress);
        ds.Write(data, 0, data.Length);
        ds.Close();
        byte[] compressed = ms.ToArray();
        var ms2 = new MemoryStream();
        ms2.Write(compressed, 0, compressed.Length);
        ms2.Position = 0;
        var ds2 = new DeflateStream(ms2, CompressionMode.Decompress);
        byte[] result = new byte[data.Length];
        int n = ds2.Read(result, 0, result.Length);
        Console.WriteLine(n == data.Length);
        bool same = true;
        for (int i = 0; i < data.Length; i++) { if (result[i] != data[i]) same = false; }
        Console.WriteLine(same);

        // ---- 2. A larger round-trip (repeated pattern) ----
        byte[] big = new byte[1000];
        for (int i = 0; i < big.Length; i++) { big[i] = (byte)(i % 251); }
        var ms3 = new MemoryStream();
        var ds3 = new DeflateStream(ms3, CompressionMode.Compress);
        ds3.Write(big, 0, big.Length);
        ds3.Close();
        byte[] compressed2 = ms3.ToArray();
        var ms4 = new MemoryStream();
        ms4.Write(compressed2, 0, compressed2.Length);
        ms4.Position = 0;
        var ds4 = new DeflateStream(ms4, CompressionMode.Decompress);
        byte[] result2 = new byte[big.Length];
        int n2 = ds4.Read(result2, 0, result2.Length);
        Console.WriteLine(n2 == big.Length);
        bool same2 = true;
        for (int i = 0; i < big.Length; i++) { if (result2[i] != big[i]) same2 = false; }
        Console.WriteLine(same2);
    }
}
