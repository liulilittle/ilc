using System;
using System.Text;

static class E2eSb {
    static int fails = 0;

    static void Check(string name, bool cond) {
        if (cond) {
            Console.Write("PASS ");
            Console.WriteLine(name);
        } else {
            fails++;
            Console.Write("FAIL ");
            Console.WriteLine(name);
        }
    }

    public static void Main() {
        // R1: Append chain + AppendFormat {0}
        StringBuilder sb = new StringBuilder();
        sb.Append("Hello").Append(',').Append(' ').Append("World");
        Check("append-chain", sb.ToString() == "Hello, World" && sb.Length == 12);
        sb.AppendFormat("[{0}]", 42);
        Check("appendformat-0", sb.ToString() == "Hello, World[42]");
        StringBuilder sb2 = new StringBuilder().Append("a").AppendFormat("<{0}>", "X").Append("b");
        Check("appendformat-chain", sb2.ToString() == "a<X>b");

        // R2: Insert at 0
        StringBuilder ins = new StringBuilder("world");
        ins.Insert(0, "hello ");
        Check("insert-at-0", ins.ToString() == "hello world");
        ins.Insert(0, '>').Insert(0, ">>");
        Check("insert-at-0-chain", ins.ToString() == ">>>hello world");

        // R3: Remove
        StringBuilder rem = new StringBuilder("hello world");
        rem.Remove(5, 6);
        Check("remove-mid", rem.ToString() == "hello");
        rem.Remove(0, 1);
        Check("remove-head", rem.ToString() == "ello");
        rem.Remove(rem.Length - 1, 1);
        Check("remove-tail", rem.ToString() == "ell");

        // R4: Replace char + string forms
        StringBuilder rep = new StringBuilder("banana");
        rep.Replace('a', 'o');
        Check("replace-char", rep.ToString() == "bonono");
        rep.Replace("no", "na");
        Check("replace-string", rep.ToString() == "bonana");
        StringBuilder rep2 = new StringBuilder("aaa");
        rep2.Replace("aa", "b");
        Check("replace-nonoverlap", rep2.ToString() == "ba");

        // R5: Capacity >= Length invariant under growth
        StringBuilder cap = new StringBuilder(4);
        bool capOk = true;
        for (int i = 0; i < 100; i++) {
            cap.Append("xy");
            if (cap.Capacity < cap.Length) { capOk = false; break; }
        }
        Check("capacity-invariant", capOk && cap.Length == 200);

        // R6: ToString snapshot independence
        StringBuilder mut = new StringBuilder("v1");
        string snap1 = mut.ToString();
        mut.Append("-more");
        Check("snapshot-append", snap1 == "v1");
        mut.Insert(0, "I:");
        Check("snapshot-insert", snap1 == "v1");
        mut.Replace('1', '9');
        Check("snapshot-replace", snap1 == "v1" && mut.ToString() == "I:v9-more");
        string snap2 = mut.ToString();
        mut.Remove(0, 2);
        Check("snapshot-remove", snap2 == "I:v9-more" && mut.ToString() == "v9-more");

        if (fails == 0) {
            Console.WriteLine("ALL-PASS");
        } else {
            Console.Write("FAILED count=");
            Console.WriteLine(fails);
        }
    }
}
