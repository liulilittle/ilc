using System;
public static class P {
    static void Main() {
        // 1) void() 
        Action a = new Action(OnVoid); a();
        // 2) int(int)
        Func<int,int> f1 = new Func<int,int>(OnInt); int r1 = f1(7);
        // 3) string(string)
        Func<string,string> f2 = new Func<string,string>(OnStr); string r2 = f2("ab");
        // 4) long(long)
        Func<long,long> f3 = new Func<long,long>(OnLong); long r3 = f3(9L);
        // 5) double(double)
        Func<double,double> f4 = new Func<double,double>(OnDouble); double r4 = f4(1.5);
        // 6) bool(int,int)
        Func<int,int,bool> f5 = new Func<int,int,bool>(OnCmp); bool r5 = f5(3,4);
        // 7) void(object)
        Action<object> a2 = new Action<object>(OnObj); a2("x");
        // 8) int() no args
        Func<int> f6 = new Func<int>(OnZero); int r6 = f6();
        Console.WriteLine("r1=" + r1 + " r2=" + r2 + " r3=" + r3 + " r4=" + r4 + " r5=" + r5 + " r6=" + r6);
    }
    static void OnVoid() { }
    static int OnInt(int x) { return x * 2; }
    static string OnStr(string s) { return s + "!"; }
    static long OnLong(long x) { return x + 1; }
    static double OnDouble(double x) { return x * 2.0; }
    static bool OnCmp(int a, int b) { return a < b; }
    static void OnObj(object o) { }
    static int OnZero() { return 42; }
}
