using System;

static unsafe class Program
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        // new string(char*) via stackalloc
        char* cp = stackalloc char[6];
        cp[0] = 'h'; cp[1] = 'e'; cp[2] = 'l'; cp[3] = 'l'; cp[4] = 'o'; cp[5] = '\0';
        string s1 = new string(cp);
        Check("ctor.char_ptr", s1 == "hello");

        // new string(char*, int start, int length)
        string s2 = new string(cp, 1, 3);
        Check("ctor.char_ptr_range", s2 == "ell");

        // new string(sbyte*) via stackalloc (ASCII == UTF-8 subset)
        sbyte* bp = stackalloc sbyte[6];
        bp[0] = (sbyte)'w'; bp[1] = (sbyte)'o'; bp[2] = (sbyte)'r'; bp[3] = (sbyte)'l'; bp[4] = (sbyte)'d'; bp[5] = 0;
        string s3 = new string(bp);
        Check("ctor.sbyte_ptr", s3 == "world");

        // new string(sbyte*, int start, int length)
        string s4 = new string(bp, 1, 3);
        Check("ctor.sbyte_ptr_range", s4 == "orl");

        Console.WriteLine(failures == 0 ? "ALL-PASS" : "FAILURES=" + failures);
    }
}

