//------------------------------------------------------------------------------
// strcompare.cs -- System.String comparison family (net48 oracle).
//
// Verification case for the stringext3 comparison-mode batch: the
// StringComparison-mode overloads and the value-equality operators:
//
//   * Equals(string, StringComparison) -- Ordinal / OrdinalIgnoreCase /
//     CurrentCulture (culture modes only on culture-proof ASCII inputs)
//   * CompareTo (instance, ordinal) and string.CompareOrdinal (static)
//   * StartsWith / EndsWith (string, StringComparison) -- mode family
//   * Contains
//   * op_Equality / op_Inequality -- VALUE equality (op_Equality fix
//     verification: different instances with equal content), including
//     null == null / null != "x" / "x" != null
//   * case differences -- Ordinal vs OrdinalIgnoreCase only
//   * error paths: invalid StringComparison value -> ArgumentException,
//     null value -> ArgumentNullException
//
// Every line prints a BOOL, so a diff failure shows exactly which
// assertion flipped.
//
// CULTURE NOTE: all case-folding assertions use Ordinal / OrdinalIgnoreCase
// only; the CurrentCulture lines use ASCII strings whose outcome is the same
// in every culture ('i'/'I' is never compared). The runtime implements the
// culture modes (0-3) as their ORDINAL equivalents (documented ASCII-domain
// deviation in il2cpp_string_ext3.h) -- safe for the strings below.
//------------------------------------------------------------------------------

using System;

