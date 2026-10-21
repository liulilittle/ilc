using System;

public struct GuidHolder
{
    public Guid G;
    public int N;
}

public struct GuidPair
{
    public Guid A;
    public Guid B;
}

public class GuidBox
{
    public Guid Value;
    public int Tag;
}

public static class P
{
    public static void Main()
    {
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        Guid p = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Guid other = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // (1) Guid as a USER STRUCT field: store, read back, and the
        //     following int field still lands at the net48 offset
        //     (Guid is 16 bytes, align 4 -- the C++ member is Il2CppGuid).
        GuidHolder h;
        h.G = g;
        h.N = 7;
        Console.WriteLine("[g] field=" + h.G.ToString("D"));
        Console.WriteLine("[g] n=" + h.N);
        Guid copied = h.G;
        Console.WriteLine("[g] copied=" + copied.ToString("D"));

        GuidPair gp;
        gp.A = g;
        gp.B = other;
        Console.WriteLine("[g] pair_a=" + gp.A.ToString("D"));
        Console.WriteLine("[g] pair_b=" + gp.B.ToString("D"));
        Console.WriteLine("[g] pair_ne=" + (gp.A != gp.B));

        // (2) Guid as a USER CLASS field (a GC object slot, value copied).
        GuidBox box = new GuidBox();
        box.Value = g;
        box.Tag = 3;
        Console.WriteLine("[g] cls=" + box.Value.ToString("D"));
        Console.WriteLine("[g] cls_tag=" + box.Tag);
        Console.WriteLine("[g] cls_eq=" + (box.Value == g));

        // (3) Guid[] element read/write (16-byte memcpy cells).
        Guid[] arr = new Guid[2];
        arr[0] = g;
        arr[1] = other;
        Console.WriteLine("[g] arr0=" + arr[0].ToString("D"));
        Console.WriteLine("[g] arr1eq=" + (arr[1] == other));
        Console.WriteLine("[g] arrlen=" + arr.Length);
        arr[0] = arr[1];
        Console.WriteLine("[g] arr_swap=" + arr[0].ToString("D"));

        // (4) element comparison by value equality (no delegate involved).
        Console.WriteLine("[g] scan0=" + (arr[0] == other));
        Console.WriteLine("[g] scan1=" + (arr[1] == g));

        // (5) == / != plus Parse round-trip and Guid.Empty.
        Console.WriteLine("[g] eq=" + (g == p));
        Console.WriteLine("[g] ne=" + (g != Guid.Empty));
        Console.WriteLine("[g] ne2=" + (g != p));
        Console.WriteLine("[g] empty=" + (Guid.Empty == default(Guid)));

        // (6) ToString formats + Parse round-trip.
        Console.WriteLine("[g] str=" + g.ToString());
        Console.WriteLine("[g] fmtD=" + g.ToString("D"));
        Console.WriteLine("[g] fmtN=" + g.ToString("N"));
        Console.WriteLine("[g] fmtB=" + g.ToString("B"));
        Console.WriteLine("[g] rt=" + Guid.Parse(g.ToString()).ToString("D"));
    }
}
