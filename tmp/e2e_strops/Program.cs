using System;

namespace E2eStrOps
{
    public static class Program
    {
        private static int failures = 0;

        private static void CheckStr(string name, string actual, string expected)
        {
            if (actual == expected)
            {
                Console.WriteLine("PASS " + name + " -> [" + actual + "]");
            }
            else
            {
                Console.WriteLine("FAIL " + name + " expected=[" + expected + "] actual=[" + actual + "]");
                failures++;
            }
        }

        private static void CheckInt(string name, int actual, int expected)
        {
            if (actual == expected)
            {
                Console.WriteLine("PASS " + name + " -> " + actual);
            }
            else
            {
                Console.WriteLine("FAIL " + name + " expected=" + expected + " actual=" + actual);
                failures++;
            }
        }

        private static void CheckBool(string name, bool actual, bool expected)
        {
            CheckInt(name, actual ? 1 : 0, expected ? 1 : 0);
        }

        private static void CheckChar(string name, char actual, char expected)
        {
            CheckInt(name, actual, expected);
        }

        public static int Main()
        {
            string s1 = "Hello, IL2CPP World!";
            string s2 = "  padded text  ";
            string s3 = "banana";
            string sTab = "\t indented \n";
            string sEmpty = "";

            // Length
            CheckInt("Length.hello", s1.Length, 20);
            CheckInt("Length.empty", sEmpty.Length, 0);

            // get_Chars (indexer)
            CheckChar("Chars.first", s1[0], 'H');
            CheckChar("Chars.mid", s1[7], 'I');
            CheckChar("Chars.last", s1[19], '!');
            CheckChar("Chars.banana", s3[3], 'a');

            // Substring
            CheckStr("Substring.tail", s1.Substring(7), "IL2CPP World!");
            CheckStr("Substring.head", s1.Substring(0, 5), "Hello");
            CheckStr("Substring.mid", s3.Substring(1, 3), "ana");
            CheckStr("Substring.world", s1.Substring(14, 6), "World!");
            CheckStr("Substring.emptySrc", sEmpty.Substring(0), "");
            CheckStr("Substring.atEnd", s1.Substring(20), "");

            // IndexOf
            CheckInt("IndexOf.char", s3.IndexOf('a'), 1);
            CheckInt("IndexOf.str", s3.IndexOf("na"), 2);
            CheckInt("IndexOf.str2", s1.IndexOf("IL2CPP"), 7);
            CheckInt("IndexOf.charMiss", s3.IndexOf('z'), -1);
            CheckInt("IndexOf.strFrom", s3.IndexOf("ana", 2), 3);
            CheckInt("IndexOf.charFrom", s3.IndexOf('a', 4), 5);
            CheckInt("IndexOf.charFrom2", s1.IndexOf('o', 10), 15);
            CheckInt("IndexOf.emptySrc", sEmpty.IndexOf('a'), -1);

            // LastIndexOf
            CheckInt("LastIndexOf.char", s3.LastIndexOf('a'), 5);
            CheckInt("LastIndexOf.str", s3.LastIndexOf("an"), 3);
            CheckInt("LastIndexOf.charMiss", s3.LastIndexOf('z'), -1);
            CheckInt("LastIndexOf.char2", s1.LastIndexOf('o'), 15);
            CheckInt("LastIndexOf.charFrom", s3.LastIndexOf('a', 3), 3);
            CheckInt("LastIndexOf.str2", s1.LastIndexOf("World"), 14);

            // Replace
            CheckStr("Replace.char", s3.Replace('a', 'o'), "bonono");
            CheckStr("Replace.str", s1.Replace("L", "#"), "Hello, I#2CPP World!");
            CheckStr("Replace.multi", s3.Replace("an", "x"), "bxxa");
            CheckStr("Replace.noMatch", s3.Replace("z", "!"), "banana");
            CheckStr("Replace.delete", s2.Replace(" ", ""), "paddedtext");

            // Trim / TrimStart / TrimEnd
            CheckStr("Trim.spaces", s2.Trim(), "padded text");
            CheckStr("Trim.ws", sTab.Trim(), "indented");
            CheckStr("TrimStart.spaces", s2.TrimStart(), "padded text  ");
            CheckStr("TrimEnd.spaces", s2.TrimEnd(), "  padded text");
            CheckStr("Trim.allWs", "   ".Trim(), "");
            CheckStr("Trim.none", s1.Trim(), "Hello, IL2CPP World!");

            // StartsWith / EndsWith
            CheckBool("StartsWith.true", s1.StartsWith("Hello"), true);
            CheckBool("StartsWith.case", s1.StartsWith("hello"), false);
            CheckBool("EndsWith.true", s1.EndsWith("World!"), true);
            CheckBool("EndsWith.case", s1.EndsWith("world!"), false);
            CheckBool("StartsWith.emptyBoth", sEmpty.StartsWith(""), true);
            CheckBool("EndsWith.emptyValue", s3.EndsWith(""), true);
            CheckBool("StartsWith.ban", s3.StartsWith("ban"), true);
            CheckBool("EndsWith.na", s3.EndsWith("na"), true);

            // CompareOrdinal
            CheckInt("CompareOrdinal.eq", string.CompareOrdinal("abc", "abc"), 0);
            CheckInt("CompareOrdinal.lt", string.CompareOrdinal("a", "b"), -1);
            CheckInt("CompareOrdinal.gt", string.CompareOrdinal("b", "a"), 1);
            CheckInt("CompareOrdinal.case", string.CompareOrdinal("ABC", "abc"), -32);
            CheckInt("CompareOrdinal.sameRef", string.CompareOrdinal(s3, "banana"), 0);
            CheckInt("CompareOrdinal.prefixEven", string.CompareOrdinal("abcd", "abcdef"), -2);

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
