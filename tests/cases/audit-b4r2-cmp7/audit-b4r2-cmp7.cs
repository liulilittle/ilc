using System;
using System.Collections.Generic;
using System.Linq;

//------------------------------------------------------------------------------
// audit-b4r2-cmp7 -- D5: narrow TKey + USER IEqualityComparer<T>.
//
// Before the D5 fix the runtime comparer dispatch probed only the reflected
// Equals(int,int) slot, so a user IEqualityComparer<byte> (whose reflected
// slot is Equals(byte,byte) -> "(u1,u1)") threw at RUN time:
//   IEqualityComparer<int>.Equals dispatch: the comparer type has no reflected
//   virtual Equals(int,int) slot.
// The runtime now selects the slot by keyKind. int keys are the control (they
// already worked and must not regress).
//
// net48 oracle:
//   [cmp_int]    key=1 n=3
//   [cmp_byte]   key=44 n=3
//   [cmp_sbyte]  key=-1 n=3
//   [cmp_short]  key=-1 n=3
//   [cmp_ushort] key=65535 n=3
//   [cmp_char]   key=4464 n=3
//   [cmp_bool]   key=True n=2
//   [cmp_uint]   key=4294967295 n=2
//------------------------------------------------------------------------------

internal sealed class IntCmp : IEqualityComparer<int>
{
    public bool Equals(int a, int b) { return a == b; }
    public int GetHashCode(int v) { return v; }
}

internal sealed class ByteCmp : IEqualityComparer<byte>
{
    public bool Equals(byte a, byte b) { return a == b; }
    public int GetHashCode(byte v) { return v; }
}

internal sealed class SByteCmp : IEqualityComparer<sbyte>
{
    public bool Equals(sbyte a, sbyte b) { return a == b; }
    public int GetHashCode(sbyte v) { return v; }
}

internal sealed class ShortCmp : IEqualityComparer<short>
{
    public bool Equals(short a, short b) { return a == b; }
    public int GetHashCode(short v) { return v; }
}

internal sealed class UShortCmp : IEqualityComparer<ushort>
{
    public bool Equals(ushort a, ushort b) { return a == b; }
    public int GetHashCode(ushort v) { return v; }
}

internal sealed class CharCmp : IEqualityComparer<char>
{
    public bool Equals(char a, char b) { return a == b; }
    public int GetHashCode(char v) { return v; }
}

internal sealed class BoolCmp : IEqualityComparer<bool>
{
    public bool Equals(bool a, bool b) { return a == b; }
    public int GetHashCode(bool v) { return v ? 1 : 0; }
}

internal sealed class UIntCmp : IEqualityComparer<uint>
{
    public bool Equals(uint a, uint b) { return a == b; }
    public int GetHashCode(uint v) { return (int)v; }
}

internal static class P
{
    private static void Main()
    {
        // int control: unchanged behaviour.
        foreach (var g in new[] { 1, 1, 1, 0 }.GroupBy(x => x, new IntCmp()))
            Console.WriteLine("[cmp_int] key=" + g.Key + " n=" + g.Count());

        // byte: 300 narrows to 44 -> one bucket.
        foreach (var g in new[] { 44, 300, 44 }.GroupBy(x => (byte)x, new ByteCmp()))
            Console.WriteLine("[cmp_byte] key=" + (int)g.Key + " n=" + g.Count());

        // sbyte: 255 narrows to -1.
        foreach (var g in new[] { -1, 255, -1 }.GroupBy(x => (sbyte)x, new SByteCmp()))
            Console.WriteLine("[cmp_sbyte] key=" + (int)g.Key + " n=" + g.Count());

        // short: 65535 narrows to -1.
        foreach (var g in new[] { -1, 65535, -1 }.GroupBy(x => (short)x, new ShortCmp()))
            Console.WriteLine("[cmp_short] key=" + (int)g.Key + " n=" + g.Count());

        // ushort: -1 narrows to 65535.
        foreach (var g in new[] { 65535, -1, 65535 }.GroupBy(x => (ushort)x, new UShortCmp()))
            Console.WriteLine("[cmp_ushort] key=" + (int)g.Key + " n=" + g.Count());

        // char: 70000 narrows to 4464.
        foreach (var g in new[] { 70000, 4464, 4464 }.GroupBy(x => (char)x, new CharCmp()))
            Console.WriteLine("[cmp_char] key=" + (int)g.Key + " n=" + g.Count());

        // bool: 2/0 -> true.
        foreach (var g in new[] { 2, 5, 0 }.GroupBy(x => x != 0, new BoolCmp()))
            Console.WriteLine("[cmp_bool] key=" + g.Key + " n=" + g.Count());

        // uint: -1 -> uint.MaxValue.
        foreach (var g in new[] { -1, -1, 7 }.GroupBy(x => (uint)x, new UIntCmp()))
            Console.WriteLine("[cmp_uint] key=" + (long)g.Key + " n=" + g.Count());
    }
}