class Program
{
    static void Main()
    {
        string n = null;                    // null variable (avoids constant folding)

        // --- Equals(string, StringComparison) ---
        Console.WriteLine("abc".Equals("abc", StringComparison.Ordinal));
        Console.WriteLine("abc".Equals("ABC", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".Equals("ABC", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("abc".Equals("abd", StringComparison.OrdinalIgnoreCase) == false);
        Console.WriteLine("abc".Equals("abc", StringComparison.CurrentCulture));
        Console.WriteLine("abc".Equals("ABC", StringComparison.CurrentCulture) == false);
        Console.WriteLine("abc".Equals("ABC", StringComparison.CurrentCultureIgnoreCase));
        Console.WriteLine("".Equals("", StringComparison.Ordinal));
        Console.WriteLine("".Equals("x", StringComparison.Ordinal) == false);
        bool threw = false;
        try { "abc".Equals("x", (StringComparison)99); } catch (ArgumentException) { threw = true; }
        Console.WriteLine(threw);                           // invalid mode throws
        threw = false;
        try { "abc".Equals(null, StringComparison.Ordinal); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);                           // null value throws

        // --- CompareTo (instance, ordinal) ---
        Console.WriteLine("abc".CompareTo("abc") == 0);
        Console.WriteLine("abc".CompareTo("abd") == -1);
        Console.WriteLine("abd".CompareTo("abc") == 1);
        Console.WriteLine("abc".CompareTo("ab") == 1);      // longer prefix sorts after
        Console.WriteLine("a".CompareTo("A") != 0);         // ordinal: 'a'(97) > 'A'(65)
        Console.WriteLine("abc".CompareTo(null) == 1);      // null sorts before any string
        Console.WriteLine("abc".CompareTo("") == 1);
        Console.WriteLine("".CompareTo("") == 0);

        // --- CompareOrdinal (static) ---
        Console.WriteLine(string.CompareOrdinal("abc", "abc") == 0);
        Console.WriteLine(string.CompareOrdinal("abc", "abd") == -1);
        Console.WriteLine(string.CompareOrdinal("abc", "ab") == 1);
        Console.WriteLine(string.CompareOrdinal("A", "a") == -32);
        Console.WriteLine(string.CompareOrdinal(null, "x") == -1);
        Console.WriteLine(string.CompareOrdinal("x", null) == 1);
        Console.WriteLine(string.CompareOrdinal(null, null) == 0);
        Console.WriteLine(string.CompareOrdinal("", "") == 0);

        // --- StartsWith(string, StringComparison) ---
        Console.WriteLine("abc".StartsWith("ab", StringComparison.Ordinal));
        Console.WriteLine("abc".StartsWith("AB", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".StartsWith("AB", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("abc".StartsWith("ab", StringComparison.CurrentCulture));
        Console.WriteLine("abc".StartsWith("AB", StringComparison.CurrentCultureIgnoreCase));
        Console.WriteLine("abc".StartsWith("", StringComparison.Ordinal));
        Console.WriteLine("".StartsWith("", StringComparison.Ordinal));
        Console.WriteLine("abc".StartsWith("abcd", StringComparison.OrdinalIgnoreCase) == false);
        threw = false;
        try { "abc".StartsWith("a", (StringComparison)99); } catch (ArgumentException) { threw = true; }
        Console.WriteLine(threw);
        threw = false;
        try { "abc".StartsWith(null, StringComparison.Ordinal); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- EndsWith(string, StringComparison) ---
        Console.WriteLine("abc".EndsWith("bc", StringComparison.Ordinal));
        Console.WriteLine("abc".EndsWith("BC", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".EndsWith("BC", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("abc".EndsWith("bc", StringComparison.CurrentCulture));
        Console.WriteLine("abc".EndsWith("", StringComparison.Ordinal));
        Console.WriteLine("".EndsWith("", StringComparison.Ordinal));
        threw = false;
        try { "abc".EndsWith("c", (StringComparison)99); } catch (ArgumentException) { threw = true; }
        Console.WriteLine(threw);
        threw = false;
        try { "abc".EndsWith(null, StringComparison.Ordinal); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- Contains ---
        Console.WriteLine("abc".Contains("bc"));
        Console.WriteLine("abc".Contains("d") == false);
        Console.WriteLine("abc".Contains(""));
        Console.WriteLine("".Contains(""));
        threw = false;
        try { "abc".Contains(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- op_Equality / op_Inequality: VALUE equality ---
        // NOTE: the C# compiler CONSTANT-FOLDS literal==literal comparisons,
        // so value equality must be exercised through runtime-known operands
        // (the helper methods below compare a parameter).
        Console.WriteLine("abc" == "abc");                  // folded constant (oracle only)
        Console.WriteLine(("abc" == "ABC") == false);       // folded constant (oracle only)
        Console.WriteLine(("abc" != "abc") == false);       // folded constant (oracle only)
        Console.WriteLine(ValueEq("ab" + "c"));             // op_Equality, equal content
        Console.WriteLine(ValueEq("abc"));                  // op_Equality, equal content
        Console.WriteLine(ValueEq("ABC") == false);         // op_Equality, case-sensitive
        Console.WriteLine(InEq("abc"));                     // op_Equality, receiver-side operand
        Console.WriteLine(ValueNeq("abd"));                 // op_Inequality, different content
        Console.WriteLine(ValueNeq("ABC"));                 // op_Inequality, case difference
        Console.WriteLine(ValueNeq("abc") == false);        // op_Inequality, equal content
        Console.WriteLine("abc".Equals("abc"));             // instance Equals(string) value equality
        Console.WriteLine(("abc".Equals("ABC")) == false);  // instance Equals(string) case-sensitive
        Console.WriteLine(n == null);                       // null == null
        Console.WriteLine(n != "x");                        // null != "x"
        Console.WriteLine("x" != n);                        // "x" != null
        Console.WriteLine((n == "x") == false);             // null == "x"
    }

    // Value-equality helpers: the operand arrives as a parameter, so the
    // compiler cannot fold the comparison -- op_Equality / op_Inequality are
    // genuinely exercised at runtime.
    static bool ValueEq(string x) { return x == "abc"; }
    static bool InEq(string x) { return "abc" == x; }
    static bool ValueNeq(string x) { return x != "abc"; }
}
