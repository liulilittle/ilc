//------------------------------------------------------------------------------
// stringsearch.cs -- STRING SEARCH DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C79, AFTER the string search domain
// closure: the 1-arg search icalls (MethodMap.String.cs icall_string_index_of_
// c/s, last_index_of_c/s) + the positional/range overload family
// (MethodMap.StringExt3.cs -> il2cpp_string_ext3.h icall_string_index_of_c_
// from/range, index_of_s_from/range, last_index_of_c_from/range, last_index_
// of_s/from/range) + the char-SET search (icall_string_index_of_any_range /
// last_index_of_any_range).
//
// strdeep (C31) pinned the overload lattice + the exception matrix; THIS case
// drives the SEARCH SEMANTICS one stage deeper on the raw INDEX VALUES and
// the char-SET family:
//
//   1. RAWVALUE index probes: every IndexOf/LastIndexOf line prints the
//      actual int index (not a bool) -- the exact match position is the
//      contract, verified byte-for-byte against net48.
//   2. FIRST-VS-LAST: IndexOf returns the FIRST occurrence, LastIndexOf the
//      LAST, on multi-occurrence strings; symmetric on a shared haystack.
//   3. OVERLAP LATTICE: "aaaa".IndexOf("aa") == 0 while
//      "aaaa".LastIndexOf("aa") == 2 -- the search does NOT consume the
//      match (overlapping next-match). "ababa".IndexOf("aba") == 0 and
//      IndexOf("aba", 2) == 2 (the overlap position). Raw values prove the
//      scan resumes correctly.
//   4. MULTI-OCCURRENCE ENUMERATION: a loop using IndexOf(value, start)
//      walks EVERY occurrence of a needle in a haystack and counts them;
//      the running startIndex is advanced past each hit. The count is the
//      assertion; the walk exercises the from-variant repeatedly.
//   5. POSITIONAL / RANGE forms: IndexOf(char,start[,count]) and
//      IndexOf(string,start[,count]) raw values across the window lattice,
//      including start == Length (-1), count == 0 (-1), count truncating the
//      window mid-match, and a value longer than the window.
//   6. LASTINDEXOF window rules: the char form's startIndex == Length THROWS
//      while the string form's startIndex == Length is LEGAL (the net48
//      quirk the header pins); the string form's match must END at or before
//      startIndex.
//   7. CHAR-SET SEARCH: IndexOfAny / LastIndexOfAny over char[] sets --
//      single/set membership, empty anyOf (never matches), the bounds
//      matrix (startIndex/count), and the LastIndexOfAny empty-string
//      short-circuit (returns -1 BEFORE the bounds checks).
//   8. EMPTY-VALUE LATTICE: IndexOf("") == 0, LastIndexOf("") == Length - 1
//      (0 for the empty string), and the positional empty-value quirks.
//   9. EXCEPTION MATRIX (net48 types): ArgumentNullException for null
//      value/anyOf, ArgumentOutOfRangeException for every out-of-window
//      start/count -- each caught and asserted as a labeled bool.
//
// Every line prints "label=<raw index>" or "label=True/False"; net48 is the
// byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Stringsearch {
    // Count every occurrence of 'value' in 'hay' by advancing startIndex.
    static int CountAll(string hay, string value)
    {
        int n = 0, p = 0;
        while (true)
        {
            int i = hay.IndexOf(value, p);
            if (i < 0) return n;
            n++;
            p = i + 1;
        }
    }

    public static void Main() {
        // ============ 1. raw IndexOf(char) 1/2/3-arg ============
        Console.WriteLine("ic1=" + "abcabc".IndexOf('a'));        // 0
        Console.WriteLine("ic2=" + "abcabc".IndexOf('b'));        // 1
        Console.WriteLine("ic3=" + "abcabc".IndexOf('c'));        // 2
        Console.WriteLine("ic4=" + "abcabc".IndexOf('x'));        // -1
        Console.WriteLine("ic5=" + "a".IndexOf('a'));             // 0
        Console.WriteLine("ic6=" + "".IndexOf('a'));              // -1
        Console.WriteLine("ic7=" + "abcabc".IndexOf('a', 1));     // 3
        Console.WriteLine("ic8=" + "abcabc".IndexOf('c', 3));     // 5
        Console.WriteLine("ic9=" + "abcabc".IndexOf('c', 6));     // -1 (start == Length)
        Console.WriteLine("ic10=" + "abcabc".IndexOf('a', 0));    // 0
        Console.WriteLine("ic11=" + "abcabc".IndexOf('a', 2, 3)); // 3 (window [2,5))
        Console.WriteLine("ic12=" + "abcabc".IndexOf('a', 1, 1)); // -1 (window [1,2) = 'b')
        Console.WriteLine("ic13=" + "abcabc".IndexOf('c', 3, 3)); // 5 (window [3,6))
        Console.WriteLine("ic14=" + "abcabc".IndexOf('a', 3, 0)); // -1 (count == 0)
        Console.WriteLine("ic15=" + "abcabc".IndexOf('c', 4, 1)); // -1 (window [4,5) = 'b')

        // ============ 2. raw IndexOf(string) 1/2/3-arg ============
        Console.WriteLine("is1=" + "abcabc".IndexOf("bc"));       // 1
        Console.WriteLine("is2=" + "abcabc".IndexOf("ab"));       // 0
        Console.WriteLine("is3=" + "abcabc".IndexOf("cab"));      // 2
        Console.WriteLine("is4=" + "abcabc".IndexOf("zzz"));      // -1
        Console.WriteLine("is5=" + "abcabc".IndexOf(""));         // 0 (empty value)
        Console.WriteLine("is6=" + "abcabc".IndexOf("bc", 3));    // 4
        Console.WriteLine("is7=" + "abcabc".IndexOf("bc", 5));    // -1 (needle too long)
        Console.WriteLine("is8=" + "abcabc".IndexOf("bc", 6));    // -1 (start == Length)
        Console.WriteLine("is9=" + "abcabc".IndexOf("ab", 1, 4)); // 3 (window [1,5))
        Console.WriteLine("is10=" + "abcabc".IndexOf("bc", 1, 3));// 1 (window [1,4))
        Console.WriteLine("is11=" + "abcabc".IndexOf("bc", 1, 1));// -1 (window [1,2) too short)
        Console.WriteLine("is12=" + "abcabc".IndexOf("cab", 2, 4));// 2 (window [2,6))
        Console.WriteLine("is13=" + "abcabc".IndexOf("ab", 2, 0));// -1 (count == 0)
        Console.WriteLine("is14=" + "abcabc".IndexOf("ab", 6, 0));// 6 (empty value -> startIndex)

        // ============ 3. overlap lattice (search does not consume the match) ============
        Console.WriteLine("ov1=" + "aaaa".IndexOf("aa"));         // 0
        Console.WriteLine("ov2=" + "aaaa".IndexOf("aa", 1));      // 1 (overlap)
        Console.WriteLine("ov3=" + "aaaa".IndexOf("aa", 2));      // 2 (overlap)
        Console.WriteLine("ov4=" + "aaaa".IndexOf("aa", 3));      // -1 (only 1 char left)
        Console.WriteLine("ov5=" + "ababa".IndexOf("aba"));       // 0
        Console.WriteLine("ov6=" + "ababa".IndexOf("aba", 1));    // -1
        Console.WriteLine("ov7=" + "ababa".IndexOf("aba", 2));    // 2 (overlap)
        Console.WriteLine("ov8=" + "aaaa".LastIndexOf("aa"));     // 2
        Console.WriteLine("ov9=" + "aaaa".LastIndexOf("aa", 3));  // 2
        Console.WriteLine("ov10=" + "ababa".LastIndexOf("aba"));  // 2

        // ============ 4. multi-occurrence enumeration ============
        Console.WriteLine("mo1=" + CountAll("abcabc", "bc"));     // 2
        Console.WriteLine("mo2=" + CountAll("abababa", "aba"));   // 3 (overlapping: 0,2,4)
        Console.WriteLine("mo3=" + CountAll("aaaa", "aa"));       // 3 (overlapping: 0,1,2)
        Console.WriteLine("mo4=" + CountAll("abcabc", "x"));      // 0
        Console.WriteLine("mo5=" + "abc".IndexOf("", 3));        // 3 (empty -> start == Length)
        Console.WriteLine("mo6=" + CountAll("", "x"));            // 0

        // ============ 5. raw LastIndexOf(char) ============
        Console.WriteLine("lc1=" + "abcabc".LastIndexOf('a'));    // 3
        Console.WriteLine("lc2=" + "abcabc".LastIndexOf('c'));    // 5
        Console.WriteLine("lc3=" + "abcabc".LastIndexOf('x'));    // -1
        Console.WriteLine("lc4=" + "a".LastIndexOf('a'));         // 0
        Console.WriteLine("lc5=" + "".LastIndexOf('a'));          // -1
        Console.WriteLine("lc6=" + "abcabc".LastIndexOf('a', 2)); // 0
        Console.WriteLine("lc7=" + "abcabc".LastIndexOf('c', 4)); // 2
        Console.WriteLine("lc8=" + "abcabc".LastIndexOf('c', 5)); // 5
        Console.WriteLine("lc9=" + "abcabc".LastIndexOf('b', 3)); // 1
        Console.WriteLine("lc10=" + "abcabc".LastIndexOf('a', 4, 3)); // 3 (window [2,4])
        Console.WriteLine("lc11=" + "abcabc".LastIndexOf('b', 3, 2)); // 1 (window [2,3])
        Console.WriteLine("lc12=" + "abcabc".LastIndexOf('c', 5, 6)); // 5 (window [0,5])
        Console.WriteLine("lc13=" + "abcabc".LastIndexOf('a', 1, 2)); // 0 (window [0,1])

        // ============ 6. raw LastIndexOf(string) ============
        Console.WriteLine("ls1=" + "abcabc".LastIndexOf("ab"));   // 3
        Console.WriteLine("ls2=" + "abcabc".LastIndexOf("bc"));   // 4
        Console.WriteLine("ls3=" + "abcabc".LastIndexOf("cab"));  // 2
        Console.WriteLine("ls4=" + "abcabc".LastIndexOf("zzz"));  // -1
        Console.WriteLine("ls5=" + "abcabc".LastIndexOf(""));     // 5 (Length - 1)
        Console.WriteLine("ls6=" + "".LastIndexOf(""));           // 0
        Console.WriteLine("ls7=" + "abcabc".LastIndexOf("ab", 4));// 3
        Console.WriteLine("ls8=" + "abcabc".LastIndexOf("bc", 5));// 4
        Console.WriteLine("ls9=" + "abcabc".LastIndexOf("ab", 2));// 0
        Console.WriteLine("ls10=" + "abcabc".LastIndexOf("bc", 6));// 4 (start == Length LEGAL)
        Console.WriteLine("ls11=" + "abcabc".LastIndexOf("ab", 4, 2)); // 3 (window [3,4])
        Console.WriteLine("ls12=" + "abcabc".LastIndexOf("bc", 4, 3)); // 1 (window [2,4])
        Console.WriteLine("ls13=" + "abcabc".LastIndexOf("abc", 5, 3));// 3 (window [3,5])
        Console.WriteLine("ls14=" + "abcabc".LastIndexOf("abc", 2, 3));// 0 (window [0,2])

        // ============ 7. char-set search: IndexOfAny / LastIndexOfAny ============
        char[] vowels = { 'a', 'e', 'i', 'o', 'u' };
        char[] single = { 'x' };
        char[] none = { };
        Console.WriteLine("ia1=" + "hello world".IndexOfAny(vowels, 0, 11));  // 1 ('e')
        Console.WriteLine("ia2=" + "hello world".IndexOfAny(vowels, 2, 9));   // 4 ('o')
        Console.WriteLine("ia3=" + "hello world".IndexOfAny(vowels, 0, 1));   // -1 (window "h")
        Console.WriteLine("ia4=" + "hello world".IndexOfAny(single, 0, 11));  // -1 ('x' absent)
        Console.WriteLine("ia5=" + "hello world".IndexOfAny(none, 0, 11));    // -1 (empty set)
        Console.WriteLine("ia6=" + "xyz".IndexOfAny(vowels, 0, 3));           // -1 (none)
        Console.WriteLine("ia7=" + "aeiou".IndexOfAny(vowels, 0, 5));         // 0
        Console.WriteLine("ia8=" + "aeiou".IndexOfAny(vowels, 3, 2));         // 3 (window [3,5))
        Console.WriteLine("la1=" + "hello world".LastIndexOfAny(vowels, 10, 11)); // 7 ('o')
        Console.WriteLine("la2=" + "hello world".LastIndexOfAny(vowels, 7, 4));   // 7 (window [4,7])
        Console.WriteLine("la3=" + "hello world".LastIndexOfAny(vowels, 6, 7));   // 4 (window [0,6])
        Console.WriteLine("la4=" + "hello world".LastIndexOfAny(single, 10, 11)); // -1 ('x' absent)
        Console.WriteLine("la5=" + "hello world".LastIndexOfAny(none, 10, 11));   // -1 (empty set)
        Console.WriteLine("la6=" + "aeiou".LastIndexOfAny(vowels, 4, 5));    // 4
        Console.WriteLine("la7=" + "".LastIndexOfAny(vowels, 0, 1));         // -1 (empty string short-circuit)
        Console.WriteLine("la8=" + "a".LastIndexOfAny(vowels, 0, 1));        // 0

        // ============ 8. empty-value / positional quirks ============
        Console.WriteLine("eq1=" + "abc".IndexOf("", 2));         // 2 (empty -> startIndex)
        Console.WriteLine("eq2=" + "abc".IndexOf("", 3));         // 3 (start == Length)
        Console.WriteLine("eq3=" + "abc".IndexOf("", 1, 2));      // 1 (empty -> startIndex)
        Console.WriteLine("eq4=" + "abc".LastIndexOf("", 0));     // 0
        Console.WriteLine("eq5=" + "abc".LastIndexOf("", 2));     // 2
        Console.WriteLine("eq6=" + "abc".LastIndexOf("", 3));     // 2 (min(3, Length-1))
        Console.WriteLine("eq7=" + "abc".LastIndexOf("", 2, 3));  // 2
        Console.WriteLine("eq8=" + "".IndexOf('a', 0));           // -1
        Console.WriteLine("eq9=" + "".IndexOf("a", 0));           // -1

        // ============ 9. exception matrix ============
        bool t;
        t = false; try { "abc".IndexOf((string)null); } catch (ArgumentNullException) { t = true; }
        Console.WriteLine("ex1=" + t);                            // True (null value)
        t = false; try { "abc".IndexOf('a', -1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex2=" + t);                            // True
        t = false; try { "abc".IndexOf('a', 4); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex3=" + t);                            // True (start > Length)
        t = false; try { "abc".IndexOf('a', 1, 3); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex4=" + t);                            // True (window overflow)
        t = false; try { "abc".IndexOf("ab", 1, 3); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex5=" + t);                            // True
        t = false; try { "abc".LastIndexOf('a', 3); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex6=" + t);                            // True (char form start == Length)
        t = false; try { "abc".LastIndexOf((string)null); } catch (ArgumentNullException) { t = true; }
        Console.WriteLine("ex7=" + t);                            // True
        t = false; try { "abc".LastIndexOf('a', 0, 2); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex8=" + t);                            // True (count > startIndex+1)
        t = false; try { "abc".IndexOfAny((char[])null, 0, 3); } catch (ArgumentNullException) { t = true; }
        Console.WriteLine("ex9=" + t);                            // True (null anyOf)
        t = false; try { "abc".IndexOfAny(vowels, 4, 1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex10=" + t);                           // True (start > Length)
        t = false; try { "abc".IndexOfAny(vowels, 1, 3); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex11=" + t);                           // True (count > span)
        t = false; try { "abc".LastIndexOfAny(vowels, 3, 1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex12=" + t);                           // True (start >= Length)
        t = false; try { "abc".LastIndexOfAny(vowels, 1, 3); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex13=" + t);                           // True (count-1 > startIndex)
        t = false; try { "abc".LastIndexOfAny(vowels, 2, -1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex14=" + t);                           // True (count < 0)
        t = false; try { "abc".IndexOf('a', 1, -1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("ex15=" + t);                           // True (negative count)

        Console.WriteLine("done");
    }
}}
