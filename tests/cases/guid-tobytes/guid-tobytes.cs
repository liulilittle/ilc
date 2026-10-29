//------------------------------------------------------------------------------
// guid-tobytes.cs -- System.Guid.ToByteArray(): the net48 byte[16] image.
//
// Contract (net48 Guid.cs:1281): a FRESH byte[16] per call, the .NET storage
// layout image (Data1/Data2/Data3 little-endian, Data4 in order -- the same
// layout guid.h documents). Covers: the parsed-Guid image, round-trip
// ctor(bytes) identity, fresh-array-per-call identity semantics, mutation
// isolation (mutating a returned array never corrupts the Guid slot), and
// the NewGuid face (random on both sides -- structural assertions only:
// length, the RFC 4122 v4 version/variant nibbles, round-trip identity).
// Every printed line is deterministic on both channels.
//------------------------------------------------------------------------------
using System;

static class Program
{
    static void Main()
    {
        // ---- parsed Guid: the full 16-byte image, byte for byte ----
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        byte[] b = g.ToByteArray();
        Console.WriteLine("[bytes]=" + BitConverter.ToString(b));
        Console.WriteLine("[len]=" + b.Length);
        Console.WriteLine("[str]=" + g.ToString());

        // ---- round-trip: ctor(bytes) restores the exact value ----
        Guid g2 = new Guid(b);
        Console.WriteLine("[roundtrip]=" + (g2 == g));

        // ---- fresh array per call (net48 allocates a new byte[16] each call) ----
        byte[] b1 = g.ToByteArray();
        byte[] b2 = g.ToByteArray();
        Console.WriteLine("[distinct]=" + !ReferenceEquals(b1, b2));
        Console.WriteLine("[equal]=" + BitConverter.ToString(b1) + "|" + BitConverter.ToString(b2));

        // ---- mutation isolation: the returned array is a copy ----
        b1[0] = 0xFF;
        b1[15] = 0xFF;
        Console.WriteLine("[after-mut]=" + BitConverter.ToString(g.ToByteArray()));

        // ---- component ctor: the LE field image of 0aa79e57-1c2b-4d3e-... ----
        Guid g3 = new Guid(0x0aa79e57, 0x1c2b, 0x4d3e, 0x9f, 0x40, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66);
        Console.WriteLine("[comp]=" + BitConverter.ToString(g3.ToByteArray()));

        // ---- NewGuid face: random on both sides, structural assertions ----
        Guid n = Guid.NewGuid();
        byte[] nb = n.ToByteArray();
        Console.WriteLine("[nb.len]=" + nb.Length);
        Console.WriteLine("[nb.ver]=" + ((nb[7] >> 4) == 4));
        Console.WriteLine("[nb.var]=" + ((nb[8] & 0xC0) == 0x80));
        Console.WriteLine("[nb.rt]=" + (new Guid(nb) == n));
        Console.WriteLine("[nb.strlen]=" + (n.ToString().Length == 36));

        // ---- Guid.Empty: the all-zero image ----
        Console.WriteLine("[empty]=" + BitConverter.ToString(Guid.Empty.ToByteArray()));
    }
}
