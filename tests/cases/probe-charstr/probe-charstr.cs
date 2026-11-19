// probe-charstr.cs -- System.Char's (string, int) overloads.
//
// net48 declares a second family of char classification methods that index into
// a string instead of taking a single char: char.IsDigit(s, i) and friends.
// Only the single-char overloads were wired, so every call in this family failed
// to transpile with "no BCL mapping in M5" -- a whole-method loss, not a
// per-call one.
//
// Determinism: fixed literals and booleans only. No wall clock, no addresses,
// no exception text. Every row prints the value net48 returns for that exact
// input, so a wrong index or a missing bound check is visible.
//------------------------------------------------------------------------------
using System;

internal static class Program
{
    private static int Main()
    {
        // A mixed-classification probe string: digit, letter, upper, lower,
        // space, punctuation, symbol, surrogate pair.
        string s = "a1 Z_ .+\uD83D\uDE00";

        Console.WriteLine("len=" + s.Length);

        // IsDigit(s, i): index 1 is '1'.
        Console.WriteLine("isdigit1=" + char.IsDigit(s, 1));
        Console.WriteLine("isdigit0=" + char.IsDigit(s, 0));
        Console.WriteLine("isdigit4=" + char.IsDigit(s, 4));

        // IsLetter / IsUpper / IsLower / IsWhiteSpace / IsPunctuation / IsSymbol.
        Console.WriteLine("isletter0=" + char.IsLetter(s, 0));
        Console.WriteLine("isupper2=" + char.IsUpper(s, 2));
        Console.WriteLine("islower0=" + char.IsLower(s, 0));
        Console.WriteLine("isws5=" + char.IsWhiteSpace(s, 5));
        Console.WriteLine("iswpunct6=" + char.IsPunctuation(s, 6));
        Console.WriteLine("issym7=" + char.IsSymbol(s, 7));

        // IsNumber / IsControl / IsSeparator / IsSurrogate.
        Console.WriteLine("isnum1=" + char.IsNumber(s, 1));
        Console.WriteLine("isctrl0=" + char.IsControl(s, 0));

        // IsSurrogatePair on the trailing emoji (indices 8 and 9).
        Console.WriteLine("surrpair=" + char.IsSurrogatePair(s, 8));
        Console.WriteLine("high8=" + char.IsHighSurrogate(s, 8));
        Console.WriteLine("low9=" + char.IsLowSurrogate(s, 9));
        Console.WriteLine("surr8=" + char.IsSurrogate(s, 8));

        // GetUnicodeCategory(s, i) is an enum: print the numeric value.
        Console.WriteLine("cat1=" + (int)char.GetUnicodeCategory(s, 1));
        Console.WriteLine("cat0=" + (int)char.GetUnicodeCategory(s, 0));

        // GetNumericValue(s, i): digit chars answer their numeric value.
        Console.WriteLine("numval1=" + char.GetNumericValue(s, 1));
        Console.WriteLine("numval0=" + char.GetNumericValue(s, 0));

        // ToString(char) and the (string, int) family must agree.
        Console.WriteLine("tostr0=" + char.ToString(s[0]));
        Console.WriteLine("tostr2=" + char.ToString(s[2]));

        return 0;
    }
}