using System;
namespace Cases { public static class GuidCase {
    // e2e oracle for System.Guid: all lines are deterministic booleans (the
    // GUIDs themselves are random -- never print them, only shape/equality).
    private static bool IsHex(char c) {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
    public static void Main() {
        Guid g1 = Guid.NewGuid();
        Guid g2 = Guid.NewGuid();
        string s1 = g1.ToString();
        string s2 = g2.ToString();

        // D-format shape: 36 chars, dashes at 8/13/18/23, hex digits elsewhere.
        Console.WriteLine(s1.Length == 36);
        Console.WriteLine(s1[8] == '-' && s1[13] == '-' && s1[18] == '-' && s1[23] == '-');
        bool hexOk = true;
        for (int i = 0; i < s1.Length; i++) {
            if (i == 8 || i == 13 || i == 18 || i == 23) continue;
            if (!IsHex(s1[i])) hexOk = false;
        }
        Console.WriteLine(hexOk);

        // Two NewGuid() calls virtually never produce the same string.
        Console.WriteLine(s1 != s2);

        // Parse(ToString) round-trip.
        Guid p = Guid.Parse(s1);
        Console.WriteLine(p == g1);
        Console.WriteLine(p.ToString() == s1);

        // Parse rejects malformed input with FormatException (bool output only).
        bool caught;
        caught = false; try { Guid.Parse(""); } catch (FormatException) { caught = true; }
        Console.WriteLine(caught);
        caught = false; try { Guid.Parse("not-a-guid"); } catch (FormatException) { caught = true; }
        Console.WriteLine(caught);
        caught = false; try { Guid.Parse("12345678-1234-1234-1234-12345678901"); } catch (FormatException) { caught = true; }
        Console.WriteLine(caught);
        caught = false; try { Guid.Parse("12345678-1234-1234-1234-1234567890g"); } catch (FormatException) { caught = true; }
        Console.WriteLine(caught);

        // Equals / CompareTo / comparison operators.
        Console.WriteLine(g1.Equals(g1));
        Console.WriteLine(g1.Equals(g2));
        Console.WriteLine(g1.Equals(Guid.Parse(s1)));
        Console.WriteLine(g1.CompareTo(g1) == 0);
        Guid small = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid big = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Console.WriteLine(small.CompareTo(big) < 0);
        Console.WriteLine(big.CompareTo(small) > 0);
        Console.WriteLine(small == big);
    }
}}
