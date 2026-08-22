using System;
public class T {
    public static int A(int x) { return x+1; }
    public static void Main() {
        Func<int,int> f = A;
        int h = f.GetHashCode();
        Console.WriteLine(h);
    }
}
