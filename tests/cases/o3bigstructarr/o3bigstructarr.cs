// o3bigstructarr.cs -- O3 probe: user struct arrays whose struct is LARGER
// than one pointer (8 bytes). The transpiler's newarr fallback
// (CodeGenerator.ArrayElementType, audit O3) used to answer 8 bytes/element
// for any unknown user value type, so the array was allocated with an 8-byte
// stride and every element beyond a single field was addressed at the wrong
// byte offset (arr[i] landed at data + i*8 instead of data + i*sizeof(s)).
//
// net48 oracle truth:
//   len=8
//   b20: 101/501 102/502 103/503
//   b48: 11/91   12/92   13/93
// A whoo-struct (20B) and a 12-int struct (48B), each in an 8-element array.
// With the 8-byte fallback the field addresses alias across elements (stride 8
// vs struct 20/48), so element[2].A reads back element[0].E's value, etc. --
// a deterministic diff that is the locked RED. After the fix (stride =
// metadata InstanceSize) the reads are byte-exact.
//------------------------------------------------------------------------------
using System;

public static class Program
{
    public static void Main()
    {
        Big20[] b20 = new Big20[8];
        b20[0].A = 101; b20[1].A = 102; b20[2].A = 103;
        b20[0].E = 501; b20[1].E = 502; b20[2].E = 503;
        Console.WriteLine("len=" + b20.Length);
        Console.Write("b20: ");
        for (int i = 0; i < 3; i++) Console.Write(b20[i].A + "/" + b20[i].E + " ");
        Console.WriteLine();

        Big48[] b48 = new Big48[8];
        b48[0].A = 11; b48[1].A = 12; b48[2].A = 13;
        b48[0].E = 51; b48[1].E = 52; b48[2].E = 53;
        b48[0].L = 91; b48[1].L = 92; b48[2].L = 93;
        Console.Write("b48: ");
        for (int i = 0; i < 3; i++) Console.Write(b48[i].A + "/" + b48[i].E + "/" + b48[i].L + " ");
        Console.WriteLine();
    }
}

// 5 x int32 = 20 bytes (bigger than one pointer, not a multiple of 8).
struct Big20
{
    public int A, B, C, D, E;
}

// 12 x int32 = 48 bytes.
struct Big48
{
    public int A, B, C, D, E, F, G, H, I, J, K, L;
}