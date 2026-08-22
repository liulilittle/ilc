using System;
using System.Threading.Tasks;
namespace Cases { public static class TaskAuditA {
    public static void Main() {
        Console.WriteLine(B().Result);
        Console.WriteLine(B().Result.GetType().Name);
        Console.WriteLine(Sb().Result);
        Console.WriteLine(Sb().Result.GetType().Name);
        Console.WriteLine(S().Result);
        Console.WriteLine(S().Result.GetType().Name);
        Console.WriteLine(Us().Result);
        Console.WriteLine(Us().Result.GetType().Name);
        Console.WriteLine(U().Result);
        Console.WriteLine(U().Result.GetType().Name);
    }
    static async Task<byte> B() {
        byte v = 1;
        v = await Task.FromResult((byte)(v + 2));
        return v;
    }
    static async Task<sbyte> Sb() {
        sbyte v = 1;
        v = await Task.FromResult((sbyte)(v + 2));
        return v;
    }
    static async Task<short> S() {
        short v = 1;
        v = await Task.FromResult((short)(v + 2));
        return v;
    }
    static async Task<ushort> Us() {
        ushort v = 1;
        v = await Task.FromResult((ushort)(v + 2));
        return v;
    }
    static async Task<uint> U() {
        uint v = 1;
        v = await Task.FromResult(v + 2);
        return v;
    }
}}
