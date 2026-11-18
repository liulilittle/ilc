using System;

class Program
{
    static void Main()
    {
        Array a = Array.CreateInstance(typeof(int), 3);
        Console.WriteLine(a.Length);
        Console.WriteLine(a.GetType().Name);
        a.SetValue(42, 1);
        Console.WriteLine(a.GetValue(1));
    }
}
