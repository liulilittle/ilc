using System;

internal static class ConvIc1
{
    private static void W(string k, string v) { Console.WriteLine("[" + k + "] " + v); }

    private static void IcToInt32(int v)
    {
        try { W("ic_i32", ((IConvertible)v).ToInt32(null).ToString()); }
        catch (Exception e) { W("ic_i32", "ex=" + e.GetType().FullName); }
    }

    private static void Main()
    {
        IcToInt32(42);
    }
}