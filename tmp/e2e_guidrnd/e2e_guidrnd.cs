using System;

static class Program
{
    static int failures = 0;

    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }

    // Guid.Parse roundtrip: parse the fixed string, render back with
    // ToString(), require exact string equality AND a second parse that is
    // operator-equal to the first Guid.
    static bool RoundTrip(string s)
    {
        Guid g = Guid.Parse(s);
        string back = g.ToString();
        if (back != s) return false;
        Guid re = Guid.Parse(back);
        return re == g && re.ToString() == back;
    }

    static int Main()
    {
        // ---- 1. Guid.Parse roundtrip on fixed GUID strings ----
        Check("guid.roundtrip.canonical", RoundTrip("12345678-1234-1234-1234-123456789abc"));
        Check("guid.roundtrip.zeros", RoundTrip("00000000-0000-0000-0000-000000000000"));
        Check("guid.roundtrip.maxes", RoundTrip("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Check("guid.roundtrip.mixed", RoundTrip("a1b2c3d4-e5f6-4789-a0bc-def012345678"));

        // ---- 2. Two Random(42) instances: identical first 5 Next(1000) ----
        Random ra = new Random(42);
        Random rb = new Random(42);
        bool identical = true;
        bool inRange = true;
        for (int i = 0; i < 5; i++)
        {
            int x = ra.Next(1000);
            int y = rb.Next(1000);
            if (x != y) identical = false;
            if (x < 0 || x > 999 || y < 0 || y > 999) inRange = false;
        }
        Check("random.seed42.identical", identical && inRange);

        // ---- 3. NextBytes fills 32 bytes, at least one byte != 0 ----
        byte[] buf = new byte[32];
        rb.NextBytes(buf);
        bool anyNonzero = false;
        for (int i = 0; i < 32; i++)
        {
            if (buf[i] != 0) anyNonzero = true;
        }
        Check("random.nextbytes.nonzero", anyNonzero);

        if (failures == 0)
        {
            Console.WriteLine("ALL-PASS");
            return 0;
        }
        return 1;
    }
}
